using System.Runtime.CompilerServices;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Presentation.Resources;
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

    private static readonly GpuDevice Gpu = new("NVIDIA GeForce GTX 1650", 1);

    private readonly InMemorySubtitleStore _subtitles = new();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly BlockingTranscriber _transcriber = new();
    private readonly InMemorySettingsStore _settingsStore = new();
    private readonly IModelStore _models = Substitute.For<IModelStore>();
    private readonly IAudioDecoder _decoder = Substitute.For<IAudioDecoder>();
    private readonly RecordingKeepAwake _keepAwake = new();
    private readonly TranscriptionViewModel _vm;

    public TranscriptionViewModelTests()
    {
        _models.EnsureDownloadedAsync(default!, default, default).ReturnsForAnyArgs("model.bin");
        _decoder.DecodeForSpeechAsync(default!, default)
            .ReturnsForAnyArgs(new AudioSamples(new float[AudioSamples.SpeechSampleRate * 2], AudioSamples.SpeechSampleRate));
        _vm = CreateViewModel(Gpu);
    }

    private TranscriptionViewModel CreateViewModel(GpuDevice? detectedGpu)
    {
        var detector = Substitute.For<IGpuDetector>();
        detector.FindGpuAsync(default).ReturnsForAnyArgs(detectedGpu);
        return new TranscriptionViewModel(
            new SubtitleGenerator(_models, _decoder, Substitute.For<ISpeechDetector>(), _transcriber, TimeProvider.System),
            _subtitles, _models, detector, _keepAwake, _dialogs, new AppSettingsService(_settingsStore),
            NullLogger<TranscriptionViewModel>.Instance)
        {
            TargetAudioFilePath = AudioPath,
        };
    }

    [Fact]
    public async Task Generates_on_the_detected_gpu_and_says_so_when_done()
    {
        _transcriber.Release();

        await _vm.GenerateCommand.ExecuteAsync(null);

        _transcriber.Settings!.Gpu.ShouldBe(Gpu);
        _vm.StatusText.ShouldNotBeNull().ShouldContain("generated on the GPU (NVIDIA GeForce GTX 1650)");
    }

    /// <summary>Na espera do Windows a GPU pode sumir no meio da geração: o sistema fica acordado só enquanto ela roda.</summary>
    [Fact]
    public async Task Keeps_the_system_awake_only_while_generating()
    {
        var running = _vm.GenerateCommand.ExecuteAsync(null);
        await _transcriber.Started.WaitAsync(TestContext.Current.CancellationToken);

        _keepAwake.Active.ShouldBe([KeepAwakeReason.GeneratingSubtitles]);

        _transcriber.Release();
        await running;
        _keepAwake.Active.ShouldBeEmpty();
    }

    [Fact]
    public async Task Canceling_the_generation_also_lets_the_system_sleep_again()
    {
        var running = _vm.GenerateCommand.ExecuteAsync(null);
        await _transcriber.Started.WaitAsync(TestContext.Current.CancellationToken);

        _vm.GenerateCancelCommand.Execute(null);
        await running;

        _keepAwake.Active.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_gpu_generates_on_the_cpu_and_says_so()
    {
        var vm = CreateViewModel(detectedGpu: null);
        _transcriber.Release();

        await vm.GenerateCommand.ExecuteAsync(null);

        _transcriber.Settings!.Gpu.ShouldBeNull();
        vm.StatusText.ShouldNotBeNull().ShouldContain("generated on the CPU");
    }

    /// <summary>A GPU existe mas falhou ao carregar o modelo: o transcritor usou a CPU, e a mensagem diz isso.</summary>
    [Fact]
    public async Task When_the_gpu_fails_the_message_says_cpu()
    {
        _transcriber.LoadedOn = _ => null;
        _transcriber.Release();

        await _vm.GenerateCommand.ExecuteAsync(null);

        _transcriber.Settings!.Gpu.ShouldBe(Gpu);
        _vm.StatusText.ShouldNotBeNull().ShouldContain("generated on the CPU");
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
        _dialogs.Received(1).ShowError(Arg.Any<string>(), UiText.GeneratedButNotSaved("Disk full"));
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

        _settingsStore.Stored.ModelId.ShouldBe("base-q5_1");
        _settingsStore.Stored.Language.ShouldBe("en");
    }

    /// <summary>Transcritor que espera o teste liberar, para observar o estado "em andamento".</summary>
    private sealed class BlockingTranscriber : ITranscriber
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public string Text { get; set; } = " Pronto";

        public void Release() => _release.TrySetResult();

        /// <summary>Onde o modelo "ficou": por padrão, onde foi pedido.</summary>
        public Func<TranscriberSettings, GpuDevice?> LoadedOn { get; set; } = settings => settings.Gpu;

        public TranscriberSettings? Settings { get; private set; }

        public Task<GpuDevice?> LoadModelAsync(TranscriberSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.FromResult(LoadedOn(settings));
        }

        public async IAsyncEnumerable<RecognizedSegment> TranscribeAsync(
            AudioSamples audio, TranscriberSettings settings, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            yield return new RecognizedSegment(Ms(0), Ms(1000), Text, []);
        }
    }
}
