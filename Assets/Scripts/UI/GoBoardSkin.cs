using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the reusable board presentation. Rules stay in GoRules; this component
/// only projects that state into board sprites, labels and optional overlays.
/// </summary>
public sealed class GoBoardSkin : MonoBehaviour
{
    public int VisibleStoneCount { get; private set; }
    public int VisibleMoveNumberCount { get; private set; }
    public bool ShowMoveNumbers => showMoveNumbers;
    public bool ShowCoordinates => showCoordinates;
    public bool ShowTerritory => showTerritory;
    public Transform BoardRoot { get; private set; }
    public SpriteRenderer[,] MarkRenderers { get; private set; }
    public SpriteRenderer GhostRenderer { get; private set; }
    public SpriteRenderer GhostMarkRenderer { get; private set; }
    public SpriteRenderer LastMoveRenderer { get; private set; }
    public Sprite BlackStoneSprite { get; private set; }
    public Sprite WhiteStoneSprite { get; private set; }
    public Sprite DeadMarkerSprite { get; private set; }
    public Sprite TerritorySprite { get; private set; }
    public Sprite LastMoveSprite { get; private set; }

    public Action<int, int> OnPointClick { get; private set; }
    public Action<bool, bool, bool> OnToolbarStateChanged { get; set; }

    bool showMoveNumbers;
    bool showCoordinates;
    bool showTerritory;
    int boardSize;
    Font numberFont;
    float cellSize = 1f;
    GameObject toolbarRoot;
    Text toolbarMoveText;
    Text toolbarCoordinatesText;
    Text toolbarTerritoryText;
    GameObject coordinatesRoot;
    Text[] colBottom, colTop, rowLeft, rowRight;
    SpriteRenderer[,] stoneRenderers;
    SpriteRenderer[,] deadRenderers;
    SpriteRenderer[,] territoryRenderers;
    TextMesh[,] moveNumberMeshes;
    Sprite boardSprite;
    Texture2D[] generatedTextures = new Texture2D[0];
    readonly List<Texture2D> ownedTextures = new List<Texture2D>();
    readonly List<Sprite> ownedSprites = new List<Sprite>();

    public void Build(int size, Transform parent, Font font, Action<int, int> onPointClick)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        Clear();
        boardSize = size;
        numberFont = font;
        OnPointClick = onPointClick;
        BoardRoot = new GameObject("BoardRoot").transform;
        BoardRoot.SetParent(parent, false);

        int cellPx = size >= 19 ? 72 : size >= 13 ? 100 : 140;
        boardSprite = LoadOrCreate(GoRuntimeAssetManifest.BoardWoodKey, MakeBoardSprite(size, cellPx));
        BlackStoneSprite = LoadOrCreate(GoRuntimeAssetManifest.BlackStoneKey, MakeStoneSprite(true));
        WhiteStoneSprite = LoadOrCreate(GoRuntimeAssetManifest.WhiteStoneKey, MakeStoneSprite(false));
        DeadMarkerSprite = LoadOrCreate(GoRuntimeAssetManifest.StoneShadowKey, MakeDeadMarkerSprite());
        LastMoveSprite = LoadOrCreate(GoRuntimeAssetManifest.LastMoveKey, MakeRingSprite());
        TerritorySprite = MakeBoxSprite();

        var board = new GameObject("Board");
        board.transform.SetParent(BoardRoot, false);
        var boardRenderer = board.AddComponent<SpriteRenderer>();
        boardRenderer.sprite = boardSprite;
        boardRenderer.sortingOrder = 0;
        board.transform.localPosition = new Vector3((size - 1) * 0.5f, (size - 1) * 0.5f, 0f);

