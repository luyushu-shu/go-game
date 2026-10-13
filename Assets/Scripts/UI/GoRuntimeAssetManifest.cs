using UnityEngine;

/// <summary>
/// Serialized references to the fixed Qiyi art/audio assets.
/// The manifest lives in the startup scene so Unity includes every referenced
/// asset in a player build without relying on editor-only AssetDatabase calls
/// or a Resources folder.
/// </summary>
public sealed class GoRuntimeAssetManifest : MonoBehaviour
{
    public const string InkRoomBackgroundKey = "UI/Background/qiyi_ink_room_2048";
    public const string MountainSilhouetteKey = "UI/Background/qiyi_mountain_silhouette_2048";
    public const string BambooShadowKey = "UI/Background/qiyi_bamboo_shadow_1024";

    [SerializeField] Sprite inkRoomBackground;
    [SerializeField] Sprite mountainSilhouette;
    [SerializeField] Sprite bambooShadow;

    [SerializeField] AudioClip buttonClick;
    [SerializeField] AudioClip buttonConfirm;
    [SerializeField] AudioClip stonePlace;
    [SerializeField] AudioClip stoneCapture;
    [SerializeField] AudioClip clockWarning;
    [SerializeField] AudioClip gameEnd;
    [SerializeField] AudioClip bgmStudy;
    [SerializeField] AudioClip bgmReview;

    public static GoRuntimeAssetManifest Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    public Sprite GetBackground(string key)
    {
        switch (key)
        {
            case InkRoomBackgroundKey:
                return inkRoomBackground;
            case MountainSilhouetteKey:
                return mountainSilhouette;
            case BambooShadowKey:
                return bambooShadow;
            default:
                return null;
        }
    }

    public AudioClip GetAudio(string key)
    {
        switch (key)
        {
            case "button_click": return buttonClick;
            case "button_confirm": return buttonConfirm;
            case "stone_place": return stonePlace;
            case "stone_capture": return stoneCapture;
            case "clock_warning": return clockWarning;
            case "game_end": return gameEnd;
            case "bgm_study": return bgmStudy;
            case "bgm_review": return bgmReview;
            default: return null;
        }
    }

    public bool HasRequiredAssets()
    {
        return inkRoomBackground != null
            && mountainSilhouette != null
            && bambooShadow != null
            && buttonClick != null
            && buttonConfirm != null
            && stonePlace != null
            && stoneCapture != null
            && clockWarning != null
            && gameEnd != null
            && bgmStudy != null
            && bgmReview != null;
    }

    // Called by the editor build script when it recreates the startup scene.
    public void AssignAssets(
        Sprite inkRoomBackground,
        Sprite mountainSilhouette,
        Sprite bambooShadow,
        AudioClip buttonClick,
        AudioClip buttonConfirm,
        AudioClip stonePlace,
        AudioClip stoneCapture,
        AudioClip clockWarning,
        AudioClip gameEnd,
        AudioClip bgmStudy,
        AudioClip bgmReview)
    {
        this.inkRoomBackground = inkRoomBackground;
        this.mountainSilhouette = mountainSilhouette;
        this.bambooShadow = bambooShadow;
        this.buttonClick = buttonClick;
        this.buttonConfirm = buttonConfirm;
        this.stonePlace = stonePlace;
        this.stoneCapture = stoneCapture;
        this.clockWarning = clockWarning;
        this.gameEnd = gameEnd;
        this.bgmStudy = bgmStudy;
        this.bgmReview = bgmReview;
    }
}
