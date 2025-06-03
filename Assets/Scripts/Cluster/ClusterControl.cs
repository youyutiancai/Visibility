using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using TMPro;
//using Unity.VisualScripting;
using UnityTCPClient.Assets.Scripts;
using System.IO;
using System;
using Random = UnityEngine.Random;
using UnityEngine.InputSystem;
using System.Net.Sockets;

public class ClusterControl : Singleton<ClusterControl>
{
    public GameObject randomMovingUserPrefab, followUserPrefab, realUserPrefab, clusterPrefab, initialClusterCenter;  // clusterPrefab is used to represent a cluster visually
    //public SyntheticPathNode[] clusterInitPoses;
    //public SyntheticPathNode[] paths;
    public TextMeshProUGUI displayText;

    public float epsilon = 10;  // Radius for clustering
    public int minPts = 2;      // Minimum points to form a cluster
    public float updateInterval, newChunkInterval, timegapForSwapUsers;  // How often to update (in seconds)
    public bool regularlySwapUsers, regularlySwapLeader, writeToData;

    //[HideInInspector]
    public List<User> users = new List<User>();
    [HideInInspector]
    public List<List<SyntheticPathNode>> allPathNodes;
    [HideInInspector]
    public Vector3 initialClusterCenterPos;
    [HideInInspector]
    //public List<int> objectsWaitToBeSent;
    public PriorityQueue<int, float> objectsWaitToBeSent;
    private float timeSinceLastUpdate = 0f;
    public SimulationStrategyDropDown SimulationStrategy;
    private SimulationStrategy ss;
    private Dictionary<int, Color> clusterColors = new Dictionary<int, Color>();  // Cluster ID to color mapping
    private Dictionary<int, GameObject> clusterGameObjects = new Dictionary<int, GameObject>(); // Cluster ID to GameObject mapping
    private VisibilityCheck vc;
    private int[] visibleObjectsInRegion;
    private GridDivide gd;
    private int objectSentCluster, objectSentIndi;
    private GameObject pathNodesRoot;
    private NetworkControl nc;
    private bool canSendObjects;

    private StreamWriter writer;
    private string filePath;

    void Start()
    {
        InitialValues();
        CreateClusters();
        if (writeToData)
        {
            InitializeIndividualUserDataWriter();
        }
    }

    private void InitialValues()
    {
        vc = VisibilityCheck.Instance;
        nc = NetworkControl.Instance;
        gd = GridDivide.Instance;
        visibleObjectsInRegion = new int[vc.objectsInScene.Count];
        objectSentCluster = 0;
        objectSentIndi = 0;
        pathNodesRoot = GameObject.Find("PathNodes");
        initialClusterCenterPos = initialClusterCenter.transform.position;
        objectsWaitToBeSent = new PriorityQueue<int, float>();
        canSendObjects = false;
    }

    private void InitializeIndividualUserDataWriter()
    {
        filePath = $"C:\\Users\\zhou1168\\VRAR\\Visibility\\Assets\\GeneratedData\\IndividualUserUpdateData\\{0}.csv";
        writer = new StreamWriter(filePath, false);
        //if (new FileInfo(filePath).Length == 0)
        //{
        writer.WriteLine("Timestamp,UserID,ObjectID,UserX,UserY,UserZ,ObjectX,ObjectY,ObjectZ,Object_dis,Object_size");
        //}
        Debug.Log("CSV file created or opened at: " + filePath);
    }

    private void CreateClusters()
    {
        allPathNodes = new List<List<SyntheticPathNode>>();
        for (int i = 0; i < pathNodesRoot.transform.childCount; i++)
        {
            Transform path = pathNodesRoot.transform.GetChild(i);
            allPathNodes.Add(new List<SyntheticPathNode>() {});
            for (int j = 0; j < path.childCount; j++)
            {
                allPathNodes[i].Add(path.GetChild(j).GetComponent<SyntheticPathNode>());
            }

        }

        switch (SimulationStrategy)
        {
            case SimulationStrategyDropDown.FollowStrategy:
                ss = new FollowStrategy();
                ss.CreateUsers(5, followUserPrefab);
                break;

            case SimulationStrategyDropDown.IndiUserRandomSpawn:
                ss = new IndiUserRandomSpawn();
                ss.CreateUsers(100, randomMovingUserPrefab);
                break;

            case SimulationStrategyDropDown.RealUser:
                ss = new RealUserStrategy();
                break;
        }    
    }

