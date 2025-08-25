using UnityEngine;
using static UnityEngine.UIElements.UxmlAttributeDescription;

public class IndiUserRandomSpawn : SimulationStrategy
{
    public override void CreateUsers(int userNum, GameObject userPrefab)
    {
        ClusterControl cc = ClusterControl.Instance;
        for (int j = 0; j < userNum; j++)
        {
            GameObject newUser = Instantiate(userPrefab);
            SyntheticPathNode randomPathNode = cc.allPathNodes[0][Random.Range(0, cc.allPathNodes.Count)];
            newUser.transform.position = randomPathNode.transform.position +
                new Vector3(Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f), 1.3f, 
                Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f));
            newUser.transform.parent = cc.transform;  // Set default parent to ClusterControl
            RandomMovingUser user = newUser.GetComponent<RandomMovingUser>();
            user.GenerateInitialPath(randomPathNode);
            user.speed = 2f;
            cc.users.Add(user);
        }
    }

    public override void UpdateRegularly()
    {
        throw new System.NotImplementedException();
    }
}
