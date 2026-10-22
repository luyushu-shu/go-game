#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;

public partial class GoGame
{
    void StartAiArtPreviewIfRequested()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-localEstimatorPreview") >= 0)
        {
            Application.runInBackground = true;
            StartCoroutine(CaptureLocalEstimatorPreview());
            return;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-aiArtPreview") < 0) return;
        Application.runInBackground = true;
        StartCoroutine(CaptureAiArtPreview());
    }

    IEnumerator CaptureLocalEstimatorPreview()
    {
        yield return null;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../docs/local-estimator"));
        Directory.CreateDirectory(folder);
        inStudyMode = false; vsAI = true; reviewingKifu = false;
        humanColor = GoRules.BLACK; showMoveNums = true; showTerritory = false;
        timeControl = GoTimeControl.FromPreset(GoTimePreset.FiveMinutes, GoByoyomiPreset.ThreeByThirty);
        boardSize = 19; rules = new GoRules(19); rules.Komi = 7.5f;
        EnterGame();
        rules.Play(15, 15, out _); rules.Play(3, 15, out _);
        StartPlayClock(); Refresh(); ToggleAiArtJudgment();
        float deadline = Time.realtimeSinceStartup + 20;
        while (!rules.TryGetSituation(out _, true) && Time.realtimeSinceStartup < deadline) yield return null;
        if (!rules.TryGetSituation(out var result, true)) throw new Exception("Local estimate did not complete");
        Refresh(); UpdateClockPauseState(); yield return null;
        if (studyArtDialog.activeSelf || !clockController.IsRunning)
            throw new Exception("Judgment blocks play or clock");
        if (result.BlackTerritory != result.WhiteTerritory || result.BlackScore + 7.5f != result.WhiteScore)
            throw new Exception("Symmetric opening estimate differs");
        CaptureStudyFrame(Path.Combine(folder, "two-stones.png"));
        if (!rules.Play(15, 3, out _) || !rules.Play(3, 3, out _))
            throw new Exception("Cannot continue play with judgment active");
        Refresh(); deadline = Time.realtimeSinceStartup + 20;
        while (!rules.TryGetSituation(out _, true) && Time.realtimeSinceStartup < deadline) yield return null;
        if (!rules.TryGetSituation(out result, true)) throw new Exception("Updated estimate did not complete");
        Refresh(); yield return null;
        CaptureStudyFrame(Path.Combine(folder, "four-stones.png"));
        int markers = 0;
        foreach (var renderer in boardRoot.GetComponentsInChildren<SpriteRenderer>())
            if (renderer.name == "Territory" && renderer.enabled) markers++;
        if (markers != result.BlackTerritory + result.WhiteTerritory + result.DeadBlack + result.DeadWhite)
            throw new Exception("Rendered markers and score snapshot differ");
        if (markers == 0) throw new Exception("Local opening estimate has no visible ownership evidence");
        ShowScoreDialog();
        string expectedScore = GoResultFormatter.FormatScore(result.BlackScore, result.WhiteScore)
            .Replace("方", "").Replace(" ", "");
        if (!scoreDialog.activeSelf || scoreArtOutcome.text != expectedScore)
            throw new Exception("Score dialog disagrees with situation snapshot");
        CaptureStudyFrame(Path.Combine(folder, "score-confirm.png"));
        CancelScoreDialog();
        if (rules.Result != null) throw new Exception("Cancelling scoring ended the game");
        ToggleAiArtJudgment();
        if (aiArtJudgmentCard.gameObject.activeSelf || !aiArtRecord.gameObject.activeSelf)
            throw new Exception("Judgment toggle failed");
        File.WriteAllText(Path.Combine(folder, "validation.txt"),
            "PASS: background estimate; symmetric opening; live clock; continued legal play; updated snapshot; rendered markers="
            + markers + "; black area=" + result.BlackScore + "; white area=" + result.WhiteScore + "; toggle restores record.");
        Application.Quit(0);
    }

