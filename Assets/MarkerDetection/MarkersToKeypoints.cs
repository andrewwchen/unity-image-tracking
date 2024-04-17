using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using System.IO;
using UnityEngine.XR.ARFoundation;

public class MarkersToKeypoints: MonoBehaviour
{
    #if UNITY_IOS || UNITY_IPHONE || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        protected const int gpuMemoryBlockSizeBlur = 484;
        protected const int maxRadius = 64;
    #elif UNITY_ANDROID
        protected const int gpuMemoryBlockSizeBlur = 64;
        protected const int maxRadius = 32;
    #else
        protected const int gpuMemoryBlockSizeBlur = 1024;
        protected const int maxRadius = 92;
    #endif

    [SerializeField] private XRReferenceImageLibrary markers;
    //[SerializeField] private RenderTexture[,] keypoints;
    //[SerializeField] private RenderTexture[] keypoints2;
    [SerializeField] private ComputeShader rgba2Gray;
    [SerializeField] private ComputeShader blurShader;
    [SerializeField] private ComputeShader normalizeShader;
    [SerializeField] private ComputeShader marker2Keypoints;
    [SerializeField] private ComputeShader keypointsNonMaximalSuppression;
    [SerializeField] private ComputeShader oFastShader;
    [SerializeField] private ComputeShader briefShader;

    [Range(1, 6)]
    [SerializeField] private int numScales = 5;
    [Range(0.0f, 1.0f)]
    [SerializeField] private float cameraFastThreshold = 0.1f;
    [Range(0.0f, 1.0f)]
    [SerializeField] private float markerFastThresholdMin = 0.3f;
    [Range(0.0f, 1.0f)]
    [SerializeField] private float markerFastThresholdIncrement = 0.1f;

    [Range(0, maxRadius)]
    [SerializeField] private int radius = 1;
    [SerializeField] private int oFastRadius = 8;
    [SerializeField] private int briefNumTests = 128;
    [SerializeField] private int briefPatchSize = 31;

    [SerializeField] private bool useSecondaryCamera = false;
    [SerializeField] private Camera secondaryCamera;

    private ComputeBuffer weightsBuffer;
    private int blurHorID;
    private int blurVerID;
    private int[] briefTestsX;
    private int[] briefTestsY;
    private float[] blurWeights;
    private ComputeBuffer briefTestsXBuffer;
    private ComputeBuffer briefTestsYBuffer;


    private RenderTexture horBlurOutput;
    private RenderTexture verBlurOutput;
    private RenderTexture normalized;
    private RenderTexture fps;
    private RenderTexture fps_debug;
    private RenderTexture fps_supp;
    private RenderTexture fps_supp_debug;
    private RenderTexture ofps;
    private RenderTexture ofps_debug;


    private Camera primaryCamera;
    private ARCameraBackground arCameraBackground;
    private RenderTexture grayCameraRT;
    private RenderTexture rgbaCameraRT;
    private int viewWidth;
    private int viewHeight;
    private int viewDepth;
    [SerializeField] private int viewScale = 8;

    float[] OneDimensinalKernel(int radius, float sigma)
    {
        float[] kernelResult = new float[radius * 2 + 1];
        float sum = 0.0f;
        for (int t = 0; t < radius; t++)
        {
            double newBlurWalue = 0.39894 * Mathf.Exp(-0.5f * t * t / (sigma * sigma)) / sigma;
            kernelResult[radius + t] = (float)newBlurWalue;
            kernelResult[radius - t] = (float)newBlurWalue;
            if (t != 0)
                sum += (float)newBlurWalue * 2.0f;
            else
                sum += (float)newBlurWalue;
        }
        // normalize kernels
        for (int k = 0; k < radius * 2 + 1; k++)
        {
            kernelResult[k] /= sum;
        }
        return kernelResult;
    }

    void GenerateBriefTests()
    {
        briefTestsX = new int[briefNumTests];
        briefTestsY = new int[briefNumTests];

        // uniform distribution
        for (int i = 0; i < briefNumTests; i++)
        {
            briefTestsX[i] = Random.Range(-briefPatchSize / 2, briefPatchSize / 2 + 1);
            briefTestsY[i] = Random.Range(-briefPatchSize / 2, briefPatchSize / 2 + 1);
        }
    }

