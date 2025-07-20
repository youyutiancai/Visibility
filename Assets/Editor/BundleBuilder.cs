using UnityEditor;
using System.IO;

public class CreateAssetBundles
{
    [MenuItem("Assets/Build AssetBundles/Materials for Windows")]
    static void BuildAllAssetBundlesMaterialWindows()
    {
        string assetBundleDirectory = "Assets/AssetBundles/Materials/Windows";
        if (!Directory.Exists(assetBundleDirectory))
        {
            Directory.CreateDirectory(assetBundleDirectory);
        }
        BuildPipeline.BuildAssetBundles(assetBundleDirectory,
                                        BuildAssetBundleOptions.None,
                                        BuildTarget.StandaloneWindows);
    }

    [MenuItem("Assets/Build AssetBundles/Prefabs for Windows")]
    static void BuildAllAssetBundlesPrefabWindows()
    {
        string assetBundleDirectory = "Assets/AssetBundles/Prefabs/Windows";
        if (!Directory.Exists(assetBundleDirectory))
        {
            Directory.CreateDirectory(assetBundleDirectory);
        }
        BuildPipeline.BuildAssetBundles(assetBundleDirectory,
                                        BuildAssetBundleOptions.None,
                                        BuildTarget.StandaloneWindows);
    }

    [MenuItem("Assets/Build AssetBundles/Prefabs for Android")]
    static void BuildAllAssetBundlesPrefabAndroid()
    {
        string assetBundleDirectory = "Assets/AssetBundles/Prefabs/Android";
        if (!Directory.Exists(assetBundleDirectory))
        {
            Directory.CreateDirectory(assetBundleDirectory);
        }
        BuildPipeline.BuildAssetBundles(assetBundleDirectory,
                                        BuildAssetBundleOptions.None,
                                        BuildTarget.Android);
    }

    [MenuItem("Assets/Build AssetBundles/Test")]
    static void BuildAllAssetBundlesTest()
    {
        string assetBundleDirectory = "Assets/AssetBundles/Test";
        if (!Directory.Exists(assetBundleDirectory))
        {
            Directory.CreateDirectory(assetBundleDirectory);
        }
        BuildPipeline.BuildAssetBundles(assetBundleDirectory,
                                        BuildAssetBundleOptions.None,
                                        BuildTarget.StandaloneWindows);
    }

    [MenuItem("Assets/Build AssetBundles/Test for Android")]
    static void BuildAllAssetBundlesTestAndroid()
    {
        string assetBundleDirectory = "Assets/AssetBundles/Test";
        if (!Directory.Exists(assetBundleDirectory))
        {
            Directory.CreateDirectory(assetBundleDirectory);
        }
        BuildPipeline.BuildAssetBundles(assetBundleDirectory,
                                        BuildAssetBundleOptions.None,
                                        BuildTarget.Android);
    }
}