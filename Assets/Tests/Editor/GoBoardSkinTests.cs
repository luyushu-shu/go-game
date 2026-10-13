using NUnit.Framework;
using UnityEngine;

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
}
