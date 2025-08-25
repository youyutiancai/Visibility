using UnityEngine;

public class ResourceLoader : MonoBehaviour
{
    public Material LoadMaterialByName(string materialName)
    {
        // Ensure your materials are in "Assets/Resources/Materials/"
        
        if (materialName.Equals("null"))
            return null;
        
        Material mat = Resources.Load<Material>("Materials/DesertCityMateials/" + materialName);
        if (mat == null)
        {
            Debug.LogWarning("Material " + materialName + " not found in Resources/MaterialsDesertCityMateials");
        }
        return mat;
    }
}
