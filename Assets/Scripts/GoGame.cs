using System.Collections;
using System.Collections.Generic;
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
public class GoGame : MonoBehaviour
{
    GoRules rules;
    int boardSize = 19;
    bool vsAI = false;
    bool inStudyMode = false;
    bool reviewingKifu = false;
    KifuRecord reviewRec;
    string reviewLogCsv = "";
    int reviewPly, reviewTotal;
    GameObject btnUndo, btnPass, btnResign, btnClear, btnScore, btnReviewPrev, btnReviewNext, btnReviewEnd;
    Text homeBtnLabel;
    bool aiBusy = false;
    bool inMenu = true;

    // KataGo 复盘分析与变化推演状态
    bool aiReviewEnabled = false;
    bool isAnalyzingFullGame = false;
    KataPositionAnalysis curPosAnalysis;
    KataGameAnalysis gameAnalysis;
    Coroutine curAnalysisCo, fullAnalysisCo;
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

    RectTransform barBlackRt, barWhiteRt;
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
    int humanColor = GoRules.BLACK;
    int aiColor => GoRules.Opp(humanColor);
    List<Button> optOpponent;
    InputField llmKeyInput;

    Camera cam;
    Transform boardRoot;
    SpriteRenderer boardSr;
    SpriteRenderer[,] stoneSr;
    SpriteRenderer[,] deadSr;
    SpriteRenderer[,] terrSr;
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

    TextMesh[,] moveNumMeshes;
    SpriteRenderer ghostSr, markerSr;
    Sprite stoneBlack, stoneWhite, deadXSpr, ringSpr, boxSpr;
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
    GameObject studyBoardUi, studyLabelRoot;
    Text studyTerritoryText, studyBtnMovesText, studyBtnCoordsText, studyBtnTerrText;
    Text[] studyColBot, studyColTop, studyRowLeft, studyRowRight;
    static readonly Color MoveNumOnBlack = Color.white;
    static readonly Color MoveNumOnWhite = new Color(0.08f, 0.08f, 0.08f);
    bool showMoveNums, showCoords, showTerritory;
    Coroutine msgCo;

    // 主界面
    GameObject menuCanvas, mainPanel, aiSetupPanel, historyPanel, historyDetail, settingsPanel;
    InputField historySearch;
    Transform historyListContent;
    Text historyDetailText;
    List<Button> optSize, optColor;
    Text komiValText, handiValText;

    GoAudioManager audioManager;
    Slider quickMasterVolumeSlider;
    Button quickMuteButton;
    Slider settingsMasterSlider, settingsMusicSlider, settingsSfxSlider, settingsWarningSlider;
    Toggle settingsMuteToggle, settingsReduceMotionToggle;
    static readonly string ReduceMotionKey = "Qiyi.ReduceMotion";

    GoUiTheme uiTheme;
    GoUiFactory uiFactory;
    GoUiNavigator uiNavigator;
    GameObject uiAppRoot, sceneBackgroundLayer, mainCanvasLayer, modalCanvasLayer, transitionLayer;
    GameObject gameUiPanel;

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

