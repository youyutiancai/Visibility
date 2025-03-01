using UnityEngine;

public class DynamicSlider : MonoBehaviour
{
    public int arraySize = 8;  // Number of sliders (modifiable)

    public int[] minValue;  // Dynamic minimum range
    public int[] maxValue;  // Dynamic maximum range
    public int[] targetSteps;  // Array of slider values

    void OnValidate()
    {
        // Ensure arrays are initialized
        if (minValue == null || minValue.Length != arraySize)
            minValue = new int[arraySize];

        if (maxValue == null || maxValue.Length != arraySize)
            maxValue = new int[arraySize];

        if (targetSteps == null || targetSteps.Length != arraySize)
            targetSteps = new int[arraySize];

        // Ensure values stay within the dynamically changing range
        for (int i = 0; i < arraySize; i++)
        {
            minValue[i] = Mathf.Min(minValue[i], maxValue[i]); // Ensure min <= max
            maxValue[i] = Mathf.Max(maxValue[i], minValue[i]); // Ensure max >= min
            targetSteps[i] = Mathf.Clamp(targetSteps[i], minValue[i], maxValue[i]); // Clamp targetSteps
        }
    }
}
