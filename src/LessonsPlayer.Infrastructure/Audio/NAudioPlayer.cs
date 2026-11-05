using LessonsPlayer.Core.Abstractions;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using PlaybackState = LessonsPlayer.Core.Abstractions.PlaybackState;

namespace LessonsPlayer.Infrastructure.Audio;

/// <summary>
/// Player baseado em NAudio: <see cref="AudioFileReader"/> + WASAPI compartilhado.
/// <para>
/// A posição reportada é a <b>realmente tocada</b> no dispositivo, calculada pelo relógio do WASAPI
/// (<see cref="IWavePosition.GetPosition"/>) a partir da posição do último seek. A posição de leitura do
/// arquivo fica adiantada pelo tamanho do buffer e deixaria o karaokê antecipado.
/// </para>
/// </summary>
public sealed partial class NAudioPlayer(ILogger<NAudioPlayer> logger) : IAudioPlayer
{
    private const int LatencyMilliseconds = 80;

    private readonly Lock _sync = new();
    private AudioFileReader? _reader;
    private WasapiPlayer? _output;
    private TimeSpan _origin;      // posição do arquivo onde o relógio do dispositivo começou a contar
    private bool _reachedEnd;
    private float _volume = 1f;
    private PlaybackState _state = PlaybackState.Stopped;

    public event EventHandler? StateChanged;

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    public string? CurrentFile { get; private set; }

    public PlaybackState State => _state;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get
        {
            lock (_sync)
            {
                if (_reader is null || _output is null)
                {
                    return TimeSpan.Zero;
                }

                if (_reachedEnd)
                {
                    return _reader.TotalTime;
                }

                var bytesPerSecond = _output.OutputWaveFormat.AverageBytesPerSecond;
                var played = bytesPerSecond > 0
                    ? TimeSpan.FromSeconds((double)_output.GetPosition() / bytesPerSecond)
                    : TimeSpan.Zero;
                return Clamp(_origin + played, TimeSpan.Zero, _reader.TotalTime);
            }
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            lock (_sync)
            {
                if (_reader is not null)
                {
                    _reader.Volume = _volume;
                }
            }
        }
    }

    public void Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Close();

        var reader = new AudioFileReader(filePath) { Volume = _volume };
        var output = new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithEventSync()
            .WithLatency(LatencyMilliseconds)
            .WithDefaultDeviceStreamRouting() // acompanha a troca do dispositivo padrão (ex.: fone conectado)
            .Build();
        try
        {
            output.Init(reader);
        }
        catch
        {
            output.Dispose();
            reader.Dispose();
            throw;
        }

        output.PlaybackStopped += OnPlaybackStopped;
        lock (_sync)
        {
            _reader = reader;
            _output = output;
            _origin = TimeSpan.Zero;
            _reachedEnd = false;
            CurrentFile = filePath;
        }

        LogOpened(logger, filePath, reader.TotalTime);
        SetState(PlaybackState.Stopped);
    }

    public void Close()
    {
        WasapiPlayer? output;
        AudioFileReader? reader;
        lock (_sync)
        {
            (output, reader) = (_output, _reader);
            (_output, _reader) = (null, null);
            CurrentFile = null;
            _reachedEnd = false;
        }

        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Dispose();
        }

        reader?.Dispose();
        SetState(PlaybackState.Stopped);
    }

    public void Play()
    {
        lock (_sync)
        {
            if (_reader is null || _output is null)
            {
                return;
            }

            if (_reachedEnd)
            {
                // Tocar de novo depois do fim recomeça do início.
                _reader.Position = 0;
                _origin = TimeSpan.Zero;
                _reachedEnd = false;
            }

            _output.Play();
        }

        SetState(PlaybackState.Playing);
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_output is null || _state != PlaybackState.Playing)
            {
                return;
            }

            _output.Pause();
        }

        SetState(PlaybackState.Paused);
    }

    public void Seek(TimeSpan position)
    {
        lock (_sync)
        {
            if (_reader is null || _output is null)
            {
                return;
            }

            var resume = _state == PlaybackState.Playing;

            // Stop descarta o buffer do dispositivo e zera o relógio: a nova origem passa a valer imediatamente.
            if (_output.PlaybackState != NAudio.Wave.PlaybackState.Stopped)
            {
                _output.Stop();
            }

            _reader.CurrentTime = Clamp(position, TimeSpan.Zero, _reader.TotalTime);
            _origin = _reader.CurrentTime;
            _reachedEnd = false;

            if (resume)
            {
                _output.Play();
            }
        }
    }

    public void Dispose() => Close();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            LogPlaybackFailed(logger, e.Exception, CurrentFile);
            SetState(PlaybackState.Stopped);
            PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(e.Exception));
            return;
        }

        bool completed;
        lock (_sync)
        {
            // Também chega aqui depois de um Stop feito pelo Seek; só é "fim" se o arquivo foi lido até o final.
            completed = sender is WasapiPlayer output
                && ReferenceEquals(output, _output)
                && _reader is not null
                && output.PlaybackState == NAudio.Wave.PlaybackState.Stopped
                && _reader.Position >= _reader.Length;
            if (completed)
            {
                _reachedEnd = true;
            }
        }

        if (completed)
        {
            SetState(PlaybackState.Stopped);
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetState(PlaybackState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    [LoggerMessage(Level = LogLevel.Information, Message = "Áudio aberto: {FilePath} ({Duration})")]
    private static partial void LogOpened(ILogger logger, string filePath, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha na reprodução de {FilePath}")]
    private static partial void LogPlaybackFailed(ILogger logger, Exception exception, string? filePath);
}
