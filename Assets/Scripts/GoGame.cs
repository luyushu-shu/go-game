using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

    const int StudyBoardSize = 19;
    const string StudySaveKey = "GoStudySave";
    static string StudySavePath => System.IO.Path.Combine(Application.persistentDataPath, "study_save.json");

    // 人机对弈开局设置（进入对局前选定，局内不可改）
    int sizeChoice = 2;      // 0=9路 1=13路 2=19路
    int colorChoice = 0;     // 0=执黑 1=执白 2=猜先
    float komi = 7.5f;
    int handicap = 0;
    int humanColor = GoRules.BLACK;
    int aiColor => GoRules.Opp(humanColor);

    Camera cam;
    Transform boardRoot;
    SpriteRenderer boardSr;
    SpriteRenderer[,] stoneSr;
    SpriteRenderer[,] deadSr;
    SpriteRenderer[,] terrSr;
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
    GameObject menuCanvas, mainPanel, aiSetupPanel, historyPanel, historyDetail;
    InputField historySearch;
    Transform historyListContent;
    Text historyDetailText;
    List<Button> optSize, optColor;
    Text komiValText, handiValText;

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

        rules = new GoRules(boardSize);
        BuildBoard();
        BuildUI();
        BuildMenu();
        ShowMainMenu();
    }

    /* ---------------- 对局流程 ---------------- */

    void StartStudyGame()
    {
        inStudyMode = true;
        vsAI = false;
        boardSize = StudyBoardSize;
        showMoveNums = true;
        aiBusy = false;
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
        inStudyMode = false;
        vsAI = true;
        boardSize = sizeChoice == 0 ? 9 : sizeChoice == 1 ? 13 : 19;
        humanColor = colorChoice == 0 ? GoRules.BLACK
                   : colorChoice == 1 ? GoRules.WHITE
                   : (Random.value < 0.5f ? GoRules.BLACK : GoRules.WHITE);

        aiBusy = false;
        StopAllCoroutines();
        rules.Reset(boardSize);
        rules.Komi = komi;
        if (handicap > 0) rules.SetupHandicap(HandicapPoints(boardSize, handicap));

        EnterGame();
        Flash(colorChoice == 2
            ? "猜先结果：你" + (humanColor == GoRules.BLACK ? "执黑" : "执白")
            : humanColor == GoRules.BLACK ? "你执黑" : "你执白");
        if (rules.Result == null && rules.Phase == GoPhase.Play && rules.Turn == aiColor)
            StartCoroutine(AiTurn());
    }

    void EnterGame()
    {
        BuildBoard();
        inMenu = false;
        reviewingKifu = false;
        menuCanvas.SetActive(false);
        gameStartedAt = System.DateTime.Now;
        lastResultLine = "";
        kifuDraftReady = false;
        if (kifuForm != null) kifuForm.SetActive(false);
        Refresh();
    }

    bool TryLoadStudySave(out GoStateData data)
    {
        data = null;
        var a = ParseStudyJson(System.IO.File.Exists(StudySavePath)
            ? System.IO.File.ReadAllText(StudySavePath) : null);
        var b = PlayerPrefs.HasKey(StudySaveKey)
            ? ParseStudyJson(PlayerPrefs.GetString(StudySaveKey)) : null;
        data = PickRicherSave(a, b);
        return data != null && data.size > 0;
    }

    static GoStateData ParseStudyJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var d = JsonUtility.FromJson<GoStateData>(json);
        return d != null && d.size > 0 ? d : null;
    }

    static GoStateData PickRicherSave(GoStateData a, GoStateData b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return CountMoveNums(b) > CountMoveNums(a) ? b : a;
    }

    static int CountMoveNums(GoStateData d)
    {
        if (d == null) return 0;
        int n = 0;
        if (!string.IsNullOrEmpty(d.moveAtCsv))
            foreach (var p in d.moveAtCsv.Split(','))
                if (int.TryParse(p, out int v) && v > 0) n++;
        if (!string.IsNullOrEmpty(d.moveLogCsv))
            n = System.Math.Max(n, d.moveLogCsv.Split('|').Length);
        if (d.moveCount > n) n = d.moveCount;
        return n;
    }

    void SaveStudyProgress()
    {
        if (!inStudyMode || reviewingKifu) return;
        var data = rules.ExportState();
        data.phase = (int)GoPhase.Play;
        data.passes = 0;
        data.result = null;
        data.dead = new int[0];
        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(StudySaveKey, json);
        PlayerPrefs.Save();
        try { System.IO.File.WriteAllText(StudySavePath, json); }
        catch { }
    }

    void ReturnToMainMenu()
    {
        HideScorePopup();
        if (reviewingKifu)
        {
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
        StopAllCoroutines();
        ShowMainMenu();
    }

    void ShowMainMenu()
    {
        inMenu = true;
        reviewingKifu = false;
        menuCanvas.SetActive(true);
        mainPanel.SetActive(true);
        aiSetupPanel.SetActive(false);
        if (historyPanel != null) historyPanel.SetActive(false);
        if (historyDetail != null) historyDetail.SetActive(false);
        if (kifuForm != null) kifuForm.SetActive(false);
        if (ghostSr != null) ghostSr.enabled = false;
        RefreshStudyVisuals();
    }

    void ShowAiSetup()
    {
        mainPanel.SetActive(false);
        if (historyPanel != null) historyPanel.SetActive(false);
        aiSetupPanel.SetActive(true);
        SyncAiSetup();
    }

    void ShowHistoryPanel()
    {
        inMenu = true;
        menuCanvas.SetActive(true);
        mainPanel.SetActive(false);
        aiSetupPanel.SetActive(false);
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
        aiBusy = true; Refresh();
        float budget = boardSize <= 9 ? 1.2f : boardSize <= 13 ? 2f : 2.6f;
        yield return null;   // 让"思考中"先显示一帧
        GoAI.BeginSearch(rules, aiColor, budget);
        while (!GoAI.SearchDone) yield return null;
        aiBusy = false;
        if (GoAI.ResultIsPass)
        {
            rules.Pass();
            Flash(rules.Phase == GoPhase.Scoring ? "AI 停一手，进入数子" : "AI 停一手");
        }
        else if (GoAI.BestWinRate < 0.03 && rules.MoveCount > 100)
        {
            rules.Resign();
            ShowResultPopup(rules.Result);
        }
        else rules.Play(GoAI.ResultX, GoAI.ResultY, out _);
        Refresh();
    }

    void Undo()
    {
        if (reviewingKifu) { ReviewSeek(reviewPly - 1); return; }
        if (aiBusy || inMenu) return;
        int steps = vsAI ? 2 : 1;
        if (!rules.Undo(steps)) { Flash("没有可悔的棋"); return; }
        Flash("已悔棋");
        Refresh();
        if (vsAI && rules.Phase == GoPhase.Play && rules.Turn == aiColor)
            StartCoroutine(AiTurn());
    }

    static int[] HandicapPoints(int n, int k)
    {
        int[] s = StarPoints(n);
        // 让子顺序：右上、左下、左上、右下、天元、上边、下边、左边、右边
        int[,] grid = { { 2, 2 }, { 0, 0 }, { 0, 2 }, { 2, 0 }, { 1, 1 }, { 1, 2 }, { 1, 0 }, { 0, 1 }, { 2, 1 } };
        var pts = new int[k];
        for (int i = 0; i < k; i++) pts[i] = s[grid[i, 0]] + s[grid[i, 1]] * n;
        return pts;
    }

    /* ---------------- 输入 ---------------- */

    void Update()
    {
        if (cam == null || rules == null) return;
        if (inMenu)
        {
            if (ghostSr != null) ghostSr.enabled = false;
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

        var mk = new GameObject("Marker");
        mk.transform.SetParent(boardRoot, false);
        markerSr = mk.AddComponent<SpriteRenderer>();
        markerSr.sprite = ringSpr;
        markerSr.sortingOrder = 3;
        markerSr.enabled = false;

        cam.orthographicSize = (n + 1) / 2f + 0.7f;
        cam.transform.position = new Vector3((n - 1) / 2f, (n - 1) / 2f, -10f);
        BuildStudyBoardLabels(n);
    }

    static int[] StarPoints(int n)
    {
        if (n == 19) return new[] { 3, 9, 15 };
        if (n == 13) return new[] { 3, 6, 9 };
        return new[] { 2, 4, 6 };
    }

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

    /* ---------------- 对局面板 ---------------- */

    void BuildUI()
    {
        var cgo = new GameObject("UI");
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440f, 900f);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();

        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        var pgo = new GameObject("Panel");
        pgo.transform.SetParent(canvas.transform, false);
        var pimg = pgo.AddComponent<Image>();
        pimg.color = new Color(0.14f, 0.11f, 0.08f, 0.96f);
        var prt = pgo.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(1f, 0f);
        prt.anchorMax = new Vector2(1f, 1f);
        prt.pivot = new Vector2(1f, 0.5f);
        prt.sizeDelta = new Vector2(268f, -72f);
        prt.anchoredPosition = new Vector2(-36f, 0f);

        var vlg = pgo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 16, 16);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var pt = pgo.transform;

        MakeText(pt, "落子记录", 16, Dim, TextAnchor.MiddleCenter);
        BuildMoveLogScroll(pt);
        turnText = MakeText(pt, "", 15, Ink, TextAnchor.MiddleCenter);
        msgText = MakeText(pt, "", 15, Accent, TextAnchor.MiddleCenter);

        btnUndo = MakeButton(pt, "悔棋", 42f, Undo).gameObject;
        btnPass = MakeButton(pt, "停一手", 42f, HumanPass).gameObject;
        btnResign = MakeButton(pt, "认输", 42f, HumanResign).gameObject;
        btnClear = MakeButton(pt, "清空棋盘", 42f, ClearBoardKeepPlaying).gameObject;
        btnScore = MakeButton(pt, "数目", 42f, OpenScorePopup).gameObject;
        btnReviewPrev = MakeButton(pt, "上一手", 42f, () => ReviewSeek(reviewPly - 1)).gameObject;
        btnReviewNext = MakeButton(pt, "下一手", 42f, () => ReviewSeek(reviewPly + 1)).gameObject;
        btnReviewEnd = MakeButton(pt, "终局", 42f, () => ReviewSeek(reviewTotal)).gameObject;
        btnReviewPrev.SetActive(false);
        btnReviewNext.SetActive(false);
        btnReviewEnd.SetActive(false);
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
        le.minHeight = 180f;
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
        PlayerPrefs.DeleteKey(StudySaveKey);
        PlayerPrefs.Save();
        try
        {
            if (System.IO.File.Exists(StudySavePath))
                System.IO.File.Delete(StudySavePath);
        }
        catch { }
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
        KifuDatabase.Add(rec);
        kifuForm.SetActive(false);
        if (scorePopup != null) scorePopup.SetActive(false);
        Flash("棋谱已保存");
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
            studyTerritoryText.gameObject.SetActive((inStudyMode || reviewingKifu) && !inMenu && showTerritory);
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

    void RefreshStudyVisuals()
    {
        bool on = (inStudyMode || reviewingKifu) && !inMenu;
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
                if (!showMoveNums || stone == GoRules.EMPTY || mv <= 0)
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
        bimg.color = new Color(0.08f, 0.07f, 0.05f, 0.96f);
        var brt = bg.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.sizeDelta = Vector2.zero; brt.anchoredPosition = Vector2.zero;

        mainPanel = BuildMenuPanel("MainPanel", 380f, 500f, pt =>
        {
            MakeText(pt, "围  棋", 42, Ink, TextAnchor.MiddleCenter);
            MakeText(pt, "请选择模式", 14, Dim, TextAnchor.MiddleCenter);

            var study = MakeButton(pt, "打  谱", 52f, StartStudyGame);
            study.GetComponent<Image>().color = Accent;
            StyleAccentLabel(study);

            MakeButton(pt, "人机对弈", 52f, ShowAiSetup);
            MakeButton(pt, "历史棋谱", 52f, ShowHistoryPanel);

            var quit = MakeButton(pt, "退  出", 52f, QuitGame);
            quit.GetComponentInChildren<Text>().color = Dim;
        });

        aiSetupPanel = BuildMenuPanel("AiSetupPanel", 470f, 620f, pt =>
        {
            MakeText(pt, "人机对弈", 36, Ink, TextAnchor.MiddleCenter);
            MakeText(pt, "开局前选定棋盘与规则，进入对局后不可更改", 13, Dim, TextAnchor.MiddleCenter);

            MakeText(pt, "棋盘", 14, Dim, TextAnchor.MiddleCenter);
            optSize = MakeOptions(pt, new[] { "9 路", "13 路", "19 路" }, i => sizeChoice = i);

            MakeText(pt, "执子", 14, Dim, TextAnchor.MiddleCenter);
            optColor = MakeOptions(pt, new[] { "执黑先行", "执白后行", "猜先" }, i => colorChoice = i);

            MakeText(pt, "贴目", 14, Dim, TextAnchor.MiddleCenter);
            komiValText = MakeStepper(pt,
                () => komi = Mathf.Max(0f, komi - 0.5f),
                () => komi = Mathf.Min(15f, komi + 0.5f));

            MakeText(pt, "让子（黑方预先布子，白方先下）", 14, Dim, TextAnchor.MiddleCenter);
            handiValText = MakeStepper(pt,
                () => handicap = Mathf.Max(0, handicap - 1),
                () => handicap = Mathf.Min(9, handicap + 1));

            var start = MakeButton(pt, "开始对局", 46f, StartAiGame);
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
        reviewingKifu = true;
        reviewRec = rec;
        inStudyMode = false;
        vsAI = false;
        aiBusy = false;
        StopAllCoroutines();
        boardSize = rec.boardSize > 0 ? rec.boardSize : 19;
        showMoveNums = true;
        reviewLogCsv = rec.moveLogCsv ?? "";
        reviewTotal = rules.CountLogMoves(reviewLogCsv);
        if (reviewTotal <= 0 && rec.moveCount > 0) reviewTotal = rec.moveCount;
        reviewPly = reviewTotal;
        inMenu = false;
        if (historyDetail != null) historyDetail.SetActive(false);
        if (historyPanel != null) historyPanel.SetActive(false);
        menuCanvas.SetActive(false);
        HideScorePopup();
        BuildBoard();
        ReviewSeek(reviewPly);
        Flash($"{rec.title}  ·  {rec.blackName} vs {rec.whiteName}");
    }

    void ReviewSeek(int ply)
    {
        if (!reviewingKifu || rules == null) return;
        if (ply < 0) ply = 0;
        if (ply > reviewTotal) ply = reviewTotal;
        reviewPly = ply;

        rules.Reset(boardSize);
        rules.Komi = reviewRec != null && reviewRec.komi > 0f ? reviewRec.komi : 7.5f;
        if (reviewRec != null && reviewRec.handicap > 0)
            rules.SetupHandicap(HandicapPoints(boardSize, reviewRec.handicap));

        bool ok = rules.ReplayToPly(reviewLogCsv, reviewPly);
        if (!ok) Flash("棋谱部分着法无法复原");

        if (reviewPly >= reviewTotal && reviewRec != null && !string.IsNullOrEmpty(reviewRec.stateJson))
        {
            var data = JsonUtility.FromJson<GoStateData>(reviewRec.stateJson);
            if (data != null && data.size == boardSize)
                rules.ImportState(data);
        }
        else
        {
            rules.ResumeRecording();
        }
        rules.EnsureMoveAtForDisplay();
        Refresh();
    }

    void RefreshReviewMoveLog()
    {
        if (reviewTotal == 0 || string.IsNullOrEmpty(reviewLogCsv))
        {
            moveLogText.text = "本局无落子记录";
            return;
        }
        var parts = reviewLogCsv.Split('|');
        var sb = new System.Text.StringBuilder(reviewTotal * 28);
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
            string mark = shown + 1 == reviewPly ? "▶ " : "   ";
            if (pass) sb.Append($"{mark}第{shown + 1:000}棋停一手");
            else sb.Append($"{mark}第{shown + 1:000}棋下在（{ColLabel(x)}，{RowLabel(y)}）");
            shown++;
        }
        moveLogText.text = sb.ToString();
        if (moveLogScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            float den = Mathf.Max(1, reviewTotal);
            moveLogScroll.verticalNormalizedPosition = 1f - (reviewPly / den);
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
        komiValText.text = $"贴 {komi:0.0} 目";
        handiValText.text = handicap == 0 ? "不让子" : $"让 {handicap} 子";
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

    Button MakeButton(Transform parent, string label, float height, UnityEngine.Events.UnityAction cb)
    {
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
        btn.onClick.AddListener(cb);

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
        if (btnClear != null) btnClear.SetActive(!rev);
        if (btnScore != null) btnScore.SetActive(!rev);
        if (btnReviewPrev != null) btnReviewPrev.SetActive(rev);
        if (btnReviewNext != null) btnReviewNext.SetActive(rev);
        if (btnReviewEnd != null) btnReviewEnd.SetActive(rev);
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

        if (reviewingKifu && reviewRec != null)
        {
            string names = $"{reviewRec.blackName} vs {reviewRec.whiteName}";
            string res = string.IsNullOrEmpty(reviewRec.result) ? "" : reviewRec.result + "\n";
            turnText.text = $"{reviewRec.title}\n{names}\n{res}第 {reviewPly}/{reviewTotal} 手";
        }
        else if (rules.Result != null) turnText.text = rules.Result;
        else if (aiBusy) turnText.text = (aiColor == GoRules.BLACK ? "黑棋" : "白棋") + "（AI）思考中…";
        else
        {
            string who = rules.Turn == GoRules.BLACK ? "黑棋" : "白棋";
            if (inStudyMode) turnText.text = who + "落子";
            else turnText.text = who + "落子" + (rules.Turn == humanColor ? "（你）" : "（AI）");
        }

        RefreshMoveLog();
        RefreshStudyVisuals();
    }
}
