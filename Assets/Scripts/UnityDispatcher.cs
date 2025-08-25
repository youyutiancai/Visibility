using System;
using System.Collections.Generic;
using UnityEngine;

public class UnityDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _actions = new Queue<Action>();
    private static UnityDispatcher _instance;

    public static UnityDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                Debug.LogError("UnityDispatcher is not initialized. " +
                    "Ensure the UnityDispatcher script is attached to a GameObject in your scene.");
            }
            return _instance;
        }
    }

    private void Awake()
    {
        // Initialize the singleton instance on the main thread.
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Enqueue an action to be executed on the main thread.
    /// </summary>
    public void Enqueue(Action action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }
        lock (_actions)
        {
            _actions.Enqueue(action);
        }
    }

    private void Update()
    {
        // Execute all queued actions on the main thread.
        while (true)
        {
            Action action = null;
            lock (_actions)
            {
                if (_actions.Count > 0)
                {
                    action = _actions.Dequeue();
                }
                else
                {
                    break;
                }
            }
            action?.Invoke();
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }
}
