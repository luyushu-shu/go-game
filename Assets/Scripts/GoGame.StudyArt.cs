using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public partial class GoGame
{
    static readonly Color StudyInk = new Color(0.22f, 0.13f, 0.065f);
    static readonly Color StudyGold = new Color(0.76f, 0.59f, 0.34f);
    readonly Dictionary<string, Sprite> studyArtSprites = new Dictionary<string, Sprite>();
    GameObject studyArtRoot, studyLegacyPanel, studyArtDialog;
    RectTransform studyArtCanvas, studyArtPanel, studyArtToggles, studyArtDialogCard;
    SpriteRenderer studyArtBackground;
    Text studyArtLog, studyArtTurn, studyArtMessage, studyBlackCaptures, studyWhiteCaptures;
    Text studyArtMovesLabel, studyArtCoordsLabel, studyArtLead, studyArtBlackScore, studyArtWhiteScore;
    Text studyArtBlackPrisoners, studyArtWhitePrisoners, studyArtDialogTitle, studyArtDialogNote;
    Button studyArtUndo, studyArtPass, studyArtDraw, studyArtScore, studyArtResign;
    Button studyArtDialogConfirm;
    bool studyArtWasActive;
    float studyArtLastAspect = -1f;
    bool StudyArtActive => inStudyMode && !reviewingKifu && !inMenu;

    Sprite StudyArtSprite(string name)
    {
        if (studyArtSprites.TryGetValue(name, out var cached)) return cached;
        var tex = Resources.Load<Texture2D>("UI/Kifu/" + name);
        if (tex == null) { Debug.LogWarning("缺少打谱 UI 素材: " + name); return null; }
        // Trim the sidebar's large transparent side margins without changing its source image.
        Rect rect = name == "sidebar-walnut-v1"
            ? new Rect(108, 0, 758, tex.height) : new Rect(0, 0, tex.width, tex.height);
        Vector4 border = name == "button-parchment-v1" ? new Vector4(260, 190, 260, 190)
            : name == "toggle-walnut-v1" ? new Vector4(210, 210, 210, 210)
            : new Vector4(130, 130, 130, 130);
        var sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, border);
        studyArtSprites[name] = sprite;
        return sprite;
    }

    RectTransform StudyArtRect(Transform parent, string name, Vector2 min, Vector2 max,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        return rt;
    }

    Image StudyArtImage(RectTransform rt, string asset, bool blockInput = false)
    {
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = StudyArtSprite(asset);
        image.type = asset == "toggle-walnut-v1" ? Image.Type.Simple : Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = asset == "button-parchment-v1" ? 9f : 3f;
        image.color = image.sprite != null ? Color.white : BtnBg;
        image.raycastTarget = blockInput;
        return image;
    }

    Text StudyArtText(Transform parent, string name, string value, int size, Color color,
        Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rt = StudyArtRect(parent, name, min, max, offsetMin, offsetMax);
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font; text.text = value; text.fontSize = size; text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }

    Button StudyArtButton(Transform parent, string label, UnityAction action,
        Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, bool dark = false)
    {
        var rt = StudyArtRect(parent, "StudyButton_" + label, min, max, offsetMin, offsetMax);
        var image = StudyArtImage(rt, dark ? "toggle-walnut-v1" : "button-parchment-v1", true);
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.94f, 0.78f);
        colors.pressedColor = new Color(0.75f, 0.61f, 0.40f);
        colors.disabledColor = new Color(0.65f, 0.62f, 0.57f, 0.65f);
        colors.fadeDuration = 0.12f; button.colors = colors;
        button.onClick.AddListener(action);
        StudyArtText(rt, "Label", label, dark ? 25 : 18, dark ? Ink : StudyInk,
            Vector2.zero, Vector2.one, new Vector2(5, 5), new Vector2(-5, -5));
        return button;
    }

    void StudyArtStone(Transform parent, bool black, Vector2 center, float size)
    {
        var rt = StudyArtRect(parent, black ? "BlackStone" : "WhiteStone", center, center,
            new Vector2(-size / 2, -size / 2), new Vector2(size / 2, size / 2));
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = black ? stoneBlack : stoneWhite;
        image.preserveAspect = true; image.raycastTarget = false;
    }

    void StudyArtRule(Transform parent, float y)
    {
        var rt = StudyArtRect(parent, "Divider", new Vector2(0, y), new Vector2(1, y),
            new Vector2(32, 0), new Vector2(-32, 1));
        var image = rt.gameObject.AddComponent<Image>();
        image.color = new Color(0.64f, 0.44f, 0.20f, 0.35f); image.raycastTarget = false;
    }

    void BuildStudyArtUi(Transform canvasRoot, GameObject legacyPanel)
    {
        studyLegacyPanel = legacyPanel;
        studyArtCanvas = (RectTransform)canvasRoot;
        var bg = new GameObject("StudyOverheadBackground");
        studyArtBackground = bg.AddComponent<SpriteRenderer>();
        studyArtBackground.sprite = StudyArtSprite("background-overhead-v2");
        studyArtBackground.sortingOrder = -100;
        studyArtBackground.enabled = false;

        var root = StudyArtRect(canvasRoot, "StudyArt", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        studyArtRoot = root.gameObject;
        // The root deliberately has no Image: nothing is drawn across or intercepts the board.
        studyArtPanel = StudyArtRect(root, "WalnutSidebar", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        StudyArtImage(studyArtPanel, "sidebar-walnut-v1", true);
        var rightRim = StudyArtRect(studyArtPanel, "RightRim", new Vector2(1, 0), Vector2.one,
            new Vector2(-11, 17), new Vector2(-8, -17));
        var rightRimImage = rightRim.gameObject.AddComponent<Image>();
        rightRimImage.color = new Color(.72f, .55f, .30f, .78f);
        rightRimImage.raycastTarget = false;
        var players = StudyArtRect(studyArtPanel, "Players", new Vector2(0, 1), Vector2.one,
            new Vector2(20, -125), new Vector2(-20, -20));
        StudyArtStone(players, true, new Vector2(0.25f, 0.70f), 58);
        StudyArtStone(players, false, new Vector2(0.75f, 0.70f), 58);
        studyBlackCaptures = StudyArtText(players, "BlackCaptures", "0", 26, Ink, new Vector2(0, 0), new Vector2(.4f, .38f), Vector2.zero, Vector2.zero);
        StudyArtText(players, "CapturesTitle", "提子", 17, StudyGold, new Vector2(.4f, 0), new Vector2(.6f, .38f), Vector2.zero, Vector2.zero);
        studyWhiteCaptures = StudyArtText(players, "WhiteCaptures", "0", 26, Ink, new Vector2(.6f, 0), new Vector2(1, .38f), Vector2.zero, Vector2.zero);

        var record = StudyArtRect(studyArtPanel, "ParchmentRecord", Vector2.zero, Vector2.one,
            new Vector2(22, 207), new Vector2(-22, -137));
        StudyArtImage(record, "record-parchment-v1");
        StudyArtText(record, "RecordTitle", "行 棋 记 录", 22, StudyInk,
            new Vector2(0, 1), Vector2.one, new Vector2(22, -61), new Vector2(-22, -18));
        var viewport = StudyArtRect(record, "RecordViewport", Vector2.zero, Vector2.one,
            new Vector2(30, 32), new Vector2(-30, -70));
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
        var content = StudyArtRect(viewport, "RecordContent", new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
        content.pivot = new Vector2(.5f, 1);
        studyArtLog = content.gameObject.AddComponent<Text>();
        studyArtLog.font = font; studyArtLog.fontSize = 19; studyArtLog.color = StudyInk;
        studyArtLog.alignment = TextAnchor.UpperLeft;
        studyArtLog.horizontalOverflow = HorizontalWrapMode.Wrap;
        studyArtLog.verticalOverflow = VerticalWrapMode.Overflow;
        studyArtLog.lineSpacing = 1.45f; studyArtLog.raycastTarget = false;
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content;

        studyArtTurn = StudyArtText(studyArtPanel, "Turn", "", 18, Ink, Vector2.zero, new Vector2(1, 0), new Vector2(22, 172), new Vector2(-22, 205));
        studyArtMessage = StudyArtText(studyArtPanel, "SaveStatus", "自动保存", 13, StudyGold, Vector2.zero, new Vector2(1, 0), new Vector2(22, 148), new Vector2(-22, 173));
        var actions = StudyArtRect(studyArtPanel, "FiveActions", Vector2.zero, new Vector2(1, 0), new Vector2(22, 80), new Vector2(-22, 145));
        studyArtUndo = StudyArtAction(actions, "悔棋", Undo, 0);
        studyArtPass = StudyArtAction(actions, "停一手", HumanPass, 1);
        studyArtDraw = StudyArtAction(actions, "和棋", OpenStudyDrawDialog, 2);
        studyArtScore = StudyArtAction(actions, "数目", OpenScorePopup, 3);
        studyArtResign = StudyArtAction(actions, "认输", HumanResign, 4);
        StudyArtButton(studyArtPanel, "形势判断", OpenStudyJudgment,
            Vector2.zero, new Vector2(.5f, 0), new Vector2(22, 23), new Vector2(-4, 75));
        StudyArtButton(studyArtPanel, "返回主界面", ReturnToMainMenu,
            new Vector2(.5f, 0), new Vector2(1, 0), new Vector2(4, 23), new Vector2(-22, 75));
        studyArtToggles = StudyArtRect(root, "CenteredToggles", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        studyArtMovesLabel = StudyArtButton(studyArtToggles, "手数", () => { showMoveNums = !showMoveNums; boardSkin.Refresh(rules, showMoveNums, showCoords, showTerritory); RefreshStudyVisuals(); RefreshStudyArtUi(); },
            new Vector2(0, .5f), Vector2.one, new Vector2(0, 5), Vector2.zero, true).GetComponentInChildren<Text>();
        studyArtCoordsLabel = StudyArtButton(studyArtToggles, "坐标", () => { showCoords = !showCoords; boardSkin.Refresh(rules, showMoveNums, showCoords, showTerritory); RefreshStudyVisuals(); RefreshStudyArtUi(); },
            Vector2.zero, new Vector2(1, .5f), Vector2.zero, new Vector2(0, -5), true).GetComponentInChildren<Text>();
        BuildStudyArtDialog(modalCanvasLayer.transform);
        root.SetAsFirstSibling();
        studyArtRoot.SetActive(false);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        StartStudyArtPreviewIfRequested();
#endif
    }

    Button StudyArtAction(Transform parent, string label, UnityAction action, int index)
    {
        var button = StudyArtButton(parent, label, action, new Vector2(index / 5f, 0),
            new Vector2((index + 1) / 5f, 1), new Vector2(2, 0), new Vector2(-2, 0));
        button.GetComponentInChildren<Text>().fontSize = 20;
        return button;
    }

    void UpdateStudyArtLayout()
    {
        if (studyArtRoot == null) return;
        bool active = StudyArtActive;
        studyArtRoot.SetActive(active);
        studyLegacyPanel.SetActive(!active);
        studyArtBackground.enabled = active;
        if (sceneBackgroundLayer != null) sceneBackgroundLayer.SetActive(!active);
        if (!active)
        {
            if (studyArtWasActive)
            {
                HideStudyArtDialog();
                ApplyResponsiveLayout();
                cam.orthographicSize = (boardSize + 1) / 2f + .7f;
                cam.transform.position = new Vector3((boardSize - 1) / 2f, (boardSize - 1) / 2f, -10);
            }
            studyArtWasActive = false;
            return;
        }
        cam.rect = new Rect(0, 0, 1, 1);
        if (boardSkin != null) boardSkin.SetToolbarVisible(false);
        if (studyBoardUi != null) studyBoardUi.SetActive(false);
        if (studyTerritoryText != null) studyTerritoryText.gameObject.SetActive(false);
        float width = studyArtCanvas.rect.width, height = studyArtCanvas.rect.height;
        if (width <= 0 || height <= 0) return;
        float margin = 64;
        float panelWidth = Mathf.Clamp(width * .315f, 420, 500);
        float panelHeight = Mathf.Min(height - 2 * margin, 920);
        studyArtPanel.anchorMin = studyArtPanel.anchorMax = new Vector2(1, .5f);
        studyArtPanel.pivot = new Vector2(1, .5f);
        studyArtPanel.sizeDelta = new Vector2(panelWidth, panelHeight);
        studyArtPanel.anchoredPosition = new Vector2(-margin, 0);
        float gap = 82;
        float regionWidth = width - panelWidth - gap - 3 * margin;
        float boardPixels = Mathf.Min(height - 2 * margin, regionWidth);
        float boardCenterX = margin + regionWidth * .5f;
        float worldHeight = (boardSize + 1) * height / Mathf.Max(boardPixels, 1);
        cam.orthographicSize = worldHeight / 2;
        float worldWidth = worldHeight * cam.aspect;
        float center = (boardSize - 1) / 2f;
        cam.transform.position = new Vector3(center - (boardCenterX / width - .5f) * worldWidth, center, -10);
        studyArtToggles.anchorMin = studyArtToggles.anchorMax = new Vector2(0, .5f);
        studyArtToggles.pivot = new Vector2(.5f, .5f);
        studyArtToggles.sizeDelta = new Vector2(108, 208);
        float rightOfBoard = boardCenterX + boardPixels / 2;
        float leftOfPanel = width - margin - panelWidth;
        studyArtToggles.anchoredPosition = new Vector2((rightOfBoard + leftOfPanel) / 2, 0);
        var sprite = studyArtBackground.sprite;
        if (sprite != null)
        {
            float scale = Mathf.Max(worldWidth / sprite.bounds.size.x, worldHeight / sprite.bounds.size.y);
            studyArtBackground.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 2);
            studyArtBackground.transform.localScale = Vector3.one * scale;
        }
        studyArtDialogCard.anchorMin = studyArtDialogCard.anchorMax = new Vector2(1, .5f);
        studyArtDialogCard.pivot = new Vector2(.5f, .5f);
        studyArtDialogCard.sizeDelta = new Vector2(panelWidth - 24, Mathf.Min(panelHeight - 50, 550));
        studyArtDialogCard.anchoredPosition = new Vector2(-margin - panelWidth / 2, 0);
        studyArtDialogCard.localScale = Vector3.one;
        if (!studyArtWasActive || !Mathf.Approximately(studyArtLastAspect, width / height)) RefreshStudyArtUi();
        studyArtLastAspect = width / height;
        studyArtWasActive = true;
        if (studyArtMessage != null) studyArtMessage.text = !string.IsNullOrEmpty(msgText.text) ? msgText.text : (studySaveStatusText != null ? studySaveStatusText.text : "自动保存");
    }

    void RefreshStudyArtUi()
    {
        if (studyArtRoot == null || rules == null) return;
        studyBlackCaptures.text = rules.Captures[GoRules.BLACK].ToString();
        studyWhiteCaptures.text = rules.Captures[GoRules.WHITE].ToString();
        studyArtTurn.text = rules.Result ?? $"第 {rules.LoggedMoveCount} 手 · {(rules.Turn == GoRules.BLACK ? "黑方" : "白方")}行棋";
        if (rules.Phase == GoPhase.Scoring && rules.Result == null) studyArtTurn.text = "数目阶段 · 点击棋子标记死活";
        string nextLog = moveLogText != null ? moveLogText.text : "暂无落子";
        bool changed = studyArtLog.text != nextLog;
        studyArtLog.text = nextLog;
        if (changed && StudyArtActive && studyArtRoot.activeInHierarchy)
        {
            Canvas.ForceUpdateCanvases();
            studyArtLog.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 0;
        }
        studyArtMovesLabel.color = showMoveNums ? new Color(1, .83f, .47f) : Dim;
        studyArtCoordsLabel.color = showCoords ? new Color(1, .83f, .47f) : Dim;
        bool playing = rules.Result == null && rules.Phase == GoPhase.Play;
        studyArtUndo.interactable = rules.LoggedMoveCount > 0;
        studyArtPass.interactable = playing;
        studyArtDraw.interactable = rules.Result == null;
        studyArtResign.interactable = rules.Result == null;
        studyArtScore.interactable = rules.Result == null;
    }

    void BuildStudyArtDialog(Transform parent)
    {
        var overlay = StudyArtRect(parent, "StudyJudgmentDialog", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        studyArtDialog = overlay.gameObject;
        var dim = overlay.gameObject.AddComponent<Image>(); dim.color = new Color(.035f, .025f, .015f, .68f);
        studyArtDialogCard = StudyArtRect(overlay, "ParchmentDialog", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-310, -260), new Vector2(310, 260));
        StudyArtImage(studyArtDialogCard, "judgment-dialog-v1", true);
        studyArtDialogTitle = StudyArtText(studyArtDialogCard, "Title", "形势判断", 30, StudyInk, new Vector2(0, .78f), new Vector2(1, .94f), new Vector2(45, 0), new Vector2(-45, 0));
        StudyArtButton(studyArtDialogCard, "×", HideStudyArtDialog, new Vector2(1, 1), Vector2.one, new Vector2(-75, -75), new Vector2(-35, -35)).GetComponentInChildren<Text>().fontSize = 25;
        StudyArtStone(studyArtDialogCard, true, new Vector2(.16f, .65f), 65);
        StudyArtStone(studyArtDialogCard, false, new Vector2(.84f, .65f), 65);
        studyArtLead = StudyArtText(studyArtDialogCard, "Lead", "", 25, StudyInk, new Vector2(.25f, .55f), new Vector2(.75f, .75f), Vector2.zero, Vector2.zero);
        StudyArtRule(studyArtDialogCard, .51f);
        StudyArtRule(studyArtDialogCard, .39f);
        StudyArtRule(studyArtDialogCard, .27f);
        studyArtBlackScore = StudyArtComparison("BlackScore", "0", .08f, .36f, .40f, .50f, 27);
        StudyArtComparison("ScoreLabel", "目数", .36f, .64f, .40f, .50f, 20);
        studyArtWhiteScore = StudyArtComparison("WhiteScore", "0", .64f, .92f, .40f, .50f, 27);
        studyArtBlackPrisoners = StudyArtComparison("BlackPrisoners", "0", .08f, .36f, .28f, .38f, 27);
        StudyArtComparison("PrisonersLabel", "提子数", .36f, .64f, .28f, .38f, 20);
        studyArtWhitePrisoners = StudyArtComparison("WhitePrisoners", "0", .64f, .92f, .28f, .38f, 27);
        studyArtDialogNote = StudyArtComparison("Note", "白方目数含贴目 · 估目仅供参考", .1f, .9f, .19f, .26f, 13);
        studyArtDialogConfirm = StudyArtButton(studyArtDialogCard, "知道了", HideStudyArtDialog, new Vector2(.32f, .07f), new Vector2(.68f, .18f), Vector2.zero, Vector2.zero);
        studyArtDialog.SetActive(false);
    }

    Text StudyArtComparison(string name, string text, float x0, float x1, float y0, float y1, int size)
    {
        return StudyArtText(studyArtDialogCard, name, text, size, StudyInk, new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero);
    }

    void OpenStudyJudgment()
    {
        rules.EstimateSituation(out float black, out float white, out _, out _, out _, out _, out _, out _, out _);
        float lead = black - white;
        studyArtDialogTitle.text = "形势判断";
        studyArtLead.text = Mathf.Abs(lead) < .05f ? "双方均势" : $"{(lead > 0 ? "黑棋" : "白棋")}领先 {Mathf.Abs(lead):0.#} 目";
        studyArtBlackScore.text = black.ToString("0.#");
        studyArtWhiteScore.text = white.ToString("0.#");
        studyArtBlackPrisoners.text = rules.Captures[GoRules.BLACK].ToString();
        studyArtWhitePrisoners.text = rules.Captures[GoRules.WHITE].ToString();
        studyArtDialogNote.text = $"白方含贴目 {rules.Komi:0.#} 目 · 估目仅供参考";
        ConfigureStudyDialogButton("知道了", HideStudyArtDialog);
        studyArtDialog.transform.SetAsLastSibling(); studyArtDialog.SetActive(true);
    }

    void OpenStudyDrawDialog()
    {
        OpenStudyJudgment();
        studyArtDialogTitle.text = "双方和棋";
        studyArtLead.text = "确认双方同意和棋？";
        studyArtDialogNote.text = "确认后本局结束，当前棋谱可保存";
        ConfigureStudyDialogButton("确认和棋", () =>
        {
            HideStudyArtDialog();
            rules.AgreeDraw();
            StopPlayClock(); SaveTerminalDraft(); Refresh(); ShowResultPopup(rules.Result);
        });
    }

    void ConfigureStudyDialogButton(string label, UnityAction callback)
    {
        studyArtDialogConfirm.GetComponentInChildren<Text>().text = label;
        studyArtDialogConfirm.onClick.RemoveAllListeners();
        studyArtDialogConfirm.onClick.AddListener(callback);
    }

    void HideStudyArtDialog()
    {
        if (studyArtDialog != null) studyArtDialog.SetActive(false);
    }
}
