using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class GoBoardSkinTests
{
    [Test]
    public void GridCoordinatesUseCellSize()
    {
        var origin = GoBoardSkin.GridToLocal(0, 0, 19, 32f);
        var corner = GoBoardSkin.GridToLocal(18, 18, 19, 32f);
        Assert.AreEqual(Vector2.zero, origin);
        Assert.AreEqual(new Vector2(576f, 576f), corner);
    }

    [Test]
    public void EmptyBoardHasNoVisibleStonesOrMoveNumbers()
    {
        var root = new GameObject("BoardSkinTest");
        var skin = root.AddComponent<GoBoardSkin>();
        skin.Build(9, root.transform, null, null);
        skin.Refresh(new GoRules(9), false, false, false);
        Assert.AreEqual(0, skin.VisibleStoneCount);
        Assert.AreEqual(0, skin.VisibleMoveNumberCount);
        Object.DestroyImmediate(root);
    }

    [Test]
    public void RefreshCountsStonesAndMoveNumbersOnAllSupportedBoardSizes()
    {
        foreach (int size in new[] { 9, 13, 19 })
        {
            var root = new GameObject("BoardSkinTest_" + size);
            var skin = root.AddComponent<GoBoardSkin>();
            var rules = new GoRules(size);
            Assert.IsTrue(rules.Play(0, 0, out _));
            skin.Build(size, root.transform, null, null);
            skin.Refresh(rules, true, true, false);
            Assert.AreEqual(1, skin.VisibleStoneCount);
            Assert.AreEqual(1, skin.VisibleMoveNumberCount);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void BoardAndPointSpritesMapImportedPixelsToWorldUnitsOnAllBoardSizes()
    {
        // EditMode runs without the startup-scene manifest. The catalog must
        // report the three missing toolbar icons before using visible fallbacks.
        LogAssert.Expect(LogType.Error, "Missing runtime icon sprite: UI/Icons/icon_board_moves_64");
        LogAssert.Expect(LogType.Error, "Missing runtime icon sprite: UI/Icons/icon_board_coordinates_64");
        LogAssert.Expect(LogType.Error, "Missing runtime icon sprite: UI/Icons/icon_board_situation_64");
        foreach (int size in new[] { 9, 13, 19 })
        {
            var root = new GameObject("BoardSkinSizingTest_" + size);
            var skin = root.AddComponent<GoBoardSkin>();
            var rules = new GoRules(size);
            Assert.IsTrue(rules.Play(0, 0, out _));
            skin.Build(size, root.transform, null, null);
            skin.Refresh(rules, false, false, false);

            var boardRenderer = skin.BoardRoot.Find("Board").GetComponent<SpriteRenderer>();
            Assert.AreEqual(GoBoardSkin.BoardWorldSize(size, 1f), boardRenderer.bounds.size.x, 0.001f);
            Assert.IsTrue(skin.UsesProceduralGridAndStars);

            var point = skin.BoardRoot.Find("Point_0_0");
            Assert.AreEqual(GoBoardSkin.PointWorldSize(1f),
                point.Find("Stone").GetComponent<SpriteRenderer>().bounds.size.x, 0.001f);
            Assert.AreEqual(GoBoardSkin.PointWorldSize(1f),
                point.Find("Dead").GetComponent<SpriteRenderer>().bounds.size.x, 0.001f);
            Assert.AreEqual(GoBoardSkin.MarkerWorldSize(1f),
                skin.LastMoveRenderer.bounds.size.x, 0.001f);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ToolbarStateAndPointCallbackAreObservable()
    {
        var root = new GameObject("BoardSkinToolbarTest");
        var skin = root.AddComponent<GoBoardSkin>();
        int clickedX = -1, clickedY = -1;
        skin.Build(9, root.transform, null, (x, y) => { clickedX = x; clickedY = y; });
        skin.SetToolbarState(true, true, true);
        Assert.IsTrue(skin.ShowMoveNumbers);
        Assert.IsTrue(skin.ShowCoordinates);
        Assert.IsTrue(skin.ShowTerritory);
        skin.NotifyPointClick(2, 3);
        Assert.AreEqual(2, clickedX);
        Assert.AreEqual(3, clickedY);
        Object.DestroyImmediate(root);
    }

    [Test]
    public void ClearRemovesPointCallbackBeforeFutureNotifications()
    {
        var root = new GameObject("BoardSkinClearCallbackTest");
        var skin = root.AddComponent<GoBoardSkin>();
        int callbackCount = 0;
        skin.Build(9, root.transform, null, (x, y) => callbackCount++);
        skin.OnToolbarStateChanged = (moveNumbers, coordinates, territory) => callbackCount++;
        skin.Clear();
        skin.NotifyPointClick(2, 3);
        Assert.AreEqual(0, callbackCount);
        Assert.IsNull(skin.OnToolbarStateChanged);
        Object.DestroyImmediate(root);
    }

    [Test]
    public void LastMoveAlwaysUsesCinnabarRingAndDeadStateRemainsVisible()
    {
        var root = new GameObject("BoardSkinStateVisualTest");
        var skin = root.AddComponent<GoBoardSkin>();
        var rules = new GoRules(9);
        Assert.IsTrue(rules.Play(0, 0, out _));
        rules.Dead.Add(rules.LastMove);
        skin.Build(9, root.transform, null, null);
        skin.Refresh(rules, false, true, false);
        Assert.IsTrue(skin.LastMoveRenderer.enabled);
        Assert.AreEqual(new Color(0.843f, 0.290f, 0.227f, 1f), skin.LastMoveRenderer.color);
        Assert.IsNotNull(skin.DeadMarkerSprite);
        Object.DestroyImmediate(root);
    }
}
