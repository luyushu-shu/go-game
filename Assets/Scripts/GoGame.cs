using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[System.Serializable]
public class MoveCommentItem
{
    public int ply;
    public string text;
}

[System.Serializable]
public class MoveCommentsData
{
    public List<MoveCommentItem> items = new List<MoveCommentItem>();
}

[System.Serializable]
public class BranchStepData
{
    public int step;
    public int x;
    public int y;
    public int color;
    public bool isPass;
    public float winrate = -1f;
    public float scoreLead = 0f;
    public bool hasAnalysis;
    public string comment = "";
}

[System.Serializable]
public class MoveBranchData
{
    public int branchId;
    public string name;
    public int startPly;
    public bool isExpanded = true;
    public string branchComment = "";
    public List<BranchStepData> steps = new List<BranchStepData>();
}

[System.Serializable]
public class MoveBranchesCatalog
{
    public List<MoveBranchData> branches = new List<MoveBranchData>();
}

// 运行自举：场景无需任何手工布置，按 Play 即进入主界面。
// 棋盘木纹、网格、星位、棋子、标记全部程序化生成。
public partial class GoGame : MonoBehaviour
{
    public static int ResolveKifuSaveHandicap(bool restoredSession, int restoredHandicap, bool studyMode, int configuredHandicap)
    {
        if (restoredSession) return Mathf.Max(0, restoredHandicap);
        return studyMode ? 0 : Mathf.Max(0, configuredHandicap);
    }

    public static BranchStepData CreateUnanalyzedBranchStep(int step, int x, int y, int color, bool isPass)
    {
        return new BranchStepData
        {
            step = step,
            x = x,
            y = y,
            color = color,
            isPass = isPass,
            winrate = -1f,
            scoreLead = 0f,
            hasAnalysis = false
        };
    }

    public static bool IsReviewAnalysisTargetCurrent(
        int requestToken,
        int activeToken,
        bool targetInBranch,
        int targetBranchId,
        int currentBranchId,
        int targetBranchStep,
        int currentBranchStep,
        int targetPly,
        int currentPly,
        bool currentInBranch)
    {
        if (requestToken != activeToken || targetInBranch != currentInBranch || targetPly != currentPly)
            return false;
        if (!targetInBranch)
            return currentBranchId < 0 && currentBranchStep < 0;
        return targetBranchId >= 0
            && targetBranchId == currentBranchId
            && targetBranchStep == currentBranchStep;
    }

    GoRules rules;
    int boardSize = 19;
    bool vsAI = false;
    bool inStudyMode = false;
    bool reviewingKifu = false;
    KifuRecord reviewRec;
    KifuRecord resumedKifuMetadata;
    bool isResumedKifuSession;
    int resumedKifuHandicap;
    int resumedReviewPly;
    string reviewLogCsv = "";
    int reviewPly, reviewTotal;
    GameObject btnUndo, btnPass, btnResign, btnClear, btnScore, btnReviewPrev, btnReviewNext, btnReviewEnd;
    Text homeBtnLabel;
    GoClockController clockController;
    GoClockView blackClockView, whiteClockView;
    GameObject resignDialog, scoreDialog, timeoutDialog;
    Text timeoutDialogResult;
    Text studySaveStatusText;
    bool aiBusy = false;
    int gameEpoch;
    bool inMenu = true;

    // KataGo 复盘分析与变化推演状态
    bool aiReviewEnabled = false;
    bool isAnalyzingFullGame = false;
    KataPositionAnalysis curPosAnalysis;
    KataGameAnalysis gameAnalysis;
    Coroutine curAnalysisCo, fullAnalysisCo;
    int reviewAnalysisToken;
    int hoveredCandidateIdx = -1;
    bool inBranchMode = false;
    int branchPlyStart = 0;
    int branchMoveCount = 0;
    float branchLastWinrate = -1f;

    // 推演变化与批注系统
    List<MoveBranchData> reviewBranches = new List<MoveBranchData>();
    int curBranchIdx = -1;
    int curBranchStepIdx = -1;
    Text btnCurBranchLabel;

    Dictionary<int, string> moveComments = new Dictionary<int, string>();
    GameObject commentModuleRoot;
    Text commentModuleTitle;
    InputField commentInputField;
    Text commentSaveIndicator;
    Coroutine commentSaveTipCo;

    GameObject reviewAiRoot;
    Text winrateInfoText;
    RawImage winrateChartImg;
    Texture2D winrateChartTex;
    Text btnAiReviewLabel;
    Text btnFullAnalyzeLabel;
    GameObject btnExitBranch;
    GameObject btnBranchUndo;
    GameObject btnStepAi;
    GameObject btnRestoreReview;

    RectTransform barBlackRt, barWhiteRt;
    GameObject winrateBarRoot;
    Text winrateBarText;
    Text candInfoText;

    const int MaxCandidates = 5;
    const int MaxPvSteps = 15;
    Sprite candBadgeSpr;
    GameObject candRoot;
    GameObject[] candBadges = new GameObject[MaxCandidates];
    TextMesh[] candTexts = new TextMesh[MaxCandidates];
    SpriteRenderer[] candSrs = new SpriteRenderer[MaxCandidates];
    int[] candIdxs = new int[MaxCandidates];
    int activeCandCount = 0;

    GameObject pvRoot;
    SpriteRenderer[] pvStoneSrs = new SpriteRenderer[MaxPvSteps];
    TextMesh[] pvTextMeshes = new TextMesh[MaxPvSteps];

    const int StudyBoardSize = 19;
    const string StudySaveKey = GoSaveManager.StudySaveKey;
    static string StudySavePath => GoSaveManager.StudySavePath;
    GoAiController aiController;

    // 人机对弈开局设置（进入对局前选定，局内不可改）
    int sizeChoice = 2;      // 0=9路 1=13路 2=19路
    int colorChoice = 0;     // 0=执黑 1=执白 2=猜先
    float komi = 7.5f;
    int handicap = 0;
    int opponentChoice = 1; // 0=本地引擎 1=KataGo 2=大模型
    GoTimeControl timeControl = GoTimeControl.FromPreset(
        GoTimePreset.Unlimited, GoByoyomiPreset.None);
    int humanColor = GoRules.BLACK;
    int aiColor => GoRules.Opp(humanColor);
    List<Button> optOpponent;
    List<Button> optMainTime, optByoyomi;
    InputField llmKeyInput;
    InputField customMainMinutesInput, customByoyomiPeriodsInput, customByoyomiSecondsInput;
    GameObject customMainTimeRoot, customByoyomiRoot;
    Text timeValidationText, byoyomiValidationText;
    string customMainMinutesText = "", customByoyomiPeriodsText = "", customByoyomiSecondsText = "";
    bool customMainMinutesDirty, customByoyomiPeriodsDirty, customByoyomiSecondsDirty;

    Camera cam;
    Transform boardRoot;
    GoBoardSkin boardSkin;
    public enum GoMarkType
    {
        None = 0,
        Cross = 1,
        Triangle = 2,
        Square = 3,
        Circle = 4
    }

    GoMarkType activeMarkTool = GoMarkType.None;
    int[] boardMarks;
    SpriteRenderer[,] markSr;
    SpriteRenderer ghostMarkSr;
    Sprite markCrossSpr, markTriangleSpr, markSquareSpr, markCircleSpr;
    List<Button> optMarkButtons = new List<Button>();

    SpriteRenderer ghostSr, markerSr;
    Sprite stoneBlack, stoneWhite;
    Font numberFont;

    Font font;
    Text turnText, msgText, moveLogText;
    ScrollRect moveLogScroll;
    GameObject scorePopup;
    Text scorePopupTitle;
    GameObject kifuForm;
    InputField kifuTitle, kifuBlack, kifuWhite, kifuBlackRank, kifuWhiteRank, kifuDesc;
    Text kifuTimeText, kifuResultText;
    string lastResultLine = "";
    System.DateTime gameStartedAt;
    bool kifuDraftReady;
    string draftTitle, draftBlack, draftWhite, draftBlackRank, draftWhiteRank, draftDesc;
    GameObject studyBoardUi;
    Text studyTerritoryText;
    static readonly Color MoveNumOnBlack = Color.white;
    static readonly Color MoveNumOnWhite = new Color(0.08f, 0.08f, 0.08f);
    bool showMoveNums, showCoords, showTerritory;
    Coroutine msgCo;

    // 主界面
    GameObject menuCanvas, mainPanel, aiSetupPanel, historyPanel, historyDetail, settingsPanel;
    Sprite restoredMenuTitle, restoredMenuPrimary, restoredMenuSecondary, restoredSettingsIcon, restoredVolumeIcon, restoredMuteIcon;
    Font restoredMenuFont;
    RectTransform menuSafeArea;
    GameObject historyDeleteDialog;
    GameObject settingsButtonRoot;
    InputField historySearch;
    Transform historyListContent;
    Text historyDetailText;
    Text historyCountText;
    Text historyDeleteText;
    KifuRecord historyDetailRecord;
    string pendingDeleteKifuId;
    bool editingKifu;
    KifuRecord editingKifuRecord;
    List<Button> historyBoardOptions, historyModeOptions, historySortOptions;
    int historyBoardSizeFilter;
    string historyModeFilter = "";
    KifuSort historySort = KifuSort.PlayedAtDescending;
    List<Button> optSize, optColor;
    Text komiValText, handiValText;

    GoAudioManager audioManager;
    Slider quickMasterVolumeSlider;
    Button quickMuteButton;
    Image quickMuteIcon;
    Slider settingsMasterSlider, settingsMusicSlider, settingsSfxSlider, settingsWarningSlider;
    Text settingsKataStatusText;
    Toggle settingsMuteToggle, settingsReduceMotionToggle;
    Toggle settingsMoveNumbersToggle, settingsCoordinatesToggle, settingsTerritoryToggle;
    static readonly string ReduceMotionKey = "Qiyi.ReduceMotion";
    readonly List<GameObject> settingsSectionRoots = new List<GameObject>();
    readonly List<Button> settingsSectionButtons = new List<Button>();
    readonly Dictionary<string, Sprite> settingsArt = new Dictionary<string, Sprite>();

    GoUiTheme uiTheme;
    GoUiFactory uiFactory;
    GoUiNavigator uiNavigator;
    GameObject uiAppRoot, sceneBackgroundLayer, mainCanvasLayer, modalCanvasLayer, transitionLayer;
    GameObject gameUiPanel;
    Transform gameUiContent;
    Text reviewPositionInfoText, pvInfoText;
    int lastResponsiveWidth = -1, lastResponsiveHeight = -1;

    static readonly Color Ink = new Color(0.91f, 0.86f, 0.77f);
    static readonly Color Dim = new Color(0.61f, 0.55f, 0.43f);
    static readonly Color Accent = new Color(0.88f, 0.66f, 0.25f);
    static readonly Color BtnBg = new Color(0.26f, 0.21f, 0.14f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        new GameObject("GoGame").AddComponent<GoGame>();
    }

    void Awake()
    {
        audioManager = gameObject.AddComponent<GoAudioManager>();
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        numberFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Segoe UI", "Calibri", "Microsoft YaHei" }, 48);
        if (numberFont == null) numberFont = font;
        if (numberFont != null)
            numberFont.RequestCharactersInTexture("0123456789", 42, FontStyle.Normal);

        cam = Camera.main;
        if (cam == null)
        {
            var co = new GameObject("Main Camera");
            co.tag = "MainCamera";
            cam = co.AddComponent<Camera>();
        }
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.11f, 0.09f, 0.07f);
        cam.transform.position = new Vector3(0f, 0f, -10f);

        candBadgeSpr = MakeBadgeSprite();
        markCrossSpr = MakeMarkCrossSprite();
        markTriangleSpr = MakeMarkTriangleSprite();
        markSquareSpr = MakeMarkSquareSprite();
        markCircleSpr = MakeMarkCircleSprite();

