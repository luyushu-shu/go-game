using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GoUiNavigator : MonoBehaviour
{
    sealed class PageEntry
    {
        public GameObject Root;
        public CanvasGroup Group;
    }

    readonly Dictionary<GoUiPage, PageEntry> pages = new Dictionary<GoUiPage, PageEntry>();
    int transitionVersion;
    bool hasCurrentPage;

    public GoUiPage CurrentPage { get; private set; }
    public bool ReduceMotion { get; set; }
    public event Action<GoUiPage, GoUiPage> PageChanged;

    public void Register(GoUiPage page, GameObject root)
    {
        if (root == null) return;

        var entry = new PageEntry
        {
            Root = root,
            Group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>()
        };
        pages[page] = entry;
    }

    public bool Show(GoUiPage page)
    {
        PageEntry next;
        if (!pages.TryGetValue(page, out next) || next.Root == null)
            return false;

        if (hasCurrentPage && CurrentPage == page)
        {
            next.Root.SetActive(true);
            SetInteractive(next.Group, true);
            return true;
        }

        GoUiPage previousPage = hasCurrentPage ? CurrentPage : GoUiPage.Splash;
        PageEntry previous = null;
        if (hasCurrentPage)
            pages.TryGetValue(CurrentPage, out previous);

        if (previous != null && previous.Root == next.Root)
        {
            CurrentPage = page;
            SetInteractive(next.Group, true);
            PageChanged?.Invoke(previousPage, page);
            return true;
        }

        // Disable the old page before the new page becomes interactive.
        if (previous != null && previous.Group != null)
            SetInteractive(previous.Group, false);

        next.Root.SetActive(true);
        if (next.Group != null)
        {
            next.Group.alpha = 0f;
            SetInteractive(next.Group, false);
        }

        hasCurrentPage = true;
        CurrentPage = page;
        PageChanged?.Invoke(previousPage, page);

        int version = ++transitionVersion;
        GoUiMotion.PlayPageTransition(previous != null ? previous.Group : null,
            next.Group, ReduceMotion, () =>
            {
                if (version != transitionVersion) return;
                if (previous != null && previous.Root != null && previous.Root != next.Root)
                    previous.Root.SetActive(false);
            });
        return true;
    }

    public void ShowModal(GameObject modal)
    {
        if (modal == null) return;
        modal.SetActive(true);
        var group = modal.GetComponent<CanvasGroup>() ?? modal.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        SetInteractive(group, true);
    }

    public void HideModal(GameObject modal)
    {
        if (modal == null) return;
        var group = modal.GetComponent<CanvasGroup>();
        if (group != null) SetInteractive(group, false);
        modal.SetActive(false);
    }

    static void SetInteractive(CanvasGroup group, bool value)
    {
        if (group == null) return;
        group.interactable = value;
        group.blocksRaycasts = value;
    }
}
