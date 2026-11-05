namespace LessonsPlayer.Presentation.Services;

/// <summary>Relógio de atualização da tela (~30 fps), disparado na thread da UI.</summary>
public interface IFrameTimer
{
    event EventHandler? Tick;

    bool IsEnabled { get; set; }
}
