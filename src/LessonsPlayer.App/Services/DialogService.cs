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
        var save = new DialogButton(UiText.Save, IsDefault: true);
        var discard = new DialogButton(UiText.DontSave);
        var cancel = new DialogButton(UiText.Cancel, IsCancel: true);
        var chosen = MessageDialog.Show(
            Owner, UiText.SaveChangesTitle, UiText.SaveChangesMessage(fileName), MessageKind.Question, save, discard, cancel);
        return chosen == save ? SaveChangesChoice.Save
            : chosen == discard ? SaveChangesChoice.Discard
            : SaveChangesChoice.Cancel;
    }

    public bool Confirm(string title, string message, string confirmLabel)
    {
        var confirm = new DialogButton(confirmLabel, IsDefault: true);
        var cancel = new DialogButton(UiText.Cancel, IsCancel: true);
        return MessageDialog.Show(Owner, title, message, MessageKind.Question, confirm, cancel) == confirm;
    }

    public string? EditText(string title, string initialText)
    {
        var dialog = new EditTextDialog(title, initialText) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Text : null;
    }

    public void ShowError(string title, string message) =>
        MessageDialog.Show(Owner, title, message, MessageKind.Error, new DialogButton(UiText.Ok, IsDefault: true, IsCancel: true));
}
