using UnityEngine.UI;
using TMPro;
using UnityEngine;
using UnityTCPClient.Assets.Scripts;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;
using Oculus.Interaction.Locomotion;

public enum TestPhase
{
    InitialPhase, StandPhase, MovingPhase, QuestionPhase, WaitPhase, EndPhase
}

public class TestClient : Singleton<TestClient>
{
    [HideInInspector]
    public TestPhase testPhase;
    [HideInInspector]
    public int currentPathNum, currentNodeNum, currentQuestionNum;
    public Camera clientCamera;
    public TextMeshProUGUI title, instruction, nextButtonText;
    public GameObject invisibleFenses, client, mileStoneObject, questionBoard, smoothTunnel;
    public Toggle PrevButton, NextButton;
    public ToggleGroup answerGroup;
    public GameObject answerTexts, paths;
    public FirstPersonLocomotor firstPersonLocomotor;
    private GameObject[][] pathNodes;
    private int[][] answers;
    private bool bWasPressedLastFrame = false;
    private string[] Questions = new string[]
    {
        "I noticed many missing parts in the scene while it was loading.",
        "The scene became engaging quickly, even though it was still loading.",
        "I noticed many holes in the scene while it was loading.",
        "The scene never loaded completely.",
        "The parts of the scene appearing as they were loaded were distracting.",
        "The loading time was acceptable.",
        "I would prefer to see a blank screen until the entire scene is loaded.",
        "I like seeing the scene fill in gradually.",
        "I think that I would like to use this system frequently.",
        "I found the system unnecessarily complex.",
        "I thought the system was easy to use.",
        "I think that I would need the support of a technical person to be able to use this system.",
        "I found the various functions in this system were well integrated.",
        "I thought there was too much inconsistency in this system.",
        "I would imagine that most people would learn to use this system very quickly.",
        "I found the system very cumbersome to use.",
        "I felt very confident using the system.",
        "I needed to learn a lot of things before I could get going with this system."
    };

    private void Awake()
    {
        PrevButton.onValueChanged.AddListener(OnPrevToggleChanged);
        NextButton.onValueChanged.AddListener(OnNextToggleChanged);
        for (int i = 0; i < answerGroup.transform.childCount; i++)
        {
            Toggle childToggle = answerGroup.transform.GetChild(i).GetComponent<Toggle>();
            childToggle.onValueChanged.AddListener(OnAnswerToggleChanged);
        }
    }

    private void OnPrevToggleChanged(bool isOn)
    {
        if (!isOn || currentQuestionNum <= 0 || testPhase != TestPhase.QuestionPhase)
            return;
        currentQuestionNum--;
        int prevAnswer = answers[currentPathNum][currentQuestionNum];
        PrevButton.isOn = false;
        if (prevAnswer > 0)
        {
            answerGroup.transform.GetChild(prevAnswer - 1).GetComponent<Toggle>().isOn = true;
        }
        UpdateAll();
    }

    private void OnNextToggleChanged(bool isOn)
    {
        if (!isOn || currentQuestionNum >= Questions.Length || testPhase != TestPhase.QuestionPhase)
            return;

        if (!answerGroup.ActiveToggles().Any())
            return;

        if (int.TryParse(answerGroup.ActiveToggles().FirstOrDefault()?.gameObject.name, out int parsedAnswer))
        {
            answers[currentPathNum][currentQuestionNum] = parsedAnswer;
        }
        else
        {
            Debug.LogWarning("Failed to parse answer toggle name to an integer.");
        }

        currentQuestionNum++;
        if (currentQuestionNum >= Questions.Length)
        {
            currentQuestionNum = 0;
            QuitQuestion();
        }
        NextButton.isOn = false;
        answerGroup.SetAllTogglesOff();
        UpdateAll();
    }

    private void QuitQuestion()
    {
        WriteCurrentAnswers();
        if (currentNodeNum == 0)
        {
            testPhase = TestPhase.MovingPhase;
            currentNodeNum = 1;
            UpdateMileStone();
            UnTrapUser();
        } else
        {
            testPhase = TestPhase.WaitPhase;
        }
    }

    private void WriteCurrentAnswers()
    {

    }

    private void OnAnswerToggleChanged(bool isOn)
    {
        UpdateVisibility();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InitializePathNodes();
        testPhase = TestPhase.InitialPhase;
        currentQuestionNum = 0;
        currentPathNum = 0;
        currentNodeNum = 0;
        client.transform.position = pathNodes[currentPathNum][currentNodeNum].transform.position;
        TrapUser();
        answers = new int[pathNodes.Length][];
        for (int i = 0; i < pathNodes.Length; i++)
        {
            answers[i] = new int[Questions.Length];
        }
        UpdateAll();
    }

    public void ResetAll()
    {
        testPhase = TestPhase.StandPhase;
        currentPathNum++;
        currentQuestionNum = 0;
        currentNodeNum = 0;
        client.transform.position = pathNodes[currentPathNum][currentNodeNum].transform.position;
        TrapUser();
        UpdateAll();
    }

    private void InitializePathNodes()
    {
        int pathNum = 2;
        int conditionNum = 2;
        pathNodes = new GameObject[pathNum * conditionNum][];
        for (int i = 0; i < pathNum; i++)
        {
            Transform path = paths.transform.GetChild(i);
            int nodeNumThisPath = path.childCount;
            GameObject[] allNodesThisPath = new GameObject[nodeNumThisPath];
            for (int j = 0; j < nodeNumThisPath; j++)
            {
                allNodesThisPath[j] = paths.transform.GetChild(i).GetChild(j).gameObject;
            }
            for (int j = 0; j < conditionNum; j++)
            {
                pathNodes[i * conditionNum + j] = allNodesThisPath;
            }
        }
    }

