using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public partial class GoGame
{
    bool AiArtActive => vsAI && !inMenu && !reviewingKifu;
    readonly Dictionary<string, Sprite> aiArtSprites = new Dictionary<string, Sprite>();
    GameObject aiArtRoot;
    RectTransform aiArtPanel, aiArtPlayers, aiArtRecord, aiArtActions, aiArtTools;
    RectTransform aiArtLeftBowl, aiArtRightBowl, aiArtLeftLid, aiArtRightLid;
    RectTransform aiArtContent;
    RectTransform aiArtPlayerAvatar, aiArtComputerAvatar;
    SpriteRenderer aiArtBackground;
    ScrollRect aiArtScroll;
    Image aiArtPlayerStone, aiArtComputerStone, aiArtPlayerFrame, aiArtComputerFrame;
    Text aiArtKomi, aiArtPlayerCaptures, aiArtComputerCaptures, aiArtPlayerTime, aiArtComputerTime;
    Text aiArtPlayerPeriods, aiArtComputerPeriods;
    Button aiArtUndo, aiArtPass, aiArtDraw, aiArtResign, aiArtScore;
    int aiArtRecordSignature = int.MinValue;
    bool aiArtWasActive;
    readonly List<Image> aiArtLeftPrisoners = new List<Image>();
    readonly List<Image> aiArtRightPrisoners = new List<Image>();

    Sprite AiArtSprite(string name)
    {
        if (aiArtSprites.TryGetValue(name, out var sprite)) return sprite;
        var texture = Resources.Load<Texture2D>("UI/AiMatch/" + name);
        if (texture == null) { Debug.LogError("Missing AI match art: " + name); return null; }
        // Trim authored clear padding in the runtime sprite, preserving the source PNG.
        Rect rect = new Rect(0, 0, texture.width, texture.height);
        if (name == "players-card-v1") rect = AiArtCrop(texture, 15f / 1510, 140f / 1041, 1480f / 1510, 780f / 1041);
        if (name == "button-analysis-v1") rect = AiArtCrop(texture, 45f / 2174, 90f / 723, 2080f / 2174, 550f / 723);
        if (name == "avatar-frame-v1") rect = AiArtCrop(texture, 65f / 1254, 85f / 1254, 1120f / 1254, 1120f / 1254);
        sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100, 0,
            SpriteMeshType.FullRect, new Vector4(160, 160, 160, 160));
        aiArtSprites[name] = sprite;
        return sprite;
    }

    static Rect AiArtCrop(Texture2D texture, float x, float y, float width, float height)
    {
        return new Rect(texture.width * x, texture.height * y, texture.width * width, texture.height * height);
    }

    Image AiArtImage(RectTransform rect, string name, bool sliced = false, bool hit = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = AiArtSprite(name);
        image.raycastTarget = hit;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = 6;
        image.preserveAspect = !sliced;
        return image;
    }

    Text AiArtText(Transform parent, string name, string value, int size,
        float x0, float y0, float x1, float y1)
    {
        var text = StudyArtText(parent, name, value, size, StudyInk,
            new Vector2(x0, y0), new Vector2(x1, y1), Vector2.zero, Vector2.zero);
        text.font = restoredMenuFont != null ? restoredMenuFont : font;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Min(16, size);
        text.resizeTextMaxSize = size;
        return text;
    }

    RectTransform AiArtRect(Transform parent, string name, float x0, float y0, float x1, float y1)
    {
        return StudyArtRect(parent, name, new Vector2(x0, y0), new Vector2(x1, y1),
            Vector2.zero, Vector2.zero);
    }

    void BuildAiArtUi(Transform parent)
    {
        aiArtRoot = AiArtRect(parent, "AiMatchArt", 0, 0, 1, 1).gameObject;
        aiArtRoot.transform.SetAsFirstSibling();
        var background = new GameObject("AiMatchTabletop");
        aiArtBackground = background.AddComponent<SpriteRenderer>();
        aiArtBackground.sprite = AiArtSprite("tabletop-v1");
        aiArtBackground.sortingOrder = -100;
        aiArtBackground.enabled = false;

        aiArtPanel = AiArtRect(aiArtRoot.transform, "WalnutSidebar", 1, .5f, 1, .5f);
        StudyArtImage(aiArtPanel, "sidebar-walnut-v1", true);
        aiArtPlayers = AiArtRect(aiArtPanel, "PlayersCard", 0, 0, 1, 1);
        AiArtImage(aiArtPlayers, "players-card-v1", true);
        BuildAiArtAvatar(aiArtPlayers, true);
        BuildAiArtAvatar(aiArtPlayers, false);
        AiArtText(aiArtPlayers, "KomiLabel", "贴目", 22, .40f, .68f, .60f, .82f);
        aiArtKomi = AiArtText(aiArtPlayers, "Komi", "7.5目", 26, .37f, .53f, .63f, .69f);
        AiArtText(aiArtPlayers, "PlayerName", "玩家", 25, .10f, .39f, .40f, .53f);
        AiArtText(aiArtPlayers, "ComputerName", "电脑", 25, .60f, .39f, .90f, .53f);
        aiArtPlayerCaptures = AiArtText(aiArtPlayers, "PlayerCaptures", "0", 27, .12f, .22f, .38f, .39f);
        AiArtText(aiArtPlayers, "CapturesLabel", "提子", 22, .40f, .22f, .60f, .39f);
        aiArtComputerCaptures = AiArtText(aiArtPlayers, "ComputerCaptures", "0", 27, .62f, .22f, .88f, .39f);
        aiArtPlayerTime = AiArtText(aiArtPlayers, "PlayerTime", "", 30, .08f, .07f, .42f, .23f);
        aiArtComputerTime = AiArtText(aiArtPlayers, "ComputerTime", "", 30, .58f, .07f, .92f, .23f);
        aiArtPlayerPeriods = AiArtText(aiArtPlayers, "PlayerByoyomi", "", 16, .33f, .16f, .40f, .25f);
        aiArtComputerPeriods = AiArtText(aiArtPlayers, "ComputerByoyomi", "", 16, .83f, .16f, .90f, .25f);
        aiArtPlayerTime.font = aiArtComputerTime.font = numberFont;
        aiArtPlayerPeriods.font = aiArtComputerPeriods.font = numberFont;

        aiArtRecord = AiArtRect(aiArtPanel, "MoveRecord", 0, 0, 1, 1);
        StudyArtImage(aiArtRecord, "record-parchment-v1");
        StudyArtLabelImage(aiArtRecord, "RecordTitle", "record-title-v1",
            new Vector2(.25f, 1), new Vector2(.75f, 1), new Vector2(0, -58), new Vector2(0, -20));
        var viewport = StudyArtRect(aiArtRecord, "Viewport", Vector2.zero, Vector2.one,
            new Vector2(30, 25), new Vector2(-30, -65));
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        aiArtScroll = viewport.gameObject.AddComponent<ScrollRect>();
        aiArtScroll.horizontal = false;
        aiArtScroll.movementType = ScrollRect.MovementType.Clamped;
        aiArtScroll.scrollSensitivity = 30;
        aiArtContent = AiArtRect(viewport, "Rows", 0, 1, 1, 1);
        aiArtContent.pivot = new Vector2(.5f, 1);
        aiArtScroll.viewport = viewport; aiArtScroll.content = aiArtContent;

        aiArtActions = AiArtRect(aiArtPanel, "Actions", 0, 0, 1, 1);
        aiArtUndo = AiArtButton(aiArtActions, "悔棋", Undo, 0, .52f, .25f, 1);
        aiArtPass = AiArtButton(aiArtActions, "停一手", HumanPass, .25f, .52f, .5f, 1);
        aiArtDraw = AiArtButton(aiArtActions, "和棋", OpenAiArtDraw, .5f, .52f, .75f, 1);
        aiArtResign = AiArtButton(aiArtActions, "认输", HumanResign, .75f, .52f, 1, 1);
        AiArtButton(aiArtActions, "形势判断", OpenStudyJudgment, 0, 0, .5f, .46f, true);
        aiArtScore = AiArtButton(aiArtActions, "数目", OpenScorePopup, .5f, 0, 1, .46f, true);
        aiArtTools = AiArtRect(aiArtRoot.transform, "BoardTools", 0, 0, 0, 0);
        AiArtButton(aiArtTools, "手数", () => ToggleAiArtBoard(true), 0, .54f, 1, 1);
        AiArtButton(aiArtTools, "坐标", () => ToggleAiArtBoard(false), 0, 0, 1, .46f);
        aiArtLeftBowl = AiArtRect(aiArtRoot.transform, "ComputerBowl", 0, 0, 0, 0);
        AiArtImage(aiArtLeftBowl, "bowl-black-v1");
        aiArtRightBowl = AiArtRect(aiArtRoot.transform, "PlayerBowl", 0, 0, 0, 0);
        AiArtImage(aiArtRightBowl, "bowl-white-v1");
        aiArtLeftLid = AiArtRect(aiArtRoot.transform, "ComputerLid", 0, 0, 0, 0);
        AiArtImage(aiArtLeftLid, "lid-black-v1");
        aiArtRightLid = AiArtRect(aiArtRoot.transform, "PlayerLid", 0, 0, 0, 0);
        AiArtImage(aiArtRightLid, "lid-white-v1");
        BuildAiArtPrisoners(aiArtLeftLid, aiArtLeftPrisoners);
        BuildAiArtPrisoners(aiArtRightLid, aiArtRightPrisoners);
        aiArtRoot.SetActive(false);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        StartAiArtPreviewIfRequested();
#endif
    }

    void BuildAiArtAvatar(Transform parent, bool player)
    {
        float x = player ? .25f : .75f;
        var root = AiArtRect(parent, player ? "PlayerAvatar" : "ComputerAvatar", x, .74f, x, .74f);
        if (player) aiArtPlayerAvatar = root; else aiArtComputerAvatar = root;
        var mask = AiArtRect(root, "PortraitMask", .055f, .055f, .945f, .945f);
        var maskImage = mask.gameObject.AddComponent<Image>();
        maskImage.sprite = candBadgeSpr; maskImage.color = new Color(.84f, .86f, .78f);
        maskImage.raycastTarget = false;
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        AiArtImage(AiArtRect(mask, "InkPortrait", 0, 0, 1, 1), player
            ? "avatar-player-ink-profile-v5" : "avatar-computer-ink-sage-v3");
        var frame = AiArtImage(AiArtRect(root, "SeparateFrame", 0, 0, 1, 1), "avatar-frame-v1");
        var stone = AiArtRect(root, "SideBadge", .72f, -.015f, 1.035f, .30f).gameObject.AddComponent<Image>();
        stone.preserveAspect = true; stone.raycastTarget = false;
        if (player) { aiArtPlayerStone = stone; aiArtPlayerFrame = frame; }
        else { aiArtComputerStone = stone; aiArtComputerFrame = frame; }
    }

    Button AiArtButton(Transform parent, string label, UnityAction callback,
        float x0, float y0, float x1, float y1, bool green = false)
    {
        var rect = StudyArtRect(parent, "Action_" + label, new Vector2(x0, y0), new Vector2(x1, y1),
            new Vector2(3, 0), new Vector2(-3, 0));
        var image = green ? AiArtImage(rect, "button-analysis-v1", true, true)
            : StudyArtImage(rect, "button-parchment-v1", true);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = new Color(1, .95f, .83f);
        colors.pressedColor = new Color(.80f, .72f, .58f);
        colors.disabledColor = new Color(.7f, .7f, .7f, .65f);
        button.colors = colors; button.onClick.AddListener(callback);
        var caption = AiArtText(rect, "Label", label, label.Length > 4 ? 20 : 23, .08f, .04f, .92f, .96f);
        caption.verticalOverflow = VerticalWrapMode.Overflow;
        if (label == "认输") caption.color = new Color(.62f, .17f, .12f);
        return button;
    }

    void ToggleAiArtBoard(bool moves)
    {
        if (moves) showMoveNums = !showMoveNums; else showCoords = !showCoords;
        boardSkin.Refresh(rules, showMoveNums, showCoords, showTerritory);
    }

    void OpenAiArtDraw()
    {
        if (aiBusy || rules.Result != null || rules.Phase != GoPhase.Play) return;
        OpenStudyDrawDialog();
        // An agreed draw must cancel a pending opponent request before ending the game.
        ConfigureStudyDialogButton("确认和棋", () =>
        {
            InvalidateAiTurn(); HideStudyArtDialog(); rules.AgreeDraw();
            StopPlayClock(); SaveTerminalDraft(); Refresh(); ShowResultPopup(rules.Result);
        });
    }

    void BuildAiArtPrisoners(Transform lid, List<Image> images)
    {
        for (int i = 0; i < 12; i++)
        {
            float a = i * 2.39996f, r = .055f * Mathf.Sqrt(i);
            var point = new Vector2(.5f + Mathf.Cos(a) * r, .5f + Mathf.Sin(a) * r);
            var rect = StudyArtRect(lid, "CapturedStone_" + i, point, point,
                new Vector2(-9, -9), new Vector2(9, 9));
            var image = rect.gameObject.AddComponent<Image>(); image.preserveAspect = true;
            image.raycastTarget = false; image.gameObject.SetActive(false); images.Add(image);
        }
    }

    void UpdateAiArtLayout()
    {
        if (aiArtRoot == null) return;
        bool active = AiArtActive;
        aiArtRoot.SetActive(active); aiArtBackground.enabled = active;
        if (!active)
        {
            if (aiArtWasActive && !StudyArtActive)
            {
                HideStudyArtDialog(); ApplyResponsiveLayout();
                cam.orthographicSize = (boardSize + 1) / 2f + .7f;
                cam.transform.position = new Vector3((boardSize - 1) / 2f, (boardSize - 1) / 2f, -10);
            }
            aiArtWasActive = false; return;
        }
        studyLegacyPanel.SetActive(false); sceneBackgroundLayer.SetActive(false);
        boardSkin.SetToolbarVisible(false);
        if (studyBoardUi != null) studyBoardUi.SetActive(false);
        if (studyTerritoryText != null) studyTerritoryText.gameObject.SetActive(false);
        cam.rect = new Rect(0, 0, 1, 1);
        float w = studyArtCanvas.rect.width, h = studyArtCanvas.rect.height;
        if (w <= 0 || h <= 0) return;
        const float margin = 36, gap = 22;
        float panelW = Mathf.Clamp(w * .25f, 400, 480), panelH = h - 2 * margin;
        aiArtPanel.pivot = new Vector2(1, .5f);
        aiArtPanel.anchoredPosition = new Vector2(-margin, 0);
        aiArtPanel.sizeDelta = new Vector2(panelW, panelH);
        float infoH = Mathf.Clamp(panelH * .31f, 270, 320);
        PlaceAiArtSection(aiArtPlayers, 18, panelH - 18 - infoH, panelW - 36, infoH);
        aiArtPlayerAvatar.sizeDelta = aiArtComputerAvatar.sizeDelta = Vector2.one * (infoH * .40f);
        PlaceAiArtSection(aiArtRecord, 18, 160, panelW - 36, panelH - infoH - 192);
        PlaceAiArtSection(aiArtActions, 18, 25, panelW - 36, 120);
        float regionW = w - panelW - 3 * margin - gap;
        float lane = Mathf.Clamp(regionW * .12f, 105, 154);
        float bowlSize = lane * 1.36f;
        float lidSize = bowlSize * .86f;
        float lidDistance = bowlSize * .88f;
        // Board scale stays independent of decorative bowl/lid sizes.
        float boardPixels = Mathf.Min(h - 150, regionW - 2 * (lane + gap));
        float panelLeft = w - margin - panelW;
        float boardX = Mathf.Clamp(margin + regionW / 2 + 72,
            bowlSize + 14 + boardPixels / 2, panelLeft - bowlSize - 14 - boardPixels / 2);
        float boardY = h * .52f;
        float worldH = (boardSize + 1) * h / Mathf.Max(1, boardPixels);
        float worldW = worldH * cam.aspect, c = (boardSize - 1) / 2f;
        cam.orthographicSize = worldH / 2;
        cam.transform.position = new Vector3(c - (boardX / w - .5f) * worldW,
            c - (boardY / h - .5f) * worldH, -10);
        var sprite = aiArtBackground.sprite;
        if (sprite != null)
        {
            aiArtBackground.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 2);
            aiArtBackground.transform.localScale = Vector3.one * Mathf.Max(worldW / sprite.bounds.size.x, worldH / sprite.bounds.size.y);
        }
        float left = boardX - boardPixels / 2 - 2 - bowlSize / 2;
        float right = boardX + boardPixels / 2 + 2 + bowlSize / 2;
        // Each seated player keeps the bowl on their right: opponent top-left, human bottom-right.
        // Bring the lid rims almost into contact with the bowl rims, leaving room for captured stones.
        float upperCornerY = Mathf.Min(boardY + boardPixels / 2 + bowlSize * .14f, h - bowlSize / 2 - 12);
        float lowerCornerY = Mathf.Max(boardY - boardPixels / 2 - bowlSize * .14f, bowlSize / 2 + 12);
        PlaceAiArtObject(aiArtLeftBowl, left, upperCornerY, bowlSize);
        PlaceAiArtObject(aiArtLeftLid, left, upperCornerY - lidDistance, lidSize);
        PlaceAiArtObject(aiArtRightBowl, right, lowerCornerY, bowlSize);
        PlaceAiArtObject(aiArtRightLid, right, lowerCornerY + lidDistance, lidSize);
        float boardRight = boardX + boardPixels / 2;
        float toolsWidth = Mathf.Min(108, panelLeft - boardRight - 28);
        float toolsBottom = Mathf.Max(h * .5f - 72, lowerCornerY + lidDistance + lidSize / 2 + 10);
        PlaceAiArtSection(aiArtTools, (boardRight + panelLeft) / 2 - toolsWidth / 2,
            toolsBottom, toolsWidth, 144);
        studyArtDialogCard.anchorMin = studyArtDialogCard.anchorMax = new Vector2(1, .5f);
        studyArtDialogCard.pivot = new Vector2(.5f, .5f);
        studyArtDialogCard.sizeDelta = new Vector2(panelW - 24, Mathf.Min(panelH - 50, 550));
        studyArtDialogCard.anchoredPosition = new Vector2(-margin - panelW / 2, 0);
        if (!aiArtWasActive) RefreshAiArtUi();
        aiArtWasActive = true;
    }

    void PlaceAiArtSection(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
    }

    void PlaceAiArtObject(RectTransform rect, float x, float y, float size)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = Vector2.one * size;
    }

    void RefreshAiArtUi()
    {
        if (aiArtRoot == null || rules == null || !AiArtActive) return;
        bool black = humanColor == GoRules.BLACK;
        aiArtPlayerStone.sprite = black ? stoneBlack : stoneWhite;
        aiArtComputerStone.sprite = black ? stoneWhite : stoneBlack;
        aiArtLeftBowl.GetComponent<Image>().sprite = AiArtSprite(black ? "bowl-white-v1" : "bowl-black-v1");
        aiArtRightBowl.GetComponent<Image>().sprite = AiArtSprite(black ? "bowl-black-v1" : "bowl-white-v1");
        aiArtLeftLid.GetComponent<Image>().sprite = AiArtSprite("lid-white-v1");
        aiArtRightLid.GetComponent<Image>().sprite = AiArtSprite("lid-black-v1");
        aiArtKomi.text = rules.Komi.ToString("0.#") + "目";
        aiArtPlayerCaptures.text = rules.Captures[humanColor].ToString();
        aiArtComputerCaptures.text = rules.Captures[aiColor].ToString();
        for (int i = 0; i < aiArtLeftPrisoners.Count; i++)
        {
            aiArtLeftPrisoners[i].sprite = black ? stoneBlack : stoneWhite;
            aiArtRightPrisoners[i].sprite = black ? stoneWhite : stoneBlack;
            aiArtLeftPrisoners[i].gameObject.SetActive(i < rules.Captures[aiColor]);
            aiArtRightPrisoners[i].gameObject.SetActive(i < rules.Captures[humanColor]);
        }
        bool playing = rules.Result == null && rules.Phase == GoPhase.Play;
        aiArtUndo.interactable = !aiBusy && rules.LoggedMoveCount >= 2 && rules.Result == null;
        aiArtPass.interactable = playing && !aiBusy && rules.Turn == humanColor;
        aiArtDraw.interactable = playing && !aiBusy;
        aiArtResign.interactable = playing && !aiBusy;
        aiArtScore.interactable = rules.Result == null;
        RefreshAiArtClock(); RefreshAiArtRecord();
    }

    void RefreshAiArtClock()
    {
        if (!AiArtActive || aiArtPlayerTime == null || clockController == null) return;
        RenderAiArtClock(humanColor, aiArtPlayerTime, aiArtPlayerPeriods, aiArtPlayerFrame);
        RenderAiArtClock(aiColor, aiArtComputerTime, aiArtComputerPeriods, aiArtComputerFrame);
        // AI busy state can change between moves, without a rule-state refresh.
        if (rules != null)
        {
            bool play = rules.Result == null && rules.Phase == GoPhase.Play;
            aiArtUndo.interactable = !aiBusy && rules.LoggedMoveCount >= 2 && rules.Result == null;
            aiArtPass.interactable = play && !aiBusy && rules.Turn == humanColor;
            aiArtDraw.interactable = aiArtResign.interactable = play && !aiBusy;
        }
    }

    void RenderAiArtClock(int color, Text timer, Text periods, Image frame)
    {
        var state = clockController.GetSnapshot(color);
        timer.text = GoClockView.FormatSeconds(state.TimedOut ? 0 : state.InByoyomi ? state.ByoyomiSeconds : state.MainSeconds);
        periods.text = clockController.ByoyomiPeriodsTotal > 0 && !double.IsPositiveInfinity(state.MainSeconds)
            ? state.ByoyomiPeriodsLeft.ToString() : "";
        timer.color = state.TimedOut || (state.InByoyomi && state.ByoyomiSeconds <= 10)
            ? new Color(.65f, .18f, .12f) : StudyInk;
        frame.color = rules.Result == null && rules.Turn == color ? Color.white : new Color(.82f, .82f, .82f);
    }

    void RefreshAiArtRecord()
    {
        int count = rules.LoggedMoveCount, signature = boardSize;
        unchecked
        {
            for (int i = 0; i < count; i++)
            {
                rules.GetLoggedMove(i, out int x, out int y, out int color, out bool pass);
                signature = signature * 31 + x + 23 * y + 541 * color + (pass ? 17000 : 0);
            }
        }
        if (signature == aiArtRecordSignature) return;
        aiArtRecordSignature = signature;
        for (int i = aiArtContent.childCount - 1; i >= 0; i--) Destroy(aiArtContent.GetChild(i).gameObject);
        // One entry per actual move; the icon follows the logged color, including handicap openings.
        aiArtContent.sizeDelta = new Vector2(0, Mathf.Max(40, count * 36 + 8));
        for (int i = 0; i < count; i++)
        {
            rules.GetLoggedMove(i, out int x, out int y, out int color, out bool pass);
            var row = StudyArtRect(aiArtContent, "Move_" + (i + 1), new Vector2(0, 1), Vector2.one,
                new Vector2(0, -(i + 1) * 36), new Vector2(0, -i * 36));
            var entry = StudyArtRect(row, "Entry", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-120, -18), new Vector2(120, 18));
            AiArtText(entry, "Number", (i + 1).ToString("000"), 21, 0, 0, .26f, 1);
            StudyArtStone(entry, color == GoRules.BLACK, new Vector2(.39f, .5f), 26);
            var coordinate = AiArtText(entry, "Coordinate", pass ? "停一手" : ColLabel(x) + RowLabel(y),
                23, .54f, 0, 1, 1);
            coordinate.alignment = TextAnchor.MiddleLeft;
        }
        Canvas.ForceUpdateCanvases(); aiArtScroll.verticalNormalizedPosition = 0;
    }
}
