using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

//[RequireComponent(typeof(ARCameraBackground))]
public class BoxFilterTest : MonoBehaviour
{
    [SerializeField] private int boxFilterKernel = 5;
    [SerializeField] private bool useSecondaryCamera = false;
    [SerializeField] private ComputeShader computeShader;
    [SerializeField] private RenderTexture arCameraBackgroundRenderTexture;
    [SerializeField] private RenderTexture boxFilterRenderTexture;
    [SerializeField] private Camera secondaryCamera;
    private Camera primaryCamera;
    private ARCameraBackground arCameraBackground;
    // Start is called before the first frame update
    void Start()
    {
        primaryCamera = GetComponent<Camera>();
        arCameraBackground = GetComponent<ARCameraBackground>();
        arCameraBackgroundRenderTexture = new RenderTexture(primaryCamera.pixelWidth, primaryCamera.pixelHeight, 24);
        arCameraBackgroundRenderTexture.enableRandomWrite = true;
        arCameraBackgroundRenderTexture.Create();

        if (useSecondaryCamera)
        {
            secondaryCamera.targetTexture = arCameraBackgroundRenderTexture;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (arCameraBackground.material != null)
        {
            Graphics.Blit(null, arCameraBackgroundRenderTexture, arCameraBackground.material);
        }
        // Graphics.Blit(secondaryCamera.activeTexture, arCameraBackgroundRenderTexture);
        // arCameraBackgroundRenderTexture = secondaryCamera.activeTexture;
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (boxFilterRenderTexture == null)
        {
            boxFilterRenderTexture = new RenderTexture(arCameraBackgroundRenderTexture.width, arCameraBackgroundRenderTexture.height, arCameraBackgroundRenderTexture.depth);
            boxFilterRenderTexture.enableRandomWrite = true;
            boxFilterRenderTexture.Create();
        }
        computeShader.SetTexture(0, "Input", arCameraBackgroundRenderTexture);
        computeShader.SetTexture(0, "Result", boxFilterRenderTexture);
        computeShader.SetInt("Width", arCameraBackgroundRenderTexture.width);
        computeShader.SetInt("Height", arCameraBackgroundRenderTexture.height);
        computeShader.SetInt("K", boxFilterKernel);
        computeShader.Dispatch(0, boxFilterRenderTexture.width / 8, boxFilterRenderTexture.height / 8, 1);

        Graphics.Blit(boxFilterRenderTexture, destination);
    }

    public void IncrementBoxFilterKernel(int n)
    {
        boxFilterKernel += n;
    }
    public void DecrementBoxFilterKernel(int n)
    {
        boxFilterKernel = Mathf.Max(boxFilterKernel - n, 0);
    }
}
