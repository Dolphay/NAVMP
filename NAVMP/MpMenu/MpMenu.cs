using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.Attributes;
using HMUI;
using IPA.Utilities.Async;
using MultiplayerCore.Beatmaps.Providers;
using SiraUtil.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MultiplayerCore.UI;
using MultiplayerCore.Beatmaps;
using Zenject;

namespace NAVMP.MpMenu;

internal class MpMenu : IInitializable, IDisposable
{
    public static string NpsDisplayResourcePath => "NAVMP.MpMenu.NpsDisplay.bsml";
    public static string MpMenuResourcePath => "NAVMP.MpMenu.MpMenu.bsml";
    public static string MpMenuTab => "NAVMP";
    private readonly GameServerLobbyFlowCoordinator _gameServerLobbyFlowCoordinator;
    private readonly LobbySetupViewController _lobbyViewController;
    private readonly ILobbyGameStateController _gameStateController;
    private readonly MpBeatmapLevelProvider _beatmapLevelProvider;
    private readonly MpPerPlayerUI _perPlayerUI;
    private BeatmapKey _currentBeatmapKey;
    private List<string>? _npsDiffList;
    private List<string>? _allowedDiffs;

    private readonly SiraLog _logger;

    public MpMenu(
        GameServerLobbyFlowCoordinator gameServerLobbyFlowCoordinator,
        MpBeatmapLevelProvider beatmapLevelProvider,
        MpPerPlayerUI perPlayerUI,
        SiraLog logger)
    {
        _gameServerLobbyFlowCoordinator = gameServerLobbyFlowCoordinator;
        _lobbyViewController = _gameServerLobbyFlowCoordinator._lobbySetupViewController;
        _gameStateController = _gameServerLobbyFlowCoordinator._lobbyGameStateController;
        _beatmapLevelProvider = beatmapLevelProvider;
        _perPlayerUI = perPlayerUI;
        _logger = logger;
    }

    // Ignore never assigned warning
#pragma warning disable 0649 // Field is never assigned to, and will always have its default value
#pragma warning disable IDE0044 // Add modifier "readonly"

    [UIComponent("npsDisplay")] private TextSegmentedControl? npsDisplay;
    [UIComponent("npsMenu")] private TextSegmentedControl? npsMenu;

#pragma warning restore IDE0044 // Add modifier "readonly"
#pragma warning restore 0649 // Field is never assigned to, and will always have its default value

    public void Initialize()
    {
        // DifficultySelector
        BSMLParser.Instance.Parse(
            Utilities.GetResourceContent(Assembly.GetExecutingAssembly(),
                NpsDisplayResourcePath), _perPlayerUI.segmentVert?.gameObject, this);
        
        GameplaySetup.Instance.AddTab(MpMenuTab, MpMenuResourcePath, this);

        // Check UI Elements
        if (npsDisplay == null)
        {
            _logger.Critical("Error could not initialize UI");
            return;
        }

        _perPlayerUI.difficultyControl?.didSelectCellEvent += SetDifficulty;

        // We register the callbacks
        _gameStateController.lobbyStateChangedEvent += SetLobbyState;
        _gameServerLobbyFlowCoordinator._multiplayerLevelSelectionFlowCoordinator.didSelectLevelEvent +=
            LocalSelectedBeatmap;
        _gameServerLobbyFlowCoordinator._serverPlayerListViewController.selectSuggestedBeatmapEvent +=
            UpdateNpsListWithBeatmapKey;
        _lobbyViewController.clearSuggestedBeatmapEvent += ClearLocalSelectedBeatmap;
    }

    public void Dispose()
    {
        GameplaySetup.Instance.RemoveTab(MpMenuTab);
    }

    private void HideCustomMetrics()
    {
        npsMenu?.SetTexts(new string[] { });
        npsDisplay?.SetTexts(new string[] { });
        npsDisplay?.gameObject.SetActive(false);
    }

    #region DiffListUpdater

    private void UpdateNpsListWithBeatmapKey(BeatmapKey beatmapKey)
    {
        _currentBeatmapKey = beatmapKey;
        if (!_currentBeatmapKey.IsValid())
        {
            HideCustomMetrics();
            return;
        }

        var levelHash = MultiplayerCore.Utilities.HashForLevelID(beatmapKey.levelId);
        if (levelHash == null)
        {
            HideCustomMetrics();
            return;
        }

        _logger.Debug($"Level is custom, trying to get beatmap for hash {levelHash}");
        _beatmapLevelProvider.GetBeatmap(levelHash).ContinueWith(levelTask =>
        {
            if (!levelTask.IsCompleted || levelTask.IsFaulted || levelTask.Result == null) return;

            var level = levelTask.Result;

            _logger.Debug(
                $"Got level {level.LevelHash}, {level.Requirements}, {level.Requirements[beatmapKey.beatmapCharacteristic.serializedName]}");

            if (level.Requirements[beatmapKey.beatmapCharacteristic.serializedName].Count == 0) return;

            if (level is BeatSaverBeatmapLevel beatSaverLevel)
            {
                UpdateNpsList(GetNpsFromBeatSaverMap(beatSaverLevel)[beatmapKey.beatmapCharacteristic.serializedName]);
            }
            else
            {
                GetNpsFromBeatSaver(levelHash);
            }
        });
    }


