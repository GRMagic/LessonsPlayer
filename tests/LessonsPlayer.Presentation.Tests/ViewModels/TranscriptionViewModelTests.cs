using System.Runtime.CompilerServices;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Presentation.Services;
using LessonsPlayer.Presentation.Tests.Fakes;
using LessonsPlayer.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static LessonsPlayer.Presentation.Tests.Samples;

namespace LessonsPlayer.Presentation.Tests.ViewModels;

public sealed class TranscriptionViewModelTests
{
    private const string AudioPath = @"C:\aulas\aula1.mp3";

    private readonly InMemorySubtitleStore _subtitles = new();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly BlockingTranscriber _transcriber = new();
    private readonly InMemorySettingsStore _settingsStore = new();
    private readonly TranscriptionViewModel _vm;

    public TranscriptionViewModelTests()
    {
        var models = Substitute.For<IModelStore>();
        models.EnsureDownloadedAsync(default!, default, default).ReturnsForAnyArgs("model.bin");
        var decoder = Substitute.For<IAudioDecoder>();
        decoder.DecodeForSpeechAsync(default!, default)
            .ReturnsForAnyArgs(new AudioSamples(new float[AudioSamples.SpeechSampleRate * 2], AudioSamples.SpeechSampleRate));

        _vm = new TranscriptionViewModel(
            new SubtitleGenerator(models, decoder, Substitute.For<ISpeechDetector>(), _transcriber, TimeProvider.System),
            _subtitles, models, _dialogs, new AppSettingsService(_settingsStore),
            NullLogger<TranscriptionViewModel>.Instance)
        {
            TargetAudioFilePath = AudioPath,
        };
    }

    [Fact]
    public async Task IsRunning_is_true_while_generating_and_false_after()
    {
        var changes = new List<(string Property, bool IsRunning)>();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TranscriptionViewModel.IsRunning) or nameof(TranscriptionViewModel.IsIdle))
            {
                changes.Add((e.PropertyName!, _vm.IsRunning));
            }
        };

        var running = _vm.GenerateCommand.ExecuteAsync(null);
        await _transcriber.Started.WaitAsync(TestContext.Current.CancellationToken);

        _vm.IsRunning.ShouldBeTrue();
        _vm.IsIdle.ShouldBeFalse();
        changes.ShouldContain((nameof(TranscriptionViewModel.IsIdle), true));

        _transcriber.Release();
        await running;

        _vm.IsRunning.ShouldBeFalse();
        _vm.IsIdle.ShouldBeTrue();
        _subtitles.Saved.ShouldContainKey(AudioPath);
    }

    [Fact]
    public async Task Cancel_stops_generation_without_saving()
    {
        var running = _vm.GenerateCommand.ExecuteAsync(null);
        await _transcriber.Started.WaitAsync(TestContext.Current.CancellationToken);

        _vm.GenerateCancelCommand.Execute(null);
        await running;

        _vm.IsRunning.ShouldBeFalse();
        _subtitles.Saved.ShouldBeEmpty();
        _vm.StatusText.ShouldNotBeNull();
    }

    /// <summary>
    /// Regressão: o botão volta a aparecer quando <c>IsRunning</c> fica falso; se o comando ainda contasse
    /// como "executando" nesse instante, o botão aparecia desabilitado.
    /// </summary>
    [Fact]
    public async Task Generate_command_is_enabled_as_soon_as_generation_ends()
    {
        bool? canExecuteWhenIdle = null;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TranscriptionViewModel.IsRunning) && !_vm.IsRunning)
            {
                canExecuteWhenIdle = _vm.GenerateCommand.CanExecute(null);
            }
        };

        var running = _vm.GenerateCommand.ExecuteAsync(null);
        await _transcriber.Started.WaitAsync(TestContext.Current.CancellationToken);
        _vm.GenerateCommand.CanExecute(null).ShouldBeFalse("não pode gerar duas vezes ao mesmo tempo");

        _transcriber.Release();
        await running;

        canExecuteWhenIdle.ShouldBe(true);
    }

    [Fact]
    public async Task Result_without_speech_is_not_saved()
    {
        _transcriber.Text = " [BLANK_AUDIO]";
        var completed = false;
        _vm.Completed += (_, _) => completed = true;
        _transcriber.Release();

        await _vm.GenerateCommand.ExecuteAsync(null);

        _subtitles.Saved.ShouldBeEmpty();
        completed.ShouldBeFalse();
        _vm.StatusText.ShouldBe(Resources.UiText.NoSpeechFound("aula1.mp3"));
    }

    /// <summary>Regressão do QA: numa pasta somente leitura, minutos de transcrição eram descartados.</summary>
    [Fact]
    public async Task Result_that_cannot_be_saved_is_kept_open_as_unsaved()
    {
        _subtitles.FailOnSave = true;
        TranscriptionCompletedEventArgs? completed = null;
        _vm.Completed += (_, e) => completed = e;
        _transcriber.Release();

        await _vm.GenerateCommand.ExecuteAsync(null);

        completed.ShouldNotBeNull();
        completed.IsSaved.ShouldBeFalse();
        completed.Transcript.WordCount.ShouldBe(1);
        _dialogs.Received(1).ShowError(Arg.Any<string>(), Arg.Is<string>(m => m.StartsWith("A legenda foi gerada", StringComparison.Ordinal)));
    }

    [Fact]
    public void Language_is_locked_for_english_only_models()
    {
        _vm.CanChooseLanguage.ShouldBeTrue();

        _vm.SelectedModel = _vm.Models.Single(m => m.Model == WhisperModels.SmallEnglish);

        _vm.CanChooseLanguage.ShouldBeFalse();
    }

    [Fact]
    public async Task Status_of_a_finished_generation_is_cleared_when_the_track_changes()
    {
        _transcriber.Release();
        await _vm.GenerateCommand.ExecuteAsync(null);
        _vm.StatusText.ShouldNotBeNull();

        _vm.TargetAudioFilePath = @"C:\aulas\aula2.mp3";

        _vm.StatusText.ShouldBeNull();
    }

    [Fact]
    public void Options_are_restored_from_and_saved_to_settings()
    {
        _vm.SelectedModel = _vm.Models.Single(m => m.Model == WhisperModels.Base);
        _vm.SelectedLanguage = _vm.Languages.Single(l => l.Code == "en");
        _vm.UseGpu = false;

        _settingsStore.Stored.ModelId.ShouldBe("base-q5_1");
        _settingsStore.Stored.Language.ShouldBe("en");
        _settingsStore.Stored.UseGpu.ShouldBeFalse();
    }

    /// <summary>Transcritor que espera o teste liberar, para observar o estado "em andamento".</summary>
    private sealed class BlockingTranscriber : ITranscriber
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public string Text { get; set; } = " Pronto";

        public void Release() => _release.TrySetResult();

        public async IAsyncEnumerable<RecognizedSegment> TranscribeAsync(
            AudioSamples audio, TranscriberSettings settings, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            yield return new RecognizedSegment(Ms(0), Ms(1000), Text, []);
        }
    }
}
