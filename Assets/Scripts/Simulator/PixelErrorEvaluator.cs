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

    Material pixelErrorMat;
    RenderTexture errorMaskRT;     // binary mask (0/1), with mips
    float pixelErrorPercent;
    List<float> pixelErrorPercentList = new List<float>();
    int smallestMip;

    void Start()
    {
        depthRT_GroundTruth = GT_Camera.GetComponent<DepthCapture>().GetRenderTexture();
        depthRT_Received    = RCV_Camera.GetComponent<DepthCapture>().GetRenderTexture();

        // Make sure sizes match your depth RTs
        int w = depthRT_GroundTruth.width;
        int h = depthRT_GroundTruth.height;

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
        // errorMaskRT = new RenderTexture(w, h, 0, RenderTextureFormat.RHalf) {
        //     useMipMap = true,
        //     autoGenerateMips = true,
        //     filterMode = FilterMode.Bilinear
        // };
        // errorMaskRT.Create();

        smallestMip = errorMaskRT.mipmapCount - 1;

        if (errorMaskImage) errorMaskImage.texture = errorMaskRT;

        // Run AFTER all cameras render
        StartCoroutine(PerFrameEvaluation());
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
            // (autoGenerateMips=true => mips are built automatically here)

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
                    pixelErrorPercentList.Add(pixelErrorPercent);
                }
            });
        }
    }

    void OnDestroy()
    {
        if (errorMaskRT)
        {
            errorMaskRT.Release();
        }

        Debug.Log($"Pixel Error Percent List: {pixelErrorPercentList.Count}");
        foreach (var tex in pixelErrorPercentList)
        {
            if (tex > 0f)
            Debug.Log($"Pixel Error Percent: {tex}");
        }
    }
}
