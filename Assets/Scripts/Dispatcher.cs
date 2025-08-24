using System;
using System.Collections.Generic;
using UnityTCPClient.Assets.Scripts;

public class Dispatcher : Singleton<Dispatcher>
{
    private static readonly Queue<Action> actions = new Queue<Action>();

    void Update()
    {
        lock (actions)
        {
            while (actions.Count > 0)
            {
                actions.Dequeue().Invoke();
            }
        }
    }

    public void Enqueue(Action action)
    {
        lock (actions)
        {
            actions.Enqueue(action);
        }
    }
}