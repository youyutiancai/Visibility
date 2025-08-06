using UnityEngine.UI;
using TMPro;
using UnityEngine;
using UnityTCPClient.Assets.Scripts;
using System.Linq;
using System.Collections;

public enum TestPhase
{
    InitialPhase, StandPhase, MovingPhase, QuestionPhase, EndPhase
}

public class TestClient : Singleton<TestClient>
{
    [HideInInspector]
    public TestPhase testPhase;
    [HideInInspector]
    public int currentPathNum, currentNodeNum, currentQuestionNum;
    public TextMeshProUGUI title;
    public TextMeshProUGUI instruction;
    public GameObject invisibleFenses, client;
    public Toggle PrevButton, NextButton;
    public ToggleGroup answerGroup;
    private int[][] answers;
    private int totalPathNum = 4;
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
        UpdateText();
        UpdateVisibility();
    }

    private void OnNextToggleChanged(bool isOn)
    {
        if (!isOn || currentQuestionNum >= Questions.Length - 1 || testPhase != TestPhase.QuestionPhase)
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
            currentPathNum++;
            if (currentPathNum >= totalPathNum)
            {
                // Finish all paths
                testPhase = TestPhase.EndPhase;
                instruction.text = "Thank you for participating in the experiment! You may now remove your headset.";
                title.text = "Experiment Finished";
                invisibleFenses.SetActive(false);
                client.SetActive(false);
                PrevButton.gameObject.SetActive(false);
                NextButton.gameObject.SetActive(false);
                answerGroup.gameObject.SetActive(false);
                return;
            }
            else
            {
                // Move to the next path
                currentQuestionNum = 0;
                currentNodeNum = 0;
                testPhase = TestPhase.StandPhase;
            }
        }
        NextButton.isOn = false;
        answerGroup.SetAllTogglesOff();
        UpdateText();
        UpdateVisibility();
    }

    private void OnAnswerToggleChanged(bool isOn)
    {
        UpdateVisibility();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        testPhase = TestPhase.QuestionPhase;
        currentQuestionNum = 0;
        currentPathNum = 0;
        currentNodeNum = 0;
        answers = new int[totalPathNum][];
        for (int i = 0; i < totalPathNum; i++)
        {
            answers[i] = new int[Questions.Length];
        }
        UpdateText();
        UpdateVisibility();
        TrapUser();
    }

    private void UpdateVisibility()
    {
        PrevButton.gameObject.SetActive(testPhase == TestPhase.QuestionPhase && currentQuestionNum > 0);
        NextButton.gameObject.SetActive(testPhase == TestPhase.QuestionPhase && currentQuestionNum < Questions.Length - 1 && answerGroup.ActiveToggles().Any());
    }

    private void UpdateText()
    {
        switch (testPhase)
        {
            case TestPhase.InitialPhase:
            case TestPhase.StandPhase:
                if (currentPathNum == 0)
                {
                    instruction.text = "Welcome to the VR experiment! Please wait for everyone to join before the instructor begins.\n" +
                        "Use the right thumbstick to look around. If you feel any discomfort, feel free to look around by turning your head instead.\n";
                } else
                {
                    instruction.text = "Please wait for the instructor to start the next path.";
                }
                title.text = $"Path {currentPathNum + 1}/{totalPathNum} - Standby";
                break;
            
            case TestPhase.QuestionPhase:
                title.text = $"Current Question: {currentQuestionNum + 1}/{Questions.Length}";
                instruction.text = Questions[currentQuestionNum];
                break;
        }
    }

    public void TrapUser()
    {
        if (testPhase == TestPhase.MovingPhase)
            return;
        invisibleFenses.SetActive(true);
        invisibleFenses.transform.position = client.transform.position;
    }

    public void UnTrapUser()
    {
        if (testPhase != TestPhase.MovingPhase)
            return;
        invisibleFenses.SetActive(false);
        invisibleFenses.transform.position = new Vector3(0, -1000, 0); // Move it out of sight
    }
}
