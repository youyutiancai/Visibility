using TMPro;
using UnityEngine;
using UnityTCPClient.Assets.Scripts;

public enum TestPhase
{
    InitialPhase, StandPhase, MovingPhase, QuestionPhase, EndPhase
}

public class TestClient : Singleton<TestClient>
{
    [HideInInspector]
    public TestPhase testPhase;
    [HideInInspector]
    public int currentPathNum, currentNodeNum;
    public TextMeshProUGUI instruction;
    public GameObject invisibleFenses, client;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        testPhase = TestPhase.InitialPhase;
        currentPathNum = 0;
        currentNodeNum = 0;
    }

    // Update is called once per frame
    void Update()
    {
        UpdateUI();
        UpdateText();
    }

    private void UpdateUI()
    {
        //invisibleFenses.SetActive(testPhase != TestPhase.MovingPhase);
        //if (testPhase != TestPhase.MovingPhase)
        //{
        //    Vector3 clientPos = client.transform.position;
        //    invisibleFenses.transform.position = new Vector3(clientPos.x, 0, clientPos.z);

        //}
    }

    private void UpdateText()
    {
        switch (testPhase)
        {
            case TestPhase.InitialPhase:
            case TestPhase.StandPhase:
                instruction.text = "Welcome to the VR experiment! Please wait for everyone to get in before the instructor starts the experiment.\n" +
                    "You can use the right thumbstick to look around. If you feel uncomfortable doing so, please only look around by rotating your head.\n";
                break;
        }
    }
}
