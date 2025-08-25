using UnityEngine;
using UnityEditor;
using static Codice.Client.Commands.WkTree.WorkspaceTreeNode;
using Unity.VisualScripting;
using System.Collections.Generic;
using System;

public class SceneViewClickTool : EditorWindow
{
    private bool isToolActive = false, isMouseHeld = false, isLoop = false; // Track whether the tool is active
    private GameObject pathNodePrefab, pathNodeParent, pathNodeRoot, lastPathNode;
    private string pathNodePrefabPath = "Assets/Prefabs/PathNode.prefab";
    private Vector3 lastGeneratedPos = Vector3.positiveInfinity;

    [MenuItem("Tools/Scene View Click Tool")]
    public static void ShowWindow()
    {
        // Open the window
        SceneViewClickTool window = GetWindow<SceneViewClickTool>("Scene View Click Tool");

        // Enable the tool automatically
        window.ToggleTool(true);
    }

    private void OnGUI()
    {
        GUILayout.Label("Scene View Click Tool", EditorStyles.boldLabel);

        // Parent object field
        //pathNodePrefab = (GameObject)EditorGUILayout.ObjectField("pathNodePrefab", pathNodeParent, typeof(GameObject), true);
        isLoop = EditorGUILayout.Toggle("Loop path", isLoop);
        pathNodeRoot = GameObject.Find("PathNodes");

        // Instructions
        GUILayout.Space(10);
        EditorGUILayout.HelpBox("Click in the Scene View to place a cube at y = 0. The tool is enabled as long as this window is open.", MessageType.Info);
    }

    private void ToggleTool(bool enable)
    {
        if (enable && !isToolActive)
        {
            SceneView.duringSceneGui += OnSceneGUI;
            isToolActive = true;
            Debug.Log("Scene View Click Tool Enabled.");
        }
        else if (!enable && isToolActive)
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            isToolActive = false;
            Debug.Log("Scene View Click Tool Disabled.");
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!isToolActive) return;

        Event e = Event.current;

        // Check for left mouse click
        if (e.type == EventType.MouseDown && e.button == 0) // Left-click
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition); // Ray from camera through mouse
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0, 1, 0));

            if (groundPlane.Raycast(ray, out float distance))
            {
                Vector3 hitPoint = ray.GetPoint(distance); // Point on the ground plane
                Debug.Log($"Clicked Position in Scene: {hitPoint}");
                pathNodeParent = new GameObject($"newPath_{pathNodeRoot.transform.childCount}");
                pathNodeParent.transform.SetParent(pathNodeRoot.transform);
                lastPathNode = null;
                // Generate a cube at the clicked position
                GenerateNewPathNode(hitPoint);
                isMouseHeld = true;

                // Consume the event
                e.Use();
            }
            
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            isMouseHeld = false;
            if (isLoop)
            {
                AddConnection(pathNodeParent.transform.GetChild(0).GetComponent<SyntheticPathNode>(),
                    pathNodeParent.transform.GetChild(pathNodeParent.transform.childCount - 1).GetComponent<SyntheticPathNode>());
            }

            // Add and configure the LineRenderer
            LineRenderer lr = pathNodeParent.AddComponent<LineRenderer>();

            // Ensure the LineRenderer has a material (mandatory for rendering)
            lr.material = new Material(Shader.Find("Sprites/Default")); // You can replace with a custom material if needed

            // Configure width and color
            lr.startWidth = 0.2f; // Adjust width as needed
            lr.endWidth = 0.2f;
            lr.startColor = Color.white; // Adjust color as needed
            lr.endColor = Color.white;
            lr.loop = isLoop;
            // Set positions for the LineRenderer
            lr.positionCount = pathNodeParent.transform.childCount;
            Vector3[] lrPoints = new Vector3[lr.positionCount];
            for (int i = 0; i < lrPoints.Length; i++) {
                lrPoints[i] = pathNodeParent.transform.GetChild(i).position;
            }
            lr.SetPositions(lrPoints);

            // Optional: Adjust rendering properties
            lr.useWorldSpace = true; // Ensure it works in world space
            lr.numCapVertices = 5;   // Smooth rounded ends
        }

        if (isMouseHeld)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition); // Ray from camera through mouse
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0, 1, 0));

            if (groundPlane.Raycast(ray, out float distance))
            {
                Vector3 hitPoint = ray.GetPoint(distance);
                if (Vector3.Distance(hitPoint, lastGeneratedPos) > 5)
                {
                    GenerateNewPathNode(hitPoint);
                }
                // Consume the event
                e.Use();
            }
            
        }
    }

    private void GenerateNewPathNode(Vector3 position)
    {
        // Instantiate the new path node
        GameObject newPathNode = Instantiate(pathNodePrefab);
        newPathNode.transform.position = position;

        // Set parent
        if (pathNodeParent != null)
        {
            newPathNode.transform.SetParent(pathNodeParent.transform);
        }

        // Register Undo
        Undo.RegisterCreatedObjectUndo(newPathNode, "Created Path Node");
        newPathNode.name = $"{pathNodeParent.name}_{pathNodeParent.transform.childCount}";

        // Link to the last path node
        if (lastPathNode != null)
        {
            AddConnection(newPathNode.GetComponent<SyntheticPathNode>(), lastPathNode.GetComponent<SyntheticPathNode>());   
        }

        // Update tracking variables
        lastGeneratedPos = position;
        lastPathNode = newPathNode;
    }

    private void AddConnection(SyntheticPathNode spn1,  SyntheticPathNode spn2)
    {
        if (spn1.connectedNodes == null)
            spn1.connectedNodes = new List<SyntheticPathNode>();
        if (spn2.connectedNodes == null)
            spn2.connectedNodes = new List<SyntheticPathNode>();
        spn1.connectedNodes.Add(spn2);
        spn2.connectedNodes.Add(spn1);
    }

    private void OnEnable()
    {
        // Automatically enable the tool when the window is opened
        ToggleTool(true);

        // Load the prefab from the Assets folder
        pathNodePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pathNodePrefabPath);
        if (pathNodePrefab == null)
        {
            Debug.LogError($"Failed to load prefab at path: {pathNodePrefabPath}");
        }
    }


    private void OnDisable()
    {
        // Automatically disable the tool when the window is closed
        ToggleTool(false);
    }
}
