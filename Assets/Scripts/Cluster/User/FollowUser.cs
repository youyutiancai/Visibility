using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class FollowUser : User
{
    public bool isFollower;
    public float followLatency, initFollowLatency;
    public FollowUser leader;
    public List<float> timestampDirChange;
    public List<Vector3> dirAtTimeStamp;
    public int innerCount;

    public FollowUser(Vector3 initialPos) : base(initialPos)
    {
        timestampDirChange = new List<float>();
        dirAtTimeStamp = new List<Vector3>();
    }

    void Update()
    {
        if (isFollower)
        {
            path = leader.path;
            FollowLeader();
        } else
        {
            MoveAlongPath();
            UpdatePath();
        }
    }

    private void FollowLeader()
    {
        transform.position = leader.transform.position;
        timestampDirChange = leader.timestampDirChange;
        dirAtTimeStamp = leader.dirAtTimeStamp;
        if (dirAtTimeStamp.Count == 0)
        {
            return;
        }
        int followCount = timestampDirChange.Count - 1;
        float currentTime = timestampDirChange[followCount];
        while (followCount > 0)
        {
            if (currentTime - timestampDirChange[followCount - 1] >= followLatency)
            {
                transform.position = (dirAtTimeStamp[followCount] - dirAtTimeStamp[followCount - 1]) *
                    (currentTime - followLatency - timestampDirChange[followCount - 1]) /
                    (timestampDirChange[followCount] - timestampDirChange[followCount - 1]) + dirAtTimeStamp[followCount - 1];
                //Debug.Log($"{currentTime}, {timestampDirChange[followCount - 1]}, {followCount}, {timestampDirChange.Count}");
                return;
            }
            followCount--;
        }
        transform.position = leader.transform.position - leader.offset + offset; // comment out
    }

    private void UpdatePath()
    {
        if (currentNodeIndex != 0)
        {
            currentNodeIndex = 0;
            path[0] = path[1];
            path[1] = path[2];
            possibleNodes = new List<SyntheticPathNode>(path[1].connectedNodes);
            possibleNodes.Remove(path[0]);
            path[2] = possibleNodes[Random.Range(0, possibleNodes.Count)];
        }
    }

    private void MoveAlongPath()
    {
        if (path == null || path.Length == 0)
        {
            Debug.LogError("There is no path!");
        }
        if (currentNodeIndex >= path.Length)
        {
            Debug.LogError("The user has suppassed the path!");
        }

        float remainingTime = Time.deltaTime;

        while (currentNodeIndex < path.Length - 1 && remainingTime > 0)
        {
            Vector3 targetPosition = path[currentNodeIndex + 1].transform.position + offset;
            float distanceToTarget = Vector3.Distance(transform.position, targetPosition);
            float timeToTarget = distanceToTarget / speed;

            if (remainingTime >= timeToTarget)
            {
                transform.position = targetPosition;
                currentNodeIndex++;
                remainingTime -= timeToTarget;
                timestampDirChange[timestampDirChange.Count - 1] = Time.time - remainingTime;
                timestampDirChange.Add(Time.time);
                dirAtTimeStamp.Add(path[currentNodeIndex + 1].transform.position);
            }
            else
            {
                Vector3 direction = (targetPosition - transform.position).normalized;
                transform.position += direction * speed * remainingTime;
                remainingTime = 0;
            }
        }
        //Debug.Log(string.Join(", ", timestampDirChange.ToArray()));
        timestampDirChange[timestampDirChange.Count - 1] = Time.time;
    }
}
