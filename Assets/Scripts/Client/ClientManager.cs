using UnityEngine;
using System.IO;

public class ClientManager : MonoBehaviour
{
    [SerializeField] UDPBroadcastClient m_Client;

    private void OnEnable()
    {
        m_Client.OnReceivedServerData += InitPrefab;
    }

    private void OnDisable()
    {
        m_Client.OnReceivedServerData -= InitPrefab;
    }

    void Start()
    {
        InitPrefab(Application.dataPath + "net_asset_bundle");
    }

    
    void Update()
    {
        
    }

    private void InitPrefab(string path)
    {
        byte[] assetBundleData = File.ReadAllBytes(path);

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

}