    void SetBlurWeightsBuffer()
    {
        weightsBuffer = new ComputeBuffer(radius * 2 + 1, sizeof(float));
        weightsBuffer.SetData(blurWeights);

        blurHorID = blurShader.FindKernel("HorzBlurCs");
        blurVerID = blurShader.FindKernel("VertBlurCs");

        blurShader.SetBuffer(blurHorID, "gWeights", weightsBuffer);
        blurShader.SetBuffer(blurVerID, "gWeights", weightsBuffer);
        blurShader.SetInt("blurRadius", radius);
    }

    void SetBriefTestsBuffer()
    {
        briefTestsXBuffer = new ComputeBuffer(briefNumTests, sizeof(int));
        briefTestsYBuffer = new ComputeBuffer(briefNumTests, sizeof(int));
        briefTestsXBuffer.SetData(briefTestsX);
        briefTestsYBuffer.SetData(briefTestsY);
        briefShader.SetBuffer(0, "TestsX", briefTestsXBuffer);
        briefShader.SetBuffer(0, "TestsY", briefTestsYBuffer);
        briefShader.SetInt("TestsN", briefNumTests);
    }

    RenderTexture MakeRenderTexture(int width, int height)
    {
        RenderTexture rt = new RenderTexture(width, height, 0);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }
    RenderTexture BlitTex2RT(Texture2D tex, int width, int height)
    {
        RenderTexture rt = MakeRenderTexture(width, height);
        RenderTexture.active = rt;
        Graphics.Blit(tex, rt);
        return rt;
    }

    void RGBA2GrayRT(RenderTexture rgba, RenderTexture gray, int width, int height)
    {
        rgba2Gray.SetTexture(0, "Input", rgba);
        rgba2Gray.SetTexture(0, "Result", gray);
        rgba2Gray.Dispatch(0, width, height, 1);
    }

    Texture2D ReadRT2Tex(RenderTexture rt, int width, int height)
    {
        Texture2D tex = new Texture2D(width, height);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        return tex;
    }

    void NormalizeRT(RenderTexture normalized, RenderTexture gray, int width, int height)
    {
        Texture2D grayTex = ReadRT2Tex(gray, width, height);
        Color32[] texColors = grayTex.GetPixels32();
        int min = 255;
        int max = 0;
        for (int k = 0; k < texColors.Length; k++)
        {
            min = Mathf.Min(texColors[k].r, min);
            max = Mathf.Max(texColors[k].r, max);
        }
        float minf = min / 255.0f;
        float maxf = max / 255.0f;
        float range = maxf - minf;
        normalizeShader.SetTexture(0, "Input", gray);
        normalizeShader.SetTexture(0, "Result", normalized);
        normalizeShader.SetFloat("Min", minf + (0.1f * range));
        normalizeShader.SetFloat("Max", maxf - (0.1f * range));
        normalizeShader.Dispatch(0, width, height, 1);
    }

    void FAST(RenderTexture source, RenderTexture fps, RenderTexture fps_debug, RenderTexture fps_supp, RenderTexture fps_supp_debug, int width, int height, float threshold)
    {
        // KEYPOINTS
        marker2Keypoints.SetTexture(0, "Input", source);
        marker2Keypoints.SetTexture(0, "Debug", fps_debug);
        marker2Keypoints.SetTexture(0, "Result", fps);
        marker2Keypoints.SetFloat("Threshold", threshold);
        marker2Keypoints.SetInt("Width", width);
        marker2Keypoints.SetInt("Height", height);
        marker2Keypoints.Dispatch(0, width, height, 1);

        // KEYPOINTS non maximal suppression
        keypointsNonMaximalSuppression.SetTexture(0, "Source", source);
        keypointsNonMaximalSuppression.SetTexture(0, "Input", fps);
        keypointsNonMaximalSuppression.SetTexture(0, "Result", fps_supp);
        keypointsNonMaximalSuppression.SetTexture(0, "Debug", fps_supp_debug);
        keypointsNonMaximalSuppression.Dispatch(0, width, height, 1);
    }

    void oFAST(RenderTexture source, RenderTexture fps_supp, RenderTexture ofps, RenderTexture ofps_debug, int width, int height)
    {
        oFastShader.SetInt("Radius", oFastRadius);
        oFastShader.SetTexture(0, "Source", source);
        oFastShader.SetTexture(0, "Input", fps_supp);
        oFastShader.SetTexture(0, "Result", ofps);
        oFastShader.SetTexture(0, "Debug", ofps_debug);
        oFastShader.Dispatch(0, width, height, 1);
    }