    private void UpdateNpsList(IReadOnlyList<(BeatmapDifficulty, double)> difficulties)
    {
        UnityMainThreadTaskScheduler.Factory.StartNew(() =>
            {
                _allowedDiffs = (from diff in difficulties
                        where _gameServerLobbyFlowCoordinator._unifiedNetworkPlayerModel.selectionMask.difficulties
                            .Contains(diff.Item1)
                        select _perPlayerUI.DiffToStr(diff.Item1)
                    ).ToList();
                _npsDiffList = (from diff in difficulties
                        where _gameServerLobbyFlowCoordinator._unifiedNetworkPlayerModel.selectionMask.difficulties
                            .Contains(diff.Item1)
                        select diff.Item2.ToString()
                    ).ToList();
                
                npsDisplay?.gameObject.SetActive(true);
                npsDisplay?.SetTexts(_npsDiffList);
                npsMenu?.SetTexts(_npsDiffList);

                if (_allowedDiffs?.Count > 1)
                {
                    int index = _allowedDiffs.IndexOf(_perPlayerUI.DiffToStr(_currentBeatmapKey.difficulty));
                    if (index > 0)
                    {
                        
                    }
                        npsMenu?.SelectCellWithNumber(index);
                        npsDisplay?.SelectCellWithNumber(index);
                }
            }
        );
    }

    #endregion

    #region Callbacks

    private void SetLobbyState(MultiplayerLobbyState lobbyState)
    {
        foreach (var cell in npsDisplay!.cells)
        {
            cell.interactable = lobbyState == MultiplayerLobbyState.LobbySetup ||
                                lobbyState == MultiplayerLobbyState.LobbyCountdown;
        }
    }

    private Dictionary<string, List<(BeatmapDifficulty, double)>> GetNpsFromBeatSaverMap(BeatSaverBeatmapLevel beatmap)
    {
        Dictionary<string, List<(BeatmapDifficulty, double)>> nps =
            new Dictionary<string, List<(BeatmapDifficulty, double)>>();
        foreach (BeatSaverSharp.Models.BeatmapDifficulty difficulty1 in beatmap._beatmap.LatestVersion.Difficulties)
        {
            string key = difficulty1.Characteristic.ToString();
            BeatSaverSharp.Models.BeatmapDifficulty.BeatSaverBeatmapDifficulty difficulty2 = difficulty1.Difficulty;
            BeatmapDifficulty beatmapDifficulty1;
            switch (difficulty2)
            {
                case BeatSaverSharp.Models.BeatmapDifficulty.BeatSaverBeatmapDifficulty.Easy:
                    beatmapDifficulty1 = BeatmapDifficulty.Easy;
                    break;
                case BeatSaverSharp.Models.BeatmapDifficulty.BeatSaverBeatmapDifficulty.Normal:
                    beatmapDifficulty1 = BeatmapDifficulty.Normal;
                    break;
                case BeatSaverSharp.Models.BeatmapDifficulty.BeatSaverBeatmapDifficulty.Hard:
                    beatmapDifficulty1 = BeatmapDifficulty.Hard;
                    break;
                case BeatSaverSharp.Models.BeatmapDifficulty.BeatSaverBeatmapDifficulty.Expert:
                    beatmapDifficulty1 = BeatmapDifficulty.Expert;
                    break;
                case BeatSaverSharp.Models.BeatmapDifficulty.BeatSaverBeatmapDifficulty.ExpertPlus:
                    beatmapDifficulty1 = BeatmapDifficulty.ExpertPlus;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Difficulty",
                        $"Unexpected difficulty value: {difficulty1.Difficulty}");
            }
            BeatmapDifficulty beatmapDifficulty2 = beatmapDifficulty1;
            if (!nps.ContainsKey(key))
                nps.Add(key, new List<(BeatmapDifficulty, double)>());
            nps[key].Add((beatmapDifficulty2, difficulty1.NPS));
        }

        return nps;
    }

    private void GetNpsFromBeatSaver(string levelHash)
    {
        _beatmapLevelProvider.GetBeatmapFromBeatSaver(levelHash).ContinueWith(levelTask =>
        {
            if (!levelTask.IsCompleted || levelTask.IsFaulted || levelTask.Result == null)
            {
                HideCustomMetrics();
            }

            if (levelTask.Result is BeatSaverBeatmapLevel beatSaverLevel)
            {
                try
                {
                    if (GetNpsFromBeatSaverMap(beatSaverLevel)[_currentBeatmapKey.beatmapCharacteristic.serializedName].Count == 0)
                    {
                        HideCustomMetrics();
                    }

                    UpdateNpsList(GetNpsFromBeatSaverMap(beatSaverLevel)[_currentBeatmapKey.beatmapCharacteristic.serializedName]);
                }
                catch (Exception e)
                {
                    _logger.Error(e);
                }
            }
        });
    }

    private void LocalSelectedBeatmap(LevelSelectionFlowCoordinator.State state)
    {
        _currentBeatmapKey = state.beatmapKey;

        var levelHash = MultiplayerCore.Utilities.HashForLevelID(_currentBeatmapKey.levelId);
        if (levelHash == null) 
        {
            HideCustomMetrics();
            return;
        }

        GetNpsFromBeatSaver(levelHash);
    }

    private void ClearLocalSelectedBeatmap()
    {
        _currentBeatmapKey = new BeatmapKey();
    }

    #endregion

    #region UIValues

    [UIAction("SetNps")]
    public void SetNps(TextSegmentedControl _, int index)
    {
        var difficultyControl = _perPlayerUI.difficultyControl;

        if (difficultyControl is not null && difficultyControl.selectedCellNumber != index)
        {
            difficultyControl.SelectCellWithNumber(index);
            _perPlayerUI.SetSelectedDifficulty(difficultyControl, index);
        }
    }


    public void SetDifficulty(SegmentedControl _, int index)
    {
        npsDisplay?.SelectCellWithNumber(index);
    }

    #endregion
}