        stoneBlack = MakeStoneSprite(true);
        stoneWhite = MakeStoneSprite(false);
        deadXSpr = MakeDeadXSprite();
        ringSpr = MakeRingSprite();
        boxSpr = MakeBoxSprite();
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
        ShowMainMenu();
    }

    /* ---------------- 对局流程 ---------------- */

    void StartStudyGame()
    {
        audioManager?.PlaySfx("button_confirm");
        audioManager?.PlayMusic("bgm_study");
        inStudyMode = true;
        vsAI = false;
        boardSize = StudyBoardSize;
        showMoveNums = true;
        aiBusy = false;
        aiController?.Cancel();
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
            Flash(GoKataGo.InstallHint());
            return;
        }
        audioManager?.PlaySfx("button_confirm");
        audioManager?.PlayMusic("bgm_study");
        inStudyMode = false;
        vsAI = true;
        showMoveNums = true;
        boardSize = sizeChoice == 0 ? 9 : sizeChoice == 1 ? 13 : 19;
        humanColor = colorChoice == 0 ? GoRules.BLACK
                   : colorChoice == 1 ? GoRules.WHITE
                   : (Random.value < 0.5f ? GoRules.BLACK : GoRules.WHITE);

        aiBusy = false;
        aiController?.Cancel();
        StopAllCoroutines();
        rules.Reset(boardSize);
        rules.Komi = komi;
        if (handicap > 0) rules.SetupHandicap(HandicapPoints(boardSize, handicap));

        EnterGame();
        Flash(colorChoice == 2
            ? "猜先结果：你" + (humanColor == GoRules.BLACK ? "执黑" : "执白")
            : humanColor == GoRules.BLACK ? "你执黑" : "你执白");
        if (opponentChoice == 1) Flash("电脑由 KataGo 对弈");
        else if (opponentChoice == 2) Flash("电脑由大模型选点");
        if (rules.Result == null && rules.Phase == GoPhase.Play && rules.Turn == aiColor)
            StartCoroutine(AiTurn());
    }

    void EnterGame()
    {
        BuildBoard();
        inMenu = false;
        reviewingKifu = false;
        menuCanvas.SetActive(false);
        if (uiNavigator != null)
            uiNavigator.Show(vsAI ? GoUiPage.Playing : GoUiPage.Study);
        gameStartedAt = System.DateTime.Now;
        lastResultLine = "";
        kifuDraftReady = false;
        if (kifuForm != null) kifuForm.SetActive(false);
        Refresh();
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
            Debug.LogWarning("自动保存打谱进度失败: " + err);
        }
    }

    void ReturnToMainMenu()
    {
        HideScorePopup();
        if (reviewingKifu)
        {
            if (isAnalyzingFullGame) { GoKataGo.CancelAnalysis(); isAnalyzingFullGame = false; }
            if (fullAnalysisCo != null) { StopCoroutine(fullAnalysisCo); fullAnalysisCo = null; }
            if (curAnalysisCo != null) { StopCoroutine(curAnalysisCo); curAnalysisCo = null; }
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
            aiBusy = false;
            StopAllCoroutines();
            ShowHistoryPanel();
            return;
        }
        if (inStudyMode) SaveStudyProgress();
        inStudyMode = false;
        vsAI = false;
        aiBusy = false;
        aiController?.Cancel();
        StopAllCoroutines();
        GoKataGo.Shutdown();
        ShowMainMenu();
    }

    void ShowMainMenu()
    {
        if (audioManager != null) audioManager.SavePrefs();
        inMenu = true;
        reviewingKifu = false;
        menuCanvas.SetActive(true);
        if (uiNavigator != null) uiNavigator.Show(GoUiPage.MainMenu);
        mainPanel.SetActive(true);
        aiSetupPanel.SetActive(false);
        if (historyPanel != null) historyPanel.SetActive(false);
        if (historyDetail != null) historyDetail.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        SetVolumeQuickVisible(true);
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
        historyPanel.SetActive(true);
        if (historyDetail != null) historyDetail.SetActive(false);
        if (historySearch != null) historySearch.text = "";
        RebuildHistoryList(historySearch != null ? historySearch.text : "");
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
        AfterMove();
    }

    void HumanPass()
    {
        if (reviewingKifu) { ReviewSeek(reviewPly + 1); return; }
        if (aiBusy || inMenu || rules.Result != null || rules.Phase != GoPhase.Play) return;
        if (vsAI && rules.Turn != humanColor) return;
        rules.Pass();
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
        aiBusy = true;
        Refresh();
        yield return null;

        GoMoveResult result = null;
        string notice = null;

        yield return aiController.RequestAiMove(
            rules,
            aiColor,
            opponentChoice,
            boardSize,
            handicap,
            HandicapPoints,
            (res, fallback) =>
            {
                result = res;
                notice = fallback;
            });

        if (!string.IsNullOrEmpty(notice)) Flash(notice);

        if (result != null && result.success)
        {
            if (result.isResign)
            {
                rules.ResignBy(aiColor);
                ShowResultPopup(rules.Result);
            }
            else if (result.isPass)
            {
                rules.Pass();
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
            }
        }
        else if (result != null && !string.IsNullOrEmpty(result.error))
        {
            Flash("AI 无法落子: " + result.error);
        }

        aiBusy = false;
        Refresh();
    }

    void Undo()
    {
        if (reviewingKifu) { ReviewSeek(reviewPly - 1); return; }
        if (aiBusy || inMenu) return;
        aiController?.Cancel();
        int steps = vsAI ? 2 : 1;
        if (!rules.Undo(steps)) { Flash("没有可悔的棋"); return; }
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
        if (inMenu)
        {
            if (ghostSr != null) ghostSr.enabled = false;
            if (ghostMarkSr != null) ghostMarkSr.enabled = false;
            return;
        }
        Vector3 w = cam.ScreenToWorldPoint(Input.mousePosition);
        int x = Mathf.RoundToInt(w.x), y = Mathf.RoundToInt(w.y);
        bool inside = x >= 0 && y >= 0 && x < boardSize && y < boardSize
                      && Mathf.Abs(w.x - x) < 0.48f && Mathf.Abs(w.y - y) < 0.48f;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        bool canHover = !reviewingKifu && inside && !overUI && rules.Phase == GoPhase.Play && rules.Result == null
                        && !aiBusy && rules.Board[rules.Idx(x, y)] == GoRules.EMPTY
                        && (!vsAI || rules.Turn == humanColor);
        ghostSr.enabled = canHover;
        if (canHover)
        {
            ghostSr.sprite = rules.Turn == GoRules.BLACK ? stoneBlack : stoneWhite;
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
                    ghostMarkSr.sprite = GetMarkSprite(activeMarkTool);
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
                    ghostSr.sprite = rules.Turn == GoRules.BLACK ? stoneBlack : stoneWhite;
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
            if (rules.Phase == GoPhase.Scoring) { rules.ToggleDead(x, y); Refresh(); }
            else HumanPlay(x, y);
        }
    }

    /* ---------------- 棋盘渲染 ---------------- */

    void BuildBoard()
    {
        if (boardRoot != null) Destroy(boardRoot.gameObject);
        boardRoot = new GameObject("BoardRoot").transform;
        int n = boardSize;
        int cellPx = n >= 19 ? 72 : n >= 13 ? 100 : 140;

        var bo = new GameObject("Board");
        bo.transform.SetParent(boardRoot, false);
        boardSr = bo.AddComponent<SpriteRenderer>();
        boardSr.sprite = MakeBoardSprite(n, cellPx);
        boardSr.sortingOrder = 0;
        bo.transform.position = new Vector3((n - 1) / 2f, (n - 1) / 2f, 0f);

        stoneSr = new SpriteRenderer[n, n];
        deadSr = new SpriteRenderer[n, n];
        terrSr = new SpriteRenderer[n, n];
        moveNumMeshes = new TextMesh[n, n];
        markSr = new SpriteRenderer[n, n];
        if (boardMarks == null || boardMarks.Length != n * n)
        {
            boardMarks = new int[n * n];
        }
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            var so = new GameObject($"s{x}_{y}");
            so.transform.SetParent(boardRoot, false);
            so.transform.position = new Vector3(x, y, 0f);
            so.transform.localScale = Vector3.one * 0.94f;
            var sr = so.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 1; sr.enabled = false;
            stoneSr[x, y] = sr;

            var xo = new GameObject("deadX");
            xo.transform.SetParent(so.transform, false);
            xo.transform.localPosition = Vector3.zero;
            var xr = xo.AddComponent<SpriteRenderer>();
            xr.sprite = deadXSpr; xr.sortingOrder = 2; xr.enabled = false;
            deadSr[x, y] = xr;

            var to = new GameObject("terr");
            to.transform.SetParent(so.transform, false);
            to.transform.localPosition = Vector3.zero;
            to.transform.localScale = Vector3.one * 0.5f;
            var tsr = to.AddComponent<SpriteRenderer>();
            tsr.sprite = boxSpr;
            tsr.sortingOrder = 1;
            tsr.enabled = false;
            terrSr[x, y] = tsr;

            var mo = new GameObject("mark");
            mo.transform.SetParent(so.transform, false);
            mo.transform.localPosition = Vector3.zero;
            var msr = mo.AddComponent<SpriteRenderer>();
            msr.sortingOrder = 5;
            msr.enabled = false;
            markSr[x, y] = msr;

            var numGo = new GameObject("MoveNum");
            numGo.transform.SetParent(so.transform, false);
            numGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            numGo.transform.localRotation = Quaternion.identity;
            numGo.transform.localScale = Vector3.one;
            var tm = numGo.AddComponent<TextMesh>();
            tm.font = numberFont;
            tm.fontSize = 32;
            tm.characterSize = 0.095f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Normal;
            tm.richText = false;
            tm.text = "";
            var mr = numGo.GetComponent<MeshRenderer>();
            if (numberFont != null && numberFont.material != null)
                mr.sharedMaterial = numberFont.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = 4;
            numGo.SetActive(false);
            moveNumMeshes[x, y] = tm;
        }

        var gh = new GameObject("Ghost");
        gh.transform.SetParent(boardRoot, false);
        gh.transform.localScale = Vector3.one * 0.94f;
        ghostSr = gh.AddComponent<SpriteRenderer>();
        ghostSr.sortingOrder = 3;
        ghostSr.color = new Color(1f, 1f, 1f, 0.45f);
        ghostSr.enabled = false;

        var gm = new GameObject("GhostMark");
        gm.transform.SetParent(boardRoot, false);
        gm.transform.localScale = Vector3.one * 0.94f;
        ghostMarkSr = gm.AddComponent<SpriteRenderer>();
        ghostMarkSr.sortingOrder = 5;
        ghostMarkSr.enabled = false;

        var mk = new GameObject("Marker");
        mk.transform.SetParent(boardRoot, false);
        markerSr = mk.AddComponent<SpriteRenderer>();
        markerSr.sprite = ringSpr;
        markerSr.sortingOrder = 3;
        markerSr.enabled = false;

        cam.orthographicSize = (n + 1) / 2f + 0.7f;
        cam.transform.position = new Vector3((n - 1) / 2f, (n - 1) / 2f, -10f);
        BuildStudyBoardLabels(n);
        BuildAnalysisOverlays();
        UpdateBoardMarksVisuals();
    }

    static int[] StarPoints(int n) => GoRules.StarPoints(n);

    static void FillRect(Color32[] px, int res, int x0, int y0, int x1, int y1, Color32 c)
    {
        if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0;
        if (x1 > res - 1) x1 = res - 1; if (y1 > res - 1) y1 = res - 1;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            px[y * res + x] = c;
    }

    static void FillCircle(Color32[] px, int res, int cx, int cy, int r, Color32 c)
    {
        for (int y = cy - r; y <= cy + r; y++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            if (x < 0 || y < 0 || x >= res || y >= res) continue;
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) px[y * res + x] = c;
        }
    }

    Sprite MakeBoardSprite(int n, int cellPx)
    {
        int res = cellPx * (n + 1);
        var px = new Color32[res * res];
        Color32 w1 = new Color32(0xE8, 0xB8, 0x60, 0xFF);
        Color32 w2 = new Color32(0xCF, 0x99, 0x3C, 0xFF);
        for (int y = 0; y < res; y++)
        {
            float g = y / (float)res;
            for (int x = 0; x < res; x++)
            {
                float grain = Mathf.Sin(x * 0.045f + Mathf.Sin(y * 0.012f) * 2f) * 0.5f + 0.5f;
                float t = Mathf.Clamp01(g * 0.5f + grain * 0.2f);
                px[y * res + x] = Color32.Lerp(w1, w2, t);
            }
        }
        Color32 line = new Color32(0x50, 0x32, 0x0F, 0xE6);
        for (int i = 0; i < n; i++)
        {
            int c = (i + 1) * cellPx;
            int th = (i == 0 || i == n - 1) ? 4 : 2;
            FillRect(px, res, c - th / 2, cellPx, c + th / 2, res - cellPx, line);
            FillRect(px, res, cellPx, c - th / 2, res - cellPx, c + th / 2, line);
        }
        int sr = Mathf.Max(4, cellPx / 10);
        Color32 star = new Color32(0x50, 0x32, 0x0F, 0xF0);
        foreach (int a in StarPoints(n))
        foreach (int b in StarPoints(n))
            FillCircle(px, res, (a + 1) * cellPx, (b + 1) * cellPx, sr, star);

        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), cellPx);
    }

    Sprite MakeStoneSprite(bool black)
    {
        int s = 128;
        float r = s / 2f - 1.5f;
        var px = new Color32[s * s];
        Vector2 hi = new Vector2(s * 0.36f, s * 0.64f);
        Color32 lite = black ? new Color32(0x5A, 0x5A, 0x5A, 0xFF) : new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        Color32 dark = black ? new Color32(0x06, 0x06, 0x06, 0xFF) : new Color32(0xC2, 0xC2, 0xC2, 0xFF);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = x - s / 2f, dy = y - s / 2f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(r - d);
            if (a <= 0f) { px[y * s + x] = new Color32(0, 0, 0, 0); continue; }
            float t = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), hi) / (r * 1.7f));
            Color32 c = Color32.Lerp(lite, dark, t);
            c.a = (byte)(255 * a);
            px[y * s + x] = c;
        }
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }

    Sprite MakeRingSprite()
    {
        int s = 128;
        float r0 = s * 0.22f, wdt = s * 0.05f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = x - s / 2f, dy = y - s / 2f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(wdt - Mathf.Abs(d - r0));
            px[y * s + x] = new Color32(0xFF, 0xFF, 0xFF, (byte)(255 * a));
        }
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }

    Sprite MakeBoxSprite()
    {
        int s = 32;
        var px = new Color32[s * s];
        Color32 on = new Color32(255, 255, 255, 255);
        for (int i = 0; i < px.Length; i++) px[i] = on;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px); tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }

    Sprite MakeDeadXSprite()
    {
        int s = 128;
        float r = s / 2f - 4f, wdt = s * 0.05f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = x - s / 2f, dy = y - s / 2f;
            if (Mathf.Sqrt(dx * dx + dy * dy) > r) { px[y * s + x] = new Color32(0, 0, 0, 0); continue; }
            float d1 = Mathf.Abs(x - y) / 1.4142f;
            float d2 = Mathf.Abs(x + y - s) / 1.4142f;
            float a = Mathf.Clamp01(wdt - Mathf.Min(d1, d2));
            px[y * s + x] = new Color32(0xC0, 0x39, 0x2B, (byte)(230 * a));
        }
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
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
                sr.sprite = GetMarkSprite((GoMarkType)markVal);
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
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440f, 900f);
        scaler.matchWidthOrHeight = 0.5f;
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
        prt.sizeDelta = new Vector2(280f, -48f);
        prt.anchoredPosition = new Vector2(-28f, 0f);

        var vlg = pgo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 14, 14);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var pt = pgo.transform;

        MakeText(pt, "落子记录与变化推演", 15, Dim, TextAnchor.MiddleCenter);
        BuildMoveLogScroll(pt);
        BuildCommentModuleUi(pt);
        turnText = MakeText(pt, "", 14, Ink, TextAnchor.MiddleCenter);
        msgText = MakeText(pt, "", 14, Accent, TextAnchor.MiddleCenter);

        btnUndo = MakeButton(pt, "悔棋", 42f, Undo).gameObject;
        btnPass = MakeButton(pt, "停一手", 42f, HumanPass).gameObject;
        btnResign = MakeButton(pt, "认输", 42f, HumanResign).gameObject;
        btnClear = MakeButton(pt, "清空棋盘", 42f, ClearBoardKeepPlaying).gameObject;
        btnScore = MakeButton(pt, "数目", 42f, OpenScorePopup).gameObject;
        BuildReviewAiUi(pt);
        var home = MakeButton(pt, "返回主界面", 42f, ReturnToMainMenu);
        homeBtnLabel = home.GetComponentInChildren<Text>();

        BuildScorePopup(canvas.transform);
        BuildKifuForm(canvas.transform);
        BuildStudyBoardUi(canvas.transform);
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

        commentModuleTitle = MakeText(headerRow.transform, "📝 批注：实战", 12, Ink, TextAnchor.MiddleLeft);
        var tLe = commentModuleTitle.gameObject.AddComponent<LayoutElement>();
        tLe.flexibleWidth = 1f;
        tLe.minWidth = 110f;

        commentSaveIndicator = MakeText(headerRow.transform, "", 11, new Color(0.45f, 0.82f, 0.45f), TextAnchor.MiddleRight);
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
        textComp.fontSize = 12;
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
        phComp.fontSize = 12;
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
            ExitBranchMode();
        }

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
        branchLastWinrate = curPosAnalysis != null ? curPosAnalysis.blackWinrate 
            : (gameAnalysis != null && reviewPly < gameAnalysis.blackWinrates.Length ? gameAnalysis.blackWinrates[reviewPly] : 0.5f);

        if (btnExitBranch != null) btnExitBranch.SetActive(true);
        if (btnBranchUndo != null) btnBranchUndo.SetActive(true);

        Flash($"已新建【{br.name}】（以实战第 {reviewPly} 手为起点），请在盘面落子");
        SyncCommentModule();
        RefreshReviewMoveLog();
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
        if (curAnalysisCo != null) { StopCoroutine(curAnalysisCo); curAnalysisCo = null; }

        ReviewSeek(br.startPly);

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
        branchLastWinrate = br.steps.Count > 0 ? br.steps[br.steps.Count - 1].winrate : -1f;

        if (btnExitBranch != null) btnExitBranch.SetActive(true);
        if (btnBranchUndo != null) btnBranchUndo.SetActive(true);

        rules.EnsureMoveAtForDisplay();
        Refresh();
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
        scorePopup = new GameObject("ScorePopup");
        scorePopup.transform.SetParent(canvasRoot, false);
        var overlay = scorePopup.AddComponent<Image>();
        overlay.color = new Color(0.05f, 0.04f, 0.03f, 0.72f);
        overlay.raycastTarget = true;
        var ort = scorePopup.GetComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;

        var card = new GameObject("Card");
        card.transform.SetParent(scorePopup.transform, false);
        var cimg = card.AddComponent<Image>();
        cimg.color = new Color(0.16f, 0.13f, 0.09f, 1f);
        var crt = card.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(460f, 280f);

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 28, 24);
        vlg.spacing = 16f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        scorePopupTitle = MakeText(card.transform, "", 26, Ink, TextAnchor.MiddleCenter);
        var titleLe = scorePopupTitle.gameObject.AddComponent<LayoutElement>();
        titleLe.preferredHeight = 72f;
        titleLe.minHeight = 48f;

        MakeButton(card.transform, "返回主界面", 44f, ScorePopupReturnHome);
        MakeButton(card.transform, "再来一局", 44f, ScorePopupReplay);
        MakeButton(card.transform, "保存棋谱", 44f, ScorePopupSaveKifu);
        scorePopup.SetActive(false);
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
        HideScorePopup();
        WipeBoardState();
        BuildBoard();
        Refresh();
        Flash("棋盘已清空");
    }

    void WipeBoardState()
    {
        rules.Reset(boardSize);
        GoSaveManager.ClearStudySave();
    }

    void HumanResign()
    {
        if (reviewingKifu || aiBusy || inMenu || rules.Result != null) return;
        if (vsAI) rules.ResignBy(humanColor);
        else rules.Resign();
        Refresh();
        ShowResultPopup(rules.Result);
    }

    void OpenScorePopup()
    {
        if (reviewingKifu || scorePopup == null || rules == null) return;
        rules.EstimateSituation(out float bs, out float ws,
            out _, out _, out _, out _, out _, out _, out _);
        float diff = bs - ws;
        string line;
        if (diff > 0.05f) line = $"黑胜{diff:0.0}目";
        else if (diff < -0.05f) line = $"白胜{-diff:0.0}目";
        else line = "双方打平";
        ShowResultPopup(line);
    }

    void ShowResultPopup(string title)
    {
        if (scorePopup == null) return;
        audioManager?.PlaySfx("game_end");
        lastResultLine = title;
        scorePopupTitle.text = title;
        scorePopup.SetActive(true);
        if (kifuForm != null) kifuForm.SetActive(false);
    }

    void HideScorePopup()
    {
        if (scorePopup != null) scorePopup.SetActive(false);
        if (kifuForm != null) kifuForm.SetActive(false);
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
            kifuDraftReady = true;
        }
        kifuTitle.text = draftTitle ?? "";
        kifuBlack.text = draftBlack ?? "";
        kifuWhite.text = draftWhite ?? "";
        kifuBlackRank.text = draftBlackRank ?? "";
        kifuWhiteRank.text = draftWhiteRank ?? "";
        kifuDesc.text = draftDesc ?? "";
        kifuTimeText.text = gameStartedAt == default
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
        if (scorePopup != null) scorePopup.SetActive(true);
    }

    void KifuFormConfirm()
    {
        CaptureKifuDraft();
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
            mode = inStudyMode ? "打谱" : "人机对弈",
            moveCount = rules != null ? rules.LoggedMoveCount : 0,
            komi = rules != null ? rules.Komi : komi,
            handicap = inStudyMode ? 0 : handicap,
            moveLogCsv = rules != null ? rules.ExportMoveLogCsv() : "",
            stateJson = rules != null ? JsonUtility.ToJson(rules.ExportState()) : ""
        };
        if (KifuDatabase.TryAdd(rec, out string err))
        {
            kifuForm.SetActive(false);
            if (scorePopup != null) scorePopup.SetActive(false);
            Flash("棋谱已保存");
        }
        else
        {
            Flash("保存棋谱失败: " + err);
        }
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
        var barRt = studyBoardUi.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0f, 0f);
        barRt.anchorMax = new Vector2(0f, 0f);
        barRt.pivot = new Vector2(0f, 0f);
        barRt.anchoredPosition = new Vector2(12f, 12f);
        barRt.sizeDelta = new Vector2(420f, 40f);

        var hlg = studyBoardUi.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        studyBtnMovesText = MakeStudyToggleBtn(studyBoardUi.transform, "手数", () =>
        {
            showMoveNums = !showMoveNums;
            SyncStudyToggleLabels();
            RefreshStudyVisuals();
        });
        studyBtnCoordsText = MakeStudyToggleBtn(studyBoardUi.transform, "坐标", () =>
        {
            showCoords = !showCoords;
            SyncStudyToggleLabels();
            RefreshStudyVisuals();
        });
        studyBtnTerrText = MakeStudyToggleBtn(studyBoardUi.transform, "形势", () =>
        {
            showTerritory = !showTerritory;
            SyncStudyToggleLabels();
            RefreshStudyVisuals();
        });

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

    Text MakeStudyToggleBtn(Transform parent, string name, UnityEngine.Events.UnityAction cb)
    {
        var btn = MakeButton(parent, name + "：关", 36f, cb);
        btn.GetComponentInChildren<Text>().fontSize = 15;
        return btn.GetComponentInChildren<Text>();
    }

    void SyncStudyToggleLabels()
    {
        if (studyBtnMovesText != null)
            studyBtnMovesText.text = showMoveNums ? "手数：开" : "手数：关";
        if (studyBtnCoordsText != null)
            studyBtnCoordsText.text = showCoords ? "坐标：开" : "坐标：关";
        if (studyBtnTerrText != null)
            studyBtnTerrText.text = showTerritory ? "形势：开" : "形势：关";
        if (studyTerritoryText != null)
            studyTerritoryText.gameObject.SetActive(BoardOverlayOn && showTerritory);
    }

    void BuildStudyBoardLabels(int n)
    {
        if (studyLabelRoot != null) Destroy(studyLabelRoot);
        studyColBot = new Text[n];
        studyColTop = new Text[n];
        studyRowLeft = new Text[n];
        studyRowRight = new Text[n];

        studyLabelRoot = new GameObject("StudyLabels");
        studyLabelRoot.transform.SetParent(boardRoot, false);
        var canvas = studyLabelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 10;
        var crt = studyLabelRoot.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(n * 100, n * 100);
        studyLabelRoot.transform.localScale = Vector3.one * 0.01f;
        studyLabelRoot.transform.position = new Vector3((n - 1) / 2f, (n - 1) / 2f, 0f);

        for (int i = 0; i < n; i++)
        {
            studyColBot[i] = MakeBoardOverlayText(canvas.transform, i, -0.72f, n, ColLabel(i), 24, Dim);
            studyColTop[i] = MakeBoardOverlayText(canvas.transform, i, n - 1 + 0.72f, n, ColLabel(i), 24, Dim);
            studyRowLeft[i] = MakeBoardOverlayText(canvas.transform, -0.72f, i, n, RowLabel(i), 24, Dim);
            studyRowRight[i] = MakeBoardOverlayText(canvas.transform, n - 1 + 0.72f, i, n, RowLabel(i), 24, Dim);
        }

        studyLabelRoot.SetActive(false);
    }

    Text MakeBoardOverlayText(Transform parent, float gx, float gy, int n, string text, int fontSize, Color color)
    {
        var go = new GameObject("Lbl");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(90f, 36f);
        rt.anchoredPosition = new Vector2((gx - (n - 1) / 2f) * 100f, (gy - (n - 1) / 2f) * 100f);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static string ColLabel(int x)
    {
        const string letters = "ABCDEFGHJKLMNOPQRST";
        return x < letters.Length ? letters[x].ToString() : (x + 1).ToString();
    }

    static string RowLabel(int y) => (y + 1).ToString();

    bool BoardOverlayOn => (inStudyMode || reviewingKifu || vsAI) && !inMenu;

    void RefreshStudyVisuals()
    {
        bool on = BoardOverlayOn;
        if (studyBoardUi != null) studyBoardUi.SetActive(on);
        if (studyLabelRoot != null) studyLabelRoot.SetActive(on && showCoords);
        SyncStudyToggleLabels();

        if (!on) return;
        int n = boardSize;

        if (moveNumMeshes != null)
        {
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var tm = moveNumMeshes[x, y];
                if (tm == null) continue;
                int i = rules.Idx(x, y);
                int stone = rules.Board[i];
                int mv = rules.MoveAt[i];
                bool hasMark = boardMarks != null && i < boardMarks.Length && boardMarks[i] != 0;
                if (!showMoveNums || stone == GoRules.EMPTY || mv <= 0 || hasMark)
                {
                    tm.gameObject.SetActive(false);
                    continue;
                }
                tm.gameObject.SetActive(true);
                tm.text = mv.ToString();
                tm.color = stone == GoRules.BLACK ? MoveNumOnBlack : MoveNumOnWhite;
            }
        }

        for (int i = 0; i < n; i++)
        {
            bool cOn = showCoords;
            studyColBot[i].enabled = cOn;
            studyColTop[i].enabled = cOn;
            studyRowLeft[i].enabled = cOn;
            studyRowRight[i].enabled = cOn;
        }

        if (showTerritory)
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
            PaintTerritoryBoxes(owner);
        }
        else
        {
            if (studyTerritoryText != null) studyTerritoryText.text = "";
            PaintTerritoryBoxes(null);
        }
    }

    void PaintTerritoryBoxes(int[] owner)
    {
        if (terrSr == null) return;
        int n = boardSize;
        bool on = owner != null && owner.Length == n * n;
        Color blackBox = new Color(0.08f, 0.08f, 0.08f, 0.92f);
        Color whiteBox = new Color(0.97f, 0.97f, 0.97f, 0.92f);
        Color blackMoyo = new Color(0.12f, 0.12f, 0.12f, 0.42f);
        Color whiteMoyo = new Color(1f, 1f, 1f, 0.42f);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            var sr = terrSr[x, y];
            if (sr == null) continue;
            if (!on)
            {
                sr.enabled = false;
                continue;
            }
            int o = owner[rules.Idx(x, y)];
            if (o == GoRules.BLACK) { sr.enabled = true; sr.color = blackBox; }
            else if (o == GoRules.WHITE) { sr.enabled = true; sr.color = whiteBox; }
            else if (o == GoRules.BLACK + 10) { sr.enabled = true; sr.color = blackMoyo; }
            else if (o == GoRules.WHITE + 10) { sr.enabled = true; sr.color = whiteMoyo; }
            else sr.enabled = false;
        }
    }

    /* ---------------- 主界面 ---------------- */

    void BuildMenu()
    {
        menuCanvas = new GameObject("MenuCanvas");
        menuCanvas.transform.SetParent(mainCanvasLayer.transform, false);
        var canvas = menuCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = menuCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440f, 900f);
        scaler.matchWidthOrHeight = 0.5f;
        menuCanvas.AddComponent<GraphicRaycaster>();

        var bg = new GameObject("Bg");
        bg.transform.SetParent(menuCanvas.transform, false);
        var bimg = bg.AddComponent<Image>();
        bimg.color = new Color(0.08f, 0.07f, 0.05f, 0.72f);
        var brt = bg.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.sizeDelta = Vector2.zero; brt.anchoredPosition = Vector2.zero;

        mainPanel = BuildMenuPanel("MainPanel", 380f, 500f, pt =>
        {
            MakeText(pt, "围  棋", 42, Ink, TextAnchor.MiddleCenter);
            MakeText(pt, "请选择模式", 14, Dim, TextAnchor.MiddleCenter);

            var study = MakeButton(pt, "打  谱", 52f, StartStudyGame, false);
            study.GetComponent<Image>().color = Accent;
            StyleAccentLabel(study);

            MakeButton(pt, "人机对弈", 52f, ShowAiSetup);
            MakeButton(pt, "历史棋谱", 52f, ShowHistoryPanel);

            var quit = MakeButton(pt, "退  出", 52f, QuitGame);
            quit.GetComponentInChildren<Text>().color = Dim;
        });

        aiSetupPanel = BuildMenuPanel("AiSetupPanel", 500f, 760f, pt =>
        {
            MakeText(pt, "人机对弈", 36, Ink, TextAnchor.MiddleCenter);
            MakeText(pt, "开局前选定棋盘与规则，进入对局后不可更改", 13, Dim, TextAnchor.MiddleCenter);

            MakeText(pt, "棋盘", 14, Dim, TextAnchor.MiddleCenter);
            optSize = MakeOptions(pt, new[] { "9 路", "13 路", "19 路" }, i => sizeChoice = i);

            MakeText(pt, "执子", 14, Dim, TextAnchor.MiddleCenter);
            optColor = MakeOptions(pt, new[] { "执黑先行", "执白后行", "猜先" }, i => colorChoice = i);

            MakeText(pt, "电脑对手", 14, Dim, TextAnchor.MiddleCenter);
            optOpponent = MakeOptions(pt, new[] { "本地引擎", "KataGo", "大模型" }, i => opponentChoice = i);

            MakeText(pt, "贴目", 14, Dim, TextAnchor.MiddleCenter);
            komiValText = MakeStepper(pt,
                () => komi = Mathf.Max(0f, komi - 0.5f),
                () => komi = Mathf.Min(15f, komi + 0.5f));

            MakeText(pt, "让子（黑方预先布子，白方先下）", 14, Dim, TextAnchor.MiddleCenter);
            handiValText = MakeStepper(pt,
                () => handicap = Mathf.Max(0, handicap - 1),
                () => handicap = Mathf.Min(9, handicap + 1));

            MakeText(pt, "大模型密钥（xAI / OpenAI 兼容，仅存本机）", 13, Dim, TextAnchor.MiddleCenter);
            llmKeyInput = MakeInputField(pt, "粘贴 API Key", 36f, false);
            llmKeyInput.contentType = InputField.ContentType.Password;
            llmKeyInput.onEndEdit.AddListener(v => GoLlmAi.ApiKey = v);

            var start = MakeButton(pt, "开始对局", 46f, StartAiGame, false);
            start.GetComponent<Image>().color = Accent;
            StyleAccentLabel(start);

            MakeButton(pt, "返回", 40f, ShowMainMenu);
        });
        aiSetupPanel.SetActive(false);

        historyPanel = BuildMenuPanel("HistoryPanel", 640f, 680f, pt =>
        {
            MakeText(pt, "历史棋谱", 32, Ink, TextAnchor.MiddleCenter);
            MakeText(pt, "按名称、昵称、结果或描述搜索", 13, Dim, TextAnchor.MiddleCenter);
            historySearch = MakeInputField(pt, "搜索棋谱", 36f, false);
            historySearch.onValueChanged.AddListener(RebuildHistoryList);

            var listHost = new GameObject("HistoryList");
            listHost.transform.SetParent(pt, false);
            var listLe = listHost.AddComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 280f;
            listLe.preferredHeight = 380f;
            var listBg = listHost.AddComponent<Image>();
            listBg.color = new Color(0.08f, 0.06f, 0.04f, 0.95f);
            var scroll = listHost.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            listHost.AddComponent<RectMask2D>();

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(listHost.transform, false);
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
            crt.sizeDelta = Vector2.zero;
            var cv = content.AddComponent<VerticalLayoutGroup>();
            cv.spacing = 6f;
            cv.childAlignment = TextAnchor.UpperCenter;
            cv.childControlWidth = true;
            cv.childControlHeight = true;
            cv.childForceExpandWidth = true;
            cv.childForceExpandHeight = false;
            var fit = content.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            historyListContent = content.transform;
            scroll.viewport = vrt;
            scroll.content = crt;

            MakeButton(pt, "返回", 40f, ShowMainMenu);
        });
        historyPanel.SetActive(false);
        BuildHistoryDetail();
        BuildSettingsPanel();
        BuildSettingsButton();
        BuildVolumeQuickControl();
        RegisterUiPages();
    }

    void BuildSettingsPanel()
    {
        settingsPanel = BuildMenuPanel("SettingsPanel", 860f, 620f, pt =>
        {
            MakeText(pt, "设置", 32, Ink, TextAnchor.MiddleCenter);
            MakeText(pt, "偏好会保存在本机，下次启动继续生效", 13, Dim, TextAnchor.MiddleCenter);

            var columns = MakeRow(pt, 430f);
            var directory = new GameObject("SettingsDirectory");
            directory.transform.SetParent(columns, false);
            var directoryLayout = directory.AddComponent<LayoutElement>();
            directoryLayout.preferredWidth = 190f;
            directoryLayout.minWidth = 190f;
            directoryLayout.flexibleWidth = 0f;
            var directoryImage = directory.AddComponent<Image>();
            directoryImage.color = new Color(0.10f, 0.08f, 0.06f, 0.94f);
            var directoryGroup = directory.AddComponent<VerticalLayoutGroup>();
            directoryGroup.padding = new RectOffset(12, 12, 14, 14);
            directoryGroup.spacing = 6f;
            directoryGroup.childAlignment = TextAnchor.UpperCenter;
            directoryGroup.childControlWidth = true;
            directoryGroup.childControlHeight = true;
            directoryGroup.childForceExpandWidth = true;
            directoryGroup.childForceExpandHeight = false;

            string[] sections = { "音量", "界面", "棋盘", "AI引擎", "快捷键", "关于" };
            for (int i = 0; i < sections.Length; i++)
            {
                int section = i;
                var sectionButton = MakeButton(directory.transform, sections[i], 38f,
                    () => ShowSettingsSection(section));
                if (i == 0) StyleAccentLabel(sectionButton);
            }

            var content = new GameObject("SettingsContent");
            content.transform.SetParent(columns, false);
            var contentLayout = content.AddComponent<LayoutElement>();
            contentLayout.flexibleWidth = 1f;
            contentLayout.minWidth = 460f;
            var contentImage = content.AddComponent<Image>();
            contentImage.color = new Color(0.12f, 0.09f, 0.06f, 0.94f);
            var contentGroup = content.AddComponent<VerticalLayoutGroup>();
            contentGroup.padding = new RectOffset(24, 24, 22, 22);
            contentGroup.spacing = 10f;
            contentGroup.childAlignment = TextAnchor.UpperCenter;
            contentGroup.childControlWidth = true;
            contentGroup.childControlHeight = true;
            contentGroup.childForceExpandWidth = true;
            contentGroup.childForceExpandHeight = false;

            MakeText(content.transform, "音量", 22, Ink, TextAnchor.MiddleLeft);
            settingsMasterSlider = MakeSettingsSlider(content.transform, "主音量",
                audioManager != null ? audioManager.MasterVolume : GoAudioManager.DefaultMasterVolume,
                value => audioManager?.SetMasterVolume(value));
            settingsMusicSlider = MakeSettingsSlider(content.transform, "音乐",
                audioManager != null ? audioManager.MusicVolume : GoAudioManager.DefaultMusicVolume,
                value => audioManager?.SetMusicVolume(value));
            settingsSfxSlider = MakeSettingsSlider(content.transform, "音效",
                audioManager != null ? audioManager.SfxVolume : GoAudioManager.DefaultSfxVolume,
                value => audioManager?.SetSfxVolume(value));
            settingsWarningSlider = MakeSettingsSlider(content.transform, "计时提醒",
                audioManager != null ? audioManager.WarningVolume : GoAudioManager.DefaultWarningVolume,
                value => audioManager?.SetWarningVolume(value));
            settingsMuteToggle = uiFactory.MakeToggle(content.transform, "MuteToggle", "静音",
                audioManager != null && audioManager.IsMuted, SetMuteFromUi);

            MakeText(content.transform, "界面", 22, Ink, TextAnchor.MiddleLeft);
            settingsReduceMotionToggle = uiFactory.MakeToggle(content.transform, "ReduceMotionToggle",
                "减少动效", PlayerPrefs.GetInt(ReduceMotionKey, 0) != 0, SetReduceMotion);
            MakeText(content.transform, "棋盘、AI引擎、快捷键和关于页面将在后续迁移阶段补齐。",
                13, Dim, TextAnchor.UpperLeft);

            MakeButton(pt, "返回", 40f, ShowMainMenu);
        });
        settingsPanel.SetActive(false);
    }

    Slider MakeSettingsSlider(Transform parent, string label, float value,
        UnityEngine.Events.UnityAction<float> onChanged)
    {
        var row = MakeRow(parent, 34f);
        var text = MakeText(row, label, 14, Dim, TextAnchor.MiddleLeft);
        var textLayout = text.gameObject.GetComponent<LayoutElement>();
        if (textLayout == null) textLayout = text.gameObject.AddComponent<LayoutElement>();
        textLayout.preferredWidth = 82f;
        textLayout.minWidth = 82f;
        textLayout.flexibleWidth = 0f;
        return uiFactory.MakeSlider(row, "Slider_" + label, value, onChanged);
    }

    void BuildSettingsButton()
    {
        var button = MakeButton(menuCanvas.transform, "设置", 40f, ShowSettings);
        SetFixedWidth(button, 92f);
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-28f, -26f);
    }

    void BuildVolumeQuickControl()
    {
        var root = new GameObject("VolumeQuickControl");
        root.transform.SetParent(menuCanvas.transform, false);
        var rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(1f, 0f);
        rootRect.pivot = new Vector2(1f, 0f);
        rootRect.anchoredPosition = new Vector2(-28f, 28f);
        rootRect.sizeDelta = new Vector2(330f, 52f);
        var rootImage = root.AddComponent<Image>();
        rootImage.color = new Color(0.10f, 0.08f, 0.06f, 0.78f);
        var layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        var iconObject = new GameObject("Icon");
        iconObject.transform.SetParent(root.transform, false);
        var iconLayout = iconObject.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = 28f;
        iconLayout.minWidth = 28f;
        iconLayout.preferredHeight = 28f;
        var icon = iconObject.AddComponent<Image>();
        icon.sprite = GoIconCatalog.Load(GoIconCatalog.Volume);
        icon.color = uiTheme.Gold;
        icon.raycastTarget = false;

        quickMasterVolumeSlider = uiFactory.MakeSlider(root.transform, "MasterVolumeSlider",
            audioManager != null ? audioManager.MasterVolume : GoAudioManager.DefaultMasterVolume,
            value => audioManager?.SetMasterVolume(value));
        var sliderLayout = quickMasterVolumeSlider.GetComponent<LayoutElement>();
        sliderLayout.preferredWidth = QuickVolumeSliderWidth();
        sliderLayout.minWidth = sliderLayout.preferredWidth;
        sliderLayout.flexibleWidth = 0f;

        quickMuteButton = MakeButton(root.transform, "静音", 32f,
            () => SetMuteFromUi(audioManager != null && !audioManager.IsMuted));
        SetFixedWidth(quickMuteButton, 56f);
        SyncAudioControls();
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
        if (section == 1 && settingsReduceMotionToggle != null)
            settingsReduceMotionToggle.Select();
        if (section == 0 && settingsMasterSlider != null)
            settingsMasterSlider.Select();
    }

    void ShowSettings()
    {
        SyncAudioControls();
        menuCanvas.SetActive(true);
        mainPanel.SetActive(false);
        aiSetupPanel.SetActive(false);
        if (historyPanel != null) historyPanel.SetActive(false);
        if (historyDetail != null) historyDetail.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
        SetVolumeQuickVisible(false);
        if (uiNavigator != null) uiNavigator.Show(GoUiPage.Settings);
    }

    void SetVolumeQuickVisible(bool visible)
    {
        if (quickMasterVolumeSlider != null && quickMasterVolumeSlider.transform.parent != null)
            quickMasterVolumeSlider.transform.parent.gameObject.SetActive(visible);
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
        if (quickMuteButton != null)
        {
            var label = quickMuteButton.GetComponentInChildren<Text>();
            if (label != null) label.text = audioManager.IsMuted ? "取消静音" : "静音";
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
        cimg.color = new Color(0.16f, 0.13f, 0.09f, 1f);
        var crt = card.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(520f, 480f);

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 24, 20);
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        historyDetailText = MakeText(card.transform, "", 16, Ink, TextAnchor.UpperLeft);
        var dle = historyDetailText.gameObject.AddComponent<LayoutElement>();
        dle.flexibleHeight = 1f;
        dle.minHeight = 280f;
        MakeButton(card.transform, "返回", 40f, () => historyDetail.SetActive(false));
        historyDetail.SetActive(false);
    }

    void RebuildHistoryList(string keyword)
    {
        if (historyListContent == null) return;
        for (int i = historyListContent.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(historyListContent.GetChild(i).gameObject);

        var rows = KifuDatabase.Query(keyword);
        if (rows.Count == 0)
        {
            MakeText(historyListContent, "暂无棋谱", 16, Dim, TextAnchor.MiddleCenter);
            return;
        }
        foreach (var rec in rows)
        {
            var captured = rec;
            string line = $"{rec.title}  ·  {rec.result}\n{rec.blackName} vs {rec.whiteName}  ·  {rec.playedAt}";
            var btn = MakeButton(historyListContent, line, 56f, () => OpenKifuReview(captured));
            var t = btn.GetComponentInChildren<Text>();
            t.fontSize = 13;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }

    void OpenHistoryDetail(KifuRecord rec)
    {
        OpenKifuReview(rec);
    }

    void OpenKifuReview(KifuRecord rec)
    {
        if (rec == null) return;
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
        if (uiNavigator != null) uiNavigator.Show(GoUiPage.Review);
        HideScorePopup();
        BuildBoard();
        ReviewSeek(reviewPly);
        Flash($"{rec.title}  ·  {rec.blackName} vs {rec.whiteName}");
    }

    void ReviewSeek(int ply)
    {
        if (!reviewingKifu || rules == null) return;
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

        if (gameAnalysis != null && gameAnalysis.blackWinrates != null && reviewPly < gameAnalysis.blackWinrates.Length)
        {
            float wr = gameAnalysis.blackWinrates[reviewPly];
            float lead = gameAnalysis.blackScoreLeads[reviewPly];
            float prevWr = reviewPly > 0 ? gameAnalysis.blackWinrates[reviewPly - 1] : wr;
            UpdateWinrateBar(wr, lead, wr - prevWr);
            branchLastWinrate = wr;
        }
        else
        {
            UpdateWinrateBar(0.5f, 0f, 0f);
            branchLastWinrate = -1f;
        }

        if (aiReviewEnabled) RequestPositionAnalysis();
        else HideCandidateBadges();
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
                        string wrStr = st.winrate >= 0 ? $" <color=#88C0D0>[{st.winrate * 100f:0.0}%]</color>" : "";
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

    GameObject BuildMenuPanel(string name, float width, float height, System.Action<Transform> build)
    {
        var panel = new GameObject(name);
        panel.transform.SetParent(menuCanvas.transform, false);
        var pimg = panel.AddComponent<Image>();
        pimg.color = new Color(0.14f, 0.11f, 0.08f, 1f);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(width, height);
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

    static void StyleAccentLabel(Button btn)
    {
        var t = btn.GetComponentInChildren<Text>();
        t.color = new Color(0.11f, 0.09f, 0.07f);
        t.fontSize = 18;
    }

    void SyncAiSetup()
    {
        PaintOptions(optSize, sizeChoice);
        PaintOptions(optColor, colorChoice);
        if (optOpponent != null) PaintOptions(optOpponent, opponentChoice);
        komiValText.text = $"贴 {komi:0.0} 目";
        handiValText.text = handicap == 0 ? "不让子" : $"让 {handicap} 子";
        if (llmKeyInput != null && string.IsNullOrEmpty(llmKeyInput.text))
            llmKeyInput.text = PlayerPrefs.GetString("GoLlmKey", "");
    }

    void PaintOptions(List<Button> btns, int sel)
    {
        for (int i = 0; i < btns.Count; i++)
        {
            bool on = i == sel;
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

    /* ---------------- UI 基础件 ---------------- */

    Text MakeText(Transform parent, string content, int fontSize, Color color, TextAnchor anchor)
    {
        if (uiFactory != null)
            return uiFactory.MakeText(parent, "Label", content, fontSize, color, anchor);

        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.text = content; t.fontSize = fontSize; t.color = color;
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
        btnClear.SetActive(!rev);
        btnScore.SetActive(!rev);
        if (reviewAiRoot != null) reviewAiRoot.SetActive(rev);
        if (commentModuleRoot != null) commentModuleRoot.SetActive(rev);
        if (homeBtnLabel != null) homeBtnLabel.text = rev ? "返回棋谱" : "返回主界面";

        int n = boardSize;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int i = rules.Idx(x, y);
            int v = rules.Board[i];
            var sr = stoneSr[x, y];
            sr.enabled = v != GoRules.EMPTY;
            if (v == GoRules.BLACK) sr.sprite = stoneBlack;
            else if (v == GoRules.WHITE) sr.sprite = stoneWhite;
            bool dead = rules.Dead.Contains(i);
            sr.color = dead ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            deadSr[x, y].enabled = dead;
        }

        if (rules.LastMove >= 0 && (reviewingKifu || rules.Phase != GoPhase.Over))
        {
            markerSr.enabled = true;
            markerSr.transform.position = new Vector3(rules.LastMove % n, rules.LastMove / n, 0f);
            markerSr.color = rules.Board[rules.LastMove] == GoRules.BLACK
                ? new Color(0.91f, 0.72f, 0.38f) : new Color(0.35f, 0.22f, 0.06f);
        }
        else markerSr.enabled = false;

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
        else if (aiBusy) turnText.text = (aiColor == GoRules.BLACK ? "黑棋" : "白棋")
            + (opponentChoice == 1 ? "（KataGo）思考中…" : opponentChoice == 2 ? "（大模型）思考中…" : "（AI）思考中…");
        else
        {
            string who = rules.Turn == GoRules.BLACK ? "黑棋" : "白棋";
            if (inStudyMode) turnText.text = who + "落子";
            else turnText.text = who + "落子" + (rules.Turn == humanColor ? "（你）" : "（AI）");
        }

        RefreshMoveLog();
        RefreshStudyVisuals();
        UpdateBoardMarksVisuals();
        SyncCommentModule();

        if (reviewingKifu)
        {
            DrawWinrateChart(reviewPly);
            UpdateReviewInfoText(reviewPly);
        }
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
        winrateInfoText = MakeText(reviewAiRoot.transform, "胜率分析", 12, Ink, TextAnchor.MiddleCenter);
        winrateInfoText.supportRichText = true;
        var infoLe = winrateInfoText.gameObject.AddComponent<LayoutElement>();
        infoLe.preferredHeight = 18f;

        // 3. 双色胜率条容器
        var barGo = new GameObject("WinrateBarContainer");
        barGo.transform.SetParent(reviewAiRoot.transform, false);
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
        winrateBarText.fontSize = 11;
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
        candInfoText = MakeText(reviewAiRoot.transform, "开启「AI推演」可查看候选点与变化图", 12, Ink, TextAnchor.MiddleLeft);
        candInfoText.supportRichText = true;
        var candInfoLe = candInfoText.gameObject.AddComponent<LayoutElement>();
        candInfoLe.preferredHeight = 36f;
        candInfoLe.minHeight = 36f;

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

        if (gameAnalysis != null && gameAnalysis.blackWinrates != null && reviewTotal > 0)
        {
            float[] wrs = gameAnalysis.blackWinrates;
            int total = Mathf.Min(reviewTotal, wrs.Length - 1);
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
        else if (reviewTotal > 0)
        {
            int cx = Mathf.Clamp(Mathf.RoundToInt((curPly / (float)reviewTotal) * (w - 1)), 0, w - 1);
            for (int y = 0; y < h; y++) px[y * w + cx] = curBar;
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
            if (curPosAnalysis != null)
            {
                float wr = curPosAnalysis.blackWinrate;
                float lead = curPosAnalysis.blackScoreLead;
                string leadStr = lead > 0.05f ? $"黑+{lead:0.0}目" : lead < -0.05f ? $"白+{(-lead):0.0}目" : "均势";
                winrateInfoText.text = $"<color=#EAA83E>【试下第 {branchMoveCount} 步】{who}走 · 黑胜率 {(wr * 100f):0.0}% ({leadStr})</color>";
            }
            else
            {
                winrateInfoText.text = $"<color=#EAA83E>【试下第 {branchMoveCount} 步】轮到{who}落子 · AI思考中…</color>";
            }
            return;
        }
        if (gameAnalysis != null && gameAnalysis.blackWinrates != null && ply >= 0 && ply < gameAnalysis.blackWinrates.Length)
        {
            float wr = gameAnalysis.blackWinrates[ply];
            float lead = gameAnalysis.blackScoreLeads[ply];
            int b = gameAnalysis.blunders != null && ply < gameAnalysis.blunders.Length ? gameAnalysis.blunders[ply] : 0;
            string tag = b == 2 ? " <color=#EB3B3B>[恶手]</color>" : b == 1 ? " <color=#FA8C16>[失误]</color>" : "";
            string leadStr = lead > 0.05f ? $"黑+{lead:0.0}目" : lead < -0.05f ? $"白+{(-lead):0.0}目" : "均势";
            winrateInfoText.text = $"第 {ply} 手 · 黑胜率 {(wr * 100f):0.0}% ({leadStr}){tag}";
        }
        else if (curPosAnalysis != null)
        {
            float wr = curPosAnalysis.blackWinrate;
            float lead = curPosAnalysis.blackScoreLead;
            string leadStr = lead > 0.05f ? $"黑+{lead:0.0}目" : lead < -0.05f ? $"白+{(-lead):0.0}目" : "均势";
            winrateInfoText.text = $"第 {ply} 手 · 黑胜率 {(wr * 100f):0.0}% ({leadStr})";
        }
        else
        {
            winrateInfoText.text = isAnalyzingFullGame ? "全局分析中…" : "胜率走势（点击「全局分析」）";
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
            HideCandidateBadges();
            if (curAnalysisCo != null) { StopCoroutine(curAnalysisCo); curAnalysisCo = null; }
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
                    Flash("全局胜率分析已完成！");
                }
            }
        ));
    }

    void RequestPositionAnalysis()
    {
        if (!aiReviewEnabled || !reviewingKifu) return;
        if (curAnalysisCo != null) { StopCoroutine(curAnalysisCo); curAnalysisCo = null; }
        curAnalysisCo = StartCoroutine(DoPositionAnalysis());
    }

    IEnumerator DoPositionAnalysis()
    {
        curPosAnalysis = null;
        HideCandidateBadges();
        int side = rules.Turn;
        yield return GoKataGo.AnalyzePosition(
            rules,
            side,
            reviewRec != null ? reviewRec.handicap : 0,
            HandicapPoints,
            60,
            (pos) =>
            {
                curPosAnalysis = pos;
                if (pos != null)
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
                            SaveReviewCommentsAndBranches();
                            RefreshReviewMoveLog();
                        }
                    }

                    ShowCandidateBadges(pos);
                    UpdateReviewInfoText(reviewPly);
                }
            }
        );
        curAnalysisCo = null;
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
            pvStoneSrs[shown].sprite = isBlack ? stoneBlack : stoneWhite;
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

        if (curAnalysisCo != null)
        {
            StopCoroutine(curAnalysisCo);
            curAnalysisCo = null;
        }

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
                branchLastWinrate = curPosAnalysis != null ? curPosAnalysis.blackWinrate 
                    : (gameAnalysis != null && reviewPly < gameAnalysis.blackWinrates.Length ? gameAnalysis.blackWinrates[reviewPly] : 0.5f);
            }
            rules.Pass();
            branchMoveCount++;
            var step = new BranchStepData
            {
                step = branchMoveCount,
                x = -1,
                y = -1,
                color = GoRules.Opp(rules.Turn),
                isPass = true,
                winrate = branchLastWinrate
            };
            reviewBranches[curBranchIdx].steps.Add(step);
            curBranchStepIdx = reviewBranches[curBranchIdx].steps.Count - 1;

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
            branchLastWinrate = curPosAnalysis != null ? curPosAnalysis.blackWinrate 
                : (gameAnalysis != null && reviewPly < gameAnalysis.blackWinrates.Length ? gameAnalysis.blackWinrates[reviewPly] : 0.5f);
        }

        if (rules.Play(x, y, out string msg))
        {
            branchMoveCount++;
            int movedColor = GoRules.Opp(rules.Turn);
            var step = new BranchStepData
            {
                step = branchMoveCount,
                x = x,
                y = y,
                color = movedColor,
                isPass = false,
                winrate = branchLastWinrate
            };
            reviewBranches[curBranchIdx].steps.Add(step);
            curBranchStepIdx = reviewBranches[curBranchIdx].steps.Count - 1;

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
        if (curAnalysisCo != null)
        {
            StopCoroutine(curAnalysisCo);
            curAnalysisCo = null;
        }

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
        if (curAnalysisCo != null)
        {
            StopCoroutine(curAnalysisCo);
            curAnalysisCo = null;
        }
        inBranchMode = false;
        curBranchIdx = -1;
        curBranchStepIdx = -1;
        branchMoveCount = 0;
        branchLastWinrate = -1f;
        if (btnExitBranch != null) btnExitBranch.SetActive(false);
        if (btnBranchUndo != null) btnBranchUndo.SetActive(false);
        ReviewSeek(branchPlyStart);
        Flash("已恢复实战局面");
    }

    void UpdateWinrateBar(float blackWr, float blackScoreLead, float deltaWr = 0f)
    {
        if (barBlackRt == null || barWhiteRt == null || winrateBarText == null) return;
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

    void UpdateCandidateDetailText(KataCandidate c)
    {
        if (candInfoText == null) return;
        if (c == null)
        {
            if (inBranchMode)
                candInfoText.text = $"<color=#EAA83E>【试下第 {branchMoveCount} 步】</color>点击候选徽章或[推演一步]深入";
            else
                candInfoText.text = aiReviewEnabled ? "点击棋盘任意点可试下推演变化" : "点击「AI推演: 关」开启选点分析";
            return;
        }

        char letter = (char)('A' + Mathf.Clamp(c.order, 0, 25));
        string pvStr = FormatPvShort(c.pv, 6);
        string leadStr = (c.scoreLead >= 0 ? "+" : "") + c.scoreLead.ToString("0.0");
        int wrPct = Mathf.RoundToInt(c.winrate * 100f);
        string sideName = rules.Turn == GoRules.BLACK ? "黑" : "白";

        candInfoText.text = $"<b>候选{letter}（{c.coord}）</b> {sideName}胜率:<b>{wrPct}%</b> (领先:<b>{leadStr}目</b>)\n<color=#D4AF37>推荐序列: {pvStr}</color>";
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
        if (audioManager != null) audioManager.SavePrefs();
        GoKataGo.Shutdown();
    }
}
