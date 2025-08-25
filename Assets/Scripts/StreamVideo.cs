using UnityEngine;
using UnityEngine.Video;

public class StreamVideo : MonoBehaviour
{
    public VideoPlayer videoPlayer;

    void Start()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "TestVideo.mp4");
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = path;
        videoPlayer.Play();
    }
}
