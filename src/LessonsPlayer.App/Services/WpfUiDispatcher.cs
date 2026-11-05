using System.Windows.Threading;
using LessonsPlayer.Presentation.Services;

namespace LessonsPlayer.App.Services;

/// <summary>Executa na thread da UI; se já estiver nela, executa imediatamente.</summary>
internal sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