    void BRIEF(RenderTexture source, RenderTexture ofps, RenderTexture brief, int width, int height)
    {
        briefShader.SetTexture(0, "Source", source);
        briefShader.SetTexture(0, "Input", ofps);
        //briefShader.SetTexture(0, "Debug", ofps_debug);
        briefShader.Dispatch(0, width, height, 1);
    }

    void RT2PNG(RenderTexture rt, string filepath, int width, int height)
    {
        RenderTexture.active = rt;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(filepath, texture.EncodeToPNG());
    }

    void Awake()
    {
        // GenerateOFastMask();

        //keypoints = new RenderTexture[markers.count, numScales];
        //keypoints2 = new RenderTexture[markers.count * numScales];

        blurHorID = blurShader.FindKernel("HorzBlurCs");
        blurVerID = blurShader.FindKernel("VertBlurCs");

        float sigma = radius / 1.5f;
        blurWeights = OneDimensinalKernel(radius, sigma);
        SetBlurWeightsBuffer();

        GenerateBriefTests();
        SetBriefTestsBuffer();

        for (int i = 0; i < markers.count; i++)
        {
            Texture2D marker = markers[i].texture;
            for (int j = 0; j < numScales; j++)
            {
                int scale = 1 << j;
                int width = marker.width / scale;
                int height = marker.height / scale;

                // RGBA
                RenderTexture rgba_temp = BlitTex2RT(marker, width, height);

                // BLUR
                int horizontalBlurDisX = Mathf.CeilToInt((width / (float)gpuMemoryBlockSizeBlur));
                int horizontalBlurDisY = Mathf.CeilToInt((height / (float)gpuMemoryBlockSizeBlur));

                RenderTexture horBlurOutput = MakeRenderTexture(width, height);
                RenderTexture verBlurOutput = MakeRenderTexture(width, height);
                blurShader.SetTexture(blurHorID, "source", rgba_temp);
                blurShader.SetTexture(blurHorID, "horBlurOutput", horBlurOutput);
                blurShader.SetTexture(blurVerID, "horBlurOutput", horBlurOutput);
                blurShader.SetTexture(blurVerID, "verBlurOutput", verBlurOutput);
                blurShader.Dispatch(blurHorID, horizontalBlurDisX, height, 1);
                blurShader.Dispatch(blurVerID, width, horizontalBlurDisY, 1);

                // GRAY
                RenderTexture gray_temp = MakeRenderTexture(width, height);
                RGBA2GrayRT(verBlurOutput, gray_temp, width, height);

                // NORMALIZE
                RenderTexture normalized_temp = MakeRenderTexture(width, height);
                NormalizeRT(normalized_temp, gray_temp, width, height);

                // KEYPOINTS
                RenderTexture fps_temp = MakeRenderTexture(width, height);
                RenderTexture fps_debug_temp = MakeRenderTexture(width, height);
                RenderTexture fps_supp_temp = MakeRenderTexture(width, height);
                RenderTexture fps_supp_debug_temp = MakeRenderTexture(width, height);
                float markerThreshold = markerFastThresholdMin;
                FAST(normalized_temp, fps_temp, fps_debug_temp, fps_supp_temp, fps_supp_debug_temp, width, height, markerThreshold);

                // FAST ORIENTATION
                RenderTexture ofps = MakeRenderTexture(width, height);
                RenderTexture ofps_debug = MakeRenderTexture(width, height);
                oFAST(normalized_temp, fps_supp_temp, ofps, ofps_debug, width, height);

                // BRIEF
                //BRIEF();




                // save actual image to view in inspector
                //keypoints[i, j] = ofps;
                //keypoints2[(i * numScales) + j] = ofps;

                // if on editor, save debug images
                if (!Application.isPlaying)
                {
                    // DEBUG
                    var dirPath = Application.dataPath + "/../Output Images/";
                    if (!Directory.Exists(dirPath))
                    {
                        Directory.CreateDirectory(dirPath);
                    }

                    // Save fast points image To Disk as PNG
                    //RT2PNG(fps_debug_temp, dirPath + markers[i].name + "_" + scale.ToString() + "_fps_debug.png", width, height);
                    //RT2PNG(fps_temp, dirPath + markers[i].name + "_" + scale.ToString() + "_fps.png", width, height);

                    // Save non maximal suppressed fast points image To Disk as PNG
                    //RT2PNG(fps_supp_debug_temp, dirPath + markers[i].name + "_" + scale.ToString() + "_fps_supp_debug.png", width, height);
                    //RT2PNG(fps_supp_temp, dirPath + markers[i].name + "_" + scale.ToString() + "_fps_supp.png", width, height);

                    // Save fast orientation points image To Disk as PNG
                    RT2PNG(ofps_debug, dirPath + markers[i].name + "_" + scale.ToString() + "_ofps_debug.png", width, height);
                    RT2PNG(ofps, dirPath + markers[i].name + "_" + scale.ToString() + "_ofps.png", width, height);

                }
            }
        }
        weightsBuffer.Dispose();
        briefTestsXBuffer.Dispose();
        briefTestsYBuffer.Dispose();
    }