    public void UpdateAll()
    {
        UpdateText();
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        PrevButton.gameObject.SetActive(testPhase == TestPhase.QuestionPhase && currentQuestionNum > 0);
        NextButton.gameObject.SetActive(testPhase == TestPhase.QuestionPhase && currentQuestionNum <= Questions.Length - 1 && answerGroup.ActiveToggles().Any());
        answerGroup.gameObject.SetActive(testPhase == TestPhase.QuestionPhase);
        answerTexts.SetActive(testPhase == TestPhase.QuestionPhase);
        mileStoneObject.SetActive(testPhase == TestPhase.MovingPhase);
    }

    private void UpdateText()
    {
        switch (testPhase)
        {
            case TestPhase.InitialPhase:
            case TestPhase.StandPhase:
                if (currentPathNum == 0)
                {
                    instruction.text = "You will see a virtual environment begin to load around you. Please remain seated, observe how the scene loads, and answer the questions that appear in front of you. You may raise your hand, take off the headset, or inform the researcher if you experience any discomfort.";
                } else
                {
                    instruction.text = "Please wait for the instructor to start the next path.";
                }
                title.text = $"Path {currentPathNum + 1}/{pathNodes.Length} - Standby";
                break;
            
            case TestPhase.QuestionPhase:
                title.text = $"Current Question: {currentQuestionNum + 1}/{Questions.Length}";
                instruction.text = Questions[currentQuestionNum];
                nextButtonText.text = currentQuestionNum == Questions.Length - 1 ? "Finish" : "Next";
                break;

            case TestPhase.MovingPhase:
                title.text = $"Path {currentPathNum + 1}/{pathNodes.Length} - Move";
                instruction.text = "Now please use the right thumbstick to move along the path toward the milestones. You may pause to look around, but continue moving forward until you return to the start point.";
                break;
        }
    }

    public void TrapUser()
    {
        if (testPhase == TestPhase.MovingPhase)
            return;
        invisibleFenses.SetActive(true);
        Vector3 clientPosition = client.transform.position;
        invisibleFenses.transform.position = new Vector3(clientPosition.x, 0, clientPosition.z);
        MoveQuestionBoardInFront();
    }

    public void UnTrapUser()
    {
        if (testPhase != TestPhase.MovingPhase)
            return;
        invisibleFenses.SetActive(false);
        invisibleFenses.transform.position = new Vector3(0, -1000, 0); // Move it out of sight
    }

    private void Update()
    {
        if (testPhase == TestPhase.MovingPhase)
        {
            CheckMilsStone();
        }

        var rightHandDevices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, rightHandDevices);

        foreach (var device in rightHandDevices)
        {
            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bPressed))
            {
                if (bPressed && !bWasPressedLastFrame)
                {
                    firstPersonLocomotor.canRotate = !firstPersonLocomotor.canRotate;
                    smoothTunnel.SetActive(firstPersonLocomotor.canRotate);
                }

                bWasPressedLastFrame = bPressed;
            }
        }
    }

    private void CheckMilsStone()
    {
        if (currentNodeNum >= pathNodes[currentPathNum].Length)
            return;

        Vector3 clientPos = client.transform.position;
        Vector3 mileStonePos = mileStoneObject.transform.position;
        float distanceToMileStone = Mathf.Sqrt(Mathf.Pow(clientPos.x - mileStonePos.x, 2) + Mathf.Pow(clientPos.z - mileStonePos.z, 2));
        float mileStoneRadius = mileStoneObject.transform.GetChild(0).GetComponent<CapsuleCollider>().radius;
        if (distanceToMileStone <= mileStoneRadius)
        {
            currentNodeNum++;
            if (currentNodeNum == pathNodes[currentPathNum].Length)
            {
                testPhase = TestPhase.QuestionPhase;
                TrapUser();
                //MoveQuestionBoardInFront();
                UpdateAll();
            } else
            {
                UpdateMileStone();
            }
        }
        if (mileStoneObject.activeSelf)
        {
            UpdateMileStoneColor();
        }
    }

    public void UpdateMileStone()
    {
        mileStoneObject.transform.position = pathNodes[currentPathNum][currentNodeNum].transform.position;
    }

    private void MoveQuestionBoardInFront()
    {
        Vector3 clientCameraPos = clientCamera.transform.position;
        Vector3 targetPosition = clientCameraPos + clientCamera.transform.forward * 1;
        float height = questionBoard.transform.position.y;
        questionBoard.transform.position = new Vector3(targetPosition.x, height, targetPosition.z);
        questionBoard.transform.LookAt(questionBoard.transform.position * 2 - clientCameraPos);
    }

    private void UpdateMileStoneColor()
    {
        GameObject ring = mileStoneObject.transform.GetChild(0).GetChild(0).gameObject;
        GameObject ball = mileStoneObject.transform.GetChild(1).gameObject;
        Color mileStoneColor = ring.GetComponent<MeshRenderer>().material.color;
        mileStoneColor = new Color(Random.Range(0, 1f), Random.Range(0, 1f), Random.Range(0, 1f));

        ring.GetComponent<MeshRenderer>().material.color = mileStoneColor;
        ball.GetComponent<MeshRenderer>().material.color = mileStoneColor;
    }
}
