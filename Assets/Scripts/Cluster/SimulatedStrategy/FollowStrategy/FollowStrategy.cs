using UnityEngine;

public class FollowStrategy : SimulationStrategy
{
    public FollowUser leader;
    public FollowUser[] followers;
    private ClusterControl cc;
    private float lastTimeSwapUsers, timeTakenToSwapUser,
        lastTimeChangeLeader, timegapForChangeLeader;
    private float latencyGapBetweenUsers;

    public override void CreateUsers(int userNum, GameObject userPrefab)
    {
        cc = ClusterControl.Instance;
        latencyGapBetweenUsers = 2;
        timeTakenToSwapUser = 3;
        for (int i = 0; i < cc.allPathNodes.Count; i++)
        {
            GameObject newUser = Instantiate(userPrefab);
            newUser.name = $"Follower{cc.transform.childCount}";
            SyntheticPathNode randomPathNode = cc.allPathNodes[i][Random.Range(0, cc.allPathNodes[i].Count)];
            //newUser.transform.position = randomPathNode.transform.position +
            //    new Vector3(Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f), 1.3f,
            //    Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f)); // comment out
            newUser.transform.position = randomPathNode.transform.position -
                    new Vector3(0, randomPathNode.transform.position.y - 1.3f, 0);
            newUser.transform.parent = cc.transform;
            leader = newUser.GetComponent<FollowUser>();
            leader.isFollower = false;
            leader.GenerateInitialPath(randomPathNode);
            leader.offset = newUser.transform.position - randomPathNode.transform.position;
            leader.timestampDirChange.Add(Time.time);
            leader.timestampDirChange.Add(Time.time);
            leader.dirAtTimeStamp.Add(leader.path[0].transform.position);
            leader.dirAtTimeStamp.Add(leader.path[1].transform.position);
            leader.speed = 2f;
            leader.transform.GetComponent<MeshRenderer>().material.color = Color.green;
            cc.users.Add(leader);

            followers = new FollowUser[userNum - 1];
            for (int j = 0; j < userNum - 1; j++)
            {
                newUser = Instantiate(userPrefab);
                newUser.name = $"Follower{cc.transform.childCount}";
                randomPathNode = leader.path[0];
                newUser.transform.position = randomPathNode.transform.position -
                    new Vector3(0, randomPathNode.transform.position.y - 1.3f, 0)
                + new Vector3(Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f), 1.3f,// comment out
                Random.Range(-cc.epsilon / 4.0f, cc.epsilon / 4.0f));// comment out
                newUser.transform.parent = cc.transform;
                followers[j] = newUser.GetComponent<FollowUser>();
                followers[j].isFollower = true;
                followers[j].leader = leader;
                followers[j].path = leader.path;
                followers[j].offset = newUser.transform.position - randomPathNode.transform.position;
                followers[j].speed = 2f;
                followers[j].innerCount = j;
                followers[j].followLatency = (j + 1) * latencyGapBetweenUsers;
                followers[j].initFollowLatency = followers[j].followLatency;
                followers[j].transform.GetComponent<MeshRenderer>().material.color = j % 2 == 0 ? Color.red : Color.blue;
                //followers[j].transform.GetComponent<MeshRenderer>().material.color = Color.red;
                cc.users.Add(followers[j]);
            }
        }
        lastTimeSwapUsers = Time.time;
        timeTakenToSwapUser = 3;
        lastTimeChangeLeader = Time.time;
        timegapForChangeLeader = 5;
    }

    public override void UpdateRegularly()
    {
        if (cc.regularlySwapUsers && Time.time > cc.timegapForSwapUsers) //&& Time.time - lastTimeSwapUsers > cc.timegapForSwapUsers
        {
            //Debug.Log("Swapping users");
            //for (int i = 0; i < followers.Length; i += 2)
            //{
            //    if (i == followers.Length - 1)
            //        break;
            //    Vector3 tempPos = followers[i].transform.position;
            //    followers[i].transform.position = followers[i + 1].transform.position;
            //    followers[i + 1].transform.position = tempPos;
            //}
            //lastTimeSwapUsers = Time.time;
            int numSwap = Mathf.FloorToInt(Time.time / cc.timegapForSwapUsers);
            for (int i = 0; i < cc.transform.childCount; i ++)
            {
                FollowUser follower = cc.transform.GetChild(i).GetComponent<FollowUser>();
                if (!follower.isFollower)
                    continue;
                int forwardOrBack = follower.innerCount % 2 == 0 ? 1 : -1;
                follower.followLatency = follower.initFollowLatency + latencyGapBetweenUsers * 
                    (follower.innerCount % 2 == 0 ? 1 : -1) * (numSwap % 2 == 1 ?
                    Mathf.Min(1, (Time.time - numSwap * cc.timegapForSwapUsers) / timeTakenToSwapUser) :
                    Mathf.Max(0, (numSwap * cc.timegapForSwapUsers + timeTakenToSwapUser - Time.time) / timeTakenToSwapUser));
                //Debug.Log($"{follower.name}, {follower.initFollowLatency}, {forwardOrBack}, " +
                //    $"{latencyGapBetweenUsers}, {numSwap}, {Time.time}, " +
                //    $"{Mathf.Min(1, (Time.time - numSwap * cc.timegapForSwapUsers) / timeTakenToSwapUser)}, " +
                //    $"{Mathf.Max(0, (numSwap * cc.timegapForSwapUsers + timeTakenToSwapUser - Time.time) / timeTakenToSwapUser)}, " +
                //    $"{follower.followLatency}");
            }
        }
        if (cc.regularlySwapLeader && Time.time - lastTimeChangeLeader > timegapForChangeLeader)
        {
            Debug.Log("Changing leader");
            int changeIndex = Random.Range(0, followers.Length);
            FollowUser tempLeader = followers[changeIndex];
            followers[changeIndex] = leader;
            followers[changeIndex].isFollower = true;
            leader = tempLeader;
            leader.isFollower = false;
            leader.GenerateInitialPath(followers[changeIndex].path[0]);
            leader.offset = (leader.transform.position - followers[changeIndex].path[0].transform.position).normalized * 2;
            leader.timestampDirChange.Add(Time.time);
            leader.dirAtTimeStamp.Add((leader.path[1].transform.position - leader.path[0].transform.position).normalized);
            leader.speed = 2f;
            leader.transform.GetComponent<MeshRenderer>().material.color = Color.green;

            for (int i = 0; i < followers.Length; i++)
            {
                followers[i].leader = leader;
                followers[i].followLatency = 1f;
                followers[i].transform.GetComponent<MeshRenderer>().material.color = i % 2 == 0 ? Color.red : Color.blue;
            }
            lastTimeChangeLeader = Time.time;
        }
    }
}
