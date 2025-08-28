using UnityEngine;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

[RequireComponent(typeof(Camera))]
public class CameraSetupManager : MonoBehaviour
{
    [Header("Settings for Depth Error Analysis")]
    public Camera GT_Camera;
    public Camera Received_Camera;
    // public Shader writeDepthShader;
    // private RenderTexture depthRT_GroundTruth;
    // private RenderTexture depthRT_Received;
    public const string layer_GT = "GroundTruth";
    public const string layer_Received = "Received";
    public RawImage debugImage_GT;
    public RawImage debugImage_Received;
    private Material matDebug_GT;
    private Material matDebug_Received;

    private float fx, fy, cx, cy;
    private int width, height;
    private RenderTexture colorRenderTexture;
    private RenderTexture depthRenderTexture;
    private Camera renderCamera;

    [Header("UI Settings")]
    public TMP_Dropdown outputModeDropdown;
    private Material depthMaterial;

    public enum OutputMode
    {
        Depth,
        RGB,
        GPU_Depth
    }

    [HideInInspector]
    public OutputMode currentOutputMode = OutputMode.GPU_Depth;

    void Start()
    {
        // Read intrinsics from file
        string filePath = Path.Combine(Application.dataPath, "Data/quest3_intrinsics.txt");
        if (File.Exists(filePath))
        {
            string content = File.ReadAllText(filePath);
            string[] parts = content.Split(',');
            foreach (string part in parts)
            {
                string[] keyValue = part.Trim().Split('=');
                if (keyValue.Length == 2)
                {
                    string key = keyValue[0].Trim();
                    float value = float.Parse(keyValue[1].Trim());
                    
                    switch (key)
                    {
                        case "fx": fx = value; break;
                        case "fy": fy = value; break;
                        case "cx": cx = value; break;
                        case "cy": cy = value; break;
                        case "width": width = (int)value; break;
                        case "height": height = (int)value; break;
                    }
                }
            }
            Debug.Log($"Loaded intrinsics: fx={fx}, fy={fy}, cx={cx}, cy={cy}, width={width}, height={height}");
        }
        else
        {
            Debug.LogWarning("Intrinsics file not found at: " + filePath);
        }

        renderCamera = GetComponent<Camera>();
        
        // Enable depth texture mode only if in depth mode
        renderCamera.depthTextureMode = currentOutputMode == OutputMode.RGB ? DepthTextureMode.None : DepthTextureMode.Depth;
        
        // Create color render texture
        colorRenderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        colorRenderTexture.Create();

        // Create depth render texture
        depthRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        depthRenderTexture.Create();

        // Optimized: Create depth render texture for ground truth and received
        // int w = width, h = height;
        // var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0) {
        //     sRGB = false,
        //     useMipMap = false,
        //     autoGenerateMips = false,
        //     enableRandomWrite = false
        // };
        // depthRT_GroundTruth = new RenderTexture(desc); 
        // depthRT_GroundTruth.Create();
        // depthRT_Received = new RenderTexture(desc); 
        // depthRT_Received.Create();
        // GT_Camera.targetTexture = depthRT_GroundTruth;
        // Received_Camera.targetTexture = depthRT_Received;

        // GT_Camera.depthTextureMode |= DepthTextureMode.Depth;
        // Received_Camera.depthTextureMode |= DepthTextureMode.Depth;


        // Debug the optimized depth render texture
        // matDebug_GT = new Material(Shader.Find("Hidden/DepthDebugRange"));
        // matDebug_Received = new Material(Shader.Find("Hidden/DepthDebugRange"));
        // matDebug_GT.SetFloat("_Far", GT_Camera.farClipPlane);
        // matDebug_Received.SetFloat("_Far", Received_Camera.farClipPlane);
        // debugImage_GT.texture = depthRT_GroundTruth;
        // debugImage_Received.texture = depthRT_Received;
        // debugImage_GT.material = matDebug_GT;
        // debugImage_Received.material = matDebug_Received;


        // Create depth material
        depthMaterial = new Material(Shader.Find("Hidden/DepthToLinear"));
        Debug.Log($"depthMaterial: {depthMaterial}");

        // !! depth texture mode still first need to render to the screen, so we need to set the target texture to null
        renderCamera.targetTexture = null;
        if (currentOutputMode == OutputMode.Depth)
        {
            renderCamera.depthTextureMode = DepthTextureMode.Depth;
        }
        else if (currentOutputMode == OutputMode.RGB)
        {
            renderCamera.depthTextureMode = DepthTextureMode.None;
        }


        float near = renderCamera.nearClipPlane;
        float far = renderCamera.farClipPlane;

        Debug.Log($"near={near}, far={far}");

        // Convert intrinsics to OpenGL projection matrix
        Matrix4x4 proj = PerspectiveOffCenter(
            -cx * near / fx,
            (width - cx) * near / fx,
            -(height - cy) * near / fy,
            cy * near / fy,
            near,
            far
        );

        renderCamera.projectionMatrix = proj;

        // Initialize output mode dropdown if assigned
        if (outputModeDropdown != null)
        {
            outputModeDropdown.ClearOptions();
            outputModeDropdown.AddOptions(new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData("Depth"),
                new TMP_Dropdown.OptionData("RGB Color"),
                new TMP_Dropdown.OptionData("GPU Depth")
            });
            outputModeDropdown.onValueChanged.AddListener(OnOutputModeChanged);
            
            // Set the dropdown to match the current output mode
            outputModeDropdown.value = (int)currentOutputMode;
        }
    }


    private void Update()
    {
        // if (currentOutputMode != OutputMode.GPU_Depth)
        //     return;
        
        // GT_Camera.RenderWithShader(writeDepthShader, "");
        // Received_Camera.RenderWithShader(writeDepthShader, "");
    }

    private void OnOutputModeChanged(int index)
    {
        currentOutputMode = (OutputMode)index;
        renderCamera.depthTextureMode = currentOutputMode == OutputMode.RGB ? DepthTextureMode.None : DepthTextureMode.Depth;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        // Graphics.Blit(src, depthRT_GroundTruth, depthMaterial);
        // Graphics.Blit(src, dest);

       
        // if (currentOutputMode == OutputMode.Depth)
        // {
        //     RenderTexture.active = depthRenderTexture;
        //     Graphics.Blit(null, depthRenderTexture, depthMaterial); // convert depth to linear
        //     RenderTexture.active = null;
        // }
        // else if (currentOutputMode == OutputMode.RGB)
        // {
        //     RenderTexture.active = colorRenderTexture;
        //     Graphics.Blit(src, colorRenderTexture);
        //     RenderTexture.active = null;
        // }
    }


    Matrix4x4 PerspectiveOffCenter(float left, float right, float bottom, float top, float near, float far)
    {
        float x = (2.0f * near) / (right - left);
        float y = (2.0f * near) / (top - bottom);
        float a = (right + left) / (right - left);
        float b = (top + bottom) / (top - bottom);
        float c = -(far + near) / (far - near);
        float d = -(2.0f * far * near) / (far - near);
        float e = -1.0f;

        Matrix4x4 m = new Matrix4x4();
        m[0, 0] = x;    m[0, 1] = 0;    m[0, 2] = a;    m[0, 3] = 0;
        m[1, 0] = 0;    m[1, 1] = y;    m[1, 2] = b;    m[1, 3] = 0;
        m[2, 0] = 0;    m[2, 1] = 0;    m[2, 2] = c;    m[2, 3] = d;
        m[3, 0] = 0;    m[3, 1] = 0;    m[3, 2] = e;    m[3, 3] = 0;
        return m;
    }

    public byte[] CaptureFrameToBytes()
    {
        // Create a temporary RenderTexture to read from
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture.active = currentOutputMode == OutputMode.RGB ? colorRenderTexture : depthRenderTexture;

        // Create a new Texture2D to read the pixels into
        Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshot.Apply();

        // Convert to PNG bytes
        byte[] bytes = screenshot.EncodeToPNG();

        // Clean up
        Destroy(screenshot);
        RenderTexture.active = currentRT;

        return bytes;
    }

    void OnDestroy()
    {
        if (colorRenderTexture != null)
        {
            colorRenderTexture.Release();
            Destroy(colorRenderTexture);
        }
        if (depthRenderTexture != null)
        {
            depthRenderTexture.Release();
            Destroy(depthRenderTexture);
        }
        if (depthMaterial != null)
        {
            Destroy(depthMaterial);
        }
        if (matDebug_GT != null) Destroy(matDebug_GT);
        if (matDebug_Received != null) Destroy(matDebug_Received);
    }
}
