using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LessonsPlayer.Presentation.ViewModels;

namespace LessonsPlayer.App.Input;

/// <summary>
/// Atalhos globais da janela. São ignorados enquanto o usuário digita num campo de texto,
/// para que Espaço, setas, Del e Ctrl+Z funcionem normalmente na edição.
/// </summary>
internal static class KeyboardShortcuts
{
    public static void Handle(KeyEventArgs e, MainViewModel vm)
    {
        if (e.Handled || IsTyping(e))
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key; // combinações com Alt chegam como Key.System
        Execute(Resolve(key, Keyboard.Modifiers, vm), e);
    }

    /// <summary>"[" e "]" dependem do layout do teclado (ex.: ABNT2), então são tratados pelo texto digitado.</summary>
    public static void HandleText(TextCompositionEventArgs e, MainViewModel vm)
    {
        if (e.Handled || IsTyping(e))
        {
            return;
        }

        var command = e.Text switch
        {
            "[" => vm.Transcript.SetStartHereCommand,
            "]" => vm.Transcript.SetEndHereCommand,
            _ => null,
        };
        Execute(command, e);
    }

    private static ICommand? Resolve(Key key, ModifierKeys modifiers, MainViewModel vm) => (key, modifiers) switch
    {
        // Reprodução
        (Key.Space, ModifierKeys.None) => vm.Player.PlayPauseCommand,
        (Key.Left, ModifierKeys.None) => vm.Player.SkipBackCommand,
        (Key.Right, ModifierKeys.None) => vm.Player.SkipForwardCommand,
        (Key.Left, ModifierKeys.Control) => vm.PreviousTrackCommand,
        (Key.Right, ModifierKeys.Control) => vm.NextTrackCommand,

        // Edição da legenda (agem sobre a palavra selecionada)
        (Key.F2, ModifierKeys.None) => vm.Transcript.BeginEditWordCommand,
        (Key.E, ModifierKeys.Control) => vm.Transcript.EditSegmentCommand,
        (Key.Delete, ModifierKeys.None) => vm.Transcript.DeleteWordCommand,
        (Key.D, ModifierKeys.Control) => vm.Transcript.SplitHereCommand,
        (Key.J, ModifierKeys.Control) => vm.Transcript.MergeWithNextCommand,
        (Key.Left, ModifierKeys.Alt) => vm.Transcript.NudgeStartEarlierCommand,
        (Key.Right, ModifierKeys.Alt) => vm.Transcript.NudgeStartLaterCommand,
        (Key.Left, ModifierKeys.Alt | ModifierKeys.Shift) => vm.Transcript.NudgeEndEarlierCommand,
        (Key.Right, ModifierKeys.Alt | ModifierKeys.Shift) => vm.Transcript.NudgeEndLaterCommand,

        // Histórico e arquivo
        (Key.Z, ModifierKeys.Control) => vm.Transcript.UndoCommand,
        (Key.Y, ModifierKeys.Control) => vm.Transcript.RedoCommand,
        (Key.S, ModifierKeys.Control) => vm.Transcript.SaveCommand,
        (Key.O, ModifierKeys.Control) => vm.OpenFileCommand,
        _ => null,
    };

    private static void Execute(ICommand? command, RoutedEventArgs e)
    {
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private static bool IsTyping(RoutedEventArgs e) =>
        e.OriginalSource is TextBoxBase || Keyboard.FocusedElement is TextBoxBase;
}