    // Start is called before the first frame update
    void Start()
    {
        primaryCamera = GetComponent<Camera>();
        arCameraBackground = GetComponent<ARCameraBackground>();
        viewWidth = primaryCamera.pixelWidth / viewScale;
        viewHeight = primaryCamera.pixelHeight / viewScale;
        viewDepth = 1;

        if (useSecondaryCamera)
        {
            viewWidth = secondaryCamera.pixelWidth / viewScale;
            viewHeight = secondaryCamera.pixelHeight / viewScale;
        }

        rgbaCameraRT = new RenderTexture(viewWidth, viewHeight, viewDepth);
        rgbaCameraRT.enableRandomWrite = true;
        rgbaCameraRT.Create();

        grayCameraRT = new RenderTexture(viewWidth, viewHeight, viewDepth);
        grayCameraRT.enableRandomWrite = true;
        grayCameraRT.Create();

        if (useSecondaryCamera)
        {
            secondaryCamera.targetTexture = rgbaCameraRT;
        }

        horBlurOutput = MakeRenderTexture(viewWidth, viewHeight);
        verBlurOutput = MakeRenderTexture(viewWidth, viewHeight);
        normalized = MakeRenderTexture(viewWidth, viewHeight);
        fps = MakeRenderTexture(viewWidth, viewHeight);
        fps_debug = MakeRenderTexture(viewWidth, viewHeight);
        fps_supp = MakeRenderTexture(viewWidth, viewHeight);
        fps_supp_debug = MakeRenderTexture(viewWidth, viewHeight);
        ofps = MakeRenderTexture(viewWidth, viewHeight);
        ofps_debug = MakeRenderTexture(viewWidth, viewHeight);
    }

    [SerializeField] private int interval = 5;
    private int nextTime = 0;

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (nextTime <= 0)
        {
            nextTime = interval-1;

            // TEST
            //RGBA2GrayRT(rgbaCameraRT, grayCameraRT, viewWidth, viewHeight);
            ///Graphics.Blit(grayCameraRT, destination);
            //return;

            SetBlurWeightsBuffer();
            SetBriefTestsBuffer();

            // BLUR

            int horizontalBlurDisX = Mathf.CeilToInt(((float)viewWidth / (float)gpuMemoryBlockSizeBlur));
            int horizontalBlurDisY = Mathf.CeilToInt(((float)viewHeight / (float)gpuMemoryBlockSizeBlur));

            blurShader.SetTexture(blurHorID, "source", rgbaCameraRT);
            blurShader.SetTexture(blurHorID, "horBlurOutput", horBlurOutput);
            blurShader.SetTexture(blurVerID, "horBlurOutput", horBlurOutput);
            blurShader.SetTexture(blurVerID, "verBlurOutput", verBlurOutput);
            blurShader.Dispatch(blurHorID, horizontalBlurDisX, viewHeight, 1);
            blurShader.Dispatch(blurVerID, viewWidth, horizontalBlurDisY, 1);

            // GRAY
            RGBA2GrayRT(verBlurOutput, grayCameraRT, viewWidth, viewHeight);

            // NORMALIZE
            NormalizeRT(normalized, grayCameraRT, viewWidth, viewHeight);

            // FAST KEYPOINTS
            FAST(normalized, fps, fps_debug, fps_supp, fps_supp_debug, viewWidth, viewHeight, cameraFastThreshold);

            // FAST ORIENTATION
            oFAST(normalized, fps_supp, ofps, ofps_debug, viewWidth, viewHeight);

            Graphics.Blit(ofps_debug, destination);

            // BRIEF
            //BRIEF();

            weightsBuffer.Dispose();
            briefTestsXBuffer.Dispose();
            briefTestsYBuffer.Dispose();
        } else
        {
            nextTime -= 1;
        }

    }

    // Update is called once per frame
    void Update()
    {
        if (arCameraBackground.material != null)
        {
            Graphics.Blit(null, rgbaCameraRT, arCameraBackground.material);
        }
    }
}
