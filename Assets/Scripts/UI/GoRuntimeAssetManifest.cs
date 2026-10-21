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
    public const string BoardWoodKey = "Board/board_song_kaya_v2";
    public const string BlackStoneKey = "Board/stone_black_256";
    public const string WhiteStoneKey = "Board/stone_white_256";
    public const string StoneShadowKey = "Board/stone_shadow_256";
    public const string LastMoveKey = "Board/marker_last_move_128";
    public const string AiBestKey = "Board/marker_ai_best_128";
    public const string PanelPaperKey = "UI/Panels/panel_paper_9slice_512";
    public const string PanelInkKey = "UI/Panels/panel_ink_9slice_512";
    public const string ButtonPrimaryKey = "UI/Buttons/button_primary_9slice_256";
    public const string ButtonSecondaryKey = "UI/Buttons/button_secondary_9slice_256";
    public const string ButtonDangerKey = "UI/Buttons/button_danger_9slice_256";
    public const string ButtonIconKey = "UI/Buttons/button_icon_96";
    public const string SettingsIconKey = "UI/Icons/icon_settings_64";
    public const string VolumeIconKey = "UI/Icons/icon_volume_64";
    public const string MuteIconKey = "UI/Icons/icon_mute_64";
    public const string UndoIconKey = "UI/Icons/icon_undo_64";
    public const string PassIconKey = "UI/Icons/icon_pass_64";
    public const string ScoreIconKey = "UI/Icons/icon_score_64";
    public const string ResignIconKey = "UI/Icons/icon_resign_64";
    public const string BoardMoveNumbersKey = "UI/Icons/icon_board_moves_64";
    public const string BoardCoordinatesKey = "UI/Icons/icon_board_coordinates_64";
    public const string BoardSituationKey = "UI/Icons/icon_board_situation_64";

    [SerializeField] Sprite inkRoomBackground;
    [SerializeField] Sprite mountainSilhouette;
    [SerializeField] Sprite bambooShadow;
    [SerializeField] Sprite boardWood;
    [SerializeField] Sprite blackStone;
    [SerializeField] Sprite whiteStone;
    [SerializeField] Sprite stoneShadow;
    [SerializeField] Sprite lastMove;
    [SerializeField] Sprite aiBest;
    [SerializeField] Sprite panelPaper;
    [SerializeField] Sprite panelInk;
    [SerializeField] Sprite buttonPrimary;
    [SerializeField] Sprite buttonSecondary;
    [SerializeField] Sprite buttonDanger;
    [SerializeField] Sprite buttonIcon;
    [SerializeField] Sprite settingsIcon;
    [SerializeField] Sprite volumeIcon;
    [SerializeField] Sprite muteIcon;
    [SerializeField] Sprite undoIcon;
    [SerializeField] Sprite passIcon;
    [SerializeField] Sprite scoreIcon;
    [SerializeField] Sprite resignIcon;
    [SerializeField] Sprite boardMoveNumbers;
    [SerializeField] Sprite boardCoordinates;
    [SerializeField] Sprite boardSituation;

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

    public Sprite GetSprite(string key)
    {
        switch (key)
        {
            // Refresh older scenes without requiring their serialized references to change.
            case BoardWoodKey: return Resources.Load<Sprite>(BoardWoodKey) ?? boardWood;
            case BlackStoneKey: return blackStone;
            case WhiteStoneKey: return whiteStone;
            case StoneShadowKey: return stoneShadow;
            case LastMoveKey: return lastMove;
            case AiBestKey: return aiBest;
            case PanelPaperKey: return panelPaper;
            case PanelInkKey: return panelInk;
            case ButtonPrimaryKey: return buttonPrimary;
            case ButtonSecondaryKey: return buttonSecondary;
            case ButtonDangerKey: return buttonDanger;
            case ButtonIconKey: return buttonIcon;
            case SettingsIconKey: return settingsIcon;
            case VolumeIconKey: return volumeIcon;
            case MuteIconKey: return muteIcon;
            case UndoIconKey: return undoIcon;
            case PassIconKey: return passIcon;
            case ScoreIconKey: return scoreIcon;
            case ResignIconKey: return resignIcon;
            case BoardMoveNumbersKey: return boardMoveNumbers;
            case BoardCoordinatesKey: return boardCoordinates;
            case BoardSituationKey: return boardSituation;
            default: return null;
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
            && boardWood != null
            && blackStone != null
            && whiteStone != null
            && stoneShadow != null
            && lastMove != null
            && aiBest != null
            && panelPaper != null
            && panelInk != null
            && buttonPrimary != null
            && buttonSecondary != null
            && buttonDanger != null
            && buttonIcon != null
            && settingsIcon != null
            && volumeIcon != null
            && muteIcon != null
            && undoIcon != null
            && passIcon != null
            && scoreIcon != null
            && resignIcon != null
            && boardMoveNumbers != null
            && boardCoordinates != null
            && boardSituation != null
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
        Sprite boardWood,
        Sprite blackStone,
        Sprite whiteStone,
        Sprite stoneShadow,
        Sprite lastMove,
        Sprite aiBest,
        Sprite panelPaper,
        Sprite panelInk,
        Sprite buttonPrimary,
        Sprite buttonSecondary,
        Sprite buttonDanger,
        Sprite buttonIcon,
        Sprite settingsIcon,
        Sprite volumeIcon,
        Sprite muteIcon,
        Sprite undoIcon,
        Sprite passIcon,
        Sprite scoreIcon,
        Sprite resignIcon,
        Sprite boardMoveNumbers,
        Sprite boardCoordinates,
        Sprite boardSituation,
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
        this.boardWood = boardWood;
        this.blackStone = blackStone;
        this.whiteStone = whiteStone;
        this.stoneShadow = stoneShadow;
        this.lastMove = lastMove;
        this.aiBest = aiBest;
        this.panelPaper = panelPaper;
        this.panelInk = panelInk;
        this.buttonPrimary = buttonPrimary;
        this.buttonSecondary = buttonSecondary;
        this.buttonDanger = buttonDanger;
        this.buttonIcon = buttonIcon;
        this.settingsIcon = settingsIcon;
        this.volumeIcon = volumeIcon;
        this.muteIcon = muteIcon;
        this.undoIcon = undoIcon;
        this.passIcon = passIcon;
        this.scoreIcon = scoreIcon;
        this.resignIcon = resignIcon;
        this.boardMoveNumbers = boardMoveNumbers;
        this.boardCoordinates = boardCoordinates;
        this.boardSituation = boardSituation;
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
