using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using System.IO;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Events;

using UnityEngine.UI;
using UnityEngine.Assertions;

public class ORB : MonoBehaviour
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
    [SerializeField] private DebugCanvas debugCanvas;
    [SerializeField] private XRReferenceImageLibrary markers;
    [SerializeField] private ComputeShader rgba2Gray;
    [SerializeField] private ComputeShader rotateShader;
    [SerializeField] private ComputeShader blurShader;
    [SerializeField] private ComputeShader normalizeShader;
    [SerializeField] private ComputeShader fastShader;
    [SerializeField] private ComputeShader sobelShader;
    [SerializeField] private ComputeShader harrisShader;
    [SerializeField] private ComputeShader keypointsNonMaximalSuppression;
    [SerializeField] private ComputeShader oFastShader;
    [SerializeField] private ComputeShader briefShader;
    [SerializeField] private ComputeShader featureMatchShader;
    [SerializeField] private ComputeShader debugShader;

[Tooltip("Whether to display debug information in the video feed")]
    [SerializeField] private bool debug = true;

    [Tooltip("Number of frames between each image target scan attempt")]
    [Range(0, 32)]
    [SerializeField] private int scanInterval = 5;

    [Tooltip("Number of valid smaller scales to scan of each image target")]
    [Range(1, 8)]
    [SerializeField] private int numScales = 5;

    [Tooltip("Use 'useRotationTypes' to choose valid image rotations, otherwise use 'rotationAngle'")]
    [SerializeField] private bool useRotationTypes = false;
    [Tooltip("Valid scannable rotations of each image target in degrees")]
    //[SerializeField] private int[] rotations = new int[] { 353, 0, 7, 83, 90, 97, 173, 180, 187, 263, 270, 277 };
    [SerializeField] private int[] rotationTypes = new int[] { 350, 0, 10, 80, 90, 100, 170, 180, 190, 260, 270, 280 };
    // [SerializeField] private int[] rotations = new int[] { 345, 0, 15, 75, 90, 105, 165, 180, 195, 255, 270, 285 };

    [Tooltip("This is the angle at which a target image should be repeated rotated to create the set of valid scannable rotations of the original image")]
    [Range(1, 180)]
    [SerializeField] private int rotationAngle = 5;
    //private int[] rotations = new int[] { 0, 90, 180, 270 };


    private float initialFastThreshold = 0.4f;
    private float minFastThreshold = 0.1f;
    private float fastThresholdIncrement = 0.05f;
    private int maxFastCandidateKeypoints = 1000;
    private int maxFastKeypoints = 500;

    private int viewScale = 1;
    private int radius = 2;
    private int oFastRadius = 8;
    private int briefNumTests = 128;
    private int briefPatchSize = 31;

    [SerializeField] private bool useSecondaryCamera = false;
    [SerializeField] private Camera secondaryCamera;

    private ComputeBuffer weightsBuffer;
    private int blurHorID;
    private int blurVerID;
    private float[] blurWeights;
    private ComputeBuffer fastKeypointsAppendBuffer;
    private ComputeBuffer fastKeypointsCountBuffer;
    private ComputeBuffer fastKeypointsBuffer;
    private int[] briefTestsX1;
    private int[] briefTestsY1;
    private int[] briefTestsX2;
    private int[] briefTestsY2;
    private ComputeBuffer briefTestsX1Buffer;
    private ComputeBuffer briefTestsY1Buffer;
    private ComputeBuffer briefTestsX2Buffer;
    private ComputeBuffer briefTestsY2Buffer;
    private ComputeBuffer matchesCountBuffer;
    private ComputeBuffer matchesAppendBuffer;


    private RenderTexture horBlurOutput;
    private RenderTexture verBlurOutput;
    // private RenderTexture normalized;
    private RenderTexture fps;
    private RenderTexture sobelX2;
    private RenderTexture sobelY2;
    private RenderTexture sobelXY;
    private RenderTexture harris;
    //private RenderTexture ofps;
    //private RenderTexture ofps_debug;
    private RenderTexture debugRT;


    private Camera primaryCamera;
    private ARCameraBackground arCameraBackground;
    private AROcclusionManager arOcclusionManager;
    private RenderTexture grayCameraRT;
    private RenderTexture rgbaCameraRT;
    private int viewWidth;
    private int viewHeight;
    private int viewDepth;

    struct ImageTarget
    {
        public XRReferenceImage xrReferenceImage;
        public Vector2Int[] keypoints;
        public ComputeBuffer features;
        public int numKeypoints;
        public int scale;
        public int rotation; // out of 360
        public ImageTarget(XRReferenceImage xrReferenceImage, Vector2Int[] keypoints, ComputeBuffer features, int numKeypoints, int scale, int rotation)
        {
            this.xrReferenceImage = xrReferenceImage;
            this.keypoints = keypoints;
            this.features = features;
            this.numKeypoints = numKeypoints;
            this.scale = scale;
            this.rotation = rotation;
        }
    }

    struct MatchedTarget
    {
        public ImageTarget imageTarget;
        public int matchNum;
        public int matchTotal;
        public Vector2Int origin;
        public float scale;
        public float depth;

        public MatchedTarget(ImageTarget imageTarget, int matchNum, int matchTotal, Vector2Int origin, float scale, float depth = -1)
        {
            this.imageTarget = imageTarget;
            this.matchNum = matchNum;
            this.matchTotal = matchTotal;
            this.origin = origin;
            this.scale = scale;
            this.depth = depth;
        }

        public override string ToString()
        {
            string name = imageTarget.xrReferenceImage.name;
            int rotation = imageTarget.rotation;
            float rate = (float) matchNum / matchTotal;
            return string.Format("image: {0}\nscale: {1}\norigin: ({6},{7})\nrotation: {2} dgs\nkeypoint matches: {3}/{4} ({5})\ndepth: {8}", name, scale, rotation, matchNum, matchTotal, rate, origin.x, origin.y, depth);
        }

    }

    private HashSet<ImageTarget> imageTargets = new HashSet<ImageTarget>();

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
        briefTestsX1 = new int[briefNumTests];
        briefTestsY1 = new int[briefNumTests];
        briefTestsX2 = new int[briefNumTests];
        briefTestsY2 = new int[briefNumTests];

        // uniform distribution
        for (int i = 0; i < briefNumTests; i++)
        {
            briefTestsX1[i] = Random.Range(-briefPatchSize / 2, briefPatchSize / 2 + 1);
            briefTestsY1[i] = Random.Range(-briefPatchSize / 2, briefPatchSize / 2 + 1);
            briefTestsX2[i] = Random.Range(-briefPatchSize / 2, briefPatchSize / 2 + 1);
            briefTestsY2[i] = Random.Range(-briefPatchSize / 2, briefPatchSize / 2 + 1);
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

    void MakeComputeBuffers()
    {
        fastKeypointsAppendBuffer = new ComputeBuffer(maxFastCandidateKeypoints, sizeof(int) * 2, ComputeBufferType.Append);
        fastKeypointsCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
        fastKeypointsBuffer = new ComputeBuffer(maxFastCandidateKeypoints, sizeof(int) * 2);
        briefTestsX1Buffer = new ComputeBuffer(briefNumTests, sizeof(int));
        briefTestsY1Buffer = new ComputeBuffer(briefNumTests, sizeof(int));
        briefTestsX2Buffer = new ComputeBuffer(briefNumTests, sizeof(int));
        briefTestsY2Buffer = new ComputeBuffer(briefNumTests, sizeof(int));
        // matchesCounter = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Counter);
        matchesCountBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.IndirectArguments);
        matchesAppendBuffer = new ComputeBuffer(maxFastCandidateKeypoints, sizeof(uint), ComputeBufferType.Append);
    }
 
    void DisposeComputeBuffers()
    {
        ComputeBuffer[] buffers = new ComputeBuffer[] {
            weightsBuffer,
            fastKeypointsCountBuffer,
            fastKeypointsAppendBuffer,
            fastKeypointsBuffer,
            briefTestsX1Buffer,
            briefTestsY1Buffer,
            briefTestsX2Buffer,
            briefTestsY2Buffer,
            matchesAppendBuffer,
            //matchesCounter,
            matchesCountBuffer
        };
        foreach (ComputeBuffer buffer in buffers)
        {
            if (buffer != null)
            {
                buffer.Dispose();
            }
        }
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

    (int, Vector2Int[]) FAST(RenderTexture source, RenderTexture fps, RenderTexture sobelX2, RenderTexture sobelY2, RenderTexture sobelXY, RenderTexture harris, int width, int height)
    {
        int fastKeypointsCount = 0;
        float threshold = initialFastThreshold;
        int[] fastKeypointsCountArray = new int[1] { 0 };
        while (fastKeypointsCount == 0 || (fastKeypointsCount < maxFastKeypoints && threshold >= minFastThreshold))
        {
            fastKeypointsAppendBuffer.SetCounterValue(0);
            fastShader.SetBuffer(0, "KeypointsBuffer", fastKeypointsAppendBuffer);
            fastShader.SetTexture(0, "Input", source);
            // fastShader.SetTexture(0, "Debug", fps_debug);
            fastShader.SetTexture(0, "KeypointsTexture", fps);
            fastShader.SetFloat("Threshold", threshold);
            fastShader.SetInt("Width", width);
            fastShader.SetInt("Height", height);
            fastShader.SetInt("Edge", 3);
            fastShader.Dispatch(0, width, height, 1);

            // Copy the count
            ComputeBuffer.CopyCount(fastKeypointsAppendBuffer, fastKeypointsCountBuffer, 0);

            // Retrieve it into array
            fastKeypointsCountBuffer.GetData(fastKeypointsCountArray);

            // Actual count in append buffer
            fastKeypointsCount = fastKeypointsCountArray[0];

            // decrease threshold
            threshold -= fastThresholdIncrement;
        }

        //Debug.LogFormat("Keypoints before suppression {0}", fastKeypointsCount);
        
        // Get the append buffer data.
        Vector2Int[] fastKeypoints = new Vector2Int[Mathf.Min(fastKeypointsCount, maxFastCandidateKeypoints)];
        fastKeypointsAppendBuffer.GetData(fastKeypoints);
        fastKeypointsBuffer.SetData(fastKeypoints);

        // calculate sobel x2, y2, xy
        sobelShader.SetTexture(0, "Input", source);
        sobelShader.SetTexture(0, "X2", sobelX2);
        sobelShader.SetTexture(0, "Y2", sobelY2);
        sobelShader.SetTexture(0, "XY", sobelXY);
        sobelShader.Dispatch(0, width, height, 1);

        // calculate the harris cornerness value for each keypoint
        harrisShader.SetTexture(0, "X2", sobelX2);
        harrisShader.SetTexture(0, "Y2", sobelY2);
        harrisShader.SetTexture(0, "XY", sobelXY);
        harrisShader.SetTexture(0, "Harris", harris);
        harrisShader.SetBuffer(0, "KeypointsBuffer", fastKeypointsBuffer);
        harrisShader.Dispatch(0, fastKeypointsCount, 1, 1);


        // KEYPOINTS non maximal suppression
        fastKeypointsAppendBuffer.SetCounterValue(0);
        keypointsNonMaximalSuppression.SetBuffer(0, "SuppKeypointsAppendBuffer", fastKeypointsAppendBuffer);
        keypointsNonMaximalSuppression.SetBuffer(0, "KeypointsBuffer", fastKeypointsBuffer);
        keypointsNonMaximalSuppression.SetTexture(0, "KeypointsTexture", fps);
        keypointsNonMaximalSuppression.SetTexture(0, "Harris", harris);
        keypointsNonMaximalSuppression.Dispatch(0, fastKeypointsCount, 1, 1);

        // Copy the count
        ComputeBuffer.CopyCount(fastKeypointsAppendBuffer, fastKeypointsCountBuffer, 0);

        // Retrieve it into array
        fastKeypointsCountBuffer.GetData(fastKeypointsCountArray);

        // Actual count in append buffer
        int fastSuppKeypointsCount = fastKeypointsCountArray[0];

        // Get the append buffer data.
        fastKeypointsAppendBuffer.GetData(fastKeypoints);
        fastKeypointsBuffer.SetData(fastKeypoints);
        
        // Debug.LogFormat("Keypoints before suppression {0}, Keypoints after suppresssion {1}", fastKeypointsCount, fastSuppKeypointsCount);
        return (fastSuppKeypointsCount, fastKeypoints);
    }

    void oFAST(int numKeypoints, RenderTexture source, RenderTexture fps, RenderTexture ofps, RenderTexture ofps_debug, int width, int height)
    {
        oFastShader.SetTexture(0, "Source", source);
        oFastShader.SetInt("Radius", oFastRadius);
        oFastShader.SetTexture(0, "KeypointsTexture", fps);
        oFastShader.SetBuffer(0, "KeypointsBuffer", fastKeypointsBuffer);
        oFastShader.SetTexture(0, "Result", ofps);
        oFastShader.SetTexture(0, "Debug", ofps_debug);
        oFastShader.Dispatch(0, width, height, 1);
    }

    ComputeBuffer BRIEF(RenderTexture source, int numKeypoints)
    {
        ComputeBuffer briefTestsOutBuffer = new ComputeBuffer(numKeypoints * briefNumTests / 32, sizeof(uint));
        uint[] briefResult = new uint[briefNumTests / 32 * numKeypoints];
        briefTestsOutBuffer.SetData(briefResult);
        briefTestsX1Buffer.SetData(briefTestsX1);
        briefTestsY1Buffer.SetData(briefTestsY1);
        briefTestsX2Buffer.SetData(briefTestsX2);
        briefTestsY2Buffer.SetData(briefTestsY2);
        briefShader.SetBuffer(0, "TestsX1Buffer", briefTestsX1Buffer);
        briefShader.SetBuffer(0, "TestsY1Buffer", briefTestsY1Buffer);
        briefShader.SetBuffer(0, "TestsX2Buffer", briefTestsX2Buffer);
        briefShader.SetBuffer(0, "TestsY2Buffer", briefTestsY2Buffer);
        briefShader.SetBuffer(0, "KeypointsBuffer", fastKeypointsBuffer);
        briefShader.SetBuffer(0, "TestsOutBuffer", briefTestsOutBuffer);
        briefShader.SetTexture(0, "Source", source);
        oFastShader.SetInt("TestsN", briefNumTests);
        //briefShader.SetBuffer(0, "Input", ofps);
        //Debug.Log(numKeypoints * 4*4);
        briefShader.Dispatch(0, numKeypoints, briefNumTests/32, 1);
        briefTestsOutBuffer.GetData(briefResult);
        return briefTestsOutBuffer;
        //int lastGroup = briefNumTests / 32 * (numKeypoints - 1);
        //Debug.LogFormat("kps: {0}, last keypoint test results {1} {2} {3} {4}", numKeypoints, briefResult[lastGroup + 0], briefResult[lastGroup + 1], briefResult[lastGroup + 2], briefResult[lastGroup + 3]);
    }

    MatchedTarget? FeatureMatch(ComputeBuffer features, int numKeypoints, Vector2Int[] keypoints)
    {
        float bestMatchRate = -1;
        int bestMatchNum = -1;
        int bestMatchTotal = -1;
        uint[] bestMatchesArr = new uint[0];
        uint[] bestMatchesAppendArr = new uint[0];

        ImageTarget bestMatch = new ImageTarget();
        foreach (ImageTarget imageTarget in imageTargets)
        {
            // matchesCounter.SetCounterValue(0);
            matchesAppendBuffer.SetCounterValue(0);
            ComputeBuffer targetFeatures = imageTarget.features;
            int targetNumKeypoints = imageTarget.numKeypoints;
            int[] matchesCountArray = new int[1] { 0 };
            ComputeBuffer matches = new ComputeBuffer(numKeypoints * targetNumKeypoints, sizeof(uint));
            ComputeBuffer bestMatches = new ComputeBuffer(targetNumKeypoints, sizeof(uint));

            featureMatchShader.SetInt("TestsN", briefNumTests);
            featureMatchShader.SetInt("Target2NumFeatures", numKeypoints);
            featureMatchShader.SetBuffer(0, "Matches", matches);
            featureMatchShader.SetBuffer(0, "Target1Features", targetFeatures);
            featureMatchShader.SetBuffer(0, "Target2Features", features);
            featureMatchShader.Dispatch(0, targetNumKeypoints, numKeypoints, 1);

            featureMatchShader.SetBuffer(1, "Matches", matches);
            featureMatchShader.SetBuffer(1, "BestMatches", bestMatches);
            featureMatchShader.SetBuffer(1, "BestMatchesAppend", matchesAppendBuffer);
            featureMatchShader.Dispatch(1, targetNumKeypoints, 1, 1);

            // Copy the count
            ComputeBuffer.CopyCount(matchesAppendBuffer, matchesCountBuffer, 0);

            // Retrieve it into array
            matchesCountBuffer.GetData(matchesCountArray);

            // Actual count
            int matchesCount = matchesCountArray[0];
            float matchRate = (float) matchesCount / targetNumKeypoints;


            //string name2 = imageTarget.xrReferenceImage.name;
            //int scale2 = imageTarget.scale;
            //Debug.LogFormat("{0}, scale={1}, matches={2}/{3}, rate={4}", name2, scale2, matchesCount, targetNumKeypoints, matchRate);

            if (matchRate > bestMatchRate)
            {
                bestMatchRate = matchRate;
                bestMatchNum = matchesCount;
                bestMatchTotal = targetNumKeypoints;
                bestMatch = imageTarget;

                // retrieve data into array
                bestMatchesArr = new uint[targetNumKeypoints];
                bestMatchesAppendArr = new uint[matchesCount];
                bestMatches.GetData(bestMatchesArr);
                matchesAppendBuffer.GetData(bestMatchesAppendArr);
            }

            matches.Dispose();
            bestMatches.Dispose();
        }


        if (bestMatchNum > 3 && bestMatchRate > 0.1)
        {
            Vector2Int origin = Vector2Int.zero;
            Vector2Int[] targetKeypoints = bestMatch.keypoints;
            foreach (uint target1Num in bestMatchesAppendArr)
            {
                uint target2Num = bestMatchesArr[target1Num];
                Vector2Int target1Coords = targetKeypoints[target1Num];
                Vector2Int target2Coords = keypoints[target2Num];
                origin += target2Coords - target1Coords;
            }

            origin /= bestMatchNum;

            float scale = 0;

            foreach (uint target1Num in bestMatchesAppendArr)
            {
                uint target2Num = bestMatchesArr[target1Num];
                Vector2Int target1Coords = targetKeypoints[target1Num];
                Vector2Int target2Coords = keypoints[target2Num];
                Vector2Int target1Offset = target1Coords;
                Vector2Int target2Offset = target2Coords - origin;
                scale += (((float)target1Offset.x / (float)target2Offset.x) + ((float)target1Offset.y / (float)target2Offset.y)) / 2f;
            }
            scale /= bestMatchNum;


            return new MatchedTarget(bestMatch, bestMatchNum, bestMatchTotal, origin, scale);
        }

        return null;
    }


    void DebugMatchedTarget(RenderTexture source, RenderTexture debug, MatchedTarget target)
    {
        // Debug.Log(target);

        debugShader.SetTexture(0, "Source", source);
        //debugShader.SetTexture(0, "Keypoints", keypoints);
        debugShader.SetTexture(0, "Debug", debug);
        debugShader.SetInt("OriginX", target.origin.x);
        debugShader.SetInt("OriginY", target.origin.y);
        debugShader.SetInt("OriginR", 5);
        float width = target.scale * target.imageTarget.xrReferenceImage.texture.width / (float) target.imageTarget.scale;
        float height = target.scale * target.imageTarget.xrReferenceImage.texture.height / (float)target.imageTarget.scale;

        // Debug.LogFormat("w {0} h {1} {2} {3}", width, height, target.imageTarget.xrReferenceImage.texture.width, target.imageTarget.scale);
        debugShader.SetInt("Width", (int) width);
        debugShader.SetInt("Height", (int) height);
        debugShader.SetInt("Stroke", 5);
        debugShader.Dispatch(0, viewWidth, viewHeight, 1);
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
        blurHorID = blurShader.FindKernel("HorzBlurCs");
        blurVerID = blurShader.FindKernel("VertBlurCs");

        float sigma = radius / 1.5f;
        blurWeights = OneDimensinalKernel(radius, sigma);
        SetBlurWeightsBuffer();
        MakeComputeBuffers();

        GenerateBriefTests();
        

        for (int i = 0; i < markers.count; i++)
        {
            XRReferenceImage xrReferenceImage = markers[i];
            Texture2D texture = xrReferenceImage.texture;
            for (int j = 0; j < numScales; j++)
            {
                int scale = 1 << j;
                int width = texture.width / scale;
                int height = texture.height / scale;
                int[] rotations;
                if (useRotationTypes)
                {
                    rotations = rotationTypes;
                }
                else
                {
                    rotations = new int[360 / rotationAngle];
                    for (int currentRotationIndex = 0; currentRotationIndex < 360 / rotationAngle; currentRotationIndex++)
                    {
                        int currentRotation = currentRotationIndex * rotationAngle;
                        rotations[currentRotationIndex] = currentRotation;
                    }
                }
                foreach (int rotation in rotations)
                {
                    // RGBA
                    RenderTexture rgba_temp = BlitTex2RT(texture, width, height);

                    // rotate texture
                    if (rotation != 0)
                    {
                        RenderTexture rotation_output = MakeRenderTexture(width, height);

                        rotateShader.SetTexture(blurHorID, "Input", rgba_temp);
                        rotateShader.SetTexture(blurHorID, "Output", rotation_output);
                        rotateShader.SetInt("Angle", rotation);
                        rotateShader.SetInt("Width", width);
                        rotateShader.SetInt("Height", height);
                        rotateShader.Dispatch(0, width, height, 1);

                        rgba_temp = rotation_output;
                    }

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
                    //RGBA2GrayRT(rgba_temp, gray_temp, width, height);
                    RGBA2GrayRT(verBlurOutput, gray_temp, width, height);

                    // NORMALIZE
                    //RenderTexture normalized_temp = MakeRenderTexture(width, height);
                    //NormalizeRT(normalized_temp, gray_temp, width, height);

                    // KEYPOINTS
                    RenderTexture fps_temp = MakeRenderTexture(width, height);
                    RenderTexture sobelX2_temp = MakeRenderTexture(width, height);
                    RenderTexture sobelY2_temp = MakeRenderTexture(width, height);
                    RenderTexture sobelXY_temp = MakeRenderTexture(width, height);
                    RenderTexture harris_temp = MakeRenderTexture(width, height);
                    (int numKeypoints, Vector2Int[] keypoints) = FAST(gray_temp, fps_temp, sobelX2_temp, sobelY2_temp, sobelXY_temp, harris_temp, width, height);

                    // FAST ORIENTATION
                    //RenderTexture ofps = MakeRenderTexture(width, height);
                    //RenderTexture ofps_debug = MakeRenderTexture(width, height);
                    //oFAST(numKeypoints, gray_temp, fps_temp, ofps, ofps_debug, width, height);

                    // BRIEF
                    ComputeBuffer features = BRIEF(gray_temp, numKeypoints);
                    ImageTarget imageTarget = new ImageTarget(xrReferenceImage, keypoints, features, numKeypoints, scale, rotation);
                    imageTargets.Add(imageTarget);
                }
            }
        }
        DisposeComputeBuffers();
    }



    // Start is called before the first frame update
    void Start()
    {
        primaryCamera = GetComponent<Camera>();
        arCameraBackground = GetComponent<ARCameraBackground>();
        arOcclusionManager = GetComponent<AROcclusionManager>();

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
        // normalized = MakeRenderTexture(viewWidth, viewHeight);
        fps = MakeRenderTexture(viewWidth, viewHeight);
        sobelX2 = MakeRenderTexture(viewWidth, viewHeight);
        sobelY2 = MakeRenderTexture(viewWidth, viewHeight);
        sobelXY = MakeRenderTexture(viewWidth, viewHeight);
        harris = MakeRenderTexture(viewWidth, viewHeight);

        debugRT = new RenderTexture(viewWidth, viewHeight, viewDepth);
        debugRT.enableRandomWrite = true;
        debugRT.Create();
        //ofps = MakeRenderTexture(viewWidth, viewHeight);
        //ofps_debug = MakeRenderTexture(viewWidth, viewHeight);
    }

    private int nextTime = 0;
    private int timeSinceScan = 0;

    private MatchedTarget? lastTarget;

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        timeSinceScan += 1;
        if (nextTime <= 0)
        {
            nextTime = scanInterval;

            SetBlurWeightsBuffer();
            MakeComputeBuffers();

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
            //RGBA2GrayRT(rgbaCameraRT, grayCameraRT, viewWidth, viewHeight);

            // NORMALIZE
            // NormalizeRT(normalized, grayCameraRT, viewWidth, viewHeight);

            // FAST KEYPOINTS
            (int numKeypoints, Vector2Int[] keypoints) = FAST(grayCameraRT, fps, sobelX2, sobelY2, sobelXY, harris, viewWidth, viewHeight);

            // FAST ORIENTATION
            //oFAST(numKeypoints, grayCameraRT, fps, ofps, ofps_debug, viewWidth, viewHeight);


            // BRIEF
            ComputeBuffer features = BRIEF(grayCameraRT, numKeypoints);

            // Feature Matching
            MatchedTarget? maybeTarget = FeatureMatch(features, numKeypoints, keypoints);

            if (maybeTarget is MatchedTarget newMatchedTarget)
            {
                lastTarget = maybeTarget;
                timeSinceScan = 0;

                // calculate depth here
                if (arOcclusionManager.TryAcquireEnvironmentDepthCpuImage(out var cpuImage) && cpuImage.valid)
                {
                    using (cpuImage)
                    {
                        Texture2D depthTexture = null;
                        if (depthTexture == null || depthTexture.width != cpuImage.width || depthTexture.height != cpuImage.height)
                        {
                            depthTexture = new Texture2D(cpuImage.width, cpuImage.height, cpuImage.format.AsTextureFormat(), false);
                        }

                        var conversionParams = new XRCpuImage.ConversionParams(cpuImage, cpuImage.format.AsTextureFormat());
                        var rawTextureData = depthTexture.GetRawTextureData<byte>();

                        Debug.Assert(rawTextureData.Length == cpuImage.GetConvertedDataSize(conversionParams.outputDimensions, conversionParams.outputFormat),
                            "The Texture2D is not the same size as the converted data.");

                        cpuImage.Convert(conversionParams, rawTextureData);
                        depthTexture.Apply();
                        int targetCenterX = newMatchedTarget.origin.x + (int)(newMatchedTarget.scale / 2f);
                        int targetCenterY = newMatchedTarget.origin.y + (int)(newMatchedTarget.scale / 2f);
                        float targetCenterXRatio = (float)targetCenterX / (float)viewWidth;
                        float targetCenterYRatio = (float)targetCenterY / (float)viewHeight;
                        int targetCenterXInDepthImage = (int)(targetCenterXRatio * (float)depthTexture.width);
                        int targetCenterYInDepthImage = (int)(targetCenterYRatio * (float)depthTexture.height);
                        Color depthColor = depthTexture.GetPixel(targetCenterXInDepthImage, targetCenterYInDepthImage);
                        newMatchedTarget.depth = depthColor.r;
                        //Debug.LogFormat("{0} {1} {2} {3}\n", depthColor.r, depthColor.g, depthColor.b, depthColor.a);
                    }
                }
            }

            DisposeComputeBuffers();

        } else
        {
            nextTime -= 1;
        }

        // Display results
        if (lastTarget is MatchedTarget matchedTarget && timeSinceScan < 20)
        {
            if (debug)
            {
                DebugMatchedTarget(rgbaCameraRT, debugRT, matchedTarget);
                Graphics.Blit(debugRT, destination);
            }
            if (debugCanvas != null)
            {

                debugCanvas.image.enabled = true;
                debugCanvas.image.texture = matchedTarget.imageTarget.xrReferenceImage.texture;
                debugCanvas.text.enabled = true;
                debugCanvas.text.text = matchedTarget.ToString();
            }

        }
        else
        {
            Graphics.Blit(rgbaCameraRT, destination);

            if (debugCanvas != null)
            {
                debugCanvas.image.enabled = false;
                debugCanvas.text.enabled = true;
                debugCanvas.text.text = "no matches";
            }
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