        stoneRenderers = new SpriteRenderer[size, size];
        deadRenderers = new SpriteRenderer[size, size];
        territoryRenderers = new SpriteRenderer[size, size];
        MarkRenderers = new SpriteRenderer[size, size];
        moveNumberMeshes = new TextMesh[size, size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var point = new GameObject($"Point_{x}_{y}");
            point.transform.SetParent(BoardRoot, false);
            point.transform.localPosition = (Vector3)GridToLocal(x, y, size, cellSize);

            var stone = point.AddComponent<SpriteRenderer>();
            stone.sortingOrder = 1;
            stone.enabled = false;
            stoneRenderers[x, y] = stone;

            deadRenderers[x, y] = AddSpriteChild(point.transform, "Dead", DeadMarkerSprite, 2, 1f);
            territoryRenderers[x, y] = AddSpriteChild(point.transform, "Territory", TerritorySprite, 1, 0.5f);
            MarkRenderers[x, y] = AddSpriteChild(point.transform, "Mark", null, 5, 0.94f);
            MarkRenderers[x, y].enabled = false;

            var moveObject = new GameObject("MoveNumber");
            moveObject.transform.SetParent(point.transform, false);
            moveObject.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            var textMesh = moveObject.AddComponent<TextMesh>();
            textMesh.font = numberFont;
            textMesh.fontSize = 32;
            textMesh.characterSize = 0.095f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontStyle = FontStyle.Normal;
            textMesh.richText = false;
            var meshRenderer = moveObject.GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = 4;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            if (numberFont != null && numberFont.material != null)
                meshRenderer.sharedMaterial = numberFont.material;
            moveObject.SetActive(false);
            moveNumberMeshes[x, y] = textMesh;
        }

        GhostRenderer = AddSpriteChild(BoardRoot, "Ghost", null, 3, 0.94f);
        GhostRenderer.color = new Color(1f, 1f, 1f, 0.45f);
        GhostRenderer.enabled = false;
        GhostMarkRenderer = AddSpriteChild(BoardRoot, "GhostMark", null, 5, 0.94f);
        GhostMarkRenderer.enabled = false;
        LastMoveRenderer = AddSpriteChild(BoardRoot, "LastMove", LastMoveSprite, 3, 0.94f);
        LastMoveRenderer.enabled = false;