    IEnumerator CaptureAiArtPreview()
    {
        yield return null;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../docs/ai-ui-preview"));
        Directory.CreateDirectory(folder);
        inStudyMode = false; vsAI = true; reviewingKifu = false;
        humanColor = GoRules.BLACK; showMoveNums = true; showCoords = false;
        timeControl = GoTimeControl.FromPreset(GoTimePreset.FiveMinutes, GoByoyomiPreset.ThreeByThirty);
        boardSize = 19; rules = new GoRules(boardSize); rules.Komi = 7.5f;
        EnterGame();
        foreach (var point in new[] { new Vector2Int(3, 3), new Vector2Int(15, 15), new Vector2Int(3, 15),
            new Vector2Int(15, 3), new Vector2Int(9, 9), new Vector2Int(16, 9) })
            rules.Play(point.x, point.y, out _);
        StartPlayClock(); Refresh();
        yield return new WaitForSecondsRealtime(.8f);
        VerifyAiArtLayout();
        CaptureStudyFrame(Path.Combine(folder, "ai-match-black-19.png"));
        if (aiArtContent.childCount != 6 || aiArtPlayerPeriods.text != "3")
            throw new Exception("AI record rows or byoyomi indicator missing");
        if (aiArtRecord.Find("Columns") != null
            || aiArtContent.GetChild(0).Find("Entry/BlackStone") == null
            || aiArtContent.GetChild(1).Find("Entry/WhiteStone") == null
            || aiArtContent.GetChild(0).Find("Entry/Number").GetComponent<UnityEngine.UI.Text>().text != "001"
            || aiArtContent.GetChild(0).Find("Entry/Coordinate").GetComponent<UnityEngine.UI.Text>().text != "D4")
            throw new Exception("AI move record is not number / stone / coordinate without header");
        if (aiArtPlayerStone.sprite != stoneBlack || aiArtComputerStone.sprite != stoneWhite)
            throw new Exception("AI stone badges do not match sides");
        OpenStudyJudgment(); UpdateClockPauseState();
        if (!studyArtDialog.activeSelf || clockController.IsRunning)
            throw new Exception("AI judgment modal did not pause clock");
        yield return null;
        CaptureStudyFrame(Path.Combine(folder, "ai-judgment.png"));
        HideStudyArtDialog();
        OpenAiArtDraw();
        if (!studyArtDialog.activeSelf || studyArtDialogTitle.text != "双方和棋")
            throw new Exception("AI draw action failed");
        HideStudyArtDialog();
        aiArtResign.onClick.Invoke();
        if (!resignDialog.activeSelf) throw new Exception("AI resign action failed");
        CancelResignDialog();
        aiArtScore.onClick.Invoke();
        if (!scoreDialog.activeSelf) throw new Exception("AI score action failed");
        CancelScoreDialog();
        int moves = rules.LoggedMoveCount; aiArtUndo.onClick.Invoke();
        if (rules.LoggedMoveCount != moves - 2) throw new Exception("AI undo did not remove a pair");
        ToggleAiArtBoard(false);
        if (!boardSkin.ShowCoordinates) throw new Exception("AI coordinate toggle failed");
        ToggleAiArtBoard(true);
        if (boardSkin.ShowMoveNumbers) throw new Exception("AI move toggle failed");
        humanColor = GoRules.WHITE; Refresh();
        if (aiArtPlayerStone.sprite != stoneWhite || aiArtComputerStone.sprite != stoneBlack)
            throw new Exception("AI player-white mapping failed");
        rules.Captures[GoRules.WHITE] = 3; rules.Captures[GoRules.BLACK] = 1; Refresh();
        if (aiArtPlayerCaptures.text != "3" || aiArtComputerCaptures.text != "1"
            || !aiArtRightPrisoners[2].gameObject.activeSelf || aiArtRightPrisoners[3].gameObject.activeSelf)
            throw new Exception("AI capture or lid mapping failed");
        clockController.Tick(301); RefreshAiArtClock();
        if (!clockController.GetSnapshot(rules.Turn).InByoyomi)
            throw new Exception("AI clock did not enter byoyomi");
        yield return null;
        CaptureStudyFrame(Path.Combine(folder, "ai-match-white-19.png"));
        foreach (var size in new[] { new Vector2Int(1440, 900), new Vector2Int(1280, 720) })
        {
            Screen.SetResolution(size.x, size.y, false);
            yield return new WaitForSecondsRealtime(.5f);
            VerifyAiArtLayout();
            CaptureStudyFrame(Path.Combine(folder, "ai-match-" + size.x + "x" + size.y + ".png"));
        }
        foreach (int size in new[] { 9, 13 })
        {
            boardSize = size; rules = new GoRules(size); BuildBoard(); StartPlayClock(); Refresh();
            yield return null; VerifyAiArtLayout();
            CaptureStudyFrame(Path.Combine(folder, "ai-match-" + size + ".png"));
        }
        StopPlayClock();
        inStudyMode = true; vsAI = false; Refresh();
        yield return null;
        if (!studyArtRoot.activeSelf || aiArtRoot.activeSelf || aiArtBackground.enabled)
            throw new Exception("Study screen restoration failed");
        inStudyMode = false; ShowMainMenu();
        yield return null;
        if (aiArtRoot.activeSelf || !sceneBackgroundLayer.activeSelf)
            throw new Exception("AI menu restoration failed");
        File.WriteAllText(Path.Combine(folder, "validation.txt"),
            "PASS: AI art runtime resources; board/bowl/lid/sidebar bounds at desktop aspect ratios; 9/13/19 boards; human black/white badges and capture mapping; headerless move-number/stone/coordinate record; timer/byoyomi; modal clock pause; undo; coordinate/move toggles; judgment/draw/resign/score dialogs; study/menu restoration.");
        Application.Quit(0);
    }

