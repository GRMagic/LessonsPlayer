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
}

/// <summary>Opção de idioma na lista.</summary>
public sealed record LanguageOption(string Code)
{
    public string Name => UiText.LanguageName(Code);
}

/// <summary>Argumentos de uma legenda recém-gerada (e já salva).</summary>
public sealed class TranscriptionCompletedEventArgs(string audioFilePath, Transcript transcript) : EventArgs
{
    public string AudioFilePath { get; } = audioFilePath;

    public Transcript Transcript { get; } = transcript;
}

/// <summary>Geração de legenda com Whisper: opções, progresso e cancelamento.</summary>
public sealed partial class TranscriptionViewModel : ObservableObject
{
    private readonly SubtitleGenerator _generator;
    private readonly ISubtitleStore _subtitleStore;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsService _settings;
    private readonly ILogger<TranscriptionViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseLanguage))]
    private ModelOption _selectedModel;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private bool _useGpu;

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
        IDialogService dialogs,
        AppSettingsService settings,
        ILogger<TranscriptionViewModel> logger)
    {
        _generator = generator;
        _subtitleStore = subtitleStore;
        _dialogs = dialogs;
        _settings = settings;
        _logger = logger;

        Models = [.. WhisperModels.All.Select(m => new ModelOption(m, modelStore.IsDownloaded(m)))];
        Languages = [.. TranscriptionLanguages.All.Select(code => new LanguageOption(code))];

        var current = settings.Current;
        var savedModel = WhisperModels.FindOrDefault(current.ModelId);
        _selectedModel = Models.First(o => o.Model == savedModel);
        _selectedLanguage = Languages.First(l => l.Code == TranscriptionLanguages.NormalizeOrAuto(current.Language));
        _useGpu = current.UseGpu;
    }

    /// <summary>Uma legenda foi gerada e salva (na thread da UI).</summary>
    public event EventHandler<TranscriptionCompletedEventArgs>? Completed;

    public IReadOnlyList<ModelOption> Models { get; }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public bool IsIdle => !IsRunning;

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
        if (_subtitleStore.Exists(audioPath) && !_dialogs.Confirm(UiText.OverwriteTitle, UiText.OverwriteMessage(fileName)))
        {
            return;
        }

        var options = new TranscriptionOptions(SelectedModel.Model, SelectedLanguage.Code, UseGpu);
        var progress = new Progress<TranscriptionProgress>(OnProgress); // criado na UI: os relatos voltam para ela
        var stopwatch = Stopwatch.StartNew();
        IsRunning = true;

        try
        {
            var transcript = await Task.Run(
                () => _generator.GenerateAsync(audioPath, options, progress, cancellationToken), cancellationToken);
            SelectedModel.IsDownloaded = true;
            if (transcript.WordCount == 0)
            {
                // Silêncio ou só ruído: uma legenda vazia não ajuda e apagaria a anterior, se houver.
                StatusText = UiText.NoSpeechFound(fileName);
                return;
            }

            await _subtitleStore.SaveAsync(audioPath, transcript, cancellationToken);
            StatusText = UiText.TranscriptionFinished(fileName, stopwatch.Elapsed, transcript.WordCount);
            Completed?.Invoke(this, new TranscriptionCompletedEventArgs(audioPath, transcript));
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
            IsRunning = false;
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

    private void OnProgress(TranscriptionProgress value)
    {
        IsProgressIndeterminate = value.Stage == TranscriptionStage.DecodingAudio
            || (value.Stage == TranscriptionStage.DownloadingModel && value.Fraction >= 1);
        Progress = value.Fraction;
        StatusText = UiText.Progress(value);
    }

    /// <summary>A mensagem da última geração não vale para outra faixa (a não ser que ainda esteja rodando).</summary>
    partial void OnTargetAudioFilePathChanged(string? value)
    {
        if (!IsRunning)
        {
            StatusText = null;
        }
    }

    partial void OnSelectedModelChanged(ModelOption value) =>
        _settings.Update(s => s with { ModelId = value.Model.Id }, saveNow: true);

    partial void OnSelectedLanguageChanged(LanguageOption value) =>
        _settings.Update(s => s with { Language = value.Code }, saveNow: true);

    partial void OnUseGpuChanged(bool value) =>
        _settings.Update(s => s with { UseGpu = value }, saveNow: true);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao gerar legenda para {AudioFilePath}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string audioFilePath);
}
