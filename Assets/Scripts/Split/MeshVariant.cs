using System.Collections.Generic;
using UnityEngine;

public abstract class MeshVariant : MonoBehaviour
{
    public abstract List<byte[]> RequestChunks(int objectID, int chunkSize);
}
