using UnityEngine;

public abstract class SimulationStrategy : MonoBehaviour
{
    public abstract void CreateUsers(int userNum, GameObject userPrefab);
    public abstract void UpdateRegularly();
}

public enum SimulationStrategyDropDown
{
    FollowStrategy, IndiUserRandomSpawn, RealUserCluster, RealUserIndi
} 