using System.Windows.Threading;
using LessonsPlayer.Presentation.Services;

namespace LessonsPlayer.App.Services;

/// <summary>Timer de ~30 quadros por segundo na thread da UI, para o slider e o karaokê.</summary>
internal sealed class DispatcherFrameTimer : IFrameTimer
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    private readonly DispatcherTimer _timer;

    public DispatcherFrameTimer(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(FrameInterval, DispatcherPriority.Render, (_, _) => Tick?.Invoke(this, EventArgs.Empty), dispatcher);
        _timer.Stop();
    }

    public event EventHandler? Tick;

    public bool IsEnabled
    {
        get => _timer.IsEnabled;
        set => _timer.IsEnabled = value;
    }
}
