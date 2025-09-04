using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class PixelErrorEvaluator : MonoBehaviour
{
    public Camera GT_Camera;
    public Camera RCV_Camera;
    public RawImage errorMaskImage;

    RenderTexture depthRT_GroundTruth;
    RenderTexture depthRT_Received;

    private Material pixelErrorMat;
    private RenderTexture errorMaskRT;     // binary mask (0/1), with mips
    private float pixelErrorPercent;
    private int smallestMip;
    private List<(float time, float pixelError)> pixelErrorHistory = new List<(float, float)>();
    private bool isLoggingEnabled = false;
    private float startTime = 0f;
    private bool isGT_SetupFinished = false;
    private bool isRCV_SetupFinished = false;

    void Start()
    {
        GT_Camera.GetComponent<DepthCapture>().OnRenerCameraSetupFinished += OnRenderGT_CameraSetupFinished;
        RCV_Camera.GetComponent<DepthCapture>().OnRenerCameraSetupFinished += OnRenderREV_SetupFinished;
    }

    private void OnRenderGT_CameraSetupFinished()
    {
        isGT_SetupFinished = true;
        StartPerFrameEvaluation();
    }

    private void OnRenderREV_SetupFinished()
    {
        isRCV_SetupFinished = true;
        StartPerFrameEvaluation();
    }

    private void StartPerFrameEvaluation()
    {
        if (isGT_SetupFinished && isRCV_SetupFinished)
        {
            Debug.Log("StartPerFrameEvaluation");

            depthRT_GroundTruth = GT_Camera?.GetComponent<DepthCapture>().GetRenderTexture();
            depthRT_Received = RCV_Camera?.GetComponent<DepthCapture>().GetRenderTexture();

            // Make sure sizes match your depth RTs
            int w = depthRT_GroundTruth.width;
            int h = depthRT_GroundTruth.height;
            Debug.Log($"w={w}, h={h}");

            pixelErrorMat = new Material(Shader.Find("Hidden/PixelErrorMask"));

            // Create the mask RT with mipmaps; let Unity auto-gen mips after Blit
            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.RFloat, 0)
            {
                useMipMap = true,
                autoGenerateMips = false,
                msaaSamples = 1,
                sRGB = false,
            };
            errorMaskRT = new RenderTexture(desc);
            errorMaskRT.filterMode = FilterMode.Bilinear;
            errorMaskRT.Create();

            smallestMip = errorMaskRT.mipmapCount - 1;

            if (errorMaskImage) errorMaskImage.texture = errorMaskRT;
            if (errorMaskImage != null && errorMaskRT != null)
            {
                float aspect = (float)errorMaskRT.width / errorMaskRT.height;
                RectTransform rt = errorMaskImage.rectTransform;
                // Set width to current, adjust height to match aspect
                float width = rt.sizeDelta.x;
                float height = width / aspect;
                rt.sizeDelta = new Vector2(width, height);
            }

            StartCoroutine(PerFrameEvaluation());
        }
    }

    IEnumerator PerFrameEvaluation()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            // Wait until all cameras (including your GT/RCV depth captures)
            // have finished their OnRenderImage for this frame
            yield return wait;

            // Feed shader
            pixelErrorMat.SetTexture("_GT",  depthRT_GroundTruth);
            pixelErrorMat.SetTexture("_RCV", depthRT_Received);

            // 1) Compute binary error mask for this frame
            Graphics.Blit(null, errorMaskRT, pixelErrorMat);

            errorMaskRT.GenerateMips();

            // 2) Read the 1x1 mip asynchronously
            AsyncGPUReadback.Request(errorMaskRT, mipIndex: smallestMip, (req) =>
            {
                if (req.hasError) return;

                var data = req.GetData<float>();
                if (data.Length > 0)
                {
                    float fractionWrong = data[0];   // average of 0/1 mask
                    pixelErrorPercent = fractionWrong * 100f;

                    if (isLoggingEnabled)
                    {
                        float currentTime = Time.time - startTime;
                        pixelErrorHistory.Add((currentTime, pixelErrorPercent));
                    }
                }
            });
        }
    }

    public void StartLogging()
    {
        isLoggingEnabled = true;
        startTime = Time.time;
        pixelErrorHistory.Clear();
    }

    public void StopLogging()
    {
        isLoggingEnabled = false;
    }

    public void ClearHistory()
    {
        pixelErrorHistory.Clear();
    }

    public List<(float time, float pixelError)> GetPixelErrorHistory()
    {
        // deep copy
        return new List<(float, float)>(pixelErrorHistory);
    }

    public float GetCurrentPixelError()
    {
        return pixelErrorPercent;
    }


    void OnDestroy()
    {
        if (errorMaskRT)
            errorMaskRT.Release();
    }
}
