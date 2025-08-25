using UnityEngine;
using System.IO;
using UnityEngine;
public class SerializationTest : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Deserialize();
    }

    private void Deserialize()
    {
        string assetBundlePath = Application.dataPath + "/asset_bundle_test";
        byte[] assetBundleData = File.ReadAllBytes(assetBundlePath);

        // Optionally, you can now send this byte array over the network
        Debug.Log("AssetBundle loaded into byte array. Length: " + assetBundleData.Length);

        // To load the bundle from memory:
        AssetBundle bundle = AssetBundle.LoadFromMemory(assetBundleData);
        if (bundle == null)
        {
            Debug.LogError("Failed to load AssetBundle from memory.");
            return;
        }

        // Load a prefab by name (must match the one you assigned in the editor)
        GameObject prefab = bundle.LoadAsset<GameObject>("balcony_1");
        if (prefab != null)
        {
            // Instantiate the prefab
            Instantiate(prefab);
        }
        else
        {
            Debug.LogError("Prefab not found in AssetBundle.");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
