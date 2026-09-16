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
    bool aiBusy = false;
    bool inMenu = true;

    // 开局设置（主界面选择）
    int sizeChoice = 2;      // 0=9路 1=13路 2=19路
    int modeChoice = 0;      // 0=双人 1=人机
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
    SpriteRenderer ghostSr, markerSr;
    Sprite stoneBlack, stoneWhite, deadXSpr, ringSpr;

    Font font;
    Text turnText, statsText, msgText, scoreText, modeBtnText;
    GameObject passBtn, undoBtn, resignBtn, resumeBtn, scoreBtn;
    Coroutine msgCo;

    // 主界面
    GameObject menuCanvas;
    List<Button> optSize, optMode, optColor;
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

        rules = new GoRules(boardSize);
        BuildBoard();
        BuildUI();
        BuildMenu();
        ShowMenu();
    }

    /* ---------------- 对局流程 ---------------- */

    void StartGame()
    {
        boardSize = sizeChoice == 0 ? 9 : sizeChoice == 1 ? 13 : 19;
        vsAI = modeChoice == 1;
        if (vsAI)
        {
            humanColor = colorChoice == 0 ? GoRules.BLACK
                       : colorChoice == 1 ? GoRules.WHITE
                       : (Random.value < 0.5f ? GoRules.BLACK : GoRules.WHITE);
        }
        else humanColor = GoRules.BLACK;

        aiBusy = false;
        StopAllCoroutines();
        rules.Reset(boardSize);
        rules.Komi = komi;
        if (handicap > 0) rules.SetupHandicap(HandicapPoints(boardSize, handicap));
        BuildBoard();
        inMenu = false;
        menuCanvas.SetActive(false);
        if (vsAI)
            Flash(colorChoice == 2
                ? "猜先结果：你" + (humanColor == GoRules.BLACK ? "执黑" : "执白")
                : humanColor == GoRules.BLACK ? "你执黑" : "你执白");
        Refresh();
        if (vsAI && rules.Result == null && rules.Phase == GoPhase.Play && rules.Turn == aiColor)
            StartCoroutine(AiTurn());
    }

    void ShowMenu()
    {
        inMenu = true;
        menuCanvas.SetActive(true);
        SyncMenu();
        Refresh();
    }

    void HumanPlay(int x, int y)
    {
        if (aiBusy || inMenu || rules.Result != null || rules.Phase != GoPhase.Play) return;
        if (vsAI && rules.Turn != humanColor) return;
        if (!rules.Play(x, y, out string msg)) { Flash(msg); return; }
        AfterMove();
    }

    void HumanPass()
    {
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
            rules.Resign();   // 胜率无望时 AI 认输，不填子赖皮
        }
        else rules.Play(GoAI.ResultX, GoAI.ResultY, out _);
        Refresh();
    }

    void Undo()
    {
        if (aiBusy || inMenu) return;
        if (!rules.Undo(vsAI ? 2 : 1)) { Flash("没有可悔的棋"); return; }
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

        bool canHover = inside && !overUI && rules.Phase == GoPhase.Play && rules.Result == null
                        && !aiBusy && rules.Board[rules.Idx(x, y)] == GoRules.EMPTY
                        && (!vsAI || rules.Turn == humanColor);
        ghostSr.enabled = canHover;
        if (canHover)
        {
            ghostSr.sprite = rules.Turn == GoRules.BLACK ? stoneBlack : stoneWhite;
            ghostSr.transform.position = new Vector3(x, y, 0f);
        }

        if (inside && !overUI && Input.GetMouseButtonDown(0))
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
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
        pimg.color = new Color(0.14f, 0.11f, 0.08f, 0.94f);
        var prt = pgo.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(1f, 0f); prt.anchorMax = new Vector2(1f, 1f);
        prt.pivot = new Vector2(1f, 0.5f);
        prt.sizeDelta = new Vector2(260f, 0f);
        prt.anchoredPosition = Vector2.zero;

        var vlg = pgo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 18, 18);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var pt = pgo.transform;

        MakeText(pt, "围  棋", 30, Ink, TextAnchor.MiddleCenter);
        turnText = MakeText(pt, "", 18, Ink, TextAnchor.MiddleCenter);
        statsText = MakeText(pt, "", 15, Dim, TextAnchor.MiddleCenter);
        msgText = MakeText(pt, "", 14, Accent, TextAnchor.MiddleCenter);
        scoreText = MakeText(pt, "", 15, Ink, TextAnchor.MiddleCenter);

        var row1 = MakeRow(pt, 34f);
        MakeButton(row1, "9 路", 32f, () => { sizeChoice = 0; StartGame(); });
        MakeButton(row1, "13 路", 32f, () => { sizeChoice = 1; StartGame(); });
        MakeButton(row1, "19 路", 32f, () => { sizeChoice = 2; StartGame(); });

        var modeBtn = MakeButton(pt, "", 34f, () => { modeChoice = 1 - modeChoice; StartGame(); });
        modeBtnText = modeBtn.GetComponentInChildren<Text>();

        MakeButton(pt, "返回主界面", 36f, ShowMenu);

        var row2 = MakeRow(pt, 34f);
        undoBtn = MakeButton(row2, "悔棋", 32f, Undo).gameObject;
        passBtn = MakeButton(row2, "停一手", 32f, HumanPass).gameObject;
        resignBtn = MakeButton(pt, "认输", 32f, () => { rules.Resign(); Refresh(); }).gameObject;
        resumeBtn = MakeButton(pt, "继续对局", 32f, () => { rules.ResumePlay(); Refresh(); AfterMove(); }).gameObject;
        scoreBtn = MakeButton(pt, "确认终局", 32f, () => { rules.ConfirmEnd(); Refresh(); }).gameObject;

        MakeText(pt, "黑先贴目，让子局白先。双方各停一手进入数子；数子时点击棋子可将整串标为死子，再点恢复。",
            13, Dim, TextAnchor.UpperLeft);
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

        var panel = new GameObject("MenuPanel");
        panel.transform.SetParent(menuCanvas.transform, false);
        var pimg = panel.AddComponent<Image>();
        pimg.color = new Color(0.14f, 0.11f, 0.08f, 1f);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(470f, 640f);
        prt.anchoredPosition = Vector2.zero;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(30, 30, 24, 24);
        vlg.spacing = 9f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var pt = panel.transform;

        MakeText(pt, "围  棋", 42, Ink, TextAnchor.MiddleCenter);
        MakeText(pt, "对 局 设 置", 14, Dim, TextAnchor.MiddleCenter);

        MakeText(pt, "棋盘", 14, Dim, TextAnchor.MiddleCenter);
        optSize = MakeOptions(pt, new[] { "9 路", "13 路", "19 路" }, i => sizeChoice = i);

        MakeText(pt, "模式", 14, Dim, TextAnchor.MiddleCenter);
        optMode = MakeOptions(pt, new[] { "双人对弈", "人机对战" }, i => modeChoice = i);

        MakeText(pt, "执子（人机模式有效）", 14, Dim, TextAnchor.MiddleCenter);
        optColor = MakeOptions(pt, new[] { "执黑先行", "执白后行", "猜先" }, i => colorChoice = i);

        MakeText(pt, "贴目", 14, Dim, TextAnchor.MiddleCenter);
        komiValText = MakeStepper(pt,
            () => komi = Mathf.Max(0f, komi - 0.5f),
            () => komi = Mathf.Min(15f, komi + 0.5f));

        MakeText(pt, "让子（黑方预先布子，白方先下）", 14, Dim, TextAnchor.MiddleCenter);
        handiValText = MakeStepper(pt,
            () => handicap = Mathf.Max(0, handicap - 1),
            () => handicap = Mathf.Min(9, handicap + 1));

        var start = MakeButton(pt, "开 始 对 局", 46f, StartGame);
        start.GetComponent<Image>().color = Accent;
        var st = start.GetComponentInChildren<Text>();
        st.color = new Color(0.11f, 0.09f, 0.07f);
        st.fontSize = 18;
    }

    void SyncMenu()
    {
        PaintOptions(optSize, sizeChoice);
        PaintOptions(optMode, modeChoice);
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
        var minus = MakeButton(row, "－", 34f, () => { onMinus(); SyncMenu(); });
        SetFixedWidth(minus, 70f);

        var tgo = new GameObject("Val");
        tgo.transform.SetParent(row, false);
        var t = tgo.AddComponent<Text>();
        t.font = font; t.fontSize = 17; t.color = Ink; t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var le = tgo.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;

        var plus = MakeButton(row, "＋", 34f, () => { onPlus(); SyncMenu(); });
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

        if (rules.LastMove >= 0 && rules.Phase != GoPhase.Over)
        {
            markerSr.enabled = true;
            markerSr.transform.position = new Vector3(rules.LastMove % n, rules.LastMove / n, 0f);
            markerSr.color = rules.Board[rules.LastMove] == GoRules.BLACK
                ? new Color(0.91f, 0.72f, 0.38f) : new Color(0.35f, 0.22f, 0.06f);
        }
        else markerSr.enabled = false;

        if (rules.Result != null) turnText.text = rules.Result;
        else if (rules.Phase == GoPhase.Scoring) turnText.text = "数子中 · 点击棋子标死";
        else if (aiBusy) turnText.text = (aiColor == GoRules.BLACK ? "黑棋" : "白棋") + "（AI）思考中…";
        else
        {
            string who = rules.Turn == GoRules.BLACK ? "黑棋" : "白棋";
            string side = vsAI ? (rules.Turn == humanColor ? "（你）" : "（AI）") : "";
            turnText.text = who + "落子" + side;
        }

        string phase = rules.Phase == GoPhase.Play ? "对局中" : rules.Phase == GoPhase.Scoring ? "数子" : "终局";
        statsText.text = $"手数 {rules.MoveCount}    状态 {phase}\n黑提子 {rules.Captures[GoRules.BLACK]}    白提子 {rules.Captures[GoRules.WHITE]}";

        if (rules.Phase != GoPhase.Play)
        {
            rules.ComputeScore(out float bs, out float ws, out int bst, out int bt, out int wst, out int wt);
            scoreText.text = $"黑：子 {bst} + 空 {bt} = {bs}\n白：子 {wst} + 空 {wt} + 贴目 {rules.Komi:0.0} = {ws:0.0}";
        }
        else scoreText.text = "";

        bool playing = rules.Phase == GoPhase.Play && rules.Result == null;
        passBtn.SetActive(playing);
        undoBtn.SetActive(playing || rules.Phase == GoPhase.Scoring);
        resignBtn.SetActive(playing);
        resumeBtn.SetActive(rules.Phase == GoPhase.Scoring);
        scoreBtn.SetActive(rules.Phase == GoPhase.Scoring);

        if (modeBtnText != null) modeBtnText.text = vsAI ? "模式：人机对战" : "模式：双人对弈";
    }
}
