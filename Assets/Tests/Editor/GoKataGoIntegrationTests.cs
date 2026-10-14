using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

// Optional live-engine coverage. The test is ignored when the user has not
// installed KataGo, while a release workstation with Tools/KataGo populated
// exercises the same GoKataGo process path used by the game.
public class GoKataGoIntegrationTests
{
    [UnityTest]
    public IEnumerator GenMoveUsesConfiguredKataGoAndReturnsAValidMove()
    {
        if (!GoKataGo.IsAvailable())
            Assert.Ignore("KataGo is not installed in Tools/KataGo.");

        var rules = new GoRules(9);
        GoMoveResult result = null;
        try
        {
            yield return GoKataGo.GenMoveAsync(
                rules,
                GoRules.BLACK,
                0,
                null,
                value => result = value);

            Assert.NotNull(result);
            Assert.IsTrue(result.success, result.error);
            if (!result.isPass && !result.isResign)
            {
                Assert.That(result.x, Is.InRange(0, rules.Size - 1));
                Assert.That(result.y, Is.InRange(0, rules.Size - 1));
            }
        }
        finally
        {
            GoKataGo.Shutdown();
        }
    }
}
