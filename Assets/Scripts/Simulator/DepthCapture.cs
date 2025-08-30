using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Camera))]
public class DepthCapture : MonoBehaviour
{
    public Camera parentCam;
    public RawImage debugImage;
    public RenderTexture depthLinearRT;   // assign GT or Received RT in inspector
    private Material depthToLinearMat;     // material using your Hidden/DepthToLinear shader
    private Camera renderCam;
    private RenderTexture dummyColor;

    void Awake()
    {
        // TODO: need to reset to the parent cam later
        renderCam = GetComponent<Camera>();
        renderCam.depthTextureMode |= DepthTextureMode.Depth;

        // TODO: need to change later
        //depthLinearRT = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
        var desc = new RenderTextureDescriptor(1024, 1024, RenderTextureFormat.ARGB32, 32)
        {
            useMipMap = false,
            autoGenerateMips = false,   // we'll call GenerateMips() explicitly
            msaaSamples = 1,            // IMPORTANT: no MSAA
            sRGB = false
        };
        depthLinearRT = new RenderTexture(desc);
        depthLinearRT.Create();

        // Dummy color target, camera must render somewhere
        dummyColor = new RenderTexture(depthLinearRT.width, depthLinearRT.height, 0, RenderTextureFormat.ARGB32);
        dummyColor.Create();
        renderCam.targetTexture = dummyColor;

        depthToLinearMat = new Material(Shader.Find("Hidden/DepthToLinear"));
        debugImage.texture = depthLinearRT;
    }

    public RenderTexture GetRenderTexture()
    {
        return depthLinearRT;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (depthToLinearMat && depthLinearRT)
            Graphics.Blit(src, depthLinearRT, depthToLinearMat);
    }

    void OnDestroy()
    {
        if (dummyColor) { dummyColor.Release(); Destroy(dummyColor); }
    }
}
