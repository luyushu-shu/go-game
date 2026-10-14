using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class GoUiFactory : MonoBehaviour
{
    public GoUiTheme Theme { get; set; }

    const string PrimaryBackground = GoRuntimeAssetManifest.ButtonPrimaryKey;
    const string SecondaryBackground = GoRuntimeAssetManifest.ButtonSecondaryKey;
    const string DangerBackground = GoRuntimeAssetManifest.ButtonDangerKey;

    readonly List<Sprite> fallbackSprites = new List<Sprite>();
    readonly List<Texture2D> fallbackTextures = new List<Texture2D>();
    bool ownsTheme;

    public Button MakeButton(Transform parent, string objectName, string label,
        GoButtonStyle style, UnityAction onClick)
    {
        var theme = EnsureTheme();
        var go = CreateChild(parent, objectName);
        var layout = go.AddComponent<LayoutElement>();
        float height = style == GoButtonStyle.Primary ? 64f
            : style == GoButtonStyle.Icon ? 48f : 40f;
        layout.preferredHeight = height;
        layout.minHeight = height;
        layout.flexibleWidth = style == GoButtonStyle.Icon ? 0f : 1f;
        if (style == GoButtonStyle.Icon)
            layout.preferredWidth = 48f;

        var image = go.AddComponent<Image>();
        image.sprite = LoadButtonBackground(style);
        image.type = Image.Type.Sliced;
        image.color = ButtonColor(theme, style);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = ButtonColor(theme, style);
        colors.highlightedColor = HighlightColor(theme, style);
        colors.pressedColor = PressedColor(theme, style);
        colors.selectedColor = HighlightColor(theme, style);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
        colors.fadeDuration = theme.ButtonHoverSeconds;
        button.colors = colors;
        if (onClick != null)
            button.onClick.AddListener(onClick);

        var text = MakeText(go.transform, "Label", label,
            style == GoButtonStyle.Primary ? 18 : 15, LabelColor(theme, style),
            TextAnchor.MiddleCenter);
        var textFitter = text.GetComponent<ContentSizeFitter>();
        if (textFitter != null) textFitter.enabled = false;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;
        return button;
    }

    public Text MakeText(Transform parent, string objectName, string value,
        int fontSize, Color color, TextAnchor anchor)
    {
        var theme = EnsureTheme();
        var go = CreateChild(parent, objectName);
        var text = go.AddComponent<Text>();
        text.font = theme.FallbackFont;
        text.text = value ?? string.Empty;
        text.fontSize = Mathf.Max(fontSize,
            GoUiTheme.MinimumBodyFontSizeForCanvas(go.GetComponentInParent<Canvas>()));
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;
        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return text;
    }

    public GameObject MakePanel(Transform parent, string objectName, Color color, Vector2 size)
    {
        var go = CreateChild(parent, objectName);
        var image = go.AddComponent<Image>();
        image.color = color;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        return go;
    }

    public Transform MakeRow(Transform parent, string objectName, float height)
    {
        var go = CreateChild(parent, objectName);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        var row = go.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = true;
        row.childForceExpandHeight = true;
        return go.transform;
    }

    public InputField MakeInput(Transform parent, string objectName, string placeholder, int fontSize)
    {
        var theme = EnsureTheme();
        var go = CreateChild(parent, objectName);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = 40f;
        layout.minHeight = 40f;

        var image = go.AddComponent<Image>();
        image.color = theme.Ink900;

        var input = go.AddComponent<InputField>();
        input.targetGraphic = image;
        input.lineType = InputField.LineType.SingleLine;

        var text = MakeText(go.transform, "Text", string.Empty, fontSize,
            theme.Paper, TextAnchor.MiddleLeft);
        var textFitter = text.GetComponent<ContentSizeFitter>();
        if (textFitter != null) textFitter.enabled = false;
        text.supportRichText = false;
        text.raycastTarget = true;
        SetTextMargins(text.rectTransform, 10f, 4f);

        var hint = MakeText(go.transform, "Placeholder", placeholder ?? string.Empty,
            fontSize, new Color(theme.PaperDim.r, theme.PaperDim.g, theme.PaperDim.b, 0.7f),
            TextAnchor.MiddleLeft);
        var hintFitter = hint.GetComponent<ContentSizeFitter>();
        if (hintFitter != null) hintFitter.enabled = false;
        hint.supportRichText = false;
        hint.raycastTarget = false;
        SetTextMargins(hint.rectTransform, 10f, 4f);

        input.textComponent = text;
        input.placeholder = hint;
        return input;
    }

    public Slider MakeSlider(Transform parent, string objectName, float value,
        UnityAction<float> onChanged)
    {
        var theme = EnsureTheme();
        var go = CreateChild(parent, objectName);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = 24f;
        layout.minHeight = 24f;
        layout.flexibleWidth = 1f;

        var background = go.AddComponent<Image>();
        background.color = theme.Ink900;

        var fillArea = CreateChild(go.transform, "Fill Area");
        var fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRect.offsetMin = new Vector2(8f, 0f);
        fillAreaRect.offsetMax = new Vector2(-8f, 0f);

        var fill = CreateChild(fillArea.transform, "Fill");
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = theme.Gold;
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;

        var slider = go.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(value);
        slider.fillRect = fillRect;
        slider.direction = Slider.Direction.LeftToRight;
        if (onChanged != null)
            slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    public Toggle MakeToggle(Transform parent, string objectName, string label,
        bool value, UnityAction<bool> onChanged)
    {
        var theme = EnsureTheme();
        var go = CreateChild(parent, objectName);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = 40f;
        layout.minHeight = 40f;
        layout.flexibleWidth = 1f;

        var row = go.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        var box = CreateChild(go.transform, "Background");
        var boxLayout = box.AddComponent<LayoutElement>();
        boxLayout.preferredWidth = 24f;
        boxLayout.preferredHeight = 24f;
        boxLayout.minWidth = 24f;
        boxLayout.minHeight = 24f;
        var boxImage = box.AddComponent<Image>();
        boxImage.color = theme.Ink900;

        var check = CreateChild(box.transform, "Checkmark");
        var checkImage = check.AddComponent<Image>();
        checkImage.color = theme.Gold;
        var checkRect = check.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.2f, 0.2f);
        checkRect.anchorMax = new Vector2(0.8f, 0.8f);
        checkRect.offsetMin = checkRect.offsetMax = Vector2.zero;

        var text = MakeText(go.transform, "Label", label, 15, theme.Paper,
            TextAnchor.MiddleLeft);
        text.raycastTarget = false;
        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = boxImage;
        toggle.graphic = checkImage;
        toggle.isOn = value;
        if (onChanged != null)
            toggle.onValueChanged.AddListener(onChanged);
        return toggle;
    }

    public void Clear(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    void SetTextMargins(RectTransform rect, float horizontal, float vertical)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(horizontal, vertical);
        rect.offsetMax = new Vector2(-horizontal, -vertical);
    }

    GameObject CreateChild(Transform parent, string objectName)
    {
        // Every object created by this factory is a uGUI element. Creating the
        // RectTransform up front keeps container-only children (for example a
        // Slider fill area) valid before any Graphic component is attached.
        var go = new GameObject(string.IsNullOrEmpty(objectName) ? "UI" : objectName,
            typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    GoUiTheme EnsureTheme()
    {
        if (Theme != null) return Theme;
        Theme = GoUiTheme.CreateRuntimeDefault(null);
        ownsTheme = true;
        return Theme;
    }

    Sprite LoadButtonBackground(GoButtonStyle style)
    {
        string key = style == GoButtonStyle.Primary ? PrimaryBackground
            : style == GoButtonStyle.Danger ? DangerBackground : SecondaryBackground;
        if (GoRuntimeAssetManifest.Instance != null)
        {
            var manifestSprite = GoRuntimeAssetManifest.Instance.GetSprite(key);
            if (manifestSprite != null) return manifestSprite;
        }
        var sprite = Resources.Load<Sprite>(key);
        if (sprite == null && key.StartsWith("UI/Buttons/"))
            sprite = Resources.Load<Sprite>(key.Substring("UI/Buttons/".Length));
        if (sprite != null) return sprite;

        // A missing packaged asset is a release error, but keep the diagnostic
        // visible and non-blank instead of silently producing a 1x1 pixel.
        Debug.LogError("Missing runtime button sprite: " + key);
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        {
            name = key + "_RuntimeFallback",
            hideFlags = HideFlags.HideAndDontSave
        };
        Color baseColor = style == GoButtonStyle.Danger ? new Color(0.65f, 0.18f, 0.14f)
            : style == GoButtonStyle.Primary ? new Color(0.75f, 0.55f, 0.20f)
            : new Color(0.20f, 0.16f, 0.12f);
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            bool edge = x < 2 || x >= 30 || y < 2 || y >= 30;
            bool mark = x == y || x == 31 - y;
            texture.SetPixel(x, y, edge || mark ? Color.white : baseColor);
        }
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f),
            new Vector2(0.5f, 0.5f), 32f, 0, SpriteMeshType.FullRect,
            new Vector4(4f, 4f, 4f, 4f));
        sprite.name = key;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        fallbackTextures.Add(texture);
        fallbackSprites.Add(sprite);
        return sprite;
    }

    static Color ButtonColor(GoUiTheme theme, GoButtonStyle style)
    {
        switch (style)
        {
            case GoButtonStyle.Primary: return theme.Gold;
            case GoButtonStyle.Danger: return theme.Cinnabar;
            case GoButtonStyle.Icon: return theme.Ink800;
            default: return theme.Ink700;
        }
    }

    static Color LabelColor(GoUiTheme theme, GoButtonStyle style)
    {
        return style == GoButtonStyle.Primary ? theme.Ink900 : theme.Paper;
    }

    static Color HighlightColor(GoUiTheme theme, GoButtonStyle style)
    {
        var color = ButtonColor(theme, style);
        return Color.Lerp(color, Color.white, 0.12f);
    }

    static Color PressedColor(GoUiTheme theme, GoButtonStyle style)
    {
        var color = ButtonColor(theme, style);
        return Color.Lerp(color, Color.black, 0.12f);
    }

    void OnDestroy()
    {
        for (int i = 0; i < fallbackSprites.Count; i++)
        {
            if (fallbackSprites[i] != null)
            {
                if (Application.isPlaying) Destroy(fallbackSprites[i]);
                else DestroyImmediate(fallbackSprites[i]);
            }
        }
        for (int i = 0; i < fallbackTextures.Count; i++)
        {
            if (fallbackTextures[i] != null)
            {
                if (Application.isPlaying) Destroy(fallbackTextures[i]);
                else DestroyImmediate(fallbackTextures[i]);
            }
        }
        if (ownsTheme && Theme != null)
        {
            if (Application.isPlaying) Destroy(Theme);
            else DestroyImmediate(Theme);
        }
    }
}
