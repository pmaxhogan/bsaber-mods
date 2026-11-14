using System;
using MaxTweaks.Common;
using MaxTweaks.Common.Playback;
using MaxTweaks.Core.ObsRecording;
using Zenject;

namespace MaxTweaks.Features.ObsRecording;

/// <summary>
/// Player container: tells the app-lifetime <see cref="ObsRecordingService"/> when a level scene
/// starts and when it goes away (finish, fail, quit to menu, restart), with the song name and
/// difficulty the recording is named after. The difficulty is read again at the end because the
/// practice-mode difficulty switch can change it mid-song.
/// </summary>
internal sealed class ObsRecordingLevelHook : IInitializable, IDisposable
{
    private const string Feature = "ObsRecording";

    private readonly ObsRecordingService _service;
    private readonly GameplayCoreSceneSetupData _setupData;
    private readonly ActiveBeatmap? _activeBeatmap;
    private bool _started;

    public ObsRecordingLevelHook(
        ObsRecordingService service,
        GameplayCoreSceneSetupData setupData,
        [InjectOptional] ActiveBeatmap? activeBeatmap)
    {
        _service = service;
        _setupData = setupData;
        _activeBeatmap = activeBeatmap;
    }

    public void Initialize() => Guard.Run(Feature, () =>
    {
        _started = true;
        _service.LevelStarted(Label(_setupData.beatmapKey));
    });

    public void Dispose() => Guard.Run(Feature, () =>
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _service.LevelEnded(Label(_activeBeatmap?.Key ?? _setupData.beatmapKey));
    });

    private RecordingLabel Label(BeatmapKey key) => new(
        _setupData.beatmapLevel?.songName,
        RecordingFileName.DifficultyLabel(key.difficulty.ToString(), key.beatmapCharacteristic?.serializedName));
}
