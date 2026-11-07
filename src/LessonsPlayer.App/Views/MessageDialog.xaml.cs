using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LessonsPlayer.App.Views;

/// <summary>Tipo de mensagem: define o ícone.</summary>
internal enum MessageKind
{
    Question,
    Error,
}

/// <summary>Um botão do <see cref="MessageDialog"/>. <c>IsCancel</c> também responde ao Esc e ao X da janela.</summary>
internal sealed record DialogButton(string Label, bool IsDefault = false, bool IsCancel = false);

/// <summary>
/// Caixa de mensagem com os textos da interface. A <c>MessageBox</c> do Windows escreve os botões no idioma
/// do sistema ("Sim", "Não", "Cancelar" num Windows em português), o que misturaria idiomas; aqui os botões
/// dizem a ação ("Delete", "Don't save").
/// </summary>
internal sealed partial class MessageDialog : Window
{
    private const string GlyphQuestion = "";
    private const string GlyphError = "";
    private static readonly Brush ErrorBrush = CreateFrozenBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));

    private DialogButton? _chosen;

    private MessageDialog(string title, string message, MessageKind kind, IReadOnlyList<DialogButton> buttons)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        IconText.Text = kind == MessageKind.Error ? GlyphError : GlyphQuestion;
        IconText.Foreground = kind == MessageKind.Error ? ErrorBrush : (Brush)FindResource("AccentBrush");

        foreach (var button in buttons)
        {
            var control = new Button
            {
                Content = button.Label,
                IsDefault = button.IsDefault,
                IsCancel = button.IsCancel,
                MinWidth = 88,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(8, 0, 0, 0),
            };
            control.Click += (_, _) =>
            {
                _chosen = button;
                DialogResult = true;
            };
            ButtonPanel.Children.Add(control);
        }
    }

    /// <summary>Mostra a mensagem e devolve o botão escolhido; fechar pelo X ou pelo Esc conta como o de cancelar.</summary>
    public static DialogButton Show(
        Window? owner, string title, string message, MessageKind kind, params IReadOnlyList<DialogButton> buttons)
    {
        var dialog = new MessageDialog(title, message, kind, buttons);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog._chosen ?? buttons.FirstOrDefault(b => b.IsCancel) ?? buttons[^1];
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
