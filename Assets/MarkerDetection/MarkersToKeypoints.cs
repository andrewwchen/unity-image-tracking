using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

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
    [SerializeField] private RenderTexture[,] keypoints;
    [SerializeField] private RenderTexture[] keypoints2;
    [SerializeField] private ComputeShader rgba2Gray;
    [SerializeField] private ComputeShader blurShader;
    [SerializeField] private ComputeShader normalizeShader;
    [SerializeField] private ComputeShader marker2Keypoints;

    [Range(1, 6)]
    [SerializeField] private int numScales = 5;

    [Range(0.0f, 1.0f)]
    [SerializeField] private float theshold = 0.3f;

    [Range(0.0f, maxRadius)]
    [SerializeField] private float radius = 1;

    private ComputeBuffer weightsBuffer;

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

    void Awake()
    {
        keypoints = new RenderTexture[markers.count, numScales];
        keypoints2 = new RenderTexture[markers.count * numScales];


        float sigma = ((int)radius) / 1.5f;

        weightsBuffer = new ComputeBuffer((int)radius * 2 + 1, sizeof(float));
        float[] blurWeights = OneDimensinalKernel((int)radius, sigma);
        weightsBuffer.SetData(blurWeights);

        int blurHorID = blurShader.FindKernel("HorzBlurCs");
        int blurVerID = blurShader.FindKernel("VertBlurCs");
        int normMaxID = blurShader.FindKernel("HorzBlurCs");
        int normID = blurShader.FindKernel("VertBlurCs");

        blurShader.SetBuffer(blurHorID, "gWeights", weightsBuffer);
        blurShader.SetBuffer(blurVerID, "gWeights", weightsBuffer);
        blurShader.SetInt("blurRadius", (int)radius);

        for (int i = 0; i < markers.count; i++)
        {
            Texture2D marker = markers[i].texture;
            for (int j = 0; j < numScales; j++)
            {
                int scale = 1 << j;
                int width = marker.width / scale;
                int height = marker.height / scale;

                // RGBA
                RenderTexture rgba = new RenderTexture(width, height, 0);
                rgba.enableRandomWrite = true;
                rgba.Create();
                RenderTexture.active = rgba;
                Graphics.Blit(marker, rgba);

                // BLUR
                int horizontalBlurDisX = Mathf.CeilToInt(((float)width / (float)gpuMemoryBlockSizeBlur));
                int horizontalBlurDisY = Mathf.CeilToInt(((float)height / (float)gpuMemoryBlockSizeBlur));

                RenderTexture horBlurOutput = new RenderTexture(width, height, 0);
                horBlurOutput.enableRandomWrite = true;
                horBlurOutput.Create();
                RenderTexture verBlurOutput = new RenderTexture(width, height, 0);
                verBlurOutput.enableRandomWrite = true;
                verBlurOutput.Create();
                blurShader.SetTexture(blurHorID, "source", rgba);
                blurShader.SetTexture(blurHorID, "horBlurOutput", horBlurOutput);
                blurShader.SetTexture(blurVerID, "horBlurOutput", horBlurOutput);
                blurShader.SetTexture(blurVerID, "verBlurOutput", verBlurOutput);
                blurShader.Dispatch(blurHorID, horizontalBlurDisX, height, 1);
                blurShader.Dispatch(blurVerID, width, horizontalBlurDisY, 1);

                // GRAY
                RenderTexture gray = new RenderTexture(width, height, 0);
                gray.enableRandomWrite = true;
                gray.Create();
                rgba2Gray.SetTexture(0, "Input", verBlurOutput);
                rgba2Gray.SetTexture(0, "Result", gray);
                rgba2Gray.Dispatch(0, width, height, 1);

                // NORMALIZE
                Texture2D grayTex = new Texture2D(width, height);
                RenderTexture.active = gray;
                grayTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                grayTex.Apply();
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
                RenderTexture norm = new RenderTexture(width, height, 0);
                norm.enableRandomWrite = true;
                norm.Create();
                normalizeShader.SetTexture(0, "Input", gray);
                normalizeShader.SetTexture(0, "Result", norm);
                normalizeShader.SetFloat("Min", minf);
                normalizeShader.SetFloat("Max", maxf);
                normalizeShader.Dispatch(0, width, height, 1);

                // KEYPOINTS
                RenderTexture kps = new RenderTexture(width, height, 0);
                kps.enableRandomWrite = true;
                kps.Create();
                marker2Keypoints.SetTexture(0, "Input", norm);
                marker2Keypoints.SetTexture(0, "Result", kps);
                marker2Keypoints.SetFloat("Threshold", theshold);
                marker2Keypoints.SetInt("Width", width);
                marker2Keypoints.SetInt("Height", height);
                marker2Keypoints.Dispatch(0, width, height, 1);

                keypoints[i, j] = kps;
                keypoints2[(i * numScales) + j] = kps;
            }
        }
        weightsBuffer.Dispose();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
