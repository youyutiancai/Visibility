using UnityEngine;

// ONLY USE IN ANDROID BUILD
public class MulticastLockManager : MonoBehaviour
{
    AndroidJavaObject wifiManager;
    AndroidJavaObject multicastLock;

    void Start()
    {
        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
            multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "myMulticastLock");
            multicastLock.Call("acquire");
        }
    }

    void OnDestroy()
    {
        if (multicastLock != null)
        {
            multicastLock.Call("release");
        }
    }
}