    void Update()
    {
        timeSinceLastUpdate += Time.deltaTime;
        nc.timeSinceLastBroadcast += Time.deltaTime;

        if (SimulationStrategy == SimulationStrategyDropDown.RealUser && Keyboard.current.bKey.wasPressedThisFrame)
        {
            canSendObjects = !canSendObjects;
            Debug.Log($"canSendObject changed to {canSendObjects}");
        }

        if (canSendObjects && nc.timeSinceLastBroadcast >= newChunkInterval && nc.readyForNextObject && objectsWaitToBeSent.Count > 0)
        {
            int sendingObjectIdx = objectsWaitToBeSent.Dequeue();
            nc.BroadcastObjectData(sendingObjectIdx, newChunkInterval);
            Debug.Log($"broadcasting: {nc.isBroadcast}. finished {sendingObjectIdx}, {objectsWaitToBeSent.Count} is left");
        }

        
        if (users.Count == 0)
            return;

        switch (ss)
        {
            case IndiUserRandomSpawn:
                if (timeSinceLastUpdate >= updateInterval)
                {
                    timeSinceLastUpdate = 0f;
                    RunDBSCAN(); // Re-run DBSCAN clustering
                    ApplyClusterColors(); // Apply colors based on clustering
                    UpdateClusterParents(); // Update parents and scale clusters
                    SendObjectsToClusters();
                    SendObjectsToUsers();
                    UpdateMethodComparison();
                }
                break;


            case RealUserStrategy:
                if (timeSinceLastUpdate >= updateInterval)
                {
                    timeSinceLastUpdate = 0f;
                    RunDBSCAN();
                    ApplyClusterColors();
                    UpdateClusterParents();
                    int[] newObjectsToSend = SendObjectsToClusters();

                    foreach (var user in users)
                    {
                        user.UpdateVisibleObjectsIndi(newObjectsToSend, ref objectSentIndi, null);
                    }

                    for (int i = 0; i < newObjectsToSend.Length; i++)
                    {
                        if (newObjectsToSend[i] > 0 && !objectsWaitToBeSent.Contains(i))
                        {
                            float newDistance = Vector3.Distance(vc.objectsInScene[i].transform.position, initialClusterCenter.transform.position);
                            objectsWaitToBeSent.Enqueue(i, newDistance);
                        }
                    }
                }
                SimulateAndSendPuppetPoses();
                break;
        }
            //if (regularlySwapUsers || regularlySwapLeader)
            //{
            //    ss.UpdateRegularly();
            //}
    }

    private void SimulateAndSendPuppetPoses()
    {
        foreach (var user in users)
        {
            if (user is RealUser realUser && realUser.isPuppet && realUser.tcpClient?.Connected == true)
            {
                //Debug.Log($"changing");
                float t = Time.time;
                float radius = 1.5f;
                float speed = 0.5f;
                float angle = t * speed;

                //Vector3 position = realUser.latestPosition;
                Vector3 position = realUser.simulatedPosition;
                position = new Vector3(position.x, 1.3f, position.z);
                //Vector3 position = new Vector3(Mathf.Cos(angle), 1.3f, Mathf.Sin(angle)) * radius + initialClusterCenterPos;
                Quaternion rotation = Quaternion.LookRotation(new Vector3(Mathf.Sin(angle), 0.05f, Mathf.Cos(angle)), Vector3.up);
                //Quaternion rotation = realUser.simulatedRotation;
                //Quaternion rotation = realUser.latestRotation;

                realUser.simulatedPosition = position;
                realUser.simulatedRotation = rotation;
                //realUser.latestPosition = position;
                //realUser.latestRotation = rotation;
                //realUser.transform.SetPositionAndRotation(position, rotation);

                try
                {
                    List<byte> buffer = new List<byte>();
                    buffer.AddRange(BitConverter.GetBytes(0));
                    buffer.AddRange(BitConverter.GetBytes((int)TCPMessageType.POSE_FROM_SERVER));
                    buffer.AddRange(BitConverter.GetBytes(position.x));
                    buffer.AddRange(BitConverter.GetBytes(position.y));
                    buffer.AddRange(BitConverter.GetBytes(position.z));
                    buffer.AddRange(BitConverter.GetBytes(rotation.x));
                    buffer.AddRange(BitConverter.GetBytes(rotation.y));
                    buffer.AddRange(BitConverter.GetBytes(rotation.z));
                    buffer.AddRange(BitConverter.GetBytes(rotation.w));
                    byte[] message = buffer.ToArray();
                    Buffer.BlockCopy(BitConverter.GetBytes(buffer.Count - sizeof(int)), 0, message, 0, sizeof(int));
                    NetworkStream stream = realUser.tcpClient.GetStream();
                    stream.Write(message, 0, message.Length);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to send puppet pose to {realUser.tcpEndPoint}: {e.Message}");
                }
            }
        }
    }


