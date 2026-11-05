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
/// <para>
/// Threads: os métodos públicos devem ser chamados de uma única thread (a da UI). O evento
/// <c>PlaybackStopped</c> do NAudio chega pela thread de reprodução, às vezes de forma síncrona dentro
/// de <c>Stop()</c>. Por isso o handler não usa locks (um lock causaria deadlock com o <c>Stop()</c>
/// do seek) e só lê estado que é seguro observar de outra thread.
/// </para>
/// </summary>
public sealed partial class NAudioPlayer(ILogger<NAudioPlayer> logger) : IAudioPlayer
{
    private const int LatencyMilliseconds = 80;

    private AudioFileReader? _reader;
    private WasapiPlayer? _output;
    private TimeSpan _origin;            // posição do arquivo onde o relógio do dispositivo começou a contar
    private volatile bool _reachedEnd;
    private volatile bool _isRepositioning; // Stop() feito pelo próprio seek: o PlaybackStopped resultante não é "fim"
    private float _volume = 1f;
    private volatile PlaybackState _state = PlaybackState.Stopped;

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
            if (_reader is not { } reader || _output is not { } output)
            {
                return TimeSpan.Zero;
            }

            if (_reachedEnd)
            {
                return reader.TotalTime;
            }

            var bytesPerSecond = output.OutputWaveFormat.AverageBytesPerSecond;
            var played = bytesPerSecond > 0
                ? TimeSpan.FromSeconds((double)output.GetPosition() / bytesPerSecond)
                : TimeSpan.Zero;
            return Clamp(_origin + played, TimeSpan.Zero, reader.TotalTime);
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            _reader?.Volume = _volume;
        }
    }

    public void Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Close();

        AudioFileReader? reader = null;
        WasapiPlayer? output = null;
        try
        {
            reader = new AudioFileReader(filePath) { Volume = _volume };
            output = new WasapiPlayerBuilder()
                .WithSharedMode()
                .WithEventSync()
                .WithLatency(LatencyMilliseconds)
                .Build();
            output.Init(reader);
        }
        catch
        {
            // Sem isso o arquivo ficaria travado até o GC.
            output?.Dispose();
            reader?.Dispose();
            throw;
        }

        output.PlaybackStopped += OnPlaybackStopped;
        _reader = reader;
        _output = output;
        _origin = TimeSpan.Zero;
        _reachedEnd = false;
        CurrentFile = filePath;

        LogOpened(logger, filePath, reader.TotalTime);
        SetState(PlaybackState.Stopped);
    }

    public void Close()
    {
        var (output, reader) = (_output, _reader);
        (_output, _reader) = (null, null);
        CurrentFile = null;
        _reachedEnd = false;

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
        if (_reader is not { } reader || _output is not { } output)
        {
            return;
        }

        if (_reachedEnd)
        {
            // Tocar de novo depois do fim recomeça do início.
            reader.Position = 0;
            _origin = TimeSpan.Zero;
            _reachedEnd = false;
        }

        output.Play();
        SetState(PlaybackState.Playing);
    }

    public void Pause()
    {
        if (_output is not { } output || _state != PlaybackState.Playing)
        {
            return;
        }

        output.Pause();
        SetState(PlaybackState.Paused);
    }

    public void Seek(TimeSpan position)
    {
        if (_reader is not { } reader || _output is not { } output)
        {
            return;
        }

        var resume = _state == PlaybackState.Playing;

        // Stop descarta o buffer do dispositivo e zera o relógio: a nova origem passa a valer imediatamente.
        _isRepositioning = true;
        try
        {
            if (output.PlaybackState != NAudio.Wave.PlaybackState.Stopped)
            {
                output.Stop();
            }

            reader.CurrentTime = Clamp(position, TimeSpan.Zero, reader.TotalTime);
            _origin = reader.CurrentTime;
            _reachedEnd = false;
        }
        finally
        {
            _isRepositioning = false;
        }

        if (resume)
        {
            output.Play();
        }
    }

    public void Dispose() => Close();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, _output))
        {
            return; // evento atrasado de um arquivo anterior
        }

        if (e.Exception is not null)
        {
            LogPlaybackFailed(logger, e.Exception, CurrentFile);
            SetState(PlaybackState.Stopped);
            PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(e.Exception));
            return;
        }

        // Também chega aqui depois do Stop() de um seek; só é "fim" se o arquivo foi lido até o final.
        if (_isRepositioning || _reader is not { } reader || reader.Position < reader.Length)
        {
            return;
        }

        _reachedEnd = true;
        SetState(PlaybackState.Stopped);
        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
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
