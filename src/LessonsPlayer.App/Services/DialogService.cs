using System.IO;
using System.Windows;
using LessonsPlayer.App.Views;
using LessonsPlayer.Core.Playlist;
using LessonsPlayer.Presentation.Resources;
using LessonsPlayer.Presentation.Services;
using Microsoft.Win32;

namespace LessonsPlayer.App.Services;

/// <summary>Diálogos nativos do Windows e janelas próprias, sempre com a janela principal como dona.</summary>
internal sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public string? PickFolder(string? initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = UiText.OpenFolder };
        if (initialFolder is not null && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public string? PickAudioFile(string? initialFolder)
    {
        var patterns = string.Join(';', PlaylistBuilder.SupportedExtensions.Order().Select(e => "*" + e));
        var dialog = new OpenFileDialog
        {
            Title = UiText.OpenFile,
            Filter = $"{UiText.AudioFilesFilterName} ({patterns})|{patterns}",
        };
        if (initialFolder is not null && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public SaveChangesChoice AskSaveChanges(string fileName)
    {
        var result = Show(UiText.SaveChangesMessage(fileName), UiText.SaveChangesTitle,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveChangesChoice.Save,
            MessageBoxResult.No => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };
    }

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public string? EditText(string title, string initialText)
    {
        var dialog = new EditTextDialog(title, initialText) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Text : null;
    }

    public void ShowError(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { IsVisible: true } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
