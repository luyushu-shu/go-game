#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public partial class GoGame
{
    void StartStudyArtPreviewIfRequested()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-studyArtPreview") >= 0)
        {
            Application.runInBackground = true;
            StartCoroutine(CaptureStudyArtPreview());
        }
    }

    IEnumerator CaptureStudyArtPreview()
    {
        yield return null;
        inStudyMode = true; inMenu = false; vsAI = false; reviewingKifu = false;
        rules = new GoRules(19); boardSize = 19; showMoveNums = true;
        menuCanvas.SetActive(false);
        BuildBoard();
        foreach (var point in new[] { new Vector2Int(3,15), new Vector2Int(15,3), new Vector2Int(15,15), new Vector2Int(3,3), new Vector2Int(9,15), new Vector2Int(15,9), new Vector2Int(9,3), new Vector2Int(3,9) })
            rules.Play(point.x, point.y, out _);
        Refresh();
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../docs/study-ui-preview"));
        Directory.CreateDirectory(folder);
        yield return new WaitForSecondsRealtime(1f);
        yield return null;
        CaptureStudyFrame(Path.Combine(folder, "study-page.png"));
        VerifyStudyArtLayout();
        OpenStudyJudgment();
        yield return null;
        CaptureStudyFrame(Path.Combine(folder, "study-judgment.png"));
        if (studyArtBlackScore.text == "" || studyArtWhiteScore.text == "") throw new Exception("Missing judgment data");
        HideStudyArtDialog();
        studyArtCoordsLabel.GetComponentInParent<Button>().onClick.Invoke();
        if (!boardSkin.ShowCoordinates) throw new Exception("Coordinate toggle failed");
        studyArtMovesLabel.GetComponentInParent<Button>().onClick.Invoke();
        if (boardSkin.ShowMoveNumbers) throw new Exception("Move toggle failed");
        Undo();
        if (rules.LoggedMoveCount != 7) throw new Exception("Undo failed");
        yield return null;
        if (studyArtRecordContent.childCount != 7) throw new Exception("Record rows did not refresh");
        OpenScorePopup();
        if (!scoreDialog.activeSelf || rules.Phase != GoPhase.Play)
            throw new Exception("Score cannot open before two passes");
        CancelScoreDialog();
        if (rules.Phase != GoPhase.Play) throw new Exception("Cancel score changed play phase");
        // Exercise other supported desktop aspect ratios without touching the user's save.
        foreach (var size in new[] { new Vector2Int(1440,900), new Vector2Int(1280,720) })
        {
            Screen.SetResolution(size.x, size.y, false);
            yield return new WaitForSecondsRealtime(.5f);
            VerifyStudyArtLayout();
        }
        rules.AgreeDraw();
        if (rules.Phase != GoPhase.Over || rules.Result != "双方和棋") throw new Exception("Draw failed");
        rules = new GoRules(19);
        rules.Play(3, 3, out _);
        Refresh();
        OpenScorePopup();
        ConfirmScoreDialog();
        if (rules.Phase != GoPhase.Over || string.IsNullOrEmpty(rules.Result))
            throw new Exception("Direct scoring did not finish an active game");
        inStudyMode = false;
        ShowMainMenu(); UpdateStudyArtLayout();
        if (studyArtRoot.activeSelf || !sceneBackgroundLayer.activeSelf) throw new Exception("Menu restoration failed");
        File.WriteAllText(Path.Combine(folder, "validation.txt"), "PASS: art assets, centered stone-coordinate record rows, active-play scoring, layout at 1600x900 / 1440x900 / 1280x720, judgment, toggles, undo, draw, menu restoration.");
        Application.Quit(0);
    }

    void VerifyStudyArtLayout()
    {
        Canvas.ForceUpdateCanvases(); UpdateStudyArtLayout();
        Vector3[] bounds = new Vector3[4]; studyArtPanel.GetWorldCorners(bounds);
        var board = boardRoot.Find("Board").GetComponent<SpriteRenderer>().bounds;
        Vector3 left = cam.WorldToScreenPoint(board.min), right = cam.WorldToScreenPoint(board.max);
        if (left.x < -1 || left.y < -1 || right.y > Screen.height + 1 || right.x >= bounds[0].x)
            throw new Exception("Board clips viewport or overlaps sidebar");
        Vector3 toggleCenter = studyArtToggles.position;
        if (Mathf.Abs(toggleCenter.y - Screen.height / 2f) > 2) throw new Exception("Toggles are not centered");
        if (studyArtBackground.sprite == null || studyArtPanel.GetComponent<Image>().sprite == null)
            throw new Exception("Art failed to load");
        if (studyArtToggles.GetComponentsInChildren<Button>().Length != 2) throw new Exception("Wrong toggle count");
    }

    void CaptureStudyFrame(string path)
    {
        var main = mainCanvasLayer.GetComponent<Canvas>();
        var modal = modalCanvasLayer.GetComponent<Canvas>();
        main.renderMode = RenderMode.ScreenSpaceCamera; main.worldCamera = cam; main.planeDistance = 1f;
        modal.renderMode = RenderMode.ScreenSpaceCamera; modal.worldCamera = cam; modal.planeDistance = .8f;
        var target = new RenderTexture(Screen.width, Screen.height, 24);
        cam.targetTexture = target;
        Canvas.ForceUpdateCanvases(); cam.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
        main.renderMode = RenderMode.ScreenSpaceOverlay; modal.renderMode = RenderMode.ScreenSpaceOverlay;
        Destroy(texture); Destroy(target);
    }
}
#endif