    void RunDBSCAN()
    {
        // Reset cluster IDs and clear existing cluster colors and clusters before starting
        foreach (var user in users)
        {
            user.ClusterId = -1;
            user.transform.parent = transform;  // Set default parent to ClusterControl
        }
        clusterColors.Clear();

        // Destroy all previous cluster GameObjects
        foreach (var clusterObj in clusterGameObjects.Values)
        {
            Destroy(clusterObj);
        }
        clusterGameObjects.Clear();

        int clusterId = 0;

        foreach (var point in users)
        {
            if (point.ClusterId != -1) continue;  // Skip if already visited

            var neighbors = GetNeighbors(point);

            if (neighbors.Count < minPts)
            {
                // Treat individual users as their own, but don't create a separate cluster GameObject
                clusterId++;
                point.ClusterId = clusterId;
                clusterColors[clusterId] = GetRandomColor();  // Assign a random color to the single-user cluster
            }
            else
            {
                clusterId++;
                clusterColors[clusterId] = GetRandomColor();  // Assign a random color to the new cluster

                // Instantiate clusterPrefab for multi-user clusters
                GameObject newClusterObj = Instantiate(clusterPrefab, transform);
                newClusterObj.name = "Cluster_" + clusterId;  // Name the cluster for clarity
                newClusterObj.tag = "Cluster";
                clusterGameObjects[clusterId] = newClusterObj;

                ExpandCluster(point, neighbors, clusterId);
            }
        }
    }

    void ExpandCluster(User point, List<User> neighbors, int clusterId)
    {
        // Start the cluster with the initial point
        List<User> clusterPoints = new List<User> { point };
        point.ClusterId = clusterId;

        int index = 0;
        while (index < neighbors.Count)
        {
            var neighbor = neighbors[index];
            index++;

            // Skip if already visited or assigned to another cluster
            if (neighbor.ClusterId != -1 && neighbor.ClusterId != 0)
                continue;

            // Check if adding this neighbor maintains the 2 * epsilon distance constraint
            bool canAddToCluster = true;
            foreach (var existingPoint in clusterPoints)
            {
                if (Vector3.Distance(existingPoint.transform.position, neighbor.transform.position) > epsilon)
                {
                    canAddToCluster = false;
                    break;
                }
            }

            if (canAddToCluster)
            {
                // Assign cluster ID only after passing the distance check
                neighbor.ClusterId = clusterId;
                clusterPoints.Add(neighbor);  // Add to the cluster

                // Get neighbors of this valid neighbor
                var neighborNeighbors = GetNeighbors(neighbor);

                // If the neighbor has enough nearby points, add them to the list to be checked
                if (neighborNeighbors.Count >= minPts)
                {
                    foreach (var newNeighbor in neighborNeighbors)
                    {
                        if (newNeighbor.ClusterId == -1 || newNeighbor.ClusterId == 0)
                        {
                            neighbors.Add(newNeighbor);  // Add to neighbors list for future checks
                        }
                    }
                }
            }
        }
    }




    List<User> GetNeighbors(User point)
    {
        List<User> neighbors = new List<User>();

        foreach (var p in users)
        {
            if (Distance(point, p) <= epsilon/2)
            {
                neighbors.Add(p);
            }
        }

        return neighbors;
    }