        rules = new GoRules(boardSize);
        aiController = gameObject.AddComponent<GoAiController>();
        CreateUiShell();
        BuildBoard();
        BuildUI();
        BuildMenu();
        ApplyResponsiveLayout();
        ShowMainMenu();
    }

    /* ---------------- 对局流程 ---------------- */

    void StartStudyGame()
    {
        InvalidateAiTurn();
        StopPlayClock();
        ClearResumedKifuSession();
        audioManager?.PlaySfx("button_confirm");
        audioManager?.PlayMusic("bgm_study");
        inStudyMode = true;
        vsAI = false;
        boardSize = StudyBoardSize;
        showMoveNums = true;
        StopAllCoroutines();

        if (TryLoadStudySave(out var data) && rules.ImportState(data))
        {
            rules.ResumeRecording();
            rules.EnsureMoveAtForDisplay();
            Flash("已恢复打谱进度");
        }
        else
        {
            rules.Reset(boardSize);
            rules.Komi = komi;
            if (PlayerPrefs.HasKey(StudySaveKey))
                Flash("存档损坏，已重新开始");
        }

        EnterGame();
    }

    void StartAiGame()
    {
        InvalidateAiTurn();
        StopPlayClock();
        string startNotice = null;
        SyncTimeControlFromInputs();
        if (!timeControl.TryValidate(out string timeError))
        {
            SetTimeValidationMessage(timeError);
            Flash("请先修正时间设置");
            return;
        }
        if (opponentChoice == 2 && !GoLlmAi.IsConfigured())
        {
            if (llmKeyInput != null) GoLlmAi.ApiKey = llmKeyInput.text;
            if (!GoLlmAi.IsConfigured())
            {
                Flash("请先填写大模型密钥（xAI / OpenAI 兼容）");
                return;
            }
        }
        if (opponentChoice == 1 && !GoKataGo.IsAvailable())
        {
            opponentChoice = 0;
            if (optOpponent != null) PaintOptions(optOpponent, opponentChoice);
            startNotice = "未找到 KataGo，已切换本地引擎";
        }
        ClearResumedKifuSession();
        audioManager?.PlaySfx("button_confirm");
        audioManager?.PlayMusic("bgm_study");
        inStudyMode = false;
        vsAI = true;
        showMoveNums = true;
        boardSize = sizeChoice == 0 ? 9 : sizeChoice == 1 ? 13 : 19;
        humanColor = colorChoice == 0 ? GoRules.BLACK
                   : colorChoice == 1 ? GoRules.WHITE
                   : (Random.value < 0.5f ? GoRules.BLACK : GoRules.WHITE);

        StopAllCoroutines();
        rules.Reset(boardSize);
        rules.Komi = komi;
        if (handicap > 0) rules.SetupHandicap(HandicapPoints(boardSize, handicap));

        StartPlayClock();

        EnterGame();
        string colorNotice = colorChoice == 2
            ? "猜先结果：你" + (humanColor == GoRules.BLACK ? "执黑" : "执白")
            : humanColor == GoRules.BLACK ? "你执黑" : "你执白";
        Flash(string.IsNullOrEmpty(startNotice) ? colorNotice : colorNotice + "\n" + startNotice);
        if (opponentChoice == 1) Flash("电脑由 KataGo 对弈");
        else if (opponentChoice == 2) Flash("电脑由大模型选点");
        if (rules.Result == null && rules.Phase == GoPhase.Play && rules.Turn == aiColor)
            StartCoroutine(AiTurn());
    }

    void EnterGame()
    {
        if (!vsAI) StopPlayClock();
        BuildBoard();
        inMenu = false;
        reviewingKifu = false;
        menuCanvas.SetActive(false);
        if (gameUiPanel != null) gameUiPanel.SetActive(true);
        if (boardRoot != null) boardRoot.gameObject.SetActive(true);
        if (uiNavigator != null)
            uiNavigator.Show(vsAI ? GoUiPage.Playing : GoUiPage.Study);
        gameStartedAt = System.DateTime.Now;
        lastResultLine = "";
        kifuDraftReady = false;
        if (kifuForm != null) kifuForm.SetActive(false);
        if (studySaveStatusText != null)
            studySaveStatusText.text = inStudyMode ? "自动保存：已保存" : "";
        Refresh();
    }

    void StartPlayClock()
    {
        StopPlayClock();
        clockController = new GoClockController();
        clockController.TimedOut += OnClockTimedOut;
        clockController.Start(timeControl, rules.Turn);
        if (blackClockView != null) blackClockView.Bind(clockController, GoRules.BLACK);
        if (whiteClockView != null) whiteClockView.Bind(clockController, GoRules.WHITE);
        UpdateClockPauseState();
    }

    void StopPlayClock()
    {
        if (clockController == null) return;
        clockController.TimedOut -= OnClockTimedOut;
        clockController.Stop();
        clockController = null;
        if (blackClockView != null) blackClockView.Bind(null, GoRules.BLACK);
        if (whiteClockView != null) whiteClockView.Bind(null, GoRules.WHITE);
    }

    void ClearResumedKifuSession()
    {
        resumedKifuMetadata = null;
        isResumedKifuSession = false;
        resumedKifuHandicap = 0;
        resumedReviewPly = 0;
    }

    void OnClockTimedOut(int loserColor)
    {
        if (rules == null || rules.Result != null) return;
        InvalidateAiTurn();
        rules.FinishByTimeout(loserColor);
        StopPlayClock();
        SaveTerminalDraft();
        Refresh();
        ShowTimeoutDialog(loserColor);
    }

    void UpdateClockPauseState()
    {
        if (clockController == null) return;
        bool blocked = inMenu || reviewingKifu || rules == null
            || rules.Phase != GoPhase.Play || rules.Result != null
            || (scorePopup != null && scorePopup.activeSelf)
            || (kifuForm != null && kifuForm.activeSelf)
            || (resignDialog != null && resignDialog.activeSelf)
            || (scoreDialog != null && scoreDialog.activeSelf)
            || (timeoutDialog != null && timeoutDialog.activeSelf)
            || (studyArtDialog != null && studyArtDialog.activeSelf);
        if (blocked) clockController.Pause();
        else clockController.Resume();
        bool blackActive = clockController.IsRunning && clockController.ActiveColor == GoRules.BLACK;
        bool whiteActive = clockController.IsRunning && clockController.ActiveColor == GoRules.WHITE;
        if (blackClockView != null) blackClockView.Render(clockController.GetSnapshot(GoRules.BLACK), blackActive);
        if (whiteClockView != null) whiteClockView.Render(clockController.GetSnapshot(GoRules.WHITE), whiteActive);
    }

    bool TryLoadStudySave(out GoStateData data)
    {
        return GoSaveManager.TryLoadStudySave(out data, out _);
    }

    void SaveStudyProgress()
    {
        if (!inStudyMode || reviewingKifu) return;
        if (!GoSaveManager.SaveStudyProgress(rules, out string err))
        {
            if (studySaveStatusText != null) studySaveStatusText.text = "自动保存：保存失败";
            Debug.LogWarning("自动保存打谱进度失败: " + err);
        }
        else if (studySaveStatusText != null)
        {
            studySaveStatusText.text = "自动保存：已保存";
        }
    }

    void InvalidateAiTurn()
    {
        gameEpoch++;
        aiBusy = false;
        aiController?.Cancel();
    }

    void ReturnToMainMenu()
    {
        InvalidateAiTurn();
        StopPlayClock();
        HideScorePopup();
        ClearResumedKifuSession();
        if (reviewingKifu)
        {
            if (isAnalyzingFullGame) { GoKataGo.CancelAnalysis(); isAnalyzingFullGame = false; }
            if (fullAnalysisCo != null) { StopCoroutine(fullAnalysisCo); fullAnalysisCo = null; }
            InvalidatePositionAnalysis();
            inBranchMode = false;
            curBranchIdx = -1;
            curBranchStepIdx = -1;
            activeMarkTool = GoMarkType.None;
            SyncMarkToolButtons();
            if (ghostMarkSr != null) ghostMarkSr.enabled = false;
            if (boardMarks != null) System.Array.Clear(boardMarks, 0, boardMarks.Length);
            HideCandidateBadges();
            if (commentModuleRoot != null) commentModuleRoot.SetActive(false);
            reviewingKifu = false;
            inStudyMode = false;
            vsAI = false;
            StopAllCoroutines();
            ShowHistoryPanel();
            return;
        }
        if (inStudyMode) SaveStudyProgress();
        inStudyMode = false;
        vsAI = false;
        StopAllCoroutines();
        GoKataGo.Shutdown();
        ShowMainMenu();
    }

    void ShowMainMenu()
    {
        audioManager?.PlayMusic("bgm_menu");
        InvalidateAiTurn();
        StopPlayClock();
        ClearResumedKifuSession();
        if (audioManager != null) audioManager.SavePrefs();
        inMenu = true;
        reviewingKifu = false;
        if (gameUiPanel != null) gameUiPanel.SetActive(false);
        if (boardRoot != null) boardRoot.gameObject.SetActive(false);
        menuCanvas.SetActive(true);
        if (uiNavigator != null) uiNavigator.Show(GoUiPage.MainMenu);
        mainPanel.SetActive(true);
        aiSetupPanel.SetActive(false);
        if (historyPanel != null) historyPanel.SetActive(false);
        if (historyDetail != null) historyDetail.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        SetVolumeQuickVisible(true);
        SetSettingsButtonVisible(true);
        if (kifuForm != null) kifuForm.SetActive(false);
        if (ghostSr != null) ghostSr.enabled = false;
        RefreshStudyVisuals();
    }

    void ShowAiSetup()
    {
        if (uiNavigator != null) uiNavigator.Show(GoUiPage.AiSetup);
        mainPanel.SetActive(false);
        if (historyPanel != null) historyPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        SetVolumeQuickVisible(false);
        SetSettingsButtonVisible(false);
        aiSetupPanel.SetActive(true);
        SyncAiSetup();
    }

    void ShowHistoryPanel()
    {
        inMenu = true;
        menuCanvas.SetActive(true);
        if (uiNavigator != null) uiNavigator.Show(GoUiPage.History);
        mainPanel.SetActive(false);
        aiSetupPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        SetVolumeQuickVisible(false);
        SetSettingsButtonVisible(false);
        historyPanel.SetActive(true);
        if (historyDetail != null) historyDetail.SetActive(false);
        if (historySearch != null) historySearch.text = "";
        historyBoardSizeFilter = 0;
        historyModeFilter = "";
        historySort = KifuSort.PlayedAtDescending;
        if (historyBoardOptions != null) PaintOptions(historyBoardOptions, 0);
        if (historyModeOptions != null) PaintOptions(historyModeOptions, 0);
        if (historySortOptions != null) PaintOptions(historySortOptions, 0);
        RebuildHistoryList();
        if (ghostSr != null) ghostSr.enabled = false;
        RefreshStudyVisuals();
    }

    static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void HumanPlay(int x, int y)
    {
        if (reviewingKifu || aiBusy || inMenu || rules.Result != null || rules.Phase != GoPhase.Play) return;
        if (vsAI && rules.Turn != humanColor) return;
        if (!rules.Play(x, y, out string msg)) { Flash(msg); return; }
        audioManager?.PlaySfx("stone_place");
        clockController?.SetTurn(rules.Turn);
        AfterMove();
    }

    void HumanPass()
    {
        if (reviewingKifu) { ReviewSeek(reviewPly + 1); return; }
        if (aiBusy || inMenu || rules.Result != null || rules.Phase != GoPhase.Play) return;
        if (vsAI && rules.Turn != humanColor) return;
        rules.Pass();
        clockController?.SetTurn(rules.Turn);
        Flash(rules.Phase == GoPhase.Scoring ? "双方停一手，进入数子：点击棋子可标记死子"
                                             : (rules.Turn == GoRules.BLACK ? "黑" : "白") + "方停一手");
        AfterMove();
    }

    void AfterMove()
    {
        Refresh();
        if (inStudyMode) SaveStudyProgress();
        if (vsAI && rules.Result == null && rules.Phase == GoPhase.Play && rules.Turn == aiColor)
            StartCoroutine(AiTurn());
    }

    IEnumerator AiTurn()
    {
        if (aiController == null) aiController = gameObject.AddComponent<GoAiController>();
        if (inMenu || !vsAI || rules == null || rules.Result != null
            || rules.Phase != GoPhase.Play || rules.Turn != aiColor) yield break;

        int requestEpoch = gameEpoch;
        GoRules requestRules = rules;
        int requestAiColor = aiColor;
        GoAiController requestController = aiController;
        aiBusy = true;
        try
        {
            Refresh();
            yield return null;
            if (!IsCurrentAiTurn(requestEpoch, requestRules, requestAiColor, requestController))
                yield break;

            GoMoveResult result = null;
            string notice = null;

            yield return requestController.RequestAiMove(
                requestRules,
                requestAiColor,
                opponentChoice,
                boardSize,
                handicap,
                HandicapPoints,
                (res, fallback) =>
                {
                    result = res;
                    notice = fallback;
                });

            if (!IsCurrentAiTurn(requestEpoch, requestRules, requestAiColor, requestController))
                yield break;

            if (!string.IsNullOrEmpty(notice)) Flash(notice);

            if (result != null && result.success)
            {
                if (result.isResign)
                {
                    rules.ResignBy(aiColor);
                    StopPlayClock();
                    SaveTerminalDraft();
                    ShowResultPopup(rules.Result);
                }
                else if (result.isPass)
                {
                    rules.Pass();
                    clockController?.SetTurn(rules.Turn);
                    string who = opponentChoice == 1 ? "KataGo" : opponentChoice == 2 ? "电脑" : "AI";
                    Flash(rules.Phase == GoPhase.Scoring ? "双方停一手，进入数子" : who + " 停一手");
                }
                else if (!rules.Play(result.x, result.y, out string msg))
                {
                    Flash(msg);
                }
                else
                {
                    audioManager?.PlaySfx("stone_place");
                    clockController?.SetTurn(rules.Turn);
                }
            }
            else if (result != null && !string.IsNullOrEmpty(result.error))
            {
                Flash("AI 无法落子: " + result.error);
            }
        }
        finally
        {
            if (requestEpoch == gameEpoch && requestRules == rules && requestController == aiController)
            {
                aiBusy = false;
                Refresh();
            }
        }
    }

    bool IsCurrentAiTurn(int requestEpoch, GoRules requestRules, int requestAiColor,
        GoAiController requestController)
    {
        return requestEpoch == gameEpoch && requestRules == rules
            && requestController == aiController && !inMenu && vsAI && !reviewingKifu
            && rules != null && rules.Phase == GoPhase.Play && rules.Result == null
            && rules.Turn == requestAiColor;
    }

    void Undo()
    {
        if (reviewingKifu) { ReviewSeek(reviewPly - 1); return; }
        if (aiBusy || inMenu) return;
        aiController?.Cancel();
        int steps = vsAI ? 2 : 1;
        if (!rules.Undo(steps)) { Flash("没有可悔的棋"); return; }
        clockController?.SetTurn(rules.Turn);
        Flash("已悔棋");
        Refresh();
        if (vsAI && rules.Phase == GoPhase.Play && rules.Turn == aiColor)
            StartCoroutine(AiTurn());
    }

    static int[] HandicapPoints(int n, int k) => GoRules.HandicapPoints(n, k);

    /* ---------------- 输入 ---------------- */

    void Update()
    {
        if (cam == null || rules == null) return;
        if (Screen.width != lastResponsiveWidth || Screen.height != lastResponsiveHeight)
            ApplyResponsiveLayout();
        UpdateStudyArtLayout();
        UpdateClockPauseState();
        clockController?.Tick(Time.unscaledDeltaTime);
        if (inMenu)
        {
            boardSkin?.ShowHoverCoordinate(0, 0, false);
            if (ghostSr != null) ghostSr.enabled = false;
            if (ghostMarkSr != null) ghostMarkSr.enabled = false;
            return;
        }
        Vector3 w = cam.ScreenToWorldPoint(Input.mousePosition);
        int x = Mathf.RoundToInt(w.x), y = Mathf.RoundToInt(w.y);
        bool inside = x >= 0 && y >= 0 && x < boardSize && y < boardSize
                      && Mathf.Abs(w.x - x) < 0.48f && Mathf.Abs(w.y - y) < 0.48f;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        boardSkin?.ShowHoverCoordinate(x, y, inside && !overUI);

        bool canHover = !reviewingKifu && inside && !overUI && rules.Phase == GoPhase.Play && rules.Result == null
                        && !aiBusy && rules.Board[rules.Idx(x, y)] == GoRules.EMPTY
                        && (!vsAI || rules.Turn == humanColor);
        ghostSr.enabled = canHover;
        if (canHover)
        {
            boardSkin.SetPointSprite(ghostSr, rules.Turn == GoRules.BLACK ? stoneBlack : stoneWhite);
            ghostSr.transform.position = new Vector3(x, y, 0f);
        }

        if (inside && !overUI && reviewingKifu)
        {
            if (activeMarkTool != GoMarkType.None)
            {
                ghostSr.enabled = false;
                if (ghostMarkSr != null)
                {
                    ghostMarkSr.enabled = true;
                    boardSkin.SetMarkerSprite(ghostMarkSr, GetMarkSprite(activeMarkTool));
                    ghostMarkSr.transform.position = new Vector3(x, y, 0f);
                    int stone = rules.Board[rules.Idx(x, y)];
                    ghostMarkSr.color = GetGhostMarkColor(stone);
                }
            }
            else
            {
                if (ghostMarkSr != null) ghostMarkSr.enabled = false;
                bool canReviewHover = rules.Board[rules.Idx(x, y)] == GoRules.EMPTY;
                ghostSr.enabled = canReviewHover;
                if (canReviewHover)
                {
                    boardSkin.SetPointSprite(ghostSr, rules.Turn == GoRules.BLACK ? stoneBlack : stoneWhite);
                    ghostSr.transform.position = new Vector3(x, y, 0f);
                }
            }

            if (activeCandCount > 0 && curPosAnalysis != null && curPosAnalysis.candidates != null && curPosAnalysis.candidates.Count > 0)
            {
                int hovered = -1;
                int targetIdx = rules.Idx(x, y);
                for (int i = 0; i < activeCandCount; i++)
                {
                    if (candIdxs[i] == targetIdx) { hovered = i; break; }
                }
                int wanted = hovered >= 0 ? hovered : 0;
                if (wanted != hoveredCandidateIdx)
                {
                    hoveredCandidateIdx = wanted;
                    ShowPv(curPosAnalysis.candidates[wanted]);
                    UpdateCandidateDetailText(curPosAnalysis.candidates[wanted]);
                }
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (activeMarkTool != GoMarkType.None)
                {
                    ToggleMarkAt(x, y, activeMarkTool);
                }
                else
                {
                    if (rules.Board[rules.Idx(x, y)] == GoRules.EMPTY)
                    {
                        PlayBranchMove(x, y);
                    }
                }
            }
            else if (Input.GetMouseButtonDown(1))
            {
                int targetIdx = rules.Idx(x, y);
                if (boardMarks != null && targetIdx >= 0 && targetIdx < boardMarks.Length && boardMarks[targetIdx] != 0)
                {
                    RemoveMarkAt(x, y);
                    Flash("已清除该位置标记");
                }
            }
            return;
        }

        if (ghostMarkSr != null) ghostMarkSr.enabled = false;

        if (inside && !overUI && Input.GetMouseButtonDown(0) && !reviewingKifu)
        {
            boardSkin.NotifyPointClick(x, y);
        }
    }

    void HandleBoardPointClick(int x, int y)
    {
        if (reviewingKifu || inMenu || rules == null) return;
        if (rules.Phase == GoPhase.Scoring) { rules.ToggleDead(x, y); Refresh(); }
        else HumanPlay(x, y);
    }

    void OnBoardToolbarStateChanged(bool moveNumbers, bool coordinates, bool territory)
    {
        showMoveNums = moveNumbers;
        showCoords = coordinates;
        showTerritory = territory;
        RefreshStudyVisuals();
        Refresh();
    }

    /* ---------------- 棋盘渲染 ---------------- */

    void BuildBoard()
    {
        if (boardSkin == null) boardSkin = gameObject.AddComponent<GoBoardSkin>();
        boardSkin.Build(boardSize, transform, numberFont, HandleBoardPointClick);
        // Build() clears the previous toolbar subscription with the old board
        // hierarchy; install the current GoGame callback after reconstruction.
        boardSkin.OnToolbarStateChanged = OnBoardToolbarStateChanged;
        boardRoot = boardSkin.BoardRoot;
        stoneBlack = boardSkin.BlackStoneSprite;
        stoneWhite = boardSkin.WhiteStoneSprite;
        ghostSr = boardSkin.GhostRenderer;
        ghostMarkSr = boardSkin.GhostMarkRenderer;
        markerSr = boardSkin.LastMoveRenderer;
        markSr = boardSkin.MarkRenderers;
        int n = boardSize;
        if (boardMarks == null || boardMarks.Length != n * n)
        {
            boardMarks = new int[n * n];
        }
        cam.orthographicSize = (n + 1) / 2f + 0.7f;
        cam.transform.position = new Vector3((n - 1) / 2f, (n - 1) / 2f, -10f);
        if (reviewingKifu) BuildAnalysisOverlays();
        UpdateBoardMarksVisuals();
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float l2 = ab.sqrMagnitude;
        if (l2 < 1e-6f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
        Vector2 proj = a + t * ab;
        return Vector2.Distance(p, proj);
    }

    Sprite MakeMarkCrossSprite()
    {
        int s = 128;
        float cx = s * 0.5f, cy = s * 0.5f;
        float d = s * 0.28f;
        Vector2 a1 = new Vector2(cx - d, cy - d), b1 = new Vector2(cx + d, cy + d);
        Vector2 a2 = new Vector2(cx - d, cy + d), b2 = new Vector2(cx + d, cy - d);
        float hw = s * 0.045f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            float dist = Mathf.Min(DistToSegment(p, a1, b1), DistToSegment(p, a2, b2));
            float alpha = Mathf.Clamp01(hw + 0.8f - dist);
            px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * alpha));
        }
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px); tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }

    Sprite MakeMarkTriangleSprite()
    {
        int s = 128;
        float cx = s * 0.5f, cy = s * 0.46f;
        float r = s * 0.32f;
        Vector2 p0 = new Vector2(cx, cy + r * 1.05f);
        Vector2 p1 = new Vector2(cx - r * 0.90f, cy - r * 0.52f);
        Vector2 p2 = new Vector2(cx + r * 0.90f, cy - r * 0.52f);
        float hw = s * 0.045f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            float dist = Mathf.Min(DistToSegment(p, p0, p1), Mathf.Min(DistToSegment(p, p1, p2), DistToSegment(p, p2, p0)));
            float alpha = Mathf.Clamp01(hw + 0.8f - dist);
            px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * alpha));
        }
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px); tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }

    Sprite MakeMarkSquareSprite()
    {
        int s = 128;
        float cx = s * 0.5f, cy = s * 0.5f;
        float hs = s * 0.25f;
        Vector2 p0 = new Vector2(cx - hs, cy - hs);
        Vector2 p1 = new Vector2(cx + hs, cy - hs);
        Vector2 p2 = new Vector2(cx + hs, cy + hs);
        Vector2 p3 = new Vector2(cx - hs, cy + hs);
        float hw = s * 0.045f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            float dist = Mathf.Min(
                Mathf.Min(DistToSegment(p, p0, p1), DistToSegment(p, p1, p2)),
                Mathf.Min(DistToSegment(p, p2, p3), DistToSegment(p, p3, p0))
            );
            float alpha = Mathf.Clamp01(hw + 0.8f - dist);
            px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * alpha));
        }
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px); tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }

    Sprite MakeMarkCircleSprite()
    {
        int s = 128;
        float cx = s * 0.5f, cy = s * 0.5f;
        float r0 = s * 0.26f;
        float hw = s * 0.045f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            float dist = Mathf.Abs(Vector2.Distance(p, new Vector2(cx, cy)) - r0);
            float alpha = Mathf.Clamp01(hw + 0.8f - dist);
            px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * alpha));
        }
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px); tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }

    Sprite GetMarkSprite(GoMarkType type)
    {
        switch (type)
        {
            case GoMarkType.Cross: return markCrossSpr;
            case GoMarkType.Triangle: return markTriangleSpr;
            case GoMarkType.Square: return markSquareSpr;
            case GoMarkType.Circle: return markCircleSpr;
            default: return null;
        }
    }

    Color GetMarkColor(int stoneColor)
    {
        if (stoneColor == GoRules.BLACK) return new Color(1f, 1f, 1f, 0.95f);
        if (stoneColor == GoRules.WHITE) return new Color(0.10f, 0.08f, 0.06f, 0.95f);
        return new Color(0.88f, 0.22f, 0.16f, 0.95f);
    }

    Color GetGhostMarkColor(int stoneColor)
    {
        if (stoneColor == GoRules.BLACK) return new Color(1f, 1f, 1f, 0.55f);
        if (stoneColor == GoRules.WHITE) return new Color(0.10f, 0.08f, 0.06f, 0.55f);
        return new Color(0.88f, 0.22f, 0.16f, 0.55f);
    }

    void SelectMarkTool(GoMarkType tool)
    {
        if (activeMarkTool == tool)
        {
            activeMarkTool = GoMarkType.None;
            Flash("已切换为【落子推演】模式");
        }
        else
        {
            activeMarkTool = tool;
            if (tool == GoMarkType.None) Flash("已切换为【落子推演】模式");
            else if (tool == GoMarkType.Cross) Flash("已选择【✕ 标记】工具，点击棋子可作标记");
            else if (tool == GoMarkType.Triangle) Flash("已选择【▲ 标记】工具，点击棋子可作标记");
            else if (tool == GoMarkType.Square) Flash("已选择【■ 标记】工具，点击棋子可作标记");
            else if (tool == GoMarkType.Circle) Flash("已选择【● 标记】工具，点击棋子可作标记");
        }
        SyncMarkToolButtons();
    }

    void SyncMarkToolButtons()
    {
        for (int i = 0; i < optMarkButtons.Count; i++)
        {
            var btn = optMarkButtons[i];
            if (btn == null) continue;
            var img = btn.GetComponent<Image>();
            if (img == null) continue;
            bool selected = (int)activeMarkTool == i;
            img.color = selected ? Accent : BtnBg;
            var txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                txt.color = selected ? new Color(0.12f, 0.09f, 0.05f) : Ink;
            }
        }
    }

    void ToggleMarkAt(int x, int y, GoMarkType type)
    {
        if (rules == null || boardMarks == null) return;
        int idx = rules.Idx(x, y);
        if (idx < 0 || idx >= boardMarks.Length) return;

        if (boardMarks[idx] == (int)type)
        {
            boardMarks[idx] = 0;
        }
        else
        {
            boardMarks[idx] = (int)type;
        }
        UpdateBoardMarksVisuals();
        RefreshStudyVisuals();
    }

    void RemoveMarkAt(int x, int y)
    {
        if (rules == null || boardMarks == null) return;
        int idx = rules.Idx(x, y);
        if (idx < 0 || idx >= boardMarks.Length) return;

        if (boardMarks[idx] != 0)
        {
            boardMarks[idx] = 0;
            UpdateBoardMarksVisuals();
            RefreshStudyVisuals();
        }
    }

    void ClearAllMarks()
    {
        if (boardMarks != null)
        {
            System.Array.Clear(boardMarks, 0, boardMarks.Length);
        }
        UpdateBoardMarksVisuals();
        RefreshStudyVisuals();
        Flash("已清空所有标记");
    }

    void UpdateBoardMarksVisuals()
    {
        if (markSr == null || rules == null || boardMarks == null) return;
        int n = boardSize;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int i = rules.Idx(x, y);
            int markVal = (i < boardMarks.Length) ? boardMarks[i] : 0;
            var sr = markSr[x, y];
            if (sr == null) continue;
            if (markVal == 0)
            {
                sr.enabled = false;
            }
            else
            {
                sr.enabled = true;
                boardSkin.SetMarkerSprite(sr, GetMarkSprite((GoMarkType)markVal));
                int stone = rules.Board[i];
                sr.color = GetMarkColor(stone);
            }
        }
    }

    /* ---------------- 对局面板 ---------------- */

    void CreateUiShell()
    {
        uiAppRoot = new GameObject("GoAppRoot");
        uiAppRoot.transform.SetParent(transform, false);

        uiTheme = GoUiTheme.CreateRuntimeDefault(font);
        uiFactory = uiAppRoot.AddComponent<GoUiFactory>();
        uiFactory.Theme = uiTheme;
        uiNavigator = uiAppRoot.AddComponent<GoUiNavigator>();
        uiNavigator.ReduceMotion = PlayerPrefs.GetInt(ReduceMotionKey, 0) != 0;

        sceneBackgroundLayer = CreateCanvasLayer("SceneBackground", -10, false);
        var background = new GameObject("InkBackground");
        background.transform.SetParent(sceneBackgroundLayer.transform, false);
        var backgroundImage = background.AddComponent<Image>();
        backgroundImage.sprite = LoadArtSprite(
            "Assets/Art/UI/Background/qiyi_ink_room_2048.png",
            GoRuntimeAssetManifest.InkRoomBackgroundKey);
        backgroundImage.color = backgroundImage.sprite != null
            ? new Color(1f, 1f, 1f, 0.40f) : uiTheme.Ink900;
        backgroundImage.raycastTarget = false;
        var backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;

        AddBackgroundSprite(sceneBackgroundLayer.transform, "MountainSilhouette",
            "Assets/Art/UI/Background/qiyi_mountain_silhouette_2048.png",
            GoRuntimeAssetManifest.MountainSilhouetteKey, 0.22f);
        AddBackgroundSprite(sceneBackgroundLayer.transform, "BambooShadow",
            "Assets/Art/UI/Background/qiyi_bamboo_shadow_1024.png",
            GoRuntimeAssetManifest.BambooShadowKey, 0.18f);

        mainCanvasLayer = CreateCanvasLayer("MainCanvas", 0, true);
        modalCanvasLayer = CreateCanvasLayer("ModalCanvas", 100, true);
        transitionLayer = CreateCanvasLayer("TransitionLayer", 200, false);
        var transitionGroup = transitionLayer.AddComponent<CanvasGroup>();
        transitionGroup.alpha = 1f;
        transitionGroup.interactable = false;
        transitionGroup.blocksRaycasts = false;
    }

    void AddBackgroundSprite(Transform parent, string objectName, string assetPath,
        string resourceKey, float alpha)
    {
        var sprite = LoadArtSprite(assetPath, resourceKey);
        if (sprite == null) return;

        var go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        image.raycastTarget = false;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    Sprite LoadArtSprite(string assetPath, string resourceKey)
    {
        Sprite sprite = null;
        if (GoRuntimeAssetManifest.Instance != null)
            sprite = GoRuntimeAssetManifest.Instance.GetBackground(resourceKey);
        if (sprite == null) sprite = Resources.Load<Sprite>(resourceKey);
        if (sprite == null) sprite = Resources.Load<Sprite>("Art/" + resourceKey);

        return sprite;
    }

    GameObject CreateCanvasLayer(string objectName, int sortingOrder, bool raycast)
    {
        var layer = new GameObject(objectName);
        layer.transform.SetParent(uiAppRoot.transform, false);
        var canvas = layer.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = layer.AddComponent<CanvasScaler>();
        ConfigureCanvasScaler(scaler);
        if (raycast) layer.AddComponent<GraphicRaycaster>();
        return layer;
    }

    void BuildUI()
    {
        var cgo = mainCanvasLayer;
        var canvas = cgo.GetComponent<Canvas>();

        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        var pgo = new GameObject("Panel");
        gameUiPanel = pgo;
        pgo.transform.SetParent(canvas.transform, false);
        var pimg = pgo.AddComponent<Image>();
        pimg.color = new Color(0.14f, 0.11f, 0.08f, 0.96f);
        var prt = pgo.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(1f, 0f);
        prt.anchorMax = new Vector2(1f, 1f);
        prt.pivot = new Vector2(1f, 0.5f);
        float safeX = GoUiTheme.SafeAreaMarginCanvasX(Screen.width, Screen.height);
        float safeY = GoUiTheme.SafeAreaMarginCanvasY(Screen.width, Screen.height);
        prt.sizeDelta = new Vector2(GameSidebarWidth(), -2f * safeY);
        prt.anchoredPosition = new Vector2(-safeX, 0f);

        var vlg = pgo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 14, 14);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var pt = pgo.transform;
        gameUiContent = pt;

        MakeText(pt, "落子记录与变化推演", 15, Dim, TextAnchor.MiddleCenter);
        BuildClockUi(pt);
        BuildMoveLogScroll(pt);
        BuildCommentModuleUi(pt);
        turnText = MakeText(pt, "", 14, Ink, TextAnchor.MiddleCenter);
        msgText = MakeText(pt, "", 14, Accent, TextAnchor.MiddleCenter);
        studySaveStatusText = MakeText(pt, "", 13, new Color(0.435f, 0.604f, 0.510f, 1f), TextAnchor.MiddleCenter);
        studySaveStatusText.gameObject.SetActive(false);

        var operationRow = new GameObject("OperationRow");
        operationRow.transform.SetParent(pt, false);
        var operationLayout = operationRow.AddComponent<LayoutElement>();
        operationLayout.preferredHeight = 42f;
        operationLayout.minHeight = 42f;
        var operationGroup = operationRow.AddComponent<HorizontalLayoutGroup>();
        operationGroup.spacing = 4f;
        operationGroup.childAlignment = TextAnchor.MiddleCenter;
        operationGroup.childControlWidth = true;
        operationGroup.childControlHeight = true;
        operationGroup.childForceExpandWidth = true;
        operationGroup.childForceExpandHeight = true;

        btnUndo = MakeButton(operationRow.transform, "悔棋", 42f, Undo).gameObject;
        btnPass = MakeButton(operationRow.transform, "停一手", 42f, HumanPass).gameObject;
        btnScore = MakeButton(operationRow.transform, "数目", 42f, OpenScorePopup).gameObject;
        btnResign = MakeButton(operationRow.transform, "认输", 42f, HumanResign).gameObject;
        AttachButtonIcon(btnUndo.GetComponent<Button>(), GoIconCatalog.Undo);
        AttachButtonIcon(btnPass.GetComponent<Button>(), GoIconCatalog.Pass);
        AttachButtonIcon(btnScore.GetComponent<Button>(), GoIconCatalog.Score);
        AttachButtonIcon(btnResign.GetComponent<Button>(), GoIconCatalog.Resign);
        btnClear = null;
        BuildTerminalDialogs(canvas.transform);
        var home = MakeButton(pt, "返回主界面", 42f, ReturnToMainMenu);
        homeBtnLabel = home.GetComponentInChildren<Text>();

        BuildScorePopup(modalCanvasLayer.transform);
        BuildKifuForm(canvas.transform);
        BuildStudyBoardUi(canvas.transform);
        BuildStudyArtUi(canvas.transform, pgo);
    }

    void BuildClockUi(Transform parent)
    {
        var row = new GameObject("ClockRow");
        row.transform.SetParent(parent, false);
        var rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = 62f;
        rowLayout.minHeight = 62f;
        var group = row.AddComponent<HorizontalLayoutGroup>();
        group.spacing = 8f;
        group.childAlignment = TextAnchor.MiddleCenter;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = true;

        var black = new GameObject("BlackClock");
        black.transform.SetParent(row.transform, false);
        blackClockView = black.AddComponent<GoClockView>();

        var white = new GameObject("WhiteClock");
        white.transform.SetParent(row.transform, false);
        whiteClockView = white.AddComponent<GoClockView>();
    }

    void BuildMoveLogScroll(Transform parent)
    {
        var scrollGo = new GameObject("MoveLog");
        scrollGo.transform.SetParent(parent, false);
        var le = scrollGo.AddComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.minHeight = 110f;
        le.preferredHeight = 150f;
        var bg = scrollGo.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.06f, 0.04f, 0.95f);
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        scrollGo.AddComponent<RectMask2D>();

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vrt = viewport.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(8f, 8f);
        vrt.offsetMax = new Vector2(-8f, -8f);
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var crt = content.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 0f);
        var cv = content.AddComponent<VerticalLayoutGroup>();
        cv.childAlignment = TextAnchor.UpperLeft;
        cv.childControlWidth = true;
        cv.childControlHeight = true;
        cv.childForceExpandWidth = true;
        cv.childForceExpandHeight = false;
        var fit = content.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        moveLogText = MakeText(content.transform, "暂无落子", 15, Ink, TextAnchor.UpperLeft);
        moveLogScroll = scroll;
        scroll.viewport = vrt;
        scroll.content = crt;
    }

    void BuildCommentModuleUi(Transform parent)
    {
        commentModuleRoot = new GameObject("CommentModule");
        commentModuleRoot.transform.SetParent(parent, false);
        var vlg = commentModuleRoot.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var le = commentModuleRoot.AddComponent<LayoutElement>();
        le.preferredHeight = 78f;
        le.minHeight = 78f;

        var headerRow = new GameObject("CommentHeader");
        headerRow.transform.SetParent(commentModuleRoot.transform, false);
        var hlg = headerRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 4f;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        var hle = headerRow.AddComponent<LayoutElement>();
        hle.preferredHeight = 22f;

        commentModuleTitle = MakeText(headerRow.transform, "📝 批注：实战", 13, Ink, TextAnchor.MiddleLeft);
        var tLe = commentModuleTitle.gameObject.AddComponent<LayoutElement>();
        tLe.flexibleWidth = 1f;
        tLe.minWidth = 110f;

        commentSaveIndicator = MakeText(headerRow.transform, "", 13, new Color(0.45f, 0.82f, 0.45f), TextAnchor.MiddleRight);
        var iLe = commentSaveIndicator.gameObject.AddComponent<LayoutElement>();
        iLe.preferredWidth = 45f;

        var btnSave = MakeButton(headerRow.transform, "保存", 22f, OnCommentSaveClicked);
        var sLe = btnSave.GetComponent<LayoutElement>();
        sLe.preferredWidth = 38f;
        sLe.flexibleWidth = 0f;
        var saveImg = btnSave.GetComponent<Image>();
        if (saveImg != null) saveImg.color = new Color(0.18f, 0.38f, 0.22f, 1f);

        var btnClear = MakeButton(headerRow.transform, "清空", 22f, OnCommentClearClicked);
        var cLe = btnClear.GetComponent<LayoutElement>();
        cLe.preferredWidth = 38f;
        cLe.flexibleWidth = 0f;
        var clearImg = btnClear.GetComponent<Image>();
        if (clearImg != null) clearImg.color = new Color(0.35f, 0.16f, 0.14f, 1f);

        var inputGo = new GameObject("CommentInputField");
        inputGo.transform.SetParent(commentModuleRoot.transform, false);
        var inputLe = inputGo.AddComponent<LayoutElement>();
        inputLe.preferredHeight = 52f;
        inputLe.minHeight = 52f;
        inputLe.flexibleWidth = 1f;
        var bg = inputGo.AddComponent<Image>();
        bg.color = new Color(0.09f, 0.07f, 0.05f, 0.95f);

        var ifComp = inputGo.AddComponent<InputField>();
        ifComp.lineType = InputField.LineType.MultiLineNewline;
        ifComp.selectionColor = new Color(0.88f, 0.66f, 0.25f, 0.4f);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(inputGo.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6f, 4f);
        trt.offsetMax = new Vector2(-6f, -4f);
        var textComp = textGo.AddComponent<Text>();
        textComp.font = font;
        textComp.fontSize = 13;
        textComp.color = Ink;
        textComp.supportRichText = false;
        textComp.alignment = TextAnchor.UpperLeft;

        var phGo = new GameObject("Placeholder");
        phGo.transform.SetParent(inputGo.transform, false);
        var phrt = phGo.AddComponent<RectTransform>();
        phrt.anchorMin = Vector2.zero;
        phrt.anchorMax = Vector2.one;
        phrt.offsetMin = new Vector2(6f, 4f);
        phrt.offsetMax = new Vector2(-6f, -4f);
        var phComp = phGo.AddComponent<Text>();
        phComp.font = font;
        phComp.fontSize = 13;
        phComp.color = new Color(0.5f, 0.45f, 0.38f, 0.7f);
        phComp.text = "在此输入当前步的分析与批注…";
        phComp.fontStyle = FontStyle.Italic;
        phComp.alignment = TextAnchor.UpperLeft;

        ifComp.textComponent = textComp;
        ifComp.placeholder = phComp;
        ifComp.onEndEdit.AddListener(text => SaveCurrentComment(text));

        commentInputField = ifComp;
        commentModuleRoot.SetActive(false);
    }

    void SyncCommentModule()
    {
        if (commentInputField == null || commentModuleTitle == null) return;

        if (inBranchMode && curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count)
        {
            var br = reviewBranches[curBranchIdx];
            int stepIdx = (curBranchStepIdx >= 0 && curBranchStepIdx < br.steps.Count) ? curBranchStepIdx : (br.steps.Count - 1);
            if (stepIdx >= 0 && stepIdx < br.steps.Count)
            {
                var st = br.steps[stepIdx];
                string who = st.color == GoRules.BLACK ? "黑" : "白";
                string coord = st.isPass ? "停一手" : GoKataGo.Coord(st.x, st.y);
                commentModuleTitle.text = $"📝 批注：{br.name} 第{st.step}步（{who}{coord}）";
                commentInputField.text = st.comment ?? "";
            }
            else
            {
                commentModuleTitle.text = $"📝 批注：{br.name}（起始前）";
                commentInputField.text = br.branchComment ?? "";
            }
        }
        else
        {
            commentModuleTitle.text = reviewPly == 0 ? "📝 批注：开局局面" : $"📝 批注：实战第 {reviewPly} 手";
            if (moveComments.TryGetValue(reviewPly, out string c))
                commentInputField.text = c ?? "";
            else
                commentInputField.text = "";
        }
    }

    void OnCommentSaveClicked()
    {
        if (commentInputField == null) return;
        SaveCurrentComment(commentInputField.text);
        ShowCommentSaveTip("已保存");
    }

    void OnCommentClearClicked()
    {
        if (commentInputField != null) commentInputField.text = "";
        SaveCurrentComment("");
        ShowCommentSaveTip("已清空");
    }

    void SaveCurrentComment(string text)
    {
        text = (text ?? "").Trim();
        if (inBranchMode && curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count)
        {
            var br = reviewBranches[curBranchIdx];
            int stepIdx = (curBranchStepIdx >= 0 && curBranchStepIdx < br.steps.Count) ? curBranchStepIdx : (br.steps.Count - 1);
            if (stepIdx >= 0 && stepIdx < br.steps.Count)
            {
                br.steps[stepIdx].comment = text;
            }
            else
            {
                br.branchComment = text;
            }
        }
        else
        {
            if (string.IsNullOrEmpty(text))
                moveComments.Remove(reviewPly);
            else
                moveComments[reviewPly] = text;
        }

        SaveReviewCommentsAndBranches();
        RefreshReviewMoveLog();
    }

    void ShowCommentSaveTip(string tip)
    {
        if (commentSaveIndicator == null) return;
        commentSaveIndicator.text = tip;
        if (commentSaveTipCo != null) StopCoroutine(commentSaveTipCo);
        commentSaveTipCo = StartCoroutine(DoHideCommentSaveTip());
    }

    IEnumerator DoHideCommentSaveTip()
    {
        yield return new WaitForSeconds(2.5f);
        if (commentSaveIndicator != null) commentSaveIndicator.text = "";
        commentSaveTipCo = null;
    }

    void SaveReviewCommentsAndBranches()
    {
        if (reviewRec == null) return;

        var cData = new MoveCommentsData();
        foreach (var kvp in moveComments)
        {
            if (!string.IsNullOrEmpty(kvp.Value))
                cData.items.Add(new MoveCommentItem { ply = kvp.Key, text = kvp.Value });
        }
        string commentsJson = cData.items.Count > 0 ? JsonUtility.ToJson(cData) : "";

        var bData = new MoveBranchesCatalog();
        bData.branches.AddRange(reviewBranches);
        string branchesJson = reviewBranches.Count > 0 ? JsonUtility.ToJson(bData) : "";

        reviewRec.commentsJson = commentsJson;
        reviewRec.branchesJson = branchesJson;
        KifuDatabase.UpdateCommentsAndBranches(reviewRec.id, commentsJson, branchesJson);
    }

    void NewVariation()
    {
        if (!reviewingKifu) return;
        if (inBranchMode)
        {
            ExitBranchMode(false);
        }
        InvalidatePositionAnalysis();

        var br = new MoveBranchData
        {
            branchId = reviewBranches.Count + 1,
            name = $"变化 {reviewBranches.Count + 1}",
            startPly = reviewPly,
            isExpanded = true,
            steps = new List<BranchStepData>()
        };
        reviewBranches.Add(br);
        curBranchIdx = reviewBranches.Count - 1;
        curBranchStepIdx = -1;
        inBranchMode = true;
        branchPlyStart = reviewPly;
        branchMoveCount = 0;
        branchLastWinrate = -1f;

        if (btnExitBranch != null) btnExitBranch.SetActive(true);
        if (btnBranchUndo != null) btnBranchUndo.SetActive(true);

        Flash($"已新建【{br.name}】（以实战第 {reviewPly} 手为起点），请在盘面落子");
        SyncCommentModule();
        RefreshReviewMoveLog();
        RefreshReviewAnalysisDisplay();
        SaveReviewCommentsAndBranches();
    }

    void ToggleVariationsExpand()
    {
        if (reviewBranches.Count == 0)
        {
            Flash("暂无推演变化");
            return;
        }
        bool anyCollapsed = reviewBranches.Exists(b => !b.isExpanded);
        foreach (var b in reviewBranches) b.isExpanded = anyCollapsed;
        RefreshReviewMoveLog();
        Flash(anyCollapsed ? "已展开全部推演变化 [-]" : "已收起全部推演变化 [+]");
    }

    void CycleLoadVariation()
    {
        if (reviewBranches.Count == 0)
        {
            Flash("暂无推演变化，在盘面落子即可开启推演");
            return;
        }
        int nextIdx = (curBranchIdx + 1) % reviewBranches.Count;
        LoadVariation(nextIdx);
    }

    void LoadVariation(int idx)
    {
        if (idx < 0 || idx >= reviewBranches.Count) return;
        var br = reviewBranches[idx];
        ReviewSeek(br.startPly, false);

        inBranchMode = true;
        curBranchIdx = idx;
        branchPlyStart = br.startPly;
        branchMoveCount = 0;
        rules.ResumeRecording();

        for (int i = 0; i < br.steps.Count; i++)
        {
            var st = br.steps[i];
            if (st.isPass) rules.Pass();
            else rules.Play(st.x, st.y, out _);
            branchMoveCount++;
        }

        curBranchStepIdx = br.steps.Count - 1;
        branchLastWinrate = br.steps.Count > 0 && br.steps[br.steps.Count - 1].hasAnalysis
            ? br.steps[br.steps.Count - 1].winrate : -1f;

        if (btnExitBranch != null) btnExitBranch.SetActive(true);
        if (btnBranchUndo != null) btnBranchUndo.SetActive(true);

        rules.EnsureMoveAtForDisplay();
        Refresh();
        RefreshReviewAnalysisDisplay();
        if (aiReviewEnabled && curBranchStepIdx >= 0) RequestPositionAnalysis();
        Flash($"已载入【{br.name}】（第 {br.startPly} 手起 · 共 {br.steps.Count} 步推演）");
    }

    void DeleteCurrentVariation()
    {
        if (reviewBranches.Count == 0)
        {
            Flash("暂无可删除的变化");
            return;
        }
        int delIdx = (curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count) ? curBranchIdx : (reviewBranches.Count - 1);
        string delName = reviewBranches[delIdx].name;

        if (inBranchMode && curBranchIdx == delIdx)
        {
            ExitBranchMode();
        }

        reviewBranches.RemoveAt(delIdx);
        for (int i = 0; i < reviewBranches.Count; i++)
        {
            reviewBranches[i].branchId = i + 1;
            reviewBranches[i].name = $"变化 {i + 1}";
        }

        curBranchIdx = -1;
        curBranchStepIdx = -1;
        SaveReviewCommentsAndBranches();
        RefreshReviewMoveLog();
        Flash($"已删除【{delName}】");
    }

    void BuildScorePopup(Transform canvasRoot)
    {
        scorePopup = BuildDialogRoot(canvasRoot, "ScorePopup", out Transform card);
        BuildStudyResultCard(card);
        scorePopup.SetActive(false);
    }

    void BuildTerminalDialogs(Transform canvasRoot)
    {
        resignDialog = BuildDialogRoot(modalCanvasLayer.transform, "ResignDialog", out Transform resignCard);
        BuildStudyResignCard(resignCard);
        resignDialog.SetActive(false);

        scoreDialog = BuildDialogRoot(modalCanvasLayer.transform, "ScoreDialog", out Transform scoreCard);
        BuildStudyScoreCard(scoreCard);
        scoreDialog.SetActive(false);

        timeoutDialog = BuildDialogRoot(canvasRoot, "TimeoutDialog", out Transform timeoutCard);
        var timeoutLayout = timeoutCard.gameObject.AddComponent<VerticalLayoutGroup>();
        timeoutLayout.padding = new RectOffset(28, 28, 24, 22);
        timeoutLayout.spacing = 12f;
        timeoutLayout.childAlignment = TextAnchor.MiddleCenter;
        timeoutLayout.childControlWidth = true;
        timeoutLayout.childControlHeight = true;
        timeoutLayout.childForceExpandWidth = true;
        timeoutLayout.childForceExpandHeight = false;
        MakeText(timeoutCard, "对局结束", 26, Ink, TextAnchor.MiddleCenter);
        timeoutDialogResult = MakeText(timeoutCard, "", 18, Accent, TextAnchor.MiddleCenter);
        var timeoutButtons = MakeRow(timeoutCard, 44f);
        MakeButton(timeoutButtons, "保存棋谱", 44f, TimeoutSaveKifu);
        MakeButton(timeoutButtons, "再来一局", 44f, TimeoutReplay);
        MakeButton(timeoutButtons, "返回主界面", 44f, TimeoutReturnHome);
        timeoutDialog.SetActive(false);
    }

    GameObject BuildDialogRoot(Transform canvasRoot, string name, out Transform card)
    {
        var root = new GameObject(name);
        root.transform.SetParent(canvasRoot, false);
        var overlay = root.AddComponent<Image>();
        overlay.color = new Color(0.05f, 0.04f, 0.03f, 0.78f);
        overlay.raycastTarget = true;
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;

        var cardGo = new GameObject("Card");
        cardGo.transform.SetParent(root.transform, false);
        var cardImage = cardGo.AddComponent<Image>();
        cardImage.color = new Color(0.16f, 0.13f, 0.09f, 1f);
        var cardRect = cardGo.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(500f, 250f);
        card = cardGo.transform;
        return root;
    }

    void RefreshMoveLog()
    {
        if (moveLogText == null || rules == null) return;
        if (reviewingKifu)
        {
            RefreshReviewMoveLog();
            return;
        }
        int n = rules.LoggedMoveCount;
        if (n == 0)
        {
            moveLogText.text = "暂无落子";
            return;
        }
        var sb = new System.Text.StringBuilder(n * 24);
        for (int i = 0; i < n; i++)
        {
            if (!rules.GetLoggedMove(i, out int x, out int y, out bool pass)) continue;
            if (i > 0) sb.Append('\n');
            if (pass) sb.Append($"第{i + 1:000}棋停一手");
            else sb.Append($"第{i + 1:000}棋下在（{ColLabel(x)}，{RowLabel(y)}）");
        }
        moveLogText.text = sb.ToString();
        if (moveLogScroll != null)
            Canvas.ForceUpdateCanvases();
        if (moveLogScroll != null)
            moveLogScroll.verticalNormalizedPosition = 0f;
    }

    void ClearBoardKeepPlaying()
    {
        if (reviewingKifu) return;
        InvalidateAiTurn();
        HideScorePopup();
        ClearResumedKifuSession();
        WipeBoardState();
        BuildBoard();
        if (vsAI) StartPlayClock();
        Refresh();
        Flash("棋盘已清空");
        if (vsAI && rules.Result == null && rules.Phase == GoPhase.Play && rules.Turn == aiColor)
            StartCoroutine(AiTurn());
    }

    void WipeBoardState()
    {
        rules.Reset(boardSize);
        GoSaveManager.ClearStudySave();
    }

    void HumanResign()
    {
        if (reviewingKifu || aiBusy || inMenu || rules.Result != null) return;
        ShowResignDialog();
    }

    void OpenScorePopup()
    {
        ShowScoreDialog();
    }

    void ShowResignDialog()
    {
        if (resignDialog == null || reviewingKifu || inMenu || rules == null
            || rules.Result != null || rules.Phase != GoPhase.Play) return;
        resignDialog.SetActive(true);
        UpdateClockPauseState();
    }

    void ConfirmResignDialog()
    {
        if (rules == null || rules.Result != null) return;
        InvalidateAiTurn();
        rules.ResignBy(rules.Turn);
        if (resignDialog != null) resignDialog.SetActive(false);
        StopPlayClock();
        SaveTerminalDraft();
        Refresh();
        ShowResultPopup(rules.Result);
    }

    void CancelResignDialog()
    {
        if (resignDialog != null) resignDialog.SetActive(false);
        UpdateClockPauseState();
    }

    void ShowScoreDialog()
    {
        if (scoreDialog == null || reviewingKifu || inMenu || rules == null
            || rules.Result != null) return;
        rules.ComputeScore(out float blackScore, out float whiteScore,
            out _, out _, out _, out _);
        RefreshStudyScoreCard(blackScore, whiteScore);
        scoreDialog.SetActive(true);
        UpdateClockPauseState();
    }

    void ConfirmScoreDialog()
    {
        if (rules == null || rules.Result != null) return;
        if (rules.Phase != GoPhase.Play && rules.Phase != GoPhase.Scoring) return;
        InvalidateAiTurn();
        if (rules.Phase == GoPhase.Play) rules.BeginScoring();
        rules.ConfirmEnd();
        if (scoreDialog != null) scoreDialog.SetActive(false);
        StopPlayClock();
        SaveTerminalDraft();
        Refresh();
        ShowResultPopup(rules.Result);
    }

    void CancelScoreDialog()
    {
        if (scoreDialog != null) scoreDialog.SetActive(false);
        UpdateClockPauseState();
    }

    void ShowTimeoutDialog(int loserColor)
    {
        lastResultLine = GoResultFormatter.FormatTimeout(loserColor);
        if (timeoutDialog == null)
        {
            ShowResultPopup(lastResultLine);
            return;
        }
        timeoutDialogResult.text = lastResultLine;
        timeoutDialog.SetActive(true);
        UpdateClockPauseState();
        audioManager?.PlaySfx("game_end");
    }

    void TimeoutSaveKifu()
    {
        if (timeoutDialog != null) timeoutDialog.SetActive(false);
        FillKifuForm();
        if (kifuForm != null) kifuForm.SetActive(true);
    }

    void TimeoutReplay()
    {
        if (timeoutDialog != null) timeoutDialog.SetActive(false);
        ClearBoardKeepPlaying();
    }

    void TimeoutReturnHome()
    {
        if (timeoutDialog != null) timeoutDialog.SetActive(false);
        ScorePopupReturnHome();
    }

    void SaveTerminalDraft()
    {
        if (!inStudyMode || reviewingKifu || rules == null) return;
        if (!GoSaveManager.SaveStudyTerminalProgress(rules, out string err))
        {
            if (studySaveStatusText != null) studySaveStatusText.text = "自动保存：保存失败";
            Debug.LogWarning("自动保存打谱终局失败: " + err);
        }
        else if (studySaveStatusText != null)
        {
            studySaveStatusText.text = "自动保存：已保存";
        }
    }

    void ShowResultPopup(string title)
    {
        if (scorePopup == null) return;
        StopPlayClock();
        audioManager?.PlaySfx("game_end");
        lastResultLine = title;
        bool draw = title == "双方和棋" || title == "和棋";
        scorePopupTitle.text = draw ? "和棋" : title;
        scorePopupTitle.gameObject.SetActive(!draw);
        studyResultDrawArt.gameObject.SetActive(draw);
        scorePopup.SetActive(true);
        if (kifuForm != null) kifuForm.SetActive(false);
    }

    void HideScorePopup()
    {
        HideStudyArtDialog();
        if (scorePopup != null) scorePopup.SetActive(false);
        if (kifuForm != null) kifuForm.SetActive(false);
        if (resignDialog != null) resignDialog.SetActive(false);
        if (scoreDialog != null) scoreDialog.SetActive(false);
        if (timeoutDialog != null) timeoutDialog.SetActive(false);
        UpdateClockPauseState();
    }

    void ScorePopupReturnHome()
    {
        HideScorePopup();
        WipeBoardState();
        inStudyMode = false;
        vsAI = false;
        ShowMainMenu();
    }

    void ScorePopupReplay()
    {
        HideScorePopup();
        ClearBoardKeepPlaying();
    }

    void ScorePopupSaveKifu()
    {
        if (kifuForm == null) return;
        FillKifuForm();
        scorePopup.SetActive(false);
        kifuForm.SetActive(true);
    }

    void FillKifuForm()
    {
        if (!kifuDraftReady)
        {
            if (resumedKifuMetadata != null)
            {
                draftTitle = resumedKifuMetadata.title;
                draftBlack = resumedKifuMetadata.blackName;
                draftWhite = resumedKifuMetadata.whiteName;
                draftBlackRank = resumedKifuMetadata.blackRank;
                draftWhiteRank = resumedKifuMetadata.whiteRank;
                draftDesc = resumedKifuMetadata.description;
            }
            else
            {
                draftTitle = inStudyMode ? "打谱" : "人机对弈";
                if (inStudyMode)
                {
                    draftBlack = "黑";
                    draftWhite = "白";
                }
                else
                {
                    draftBlack = humanColor == GoRules.BLACK ? "你" : "AI";
                    draftWhite = humanColor == GoRules.WHITE ? "你" : "AI";
                }
                draftBlackRank = "";
                draftWhiteRank = "";
                draftDesc = "";
            }
            kifuDraftReady = true;
        }
        kifuTitle.text = draftTitle ?? "";
        kifuBlack.text = draftBlack ?? "";
        kifuWhite.text = draftWhite ?? "";
        kifuBlackRank.text = draftBlackRank ?? "";
        kifuWhiteRank.text = draftWhiteRank ?? "";
        kifuDesc.text = draftDesc ?? "";
        kifuTimeText.text = resumedKifuMetadata != null && !string.IsNullOrEmpty(resumedKifuMetadata.playedAt)
            ? resumedKifuMetadata.playedAt
            : gameStartedAt == default
                ? System.DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                : gameStartedAt.ToString("yyyy-MM-dd HH:mm");
        kifuResultText.text = string.IsNullOrEmpty(lastResultLine)
            ? (rules != null && !string.IsNullOrEmpty(rules.Result) ? rules.Result : "未终局")
            : lastResultLine;
    }

    void CaptureKifuDraft()
    {
        draftTitle = kifuTitle.text;
        draftBlack = kifuBlack.text;
        draftWhite = kifuWhite.text;
        draftBlackRank = kifuBlackRank.text;
        draftWhiteRank = kifuWhiteRank.text;
        draftDesc = kifuDesc.text;
        kifuDraftReady = true;
    }

    void KifuFormBack()
    {
        CaptureKifuDraft();
        kifuForm.SetActive(false);
        if (editingKifu)
        {
            editingKifu = false;
            editingKifuRecord = null;
            ShowHistoryPanel();
        }
        else if (scorePopup != null) scorePopup.SetActive(true);
    }

    void KifuFormConfirm()
    {
        CaptureKifuDraft();
        if (editingKifu && editingKifuRecord != null)
        {
            var updated = CloneKifuRecord(editingKifuRecord);
            updated.title = string.IsNullOrWhiteSpace(draftTitle) ? "未命名对局" : draftTitle.Trim();
            updated.blackName = (draftBlack ?? "").Trim();
            updated.whiteName = (draftWhite ?? "").Trim();
            updated.blackRank = (draftBlackRank ?? "").Trim();
            updated.whiteRank = (draftWhiteRank ?? "").Trim();
            updated.description = (draftDesc ?? "").Trim();
            if (!KifuDatabase.Update(updated))
            {
                Flash("编辑棋谱失败：棋谱不存在或保存失败");
                return;
            }
            editingKifu = false;
            editingKifuRecord = null;
            kifuForm.SetActive(false);
            ShowHistoryPanel();
            Flash("棋谱信息已更新");
            return;
        }
        var rec = new KifuRecord
        {
            title = string.IsNullOrWhiteSpace(draftTitle) ? "未命名对局" : draftTitle.Trim(),
            blackName = (draftBlack ?? "").Trim(),
            whiteName = (draftWhite ?? "").Trim(),
            blackRank = (draftBlackRank ?? "").Trim(),
            whiteRank = (draftWhiteRank ?? "").Trim(),
            description = (draftDesc ?? "").Trim(),
            playedAt = kifuTimeText.text,
            result = kifuResultText.text,
            boardSize = boardSize,
            mode = resumedKifuMetadata != null && !string.IsNullOrEmpty(resumedKifuMetadata.mode)
                ? resumedKifuMetadata.mode : (inStudyMode ? "打谱" : "人机对弈"),
            moveCount = rules != null ? rules.LoggedMoveCount : 0,
            komi = rules != null ? rules.Komi : komi,
            handicap = ResolveKifuSaveHandicap(isResumedKifuSession, resumedKifuHandicap, inStudyMode, handicap),
            moveLogCsv = rules != null ? rules.ExportMoveLogCsv() : "",
            stateJson = rules != null ? JsonUtility.ToJson(rules.ExportState()) : ""
        };
        if (KifuDatabase.TryAdd(rec, out string err))
        {
            kifuForm.SetActive(false);
            if (scorePopup != null) scorePopup.SetActive(false);
            ClearResumedKifuSession();
            Flash("棋谱已保存");
        }
        else
        {
            Flash("保存棋谱失败: " + err);
        }
    }

    static KifuRecord CloneKifuRecord(KifuRecord source)
    {
        if (source == null) return null;
        return new KifuRecord
        {
            schemaVersion = source.schemaVersion,
            id = source.id,
            title = source.title,
            blackName = source.blackName,
            whiteName = source.whiteName,
            blackRank = source.blackRank,
            whiteRank = source.whiteRank,
            description = source.description,
            playedAt = source.playedAt,
            result = source.result,
            boardSize = source.boardSize,
            mode = source.mode,
            moveCount = source.moveCount,
            komi = source.komi,
            handicap = source.handicap,
            moveLogCsv = source.moveLogCsv,
            stateJson = source.stateJson,
            analysisJson = source.analysisJson,
            commentsJson = source.commentsJson,
            branchesJson = source.branchesJson
        };
    }

    void BuildKifuForm(Transform canvasRoot)
    {
        kifuForm = new GameObject("KifuForm");
        kifuForm.transform.SetParent(canvasRoot, false);
        var overlay = kifuForm.AddComponent<Image>();
        overlay.color = new Color(0.05f, 0.04f, 0.03f, 0.78f);
        overlay.raycastTarget = true;
        var ort = kifuForm.GetComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;

        var card = new GameObject("Card");
        card.transform.SetParent(kifuForm.transform, false);
        var cimg = card.AddComponent<Image>();
        cimg.color = new Color(0.16f, 0.13f, 0.09f, 1f);
        var crt = card.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(560f, 620f);

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 22, 18);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        MakeText(card.transform, "保存棋谱", 26, Ink, TextAnchor.MiddleCenter);
        kifuTitle = MakeLabeledInput(card.transform, "对局名称", "请输入对局名称", 36f, false);
        kifuBlack = MakeLabeledInput(card.transform, "黑方昵称", "黑方昵称", 36f, false);
        kifuBlackRank = MakeLabeledInput(card.transform, "黑方段位", "段位（选填）", 36f, false);
        kifuWhite = MakeLabeledInput(card.transform, "白方昵称", "白方昵称", 36f, false);
        kifuWhiteRank = MakeLabeledInput(card.transform, "白方段位", "段位（选填）", 36f, false);
        kifuTimeText = MakeReadOnlyRow(card.transform, "对局时间");
        kifuResultText = MakeReadOnlyRow(card.transform, "对局结果");
        kifuDesc = MakeLabeledInput(card.transform, "对局描述", "可填写备注", 72f, true);

        var row = MakeRow(card.transform, 44f);
        MakeButton(row, "返回", 40f, KifuFormBack);
        var ok = MakeButton(row, "确定", 40f, KifuFormConfirm);
        ok.GetComponent<Image>().color = Accent;
        StyleAccentLabel(ok);
        kifuForm.SetActive(false);
    }

    void BuildStudyBoardUi(Transform canvasRoot)
    {
        studyBoardUi = new GameObject("StudyBoardUi");
        studyBoardUi.transform.SetParent(canvasRoot, false);
        studyBoardUi.AddComponent<RectTransform>();

        var terrGo = new GameObject("Territory");
        terrGo.transform.SetParent(canvasRoot, false);
        var terrRt = terrGo.AddComponent<RectTransform>();
        terrRt.anchorMin = new Vector2(0f, 0f);
        terrRt.anchorMax = new Vector2(0f, 0f);
        terrRt.pivot = new Vector2(0f, 0f);
        terrRt.anchoredPosition = new Vector2(12f, 58f);
        terrRt.sizeDelta = new Vector2(620f, 68f);
        studyTerritoryText = terrGo.AddComponent<Text>();
        studyTerritoryText.font = font;
        studyTerritoryText.fontSize = 14;
        studyTerritoryText.color = Ink;
        studyTerritoryText.alignment = TextAnchor.UpperLeft;
        studyTerritoryText.horizontalOverflow = HorizontalWrapMode.Wrap;
        studyTerritoryText.verticalOverflow = VerticalWrapMode.Overflow;
        studyTerritoryText.text = "";

        studyBoardUi.SetActive(false);
        terrGo.SetActive(false);
        studyTerritoryText.gameObject.SetActive(false);
    }

    void SyncStudyToggleLabels()
    {
        if (studyTerritoryText != null)
            studyTerritoryText.gameObject.SetActive(BoardOverlayOn && showTerritory);
        if (boardSkin != null)
        {
            boardSkin.SetToolbarState(showMoveNums, showCoords, showTerritory);
            boardSkin.SetToolbarVisible(BoardOverlayOn && !StudyArtActive);
        }
    }

    bool BoardOverlayOn => (inStudyMode || reviewingKifu || vsAI) && !inMenu;

    static string ColLabel(int x)
    {
        const string letters = "ABCDEFGHJKLMNOPQRST";
        return x < letters.Length ? letters[x].ToString() : (x + 1).ToString();
    }

    static string RowLabel(int y) => (y + 1).ToString();

    void RefreshStudyVisuals()
    {
        bool on = BoardOverlayOn;
        if (studyBoardUi != null) studyBoardUi.SetActive(on);
        SyncStudyToggleLabels();
        if (on && showTerritory)
        {
            rules.EstimateSituation(out float bs, out float ws,
                out int bt, out int wt, out int bm, out int wm, out int db, out int dw, out int[] owner);
            float diff = bs - ws;
            string lead = diff > 0.05f ? $"黑领先约 {diff:0.0} 目"
                        : diff < -0.05f ? $"白领先约 {-diff:0.0} 目" : "接近均势";
            studyTerritoryText.text =
                $"形势（Bouzy 5/21）  黑 {bs:0.0}    白 {ws:0.0}（含贴目 {rules.Komi:0.0}）\n" +
                $"实地 黑{bt} 白{wt}    厚势 黑{bm} 白{wm}" +
                (db + dw > 0 ? $"    疑死 黑{db} 白{dw}" : "") +
                $"\n{lead}";
        }
        else
        {
            if (studyTerritoryText != null) studyTerritoryText.text = "";
        }
    }

    /* ---------------- 主界面 ---------------- */

    void BuildMenu()
    {
        // A screen-space canvas must be a root canvas. Nesting it under
        // MainCanvas left its RectTransform at the parent's default 100x100,
        // so corner-anchored controls collapsed into the centre of the menu.
        menuCanvas = CreateCanvasLayer("MenuCanvas", 20, true);

        var bg = new GameObject("Bg");
        bg.transform.SetParent(menuCanvas.transform, false);
        var bimg = bg.AddComponent<Image>();
        bimg.sprite = LoadArtSprite(
            "Assets/Art/UI/Background/qiyi_ink_room_2048.png",
            GoRuntimeAssetManifest.InkRoomBackgroundKey);
        bimg.color = bimg.sprite != null
            ? new Color(0.88f, 0.85f, 0.80f, 1f)
            : new Color(0.14f, 0.11f, 0.08f, 1f);
        bimg.raycastTarget = false;
        var brt = bg.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.sizeDelta = Vector2.zero; brt.anchoredPosition = Vector2.zero;

        // The menu has its own opaque background so the board stays hidden.
        // Reapply the decorative layers above it instead of relying on the
        // scene background, which sits behind the game board.
        AddBackgroundSprite(menuCanvas.transform, "MenuMountainSilhouette",
            "Assets/Art/UI/Background/qiyi_mountain_silhouette_2048.png",
            GoRuntimeAssetManifest.MountainSilhouetteKey, 0.32f);
        AddBackgroundSprite(menuCanvas.transform, "MenuBambooShadow",
            "Assets/Art/UI/Background/qiyi_bamboo_shadow_1024.png",
            GoRuntimeAssetManifest.BambooShadowKey, 0.26f);

        var safeArea = new GameObject("SafeArea");
        safeArea.transform.SetParent(menuCanvas.transform, false);
        menuSafeArea = safeArea.AddComponent<RectTransform>();
        menuSafeArea.anchorMin = Vector2.zero;
        menuSafeArea.anchorMax = Vector2.one;
        menuSafeArea.offsetMin = new Vector2(
            GoUiTheme.SafeAreaMarginCanvasX(Screen.width, Screen.height),
            GoUiTheme.SafeAreaMarginCanvasY(Screen.width, Screen.height));
        menuSafeArea.offsetMax = new Vector2(
            -GoUiTheme.SafeAreaMarginCanvasX(Screen.width, Screen.height),
            -GoUiTheme.SafeAreaMarginCanvasY(Screen.width, Screen.height));

        restoredMenuTitle = LoadRestoredMenuSprite("menu_title", new Rect(56f, 40f, 2090f, 640f));
        restoredMenuPrimary = LoadRestoredMenuPlaque("menu_primary", 0.31f, 0.40f);
        restoredMenuSecondary = LoadRestoredMenuPlaque("menu_secondary", 0.27f, 0.49f);
        restoredSettingsIcon = LoadRestoredMenuSprite("menu_settings", new Rect(120f, 120f, 1020f, 1020f));
        restoredVolumeIcon = LoadRestoredMenuSprite("menu_volume", new Rect(256f, 294f, 784f, 630f));
        restoredMuteIcon = LoadRestoredMenuSprite("menu_mute", new Rect(312f, 270f, 704f, 704f));
        restoredMenuFont = Font.CreateDynamicFontFromOSFont(new[] { "STZhongsong", "华文中宋", "Noto Serif CJK SC", "SimSun", "Microsoft YaHei" }, 18);

        mainPanel = new GameObject("MainMenuPage", typeof(RectTransform), typeof(Image));
        mainPanel.transform.SetParent(menuCanvas.transform, false);
        var mainRect = mainPanel.GetComponent<RectTransform>();
        mainRect.anchorMin = mainRect.anchorMax = new Vector2(0.5f, 0.5f);
        mainRect.pivot = new Vector2(0.5f, 0.5f);
        mainRect.sizeDelta = new Vector2(500f, 680f);
        mainRect.anchoredPosition = Vector2.zero;
        var mainPanelImage = mainPanel.GetComponent<Image>();
        mainPanelImage.color = Color.clear;
        mainPanelImage.raycastTarget = false;

        var title = new GameObject("MenuTitle", typeof(RectTransform), typeof(Image));
        title.transform.SetParent(mainPanel.transform, false);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(400f, 110f);
        titleRect.anchoredPosition = new Vector2(0f, -60f);
        var titleImage = title.GetComponent<Image>();
        titleImage.sprite = restoredMenuTitle;
        titleImage.preserveAspect = true;
        titleImage.color = Color.white;
        titleImage.raycastTarget = false;
        if (restoredMenuTitle == null)
        {
            var fallbackTitle = MakeText(title.transform, "棋弈有道", 50, Ink, TextAnchor.MiddleCenter);
            fallbackTitle.rectTransform.anchorMin = Vector2.zero;
            fallbackTitle.rectTransform.anchorMax = Vector2.one;
            fallbackTitle.rectTransform.offsetMin = fallbackTitle.rectTransform.offsetMax = Vector2.zero;
        }
        var subtitle = MakeText(mainPanel.transform, "宋式清雅 · 夜案棋局", 14,
            new Color(0.88f, 0.77f, 0.59f), TextAnchor.MiddleCenter);
        var subtitleFitter = subtitle.GetComponent<ContentSizeFitter>();
        if (subtitleFitter != null) subtitleFitter.enabled = false;
        subtitle.font = restoredMenuFont != null ? restoredMenuFont : font;
        var subtitleRect = subtitle.rectTransform;
        subtitleRect.anchorMin = subtitleRect.anchorMax = new Vector2(0.5f, 1f);
        subtitleRect.pivot = new Vector2(0.5f, 0.5f);
        subtitleRect.sizeDelta = new Vector2(360f, 28f);
        subtitleRect.anchoredPosition = new Vector2(0f, -133f);

        var menuActions = new GameObject("MenuActions", typeof(RectTransform), typeof(VerticalLayoutGroup));
        menuActions.transform.SetParent(mainPanel.transform, false);
        var actionsRect = menuActions.GetComponent<RectTransform>();
        actionsRect.anchorMin = actionsRect.anchorMax = new Vector2(0.5f, 1f);
        actionsRect.pivot = new Vector2(0.5f, 1f);
        actionsRect.anchoredPosition = new Vector2(0f, -184f);
        actionsRect.sizeDelta = new Vector2(370f, 208f);
        var actionsLayout = menuActions.GetComponent<VerticalLayoutGroup>();
        actionsLayout.spacing = 8f;
        actionsLayout.childAlignment = TextAnchor.UpperCenter;
        actionsLayout.childControlWidth = actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = true;
        actionsLayout.childForceExpandHeight = false;
        CreateRestoredMenuButton(menuActions.transform, "打谱", StartStudyGame, true, false);
        CreateRestoredMenuButton(menuActions.transform, "人机对弈", ShowAiSetup);
        CreateRestoredMenuButton(menuActions.transform, "历史棋谱", ShowHistoryPanel);
        CreateRestoredMenuButton(menuActions.transform, "退出", QuitGame);

        BuildAiSetupPanel();

        BuildHistoryPanel();
        BuildHistoryDetail();
        BuildSettingsPanel();
        BuildSettingsButton();
        BuildVolumeQuickControl();
        BuildVersionLabel();
        RegisterUiPages();
    }

    void BuildHistoryPanel()
    {
        historyPanel = new GameObject("HistoryPanel", typeof(RectTransform), typeof(Image));
        historyPanel.transform.SetParent(menuCanvas.transform, false);
        var panelRect = historyPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1120f, 960f);
        historyPanel.GetComponent<Image>().sprite = LoadHistorySprite("paper");
        var title = new GameObject("HistoryArtTitle", typeof(RectTransform), typeof(Image));
        title.transform.SetParent(historyPanel.transform, false);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -62f);
        titleRect.sizeDelta = new Vector2(260f, 70f);
        title.GetComponent<Image>().sprite = LoadHistorySprite("title");
        title.GetComponent<Image>().preserveAspect = true;
        title.GetComponent<Image>().raycastTarget = false;

        historySearch = MakeInputField(historyPanel.transform, "按名称、棋手、结果或备注搜索", 44f, false);
        StyleHistoryInput(historySearch);
        var searchRect = historySearch.GetComponent<RectTransform>();
        searchRect.anchorMin = new Vector2(0f, 1f);
        searchRect.anchorMax = Vector2.one;
        searchRect.offsetMin = new Vector2(64f, -152f);
        searchRect.offsetMax = new Vector2(-64f, -108f);
        historySearch.onValueChanged.AddListener(_ => RebuildHistoryList());

        var filters = new GameObject("HistoryFilters", typeof(RectTransform), typeof(VerticalLayoutGroup));
        filters.transform.SetParent(historyPanel.transform, false);
        var filtersRect = filters.GetComponent<RectTransform>();
        filtersRect.anchorMin = new Vector2(0f, 1f);
        filtersRect.anchorMax = Vector2.one;
        filtersRect.offsetMin = new Vector2(64f, -316f);
        filtersRect.offsetMax = new Vector2(-64f, -168f);
        var filterLayout = filters.GetComponent<VerticalLayoutGroup>();
        filterLayout.spacing = 8f;
        filterLayout.childControlWidth = filterLayout.childControlHeight = true;
        filterLayout.childForceExpandWidth = true;
        filterLayout.childForceExpandHeight = false;
        historyBoardOptions = MakeHistoryOptions(filters.transform, "棋盘尺寸",
            new[] { "全部", "9路", "13路", "19路" }, index =>
            {
                historyBoardSizeFilter = index == 0 ? 0 : index == 1 ? 9 : index == 2 ? 13 : 19;
                RebuildHistoryList();
            });
        historyModeOptions = MakeHistoryOptions(filters.transform, "模式",
            new[] { "全部", "打谱", "人机对弈" }, index =>
            {
                historyModeFilter = index == 0 ? "" : index == 1 ? "打谱" : "人机对弈";
                RebuildHistoryList();
            });
        historySortOptions = MakeHistoryOptions(filters.transform, "排序",
            new[] { "日期新→旧", "日期旧→新", "手数多→少", "结果" }, index =>
            {
                historySort = (KifuSort)Mathf.Clamp(index, 0, 3);
                RebuildHistoryList();
            });
        historyCountText = MakeText(historyPanel.transform, "", 16, Dim, TextAnchor.MiddleLeft);
        StyleHistoryText(historyCountText, 16);
        historyCountText.rectTransform.anchorMin = new Vector2(0f, 1f);
        historyCountText.rectTransform.anchorMax = new Vector2(1f, 1f);
        historyCountText.rectTransform.offsetMin = new Vector2(68f, -344f);
        historyCountText.rectTransform.offsetMax = new Vector2(-68f, -322f);

        historyListContent = BuildScrollBody(historyPanel.transform, "HistoryList");
        var scroll = historyListContent.GetComponentInParent<ScrollRect>();
        var listRect = scroll.GetComponent<RectTransform>();
        listRect.anchorMin = Vector2.zero;
        listRect.anchorMax = Vector2.one;
        listRect.offsetMin = new Vector2(60f, 106f);
        listRect.offsetMax = new Vector2(-60f, -352f);
        scroll.GetComponent<Image>().color = Color.clear;
        var listGroup = historyListContent.GetComponent<VerticalLayoutGroup>();
        listGroup.padding = new RectOffset(8, 8, 8, 16);
        listGroup.spacing = 14f;

        var back = MakeButton(historyPanel.transform, "返回主界面", 48f, ShowMainMenu);
        StyleHistoryButton(back, true);
        var backRect = back.GetComponent<RectTransform>();
        backRect.anchorMin = Vector2.zero;
        backRect.anchorMax = new Vector2(1f, 0f);
        backRect.offsetMin = new Vector2(64f, 40f);
        backRect.offsetMax = new Vector2(-64f, 88f);
        back.GetComponentInChildren<Text>().gameObject.SetActive(false);
        var backArt = new GameObject("ReturnArtLabel", typeof(RectTransform), typeof(Image));
        backArt.transform.SetParent(back.transform, false);
        var backArtRect = backArt.GetComponent<RectTransform>();
        backArtRect.anchorMin = backArtRect.anchorMax = new Vector2(0.5f, 0.5f);
        backArtRect.anchoredPosition = Vector2.zero;
        backArtRect.sizeDelta = new Vector2(190f, 38f);
        backArt.GetComponent<Image>().sprite = LoadSettingsSprite("settings_label_back", new Rect(90f, 70f, 1990f, 590f));
        backArt.GetComponent<Image>().preserveAspect = true;
        backArt.GetComponent<Image>().raycastTarget = false;
        historyPanel.SetActive(false);
    }

    Sprite LoadHistorySprite(string key)
    {
        string cacheKey = "History/" + key;
        if (settingsArt.TryGetValue(cacheKey, out var cached)) return cached;
        var texture = Resources.Load<Texture2D>("History/history_" + key);
        if (texture == null) return null;
        Rect crop;
        switch (key)
        {
            case "paper": crop = new Rect(8f, 18f, 1236f, 1220f); break;
            case "title": crop = new Rect(224f, 132f, 1716f, 440f); break;
            case "option": crop = new Rect(36f, 208f, 2100f, 312f); break;
            case "selected": crop = new Rect(4f, 176f, 2164f, 392f); break;
            case "card": crop = new Rect(16f, 164f, 2140f, 424f); break;
            default: crop = new Rect(68f, 200f, 2032f, 356f); break;
        }
        var sprite = Sprite.Create(texture, crop, new Vector2(0.5f, 0.5f));
        settingsArt[cacheKey] = sprite;
        return sprite;
    }

    void StyleHistoryText(Text text, int size, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        text.font = restoredMenuFont != null ? restoredMenuFont : font;
        text.fontSize = size;
        text.color = new Color(0.26f, 0.16f, 0.10f);
        text.alignment = alignment;
        text.alignByGeometry = true;
        text.supportRichText = false;
        var fitter = text.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;
    }

    void StyleHistoryButton(Button button, bool selected = false, bool danger = false)
    {
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.flexibleHeight = 0f;
        var image = button.GetComponent<Image>();
        image.sprite = LoadHistorySprite(danger ? "delete" : selected ? "selected" : "option");
        image.type = Image.Type.Simple;
        image.color = Color.white;
        var colors = button.colors;
        colors.normalColor = colors.selectedColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.96f, 0.84f);
        colors.pressedColor = new Color(0.85f, 0.75f, 0.59f);
        button.colors = colors;
        var text = button.GetComponentInChildren<Text>();
        if (text == null) return;
        StyleHistoryText(text, 20, TextAnchor.MiddleCenter);
        text.color = selected || danger ? new Color(0.98f, 0.87f, 0.68f) : new Color(0.26f, 0.16f, 0.10f);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(14f, 4f);
        text.rectTransform.offsetMax = new Vector2(-14f, -4f);
    }

    void StyleHistoryInput(InputField input)
    {
        StyleAiSetupInput(input);
        input.GetComponent<Image>().sprite = LoadHistorySprite("option");
    }

    List<Button> MakeHistoryOptions(Transform parent, string caption, string[] labels,
        UnityEngine.Events.UnityAction<int> onPick)
    {
        var row = MakeRow(parent, 44f);
        row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        var label = MakeText(row, caption, 20, Ink, TextAnchor.MiddleLeft);
        StyleHistoryText(label, 20);
        var labelLayout = label.gameObject.AddComponent<LayoutElement>();
        labelLayout.minWidth = labelLayout.preferredWidth = 104f;
        labelLayout.flexibleWidth = 0f;
        var options = MakeOptions(row, labels, onPick);
        var optionsLayout = options[0].transform.parent.GetComponent<LayoutElement>();
        optionsLayout.minHeight = optionsLayout.preferredHeight = 44f;
        optionsLayout.flexibleWidth = 1f;
        foreach (var option in options) StyleHistoryButton(option);
        return options;
    }

    Text MakeHistoryCardLine(Transform parent, string value, int size, float height)
    {
        var text = MakeText(parent, value, size, Ink, TextAnchor.MiddleLeft);
        StyleHistoryText(text, size);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        var layout = text.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = height;
        return text;
    }

    static string ShortHistoryText(string value, int limit)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var characters = System.Globalization.StringInfo.ParseCombiningCharacters(value);
        return characters.Length <= limit ? value : value.Substring(0, characters[limit]) + "…";
    }

    void CreateHistoryCard(Transform parent, KifuRecord record)
    {
        string id = record.id;
        var card = new GameObject("KifuCard", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        card.transform.SetParent(parent, false);
        card.GetComponent<LayoutElement>().minHeight = card.GetComponent<LayoutElement>().preferredHeight = 204f;
        card.GetComponent<Image>().sprite = LoadHistorySprite("card");
        var open = card.AddComponent<Button>();
        open.transition = Selectable.Transition.None;
        open.onClick.AddListener(() => OpenHistoryDetail(FindKifu(id)));
        var group = card.AddComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(32, 32, 14, 14);
        group.spacing = 6f;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        MakeHistoryCardLine(card.transform, ShortHistoryText(string.IsNullOrEmpty(record.title) ? "未命名对局" : record.title, 34), 24, 30f);
        MakeHistoryCardLine(card.transform,
            $"黑方：{ShortHistoryText(record.blackName, 18)}（{ShortHistoryText(record.blackRank, 6)}）    白方：{ShortHistoryText(record.whiteName, 18)}（{ShortHistoryText(record.whiteRank, 6)}）", 18, 26f);
        MakeHistoryCardLine(card.transform,
            $"{record.boardSize}路  ·  {record.moveCount}手  ·  {ShortHistoryText(record.playedAt, 24)}  ·  {record.mode}", 18, 24f);
        MakeHistoryCardLine(card.transform, "结果：" + ShortHistoryText(string.IsNullOrEmpty(record.result) ? "未终局" : record.result, 40), 20, 26f);
        var actions = MakeRow(card.transform, 40f);
        actions.GetComponent<HorizontalLayoutGroup>().spacing = 10f;
        StyleHistoryButton(MakeButton(actions, "打开复盘", 40f, () => OpenKifuReviewById(id)), true);
        StyleHistoryButton(MakeButton(actions, "导出 SGF", 40f, () => ExportKifuSgfById(id)));
        StyleHistoryButton(MakeButton(actions, "编辑信息", 40f, () => BeginEditKifuById(id)));
        StyleHistoryButton(MakeButton(actions, "删除", 40f, () => AskDeleteKifuById(id)), false, true);
    }

    void BuildAiSetupPanel()
    {
        aiSetupPanel = new GameObject("AiSetupPage", typeof(RectTransform), typeof(Image));
        aiSetupPanel.transform.SetParent(menuCanvas.transform, false);
        var panelRect = aiSetupPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(800f, 960f);
        aiSetupPanel.GetComponent<Image>().sprite = LoadAiSetupSprite("paper");

        var title = new GameObject("AiSetupArtTitle", typeof(RectTransform), typeof(Image));
        title.transform.SetParent(aiSetupPanel.transform, false);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -64f);
        titleRect.sizeDelta = new Vector2(256f, 70f);
        title.GetComponent<Image>().sprite = LoadAiSetupSprite("title");
        title.GetComponent<Image>().preserveAspect = true;
        title.GetComponent<Image>().raycastTarget = false;
        var subtitle = MakeText(aiSetupPanel.transform, "开局前选定棋盘与规则，进入对局后不可更改", 18,
            new Color(0.43f, 0.31f, 0.20f), TextAnchor.MiddleCenter);
        StyleAiSetupText(subtitle, 18);
        subtitle.rectTransform.anchorMin = subtitle.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, -112f);
        subtitle.rectTransform.sizeDelta = new Vector2(640f, 24f);

        var content = BuildScrollBody(aiSetupPanel.transform, "AiSetupScroll");
        var scroll = content.GetComponentInParent<ScrollRect>();
        var scrollRect = scroll.GetComponent<RectTransform>();
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(56f, 156f);
        scrollRect.offsetMax = new Vector2(-56f, -140f);
        scroll.GetComponent<Image>().color = Color.clear;
        scroll.viewport.offsetMin = new Vector2(4f, 4f);
        scroll.viewport.offsetMax = new Vector2(-4f, -4f);
        var contentGroup = content.GetComponent<VerticalLayoutGroup>();
        contentGroup.padding = new RectOffset(4, 4, 4, 12);
        contentGroup.spacing = 6f;

        optSize = MakeAiSetupOptions(MakeAiSetupField(content, "棋盘尺寸"),
            new[] { "9路", "13路", "19路" }, i => { sizeChoice = i; SyncAiSetup(); });
        optColor = MakeAiSetupOptions(MakeAiSetupField(content, "执棋"),
            new[] { "黑棋", "白棋", "猜先" }, i => { colorChoice = i; SyncAiSetup(); });
        komiValText = MakeAiSetupStepper(MakeAiSetupField(content, "贴目"),
            () => komi = Mathf.Max(0f, komi - 0.5f),
            () => komi = Mathf.Min(15f, komi + 0.5f));
        handiValText = MakeAiSetupStepper(MakeAiSetupField(content, "让子（黑方预先布子，白方先下）"),
            () => handicap = Mathf.Max(0, handicap - 1),
            () => handicap = Mathf.Min(9, handicap + 1));
        var mainTime = MakeAiSetupField(content, "对局时间");
        optMainTime = MakeAiSetupOptions(mainTime,
            new[] { "无限制", "5分钟", "1小时", "2小时", "自定义" }, SetMainTimePreset);
        customMainTimeRoot = BuildCustomMainTime(mainTime);
        StyleAiSetupCustomTime(customMainTimeRoot);
        var byoyomi = MakeAiSetupField(content, "读秒");
        optByoyomi = MakeAiSetupOptions(byoyomi,
            new[] { "无", "3×30秒", "5×30秒", "5×60秒", "自定义" }, SetByoyomiPreset);
        customByoyomiRoot = BuildCustomByoyomi(byoyomi);
        StyleAiSetupCustomTime(customByoyomiRoot);
        optOpponent = MakeAiSetupOptions(MakeAiSetupField(content, "电脑对手"),
            new[] { "本地引擎", "KataGo", "大模型" }, i => { opponentChoice = i; SyncAiSetup(); });
        llmKeyInput = MakeInputField(MakeAiSetupField(content,
            "大模型密钥（xAI / OpenAI 兼容，仅存本机）"), "粘贴 API Key", 42f, false);
        llmKeyInput.contentType = InputField.ContentType.Password;
        llmKeyInput.onEndEdit.AddListener(v => GoLlmAi.ApiKey = v);
        foreach (var input in aiSetupPanel.GetComponentsInChildren<InputField>(true))
            StyleAiSetupInput(input);

        var footer = new GameObject("AiSetupFooter", typeof(RectTransform), typeof(VerticalLayoutGroup));
        footer.transform.SetParent(aiSetupPanel.transform, false);
        var footerRect = footer.GetComponent<RectTransform>();
        footerRect.anchorMin = Vector2.zero;
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.offsetMin = new Vector2(64f, 44f);
        footerRect.offsetMax = new Vector2(-64f, 148f);
        var footerGroup = footer.GetComponent<VerticalLayoutGroup>();
        footerGroup.spacing = 4f;
        footerGroup.childAlignment = TextAnchor.LowerCenter;
        footerGroup.childControlWidth = footerGroup.childControlHeight = true;
        footerGroup.childForceExpandWidth = true;
        footerGroup.childForceExpandHeight = false;
        timeValidationText = MakeText(footer.transform, "", 16, uiTheme.Cinnabar, TextAnchor.MiddleCenter);
        byoyomiValidationText = MakeText(footer.transform, "", 16, uiTheme.Cinnabar, TextAnchor.MiddleCenter);
        foreach (var validation in new[] { timeValidationText, byoyomiValidationText })
        {
            validation.font = restoredMenuFont != null ? restoredMenuFont : font;
            validation.fontSize = 16;
        }
        var actions = MakeRow(footer.transform, 48f);
        actions.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        actions.GetComponent<HorizontalLayoutGroup>().spacing = 16f;
        var start = MakeButton(actions, "开始对局", 48f, StartAiGame, false);
        start.GetComponent<LayoutElement>().flexibleWidth = 2f;
        start.GetComponent<LayoutElement>().minWidth = 220f;
        StyleAiSetupButton(start, false, true);
        var back = MakeButton(actions, "返回", 48f, ShowMainMenu);
        back.GetComponent<LayoutElement>().flexibleWidth = 1f;
        back.GetComponent<LayoutElement>().minWidth = 140f;
        StyleAiSetupButton(back, true);
        SyncAiSetup();
        aiSetupPanel.SetActive(false);
    }

    Sprite LoadAiSetupSprite(string key)
    {
        string cacheKey = "AiSetup/" + key;
        if (settingsArt.TryGetValue(cacheKey, out var cached)) return cached;
        var texture = Resources.Load<Texture2D>("AiSetup/ai_setup_" + key);
        if (texture == null) return null;
        Rect crop;
        switch (key)
        {
            case "paper": crop = new Rect(44f, 76f, 936f, 1420f); break;
            case "title": crop = new Rect(136f, 88f, 1876f, 524f); break;
            case "option": crop = new Rect(20f, 192f, 2132f, 356f); break;
            case "option_selected": crop = new Rect(0f, 208f, 2172f, 340f); break;
            default: crop = new Rect(32f, 172f, 2108f, 404f); break;
        }
        var sprite = Sprite.Create(texture, crop, new Vector2(0.5f, 0.5f));
        settingsArt[cacheKey] = sprite;
        return sprite;
    }

    void StyleAiSetupText(Text label, int size)
    {
        label.font = restoredMenuFont != null ? restoredMenuFont : font;
        label.fontSize = size;
        label.color = new Color(0.26f, 0.16f, 0.10f);
        label.alignment = TextAnchor.MiddleCenter;
        label.alignByGeometry = true;
        var fitter = label.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;
    }

    Transform MakeAiSetupField(Transform parent, string caption)
    {
        var field = new GameObject("Field_" + caption, typeof(RectTransform), typeof(VerticalLayoutGroup));
        field.transform.SetParent(parent, false);
        var group = field.GetComponent<VerticalLayoutGroup>();
        group.spacing = 4f;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        var label = MakeText(field.transform, caption, 20, Ink, TextAnchor.MiddleCenter);
        StyleAiSetupText(label, caption.Length > 15 ? 18 : 20);
        var layout = label.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = 24f;
        return field.transform;
    }

    List<Button> MakeAiSetupOptions(Transform parent, string[] labels, UnityEngine.Events.UnityAction<int> onPick)
    {
        var options = MakeOptions(parent, labels, onPick);
        var row = options[0].transform.parent.GetComponent<LayoutElement>();
        row.minHeight = row.preferredHeight = 42f;
        foreach (var button in options)
        {
            var layout = button.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 42f;
            StyleAiSetupButton(button);
        }
        return options;
    }

    Text MakeAiSetupStepper(Transform parent, UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus)
    {
        var value = MakeStepper(parent, minus, plus);
        StyleAiSetupText(value, 22);
        var row = value.transform.parent;
        row.GetComponent<LayoutElement>().minHeight = row.GetComponent<LayoutElement>().preferredHeight = 42f;
        row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        foreach (var button in row.GetComponentsInChildren<Button>())
        {
            SetFixedWidth(button, 62f);
            button.GetComponent<LayoutElement>().minWidth = 62f;
            StyleAiSetupButton(button);
        }
        return value;
    }

    void StyleAiSetupButton(Button button, bool selected = false, bool action = false)
    {
        var image = button.GetComponent<Image>();
        image.sprite = LoadAiSetupSprite(action ? "action" : selected ? "option_selected" : "option");
        image.type = Image.Type.Simple;
        image.color = Color.white;
        var colors = button.colors;
        colors.normalColor = colors.selectedColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.96f, 0.84f);
        colors.pressedColor = new Color(0.85f, 0.75f, 0.59f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
        button.colors = colors;
        var label = button.GetComponentInChildren<Text>();
        StyleAiSetupText(label, action ? 24 : 20);
        label.color = selected ? new Color(0.98f, 0.87f, 0.68f) : new Color(0.26f, 0.16f, 0.10f);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(10f, 4f);
        label.rectTransform.offsetMax = new Vector2(-10f, -4f);
    }

    void StyleAiSetupInput(InputField input)
    {
        input.GetComponent<Image>().sprite = LoadAiSetupSprite("option");
        input.GetComponent<Image>().color = Color.white;
        foreach (var text in input.GetComponentsInChildren<Text>(true))
        {
            StyleAiSetupText(text, 20);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(18f, 4f);
            text.rectTransform.offsetMax = new Vector2(-18f, -4f);
        }
        if (input.placeholder != null) input.placeholder.color = new Color(0.45f, 0.34f, 0.23f, 0.8f);
        input.customCaretColor = true;
        input.caretColor = new Color(0.26f, 0.16f, 0.10f);
        input.selectionColor = new Color(0.73f, 0.53f, 0.25f, 0.35f);
    }

    void StyleAiSetupCustomTime(GameObject root)
    {
        var layout = root.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = 44f;
        root.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
        foreach (Transform child in root.transform)
        {
            var label = child.GetComponent<Text>();
            if (label == null) continue;
            StyleAiSetupText(label, 18);
            var labelLayout = child.gameObject.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = label.text.StartsWith("每段") ? 190f : 150f;
            labelLayout.flexibleWidth = 0f;
        }
    }

    void BuildSettingsPanel()
    {
        settingsPanel = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
        settingsPanel.transform.SetParent(menuCanvas.transform, false);
        var panelRect = settingsPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1120f, 680f);
        var pt = settingsPanel.transform;
        {
            var title = new GameObject("SettingsArtTitle", typeof(RectTransform));
            title.transform.SetParent(pt, false);
            var headerRect = title.GetComponent<RectTransform>();
            headerRect.anchorMin = headerRect.anchorMax = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -65f);
            headerRect.sizeDelta = new Vector2(160f, 66f);
            var titleArt = new GameObject("TitleImage", typeof(RectTransform), typeof(Image));
            titleArt.transform.SetParent(title.transform, false);
            var titleRect = titleArt.GetComponent<RectTransform>();
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = Vector2.zero;
            titleRect.sizeDelta = new Vector2(132f, 64f);
            var titleImage = titleArt.GetComponent<Image>();
            titleImage.sprite = LoadSettingsSprite("settings_title_v2", new Rect(400f, 180f, 940f, 500f));
            titleImage.preserveAspect = true;
            titleImage.raycastTarget = false;

            // Give each region its own anchors: section content cannot resize
            // the header, directory or footer through a parent layout group.
            var columns = new GameObject("SettingsBody", typeof(RectTransform)).GetComponent<RectTransform>();
            columns.SetParent(pt, false);
            columns.anchorMin = Vector2.zero;
            columns.anchorMax = Vector2.one;
            columns.offsetMin = new Vector2(56f, 100f);
            columns.offsetMax = new Vector2(-56f, -124f);
            var directory = new GameObject("SettingsDirectory", typeof(RectTransform));
            directory.transform.SetParent(columns, false);
            var directoryRect = directory.GetComponent<RectTransform>();
            directoryRect.anchorMin = Vector2.zero;
            directoryRect.anchorMax = new Vector2(0f, 1f);
            directoryRect.offsetMin = Vector2.zero;
            directoryRect.offsetMax = new Vector2(180f, 0f);
            var directoryImage = directory.AddComponent<Image>();
            directoryImage.color = new Color(0.80f, 0.66f, 0.42f, 0.15f);
            var directoryGroup = directory.AddComponent<VerticalLayoutGroup>();
            directoryGroup.padding = new RectOffset(10, 10, 12, 12);
            directoryGroup.spacing = 9f;
            directoryGroup.childAlignment = TextAnchor.UpperCenter;
            directoryGroup.childControlWidth = true;
            directoryGroup.childControlHeight = true;
            directoryGroup.childForceExpandWidth = true;
            directoryGroup.childForceExpandHeight = false;

            string[] sections = { "音量", "界面", "棋盘", "AI引擎", "快捷键", "关于" };
            for (int i = 0; i < sections.Length; i++)
            {
                int section = i;
                var sectionButton = MakeButton(directory.transform, sections[i], 48f,
                    () => ShowSettingsSection(section));
                StyleSettingsTab(sectionButton, i);
                settingsSectionButtons.Add(sectionButton);
            }

            var content = BuildScrollBody(columns, "SettingsScroll");
            var settingsScroll = content.GetComponentInParent<ScrollRect>();
            var scrollRect = settingsScroll.GetComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(214f, 0f);
            scrollRect.offsetMax = Vector2.zero;
            settingsScroll.viewport.offsetMin = new Vector2(4f, 4f);
            settingsScroll.viewport.offsetMax = new Vector2(-4f, -4f);
            content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(6, 12, 6, 24);
            settingsSectionRoots.Clear();

            var volume = CreateSettingsSection(content, "VolumeSection");
            MakeSettingsSectionHeading(volume, 0);
            MakeSettingsDivider(volume);
            settingsMasterSlider = MakeSettingsSlider(volume, "主音量",
                audioManager != null ? audioManager.MasterVolume : GoAudioManager.DefaultMasterVolume,
                value => audioManager?.SetMasterVolume(value));
            settingsMusicSlider = MakeSettingsSlider(volume, "音乐",
                audioManager != null ? audioManager.MusicVolume : GoAudioManager.DefaultMusicVolume,
                value => audioManager?.SetMusicVolume(value));
            settingsSfxSlider = MakeSettingsSlider(volume, "音效",
                audioManager != null ? audioManager.SfxVolume : GoAudioManager.DefaultSfxVolume,
                value => audioManager?.SetSfxVolume(value));
            settingsWarningSlider = MakeSettingsSlider(volume, "计时提醒",
                audioManager != null ? audioManager.WarningVolume : GoAudioManager.DefaultWarningVolume,
                value => audioManager?.SetWarningVolume(value));
            settingsMuteToggle = uiFactory.MakeToggle(volume, "MuteToggle", "静音",
                audioManager != null && audioManager.IsMuted, SetMuteFromUi);
            settingsMuteToggle.transform.Find("Label").gameObject.SetActive(false);
            MakeSettingsOptionArt(settingsMuteToggle.transform, "静音");

            var appearance = CreateSettingsSection(content, "AppearanceSection");
            MakeSettingsSectionHeading(appearance, 1);
            MakeSettingsDivider(appearance);
            settingsMoveNumbersToggle = uiFactory.MakeToggle(appearance, "MoveNumbersToggle",
                "手数", showMoveNums, value =>
                {
                    showMoveNums = value;
                    Refresh();
                });
            settingsCoordinatesToggle = uiFactory.MakeToggle(appearance, "CoordinatesToggle",
                "坐标", showCoords, value =>
                {
                    showCoords = value;
                    Refresh();
                });
            settingsTerritoryToggle = uiFactory.MakeToggle(appearance, "TerritoryToggle",
                "形势", showTerritory, value =>
                {
                    showTerritory = value;
                    Refresh();
                });
            settingsReduceMotionToggle = uiFactory.MakeToggle(appearance, "ReduceMotionToggle",
                "减少动效", PlayerPrefs.GetInt(ReduceMotionKey, 0) != 0, SetReduceMotion);

            var board = CreateSettingsSection(content, "BoardSection");
            MakeSettingsSectionHeading(board, 2);
            MakeSettingsDivider(board);
            MakeText(board, "棋盘显示选项已在‘界面’分区提供；棋盘皮肤与坐标布局沿用当前棋局设置。",
                14, Dim, TextAnchor.UpperLeft);
            MakeReadOnlyRow(board, "当前棋盘规格").text = "9路 / 13路 / 19路（在人机设置页选择）";

            var engine = CreateSettingsSection(content, "EngineSection");
            MakeSettingsSectionHeading(engine, 3);
            MakeSettingsDivider(engine);
            settingsKataStatusText = MakeReadOnlyRow(engine, "KataGo 状态");
            settingsKataStatusText.text = GetKataGoStatus();
            var kataPath = MakeReadOnlyRow(engine, "KataGo 路径");
            kataPath.text = "Tools/KataGo";
            var kataModel = MakeReadOnlyRow(engine, "模型");
            kataModel.text = "default_model.bin.gz";
            var kataConfig = MakeReadOnlyRow(engine, "配置");
            kataConfig.text = "gtp.cfg";
            var benchmark = MakeReadOnlyRow(engine, "benchmark");
            benchmark.text = "未运行";
            MakeButton(engine, "运行 benchmark", 36f,
                () => Flash("benchmark 尚未在设置页启动"));

            var shortcuts = CreateSettingsSection(content, "ShortcutsSection");
            MakeSettingsSectionHeading(shortcuts, 4);
            MakeSettingsDivider(shortcuts);
            MakeReadOnlyRow(shortcuts, "悔棋 / 停一手").text = "Ctrl+Z / Space";
            MakeReadOnlyRow(shortcuts, "数目 / 认输").text = "S / R";
            MakeText(shortcuts, "快捷键说明仅作当前版本占位，后续按既有入口接入。",
                12, Dim, TextAnchor.UpperLeft);

            var about = CreateSettingsSection(content, "AboutSection");
            MakeSettingsSectionHeading(about, 5);
            MakeSettingsDivider(about);
            MakeReadOnlyRow(about, "版本").text = Application.version;
            MakeText(about, "棋弈有道 · 宋式清雅 · 夜案棋局",
                14, Dim, TextAnchor.UpperLeft);
            MakeText(about, "本页只提供当前版本的设置说明，不改变规则、AI、历史或存档入口。",
                12, Dim, TextAnchor.UpperLeft);

            var back = MakeButton(pt, "返回主界面", 44f, ShowMainMenu);
            var backRect = back.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 0f);
            backRect.anchorMax = new Vector2(1f, 0f);
            backRect.pivot = new Vector2(0.5f, 0.5f);
            backRect.offsetMin = new Vector2(64f, 38f);
            backRect.offsetMax = new Vector2(-64f, 82f);
            ShowSettingsSection(0);
        }
        StyleSettingsPanel();
        ShowSettingsSection(0);
        settingsPanel.SetActive(false);
    }

    Sprite LoadSettingsSprite(string key, Rect crop = default(Rect))
    {
        string cacheKey = key + ":" + crop;
        if (settingsArt.TryGetValue(cacheKey, out var cached)) return cached;
        var texture = Resources.Load<Texture2D>("Settings/" + key);
        if (texture == null) return null;
        if (crop.width <= 0f || crop.height <= 0f)
            crop = new Rect(0f, 0f, texture.width, texture.height);
        crop.x = Mathf.Clamp(crop.x, 0f, texture.width - 1f);
        crop.y = Mathf.Clamp(crop.y, 0f, texture.height - 1f);
        crop.width = Mathf.Min(crop.width, texture.width - crop.x);
        crop.height = Mathf.Min(crop.height, texture.height - crop.y);
        var sprite = Sprite.Create(texture, crop, new Vector2(0.5f, 0.5f));
        settingsArt[cacheKey] = sprite;
        return sprite;
    }

    void StyleSettingsButton(Button button)
    {
        if (button == null) return;
        var image = button.GetComponent<Image>();
        bool isBack = settingsPanel != null && button.transform.parent == settingsPanel.transform;
        if (image != null)
        {
            image.sprite = isBack ? restoredMenuSecondary :
                LoadSettingsSprite("tab_normal", new Rect(10f, 200f, 2150f, 360f));
            if (isBack)
            {
                // Crop the painted plaque proportionally to its imported size.
                var texture = Resources.Load<Texture2D>("MainMenu/menu_secondary");
                if (texture != null)
                    image.sprite = LoadRestoredMenuSprite("menu_secondary",
                        new Rect(0f, texture.height * 0.27f, texture.width, texture.height * 0.49f));
            }
            image.type = Image.Type.Simple;
            image.color = image.sprite != null ? Color.white : new Color(0.8f, 0.7f, 0.5f);
        }
        // GoUiFactory's dark ColorBlock otherwise multiplies the parchment art to black.
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.97f, 0.87f);
        colors.pressedColor = new Color(0.88f, 0.78f, 0.61f);
        colors.selectedColor = Color.white;
        button.colors = colors;
        var label = button.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.font = restoredMenuFont != null ? restoredMenuFont : font;
            label.fontSize = isBack ? 24 : 22;
            label.color = isBack ? new Color(0.98f, 0.87f, 0.68f) : new Color(0.25f, 0.15f, 0.09f);
            label.alignment = TextAnchor.MiddleCenter;
            label.alignByGeometry = true;
            var fitter = label.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(24f, 2f);
            label.rectTransform.offsetMax = new Vector2(-24f, -2f);
        }
        if (isBack && button.transform.Find("ReturnArtLabel") == null)
        {
            if (label != null) label.gameObject.SetActive(false);
            var art = new GameObject("ReturnArtLabel", typeof(RectTransform), typeof(Image));
            art.transform.SetParent(button.transform, false);
            var rect = art.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(190f, 38f);
            var artImage = art.GetComponent<Image>();
            artImage.sprite = LoadSettingsSprite("settings_label_back", new Rect(90f, 70f, 1990f, 590f));
            artImage.preserveAspect = true;
            artImage.raycastTarget = false;
        }
    }

    Sprite SettingsSectionTitleSprite(int index)
    {
        string[] keys = { "tab_volume_original", "tab_appearance", "tab_board", "tab_engine", "tab_shortcuts_original", "tab_about" };
        Rect[] crops = {
            new Rect(180f, 75f, 1420f, 750f),
            new Rect(280f, 95f, 1220f, 670f),
            new Rect(280f, 140f, 1260f, 620f),
            new Rect(300f, 150f, 1390f, 470f),
            new Rect(370f, 110f, 1450f, 520f),
            new Rect(400f, 155f, 1000f, 560f)
        };
        return LoadSettingsSprite(keys[index], crops[index]);
    }

    void MakeSettingsSectionHeading(Transform parent, int index)
    {
        var heading = new GameObject("SectionArtHeading", typeof(RectTransform), typeof(LayoutElement));
        heading.transform.SetParent(parent, false);
        heading.GetComponent<LayoutElement>().preferredHeight = 46f;
        var art = new GameObject("ArtLabel", typeof(RectTransform), typeof(Image));
        art.transform.SetParent(heading.transform, false);
        var rect = art.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(index == 3 || index == 4 ? 126f : 92f, 40f);
        var image = art.GetComponent<Image>();
        image.sprite = SettingsSectionTitleSprite(index);
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    void StyleSettingsTab(Button button, int index)
    {
        StyleSettingsButton(button);
        var backing = button.GetComponent<Image>();
        backing.sprite = null;
        backing.color = Color.clear;
        var title = new GameObject("ArtLabel", typeof(RectTransform), typeof(Image));
        title.transform.SetParent(button.transform, false);
        var rect = title.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(index == 3 || index == 4 ? 122f : 100f, 36f);
        var image = title.GetComponent<Image>();
        image.sprite = SettingsSectionTitleSprite(index);
        image.preserveAspect = true;
        image.raycastTarget = false;
        var accent = new GameObject("ArtAccent", typeof(RectTransform), typeof(Image));
        accent.transform.SetParent(button.transform, false);
        var accentRect = accent.GetComponent<RectTransform>();
        accentRect.anchorMin = accentRect.anchorMax = new Vector2(0f, 0.5f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = new Vector2(4f, 0f);
        accentRect.sizeDelta = new Vector2(3f, 21f);
        accent.GetComponent<Image>().color = new Color(0.50f, 0.27f, 0.10f);
        accent.GetComponent<Image>().raycastTarget = false;
        accent.SetActive(false);
        var text = button.GetComponentInChildren<Text>();
        if (text != null) text.color = Color.clear;
    }

    void StyleSettingsSlider(Slider slider)
    {
        var sliderLayout = slider.GetComponent<LayoutElement>();
        sliderLayout.preferredHeight = sliderLayout.minHeight = 52f;
        sliderLayout.minWidth = 220f;
        sliderLayout.flexibleWidth = 1f;
        var track = slider.GetComponent<Image>();
        if (track != null)
        {
            track.sprite = null;
            track.color = Color.clear;
        }
        var trackArt = new GameObject("TrackArt", typeof(RectTransform), typeof(Image));
        trackArt.transform.SetParent(slider.transform, false);
        trackArt.transform.SetSiblingIndex(0);
        var trackRect = trackArt.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 0.5f);
        trackRect.anchorMax = new Vector2(1f, 0.5f);
        trackRect.offsetMin = new Vector2(6f, -12f);
        trackRect.offsetMax = new Vector2(-6f, 12f);
        var trackImage = trackArt.GetComponent<Image>();
        trackImage.sprite = LoadSettingsSprite("slider_track", new Rect(15f, 270f, 2140f, 195f));
        trackImage.color = Color.white;
        trackImage.raycastTarget = false;
        var fillArea = slider.transform.Find("Fill Area") as RectTransform;
        fillArea.SetParent(trackRect, false);
        fillArea.anchorMin = new Vector2(0.066f, 0.5f);
        fillArea.anchorMax = new Vector2(0.934f, 0.5f);
        fillArea.offsetMin = new Vector2(0f, -2f);
        fillArea.offsetMax = new Vector2(0f, 2f);
        fillArea.gameObject.AddComponent<RectMask2D>();
        var fill = fillArea.Find("Fill");
        var fillImage = fill != null ? fill.GetComponent<Image>() : null;
        if (fillImage != null)
        {
            fillImage.color = new Color(0.54f, 0.29f, 0.11f, 0.75f);
            fillImage.raycastTarget = false;
            var fillRect = fillImage.rectTransform;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        }
        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)).GetComponent<RectTransform>();
        handleArea.SetParent(trackRect, false);
        // Slider drives the handle's vertical anchors to 0..1. A zero-height
        // travel area keeps the ornament at its intended 28-pixel height.
        handleArea.anchorMin = new Vector2(0.066f, 0.5f);
        handleArea.anchorMax = new Vector2(0.934f, 0.5f);
        handleArea.offsetMin = handleArea.offsetMax = Vector2.zero;
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea, false);
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.sizeDelta = new Vector2(10f, 28f);
        var handleImage = handle.GetComponent<Image>();
        handleImage.sprite = LoadSettingsSprite("slider_thumb", new Rect(470f, 105f, 310f, 1040f));
        handleImage.color = Color.white;
        handleImage.raycastTarget = false;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        var sliderColors = slider.colors;
        sliderColors.normalColor = Color.white;
        sliderColors.highlightedColor = new Color(1f, 0.94f, 0.78f);
        sliderColors.selectedColor = Color.white;
        slider.colors = sliderColors;
    }

    void StyleSettingsToggle(Toggle toggle)
    {
        if (toggle == null) return;
        var toggleLayout = toggle.GetComponent<LayoutElement>();
        if (toggleLayout != null) toggleLayout.preferredHeight = toggleLayout.minHeight = 40f;
        var row = toggle.GetComponent<HorizontalLayoutGroup>();
        if (row != null)
        {
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandHeight = false;
            row.spacing = 12f;
        }
        var legacyLabel = toggle.transform.Find("Label");
        if (legacyLabel != null && legacyLabel.gameObject.activeSelf)
        {
            var caption = legacyLabel.GetComponent<Text>().text;
            legacyLabel.gameObject.SetActive(false);
            MakeSettingsOptionArt(toggle.transform, caption);
        }
        var box = toggle.transform.Find("Background");
        var check = box != null ? box.Find("Checkmark") : null;
        var boxImage = box != null ? box.GetComponent<Image>() : null;
        var checkImage = check != null ? check.GetComponent<Image>() : null;
        if (boxImage != null)
        {
            boxImage.sprite = LoadSettingsSprite("toggle_off", new Rect(250f, 250f, 750f, 750f));
            boxImage.color = Color.white;
            var layout = box.GetComponent<LayoutElement>();
            layout.preferredWidth = layout.minWidth = 28f;
            layout.preferredHeight = layout.minHeight = 28f;
        }
        if (checkImage != null)
        {
            checkImage.sprite = LoadSettingsSprite("toggle_on", new Rect(250f, 250f, 750f, 750f));
            checkImage.color = Color.white;
            var rect = check.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    void StyleSettingsPanel()
    {
        var panelImage = settingsPanel.GetComponent<Image>();
        panelImage.sprite = LoadSettingsSprite("settings_background", new Rect(12f, 110f, 1512f, 830f));
        panelImage.type = Image.Type.Simple;
        panelImage.color = Color.white;
        foreach (var label in settingsPanel.GetComponentsInChildren<Text>(true))
        {
            label.font = restoredMenuFont != null ? restoredMenuFont : font;
            label.color = new Color(0.26f, 0.16f, 0.10f);
            label.fontSize = Mathf.Max(22, label.fontSize);
        }
        foreach (var button in settingsPanel.GetComponentsInChildren<Button>(true))
            if (!settingsSectionButtons.Contains(button)) StyleSettingsButton(button);
        foreach (var button in settingsSectionButtons)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = Color.clear;
        }
        var scroll = settingsPanel.GetComponentInChildren<ScrollRect>(true);
        if (scroll != null)
        {
            var hostImage = scroll.GetComponent<Image>();
            if (hostImage != null) hostImage.color = new Color(1f, 1f, 1f, 0.06f);
        }
        foreach (var toggle in settingsPanel.GetComponentsInChildren<Toggle>(true))
            StyleSettingsToggle(toggle);
        foreach (var image in settingsPanel.GetComponentsInChildren<Image>(true))
        {
            if (image.transform.childCount != 1 || image.transform.GetChild(0).name != "Val") continue;
            image.sprite = LoadSettingsSprite("row_frame", new Rect(25f, 275f, 2120f, 180f));
            image.type = Image.Type.Simple;
            image.color = Color.white;
            var rowLayout = image.GetComponent<LayoutElement>();
            if (rowLayout != null) rowLayout.preferredHeight = rowLayout.minHeight = 56f;
            var value = image.transform.GetChild(0).GetComponent<Text>();
            if (value == null) continue;
            value.alignment = TextAnchor.MiddleCenter;
            value.fontSize = 20;
            value.alignByGeometry = true;
            var valueFitter = value.GetComponent<ContentSizeFitter>();
            if (valueFitter != null) valueFitter.enabled = false;
            value.horizontalOverflow = HorizontalWrapMode.Wrap;
            value.verticalOverflow = VerticalWrapMode.Truncate;
            value.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            value.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            value.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            value.rectTransform.offsetMin = new Vector2(44f, -16f);
            value.rectTransform.offsetMax = new Vector2(-44f, 16f);
        }
    }

    void MakeSettingsDivider(Transform parent)
    {
        var line = new GameObject("InkDivider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        line.transform.SetParent(parent, false);
        line.GetComponent<LayoutElement>().preferredHeight = 1f;
        line.GetComponent<Image>().color = new Color(0.61f, 0.38f, 0.19f, 0.75f);
        line.GetComponent<Image>().raycastTarget = false;
    }

    Transform CreateSettingsSection(Transform parent, string name)
    {
        var section = new GameObject(name);
        section.transform.SetParent(parent, false);
        var layout = section.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        var group = section.AddComponent<VerticalLayoutGroup>();
        group.spacing = 12f;
        group.childAlignment = TextAnchor.UpperCenter;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        settingsSectionRoots.Add(section);
        return section.transform;
    }

    string GetKataGoStatus()
    {
        switch (GoKataGo.GetAvailabilityStatus())
        {
            case GoKataGo.AvailabilityStatus.Ready: return "已就绪";
            case GoKataGo.AvailabilityStatus.ProgramMissing: return "未找到程序";
            default: return "未找到模型";
        }
    }

    void MakeSettingsOptionArt(Transform parent, string label)
    {
        string key;
        Rect crop;
        switch (label)
        {
            case "主音量":
                key = "master";
                crop = new Rect(260f, 100f, 1540f, 560f);
                break;
            case "音乐":
                key = "music";
                crop = new Rect(320f, 145f, 1120f, 590f);
                break;
            case "音效":
                key = "sfx";
                crop = new Rect(290f, 150f, 1200f, 600f);
                break;
            case "计时提醒":
                key = "warning";
                crop = new Rect(45f, 100f, 2090f, 550f);
                break;
            case "手数":
                key = "move_numbers";
                crop = new Rect(408f, 199f, 1000f, 472f);
                break;
            case "坐标":
                key = "coordinates";
                crop = new Rect(304f, 167f, 1180f, 572f);
                break;
            case "形势":
                key = "territory";
                crop = new Rect(464f, 195f, 852f, 440f);
                break;
            case "减少动效":
                key = "reduce_motion";
                crop = new Rect(104f, 186f, 1832f, 444f);
                break;
            default:
                key = "mute";
                crop = new Rect(410f, 185f, 960f, 495f);
                break;
        }

        var holder = new GameObject("OptionArt_" + key, typeof(RectTransform), typeof(LayoutElement));
        holder.transform.SetParent(parent, false);
        var layout = holder.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = 120f;
        layout.flexibleWidth = 0f;
        layout.minHeight = layout.preferredHeight = 32f;
        var art = new GameObject("ArtLabel", typeof(RectTransform), typeof(Image));
        art.transform.SetParent(holder.transform, false);
        var rect = art.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(label.Length * 30f, 32f);
        var image = art.GetComponent<Image>();
        image.sprite = LoadSettingsSprite("settings_option_" + key, crop);
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    Slider MakeSettingsSlider(Transform parent, string label, float value,
        UnityEngine.Events.UnityAction<float> onChanged)
    {
        var row = MakeRow(parent, 60f);
        // Keep the label at its stated width; only the slider consumes spare width.
        // The factory's default force-expand split the row in half and squeezed
        // every track into the far-right edge of the settings pane.
        var rowGroup = row.GetComponent<HorizontalLayoutGroup>();
        if (rowGroup != null) rowGroup.childForceExpandWidth = false;
        MakeSettingsOptionArt(row, label);
        var slider = uiFactory.MakeSlider(row, "Slider_" + label, value, onChanged);
        StyleSettingsSlider(slider);
        return slider;
    }

    void BuildSettingsButton()
    {
        settingsButtonRoot = new GameObject("SettingsIconButton", typeof(RectTransform), typeof(Image), typeof(Button));
        settingsButtonRoot.transform.SetParent(menuCanvas.transform, false);
        var image = settingsButtonRoot.GetComponent<Image>();
        image.sprite = restoredSettingsIcon != null ? restoredSettingsIcon : GoIconCatalog.Load(GoIconCatalog.Settings);
        image.color = new Color(0.94f, 0.79f, 0.48f);
        image.preserveAspect = true;
        var button = settingsButtonRoot.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(ShowSettings);
        var rect = settingsButtonRoot.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(42f, 42f);
        rect.anchoredPosition = new Vector2(-24f, -24f);
    }

    void BuildVersionLabel()
    {
        var version = MakeText(menuCanvas.transform,
            "版本号", 13, Dim, TextAnchor.MiddleLeft);
        version.text = "版本 " + Application.version;
        var rect = version.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(28f, 28f);
        rect.sizeDelta = new Vector2(180f, 28f);
    }

    void BuildVolumeQuickControl()
    {
        var root = new GameObject("VolumeQuickControl");
        root.transform.SetParent(menuCanvas.transform, false);
        var rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(1f, 0f);
        rootRect.pivot = new Vector2(1f, 0f);
        rootRect.anchoredPosition = new Vector2(-24f, 24f);
        rootRect.sizeDelta = new Vector2(210f, 38f);
        var layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        quickMasterVolumeSlider = uiFactory.MakeSlider(root.transform, "MasterVolumeSlider",
            audioManager != null ? audioManager.MasterVolume : GoAudioManager.DefaultMasterVolume,
            value => audioManager?.SetMasterVolume(value));
        var sliderLayout = quickMasterVolumeSlider.GetComponent<LayoutElement>();
        sliderLayout.preferredWidth = 160f;
        sliderLayout.minWidth = sliderLayout.preferredWidth;
        sliderLayout.flexibleWidth = 0f;
        StyleMenuVolumeSlider(quickMasterVolumeSlider);

        var muteRoot = new GameObject("MuteIconButton", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
        muteRoot.transform.SetParent(root.transform, false);
        muteRoot.transform.SetSiblingIndex(0);
        var muteLayout = muteRoot.GetComponent<LayoutElement>();
        muteLayout.preferredWidth = muteLayout.minWidth = 34f;
        muteLayout.preferredHeight = muteLayout.minHeight = 34f;
        quickMuteIcon = muteRoot.GetComponent<Image>();
        quickMuteIcon.sprite = restoredVolumeIcon != null ? restoredVolumeIcon : GoIconCatalog.Load(GoIconCatalog.Volume);
        quickMuteIcon.color = new Color(0.94f, 0.79f, 0.48f);
        quickMuteIcon.preserveAspect = true;
        quickMuteButton = muteRoot.GetComponent<Button>();
        quickMuteButton.targetGraphic = quickMuteIcon;
        quickMuteButton.onClick.AddListener(() => SetMuteFromUi(audioManager != null && !audioManager.IsMuted));
        SyncAudioControls();
    }

    void StyleMenuVolumeSlider(Slider slider)
    {
        if (slider == null) return;
        var sliderLayout = slider.GetComponent<LayoutElement>();
        if (sliderLayout != null)
        {
            sliderLayout.preferredHeight = 34f;
            sliderLayout.minHeight = 34f;
        }

        var track = slider.GetComponent<Image>();
        var trackTexture = Resources.Load<Texture2D>("MainMenu/menu_volume_track_v2");
        if (trackTexture != null)
        {
            track.sprite = Sprite.Create(trackTexture,
                new Rect(0f, trackTexture.height * 0.39f,
                    trackTexture.width, trackTexture.height * 0.25f),
                new Vector2(0.5f, 0.5f));
            track.type = Image.Type.Simple;
            track.preserveAspect = false;
            track.color = Color.white;
        }

        var fillArea = slider.transform.Find("Fill Area") as RectTransform;
        if (fillArea != null)
        {
            fillArea.anchorMin = new Vector2(0f, 0.43f);
            fillArea.anchorMax = new Vector2(1f, 0.57f);
            fillArea.offsetMin = new Vector2(12f, 0f);
            fillArea.offsetMax = new Vector2(-12f, 0f);
            var fill = fillArea.Find("Fill");
            var fillImage = fill != null ? fill.GetComponent<Image>() : null;
            if (fillImage != null)
                fillImage.color = new Color(0.81f, 0.59f, 0.27f, 0.85f);
        }

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(slider.transform, false);
        var areaRect = handleArea.GetComponent<RectTransform>();
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(12f, 0f);
        areaRect.offsetMax = new Vector2(-12f, 0f);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.sizeDelta = new Vector2(30f, 30f);
        var handleImage = handle.GetComponent<Image>();
        var handleTexture = Resources.Load<Texture2D>("MainMenu/menu_volume_thumb_v2");
        if (handleTexture != null)
        {
            handleImage.sprite = Sprite.Create(handleTexture,
                new Rect(handleTexture.width * 0.30f, handleTexture.height * 0.10f,
                    handleTexture.width * 0.62f, handleTexture.height * 0.60f),
                new Vector2(0.5f, 0.5f));
            handleImage.color = Color.white;
            handleImage.preserveAspect = true;
        }
        else handleImage.color = new Color(0.9f, 0.7f, 0.35f);
        handleImage.raycastTarget = false;
        slider.handleRect = handleRect;
    }

    static float QuickVolumeSliderWidth()
    {
        if (Screen.width <= 0) return 160f;
        if (Screen.width <= 1400) return 140f;
        if (Screen.width >= 1800) return 180f;
        return Mathf.Lerp(140f, 180f,
            Mathf.InverseLerp(1366f, 1920f, Screen.width));
    }

    void ShowSettingsSection(int section)
    {
        section = Mathf.Clamp(section, 0, settingsSectionRoots.Count - 1);
        for (int i = 0; i < settingsSectionRoots.Count; i++)
            settingsSectionRoots[i].SetActive(i == section);
        for (int i = 0; i < settingsSectionButtons.Count; i++)
        {
            bool selected = i == section;
            var image = settingsSectionButtons[i].GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.color = selected ? new Color(0.82f, 0.66f, 0.40f, 0.17f) : Color.clear;
            }
            var accent = settingsSectionButtons[i].transform.Find("ArtAccent");
            if (accent != null) accent.gameObject.SetActive(selected);
            var label = settingsSectionButtons[i].GetComponentInChildren<Text>();
            if (label != null) label.color = Color.clear;
        }
        if (settingsPanel != null)
        {
            var scroll = settingsPanel.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null)
            {
                scroll.StopMovement();
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 1f;
                if (scroll.content != null)
                    scroll.content.anchoredPosition = new Vector2(0f, 0f);
            }
        }
        if (section == 1 && settingsReduceMotionToggle != null)
            settingsReduceMotionToggle.Select();
        if (section == 0 && settingsMasterSlider != null)
            settingsMasterSlider.Select();
    }

    void ShowSettings()
    {
        SyncAudioControls();
        if (settingsKataStatusText != null)
            settingsKataStatusText.text = GetKataGoStatus();
        menuCanvas.SetActive(true);
        mainPanel.SetActive(false);
        aiSetupPanel.SetActive(false);
        if (historyPanel != null) historyPanel.SetActive(false);
        if (historyDetail != null) historyDetail.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
        SetVolumeQuickVisible(false);
        SetSettingsButtonVisible(false);
        if (uiNavigator != null) uiNavigator.Show(GoUiPage.Settings);
    }

    void SetVolumeQuickVisible(bool visible)
    {
        if (quickMasterVolumeSlider != null && quickMasterVolumeSlider.transform.parent != null)
            quickMasterVolumeSlider.transform.parent.gameObject.SetActive(visible);
    }

    void SetSettingsButtonVisible(bool visible)
    {
        if (settingsButtonRoot != null) settingsButtonRoot.SetActive(visible);
    }

    void SetMuteFromUi(bool value)
    {
        if (audioManager != null) audioManager.SetMute(value);
        SyncAudioControls();
    }

    void SetReduceMotion(bool value)
    {
        PlayerPrefs.SetInt(ReduceMotionKey, value ? 1 : 0);
        PlayerPrefs.Save();
        if (uiNavigator != null)
        {
            uiNavigator.ReduceMotion = value;
            uiNavigator.ReplayCurrentPage();
        }
    }

    void SyncAudioControls()
    {
        if (audioManager == null) return;
        if (quickMasterVolumeSlider != null)
            quickMasterVolumeSlider.SetValueWithoutNotify(audioManager.MasterVolume);
        if (settingsMasterSlider != null)
            settingsMasterSlider.SetValueWithoutNotify(audioManager.MasterVolume);
        if (settingsMusicSlider != null)
            settingsMusicSlider.SetValueWithoutNotify(audioManager.MusicVolume);
        if (settingsSfxSlider != null)
            settingsSfxSlider.SetValueWithoutNotify(audioManager.SfxVolume);
        if (settingsWarningSlider != null)
            settingsWarningSlider.SetValueWithoutNotify(audioManager.WarningVolume);
        if (settingsMuteToggle != null)
            settingsMuteToggle.SetIsOnWithoutNotify(audioManager.IsMuted);
        if (settingsReduceMotionToggle != null)
            settingsReduceMotionToggle.SetIsOnWithoutNotify(
                PlayerPrefs.GetInt(ReduceMotionKey, 0) != 0);
        if (settingsMoveNumbersToggle != null)
            settingsMoveNumbersToggle.SetIsOnWithoutNotify(showMoveNums);
        if (settingsCoordinatesToggle != null)
            settingsCoordinatesToggle.SetIsOnWithoutNotify(showCoords);
        if (settingsTerritoryToggle != null)
            settingsTerritoryToggle.SetIsOnWithoutNotify(showTerritory);
        if (quickMuteButton != null)
        {
            var label = quickMuteButton.GetComponentInChildren<Text>();
            if (label != null) label.text = audioManager.IsMuted ? "取消静音" : "静音";
            if (quickMuteIcon != null)
                quickMuteIcon.sprite = audioManager.IsMuted
                    ? (restoredMuteIcon != null ? restoredMuteIcon : GoIconCatalog.Load(GoIconCatalog.Mute))
                    : (restoredVolumeIcon != null ? restoredVolumeIcon : GoIconCatalog.Load(GoIconCatalog.Volume));
        }
    }

    void RegisterUiPages()
    {
        if (uiNavigator == null) return;
        uiNavigator.Register(GoUiPage.MainMenu, mainPanel);
        uiNavigator.Register(GoUiPage.AiSetup, aiSetupPanel);
        uiNavigator.Register(GoUiPage.History, historyPanel);
        uiNavigator.Register(GoUiPage.Settings, settingsPanel);
        uiNavigator.Register(GoUiPage.Playing, gameUiPanel);
        uiNavigator.Register(GoUiPage.Study, gameUiPanel);
        uiNavigator.Register(GoUiPage.Review, gameUiPanel);
    }

    void BuildHistoryDetail()
    {
        historyDetail = new GameObject("HistoryDetail");
        historyDetail.transform.SetParent(menuCanvas.transform, false);
        var overlay = historyDetail.AddComponent<Image>();
        overlay.color = new Color(0.05f, 0.04f, 0.03f, 0.72f);
        overlay.raycastTarget = true;
        var ort = historyDetail.GetComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;

        var card = new GameObject("Card");
        card.transform.SetParent(historyDetail.transform, false);
        var cimg = card.AddComponent<Image>();
        cimg.sprite = LoadHistorySprite("paper");
        cimg.color = Color.white;
        var crt = card.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(760f, 640f);

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(64, 64, 88, 52);
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        historyDetailText = MakeText(card.transform, "", 16, Ink, TextAnchor.UpperLeft);
        StyleHistoryText(historyDetailText, 20, TextAnchor.UpperLeft);
        historyDetailText.horizontalOverflow = HorizontalWrapMode.Wrap;
        var dle = historyDetailText.gameObject.AddComponent<LayoutElement>();
        dle.flexibleHeight = 1f;
        dle.minHeight = 180f;
        MakeButton(card.transform, "打开复盘", 38f, () => OpenKifuReview(historyDetailRecord));
        MakeButton(card.transform, "导出 SGF", 38f, () => ExportKifuSgf(historyDetailRecord));
        MakeButton(card.transform, "编辑信息", 38f, () => BeginEditKifu(historyDetailRecord));
        MakeButton(card.transform, "删除", 38f, () => AskDeleteKifu(historyDetailRecord));
        MakeButton(card.transform, "返回", 40f, CloseHistoryDetail);
        foreach (var button in card.GetComponentsInChildren<Button>())
            StyleHistoryButton(button, false, button.GetComponentInChildren<Text>().text == "删除");
        historyDetail.SetActive(false);

        historyDeleteDialog = BuildDialogRoot(menuCanvas.transform, "HistoryDeleteDialog", out Transform deleteCard);
        deleteCard.GetComponent<Image>().sprite = LoadHistorySprite("paper");
        deleteCard.GetComponent<Image>().color = Color.white;
        deleteCard.GetComponent<RectTransform>().sizeDelta = new Vector2(660f, 320f);
        var deleteLayout = deleteCard.gameObject.AddComponent<VerticalLayoutGroup>();
        deleteLayout.padding = new RectOffset(48, 48, 40, 36);
        deleteLayout.spacing = 12f;
        deleteLayout.childAlignment = TextAnchor.MiddleCenter;
        deleteLayout.childControlWidth = true;
        deleteLayout.childControlHeight = true;
        deleteLayout.childForceExpandWidth = true;
        deleteLayout.childForceExpandHeight = false;
        MakeText(deleteCard, "删除棋谱", 26, Ink, TextAnchor.MiddleCenter);
        historyDeleteText = MakeText(deleteCard, "", 16, Ink, TextAnchor.MiddleCenter);
        var deleteButtons = MakeRow(deleteCard, 36f);
        var deleteRowLayout = deleteButtons.GetComponent<LayoutElement>();
        deleteRowLayout.minHeight = deleteRowLayout.preferredHeight = 36f;
        deleteRowLayout.flexibleHeight = 0f;
        var deleteRow = deleteButtons.GetComponent<HorizontalLayoutGroup>();
        deleteRow.childForceExpandWidth = deleteRow.childForceExpandHeight = false;
        deleteRow.childAlignment = TextAnchor.MiddleCenter;
        deleteRow.spacing = 20f;
        MakeButton(deleteButtons, "确认删除", 36f, ConfirmDeleteKifu);
        MakeButton(deleteButtons, "取消", 36f, CancelDeleteKifu);
        foreach (var text in deleteCard.GetComponentsInChildren<Text>()) StyleHistoryText(text, 20, TextAnchor.MiddleCenter);
        foreach (var button in deleteCard.GetComponentsInChildren<Button>())
        {
            StyleHistoryButton(button, false, button.GetComponentInChildren<Text>().text == "确认删除");
            var buttonLayout = button.GetComponent<LayoutElement>();
            buttonLayout.minWidth = buttonLayout.preferredWidth = 160f;
            buttonLayout.flexibleWidth = 0f;
            buttonLayout.minHeight = buttonLayout.preferredHeight = 36f;
            button.GetComponentInChildren<Text>().fontSize = 18;
        }
        historyDeleteDialog.SetActive(false);
        // A separate overlay stays visible when either the game or menu
        // canvas is disabled. The form is shared by saving and history editing.
        kifuForm.transform.SetParent(null, false);
        var formCanvas = kifuForm.AddComponent<Canvas>();
        formCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        formCanvas.overrideSorting = true;
        formCanvas.sortingOrder = menuCanvas.GetComponent<Canvas>().sortingOrder + 10;
        ConfigureCanvasScaler(kifuForm.AddComponent<CanvasScaler>());
        kifuForm.AddComponent<GraphicRaycaster>();
        var editCard = kifuForm.transform.Find("Card");
        editCard.GetComponent<Image>().sprite = LoadHistorySprite("paper");
        editCard.GetComponent<Image>().color = Color.white;
        editCard.GetComponent<RectTransform>().sizeDelta = new Vector2(760f, 900f);
        editCard.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(64, 64, 80, 50);
        foreach (var label in editCard.GetComponentsInChildren<Text>(true))
            StyleHistoryText(label, 18, label.alignment);
        foreach (var input in editCard.GetComponentsInChildren<InputField>(true))
        {
            StyleHistoryInput(input);
            if (input.lineType != InputField.LineType.SingleLine)
            {
                input.textComponent.alignment = TextAnchor.UpperLeft;
                input.textComponent.rectTransform.offsetMin = new Vector2(24f, 12f);
                input.textComponent.rectTransform.offsetMax = new Vector2(-24f, -12f);
            }
        }
        foreach (var button in editCard.GetComponentsInChildren<Button>(true)) StyleHistoryButton(button);
        foreach (var row in editCard.GetComponentsInChildren<HorizontalLayoutGroup>(true))
        {
            row.childForceExpandHeight = false;
            var rowLayout = row.GetComponent<LayoutElement>();
            if (rowLayout != null) { rowLayout.minHeight = rowLayout.preferredHeight = 44f; rowLayout.flexibleHeight = 0f; }
        }
        foreach (var value in new[] { kifuTimeText, kifuResultText })
        {
            StyleHistoryText(value, 18, TextAnchor.MiddleCenter);
            var frame = value.transform.parent.GetComponent<Image>();
            if (frame != null) { frame.sprite = LoadHistorySprite("option"); frame.color = Color.white; }
        }
    }

    void RebuildHistoryList()
    {
        if (historyListContent == null) return;
        for (int i = historyListContent.childCount - 1; i >= 0; i--)
        {
            var previous = historyListContent.GetChild(i).gameObject;
            previous.SetActive(false);
            UnityEngine.Object.Destroy(previous);
        }

        var rows = KifuDatabase.Query(new KifuQuery
        {
            Keyword = historySearch != null ? historySearch.text : "",
            BoardSize = historyBoardSizeFilter,
            Mode = historyModeFilter,
            Sort = historySort
        });
        if (historyCountText != null) historyCountText.text = $"共 {rows.Count} 局";
        if (rows.Count == 0)
        {
            var empty = MakeHistoryCardLine(historyListContent,
                string.IsNullOrEmpty(historySearch.text) && historyBoardSizeFilter == 0 && string.IsNullOrEmpty(historyModeFilter)
                    ? "暂无棋谱\n完成对局并保存后，棋谱将收录于此。"
                    : "暂无匹配棋谱\n试试其他关键词或筛选条件。", 22, 144f);
            empty.name = "HistoryEmptyState";
            empty.alignment = TextAnchor.MiddleCenter;
            var scroll = historyListContent.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            return;
        }
        foreach (var rec in rows)
            CreateHistoryCard(historyListContent, rec);
        var listScroll = historyListContent.GetComponentInParent<ScrollRect>();
        if (listScroll != null)
        {
            listScroll.StopMovement();
            Canvas.ForceUpdateCanvases();
            listScroll.verticalNormalizedPosition = 1f;
        }
    }

    void OpenHistoryDetail(KifuRecord rec)
    {
        if (rec == null || historyDetail == null) return;
        historyDetailRecord = FindKifu(rec.id);
        if (historyDetailRecord == null) return;
        historyDetailText.text = HistoryCardText(historyDetailRecord);
        historyPanel.SetActive(false);
        historyDetail.SetActive(true);
    }

    string HistoryCardText(KifuRecord rec)
    {
        string title = string.IsNullOrEmpty(rec.title) ? "未命名对局" : rec.title;
        string result = string.IsNullOrEmpty(rec.result) ? "未终局" : rec.result;
        return $"{title}\n黑方：{rec.blackName}（{rec.blackRank}）  白方：{rec.whiteName}（{rec.whiteRank}）\n" +
            $"{rec.boardSize}路  ·  {rec.moveCount}手  ·  {rec.playedAt}  ·  {rec.mode}\n结果：{result}";
    }

    KifuRecord FindKifu(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var rec in KifuDatabase.All())
            if (rec != null && rec.id == id) return rec;
        return null;
    }

    void OpenKifuReviewById(string id)
    {
        OpenKifuReview(FindKifu(id));
    }

    void ExportKifuSgfById(string id)
    {
        ExportKifuSgf(FindKifu(id));
    }

    void ExportKifuSgf(KifuRecord rec)
    {
        if (rec == null) { Flash("棋谱不存在或已删除"); return; }
        try
        {
            string name = SafeKifuFileName(rec.title);
            string path = Path.Combine(Application.persistentDataPath, name + "_" + rec.id + ".sgf");
            File.WriteAllText(path, KifuDatabase.BuildSgf(rec), System.Text.Encoding.UTF8);
            Flash("SGF 已导出：" + path);
        }
        catch (System.Exception ex)
        {
            Flash("SGF 导出失败：" + ex.Message);
        }
    }

    static string SafeKifuFileName(string value)
    {
        if (string.IsNullOrEmpty(value)) return "kifu";
        var chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i]) || "<>:\"/\\|?*".IndexOf(chars[i]) >= 0) chars[i] = '_';
        return new string(chars).Trim().TrimEnd('.');
    }

    void BeginEditKifuById(string id)
    {
        BeginEditKifu(FindKifu(id));
    }

    void BeginEditKifu(KifuRecord rec)
    {
        if (rec == null || kifuForm == null) { Flash("棋谱不存在或已删除"); return; }
        editingKifu = true;
        editingKifuRecord = CloneKifuRecord(rec);
        kifuDraftReady = true;
        kifuTitle.text = editingKifuRecord.title ?? "";
        kifuBlack.text = editingKifuRecord.blackName ?? "";
        kifuWhite.text = editingKifuRecord.whiteName ?? "";
        kifuBlackRank.text = editingKifuRecord.blackRank ?? "";
        kifuWhiteRank.text = editingKifuRecord.whiteRank ?? "";
        kifuDesc.text = editingKifuRecord.description ?? "";
        kifuTimeText.text = editingKifuRecord.playedAt ?? "";
        kifuResultText.text = editingKifuRecord.result ?? "未终局";
        historyDetail.SetActive(false);
        historyPanel.SetActive(false);
        kifuForm.SetActive(true);
    }

    void AskDeleteKifuById(string id)
    {
        AskDeleteKifu(FindKifu(id));
    }

    void AskDeleteKifu(KifuRecord rec)
    {
        if (rec == null || historyDeleteDialog == null) { Flash("棋谱不存在或已删除"); return; }
        pendingDeleteKifuId = rec.id;
        historyDeleteText.text = $"确定删除“{rec.title}”吗？\n删除后无法从历史列表恢复。";
        historyDeleteDialog.SetActive(true);
    }

    void ConfirmDeleteKifu()
    {
        if (string.IsNullOrEmpty(pendingDeleteKifuId)) return;
        string id = pendingDeleteKifuId;
        pendingDeleteKifuId = null;
        if (!KifuDatabase.Delete(id))
        {
            historyDeleteDialog.SetActive(false);
            Flash("删除失败：棋谱不存在或保存失败");
            return;
        }
        historyDeleteDialog.SetActive(false);
        historyDetailRecord = null;
        if (historyDetail != null) historyDetail.SetActive(false);
        historyPanel.SetActive(true);
        RebuildHistoryList();
        Flash("棋谱已删除");
    }

    void CancelDeleteKifu()
    {
        pendingDeleteKifuId = null;
        if (historyDeleteDialog != null) historyDeleteDialog.SetActive(false);
    }

    void CloseHistoryDetail()
    {
        historyDetailRecord = null;
        historyDetail.SetActive(false);
        historyPanel.SetActive(true);
        RebuildHistoryList();
    }

    void OpenKifuReview(KifuRecord rec)
    {
        if (rec == null) return;
        if (!KifuDatabase.ValidateRecord(rec, out string recordError))
        {
            Flash("棋谱无法打开：" + recordError);
            return;
        }
        ClearResumedKifuSession();
        InvalidateAiTurn();
        StopPlayClock();
        audioManager?.PlayMusic("bgm_review");
        reviewingKifu = true;
        reviewRec = rec;
        inStudyMode = false;
        vsAI = false;
        aiBusy = false;
        inBranchMode = false;
        curBranchIdx = -1;
        curBranchStepIdx = -1;
        branchMoveCount = 0;
        branchLastWinrate = -1f;
        activeMarkTool = GoMarkType.None;
        SyncMarkToolButtons();
        if (boardMarks != null) System.Array.Clear(boardMarks, 0, boardMarks.Length);
        if (btnExitBranch != null) btnExitBranch.SetActive(false);
        if (btnBranchUndo != null) btnBranchUndo.SetActive(false);

        moveComments.Clear();
        if (!string.IsNullOrEmpty(rec.commentsJson))
        {
            var loadedC = JsonUtility.FromJson<MoveCommentsData>(rec.commentsJson);
            if (loadedC != null && loadedC.items != null)
            {
                foreach (var item in loadedC.items)
                    moveComments[item.ply] = item.text;
            }
        }

        reviewBranches.Clear();
        if (!string.IsNullOrEmpty(rec.branchesJson))
        {
            var loadedB = JsonUtility.FromJson<MoveBranchesCatalog>(rec.branchesJson);
            if (loadedB != null && loadedB.branches != null)
            {
                reviewBranches.AddRange(loadedB.branches);
            }
        }
        StopAllCoroutines();
        InvalidatePositionAnalysis();
        boardSize = rec.boardSize > 0 ? rec.boardSize : 19;
        showMoveNums = true;
        reviewLogCsv = rec.moveLogCsv ?? "";
        reviewTotal = rules.CountLogMoves(reviewLogCsv);
        if (reviewTotal <= 0 && rec.moveCount > 0) reviewTotal = rec.moveCount;
        reviewPly = reviewTotal;
        inMenu = false;

        gameAnalysis = !string.IsNullOrEmpty(rec.analysisJson) ? KataGameAnalysis.FromJson(rec.analysisJson) : null;
        curPosAnalysis = null;
        hoveredCandidateIdx = -1;

        if (GoKataGo.IsAvailable())
        {
            aiReviewEnabled = true;
            if (btnAiReviewLabel != null) btnAiReviewLabel.text = "AI推演: 开";
        }
        else
        {
            aiReviewEnabled = false;
            if (btnAiReviewLabel != null) btnAiReviewLabel.text = "AI推演: 关";
        }

        if (historyDetail != null) historyDetail.SetActive(false);
        if (historyPanel != null) historyPanel.SetActive(false);
        menuCanvas.SetActive(false);
        if (reviewAiRoot == null && gameUiContent != null)
            BuildReviewAiUi(gameUiContent);
        if (uiNavigator != null) uiNavigator.Show(GoUiPage.Review);
        HideScorePopup();
        BuildBoard();
        ReviewSeek(reviewPly);
        Flash($"{rec.title}  ·  {rec.blackName} vs {rec.whiteName}");
    }

    void RestoreReviewToPlay()
    {
        if (!reviewingKifu || rules == null) return;
        GoStateData state = rules.ExportState();
        KifuRecord restoredMetadata = CloneKifuRecord(reviewRec);
        int restoredHandicap = restoredMetadata != null ? restoredMetadata.handicap : 0;

        if (isAnalyzingFullGame) GoKataGo.CancelAnalysis();
        isAnalyzingFullGame = false;
        if (fullAnalysisCo != null) { StopCoroutine(fullAnalysisCo); fullAnalysisCo = null; }
        InvalidatePositionAnalysis();
        StopPlayClock();

        reviewingKifu = false;
        inStudyMode = false;
        vsAI = false;
        inBranchMode = false;
        reviewRec = null;
        reviewLogCsv = "";
        boardSize = state.size > 0 ? state.size : boardSize;
        if (!rules.ImportState(state))
        {
            Flash("恢复实战失败：局面存档无效");
            return;
        }
        rules.ResumeRecording();
        rules.EnsureMoveAtForDisplay();
        resumedReviewPly = reviewPly;
        resumedKifuMetadata = restoredMetadata;
        resumedKifuHandicap = restoredHandicap;
        isResumedKifuSession = resumedKifuMetadata != null;
        inMenu = false;
        menuCanvas.SetActive(false);
        if (uiNavigator != null) uiNavigator.Show(GoUiPage.Playing);
        BuildBoard();
        gameStartedAt = System.DateTime.Now;
        lastResultLine = "";
        kifuDraftReady = false;
        StartPlayClock();
        Refresh();
        Flash($"已恢复第 {resumedReviewPly} 手，轮到{(rules.Turn == GoRules.BLACK ? "黑" : "白")}方");
    }

    void InvalidatePositionAnalysis()
    {
        reviewAnalysisToken++;
        if (curAnalysisCo != null)
        {
            StopCoroutine(curAnalysisCo);
            curAnalysisCo = null;
        }
        curPosAnalysis = null;
        hoveredCandidateIdx = -1;
        HideCandidateBadges();
        ClearWinrateBar();
    }

    void ReviewSeek(int ply, bool requestAnalysis = true)
    {
        if (!reviewingKifu || rules == null) return;
        InvalidatePositionAnalysis();
        if (inBranchMode)
        {
            inBranchMode = false;
            curBranchIdx = -1;
            curBranchStepIdx = -1;
            branchMoveCount = 0;
            branchLastWinrate = -1f;
            if (btnExitBranch != null) btnExitBranch.SetActive(false);
            if (btnBranchUndo != null) btnBranchUndo.SetActive(false);
        }
        if (ply < 0) ply = 0;
        if (ply > reviewTotal) ply = reviewTotal;
        reviewPly = ply;

        rules.Reset(boardSize);
        rules.Komi = reviewRec != null && reviewRec.komi > 0f ? reviewRec.komi : 7.5f;
        if (reviewRec != null && reviewRec.handicap > 0)
            rules.SetupHandicap(HandicapPoints(boardSize, reviewRec.handicap));

        bool ok = rules.ReplayToPly(reviewLogCsv, reviewPly);
        if (!ok) Flash("棋谱部分着法无法复原");

        rules.ResumeRecording();
        if (reviewPly >= reviewTotal && reviewRec != null && !string.IsNullOrEmpty(reviewRec.stateJson))
        {
            var data = JsonUtility.FromJson<GoStateData>(reviewRec.stateJson);
            if (data != null && data.dead != null && data.dead.Length > 0)
            {
                rules.Dead.Clear();
                foreach (int d in data.dead) rules.Dead.Add(d);
            }
        }
        rules.EnsureMoveAtForDisplay();
        Refresh();

        DrawWinrateChart(reviewPly);
        UpdateReviewInfoText(reviewPly);
        if (TryGetReviewWinrate(reviewPly, out float wr) && HasGameAnalysisAt(reviewPly))
        {
            float lead = gameAnalysis.blackScoreLeads[reviewPly];
            float prevWr = reviewPly > 0 ? gameAnalysis.blackWinrates[reviewPly - 1] : wr;
            UpdateWinrateBar(wr, lead, wr - prevWr);
            branchLastWinrate = wr;
        }
        else
        {
            ClearWinrateBar();
            branchLastWinrate = -1f;
        }

        RefreshReviewAnalysisDisplay();

        if (requestAnalysis && aiReviewEnabled) RequestPositionAnalysis();
        else RefreshReviewAnalysisDisplay();
    }

    bool HasGameAnalysisSeries()
    {
        return gameAnalysis != null
            && gameAnalysis.blackWinrates != null
            && gameAnalysis.blackWinrates.Length > 0;
    }

    bool HasGameAnalysisAt(int ply)
    {
        return HasGameAnalysisSeries()
            && gameAnalysis.blackScoreLeads != null
            && ply >= 0
            && ply < gameAnalysis.blackWinrates.Length
            && ply < gameAnalysis.blackScoreLeads.Length;
    }

    bool TryGetReviewWinrate(int ply, out float winrate)
    {
        if (curPosAnalysis != null && curPosAnalysis.candidates != null && curPosAnalysis.candidates.Count > 0)
        {
            winrate = curPosAnalysis.blackWinrate;
            return true;
        }
        if (HasGameAnalysisAt(ply))
        {
            winrate = gameAnalysis.blackWinrates[ply];
            return true;
        }
        winrate = -1f;
        return false;
    }

    void RefreshReviewAnalysisDisplay()
    {
        if (!reviewingKifu) return;

        bool hasPositionAnalysis = curPosAnalysis != null
            && curPosAnalysis.candidates != null
            && curPosAnalysis.candidates.Count > 0;
        bool hasGameAnalysis = !inBranchMode && HasGameAnalysisAt(reviewPly);
        if (reviewPositionInfoText != null)
        {
            string who = rules != null && rules.Turn == GoRules.BLACK ? "黑方" : "白方";
            reviewPositionInfoText.text = $"局面信息：第 {reviewPly} 手 · {who}行棋\n分析：{(hasPositionAnalysis || hasGameAnalysis ? "已加载" : "暂无分析")}";
        }

        if (hasPositionAnalysis)
            UpdateWinrateBar(curPosAnalysis.blackWinrate, curPosAnalysis.blackScoreLead);
        else if (hasGameAnalysis)
            UpdateWinrateBar(gameAnalysis.blackWinrates[reviewPly], gameAnalysis.blackScoreLeads[reviewPly]);
        else
            ClearWinrateBar();

        KataCandidate candidate = hasPositionAnalysis
            && curPosAnalysis.candidates != null
            && curPosAnalysis.candidates.Count > 0
            ? curPosAnalysis.candidates[0] : null;
        UpdateCandidateDetailText(candidate);
        UpdateReviewInfoText(reviewPly);
    }

    void RefreshReviewMoveLog()
    {
        if (reviewTotal == 0 || string.IsNullOrEmpty(reviewLogCsv))
        {
            moveLogText.text = "本局无落子记录";
            return;
        }
        var parts = reviewLogCsv.Split('|');
        var sb = new System.Text.StringBuilder(reviewTotal * 28 + (reviewBranches.Count * 64));
        int shown = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;
            var p = parts[i].Split(',');
            if (p.Length < 2) continue;
            int.TryParse(p[0], out int x);
            int.TryParse(p[1], out int y);
            bool pass = x < 0;
            if (shown > 0) sb.Append('\n');
            string mark = (shown + 1 == reviewPly && !inBranchMode) ? "▶ " : "   ";
            string commentIcon = (moveComments.ContainsKey(shown + 1) && !string.IsNullOrEmpty(moveComments[shown + 1])) ? " <color=#EAA83E>💬</color>" : "";
            if (pass) sb.Append($"{mark}第{shown + 1:000}棋停一手{commentIcon}");
            else sb.Append($"{mark}第{shown + 1:000}棋下在（{ColLabel(x)}，{RowLabel(y)}）{commentIcon}");
            shown++;
        }

        if (reviewBranches.Count > 0 || inBranchMode)
        {
            sb.Append($"\n\n<color=#EAA83E>════ 推演变化 ({reviewBranches.Count}) ════</color>");
            for (int b = 0; b < reviewBranches.Count; b++)
            {
                var br = reviewBranches[b];
                bool isCurrent = inBranchMode && curBranchIdx == b;
                string sign = br.isExpanded ? "[-] " : "[+] ";
                string curTag = isCurrent ? " <color=#73D13D>[推演中]</color>" : "";
                sb.Append($"\n<color=#F0B042><b>{sign}{br.name}</b>（第{br.startPly}手起 · {br.steps.Count}步）{curTag}</color>");
                if (br.isExpanded)
                {
                    if (br.steps.Count == 0)
                    {
                        sb.Append("\n   <color=#888888>（暂无推演步数，在盘面落子开始）</color>");
                    }
                    for (int s = 0; s < br.steps.Count; s++)
                    {
                        var st = br.steps[s];
                        string who = st.color == GoRules.BLACK ? "黑" : "白";
                        string coord = st.isPass ? "停一手" : $"（{ColLabel(st.x)}，{RowLabel(st.y)}）";
                        string wrStr = st.hasAnalysis && st.winrate >= 0
                            ? $" <color=#88C0D0>[{st.winrate * 100f:0.0}%]</color>" : "";
                        string commStr = !string.IsNullOrEmpty(st.comment) ? $" <color=#EAA83E>💬{st.comment}</color>" : "";
                        string stepMark = (isCurrent && s == curBranchStepIdx) ? " ▶ " : "   ";
                        sb.Append($"\n<color=#D4AF37>{stepMark}{st.step}. {who}{coord}{wrStr}{commStr}</color>");
                    }
                }
            }
        }

        if (btnCurBranchLabel != null)
        {
            if (curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count)
                btnCurBranchLabel.text = $"切换:{reviewBranches[curBranchIdx].name}";
            else
                btnCurBranchLabel.text = "切换变化";
        }

        moveLogText.text = sb.ToString();
        if (moveLogScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            if (inBranchMode)
            {
                moveLogScroll.verticalNormalizedPosition = 0f;
            }
            else
            {
                float den = Mathf.Max(1, reviewTotal);
                moveLogScroll.verticalNormalizedPosition = 1f - (reviewPly / den);
            }
        }
    }

    Transform BuildScrollBody(Transform parent, string name)
    {
        var host = new GameObject(name);
        host.transform.SetParent(parent, false);
        var hostLayout = host.AddComponent<LayoutElement>();
        hostLayout.flexibleHeight = 1f;
        hostLayout.minHeight = 0f;
        hostLayout.flexibleWidth = 1f;
        var hostImage = host.AddComponent<Image>();
        hostImage.color = new Color(0.10f, 0.08f, 0.06f, 0.58f);

        var scroll = host.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(host.transform, false);
        var viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(8f, 8f);
        viewportRect.offsetMax = new Vector2(-8f, -8f);
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        var contentGroup = content.AddComponent<VerticalLayoutGroup>();
        contentGroup.padding = new RectOffset(16, 16, 14, 14);
        contentGroup.spacing = 10f;
        contentGroup.childAlignment = TextAnchor.UpperCenter;
        contentGroup.childControlWidth = true;
        contentGroup.childControlHeight = true;
        contentGroup.childForceExpandWidth = true;
        contentGroup.childForceExpandHeight = false;
        var contentFit = content.AddComponent<ContentSizeFitter>();
        contentFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        return content.transform;
    }

    GameObject BuildMenuPanel(string name, float width, float height, System.Action<Transform> build)
    {
        var panel = new GameObject(name);
        panel.transform.SetParent(menuCanvas.transform, false);
        var pimg = panel.AddComponent<Image>();
        pimg.color = new Color(0.14f, 0.11f, 0.08f, 1f);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(width, PanelHeightForScreen(height));
        prt.anchoredPosition = Vector2.zero;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(30, 30, 24, 24);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        build(panel.transform);
        return panel;
    }

    Sprite LoadRestoredMenuSprite(string key, Rect crop, Vector4 border = default(Vector4))
    {
        var texture = Resources.Load<Texture2D>("MainMenu/" + key);
        if (texture == null) return null;
        crop.x = Mathf.Clamp(crop.x, 0f, texture.width - 1f);
        crop.y = Mathf.Clamp(crop.y, 0f, texture.height - 1f);
        crop.width = Mathf.Min(crop.width, texture.width - crop.x);
        crop.height = Mathf.Min(crop.height, texture.height - crop.y);
        return Sprite.Create(texture, crop, new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, border);
    }

    Sprite LoadRestoredMenuPlaque(string key, float bottom, float height)
    {
        var texture = Resources.Load<Texture2D>("MainMenu/" + key);
        if (texture == null) return null;
        return LoadRestoredMenuSprite(key, new Rect(0f, texture.height * bottom,
            texture.width, texture.height * height));
    }

    void StyleRestoredMenuButton(Button button, bool primary = false)
    {
        if (button == null) return;
        var image = button.GetComponent<Image>();
        image.sprite = primary ? restoredMenuPrimary : restoredMenuSecondary;
        // The art is a complete long plaque, not a 9-slice texture.
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.color = image.sprite != null ? Color.white : new Color(0.30f, 0.19f, 0.11f);
        var label = button.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.font = restoredMenuFont != null ? restoredMenuFont : font;
            label.fontSize = 22;
            label.color = primary ? new Color(0.25f, 0.15f, 0.08f) : new Color(0.96f, 0.85f, 0.67f);
            label.alignment = TextAnchor.MiddleCenter;
            label.alignByGeometry = true;
            var fitter = label.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(330f, 44f);
            labelRect.anchoredPosition = Vector2.zero;
        }
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.94f, 0.78f);
        colors.pressedColor = new Color(0.82f, 0.72f, 0.55f);
        button.colors = colors;
    }

    void CreateRestoredMenuButton(Transform parent, string label,
        UnityEngine.Events.UnityAction onClick, bool primary = false,
        bool playClickSound = true)
    {
        var button = MakeButton(parent, label, 46f, onClick, playClickSound);
        var layout = button.GetComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = 46f;
        layout.flexibleHeight = 0f;
        StyleRestoredMenuButton(button, primary);
    }

    static void ConfigureCanvasScaler(CanvasScaler scaler)
    {
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = GoUiTheme.ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    static float PanelHeightForScreen(float requestedHeight)
    {
        if (Screen.height <= 0) return requestedHeight;
        float safeY = GoUiTheme.SafeAreaMarginCanvasY(Screen.width, Screen.height);
        float safeHeight = Mathf.Max(480f, GoUiTheme.ReferenceResolution.y - 2f * safeY);
        return Mathf.Min(requestedHeight, safeHeight);
    }

    static void FitPanelHeight(GameObject panel, float requestedHeight)
    {
        if (panel == null) return;
        var rect = panel.GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(rect.sizeDelta.x,
            PanelHeightForScreen(requestedHeight));
    }

    static void StyleAccentLabel(Button btn)
    {
        var t = btn.GetComponentInChildren<Text>();
        t.color = new Color(0.11f, 0.09f, 0.07f);
        t.fontSize = 18;
    }

    static float GameSidebarWidth()
    {
        return GoUiTheme.SidebarWidthCanvas(Screen.width, Screen.height);
    }

    void ApplyResponsiveLayout()
    {
        lastResponsiveWidth = Screen.width;
        lastResponsiveHeight = Screen.height;

        if (menuSafeArea != null)
        {
            menuSafeArea.anchorMin = Vector2.zero;
            menuSafeArea.anchorMax = Vector2.one;
            float safeX = GoUiTheme.SafeAreaMarginCanvasX(Screen.width, Screen.height);
            float safeY = GoUiTheme.SafeAreaMarginCanvasY(Screen.width, Screen.height);
            menuSafeArea.offsetMin = new Vector2(safeX, safeY);
            menuSafeArea.offsetMax = new Vector2(-safeX, -safeY);
        }

        FitPanelHeight(mainPanel, 680f);
        if (aiSetupPanel != null)
        {
            var canvasRect = menuCanvas.GetComponent<RectTransform>().rect;
            aiSetupPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(
                Mathf.Min(800f, canvasRect.width - 80f),
                Mathf.Min(960f, canvasRect.height - 80f));
        }
        if (historyPanel != null)
        {
            var canvasRect = menuCanvas.GetComponent<RectTransform>().rect;
            historyPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(
                Mathf.Min(1120f, canvasRect.width - 80f),
                Mathf.Min(960f, canvasRect.height - 80f));
        }
        if (settingsPanel != null)
        {
            var canvasRect = menuCanvas.GetComponent<RectTransform>().rect;
            settingsPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(
                Mathf.Min(1120f, canvasRect.width - 80f),
                Mathf.Min(680f, canvasRect.height - 80f));
        }

        if (gameUiPanel != null)
        {
            var rect = gameUiPanel.GetComponent<RectTransform>();
            float safeX = GoUiTheme.SafeAreaMarginCanvasX(Screen.width, Screen.height);
            float safeY = GoUiTheme.SafeAreaMarginCanvasY(Screen.width, Screen.height);
            rect.sizeDelta = new Vector2(GameSidebarWidth(),
                -2f * safeY);
            rect.anchoredPosition = new Vector2(-safeX, 0f);
        }

        if (cam != null)
        {
            float availableWidth = Mathf.Max(1f,
                Screen.width - GoUiTheme.PhysicalPixelsForCanvasUnits(
                    GameSidebarWidth(), Screen.width, Screen.height)
                - 2f * GoUiTheme.PhysicalPixelsForCanvasUnits(
                    GoUiTheme.SafeAreaMarginCanvasX(Screen.width, Screen.height),
                    Screen.width, Screen.height));
            cam.rect = new Rect(0f, 0f,
                Mathf.Clamp01(availableWidth / Mathf.Max(1f, Screen.width)), 1f);
        }
    }

    void SyncAiSetup()
    {
        PaintOptions(optSize, sizeChoice);
        PaintOptions(optColor, colorChoice);
        PaintOptions(optMainTime, (int)timeControl.MainPreset);
        PaintOptions(optByoyomi, (int)timeControl.ByoyomiPreset);
        if (optOpponent != null) PaintOptions(optOpponent, opponentChoice);
        komiValText.text = $"贴 {komi:0.0} 目";
        handiValText.text = handicap == 0 ? "不让子" : $"让 {handicap} 子";
        if (llmKeyInput != null && string.IsNullOrEmpty(llmKeyInput.text))
            llmKeyInput.text = PlayerPrefs.GetString("GoLlmKey", "");
        if (customMainMinutesInput != null)
            customMainMinutesInput.SetTextWithoutNotify(
                customMainMinutesDirty ? customMainMinutesText
                    : Mathf.Max(1, timeControl.MainTimeSeconds / 60).ToString());
        if (customByoyomiPeriodsInput != null)
            customByoyomiPeriodsInput.SetTextWithoutNotify(
                customByoyomiPeriodsDirty ? customByoyomiPeriodsText
                    : Mathf.Max(1, timeControl.ByoyomiPeriods).ToString());
        if (customByoyomiSecondsInput != null)
            customByoyomiSecondsInput.SetTextWithoutNotify(
                customByoyomiSecondsDirty ? customByoyomiSecondsText
                    : Mathf.Max(5, timeControl.ByoyomiSeconds).ToString());
        if (customMainTimeRoot != null)
            customMainTimeRoot.SetActive(timeControl.MainPreset == GoTimePreset.Custom);
        if (customByoyomiRoot != null)
            customByoyomiRoot.SetActive(timeControl.ByoyomiPreset == GoByoyomiPreset.Custom);
        SyncTimeControlFromInputs();
    }

    GameObject BuildCustomMainTime(Transform parent)
    {
        var root = new GameObject("CustomMainTime");
        root.transform.SetParent(parent, false);
        var row = root.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;

        MakeText(root.transform, "分钟（1–600）", 13, Dim, TextAnchor.MiddleLeft);
        customMainMinutesInput = MakeInputField(root.transform, "例如 30", 36f, false);
        customMainMinutesInput.contentType = InputField.ContentType.IntegerNumber;
        SetFixedWidth(customMainMinutesInput, 120f);
        customMainMinutesInput.onEndEdit.AddListener(_ =>
        {
            customMainMinutesText = customMainMinutesInput.text;
            customMainMinutesDirty = true;
            SyncTimeControlFromInputs();
            ValidateTimeControlUi();
        });
        return root;
    }

    GameObject BuildCustomByoyomi(Transform parent)
    {
        var root = new GameObject("CustomByoyomi");
        root.transform.SetParent(parent, false);
        var row = root.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;

        MakeText(root.transform, "段数（1–20）", 13, Dim, TextAnchor.MiddleLeft);
        customByoyomiPeriodsInput = MakeInputField(root.transform, "例如 5", 36f, false);
        customByoyomiPeriodsInput.contentType = InputField.ContentType.IntegerNumber;
        SetFixedWidth(customByoyomiPeriodsInput, 82f);
        MakeText(root.transform, "每段秒数（5–600）", 13, Dim, TextAnchor.MiddleLeft);
        customByoyomiSecondsInput = MakeInputField(root.transform, "例如 30", 36f, false);
        customByoyomiSecondsInput.contentType = InputField.ContentType.IntegerNumber;
        SetFixedWidth(customByoyomiSecondsInput, 92f);

        UnityEngine.Events.UnityAction onEdit = () =>
        {
            customByoyomiPeriodsText = customByoyomiPeriodsInput.text;
            customByoyomiSecondsText = customByoyomiSecondsInput.text;
            customByoyomiPeriodsDirty = true;
            customByoyomiSecondsDirty = true;
            SyncTimeControlFromInputs();
            ValidateTimeControlUi();
        };
        customByoyomiPeriodsInput.onEndEdit.AddListener(_ => onEdit());
        customByoyomiSecondsInput.onEndEdit.AddListener(_ => onEdit());
        return root;
    }

    void SetMainTimePreset(int index)
    {
        var preset = (GoTimePreset)Mathf.Clamp(index, 0, 4);
        var next = GoTimeControl.FromPreset(preset, timeControl.ByoyomiPreset);
        if (timeControl.ByoyomiPreset == GoByoyomiPreset.Custom)
        {
            next.ByoyomiPeriods = timeControl.ByoyomiPeriods;
            next.ByoyomiSeconds = timeControl.ByoyomiSeconds;
        }
        if (preset == GoTimePreset.Custom)
            next.MainTimeSeconds = timeControl.MainTimeSeconds >= 60
                ? timeControl.MainTimeSeconds : 60;
        timeControl = next;
        SyncAiSetup();
    }

    void SetByoyomiPreset(int index)
    {
        var preset = (GoByoyomiPreset)Mathf.Clamp(index, 0, 4);
        var next = GoTimeControl.FromPreset(timeControl.MainPreset, preset);
        if (timeControl.MainPreset == GoTimePreset.Custom)
            next.MainTimeSeconds = timeControl.MainTimeSeconds >= 60
                ? timeControl.MainTimeSeconds : 60;
        if (preset == GoByoyomiPreset.Custom)
        {
            next.ByoyomiPeriods = timeControl.ByoyomiPeriods >= 1
                ? timeControl.ByoyomiPeriods : 1;
            next.ByoyomiSeconds = timeControl.ByoyomiSeconds >= 5
                ? timeControl.ByoyomiSeconds : 5;
        }
        timeControl = next;
        SyncAiSetup();
    }

    void SyncTimeControlFromInputs()
    {
        var next = timeControl;
        if (next.MainPreset == GoTimePreset.Custom)
        {
            if (customMainMinutesInput != null)
                customMainMinutesText = customMainMinutesInput.text;
            int minutes;
            next.MainTimeSeconds = int.TryParse(
                customMainMinutesText,
                out minutes) ? minutes * 60 : 0;
        }
        if (next.ByoyomiPreset == GoByoyomiPreset.Custom)
        {
            if (customByoyomiPeriodsInput != null)
                customByoyomiPeriodsText = customByoyomiPeriodsInput.text;
            if (customByoyomiSecondsInput != null)
                customByoyomiSecondsText = customByoyomiSecondsInput.text;
            int periods;
            int seconds;
            next.ByoyomiPeriods = int.TryParse(
                customByoyomiPeriodsText,
                out periods) ? periods : 0;
            next.ByoyomiSeconds = int.TryParse(
                customByoyomiSecondsText,
                out seconds) ? seconds : 0;
        }
        timeControl = next;
        ValidateTimeControlUi();
    }

    void ValidateTimeControlUi()
    {
        if (timeControl.TryValidate(out string error))
        {
            SetTimeValidationMessage(null);
            return;
        }
        SetTimeValidationMessage(error);
    }

    void SetTimeValidationMessage(string error)
    {
        string mainError = "";
        string byoyomiError = "";
        if (!string.IsNullOrEmpty(error))
        {
            if (error.IndexOf("byoyomi", System.StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("读秒", System.StringComparison.Ordinal) >= 0)
                byoyomiError = error;
            else
                mainError = error;
        }
        if (timeValidationText != null) timeValidationText.text = mainError;
        if (byoyomiValidationText != null) byoyomiValidationText.text = byoyomiError;
    }

    void PaintOptions(List<Button> btns, int sel)
    {
        for (int i = 0; i < btns.Count; i++)
        {
            bool on = i == sel;
            if (aiSetupPanel != null && btns[i].transform.IsChildOf(aiSetupPanel.transform))
            {
                StyleAiSetupButton(btns[i], on);
                continue;
            }
            if (historyPanel != null && btns[i].transform.IsChildOf(historyPanel.transform))
            {
                StyleHistoryButton(btns[i], on);
                continue;
            }
            btns[i].GetComponent<Image>().color = on ? Accent : BtnBg;
            btns[i].GetComponentInChildren<Text>().color = on ? new Color(0.11f, 0.09f, 0.07f) : Ink;
        }
    }

    List<Button> MakeOptions(Transform parent, string[] labels, UnityEngine.Events.UnityAction<int> onPick)
    {
        var row = MakeRow(parent, 36f);
        var list = new List<Button>();
        for (int i = 0; i < labels.Length; i++)
        {
            int k = i;
            list.Add(MakeButton(row, labels[i], 34f, () => { onPick(k); PaintOptions(list, k); }));
        }
        return list;
    }

    Text MakeStepper(Transform parent, UnityEngine.Events.UnityAction onMinus, UnityEngine.Events.UnityAction onPlus)
    {
        var row = MakeRow(parent, 36f);
        var minus = MakeButton(row, "－", 34f, () => { onMinus(); SyncAiSetup(); });
        SetFixedWidth(minus, 70f);

        var tgo = new GameObject("Val");
        tgo.transform.SetParent(row, false);
        var t = tgo.AddComponent<Text>();
        t.font = font; t.fontSize = 17; t.color = Ink; t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var le = tgo.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;

        var plus = MakeButton(row, "＋", 34f, () => { onPlus(); SyncAiSetup(); });
        SetFixedWidth(plus, 70f);
        return t;
    }

    static void SetFixedWidth(Button b, float w)
    {
        var le = b.GetComponent<LayoutElement>();
        le.flexibleWidth = 0f;
        le.preferredWidth = w;
    }

    static void SetFixedWidth(InputField input, float w)
    {
        var le = input.GetComponent<LayoutElement>();
        if (le == null) le = input.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 0f;
        le.minWidth = w;
        le.preferredWidth = w;
    }

    /* ---------------- UI 基础件 ---------------- */

    Text MakeText(Transform parent, string content, int fontSize, Color color, TextAnchor anchor)
    {
        if (uiFactory != null)
            return uiFactory.MakeText(parent, "Label", content, fontSize, color, anchor);

        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.text = content;
        t.fontSize = Mathf.Max(fontSize,
            GoUiTheme.MinimumBodyFontSizeForCanvas(
                parent != null ? parent.GetComponentInParent<Canvas>() : null));
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return t;
    }

    Transform MakeRow(Transform parent, float height)
    {
        if (uiFactory != null)
            return uiFactory.MakeRow(parent, "Row", height);

        var go = new GameObject("Row");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        return go.transform;
    }

    Image AttachButtonIcon(Button button, string key)
    {
        if (button == null || string.IsNullOrEmpty(key)) return null;
        // A plain GameObject starts with Transform, so querying RectTransform
        // before adding a UI component returns null in a player build.
        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(button.transform, false);
        var rect = iconGo.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(10f, 0f);
        rect.sizeDelta = new Vector2(22f, 22f);
        var image = iconGo.AddComponent<Image>();
        image.sprite = GoIconCatalog.Load(key);
        image.color = uiTheme != null ? uiTheme.Gold : Color.white;
        image.raycastTarget = false;

        var labelText = button.GetComponentInChildren<Text>();
        if (labelText != null)
        {
            var labelRect = labelText.rectTransform;
            labelRect.offsetMin = new Vector2(26f, labelRect.offsetMin.y);
        }
        return image;
    }

    Button MakeButton(Transform parent, string label, float height,
        UnityEngine.Events.UnityAction cb, bool playClickSound = true)
    {
        UnityEngine.Events.UnityAction wrapped = () =>
        {
            if (playClickSound) audioManager?.PlaySfx("button_click");
            cb?.Invoke();
        };
        if (uiFactory != null)
        {
            var style = height >= 48f ? GoButtonStyle.Primary : GoButtonStyle.Secondary;
            var button = uiFactory.MakeButton(parent, "Btn_" + label, label, style, wrapped);
            var layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredHeight = height;
                layout.minHeight = height;
            }
            return button;
        }

        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
        var img = go.AddComponent<Image>();
        img.color = BtnBg;
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.40f, 0.31f, 0.18f);
        colors.pressedColor = new Color(0.50f, 0.38f, 0.20f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        btn.targetGraphic = img;
        btn.onClick.AddListener(wrapped);

        var tgo = new GameObject("Label");
        tgo.transform.SetParent(go.transform, false);
        var t = tgo.AddComponent<Text>();
        t.font = font; t.text = label; t.fontSize = 15; t.color = Ink;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = t.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.sizeDelta = Vector2.zero;
        trt.anchoredPosition = Vector2.zero;
        return btn;
    }

    InputField MakeLabeledInput(Transform parent, string label, string placeholder, float height, bool multi)
    {
        MakeText(parent, label, 13, Dim, TextAnchor.MiddleLeft);
        return MakeInputField(parent, placeholder, height, multi);
    }

    Text MakeReadOnlyRow(Transform parent, string label)
    {
        MakeText(parent, label, 13, Dim, TextAnchor.MiddleLeft);
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 32f;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.10f, 0.08f, 0.06f, 1f);

        var tgo = new GameObject("Val");
        tgo.transform.SetParent(go.transform, false);
        var t = tgo.AddComponent<Text>();
        t.font = font;
        t.fontSize = 15;
        t.color = Ink;
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(10f, 0f);
        rt.offsetMax = new Vector2(-10f, 0f);
        return t;
    }

    InputField MakeInputField(Transform parent, string placeholder, float height, bool multi)
    {
        if (uiFactory != null)
        {
            var compatInput = uiFactory.MakeInput(parent, "Input", placeholder, 15);
            compatInput.lineType = multi ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            var layout = compatInput.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredHeight = height;
                layout.minHeight = height;
            }
            return compatInput;
        }

        var go = new GameObject("Input");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.10f, 0.08f, 0.06f, 1f);

        var input = go.AddComponent<InputField>();
        input.targetGraphic = img;
        input.lineType = multi ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.AddComponent<Text>();
        text.font = font;
        text.fontSize = 15;
        text.color = Ink;
        text.supportRichText = false;
        text.alignment = multi ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
        var trt = text.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 4f);
        trt.offsetMax = new Vector2(-10f, -4f);

        var phGo = new GameObject("Placeholder");
        phGo.transform.SetParent(go.transform, false);
        var ph = phGo.AddComponent<Text>();
        ph.font = font;
        ph.fontSize = 15;
        ph.color = new Color(Dim.r, Dim.g, Dim.b, 0.7f);
        ph.text = placeholder;
        ph.supportRichText = false;
        ph.alignment = text.alignment;
        var prt = ph.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(10f, 4f);
        prt.offsetMax = new Vector2(-10f, -4f);

        input.textComponent = text;
        input.placeholder = ph;
        return input;
    }

    void Flash(string msg)
    {
        msgText.text = msg;
        if (msgCo != null) StopCoroutine(msgCo);
        msgCo = StartCoroutine(ClearMsg());
    }

    IEnumerator ClearMsg()
    {
        yield return new WaitForSeconds(3.2f);
        msgText.text = "";
        msgCo = null;
    }

    void Refresh()
    {
        bool rev = reviewingKifu;
        if (btnUndo != null) btnUndo.SetActive(!rev);
        if (btnPass != null) btnPass.SetActive(!rev);
        if (btnResign != null) btnResign.SetActive(!rev);
        if (btnClear != null) btnClear.SetActive(false);
        if (btnScore != null) btnScore.SetActive(!rev);
        if (reviewAiRoot != null) reviewAiRoot.SetActive(rev);
        if (commentModuleRoot != null) commentModuleRoot.SetActive(rev);
        if (studySaveStatusText != null)
            studySaveStatusText.gameObject.SetActive(inStudyMode && !rev);
        if (homeBtnLabel != null) homeBtnLabel.text = rev ? "返回棋谱" : "返回主界面";

        if (boardSkin != null)
            boardSkin.Refresh(rules, showMoveNums, showCoords, showTerritory);

        if (reviewingKifu && inBranchMode)
        {
            string who = rules.Turn == GoRules.BLACK ? "黑方" : "白方";
            string brName = (curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count) ? reviewBranches[curBranchIdx].name : "推演";
            turnText.text = $"【{brName} 推演中】\n实战第 {branchPlyStart} 手 · 推演第 {branchMoveCount} 步\n轮到{who}落子";
        }
        else if (reviewingKifu && reviewRec != null)
        {
            string who = rules.Turn == GoRules.BLACK ? "黑方" : "白方";
            string names = $"{reviewRec.blackName} vs {reviewRec.whiteName}";
            string res = string.IsNullOrEmpty(reviewRec.result) ? "" : reviewRec.result + "\n";
            turnText.text = $"{reviewRec.title}\n{names}\n{res}第 {reviewPly}/{reviewTotal} 手 · 轮到{who}落子";
        }
        else if (rules.Result != null) turnText.text = rules.Result;
        else
        {
            string who = rules.Turn == GoRules.BLACK ? "黑方" : "白方";
            turnText.text = $"第 {rules.MoveCount + 1} 手 · {who}行棋";
        }

        RefreshMoveLog();
        RefreshStudyVisuals();
        UpdateBoardMarksVisuals();
        SyncCommentModule();
        RefreshStudyArtUi();

        if (reviewingKifu)
        {
            DrawWinrateChart(reviewPly);
            UpdateReviewInfoText(reviewPly);
        }
        UpdateClockPauseState();
    }

    /* ---------------- KataGo 复盘分析与变化推演 ---------------- */

    Sprite MakeBadgeSprite()
    {
        int s = 128;
        float r = s / 2f - 2f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = x - s / 2f, dy = y - s / 2f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(r - d);
            px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * a));
        }
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }

    void BuildAnalysisOverlays()
    {
        if (candBadgeSpr == null) candBadgeSpr = MakeBadgeSprite();

        var co = new GameObject("CandidatesRoot");
        co.transform.SetParent(boardRoot, false);
        candRoot = co;

        for (int i = 0; i < MaxCandidates; i++)
        {
            var go = new GameObject($"cand_{i}");
            go.transform.SetParent(candRoot.transform, false);
            go.transform.localScale = Vector3.one * 0.88f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = candBadgeSpr;
            sr.sortingOrder = 6;
            candSrs[i] = sr;

            var to = new GameObject("Text");
            to.transform.SetParent(go.transform, false);
            to.transform.localPosition = Vector3.zero;
            var tm = to.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 24;
            tm.characterSize = 0.070f;
            tm.lineSpacing = 0.82f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var mr = to.GetComponent<MeshRenderer>();
            if (font != null && font.material != null) mr.sharedMaterial = font.material;
            mr.sortingOrder = 7;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            candTexts[i] = tm;

            go.SetActive(false);
            candBadges[i] = go;
            candIdxs[i] = -1;
        }

        var pvo = new GameObject("PvRoot");
        pvo.transform.SetParent(boardRoot, false);
        pvRoot = pvo;

        for (int i = 0; i < MaxPvSteps; i++)
        {
            var go = new GameObject($"pv_{i}");
            go.transform.SetParent(pvRoot.transform, false);
            go.transform.localScale = Vector3.one * 0.92f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 5;
            pvStoneSrs[i] = sr;

            var to = new GameObject("Text");
            to.transform.SetParent(go.transform, false);
            to.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            var tm = to.AddComponent<TextMesh>();
            tm.font = numberFont;
            tm.fontSize = 32;
            tm.characterSize = 0.09f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            var mr = to.GetComponent<MeshRenderer>();
            if (numberFont != null && numberFont.material != null) mr.sharedMaterial = numberFont.material;
            mr.sortingOrder = 6;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pvTextMeshes[i] = tm;

            go.SetActive(false);
        }
    }

    void BuildReviewAiUi(Transform pt)
    {
        reviewAiRoot = new GameObject("ReviewAiRoot");
        reviewAiRoot.transform.SetParent(pt, false);
        var vlg = reviewAiRoot.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // 1. 复盘进退按钮行（上一手 / 下一手 / 终局）
        var navRow = new GameObject("NavRow");
        navRow.transform.SetParent(reviewAiRoot.transform, false);
        var hlgNav = navRow.AddComponent<HorizontalLayoutGroup>();
        hlgNav.spacing = 4f;
        hlgNav.childControlWidth = true;
        hlgNav.childControlHeight = true;
        hlgNav.childForceExpandWidth = true;
        hlgNav.childForceExpandHeight = true;
        var navLe = navRow.AddComponent<LayoutElement>();
        navLe.preferredHeight = 34f;

        btnReviewPrev = MakeButton(navRow.transform, "上一手", 34f, () => ReviewSeek(reviewPly - 1)).gameObject;
        btnReviewNext = MakeButton(navRow.transform, "下一手", 34f, () => ReviewSeek(reviewPly + 1)).gameObject;
        btnReviewEnd = MakeButton(navRow.transform, "终局", 34f, () => ReviewSeek(reviewTotal)).gameObject;

        // 1.5 标记工具按钮行：[落子] [✕] [▲] [■] [●] [清空]
        var markRow = new GameObject("MarkToolRow");
        markRow.transform.SetParent(reviewAiRoot.transform, false);
        var hlgMark = markRow.AddComponent<HorizontalLayoutGroup>();
        hlgMark.spacing = 3f;
        hlgMark.childControlWidth = true;
        hlgMark.childControlHeight = true;
        hlgMark.childForceExpandWidth = true;
        hlgMark.childForceExpandHeight = true;
        var markLe = markRow.AddComponent<LayoutElement>();
        markLe.preferredHeight = 30f;

        optMarkButtons.Clear();
        optMarkButtons.Add(MakeButton(markRow.transform, "落子", 30f, () => SelectMarkTool(GoMarkType.None)));
        optMarkButtons.Add(MakeButton(markRow.transform, "✕", 30f, () => SelectMarkTool(GoMarkType.Cross)));
        optMarkButtons.Add(MakeButton(markRow.transform, "▲", 30f, () => SelectMarkTool(GoMarkType.Triangle)));
        optMarkButtons.Add(MakeButton(markRow.transform, "■", 30f, () => SelectMarkTool(GoMarkType.Square)));
        optMarkButtons.Add(MakeButton(markRow.transform, "●", 30f, () => SelectMarkTool(GoMarkType.Circle)));

        var btnClearMark = MakeButton(markRow.transform, "清空", 30f, ClearAllMarks);
        var clearImg = btnClearMark.GetComponent<Image>();
        if (clearImg != null) clearImg.color = new Color(0.38f, 0.18f, 0.14f, 1f);

        SyncMarkToolButtons();

        // 2. 胜率与目数信息文本
        reviewPositionInfoText = MakeText(reviewAiRoot.transform, "局面信息：暂无", 13, Dim, TextAnchor.MiddleLeft);
        var posLe = reviewPositionInfoText.gameObject.AddComponent<LayoutElement>();
        posLe.preferredHeight = 18f;

        winrateInfoText = MakeText(reviewAiRoot.transform, "胜率分析", 13, Ink, TextAnchor.MiddleCenter);
        winrateInfoText.supportRichText = true;
        var infoLe = winrateInfoText.gameObject.AddComponent<LayoutElement>();
        infoLe.preferredHeight = 18f;

        // 3. 双色胜率条容器
        var barGo = new GameObject("WinrateBarContainer");
        barGo.transform.SetParent(reviewAiRoot.transform, false);
        winrateBarRoot = barGo;
        var barLe = barGo.AddComponent<LayoutElement>();
        barLe.preferredWidth = 236f;
        barLe.preferredHeight = 22f;
        barLe.minHeight = 22f;
        var barBgImg = barGo.AddComponent<Image>();
        barBgImg.color = new Color(0.18f, 0.15f, 0.12f, 1f);

        var bBarGo = new GameObject("BarBlack");
        bBarGo.transform.SetParent(barGo.transform, false);
        var bImg = bBarGo.AddComponent<Image>();
        bImg.color = new Color(0.12f, 0.12f, 0.14f, 1f);
        barBlackRt = bBarGo.GetComponent<RectTransform>();
        barBlackRt.anchorMin = new Vector2(0f, 0f);
        barBlackRt.anchorMax = new Vector2(0.5f, 1f);
        barBlackRt.offsetMin = Vector2.zero;
        barBlackRt.offsetMax = Vector2.zero;

        var wBarGo = new GameObject("BarWhite");
        wBarGo.transform.SetParent(barGo.transform, false);
        var wImg = wBarGo.AddComponent<Image>();
        wImg.color = new Color(0.92f, 0.90f, 0.86f, 1f);
        barWhiteRt = wBarGo.GetComponent<RectTransform>();
        barWhiteRt.anchorMin = new Vector2(0.5f, 0f);
        barWhiteRt.anchorMax = new Vector2(1f, 1f);
        barWhiteRt.offsetMin = Vector2.zero;
        barWhiteRt.offsetMax = Vector2.zero;

        var tGo = new GameObject("BarText");
        tGo.transform.SetParent(barGo.transform, false);
        winrateBarText = tGo.AddComponent<Text>();
        winrateBarText.font = font;
        winrateBarText.fontSize = 13;
        winrateBarText.color = Color.white;
        winrateBarText.alignment = TextAnchor.MiddleCenter;
        winrateBarText.horizontalOverflow = HorizontalWrapMode.Overflow;
        winrateBarText.verticalOverflow = VerticalWrapMode.Overflow;
        winrateBarText.supportRichText = true;
        var barOutline = tGo.AddComponent<Outline>();
        barOutline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        barOutline.effectDistance = new Vector2(1f, -1f);
        var tRt = tGo.GetComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero;
        tRt.anchorMax = Vector2.one;
        tRt.offsetMin = Vector2.zero;
        tRt.offsetMax = Vector2.zero;
        winrateBarRoot.SetActive(false);

        // 4. 全局胜率走势折线图
        var chartGo = new GameObject("WinrateChart");
        chartGo.transform.SetParent(reviewAiRoot.transform, false);
        var chartLe = chartGo.AddComponent<LayoutElement>();
        chartLe.preferredWidth = 236f;
        chartLe.preferredHeight = 64f;
        chartLe.minHeight = 64f;

        winrateChartTex = new Texture2D(236, 64, TextureFormat.RGBA32, false);
        winrateChartTex.filterMode = FilterMode.Point;
        winrateChartImg = chartGo.AddComponent<RawImage>();
        winrateChartImg.texture = winrateChartTex;

        var trigger = chartGo.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        entry.callback.AddListener((data) => { OnChartClick((PointerEventData)data, chartGo); });
        trigger.triggers.Add(entry);
        var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        dragEntry.callback.AddListener((data) => { OnChartClick((PointerEventData)data, chartGo); });
        trigger.triggers.Add(dragEntry);

        // 5. 候选点详情展示文本
        candInfoText = MakeText(reviewAiRoot.transform, "暂无分析", 13, Ink, TextAnchor.MiddleLeft);
        candInfoText.supportRichText = true;
        var candInfoLe = candInfoText.gameObject.AddComponent<LayoutElement>();
        candInfoLe.preferredHeight = 36f;
        candInfoLe.minHeight = 36f;
        pvInfoText = MakeText(reviewAiRoot.transform, "PV：暂无分析", 13, Ink, TextAnchor.MiddleLeft);
        pvInfoText.supportRichText = true;
        var pvLe = pvInfoText.gameObject.AddComponent<LayoutElement>();
        pvLe.preferredHeight = 24f;

        // 6. 分支推演控制按钮行：[推演一步] [悔一手] [恢复实战]
        var branchRow = new GameObject("BranchRow");
        branchRow.transform.SetParent(reviewAiRoot.transform, false);
        var hlgBranch = branchRow.AddComponent<HorizontalLayoutGroup>();
        hlgBranch.spacing = 4f;
        hlgBranch.childControlWidth = true;
        hlgBranch.childControlHeight = true;
        hlgBranch.childForceExpandWidth = true;
        hlgBranch.childForceExpandHeight = true;
        var branchLe = branchRow.AddComponent<LayoutElement>();
        branchLe.preferredHeight = 32f;

        var btnStep = MakeButton(branchRow.transform, "推演一步", 32f, StepAiCandidate);
        btnStepAi = btnStep.gameObject;
        var stepImg = btnStepAi.GetComponent<Image>();
        if (stepImg != null) stepImg.color = new Color(0.18f, 0.44f, 0.28f, 1f);

        var btnUndo = MakeButton(branchRow.transform, "悔一手", 32f, UndoBranchMove);
        btnBranchUndo = btnUndo.gameObject;

        var btnExit = MakeButton(branchRow.transform, "恢复实战", 32f, ExitBranchMode);
        btnExitBranch = btnExit.gameObject;
        var exitImg = btnExitBranch.GetComponent<Image>();
        if (exitImg != null) exitImg.color = new Color(0.48f, 0.22f, 0.16f, 1f);

        btnBranchUndo.SetActive(false);
        btnExitBranch.SetActive(false);
        var btnRestore = MakeButton(branchRow.transform, "恢复实战", 32f, RestoreReviewToPlay);
        btnRestoreReview = btnRestore.gameObject;
        var restoreImg = btnRestoreReview.GetComponent<Image>();
        if (restoreImg != null) restoreImg.color = new Color(0.18f, 0.38f, 0.22f, 1f);

        // 6.5 变化分支管理按钮行：[新建变化] [展开/收起] [切换变化] [删除变化]
        var varRow = new GameObject("VarRow");
        varRow.transform.SetParent(reviewAiRoot.transform, false);
        var hlgVar = varRow.AddComponent<HorizontalLayoutGroup>();
        hlgVar.spacing = 3f;
        hlgVar.childControlWidth = true;
        hlgVar.childControlHeight = true;
        hlgVar.childForceExpandWidth = true;
        hlgVar.childForceExpandHeight = true;
        var varLe = varRow.AddComponent<LayoutElement>();
        varLe.preferredHeight = 28f;

        MakeButton(varRow.transform, "新建变化", 28f, NewVariation);
        MakeButton(varRow.transform, "展开/收起(+/-)", 28f, ToggleVariationsExpand);
        var btnCycle = MakeButton(varRow.transform, "切换变化", 28f, CycleLoadVariation);
        btnCurBranchLabel = btnCycle.GetComponentInChildren<Text>();
        var btnDel = MakeButton(varRow.transform, "删除变化", 28f, DeleteCurrentVariation);
        var delImg = btnDel.GetComponent<Image>();
        if (delImg != null) delImg.color = new Color(0.38f, 0.18f, 0.14f, 1f);

        // 7. AI 功能按钮行：[AI推演: 关/开] [全局分析]
        var aiRow = new GameObject("AiRow");
        aiRow.transform.SetParent(reviewAiRoot.transform, false);
        var hlgAi = aiRow.AddComponent<HorizontalLayoutGroup>();
        hlgAi.spacing = 4f;
        hlgAi.childControlWidth = true;
        hlgAi.childControlHeight = true;
        hlgAi.childForceExpandWidth = true;
        hlgAi.childForceExpandHeight = true;
        var aiLe = aiRow.AddComponent<LayoutElement>();
        aiLe.preferredHeight = 34f;

        var btnAiToggle = MakeButton(aiRow.transform, "AI推演: 关", 34f, ToggleAiReview);
        btnAiReviewLabel = btnAiToggle.GetComponentInChildren<Text>();

        var btnFull = MakeButton(aiRow.transform, "全局分析", 34f, OnFullGameAnalyzeClicked);
        btnFullAnalyzeLabel = btnFull.GetComponentInChildren<Text>();

        reviewAiRoot.SetActive(false);
    }

    void OnChartClick(PointerEventData data, GameObject chartGo)
    {
        if (!reviewingKifu || reviewTotal <= 0) return;
        var rt = chartGo.GetComponent<RectTransform>();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, data.position, data.pressEventCamera, out Vector2 local))
        {
            float w = rt.rect.width;
            float u = Mathf.Clamp01((local.x - rt.rect.xMin) / w);
            int targetPly = Mathf.Clamp(Mathf.RoundToInt(u * reviewTotal), 0, reviewTotal);
            ReviewSeek(targetPly);
        }
    }

    void DrawWinrateChart(int curPly)
    {
        if (winrateChartTex == null) return;
        int w = winrateChartTex.width;
        int h = winrateChartTex.height;
        var px = new Color32[w * h];

        Color32 bg = new Color32(0x14, 0x11, 0x0E, 0xFA);
        Color32 line50 = new Color32(0x50, 0x42, 0x32, 0x90);
        Color32 grid25 = new Color32(0x32, 0x28, 0x1E, 0x50);
        Color32 wrLine = new Color32(0xDF, 0xA8, 0x3E, 0xFF);
        Color32 curBar = new Color32(0xFF, 0xFF, 0xFF, 0xE5);
        Color32 colBlunder = new Color32(0xEA, 0x3C, 0x3C, 0xFF);
        Color32 colInacc = new Color32(0xFA, 0x8C, 0x16, 0xFF);

        for (int i = 0; i < px.Length; i++) px[i] = bg;

        if (HasGameAnalysisSeries())
        {
            int y50 = h / 2;
            int y25 = h / 4;
            int y75 = h * 3 / 4;
            for (int x = 0; x < w; x++)
            {
                if (x % 4 != 0) px[y50 * w + x] = line50;
                if (x % 8 == 0)
                {
                    px[y25 * w + x] = grid25;
                    px[y75 * w + x] = grid25;
                }
            }
        }

        if (HasGameAnalysisSeries() && reviewTotal > 0)
        {
            float[] wrs = gameAnalysis.blackWinrates;
            int total = Mathf.Min(reviewTotal, wrs.Length - 1);
            if (total <= 0)
            {
                winrateChartTex.SetPixels32(px);
                winrateChartTex.Apply(false, false);
                return;
            }
            int prevX = 0;
            int prevY = Mathf.Clamp(Mathf.RoundToInt(wrs[0] * (h - 4)) + 2, 2, h - 3);

            for (int x = 1; x < w; x++)
            {
                float plyF = (x / (float)(w - 1)) * total;
                int p0 = Mathf.Clamp(Mathf.FloorToInt(plyF), 0, total);
                int p1 = Mathf.Clamp(Mathf.CeilToInt(plyF), 0, total);
                float t = plyF - p0;
                float wr = Mathf.Lerp(wrs[p0], wrs[p1], t);
                int cy = Mathf.Clamp(Mathf.RoundToInt(wr * (h - 4)) + 2, 2, h - 3);

                DrawLine(px, w, h, prevX, prevY, x, cy, wrLine);
                prevX = x;
                prevY = cy;
            }

            if (gameAnalysis.blunders != null)
            {
                for (int ply = 1; ply <= total; ply++)
                {
                    if (ply >= gameAnalysis.blunders.Length) continue;
                    int b = gameAnalysis.blunders[ply];
                    if (b > 0)
                    {
                        int bx = Mathf.RoundToInt((ply / (float)total) * (w - 1));
                        int by = Mathf.Clamp(Mathf.RoundToInt(wrs[ply] * (h - 4)) + 2, 2, h - 3);
                        DrawDot(px, w, h, bx, by, b == 2 ? 3 : 2, b == 2 ? colBlunder : colInacc);
                    }
                }
            }

            int cx = Mathf.Clamp(Mathf.RoundToInt((curPly / (float)total) * (w - 1)), 0, w - 1);
            for (int y = 0; y < h; y++)
            {
                px[y * w + cx] = curBar;
                if (cx > 0) px[y * w + cx - 1] = new Color32(0xFF, 0xFF, 0xFF, 0x40);
                if (cx < w - 1) px[y * w + cx + 1] = new Color32(0xFF, 0xFF, 0xFF, 0x40);
            }
        }
        winrateChartTex.SetPixels32(px);
        winrateChartTex.Apply(false, false);
    }

    static void DrawDot(Color32[] px, int w, int h, int cx, int cy, int r, Color32 c)
    {
        for (int y = cy - r; y <= cy + r; y++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                px[y * w + x] = c;
        }
    }

    static void DrawLine(Color32[] px, int w, int h, int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if (x0 >= 0 && x0 < w && y0 >= 0 && y0 < h)
            {
                px[y0 * w + x0] = c;
                if (y0 + 1 < h) px[(y0 + 1) * w + x0] = c;
            }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    void UpdateReviewInfoText(int ply)
    {
        if (winrateInfoText == null) return;
        if (inBranchMode)
        {
            string who = rules.Turn == GoRules.BLACK ? "黑方" : "白方";
            if (curPosAnalysis != null && curPosAnalysis.candidates != null && curPosAnalysis.candidates.Count > 0)
            {
                float wr = curPosAnalysis.blackWinrate;
                float lead = curPosAnalysis.blackScoreLead;
                string leadStr = lead > 0.05f ? $"黑+{lead:0.0}目" : lead < -0.05f ? $"白+{(-lead):0.0}目" : "均势";
                winrateInfoText.text = $"<color=#EAA83E>【试下第 {branchMoveCount} 步】{who}走 · 黑胜率 {(wr * 100f):0.0}% ({leadStr})</color>";
            }
            else
            {
                winrateInfoText.text = branchMoveCount > 0 && aiReviewEnabled
                    ? $"<color=#EAA83E>【试下第 {branchMoveCount} 步】轮到{who}落子 · AI思考中…</color>"
                    : "暂无分析";
            }
            return;
        }
        if (HasGameAnalysisAt(ply))
        {
            float wr = gameAnalysis.blackWinrates[ply];
            float lead = gameAnalysis.blackScoreLeads[ply];
            int b = gameAnalysis.blunders != null && ply < gameAnalysis.blunders.Length ? gameAnalysis.blunders[ply] : 0;
            string tag = b == 2 ? " <color=#EB3B3B>[恶手]</color>" : b == 1 ? " <color=#FA8C16>[失误]</color>" : "";
            string leadStr = lead > 0.05f ? $"黑+{lead:0.0}目" : lead < -0.05f ? $"白+{(-lead):0.0}目" : "均势";
            winrateInfoText.text = $"第 {ply} 手 · 黑胜率 {(wr * 100f):0.0}% ({leadStr}){tag}";
        }
        else if (curPosAnalysis != null && curPosAnalysis.candidates != null && curPosAnalysis.candidates.Count > 0)
        {
            float wr = curPosAnalysis.blackWinrate;
            float lead = curPosAnalysis.blackScoreLead;
            string leadStr = lead > 0.05f ? $"黑+{lead:0.0}目" : lead < -0.05f ? $"白+{(-lead):0.0}目" : "均势";
            winrateInfoText.text = $"第 {ply} 手 · 黑胜率 {(wr * 100f):0.0}% ({leadStr})";
        }
        else
        {
            winrateInfoText.text = "暂无分析";
        }
    }

    void ToggleAiReview()
    {
        aiReviewEnabled = !aiReviewEnabled;
        if (btnAiReviewLabel != null)
            btnAiReviewLabel.text = aiReviewEnabled ? "AI推演: 开" : "AI推演: 关";
        if (aiReviewEnabled)
        {
            if (!GoKataGo.IsAvailable())
            {
                Flash(GoKataGo.InstallHint());
                aiReviewEnabled = false;
                if (btnAiReviewLabel != null) btnAiReviewLabel.text = "AI推演: 关";
                return;
            }
            RequestPositionAnalysis();
        }
        else
        {
            InvalidatePositionAnalysis();
            RefreshReviewAnalysisDisplay();
        }
    }

    void OnFullGameAnalyzeClicked()
    {
        if (isAnalyzingFullGame)
        {
            GoKataGo.CancelAnalysis();
            isAnalyzingFullGame = false;
            if (btnFullAnalyzeLabel != null) btnFullAnalyzeLabel.text = "全局分析";
            Flash("已取消全局分析");
            return;
        }

        if (!GoKataGo.IsAvailable())
        {
            Flash(GoKataGo.InstallHint());
            return;
        }

        StartFullGameAnalysis();
    }

    void StartFullGameAnalysis()
    {
        if (fullAnalysisCo != null) StopCoroutine(fullAnalysisCo);
        isAnalyzingFullGame = true;
        if (btnFullAnalyzeLabel != null) btnFullAnalyzeLabel.text = "分析 0% [取消]";

        fullAnalysisCo = StartCoroutine(GoKataGo.AnalyzeFullGame(
            boardSize,
            rules.Komi,
            reviewRec != null ? reviewRec.handicap : 0,
            HandicapPoints,
            reviewLogCsv,
            reviewTotal,
            30,
            (ply, total, wr, lead) =>
            {
                if (btnFullAnalyzeLabel != null)
                {
                    int pct = total > 0 ? (ply * 100 / total) : 0;
                    btnFullAnalyzeLabel.text = $"分析 {pct}% [取消]";
                }
                if (winrateInfoText != null)
                {
                    winrateInfoText.text = $"分析中 {ply}/{total} 手…";
                }
            },
            (result) =>
            {
                isAnalyzingFullGame = false;
                fullAnalysisCo = null;
                if (btnFullAnalyzeLabel != null) btnFullAnalyzeLabel.text = "全局分析";
                if (result != null)
                {
                    gameAnalysis = result;
                    if (reviewRec != null)
                    {
                        reviewRec.analysisJson = result.ToJson();
                        KifuDatabase.UpdateAnalysis(reviewRec.id, reviewRec.analysisJson);
                    }
                    DrawWinrateChart(reviewPly);
                    UpdateReviewInfoText(reviewPly);
                    RefreshReviewAnalysisDisplay();
                    Flash("全局胜率分析已完成！");
                }
            }
        ));
    }

    void RequestPositionAnalysis()
    {
        if (!aiReviewEnabled || !reviewingKifu) return;
        if (inBranchMode && (curBranchIdx < 0 || curBranchIdx >= reviewBranches.Count || curBranchStepIdx < 0))
        {
            InvalidatePositionAnalysis();
            RefreshReviewAnalysisDisplay();
            return;
        }
        InvalidatePositionAnalysis();
        int requestToken = reviewAnalysisToken;
        bool targetInBranch = inBranchMode;
        int targetBranchId = -1;
        int targetBranchStep = -1;
        if (targetInBranch && curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count)
        {
            targetBranchId = reviewBranches[curBranchIdx].branchId;
            targetBranchStep = curBranchStepIdx;
        }
        int targetPly = reviewPly;
        RefreshReviewAnalysisDisplay();
        curAnalysisCo = StartCoroutine(DoPositionAnalysis(
            requestToken, targetInBranch, targetBranchId, targetBranchStep, targetPly));
    }

    IEnumerator DoPositionAnalysis(
        int requestToken,
        bool targetInBranch,
        int targetBranchId,
        int targetBranchStep,
        int targetPly)
    {
        int side = rules.Turn;
        yield return GoKataGo.AnalyzePosition(
            rules,
            side,
            reviewRec != null ? reviewRec.handicap : 0,
            HandicapPoints,
            60,
            (pos) =>
            {
                if (!reviewingKifu || !IsReviewAnalysisTargetCurrent(
                    requestToken,
                    reviewAnalysisToken,
                    targetInBranch,
                    targetBranchId,
                    curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count ? reviewBranches[curBranchIdx].branchId : -1,
                    targetBranchStep,
                    curBranchStepIdx,
                    targetPly,
                    reviewPly,
                    inBranchMode))
                    return;

                curPosAnalysis = pos;
                if (pos != null && pos.candidates != null && pos.candidates.Count > 0)
                {
                    float delta = 0f;
                    if (branchLastWinrate >= 0f)
                    {
                        delta = pos.blackWinrate - branchLastWinrate;
                    }
                    UpdateWinrateBar(pos.blackWinrate, pos.blackScoreLead, delta);
                    branchLastWinrate = pos.blackWinrate;

                    if (inBranchMode && curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count)
                    {
                        var br = reviewBranches[curBranchIdx];
                        if (curBranchStepIdx >= 0 && curBranchStepIdx < br.steps.Count)
                        {
                            br.steps[curBranchStepIdx].winrate = pos.blackWinrate;
                            br.steps[curBranchStepIdx].scoreLead = pos.blackScoreLead;
                            br.steps[curBranchStepIdx].hasAnalysis = true;
                            SaveReviewCommentsAndBranches();
                            RefreshReviewMoveLog();
                        }
                    }

                    ShowCandidateBadges(pos);
                    UpdateReviewInfoText(reviewPly);
                    RefreshReviewAnalysisDisplay();
                }
                else
                {
                    HideCandidateBadges();
                    RefreshReviewAnalysisDisplay();
                }
            }
        );
        if (requestToken == reviewAnalysisToken)
        {
            if (curPosAnalysis == null)
                RefreshReviewAnalysisDisplay();
            curAnalysisCo = null;
        }
    }

    void ShowCandidateBadges(KataPositionAnalysis pos)
    {
        HideCandidateBadges();
        if (pos == null || pos.candidates == null || !aiReviewEnabled) return;

        Color[] colors = {
            new Color(0.12f, 0.74f, 0.38f, 0.95f), // Emerald Green
            new Color(0.18f, 0.60f, 0.92f, 0.92f), // Sky Blue
            new Color(0.94f, 0.62f, 0.16f, 0.92f), // Amber
            new Color(0.85f, 0.45f, 0.85f, 0.92f), // Purple
            new Color(0.65f, 0.65f, 0.65f, 0.90f)  // Gray
        };

        int count = Mathf.Min(pos.candidates.Count, MaxCandidates);
        activeCandCount = count;

        for (int i = 0; i < count; i++)
        {
            var c = pos.candidates[i];
            if (c.x < 0 || c.y < 0 || c.x >= boardSize || c.y >= boardSize) continue;

            candBadges[i].transform.position = new Vector3(c.x, c.y, 0f);
            candSrs[i].color = colors[i % colors.Length];

            char letter = (char)('A' + i);
            int wrPct = Mathf.RoundToInt(c.winrate * 100f);
            string leadStr = (c.scoreLead >= 0 ? "+" : "") + c.scoreLead.ToString("0.0");
            candTexts[i].text = $"{letter} {wrPct}%\n{leadStr}";

            candBadges[i].SetActive(true);
            candIdxs[i] = rules.Idx(c.x, c.y);
        }

        if (count > 0)
        {
            hoveredCandidateIdx = 0;
            ShowPv(pos.candidates[0]);
            UpdateCandidateDetailText(pos.candidates[0]);
        }
    }

    void HideCandidateBadges()
    {
        activeCandCount = 0;
        hoveredCandidateIdx = -1;
        for (int i = 0; i < MaxCandidates; i++)
        {
            if (candBadges[i] != null) candBadges[i].SetActive(false);
            candIdxs[i] = -1;
        }
        HidePv();
    }

    void ShowPv(KataCandidate cand)
    {
        HidePv();
        if (cand == null || cand.pv == null || cand.pv.Count == 0 || !aiReviewEnabled) return;

        int side = rules.Turn;
        int shown = 0;

        for (int i = 0; i < cand.pv.Count && shown < MaxPvSteps; i++)
        {
            string s = cand.pv[i];
            if (!GoKataGo.ParseCoord(s, out int x, out int y)) continue;
            if (x < 0 || y < 0 || x >= boardSize || y >= boardSize) continue;

            var go = pvStoneSrs[shown].gameObject;
            go.transform.position = new Vector3(x, y, 0f);

            bool isBlack = side == GoRules.BLACK;
            boardSkin.SetPointSprite(pvStoneSrs[shown], isBlack ? stoneBlack : stoneWhite);
            pvStoneSrs[shown].color = new Color(1f, 1f, 1f, 0.78f);

            pvTextMeshes[shown].text = (shown + 1).ToString();
            pvTextMeshes[shown].color = isBlack ? MoveNumOnBlack : MoveNumOnWhite;

            go.SetActive(true);
            shown++;
            side = GoRules.Opp(side);
        }
    }

    void HidePv()
    {
        for (int i = 0; i < MaxPvSteps; i++)
        {
            if (pvStoneSrs[i] != null && pvStoneSrs[i].gameObject != null)
                pvStoneSrs[i].gameObject.SetActive(false);
        }
    }

    void PlayBranchMove(int x, int y)
    {
        if (!reviewingKifu) return;

        InvalidatePositionAnalysis();

        if (isAnalyzingFullGame)
        {
            GoKataGo.CancelAnalysis();
            isAnalyzingFullGame = false;
            if (btnFullAnalyzeLabel != null) btnFullAnalyzeLabel.text = "全局分析";
        }

        rules.ResumeRecording();

        if (x < 0 || y < 0)
        {
            if (!inBranchMode || curBranchIdx < 0 || curBranchIdx >= reviewBranches.Count)
            {
                inBranchMode = true;
                branchPlyStart = reviewPly;
                branchMoveCount = 0;
                var br = new MoveBranchData
                {
                    branchId = reviewBranches.Count + 1,
                    name = $"变化 {reviewBranches.Count + 1}",
                    startPly = reviewPly,
                    isExpanded = true,
                    steps = new List<BranchStepData>()
                };
                reviewBranches.Add(br);
                curBranchIdx = reviewBranches.Count - 1;
                branchLastWinrate = -1f;
            }
            rules.Pass();
            branchMoveCount++;
            curPosAnalysis = null;
            branchLastWinrate = -1f;
            var step = CreateUnanalyzedBranchStep(
                branchMoveCount, -1, -1, GoRules.Opp(rules.Turn), true);
            reviewBranches[curBranchIdx].steps.Add(step);
            curBranchStepIdx = reviewBranches[curBranchIdx].steps.Count - 1;
            ClearWinrateBar();
            RefreshReviewAnalysisDisplay();

            HideCandidateBadges();
            Refresh();
            if (btnExitBranch != null) btnExitBranch.SetActive(true);
            if (btnBranchUndo != null) btnBranchUndo.SetActive(true);
            Flash($"【{reviewBranches[curBranchIdx].name}】第 {branchMoveCount} 步停一手，AI深入推演中…");
            SaveReviewCommentsAndBranches();
            if (aiReviewEnabled) RequestPositionAnalysis();
            else UpdateReviewInfoText(reviewPly);
            return;
        }

        if (x >= boardSize || y >= boardSize) return;

        if (rules.Board[rules.Idx(x, y)] != GoRules.EMPTY)
        {
            Flash("该位置已有棋子");
            return;
        }

        if (!inBranchMode || curBranchIdx < 0 || curBranchIdx >= reviewBranches.Count)
        {
            inBranchMode = true;
            branchPlyStart = reviewPly;
            branchMoveCount = 0;
            var br = new MoveBranchData
            {
                branchId = reviewBranches.Count + 1,
                name = $"变化 {reviewBranches.Count + 1}",
                startPly = reviewPly,
                isExpanded = true,
                steps = new List<BranchStepData>()
            };
            reviewBranches.Add(br);
            curBranchIdx = reviewBranches.Count - 1;
            branchLastWinrate = -1f;
        }

        if (rules.Play(x, y, out string msg))
        {
            branchMoveCount++;
            int movedColor = GoRules.Opp(rules.Turn);
            curPosAnalysis = null;
            branchLastWinrate = -1f;
            var step = CreateUnanalyzedBranchStep(
                branchMoveCount, x, y, movedColor, false);
            reviewBranches[curBranchIdx].steps.Add(step);
            curBranchStepIdx = reviewBranches[curBranchIdx].steps.Count - 1;
            ClearWinrateBar();
            RefreshReviewAnalysisDisplay();

            HideCandidateBadges();
            Refresh();
            if (btnExitBranch != null) btnExitBranch.SetActive(true);
            if (btnBranchUndo != null) btnBranchUndo.SetActive(true);

            string who = movedColor == GoRules.BLACK ? "黑" : "白";
            string coordStr = GoKataGo.Coord(x, y);
            Flash($"【{reviewBranches[curBranchIdx].name}】第 {branchMoveCount} 步（{who}走 {coordStr}），AI深入推演中…");
            SaveReviewCommentsAndBranches();

            if (aiReviewEnabled)
            {
                RequestPositionAnalysis();
            }
            else
            {
                UpdateReviewInfoText(reviewPly);
            }
        }
        else
        {
            Flash(msg);
        }
    }

    void StepAiCandidate()
    {
        if (!reviewingKifu) return;
        if (!aiReviewEnabled)
        {
            ToggleAiReview();
            if (!aiReviewEnabled) return;
        }
        if (curPosAnalysis == null || curPosAnalysis.candidates == null || curPosAnalysis.candidates.Count == 0)
        {
            Flash("AI 选点计算中，请稍候…");
            return;
        }
        int idx = (hoveredCandidateIdx >= 0 && hoveredCandidateIdx < curPosAnalysis.candidates.Count) ? hoveredCandidateIdx : 0;
        var c = curPosAnalysis.candidates[idx];
        PlayBranchMove(c.x, c.y);
    }

    void UndoBranchMove()
    {
        if (!reviewingKifu || !inBranchMode) return;
        InvalidatePositionAnalysis();

        if (branchMoveCount <= 1)
        {
            rules.Undo(1);
            if (curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count)
            {
                var br = reviewBranches[curBranchIdx];
                if (br.steps.Count > 0) br.steps.RemoveAt(br.steps.Count - 1);
            }
            ExitBranchMode();
            return;
        }

        rules.Undo(1);
        branchMoveCount--;
        if (curBranchIdx >= 0 && curBranchIdx < reviewBranches.Count)
        {
            var br = reviewBranches[curBranchIdx];
            if (br.steps.Count > 0) br.steps.RemoveAt(br.steps.Count - 1);
            curBranchStepIdx = br.steps.Count - 1;
        }
        branchLastWinrate = -1f;
        rules.EnsureMoveAtForDisplay();
        HideCandidateBadges();
        Refresh();
        Flash($"已撤销一手，当前为试下第 {branchMoveCount} 步");
        SaveReviewCommentsAndBranches();

        if (aiReviewEnabled)
        {
            RequestPositionAnalysis();
        }
        else
        {
            UpdateReviewInfoText(reviewPly);
        }
    }

    void ExitBranchMode()
    {
        ExitBranchMode(true);
    }

    void ExitBranchMode(bool requestAnalysis)
    {
        InvalidatePositionAnalysis();
        inBranchMode = false;
        curBranchIdx = -1;
        curBranchStepIdx = -1;
        branchMoveCount = 0;
        branchLastWinrate = -1f;
        if (btnExitBranch != null) btnExitBranch.SetActive(false);
        if (btnBranchUndo != null) btnBranchUndo.SetActive(false);
        ReviewSeek(branchPlyStart, requestAnalysis);
        Flash("已恢复实战局面");
    }

    void UpdateWinrateBar(float blackWr, float blackScoreLead, float deltaWr = 0f)
    {
        if (barBlackRt == null || barWhiteRt == null || winrateBarText == null) return;
        if (winrateBarRoot != null) winrateBarRoot.SetActive(true);
        blackWr = Mathf.Clamp(blackWr, 0.02f, 0.98f);
        barBlackRt.anchorMax = new Vector2(blackWr, 1f);
        barWhiteRt.anchorMin = new Vector2(blackWr, 0f);

        float bPct = blackWr * 100f;
        float wPct = (1f - blackWr) * 100f;
        string leadStr = blackScoreLead > 0.05f ? $"黑+{blackScoreLead:0.0}目" 
                       : blackScoreLead < -0.05f ? $"白+{(-blackScoreLead):0.0}目" 
                       : "双方均势";

        string deltaStr = "";
        if (Mathf.Abs(deltaWr) >= 0.005f)
        {
            float dPct = deltaWr * 100f;
            deltaStr = dPct > 0 
                ? $" <color=#73D13D>(+{dPct:0.0}%)</color>" 
                : $" <color=#FF4D4F>({dPct:0.0}%)</color>";
        }

        winrateBarText.text = $"黑 {bPct:0.0}% | 白 {wPct:0.0}% ({leadStr}){deltaStr}";
    }

    void ClearWinrateBar()
    {
        if (winrateBarRoot != null) winrateBarRoot.SetActive(false);
        if (winrateBarText != null) winrateBarText.text = "暂无分析";
    }

    void UpdateCandidateDetailText(KataCandidate c)
    {
        if (candInfoText == null) return;
        if (c == null)
        {
            candInfoText.text = "暂无分析";
            if (pvInfoText != null) pvInfoText.text = "PV：暂无分析";
            return;
        }

        char letter = (char)('A' + Mathf.Clamp(c.order, 0, 25));
        string pvStr = FormatPvShort(c.pv, 6);
        string leadStr = (c.scoreLead >= 0 ? "+" : "") + c.scoreLead.ToString("0.0");
        int wrPct = Mathf.RoundToInt(c.winrate * 100f);
        string sideName = rules.Turn == GoRules.BLACK ? "黑" : "白";

        candInfoText.text = $"<b>候选{letter}（{c.coord}）</b> {sideName}胜率:<b>{wrPct}%</b> (领先:<b>{leadStr}目</b>)\n<color=#D4AF37>推荐序列: {pvStr}</color>";
        if (pvInfoText != null)
            pvInfoText.text = c.pv != null && c.pv.Count > 0 ? $"PV：{pvStr}" : "PV：暂无分析";
    }

    static string FormatPvShort(List<string> pv, int maxSteps)
    {
        if (pv == null || pv.Count == 0) return "-";
        int cnt = Mathf.Min(pv.Count, maxSteps);
        var sb = new System.Text.StringBuilder(cnt * 5);
        for (int i = 0; i < cnt; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(pv[i]);
        }
        if (pv.Count > maxSteps) sb.Append("…");
        return sb.ToString();
    }

    void OnApplicationQuit()
    {
        if (audioManager != null) audioManager.SavePrefs();
        GoKataGo.Shutdown();
    }

    void OnDestroy()
    {
        StopPlayClock();
        if (audioManager != null) audioManager.SavePrefs();
        GoKataGo.Shutdown();
    }
}
