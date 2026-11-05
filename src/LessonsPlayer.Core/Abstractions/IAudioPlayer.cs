namespace LessonsPlayer.Core.Abstractions;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// Reprodução de um arquivo de áudio.
/// Os eventos podem ser disparados em qualquer thread; quem consome deve fazer o marshaling para a UI.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>Disparado quando <see cref="State"/> muda.</summary>
    event EventHandler? StateChanged;

    /// <summary>Disparado quando o áudio chega ao fim naturalmente.</summary>
    event EventHandler? PlaybackCompleted;

    /// <summary>Disparado quando a reprodução é interrompida por um erro do dispositivo ou do arquivo.</summary>
    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    string? CurrentFile { get; }

    PlaybackState State { get; }

    /// <summary>Posição realmente tocada no dispositivo, não a posição de leitura do buffer.</summary>
    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    /// <summary>Volume de 0 a 1.</summary>
    float Volume { get; set; }

    /// <summary>Abre o arquivo (parado no início). Lança exceção se o formato não for suportado.</summary>
    void Open(string filePath);

    void Close();

    void Play();

    void Pause();

    void Seek(TimeSpan position);
}

public sealed class PlaybackFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
