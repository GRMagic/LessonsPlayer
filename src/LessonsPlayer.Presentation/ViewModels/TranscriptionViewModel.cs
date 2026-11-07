using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Subtitles;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Presentation.Resources;
using LessonsPlayer.Presentation.Services;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.Presentation.ViewModels;

/// <summary>Opção de modelo na lista.</summary>
public sealed partial class ModelOption(WhisperModel model, bool isDownloaded) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private bool _isDownloaded = isDownloaded;

    public WhisperModel Model { get; } = model;

    public string Name => UiText.ModelName(Model);

    public string SizeText => UiText.ModelSize(Model, IsDownloaded);

    /// <summary>Também é o nome lido pelos leitores de tela (antes, o nome da classe).</summary>
    public override string ToString() => Name;
}

/// <summary>Opção de idioma na lista.</summary>
public sealed record LanguageOption(string Code)
{
    public string Name => UiText.LanguageName(Code);

    /// <summary>Também é o nome lido pelos leitores de tela.</summary>
    public override string ToString() => Name;
}

/// <summary>Argumentos de uma legenda recém-gerada (e já salva).</summary>
public sealed class TranscriptionCompletedEventArgs(string audioFilePath, Transcript transcript, bool isSaved = true) : EventArgs
{
    public string AudioFilePath { get; } = audioFilePath;

    public Transcript Transcript { get; } = transcript;

    /// <summary><c>false</c> se não foi possível gravar a legenda (ex.: pasta sem permissão de escrita).</summary>
    public bool IsSaved { get; } = isSaved;
}

/// <summary>Geração de legenda com Whisper: opções, progresso e cancelamento.</summary>
public sealed partial class TranscriptionViewModel : ObservableObject
{
    private readonly SubtitleGenerator _generator;
    private readonly ISubtitleStore _subtitleStore;
    private readonly IModelStore _modelStore;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsService _settings;
    private readonly ILogger<TranscriptionViewModel> _logger;

    /// <summary>A GPU detectada ao abrir o programa, ou <c>null</c> se não houver (a transcrição usa a CPU).</summary>
    private readonly Task<GpuDevice?> _gpu;

    private readonly IKeepAwake _keepAwake;