    void VerifyAiArtLayout()
    {
        Canvas.ForceUpdateCanvases(); UpdateAiArtLayout();
        Vector3[] sidebar = new Vector3[4]; aiArtPanel.GetWorldCorners(sidebar);
        var board = boardRoot.Find("Board").GetComponent<SpriteRenderer>().bounds;
        var min = cam.WorldToScreenPoint(board.min); var max = cam.WorldToScreenPoint(board.max);
        if (min.x < 0 || min.y < 0 || max.y > Screen.height || max.x >= sidebar[0].x)
            throw new Exception("AI board clips or overlaps sidebar");
        foreach (var objectRect in new[] { aiArtLeftBowl, aiArtRightBowl, aiArtLeftLid, aiArtRightLid })
        {
            var corners = new Vector3[4]; objectRect.GetWorldCorners(corners);
            if (corners[0].x < 0 || corners[0].y < 0 || corners[2].y > Screen.height || corners[2].x >= sidebar[0].x)
                throw new Exception("AI bowl/lid clips or overlaps sidebar");
            if (corners[0].x < max.x && corners[2].x > min.x)
                throw new Exception("AI bowl/lid overlaps board");
        }
        if (studyLegacyPanel.activeSelf || sceneBackgroundLayer.activeSelf || !aiArtBackground.enabled)
            throw new Exception("Legacy UI or background overlaps AI art");
        var tools = new Vector3[4]; aiArtTools.GetWorldCorners(tools);
        if (tools[0].x < max.x || tools[2].x > sidebar[0].x)
            throw new Exception("AI board controls are not between board and sidebar");
        float pairDistance = Vector2.Distance(aiArtRightBowl.anchoredPosition, aiArtRightLid.anchoredPosition);
        float diameters = (aiArtRightBowl.rect.width + aiArtRightLid.rect.width) / 2;
        if (pairDistance > diameters + 2 || aiArtLeftBowl.anchoredPosition.y <= aiArtRightBowl.anchoredPosition.y)
            throw new Exception("AI bowls are not arranged as close opposing right-hand pairs");
        float bowlRadius = aiArtLeftBowl.rect.width * studyArtCanvas.GetComponentInParent<UnityEngine.Canvas>().scaleFactor / 2;
        if (aiArtLeftBowl.position.x >= min.x || aiArtLeftBowl.position.y < max.y - bowlRadius
            || aiArtRightBowl.position.x <= max.x || aiArtRightBowl.position.y > min.y + bowlRadius)
            throw new Exception("AI bowls are not close to the diagonal board corners");
        float canvasW = studyArtCanvas.rect.width, canvasH = studyArtCanvas.rect.height;
        float sideWidth = Mathf.Clamp(canvasW * .25f, 400, 480);
        float regionWidth = canvasW - sideWidth - 130;
        float originalBoardSize = Mathf.Min(canvasH - 150,
            regionWidth - 2 * (Mathf.Clamp(regionWidth * .12f, 105, 154) + 22));
        if (Mathf.Abs((max.x - min.x) / Screen.width * canvasW - originalBoardSize) > 1)
            throw new Exception("AI board was resized by decorative assets");
    }
}
#endif
