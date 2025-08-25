using UnityEngine.UI;
using TMPro;
using UnityEngine;
using UnityTCPClient.Assets.Scripts;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.XR;
using Oculus.Interaction.Locomotion;
using System.IO;
using System;
using Random = UnityEngine.Random;
using System.Globalization;

public class TestClient : Singleton<TestClient>
{
    [HideInInspector]
    public TestPhase testPhase;
    [HideInInspector]
    public int currentPathNum, currentNodeNum, currentQuestionNum;
    public Camera clientCamera;
    public TextMeshProUGUI title, instruction, nextButtonText;
    public GameObject invisibleFenses, client, milestoneObject, questionBoard, smoothTunnel;
    public Toggle PrevButton, NextButton;
    public ToggleGroup answerGroup;
    public GameObject answerTexts, paths;
    public FirstPersonLocomotor firstPersonLocomotor;
    public UDPBroadcastClientNew udpBroadcastClient;
    public int pathOrder;
    private GameObject[][] pathNodes;
    private int[][] answers;
    private bool bWasPressedLastFrame = false;
    private StreamWriter answerWriter;
    private string logFilePath;
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
            UpdateMilestonePos();
            UnTrapUser();
        } else
        {
            testPhase = currentPathNum == pathNodes.Length - 1 ? TestPhase.EndPhase : TestPhase.WaitPhase;
        }
    }

    private void WriteCurrentAnswers()
    {
        string answerString = $"";
        for (int i = 0; i < answers[currentPathNum].Length; i++)
        {
            answerString += $"{answers[currentPathNum][i]},";
        }
        answerWriter.WriteLine(answerString);
        answerWriter.Flush();
    }

    private void OnAnswerToggleChanged(bool isOn)
    {
        UpdateVisibility();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        testPhase = TestPhase.InitialPhase;
        currentQuestionNum = 0;
        currentPathNum = 0;
        currentNodeNum = 0;
        UpdatePathOrder();
        answers = new int[pathNodes.Length][];
        for (int i = 0; i < pathNodes.Length; i++)
        {
            answers[i] = new int[Questions.Length];
        }
        UpdateAll();
        string filename = $"userAnswers_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        logFilePath = Path.Combine(Application.persistentDataPath, filename);
        answerWriter = new StreamWriter(logFilePath, append: false);
    }

    public void UpdatePathOrder()
    {
        int pathNum = 2;
        int conditionNum = 2;
        pathNodes = new GameObject[pathNum * conditionNum][];
        int[] pathOrderToProcess = pathOrder == 0 ? new int[] { 0, 1 } : new int[] { 1, 0 };
        for (int i = 0; i < pathNum; i++)
        {
            int currentPathToAdd = pathOrderToProcess[i];
            Transform path = paths.transform.GetChild(currentPathToAdd);
            int nodeNumThisPath = path.childCount;
            GameObject[] allNodesThisPath = new GameObject[nodeNumThisPath];
            for (int j = 0; j < nodeNumThisPath; j++)
            {
                allNodesThisPath[j] = paths.transform.GetChild(currentPathToAdd).GetChild(j).gameObject;
            }
            for (int j = 0; j < conditionNum; j++)
            {
                pathNodes[i * conditionNum + j] = allNodesThisPath;
            }
        }
        client.transform.position = pathNodes[currentPathNum][currentNodeNum].transform.position;
        TrapUser();
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
        milestoneObject.SetActive(testPhase == TestPhase.MovingPhase);
    }

    private void UpdateText()
    {
        switch (testPhase)
        {
            case TestPhase.InitialPhase:
            case TestPhase.StandPhase:
                title.text = $"Path {currentPathNum + 1}/{pathNodes.Length} - Standby";
                if (udpBroadcastClient.recGameObjects.Count == 0)
                {
                    if (currentPathNum == 0)
                    {
                        instruction.text = "You will see a virtual environment begin loading around you. Please remain seated, observe how the scene loads, and answer the questions that appear in front of you. If you experience any discomfort, you may raise your hand, remove the headset, or inform the researcher.";
                    } else
                    {
                        instruction.text = "Please wait for the instructor to start the next path.";
                    }
                } else
                {
                    instruction.text = "The scene is loading. You may look around, but please remain seated and do not move until instructed to do so.";
                }
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

            case TestPhase.WaitPhase:
                title.text = $"Path {currentPathNum + 1}/{pathNodes.Length} - Wait";
                instruction.text = "Please wait for the instructor to start the next path.";
                break;

            case TestPhase.EndPhase:
                title.text = "Test Completed";
                instruction.text = "Thank you for participating in the test. Please remove the headset and inform the researcher.";
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
            CheckMilestone();
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

    private void CheckMilestone()
    {
        if (currentNodeNum >= pathNodes[currentPathNum].Length)
            return;

        Vector3 clientPos = client.transform.position;
        Vector3 milestonePos = milestoneObject.transform.position;
        float distanceToMilestone = Mathf.Sqrt(Mathf.Pow(clientPos.x - milestonePos.x, 2) + Mathf.Pow(clientPos.z - milestonePos.z, 2));
        float milestoneRadius = milestoneObject.transform.GetChild(0).GetComponent<CapsuleCollider>().radius;
        if (distanceToMilestone <= milestoneRadius)
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
                UpdateMilestonePos();
            }
        }
        if (milestoneObject.activeSelf)
        {
            UpdateMilestoneObjects();
        }
    }

    public void UpdateMilestonePos()
    {
        milestoneObject.transform.position = pathNodes[currentPathNum][currentNodeNum].transform.position;
    }

    private void MoveQuestionBoardInFront()
    {
        Vector3 clientCameraPos = clientCamera.transform.position;
        Vector3 targetPosition = clientCameraPos + clientCamera.transform.forward * 1;
        float height = questionBoard.transform.position.y;
        questionBoard.transform.position = new Vector3(targetPosition.x, height, targetPosition.z);
        questionBoard.transform.LookAt(questionBoard.transform.position * 2 - clientCameraPos);
    }

    private void UpdateMilestoneObjects()
    {
        GameObject ring = milestoneObject.transform.GetChild(0).GetChild(0).gameObject;
        GameObject ball = milestoneObject.transform.GetChild(1).gameObject;
        GameObject arrow = milestoneObject.transform.GetChild(2).GetChild(0).gameObject;
        Color milestoneColor = ring.GetComponent<MeshRenderer>().material.color;
        milestoneColor = new Color(Random.Range(0, 1f), Random.Range(0, 1f), Random.Range(0, 1f));

        ring.GetComponent<MeshRenderer>().material.color = milestoneColor;
        ball.GetComponent<MeshRenderer>().material.color = milestoneColor;
        //arrow.GetComponent<MeshRenderer>().material.color = milestoneColor;

        Vector3 ballPos = ball.transform.position;
        Vector3 clientCameraPos = clientCamera.transform.position;
        Vector3 projectedBallPos = new Vector3(ballPos.x, arrow.transform.position.y, ballPos.z);
        Vector3 projectedCameraPos = new Vector3(clientCameraPos.x, arrow.transform.position.y, clientCameraPos.z);
        arrow.transform.position = (projectedBallPos - projectedCameraPos).normalized * 2.5f + projectedCameraPos;
        arrow.transform.LookAt(projectedBallPos);
    }

    private void OnDestroy()
    {
        if (answerWriter != null)
        {
            answerWriter.Flush();
            answerWriter.Close();
        }
    }
}
