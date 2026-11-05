namespace LessonsPlayer.Presentation.Services;

/// <summary>Executa ações na thread da UI (eventos do player podem chegar de outras threads).</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
