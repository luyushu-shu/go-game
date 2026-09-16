using UnityEditor;

// 打开工程后自动进入 Play 模式（每个编辑器会话只触发一次）。
// 不想自动运行时删除本文件即可。
[InitializeOnLoad]
static class AutoPlay
{
    static AutoPlay()
    {
        if (SessionState.GetBool("gogame_autoplay_done", false)) return;
        SessionState.SetBool("gogame_autoplay_done", true);
        EditorApplication.delayCall += TryEnter;
    }

    static void TryEnter()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryEnter;
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.EnterPlaymode();
    }
}