        BuildCoordinates();
        BuildToolbar();
        SyncToolbarLabels();
    }

    public void Refresh(GoRules rules, bool showMoveNumbers, bool showCoordinates, bool showSituation)
    {
        if (rules == null || BoardRoot == null || rules.Size != boardSize) return;
        this.showMoveNumbers = showMoveNumbers;
        this.showCoordinates = showCoordinates;
        this.showTerritory = showSituation;
        SyncToolbarLabels();
        VisibleStoneCount = 0;
        VisibleMoveNumberCount = 0;

        for (int y = 0; y < boardSize; y++)
        for (int x = 0; x < boardSize; x++)
        {
            int index = rules.Idx(x, y);
            int stone = rules.Board[index];
            var stoneRenderer = stoneRenderers[x, y];
            stoneRenderer.enabled = stone != GoRules.EMPTY;
            if (stone == GoRules.BLACK) stoneRenderer.sprite = BlackStoneSprite;
            else if (stone == GoRules.WHITE) stoneRenderer.sprite = WhiteStoneSprite;
            bool dead = rules.Dead.Contains(index);
            stoneRenderer.color = dead ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            deadRenderers[x, y].enabled = dead;
            if (stone != GoRules.EMPTY) VisibleStoneCount++;

            int move = rules.MoveAt[index];
            var moveText = moveNumberMeshes[x, y];
            bool showMove = this.showMoveNumbers && stone != GoRules.EMPTY && move > 0
                && (MarkRenderers[x, y] == null || !MarkRenderers[x, y].enabled);
            moveText.gameObject.SetActive(showMove);
            if (showMove)
            {
                moveText.text = move.ToString();
                moveText.color = stone == GoRules.BLACK ? Color.white : new Color(0.08f, 0.05f, 0.03f);
                VisibleMoveNumberCount++;
            }
        }

        if (LastMoveRenderer != null && rules.LastMove >= 0 && rules.LastMove < rules.Board.Length)
        {
            LastMoveRenderer.enabled = true;
            LastMoveRenderer.transform.localPosition = (Vector3)GridToLocal(
                rules.LastMove % boardSize, rules.LastMove / boardSize, boardSize, cellSize);
            LastMoveRenderer.color = new Color(0.843f, 0.290f, 0.227f, 1f);
        }
        else if (LastMoveRenderer != null) LastMoveRenderer.enabled = false;

        SetTerritory(rules, this.showTerritory);
        if (coordinatesRoot != null) coordinatesRoot.SetActive(this.showCoordinates);
    }

    public void SetToolbarState(bool showMoveNumbers, bool showCoordinates, bool showTerritory)
    {
        this.showMoveNumbers = showMoveNumbers;
        this.showCoordinates = showCoordinates;
        this.showTerritory = showTerritory;
        SyncToolbarLabels();
        if (coordinatesRoot != null) coordinatesRoot.SetActive(showCoordinates);
    }

    public void SetToolbarVisible(bool visible)
    {
        if (toolbarRoot != null) toolbarRoot.SetActive(visible);
        if (coordinatesRoot != null) coordinatesRoot.SetActive(visible && showCoordinates);
    }

    public void NotifyPointClick(int x, int y)
    {
        if (x >= 0 && y >= 0 && x < boardSize && y < boardSize)
            OnPointClick?.Invoke(x, y);
    }

    public void Clear()
    {
        if (BoardRoot != null)
        {
            if (Application.isPlaying) Destroy(BoardRoot.gameObject);
            else DestroyImmediate(BoardRoot.gameObject);
        }
        BoardRoot = null;
        OnPointClick = null;
        stoneRenderers = null;
        deadRenderers = null;
        territoryRenderers = null;
        moveNumberMeshes = null;
        MarkRenderers = null;
        GhostRenderer = null;
        GhostMarkRenderer = null;
        LastMoveRenderer = null;
        VisibleStoneCount = 0;
        VisibleMoveNumberCount = 0;
        toolbarRoot = null;
        coordinatesRoot = null;
        foreach (var sprite in ownedSprites) if (sprite != null) DestroyObject(sprite);
        foreach (var texture in ownedTextures) if (texture != null) DestroyObject(texture);
        ownedSprites.Clear();
        ownedTextures.Clear();
    }

    public static Vector2 GridToLocal(int x, int y, int boardSize, float cellSize)
    {
        return new Vector2(x * cellSize, y * cellSize);
    }

    SpriteRenderer AddSpriteChild(Transform parent, string name, Sprite sprite, int sortingOrder, float scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * scale;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
        renderer.enabled = false;
        return renderer;
    }

    void BuildCoordinates()
    {
        coordinatesRoot = new GameObject("Coordinates");
        coordinatesRoot.transform.SetParent(BoardRoot, false);
        var canvas = coordinatesRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 10;
        coordinatesRoot.transform.localScale = Vector3.one * 0.01f;
        coordinatesRoot.transform.localPosition = new Vector3((boardSize - 1) * 0.5f, (boardSize - 1) * 0.5f, 0f);
        var rect = coordinatesRoot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(boardSize * 100f, boardSize * 100f);
        colBottom = new Text[boardSize]; colTop = new Text[boardSize];
        rowLeft = new Text[boardSize]; rowRight = new Text[boardSize];
        for (int i = 0; i < boardSize; i++)
        {
            colBottom[i] = MakeCoordinateText(canvas.transform, i, -0.72f, ColLabel(i));
            colTop[i] = MakeCoordinateText(canvas.transform, i, boardSize - 1 + 0.72f, ColLabel(i));
            rowLeft[i] = MakeCoordinateText(canvas.transform, -0.72f, i, RowLabel(i));
            rowRight[i] = MakeCoordinateText(canvas.transform, boardSize - 1 + 0.72f, i, RowLabel(i));
        }
        coordinatesRoot.SetActive(showCoordinates);
    }

    Text MakeCoordinateText(Transform parent, float x, float y, string value)
    {
        var go = new GameObject("Coordinate");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(90f, 36f);
        rect.anchoredPosition = new Vector2((x - (boardSize - 1) * 0.5f) * 100f,
            (y - (boardSize - 1) * 0.5f) * 100f);
        var text = go.AddComponent<Text>();
        text.font = numberFont != null ? numberFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = 24;
        text.color = new Color(0.61f, 0.55f, 0.43f);
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    void BuildToolbar()
    {
        toolbarRoot = new GameObject("BoardToolbar");
        toolbarRoot.transform.SetParent(BoardRoot, false);
        toolbarRoot.transform.localPosition = new Vector3(-1.15f, (boardSize - 1) * 0.5f, -0.2f);
        toolbarRoot.transform.localScale = Vector3.one * 0.01f;
        var canvas = toolbarRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 20;
        toolbarRoot.AddComponent<GraphicRaycaster>();
        var rect = toolbarRoot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(156f, 174f);
        var layout = toolbarRoot.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        toolbarMoveText = MakeToolbarButton("MoveNumbers", "手数", GoIconCatalog.BoardMoveNumbers, () => ToggleToolbar(0));
        toolbarCoordinatesText = MakeToolbarButton("Coordinates", "坐标", GoIconCatalog.BoardCoordinates, () => ToggleToolbar(1));
        toolbarTerritoryText = MakeToolbarButton("Situation", "形势", GoIconCatalog.BoardSituation, () => ToggleToolbar(2));
        toolbarRoot.SetActive(false);
    }

    Text MakeToolbarButton(string objectName, string label, string iconKey, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(toolbarRoot.transform, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = 48f;
        layout.minHeight = 48f;
        var image = go.AddComponent<Image>();
        image.sprite = GoIconCatalog.Load(iconKey);
        image.type = Image.Type.Simple;
        image.color = new Color(0.13f, 0.10f, 0.08f, 0.94f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        var colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(1f, 0.86f, 0.56f);
        colors.pressedColor = new Color(0.76f, 0.57f, 0.25f);
        button.colors = colors;
        var labelObject = new GameObject("Label");
        labelObject.transform.SetParent(go.transform, false);
        var rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        var text = labelObject.AddComponent<Text>();
        text.font = numberFont != null ? numberFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 16;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
        text.raycastTarget = false;
        return text;
    }

    void ToggleToolbar(int index)
    {
        if (index == 0) showMoveNumbers = !showMoveNumbers;
        else if (index == 1) showCoordinates = !showCoordinates;
        else showTerritory = !showTerritory;
        SyncToolbarLabels();
        if (coordinatesRoot != null) coordinatesRoot.SetActive(showCoordinates);
        OnToolbarStateChanged?.Invoke(showMoveNumbers, showCoordinates, showTerritory);
    }

    void SyncToolbarLabels()
    {
        if (toolbarMoveText != null) toolbarMoveText.text = showMoveNumbers ? "手数：开" : "手数：关";
        if (toolbarCoordinatesText != null) toolbarCoordinatesText.text = showCoordinates ? "坐标：开" : "坐标：关";
        if (toolbarTerritoryText != null) toolbarTerritoryText.text = showTerritory ? "形势：开" : "形势：关";
    }

    void SetTerritory(GoRules rules, bool enabled)
    {
        if (!enabled)
        {
            for (int y = 0; y < boardSize; y++)
            for (int x = 0; x < boardSize; x++) territoryRenderers[x, y].enabled = false;
            return;
        }
        rules.EstimateSituation(out _, out _, out _, out _, out _, out _, out _, out _, out int[] owner);
        var black = new Color(0.08f, 0.08f, 0.08f, 0.82f);
        var white = new Color(0.97f, 0.97f, 0.97f, 0.82f);
        var blackMoyo = new Color(0.12f, 0.12f, 0.12f, 0.36f);
        var whiteMoyo = new Color(1f, 1f, 1f, 0.36f);
        for (int y = 0; y < boardSize; y++)
        for (int x = 0; x < boardSize; x++)
        {
            var renderer = territoryRenderers[x, y];
            int ownerValue = owner[rules.Idx(x, y)];
            renderer.enabled = ownerValue == GoRules.BLACK || ownerValue == GoRules.WHITE
                || ownerValue == GoRules.BLACK + 10 || ownerValue == GoRules.WHITE + 10;
            if (ownerValue == GoRules.BLACK) renderer.color = black;
            else if (ownerValue == GoRules.WHITE) renderer.color = white;
            else if (ownerValue == GoRules.BLACK + 10) renderer.color = blackMoyo;
            else renderer.color = whiteMoyo;
        }
    }

    Sprite LoadOrCreate(string resourceKey, Sprite fallback)
    {
        var sprite = GoRuntimeAssetManifest.Instance != null
            ? GoRuntimeAssetManifest.Instance.GetSprite(resourceKey) : null;
        if (sprite == null) sprite = Resources.Load<Sprite>(resourceKey);
        if (sprite == null) sprite = Resources.Load<Sprite>("Art/" + resourceKey);
        if (sprite != null)
        {
            if (fallback != null) DestroyObject(fallback);
            return sprite;
        }
        return fallback;
    }

    Sprite MakeBoardSprite(int size, int cellPx)
    {
        int resolution = cellPx * (size + 1);
        var pixels = new Color32[resolution * resolution];
        var woodA = new Color32(0xB2, 0x76, 0x3A, 0xFF);
        var woodB = new Color32(0x7A, 0x48, 0x24, 0xFF);
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float grain = Mathf.Sin(x * 0.045f + Mathf.Sin(y * 0.012f) * 2f) * 0.5f + 0.5f;
            pixels[y * resolution + x] = Color32.Lerp(woodA, woodB,
                Mathf.Clamp01(y / (float)resolution * 0.5f + grain * 0.2f));
        }
        var line = new Color32(0x42, 0x2A, 0x16, 0xE8);
        for (int i = 0; i < size; i++)
        {
            int coordinate = (i + 1) * cellPx;
            int thickness = i == 0 || i == size - 1 ? 4 : 2;
            FillRect(pixels, resolution, coordinate - thickness / 2, cellPx,
                coordinate + thickness / 2, resolution - cellPx, line);
            FillRect(pixels, resolution, cellPx, coordinate - thickness / 2,
                resolution - cellPx, coordinate + thickness / 2, line);
        }
        var star = new Color32(0x42, 0x2A, 0x16, 0xF0);
        foreach (int x in GoRules.StarPoints(size))
        foreach (int y in GoRules.StarPoints(size))
            FillCircle(pixels, resolution, (x + 1) * cellPx, (y + 1) * cellPx,
                Mathf.Max(4, cellPx / 10), star);
        return CreateSprite(pixels, resolution, resolution, cellPx);
    }

    Sprite MakeStoneSprite(bool black)
    {
        int size = 256;
        float radius = size * 0.5f - 2f;
        var pixels = new Color32[size * size];
        Color32 light = black ? new Color32(0x5A, 0x5A, 0x5A, 0xFF) : new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        Color32 dark = black ? new Color32(0x06, 0x06, 0x06, 0xFF) : new Color32(0xC2, 0xC2, 0xC2, 0xFF);
        Vector2 highlight = new Vector2(size * 0.36f, size * 0.64f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - size * 0.5f, dy = y - size * 0.5f;
            float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
            if (alpha <= 0f) { pixels[y * size + x] = new Color32(0, 0, 0, 0); continue; }
            Color32 color = Color32.Lerp(light, dark,
                Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), highlight) / (radius * 1.7f)));
            color.a = (byte)(255 * alpha);
            pixels[y * size + x] = color;
        }
        return CreateSprite(pixels, size, size, size);
    }

    Sprite MakeDeadMarkerSprite()
    {
        int size = 128;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d1 = Mathf.Abs(x - y) / 1.4142f;
            float d2 = Mathf.Abs(x + y - size) / 1.4142f;
            float alpha = Mathf.Clamp01(size * 0.05f - Mathf.Min(d1, d2));
            pixels[y * size + x] = new Color32(0xC0, 0x39, 0x2B, (byte)(230 * alpha));
        }
        return CreateSprite(pixels, size, size, size);
    }

    Sprite MakeRingSprite()
    {
        int size = 128;
        var pixels = new Color32[size * size];
        float radius = size * 0.35f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float distance = Vector2.Distance(new Vector2(x, y), new Vector2(size * 0.5f, size * 0.5f));
            float alpha = Mathf.Clamp01(size * 0.045f - Mathf.Abs(distance - radius));
            pixels[y * size + x] = new Color32(0xD7, 0x4A, 0x3A, (byte)(255 * alpha));
        }
        return CreateSprite(pixels, size, size, size);
    }

    Sprite MakeBoxSprite()
    {
        var pixels = new Color32[32 * 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        return CreateSprite(pixels, 32, 32, 32);
    }

    Sprite CreateSprite(Color32[] pixels, int width, int height, int pixelsPerUnit)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f), pixelsPerUnit);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        ownedTextures.Add(texture);
        ownedSprites.Add(sprite);
        return sprite;
    }

    static void FillRect(Color32[] pixels, int resolution, int x0, int y0, int x1, int y1, Color32 color)
    {
        x0 = Mathf.Clamp(x0, 0, resolution - 1); y0 = Mathf.Clamp(y0, 0, resolution - 1);
        x1 = Mathf.Clamp(x1, 0, resolution - 1); y1 = Mathf.Clamp(y1, 0, resolution - 1);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++) pixels[y * resolution + x] = color;
    }

    static void FillCircle(Color32[] pixels, int resolution, int cx, int cy, int radius, Color32 color)
    {
        for (int y = cy - radius; y <= cy + radius; y++)
        for (int x = cx - radius; x <= cx + radius; x++)
        {
            if (x < 0 || y < 0 || x >= resolution || y >= resolution) continue;
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                pixels[y * resolution + x] = color;
        }
    }

    static string ColLabel(int x)
    {
        const string letters = "ABCDEFGHJKLMNOPQRST";
        return x < letters.Length ? letters[x].ToString() : (x + 1).ToString();
    }

    static string RowLabel(int y) => (y + 1).ToString();

    new void DestroyObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
