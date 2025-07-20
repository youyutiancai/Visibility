using UnityEngine;
using UnityEditor;
using System.Linq;
using System.IO;

public class AssetBundleName : MonoBehaviour
{
    [MenuItem("Assets/Change Bundle Name/name")]
    static void ChangeBundleName()
    {
        Object[] selectedAssets = Selection.GetFiltered(typeof(object), SelectionMode.Assets);
        string folderPath = AssetDatabase.GetAssetPath(selectedAssets[0]);
        string folderName = Path.GetFileNameWithoutExtension(folderPath);

        string[] assetGUIDs = AssetDatabase.FindAssets("", new string[] { folderPath });

        foreach (string assetGUID in assetGUIDs)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
            string[] paths = assetPath.Split("/");
            AssetImporter.GetAtPath(assetPath).SetAssetBundleNameAndVariant(paths[paths.Length - 1].Split(".")[0], "");
        }

        AssetDatabase.Refresh();
    }

    [MenuItem("Assets/Change Bundle Name/type")]
    static void ChangeBundleNameType()
    {
        var assets = Selection.objects.Where(o => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(o))).ToArray();

        foreach (var a in assets)
        {
            string assetPath = AssetDatabase.GetAssetPath(a);
            string[] pathJoints = assetPath.Split('/');
            AssetImporter.GetAtPath(assetPath).SetAssetBundleNameAndVariant(pathJoints[pathJoints.Length - 2], "");
        }
    }

    [MenuItem("Assets/Change Bundle Name/GrandType")]
    static void ChangeBundleNameGrandType()
    {
        var assets = Selection.objects.Where(o => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(o))).ToArray();

        foreach (var a in assets)
        {
            string assetPath = AssetDatabase.GetAssetPath(a);
            string[] pathJoints = assetPath.Split('/');
            AssetImporter.GetAtPath(assetPath).SetAssetBundleNameAndVariant(pathJoints[pathJoints.Length - 3], "");
        }
    }

    [MenuItem("Assets/Change Bundle Name/Test")]
    static void ChangeBundleNameTest()
    {
        Object[] selectedAssets = Selection.GetFiltered(typeof(object), SelectionMode.Assets);
        string folderPath = AssetDatabase.GetAssetPath(selectedAssets[0]);
        string folderName = Path.GetFileNameWithoutExtension(folderPath);

        string[] assetGUIDs = AssetDatabase.FindAssets("", new string[] { folderPath });

        foreach (string assetGUID in assetGUIDs)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
            AssetImporter.GetAtPath(assetPath).SetAssetBundleNameAndVariant(folderName, "");
        }

        AssetDatabase.Refresh();
    }
}
