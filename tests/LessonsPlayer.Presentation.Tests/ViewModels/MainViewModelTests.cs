using System.Runtime.CompilerServices;
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
        var generator = new SubtitleGenerator(models, decoder, Substitute.For<ISpeechDetector>(), _transcriber, TimeProvider.System);

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
    public async Task Track_navigation_is_disabled_without_a_playlist()
    {
        _vm.NextTrackCommand.CanExecute(null).ShouldBeFalse();
        _vm.PreviousTrackCommand.CanExecute(null).ShouldBeFalse();

        await _vm.OpenPathsAsync([@"C:\aulas"]);

        _vm.NextTrackCommand.CanExecute(null).ShouldBeTrue();
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
    public async Task Canceling_a_track_switch_puts_the_selection_back_on_the_current_track()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _vm.Transcript.DeleteWordCommand.Execute(_vm.Transcript.Segments[0].Words[0]);
        _dialogs.AskSaveChanges(default!).ReturnsForAnyArgs(SaveChangesChoice.Cancel);
        var other = _vm.Playlist.Items[1];
        _vm.Playlist.SelectedItem = other; // o clique na lista seleciona antes do duplo clique pedir a troca

        await _vm.PlayItemCommand.ExecuteAsync(other);

        _vm.Playlist.SelectedItem.ShouldBe(_vm.Playlist.CurrentItem);
        _vm.Playlist.CurrentItem!.FilePath.ShouldBe(Aula1);
    }

    [Fact]
    public async Task Saving_confirms_in_the_status_bar()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _vm.Transcript.DeleteWordCommand.Execute(_vm.Transcript.Segments[0].Words[0]);

        await _vm.Transcript.SaveAsync(TestContext.Current.CancellationToken);

        _vm.StatusMessage.ShouldBe(Resources.UiText.Saved(_subtitles.GetSubtitlePath(Aula1)));
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

        // A mensagem do Media Foundation vem em inglês; o usuário vê o texto do app.
        _dialogs.Received(1).ShowError(Arg.Any<string>(), Arg.Is<string>(m => m.Contains(Resources.UiText.UnsupportedAudioFile)));
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
    public async Task Generated_subtitle_that_cannot_be_saved_is_shown_as_unsaved()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        await _vm.NextTrackCommand.ExecuteAsync(null); // aula2, sem legenda
        _subtitles.FailOnSave = true;
        _transcriber.TranscribeAsync(default!, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(Segments(
            new RecognizedSegment(Ms(0), Ms(800), " Nova legenda", [])));

        await _vm.Transcription.GenerateCommand.ExecuteAsync(null);

        _vm.Transcript.Segments.ShouldHaveSingleItem().Model.Text.ShouldBe("Nova legenda");
        _vm.Transcript.IsDirty.ShouldBeTrue();
        _vm.Title.ShouldStartWith("•");
    }

    /// <summary>Regressão: o ícone de legenda só era atualizado ao fim da geração, não ao salvar depois.</summary>
    [Fact]
    public async Task Saving_a_generated_subtitle_that_could_not_be_saved_flags_it_in_playlist()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        await _vm.NextTrackCommand.ExecuteAsync(null); // aula2, sem legenda
        _subtitles.FailOnSave = true;
        _transcriber.TranscribeAsync(default!, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(Segments(
            new RecognizedSegment(Ms(0), Ms(800), " Nova legenda", [])));
        await _vm.Transcription.GenerateCommand.ExecuteAsync(null);
        var item = _vm.Playlist.Items[1];
        item.HasSubtitle.ShouldBeFalse();

        _subtitles.FailOnSave = false;
        (await _vm.Transcript.SaveAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        item.HasSubtitle.ShouldBeTrue();
        _vm.DeleteSubtitleCommand.CanExecute(item).ShouldBeTrue();
    }

    [Fact]
    public async Task Folder_without_read_permission_shows_a_message_and_keeps_the_playlist()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _dialogs.PickFolder(default).ReturnsForAnyArgs(@"C:\bloqueada");
        _playlistSource.FromFolder(default!).ReturnsForAnyArgs(_ => throw new UnauthorizedAccessException(@"Access to the path 'C:\bloqueada' is denied."));

        await _vm.OpenFolderCommand.ExecuteAsync(null);

        _dialogs.Received(1).ShowError(Arg.Any<string>(), Arg.Is<string>(m => m.Contains(Resources.UiText.AccessDenied)));
        _vm.Playlist.Items.Count.ShouldBe(2);
        _player.CurrentFile.ShouldBe(Aula1);
    }

    [Fact]
    public async Task Regenerating_existing_subtitle_requires_confirmation()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]); // aula1 já tem legenda
        _dialogs.Confirm(default!, default!, default!).ReturnsForAnyArgs(false);

        await _vm.Transcription.GenerateCommand.ExecuteAsync(null);

        _transcriber.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Closing_flushes_settings_and_stops_timer()
    {
        await _vm.InitializeAsync([]);
        _timer.IsEnabled.ShouldBeTrue();

        (await _vm.ConfirmCloseAsync()).ShouldBeTrue();
        await _vm.ShutdownAsync();

        _timer.IsEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// Regressão: fechar durante uma geração só pedia o cancelamento, e o host liberava o modelo nativo com o
    /// whisper.cpp ainda calculando (o processo caía com 0xC0000005).
    /// </summary>
    [Fact]
    public async Task Shutdown_waits_for_a_running_generation_to_stop()
    {
        var ct = TestContext.Current.CancellationToken;
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        await _vm.NextTrackCommand.ExecuteAsync(null); // aula2, sem legenda
        var started = new TaskCompletionSource();
        var stopped = false;
        _transcriber.TranscribeAsync(default!, default!, ct)
            .ReturnsForAnyArgs(call => RunUntilCanceled(started, () => stopped = true, call.ArgAt<CancellationToken>(2)));
        _ = _vm.Transcription.GenerateCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(ct);

        (await _vm.ConfirmCloseAsync()).ShouldBeTrue();
        await _vm.ShutdownAsync();

        stopped.ShouldBeTrue("o transcritor precisa ter parado antes de o app liberar os recursos");
        _vm.Transcription.IsRunning.ShouldBeFalse();
        _subtitles.Saved.ShouldNotContainKey(Aula2);
    }

    [Fact]
    public async Task Deleting_the_open_subtitle_removes_the_file_the_icon_and_the_transcript()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _dialogs.Confirm(default!, default!, default!).ReturnsForAnyArgs(true);
        var item = _vm.Playlist.Items[0];

        _vm.DeleteSubtitleCommand.Execute(item);

        _subtitles.Saved.ShouldNotContainKey(Aula1);
        item.HasSubtitle.ShouldBeFalse();
        _vm.Transcript.HasTranscript.ShouldBeFalse();
        _vm.Transcript.AudioFilePath.ShouldBe(Aula1); // a faixa continua aberta, pronta para gerar outra legenda
        _vm.Transcription.GenerateCommand.CanExecute(null).ShouldBeTrue();
        _vm.StatusMessage.ShouldBe(Resources.UiText.SubtitleDeleted("aula1.mp3"));
        _vm.DeleteSubtitleCommand.CanExecute(item).ShouldBeFalse();
    }

    [Fact]
    public async Task Declining_the_confirmation_keeps_the_subtitle()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _dialogs.Confirm(default!, default!, default!).ReturnsForAnyArgs(false);

        _vm.DeleteSubtitleCommand.Execute(_vm.Playlist.Items[0]);

        _subtitles.Saved.ShouldContainKey(Aula1);
        _vm.Playlist.Items[0].HasSubtitle.ShouldBeTrue();
        _vm.Transcript.HasTranscript.ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_with_unsaved_edits_warns_and_discards_them()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _vm.Transcript.DeleteWordCommand.Execute(_vm.Transcript.Segments[0].Words[0]);
        string? message = null;
        _dialogs.Confirm(default!, default!, default!).ReturnsForAnyArgs(call =>
        {
            message = call.ArgAt<string>(1);
            return true;
        });

        _vm.DeleteSubtitleCommand.Execute(_vm.Playlist.Items[0]);

        message.ShouldBe(Resources.UiText.DeleteSubtitleMessage("aula1.mp3", _subtitles.GetSubtitlePath(Aula1), discardsChanges: true));
        _vm.Transcript.IsDirty.ShouldBeFalse();
        _vm.Title.ShouldNotStartWith("•");
        _dialogs.DidNotReceiveWithAnyArgs().AskSaveChanges(default!);
    }

    [Fact]
    public async Task Deleting_another_lessons_subtitle_keeps_the_open_transcript()
    {
        _subtitles.Saved[Aula2] = TwoSegments();
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _vm.Transcript.DeleteWordCommand.Execute(_vm.Transcript.Segments[0].Words[0]);
        string? message = null;
        _dialogs.Confirm(default!, default!, default!).ReturnsForAnyArgs(call =>
        {
            message = call.ArgAt<string>(1);
            return true;
        });

        _vm.DeleteSubtitleCommand.Execute(_vm.Playlist.Items[1]);

        message.ShouldBe(Resources.UiText.DeleteSubtitleMessage("aula2.mp3", _subtitles.GetSubtitlePath(Aula2), discardsChanges: false));
        _subtitles.Saved.ShouldNotContainKey(Aula2);
        _vm.Playlist.Items[1].HasSubtitle.ShouldBeFalse();
        _vm.Transcript.HasTranscript.ShouldBeTrue();
        _vm.Transcript.IsDirty.ShouldBeTrue("as edições da aula aberta não têm nada a ver com a outra aula");
    }

    [Fact]
    public async Task A_failed_delete_shows_the_reason_and_keeps_everything()
    {
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        _dialogs.Confirm(default!, default!, default!).ReturnsForAnyArgs(true);
        _subtitles.FailOnDelete = true;

        _vm.DeleteSubtitleCommand.Execute(_vm.Playlist.Items[0]);

        _dialogs.Received(1).ShowError(Resources.UiText.ErrorTitle, Resources.UiText.CannotDeleteSubtitle(Resources.UiText.AccessDenied));
        _vm.Playlist.Items[0].HasSubtitle.ShouldBeTrue();
        _vm.Transcript.HasTranscript.ShouldBeTrue();
        _vm.StatusMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_is_unavailable_without_a_subtitle_and_while_that_subtitle_is_being_generated()
    {
        var ct = TestContext.Current.CancellationToken;
        await _vm.OpenPathsAsync([@"C:\aulas"]);
        var (aula1, aula2) = (_vm.Playlist.Items[0], _vm.Playlist.Items[1]);
        _vm.DeleteSubtitleCommand.CanExecute(aula2).ShouldBeFalse("aula2 não tem legenda");
        _vm.DeleteSubtitleCommand.CanExecute(null).ShouldBeFalse();

        var started = new TaskCompletionSource();
        _transcriber.TranscribeAsync(default!, default!, ct)
            .ReturnsForAnyArgs(call => RunUntilCanceled(started, () => { }, call.ArgAt<CancellationToken>(2)));
        _dialogs.Confirm(default!, default!, default!).ReturnsForAnyArgs(true); // substituir a legenda atual
        var canExecuteChanges = 0;
        _vm.DeleteSubtitleCommand.CanExecuteChanged += (_, _) => canExecuteChanges++;
        var generation = _vm.Transcription.GenerateCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(ct);

        _vm.DeleteSubtitleCommand.CanExecute(aula1).ShouldBeFalse("a geração gravaria a legenda de novo ao terminar");
        canExecuteChanges.ShouldBeGreaterThan(0, "o menu precisa saber que o estado mudou");

        await _vm.NextTrackCommand.ExecuteAsync(null); // trocar de faixa não para a geração da aula1
        _vm.DeleteSubtitleCommand.CanExecute(aula1).ShouldBeFalse();

        await _vm.Transcription.CancelAndWaitAsync();
        await generation;
        _vm.DeleteSubtitleCommand.CanExecute(aula1).ShouldBeTrue();
    }

    /// <summary>Transcritor que só para quando cancelado, e ainda leva um tempo para parar (como o whisper.cpp).</summary>
    private static async IAsyncEnumerable<RecognizedSegment> RunUntilCanceled(
        TaskCompletionSource started, Action onStopped, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }
        finally
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            onStopped();
        }
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