    double Distance(User p1, User p2)
    {
        return Vector3.Distance(p1.transform.position, p2.transform.position);
    }

    void ApplyClusterColors()
    {
        foreach (var user in users)
        {
            int clusterId = user.ClusterId;
            if (clusterColors.ContainsKey(clusterId))
            {
                // Set the user's color based on the cluster color
                Renderer renderer = user.GetComponent<Renderer>();

                if (renderer != null)
                {
                    renderer.material.color = clusterColors[clusterId];
                }
            }
        }
    }

    void UpdateClusterParents()
    {
        // Calculate and update each cluster's position and scale
        foreach (var clusterId in clusterGameObjects.Keys)
        {
            GameObject clusterObj = clusterGameObjects[clusterId];
            if (clusterObj == null) continue;

            // Initialize variables for finding the two farthest points
            Vector3 pointA = Vector3.zero;
            Vector3 pointB = Vector3.zero;
            float maxDistance = 0f;

            // Find the two farthest points within the cluster
            foreach (var user1 in users)
            {
                if (user1.ClusterId != clusterId) continue;

                foreach (var user2 in users)
                {
                    if (user2.ClusterId != clusterId || user1 == user2) continue;

                    float distance = Vector3.Distance(user1.transform.position, user2.transform.position);
                    if (distance > maxDistance)
                    {
                        maxDistance = distance;
                        pointA = user1.transform.position;
                        pointB = user2.transform.position;
                    }
                }
            }

            if (maxDistance > 0f)
            {
                // Set the cluster center to the midpoint between the two farthest points
                Vector3 clusterCenter = (pointA + pointB) / 2;

                // Set the final radius based on maxDistance, ensuring it is at least epsilon
                float finalRadius = Mathf.Max(maxDistance / 2, epsilon);

                // Set the position and scale of the cluster to ensure it circles all users
                clusterObj.transform.position = clusterCenter;
                clusterObj.transform.localScale = new Vector3(finalRadius * 2 * 0.007f, 0.1f, finalRadius * 2 * 0.007f);

                // Set the color of the cluster to match the cluster color
                Renderer renderer = clusterObj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = clusterColors[clusterId];
                }
            }
        }

        // Update each user's parent based on their cluster ID
        foreach (var user in users)
        {
            int clusterId = user.ClusterId;

            if (clusterGameObjects.ContainsKey(clusterId) && clusterGameObjects[clusterId] != null)
            {
                // If part of a multi-user cluster, set parent to the cluster GameObject
                user.transform.parent = clusterGameObjects[clusterId].transform;
            }
            else
            {
                // Otherwise, set parent to ClusterControl (for individual users or unclustered users)
                user.transform.parent = transform;
            }
        }
    }

    Color GetRandomColor()
    {
        return new Color(Random.value, Random.value, Random.value);
    }

    private int[] SendObjectsToClusters()
    {
        int[] newObjectCount = new int[vc.objectsInScene.Count];
        for (int i = 0; i < transform.childCount; i++) {
            if (transform.GetChild(i).tag == "Cluster")
            {
                Transform child = transform.GetChild(i);
                visibleObjectsInRegion = new int[vc.objectsInScene.Count];
                vc.GetVisibleObjectsInRegion(child.position, epsilon, ref visibleObjectsInRegion);
                for (int j = 0; j < child.childCount; j++)
                {
                    child.GetChild(j).GetComponent<User>().UpdateVisibleObjects(visibleObjectsInRegion, ref newObjectCount);
                }
                objectSentCluster += newObjectCount.Sum();
            }

            if (transform.GetChild(i).tag == "User")
            {
                User user = transform.GetChild(i).GetComponent<User>();
                Vector3 position = user.transform.position;
                int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
                int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
                if (xStartIndex == user.preX && zStartIndex == user.preZ) { continue; }
                visibleObjectsInRegion = new int[vc.objectsInScene.Count];
                vc.GetVisibleObjectsInRegion(user.transform.position, epsilon / 2, ref visibleObjectsInRegion);
                user.UpdateVisibleObjects(visibleObjectsInRegion, ref newObjectCount);
            }
        }
        return newObjectCount;
    }

    private void SendObjectsToUsers()
    {
        for (int i = 0; i < users.Count; i++) {
            UpdateVisIndi(users[i], writer);
        }
    }

    private void UpdateVisIndi(User user, StreamWriter writer)
    {
        Vector3 position = user.transform.position;
        int xStartIndex = Mathf.FloorToInt((position.x - gd.gridCornerParent.transform.position.x) / gd.gridSize);
        int zStartIndex = Mathf.FloorToInt((position.z - gd.gridCornerParent.transform.position.z) / gd.gridSize);
        if (xStartIndex == user.preX && zStartIndex == user.preZ) { return; }
        visibleObjectsInRegion = new int[vc.objectsInScene.Count];
        vc.GetVisibleObjectsInRegion(user.transform.position, epsilon / 2, ref visibleObjectsInRegion);
        user.UpdateVisibleObjectsIndi(visibleObjectsInRegion, ref objectSentIndi, writer);
        user.preX = xStartIndex;
        user.preZ = zStartIndex;
    }
     
    private void UpdateMethodComparison()
    {
        displayText.text = "";
        displayText.text += $"# of objects sent based on clusters: {objectSentCluster}\n" +
            $"# of objects sent based on individuals: {objectSentIndi}";
    }

    void OnApplicationQuit()
    {
        // Close the file when the application quits
        if (writer != null)
        {
            writer.Close();
            writer.Dispose();
            Debug.Log("CSV file closed.");
        }
    }
}

