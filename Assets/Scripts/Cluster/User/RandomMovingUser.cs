using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class RandomMovingUser : User
{

    public RandomMovingUser(Vector3 initialPos) : base(initialPos)
    {
    }
    void Update()
    {
        if (ClusterControl.Instance.SimulationStrategy == SimulationStrategyDropDown.IndiUserRandomSpawn)
        {
            UpdatePath();
        }
        MoveAlongPath();
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
        if (path == null || path.Length == 0 || currentNodeIndex >= path.Length)
            return;

        float remainingTime = Time.deltaTime;

        while (currentNodeIndex < path.Length - 1 && remainingTime > 0)
        {
            //Debug.Log(transform.position);
            // Get the current target position
            Vector3 targetPosition = path[currentNodeIndex + 1].transform.position + offset;

            // Calculate the distance to the target node
            float distanceToTarget = Vector3.Distance(transform.position, targetPosition);

            // Calculate the time required to reach the target node at the current speed
            float timeToTarget = distanceToTarget / speed;
            //Debug.Log($"{timeToTarget}, {remainingTime}");

            if (remainingTime >= timeToTarget)
            {
                // Move directly to the target position and advance to the next node
                transform.position = targetPosition;
                currentNodeIndex++;
                remainingTime -= timeToTarget;  // Subtract the time spent reaching this node
            }
            else
            {
                // Move toward the target node with the remaining time
                Vector3 direction = (targetPosition - transform.position).normalized;
                transform.position += direction * speed * remainingTime;
                remainingTime = 0;  // Set remaining time to 0 as we've used up all available time
            }
        }
    }
}
