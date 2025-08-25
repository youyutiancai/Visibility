using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using System.IO;
public class QuestCameraLogger : MonoBehaviour
{
    public InputActionReference logActionRef;


    void OnEnable()
    {
        logActionRef.action.performed += OnLogPressed;
        logActionRef.action.Enable();
    }

    void OnDisable()
    {
        logActionRef.action.performed -= OnLogPressed;
        logActionRef.action.Disable();
    }

    void OnLogPressed(InputAction.CallbackContext ctx)
    {
        Debug.Log("Log pressed");
        
        Camera cam = Camera.main;
        Matrix4x4 P = cam.projectionMatrix;

        int width = cam.pixelWidth;
        int height = cam.pixelHeight;

        float fx = P[0, 0] * width / 2.0f;
        float fy = P[1, 1] * height / 2.0f;
        float cx = width / 2.0f;
        float cy = height / 2.0f;

        string intrinsics = $"fx={fx}, fy={fy}, cx={cx}, cy={cy}, width={width}, height={height}";
        Debug.Log(intrinsics);

        File.WriteAllText(Path.Combine(Application.dataPath, "Data/quest3_intrinsics.txt"), intrinsics);
    }
}