public class PriorityQueue<TElement, TPriority> where TPriority : IComparable<TPriority>
{
    private List<(TElement Element, TPriority Priority)> _heap = new();
    private Dictionary<TElement, int> _indexMap = new();

    public int Count => _heap.Count;

    public void Enqueue(TElement element, TPriority priority)
    {
        if (_indexMap.ContainsKey(element))
            throw new InvalidOperationException("Element already exists. Use UpdatePriority instead.");

        _heap.Add((element, priority));
        int i = _heap.Count - 1;
        _indexMap[element] = i;
        HeapifyUp(i);
    }

    public TElement Dequeue()
    {
        if (_heap.Count == 0)
            throw new InvalidOperationException("PriorityQueue is empty.");

        var root = _heap[0].Element;
        Swap(0, _heap.Count - 1);

        _heap.RemoveAt(_heap.Count - 1);
        _indexMap.Remove(root);

        if (_heap.Count > 0)
            HeapifyDown(0);

        return root;
    }

    public TElement Peek()
    {
        if (_heap.Count == 0)
            throw new InvalidOperationException("PriorityQueue is empty.");
        return _heap[0].Element;
    }

    public void UpdatePriority(TElement element, TPriority newPriority)
    {
        if (!_indexMap.TryGetValue(element, out int index))
            throw new KeyNotFoundException("Element not found in priority queue.");

        var current = _heap[index];
        int cmp = newPriority.CompareTo(current.Priority);
        _heap[index] = (element, newPriority);

        if (cmp < 0)
            HeapifyUp(index);
        else if (cmp > 0)
            HeapifyDown(index);
    }

    public bool Contains(TElement element) => _indexMap.ContainsKey(element);

    public void Clear()
    {
        _heap.Clear();
        _indexMap.Clear();
    }

    private void HeapifyUp(int i)
    {
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (_heap[i].Priority.CompareTo(_heap[parent].Priority) >= 0)
                break;

            Swap(i, parent);
            i = parent;
        }
    }

    private void HeapifyDown(int i)
    {
        int last = _heap.Count - 1;
        while (true)
        {
            int left = 2 * i + 1;
            int right = 2 * i + 2;
            int smallest = i;

            if (left <= last && _heap[left].Priority.CompareTo(_heap[smallest].Priority) < 0)
                smallest = left;
            if (right <= last && _heap[right].Priority.CompareTo(_heap[smallest].Priority) < 0)
                smallest = right;

            if (smallest == i) break;

            Swap(i, smallest);
            i = smallest;
        }
    }

    private void Swap(int i, int j)
    {
        var temp = _heap[i];
        _heap[i] = _heap[j];
        _heap[j] = temp;

        _indexMap[_heap[i].Element] = i;
        _indexMap[_heap[j].Element] = j;
    }
}