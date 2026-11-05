using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Presentation.Formatting;
using LessonsPlayer.Presentation.Services;

namespace LessonsPlayer.Presentation.ViewModels;

/// <summary>Controles de reprodução: play/pause, posição (slider), volume e saltos.</summary>
public sealed partial class PlayerViewModel : ObservableObject
{
    private static readonly TimeSpan SkipStep = TimeSpan.FromSeconds(5);

    private readonly IAudioPlayer _player;
    private readonly IUiDispatcher _ui;
    private readonly AppSettingsService _settings;
    private bool _isUpdatingFromPlayer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private double _positionSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private double _durationSeconds;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand), nameof(SkipBackCommand), nameof(SkipForwardCommand))]
    private bool _hasMedia;

    /// <summary>Verdadeiro enquanto o usuário arrasta o slider: a posição não é sobrescrita pelo timer.</summary>
    [ObservableProperty]
    private bool _isScrubbing;

    [ObservableProperty]
    private double _volume;

    public PlayerViewModel(IAudioPlayer player, IUiDispatcher ui, AppSettingsService settings)
    {
        _player = player;
        _ui = ui;
        _settings = settings;
        _volume = settings.Current.Volume;
        _player.Volume = (float)_volume;

        _player.StateChanged += (_, _) => _ui.Post(() => IsPlaying = _player.State == PlaybackState.Playing);
        _player.PlaybackCompleted += (_, _) => _ui.Post(() =>
        {
            Refresh();
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        });
        _player.PlaybackFailed += (_, e) => _ui.Post(() => PlaybackFailed?.Invoke(this, e));
    }

    /// <summary>A faixa terminou naturalmente (já na thread da UI).</summary>
    public event EventHandler? PlaybackCompleted;

    /// <summary>A reprodução falhou (já na thread da UI).</summary>
    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    /// <summary>Última posição lida do player (atualizada a cada <see cref="Refresh"/>).</summary>
    public TimeSpan Position { get; private set; }

    public string PositionText => TimeFormat.Compact(TimeSpan.FromSeconds(PositionSeconds));

    public string DurationText => TimeFormat.Compact(TimeSpan.FromSeconds(DurationSeconds));

    /// <summary>Abre o arquivo (parado no início). Exceções de formato/arquivo são propagadas.</summary>
    public void Load(string filePath)
    {
        try
        {
            _player.Open(filePath);
        }
        finally
        {
            HasMedia = _player.CurrentFile is not null;
            DurationSeconds = _player.Duration.TotalSeconds;
            Refresh();
        }
    }

    public void Unload()
    {
        _player.Close();
        HasMedia = false;
        DurationSeconds = 0;
        Refresh();
    }

    public void Play() => _player.Play();

    public void Seek(TimeSpan position)
    {
        _player.Seek(position);
        Refresh();
    }

    /// <summary>Lê a posição atual do player; chamado a cada quadro.</summary>
    public void Refresh()
    {
        Position = _player.Position;
        if (IsScrubbing)
        {
            return;
        }

        _isUpdatingFromPlayer = true;
        try
        {
            PositionSeconds = Position.TotalSeconds;
        }
        finally
        {
            _isUpdatingFromPlayer = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasMedia))]
    private void PlayPause()
    {
        if (_player.State == PlaybackState.Playing)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }
    }

    [RelayCommand(CanExecute = nameof(HasMedia))]
    private void SkipBack() => Seek(Position - SkipStep);

    [RelayCommand(CanExecute = nameof(HasMedia))]
    private void SkipForward() => Seek(Position + SkipStep);

    partial void OnPositionSecondsChanged(double value)
    {
        // Mudança feita pelo usuário (clique na barra do slider); arrastar só aplica ao soltar.
        if (!_isUpdatingFromPlayer && !IsScrubbing && HasMedia)
        {
            Seek(TimeSpan.FromSeconds(value));
        }
    }

    partial void OnIsScrubbingChanged(bool value)
    {
        if (!value && HasMedia)
        {
            Seek(TimeSpan.FromSeconds(PositionSeconds));
        }
    }

    partial void OnVolumeChanged(double value)
    {
        _player.Volume = (float)value;
        _settings.Update(s => s with { Volume = (float)value });
    }
}
