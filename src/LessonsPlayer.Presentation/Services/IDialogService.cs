namespace LessonsPlayer.Presentation.Services;

/// <summary>Resposta ao perguntar o que fazer com alterações não salvas.</summary>
public enum SaveChangesChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Diálogos da aplicação, abstraídos para que os ViewModels não dependam da UI.</summary>
public interface IDialogService
{
    string? PickFolder(string? initialFolder);

    string? PickAudioFile(string? initialFolder);

    SaveChangesChoice AskSaveChanges(string fileName);

    /// <summary>Pede confirmação de uma ação; <paramref name="confirmLabel"/> é o texto do botão que confirma (ex.: "Delete").</summary>
    bool Confirm(string title, string message, string confirmLabel);

    /// <summary>Mostra um editor de texto; retorna <c>null</c> se o usuário cancelar.</summary>
    string? EditText(string title, string initialText);

    void ShowError(string title, string message);
}
