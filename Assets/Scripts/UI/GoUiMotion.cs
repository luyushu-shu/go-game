using System;
using System.Collections;
using UnityEngine;

public static class GoUiMotion
{
    public const float PageTransitionSeconds = 0.220f;
    public const float PanelExpandSeconds = 0.180f;

    static MotionRunner runner;

    public static void PlayPageTransition(CanvasGroup from, CanvasGroup to,
        bool reduceMotion, Action completed)
    {
        if (reduceMotion || !Application.isPlaying)
        {
            ApplyImmediate(from, to);
            if (completed != null) completed();
            return;
        }

        if (runner == null)
        {
            var go = new GameObject("GoUiMotion");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<MotionRunner>();
        }
        runner.Play(from, to, completed);
    }

    static void ApplyImmediate(CanvasGroup from, CanvasGroup to)
    {
        if (from != null)
        {
            from.alpha = 0f;
            from.interactable = false;
            from.blocksRaycasts = false;
        }
        if (to != null)
        {
            to.alpha = 1f;
            to.interactable = true;
            to.blocksRaycasts = true;
        }
    }

    sealed class MotionRunner : MonoBehaviour
    {
        Coroutine active;

        public void Play(CanvasGroup from, CanvasGroup to, Action completed)
        {
            if (active != null) StopCoroutine(active);
            active = StartCoroutine(Fade(from, to, completed));
        }

        IEnumerator Fade(CanvasGroup from, CanvasGroup to, Action completed)
        {
            float fromAlpha = from != null ? from.alpha : 0f;
            if (from != null)
            {
                from.interactable = false;
                from.blocksRaycasts = false;
            }
            if (to != null)
            {
                to.alpha = 0f;
                to.interactable = false;
                to.blocksRaycasts = false;
            }

            float elapsed = 0f;
            while (elapsed < PageTransitionSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / PageTransitionSeconds);
                if (from != null) from.alpha = Mathf.Lerp(fromAlpha, 0f, t);
                if (to != null) to.alpha = t;
                yield return null;
            }

            ApplyImmediate(from, to);
            active = null;
            if (completed != null) completed();
        }
    }
}
