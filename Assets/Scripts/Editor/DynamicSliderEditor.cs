using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ProgressiveMeshMultiPath))]
public class ProgressiveMeshMultiPathEditor : Editor
{
    public override void OnInspectorGUI()
    {
        ProgressiveMeshMultiPath script = (ProgressiveMeshMultiPath)target;

        // Allow changing array size
        script.targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", script.targetObject, typeof(GameObject), true);
        script.parentObject = (GameObject)EditorGUILayout.ObjectField("Parent Object", script.parentObject, typeof(GameObject), true);
        script.sliceCount = EditorGUILayout.IntField("Slice Count", script.sliceCount);
        script.ifSimplify = EditorGUILayout.Toggle("If simplify", script.ifSimplify);
        script.sliceCount = Mathf.Max(1, script.sliceCount); // Prevent zero or negative sizes

        // Resize arrays dynamically
        if (script.minValue.Length != script.sliceCount)
            script.minValue = new int[script.sliceCount];

        if (script.maxValue.Length != script.sliceCount)
            script.maxValue = new int[script.sliceCount];

        if (script.targetSteps.Length != script.sliceCount)
            script.targetSteps = new int[script.sliceCount];

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scroll Bars", EditorStyles.boldLabel);

        for (int i = 0; i < script.sliceCount; i++)
        {
            //EditorGUILayout.BeginVertical(GUI.skin.box);

            //script.minValue[i] = EditorGUILayout.IntField($"Min Value {i + 1}", script.minValue[i]);
            //script.maxValue[i] = EditorGUILayout.IntField($"Max Value {i + 1}", script.maxValue[i]);

            // Ensure min is not greater than max
            if (script.minValue[i] > script.maxValue[i])
                script.minValue[i] = script.maxValue[i];

            // Draw dynamic sliders
            script.targetSteps[i] = EditorGUILayout.IntSlider($"Value {i + 1}", script.targetSteps[i], script.minValue[i], script.maxValue[i]);

            //EditorGUILayout.EndVertical();
            //EditorGUILayout.Space();
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(script);
        }
    }
}
