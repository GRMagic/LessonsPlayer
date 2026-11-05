using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Playlist;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Presentation.Services;
using LessonsPlayer.Presentation.Tests.Fakes;
using LessonsPlayer.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static LessonsPlayer.Presentation.Tests.Samples;

namespace LessonsPlayer.Presentation.Tests.ViewModels;

public sealed class MainViewModelTests
{
    private const string Aula1 = @"C:\aulas\aula1.mp3";
    private const string Aula2 = @"C:\aulas\aula2.mp3";

    private readonly FakeAudioPlayer _player = new();
    private readonly InMemorySubtitleStore _subtitles = new();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IPlaylistSource _playlistSource = Substitute.For<IPlaylistSource>();
    private readonly ITranscriber _transcriber = Substitute.For<ITranscriber>();
    private readonly ManualFrameTimer _timer = new();
    private readonly MainViewModel _vm;

    public MainViewModelTests()
    {
        _playlistSource.FromPaths(default!).ReturnsForAnyArgs([new PlaylistEntry(Aula1), new PlaylistEntry(Aula2)]);
        _subtitles.Saved[Aula1] = TwoSegments();

        var settings = new AppSettingsService(new InMemorySettingsStore());
        var models = Substitute.For<IModelStore>();
        models.EnsureDownloadedAsync(default!, default, default).ReturnsForAnyArgs("model.bin");
        var decoder = Substitute.For<IAudioDecoder>();
        decoder.DecodeForSpeechAsync(default!, default)
            .ReturnsForAnyArgs(new AudioSamples(new float[AudioSamples.SpeechSampleRate], AudioSamples.SpeechSampleRate));
        var generator = new SubtitleGenerator(models, decoder, _transcriber, TimeProvider.System);

        var dispatcher = new ImmediateDispatcher();
        _vm = new MainViewModel(
            new PlayerViewModel(_player, dispatcher, settings),
            new PlaylistViewModel(_playlistSource, _subtitles),
            new TranscriptViewModel(_subtitles, _dialogs, NullLogger<TranscriptViewModel>.Instance),
            new TranscriptionViewModel(generator, _subtitles, models, _dialogs, settings, NullLogger<TranscriptionViewModel>.Instance),
            _dialogs,
            _subtitles,
            settings,
            _timer,
            NullLogger<MainViewModel>.Instance);
    }

    [Fact]
    public async Task OpenPaths_loads_playlist_first_track_and_its_subtitle()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);

        _vm.Playlist.Items.Count.ShouldBe(2);
        _vm.Playlist.CurrentItem!.FilePath.ShouldBe(Aula1);
        _vm.Playlist.Items[0].HasSubtitle.ShouldBeTrue();
        _player.CurrentFile.ShouldBe(Aula1);
        _player.State.ShouldBe(PlaybackState.Stopped); // abrir não toca sozinho
        _vm.Transcript.HasTranscript.ShouldBeTrue();
        _vm.Transcription.TargetAudioFilePath.ShouldBe(Aula1);
        _vm.Title.ShouldContain("aula1.mp3");
    }

    [Fact]
    public async Task Frame_tick_updates_karaoke_from_player_position()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _player.Position = Ms(2100);

        _timer.Fire();

        _vm.Transcript.ActiveSegment.ShouldBe(_vm.Transcript.Segments[1]);
        _vm.Player.PositionSeconds.ShouldBe(2.1);
    }

    [Fact]
    public async Task Clicking_a_word_seeks_the_player()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);

        _vm.Transcript.ClickWordCommand.Execute(_vm.Transcript.Segments[1].Words[0]);

        _player.Seeks.ShouldContain(Ms(1500));
    }

    [Fact]
    public async Task Switching_track_with_unsaved_edits_and_cancel_stays_on_track()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _vm.Transcript.DeleteWordCommand.Execute(_vm.Transcript.Segments[0].Words[0]);
        _dialogs.AskSaveChanges(default!).ReturnsForAnyArgs(SaveChangesChoice.Cancel);

        await _vm.NextTrackCommand.ExecuteAsync(null);

        _player.CurrentFile.ShouldBe(Aula1);
        _vm.Transcript.IsDirty.ShouldBeTrue();
        _vm.Title.ShouldStartWith("•");
    }

    [Fact]
    public async Task Switching_track_with_unsaved_edits_and_save_persists_then_switches()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _vm.Transcript.DeleteWordCommand.Execute(_vm.Transcript.Segments[0].Words[0]);
        _dialogs.AskSaveChanges(default!).ReturnsForAnyArgs(SaveChangesChoice.Save);

        await _vm.NextTrackCommand.ExecuteAsync(null);

        _subtitles.Saved[Aula1].Segments[0].Text.ShouldBe("pessoal");
        _player.CurrentFile.ShouldBe(Aula2);
        _player.State.ShouldBe(PlaybackState.Playing);
        _vm.Transcript.HasTranscript.ShouldBeFalse();
    }

    [Fact]
    public async Task End_of_track_plays_the_next_one()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);

        _player.SimulateCompleted();

        _player.CurrentFile.ShouldBe(Aula2);
        _player.State.ShouldBe(PlaybackState.Playing);
    }

    [Fact]
    public async Task Unsupported_file_shows_error_and_clears_current()
    {
        _player.UnsupportedFiles.Add(Aula1);

        await _vm.OpenPathsAsync([@"C:\aulas"]);

        _dialogs.ReceivedWithAnyArgs(1).ShowError(default!, default!);
        _vm.Playlist.CurrentItem.ShouldBeNull();
        _vm.Transcription.TargetAudioFilePath.ShouldBeNull();
    }

    [Fact]
    public async Task Generated_subtitle_is_saved_shown_and_flagged_in_playlist()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        await _vm.NextTrackCommand.ExecuteAsync(null); // aula2, sem legenda
        _transcriber.TranscribeAsync(default!, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(Segments(
            new RecognizedSegment(Ms(0), Ms(800), " Nova legenda", [])));

        await _vm.Transcription.GenerateCommand.ExecuteAsync(null);

        _subtitles.Saved.ShouldContainKey(Aula2);
        _vm.Transcript.Segments.ShouldHaveSingleItem().Model.Text.ShouldBe("Nova legenda");
        _vm.Playlist.Items[1].HasSubtitle.ShouldBeTrue();
    }

    [Fact]
    public async Task Regenerating_existing_subtitle_requires_confirmation()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]); // aula1 já tem legenda
        _dialogs.Confirm(default!, default!).ReturnsForAnyArgs(false);

        await _vm.Transcription.GenerateCommand.ExecuteAsync(null);

        _transcriber.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PrepareToClose_flushes_settings_and_stops_timer()
    {
        await _vm.InitializeAsync([]);
        _timer.IsEnabled.ShouldBeTrue();

        (await _vm.PrepareToCloseAsync()).ShouldBeTrue();

        _timer.IsEnabled.ShouldBeFalse();
    }

    private static async IAsyncEnumerable<RecognizedSegment> Segments(params RecognizedSegment[] segments)
    {
        foreach (var segment in segments)
        {
            await Task.Yield();
            yield return segment;
        }
    }
}