    /// <summary>Pedido para o sistema não dormir durante a geração (na espera, a GPU pode sumir no meio dela).</summary>
    private IDisposable? _generatingRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseLanguage))]
    private ModelOption _selectedModel;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string? _targetAudioFilePath;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string? _statusText;

    /// <summary>
    /// Há uma geração em andamento. É um estado próprio, e não <c>GenerateCommand.IsRunning</c>: o comando só
    /// passa a "rodando" depois que o método retorna a Task (tarde demais para notificar a UI no início) e
    /// só deixa de rodar depois do <c>finally</c>, quando o botão já voltou a aparecer. Por isso o comando
    /// permite execuções concorrentes e quem impede duas gerações é este estado (<see cref="CanGenerate"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanChooseLanguage))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _isRunning;

    public TranscriptionViewModel(
        SubtitleGenerator generator,
        ISubtitleStore subtitleStore,
        IModelStore modelStore,
        IGpuDetector gpuDetector,
        IKeepAwake keepAwake,
        IDialogService dialogs,
        AppSettingsService settings,
        ILogger<TranscriptionViewModel> logger)
    {
        _keepAwake = keepAwake;
        // Começa já, em segundo plano (~1 s carregando os drivers Vulkan): quando o usuário pedir a primeira
        // legenda, a resposta quase sempre já chegou.
        _gpu = gpuDetector.FindGpuAsync(CancellationToken.None);
        _generator = generator;
        _subtitleStore = subtitleStore;
        _modelStore = modelStore;
        _dialogs = dialogs;
        _settings = settings;
        _logger = logger;

        Models = [.. WhisperModels.All.Select(m => new ModelOption(m, modelStore.IsDownloaded(m)))];
        Languages = [.. TranscriptionLanguages.All.Select(code => new LanguageOption(code))];

        var current = settings.Current;
        var savedModel = WhisperModels.FindOrDefault(current.ModelId);
        _selectedModel = Models.First(o => o.Model == savedModel);
        _selectedLanguage = Languages.First(l => l.Code == TranscriptionLanguages.NormalizeOrAuto(current.Language));
    }

    /// <summary>Uma legenda foi gerada e salva (na thread da UI).</summary>
    public event EventHandler<TranscriptionCompletedEventArgs>? Completed;

    public IReadOnlyList<ModelOption> Models { get; }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public bool IsIdle => !IsRunning;

    /// <summary>
    /// O áudio da geração em andamento, ou <c>null</c>. Pode ser outro que não o <see cref="TargetAudioFilePath"/>:
    /// a geração continua quando o usuário troca de faixa.
    /// </summary>
    public string? RunningAudioFilePath { get; private set; }

    /// <summary>Modelos .en só transcrevem inglês: o idioma escolhido não se aplica a eles.</summary>
    public bool CanChooseLanguage => IsIdle && !SelectedModel.Model.EnglishOnly;

    private bool CanGenerate => TargetAudioFilePath is not null && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanGenerate), IncludeCancelCommand = true, AllowConcurrentExecutions = true)]
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        if (TargetAudioFilePath is not { } audioPath)
        {
            return;
        }

        var fileName = Path.GetFileName(audioPath);
        if (_subtitleStore.Exists(audioPath) && !_dialogs.Confirm(UiText.OverwriteTitle, UiText.OverwriteMessage(fileName), UiText.Replace))
        {
            return;
        }

        // Criado na UI: os relatos voltam para ela. O rastreador guarda na hora onde a transcrição rodou, para a
        // mensagem final não depender de quando os relatos postados chegam.
        var progress = new DeviceTrackingProgress(new Progress<TranscriptionProgress>(OnProgress));
        var stopwatch = Stopwatch.StartNew();
        RunningAudioFilePath = audioPath; // antes de IsRunning: quem observa IsRunning já encontra o caminho
        IsRunning = true;

        try
        {
            // Sempre a GPU, se houver; o transcritor cai para a CPU se ela falhar, e o progresso diz onde ficou.
            var options = new TranscriptionOptions(SelectedModel.Model, SelectedLanguage.Code, await _gpu);
            var transcript = await Task.Run(
                () => _generator.GenerateAsync(audioPath, options, progress, cancellationToken), cancellationToken);
            SelectedModel.IsDownloaded = true;
            if (transcript.WordCount == 0)
            {
                // Silêncio ou só ruído: uma legenda vazia não ajuda e apagaria a anterior, se houver.
                StatusText = UiText.NoSpeechFound(fileName);
                return;
            }

            var isSaved = await TrySaveAsync(audioPath, transcript, cancellationToken);
            StatusText = UiText.TranscriptionFinished(fileName, stopwatch.Elapsed, transcript.WordCount, progress.Gpu);
            Completed?.Invoke(this, new TranscriptionCompletedEventArgs(audioPath, transcript, isSaved));
        }
        catch (OperationCanceledException)
        {
            StatusText = UiText.TranscriptionCanceled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Falhas esperadas aqui: rede, disco, formato de áudio, runtime nativo do Whisper.
            LogFailed(_logger, ex, audioPath);
            StatusText = null;
            _dialogs.ShowError(UiText.ErrorTitle, UiText.TranscriptionFailed(UiText.ErrorReason(ex)));
        }
        finally
        {
            Progress = 0;
            IsProgressIndeterminate = false;
            RunningAudioFilePath = null;
            IsRunning = false;
        }
    }

    /// <summary>
    /// Grava a legenda gerada. Se não der (pasta somente leitura, disco cheio), avisa e retorna <c>false</c>:
    /// o resultado continua aberto em vez de ser descartado depois de minutos de transcrição.
    /// </summary>
    private async Task<bool> TrySaveAsync(string audioPath, Transcript transcript, CancellationToken cancellationToken)
    {
        try
        {
            await _subtitleStore.SaveAsync(audioPath, transcript, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(_logger, ex, audioPath);
            _dialogs.ShowError(UiText.ErrorTitle, UiText.GeneratedButNotSaved(UiText.ErrorReason(ex)));
            return false;
        }
    }

    /// <summary>
    /// Cancela a geração em andamento e espera ela terminar de fato (ao fechar o app, o modelo nativo só pode
    /// ser liberado depois disso).
    /// </summary>
    public async Task CancelAndWaitAsync()
    {
        if (GenerateCommand.ExecutionTask is not { IsCompleted: false } running)
        {
            return;
        }

        GenerateCancelCommand.Execute(null);
        await running; // GenerateAsync trata o cancelamento e as falhas; não lança
    }

    /// <summary>Repassa o progresso e guarda, no momento do relato, a GPU da transcrição (<c>null</c> = CPU).</summary>
    private sealed class DeviceTrackingProgress(IProgress<TranscriptionProgress> inner) : IProgress<TranscriptionProgress>
    {
        private GpuDevice? _gpu;

        public GpuDevice? Gpu => Volatile.Read(ref _gpu);

        public void Report(TranscriptionProgress value)
        {
            if (value.Stage == TranscriptionStage.Transcribing)
            {
                Volatile.Write(ref _gpu, value.Gpu);
            }

            inner.Report(value);
        }
    }

    private void OnProgress(TranscriptionProgress value)
    {
        IsProgressIndeterminate = value.Stage == TranscriptionStage.DecodingAudio
            || (value.Stage == TranscriptionStage.DownloadingModel && value.Fraction >= 1);
        Progress = value.Fraction;
        StatusText = UiText.Progress(value);
    }

    partial void OnIsRunningChanged(bool value)
    {
        if (value)
        {
            _generatingRequest ??= _keepAwake.Acquire(KeepAwakeReason.GeneratingSubtitles);
        }
        else
        {
            _generatingRequest?.Dispose();
            _generatingRequest = null;
        }
    }

    /// <summary>
    /// A mensagem da última geração não vale para outra faixa (a não ser que ainda esteja rodando). Aproveita
    /// para atualizar o "baixado" dos modelos, caso algum arquivo tenha sido apagado por fora.
    /// </summary>
    partial void OnTargetAudioFilePathChanged(string? value)
    {
        if (!IsRunning)
        {
            StatusText = null;
            foreach (var option in Models)
            {
                option.IsDownloaded = _modelStore.IsDownloaded(option.Model);
            }
        }
    }

    partial void OnSelectedModelChanged(ModelOption value) =>
        _settings.Update(s => s with { ModelId = value.Model.Id }, saveNow: true);

    partial void OnSelectedLanguageChanged(LanguageOption value) =>
        _settings.Update(s => s with { Language = value.Code }, saveNow: true);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao gerar legenda para {AudioFilePath}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string audioFilePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Legenda gerada, mas não salva, para {AudioFilePath}")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string audioFilePath);
}
