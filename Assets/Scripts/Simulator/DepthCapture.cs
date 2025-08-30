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

    void Start()
    {
        renderCam = GetComponent<Camera>();
        renderCam.depthTextureMode |= DepthTextureMode.Depth;
        
        CameraParameters cameraParameters = parentCam.GetComponent<CameraSetupManager>().GetCameraParameters();
        renderCam.projectionMatrix = cameraParameters.projectionMatrix;

        var desc = new RenderTextureDescriptor(cameraParameters.width, cameraParameters.height, RenderTextureFormat.ARGB32, 32)
        {
            useMipMap = false,
            autoGenerateMips = false,   
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
        // Set the RawImage's rectTransform to match the depthLinearRT's aspect ratio
        if (debugImage != null && depthLinearRT != null)
        {
            float aspect = (float)depthLinearRT.width / depthLinearRT.height;
            RectTransform rt = debugImage.rectTransform;
            // Set width to current, adjust height to match aspect
            float width = rt.sizeDelta.x;
            float height = width / aspect;
            rt.sizeDelta = new Vector2(width, height);
        }
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
