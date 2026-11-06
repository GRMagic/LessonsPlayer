using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Presentation.Resources;
using LessonsPlayer.Presentation.Services;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.Presentation.ViewModels;

/// <summary>
/// Orquestra a janela principal: abrir pasta/arquivo, trocar de faixa (protegendo edições não salvas),
/// carregar a legenda salva e ligar o player ao karaokê a cada quadro.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly ISubtitleStore _subtitleStore;
    private readonly AppSettingsService _settings;
    private readonly IFrameTimer _frameTimer;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private string _title = UiText.AppName;

    [ObservableProperty]
    private string? _statusMessage;

    public MainViewModel(
        PlayerViewModel player,
        PlaylistViewModel playlist,
        TranscriptViewModel transcript,
        TranscriptionViewModel transcription,
        IDialogService dialogs,
        ISubtitleStore subtitleStore,
        AppSettingsService settings,
        IFrameTimer frameTimer,
        ILogger<MainViewModel> logger)
    {
        Player = player;
        Playlist = playlist;
        Transcript = transcript;
        Transcription = transcription;
        _dialogs = dialogs;
        _subtitleStore = subtitleStore;
        _settings = settings;
        _frameTimer = frameTimer;
        _logger = logger;

        _frameTimer.Tick += (_, _) => OnFrame();
        Player.PlaybackCompleted += async (_, _) => await PlayNeighborAsync(Playlist.Next());
        Player.PlaybackFailed += (_, e) => _dialogs.ShowError(UiText.ErrorTitle, UiText.PlaybackFailed(UiText.ErrorReason(e.Exception)));
        Transcript.SeekRequested += (_, position) => Player.Seek(position);
        Transcript.Changed += (_, _) => UpdateTitle();
        Transcript.Saved += (_, subtitlePath) => StatusMessage = UiText.Saved(subtitlePath);
        Transcription.Completed += OnTranscriptionCompleted;
        Playlist.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlaylistViewModel.IsEmpty))
            {
                NextTrackCommand.NotifyCanExecuteChanged();
                PreviousTrackCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public PlayerViewModel Player { get; }

    public PlaylistViewModel Playlist { get; }

    public TranscriptViewModel Transcript { get; }

    public TranscriptionViewModel Transcription { get; }

    /// <summary>Inicialização ao abrir a janela: arquivos da linha de comando ou a última pasta usada.</summary>
    public async Task InitializeAsync(IReadOnlyCollection<string> startupPaths)
    {
        _frameTimer.IsEnabled = true;
        if (startupPaths.Count > 0)
        {
            await OpenPathsAsync(startupPaths);
        }
        else if (_settings.Current.LastFolder is { } folder && Directory.Exists(folder))
        {
            TryLoadPlaylist(() => Playlist.LoadFolder(folder), folder, showError: false);
        }
    }

    /// <summary>
    /// Primeira etapa ao fechar a janela: pergunta sobre alterações não salvas.
    /// Retorna <c>false</c> se o usuário desistir de fechar.
    /// </summary>
    public Task<bool> ConfirmCloseAsync() => ConfirmLeaveTranscriptAsync();

    /// <summary>
    /// Segunda etapa ao fechar: para a geração e espera ela terminar, porque o modelo nativo não pode ser
    /// liberado enquanto calcula. Na CPU, com modelos grandes, isso pode levar alguns segundos (o Whisper só
    /// para entre etapas). Depois para o áudio e grava as preferências.
    /// </summary>
    public async Task ShutdownAsync()
    {
        await Transcription.CancelAndWaitAsync();
        _frameTimer.IsEnabled = false;
        Player.Unload();
        _settings.Flush();
    }

    /// <summary>Abre arquivos e/ou pastas (ex.: arrastar e soltar) e toca o primeiro áudio.</summary>
    public async Task OpenPathsAsync(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0 || !await ConfirmLeaveTranscriptAsync())
        {
            return;
        }

        if (!TryLoadPlaylist(() => Playlist.LoadPaths(paths), paths.First()))
        {
            return;
        }

        if (paths.Count == 1 && Directory.Exists(paths.First()))
        {
            _settings.Update(s => s with { LastFolder = paths.First() });
        }

        await PlayItemAsync(Playlist.Items.FirstOrDefault(), autoPlay: false, confirmLeave: false);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = _dialogs.PickFolder(_settings.Current.LastFolder);
        if (folder is null || !await ConfirmLeaveTranscriptAsync())
        {
            return;
        }

        if (!TryLoadPlaylist(() => Playlist.LoadFolder(folder), folder))
        {
            return;
        }

        _settings.Update(s => s with { LastFolder = folder });
        await PlayItemAsync(Playlist.Items.FirstOrDefault(), autoPlay: false, confirmLeave: false);
    }

    /// <summary>
    /// Monta a playlist. Pasta sem permissão de leitura (ou que sumiu) vira uma mensagem; a playlist anterior
    /// fica como estava, porque a lista nova só substitui a antiga depois de lida.
    /// </summary>
    private bool TryLoadPlaylist(Action load, string path, bool showError = true)
    {
        try
        {
            load();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LogOpenFailed(_logger, ex, path);
            if (showError)
            {
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                _dialogs.ShowError(UiText.ErrorTitle, UiText.CannotOpen(name.Length > 0 ? name : path, UiText.ErrorReason(ex)));
            }

            return false;
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var file = _dialogs.PickAudioFile(_settings.Current.LastFolder);
        if (file is not null)
        {
            await OpenPathsAsync([file]);
        }
    }

    [RelayCommand]
    private Task PlayItem(PlaylistItemViewModel? item) => PlayItemAsync(item, autoPlay: true, confirmLeave: true);

    [RelayCommand(CanExecute = nameof(HasPlaylist))]
    private Task NextTrack() => PlayNeighborAsync(Playlist.Next());

    private bool HasPlaylist => !Playlist.IsEmpty;

    [RelayCommand(CanExecute = nameof(HasPlaylist))]
    private Task PreviousTrack() => PlayNeighborAsync(Playlist.Previous());

    private Task PlayNeighborAsync(PlaylistItemViewModel? item) =>
        item is null ? Task.CompletedTask : PlayItemAsync(item, autoPlay: true, confirmLeave: true);

    private async Task PlayItemAsync(PlaylistItemViewModel? item, bool autoPlay, bool confirmLeave)
    {
        if (item is null)
        {
            return;
        }

        if (confirmLeave && !await ConfirmLeaveTranscriptAsync())
        {
            Playlist.SelectedItem = Playlist.CurrentItem; // desistiu de trocar: a seleção volta para a faixa atual
            return;
        }

        StatusMessage = null;
        try
        {
            Player.Load(item.FilePath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Formato não suportado, arquivo corrompido ou sem dispositivo de áudio.
            LogOpenFailed(_logger, ex, item.FilePath);
            _dialogs.ShowError(UiText.ErrorTitle, UiText.CannotOpen(item.FileName, UiText.ErrorReason(ex)));
            Playlist.SetCurrent(null);
            Transcript.Load(null, null);
            Transcription.TargetAudioFilePath = null;
            UpdateTitle();
            return;
        }

        Playlist.SetCurrent(item);
        Transcription.TargetAudioFilePath = item.FilePath;
        await LoadSubtitleAsync(item.FilePath);
        UpdateTitle();

        if (autoPlay)
        {
            Player.Play();
        }
    }

    private async Task LoadSubtitleAsync(string audioFilePath)
    {
        try
        {
            var result = await _subtitleStore.LoadAsync(audioFilePath, CancellationToken.None);
            Transcript.Load(audioFilePath, result?.Transcript);
            if (result is { HasWarnings: true })
            {
                StatusMessage = UiText.SubtitleWarnings(result.IgnoredLineCount, result.CorrectedTimeCount);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogOpenFailed(_logger, ex, audioFilePath);
            Transcript.Load(audioFilePath, null);
            _dialogs.ShowError(UiText.ErrorTitle, UiText.CannotOpen(_subtitleStore.GetSubtitlePath(audioFilePath), ex.Message));
        }
    }

    /// <summary>Se a legenda tem alterações, pergunta se salva. Retorna <c>false</c> se o usuário cancelar.</summary>
    private async Task<bool> ConfirmLeaveTranscriptAsync()
    {
        if (!Transcript.IsDirty || Transcript.AudioFilePath is not { } path)
        {
            return true;
        }

        return _dialogs.AskSaveChanges(Path.GetFileName(path)) switch
        {
            SaveChangesChoice.Save => await Transcript.SaveAsync(),
            SaveChangesChoice.Discard => true,
            _ => false,
        };
    }

    private void OnTranscriptionCompleted(object? sender, TranscriptionCompletedEventArgs e)
    {
        Playlist.RefreshSubtitleState(e.AudioFilePath);
        if (string.Equals(e.AudioFilePath, Transcript.AudioFilePath, StringComparison.OrdinalIgnoreCase))
        {
            Transcript.Load(e.AudioFilePath, e.Transcript, e.IsSaved);
        }

        UpdateTitle();
    }

    private void OnFrame()
    {
        Player.Refresh();
        Transcript.UpdatePosition(Player.Position);
    }

    private void UpdateTitle() =>
        Title = UiText.WindowTitle(
            Transcript.AudioFilePath is { } path ? Path.GetFileName(path) : null, Transcript.IsDirty);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível abrir {FilePath}")]
    private static partial void LogOpenFailed(ILogger logger, Exception exception, string filePath);
}
