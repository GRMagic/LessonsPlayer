using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Audio;

namespace LessonsPlayer.Presentation.Tests.Fakes;

/// <summary>Player em memória: registra as chamadas e permite simular fim de faixa e posição.</summary>
internal sealed class FakeAudioPlayer : IAudioPlayer
{
    public event EventHandler? StateChanged;

    public event EventHandler? PlaybackCompleted;

    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    public string? CurrentFile { get; private set; }

    public PlaybackState State { get; private set; }

    public TimeSpan Position { get; set; }

    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(3);

    public float Volume { get; set; }

    public List<TimeSpan> Seeks { get; } = [];

    public HashSet<string> UnsupportedFiles { get; } = [];

    public void Open(string filePath)
    {
        if (UnsupportedFiles.Contains(filePath))
        {
            throw new UnsupportedAudioFileException("The byte stream type is not supported.");
        }

        CurrentFile = filePath;
        Position = TimeSpan.Zero;
        SetState(PlaybackState.Stopped);
    }

    public void Close()
    {
        CurrentFile = null;
        SetState(PlaybackState.Stopped);
    }

    public void Play() => SetState(PlaybackState.Playing);

    public void Pause() => SetState(PlaybackState.Paused);

    public void Seek(TimeSpan position)
    {
        Seeks.Add(position);
        Position = position;
    }

    public void SimulateCompleted()
    {
        SetState(PlaybackState.Stopped);
        PlaybackCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void SimulateFailure(Exception exception) => PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(exception));

    public void Dispose()
    {
    }

    private void SetState(PlaybackState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
