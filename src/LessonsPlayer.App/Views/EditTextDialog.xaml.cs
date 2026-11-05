using System.Windows;

namespace LessonsPlayer.App.Views;

/// <summary>Diálogo simples para editar um texto (Enter confirma, Esc cancela).</summary>
public partial class EditTextDialog : Window
{
    public EditTextDialog(string title, string initialText)
    {
        InitializeComponent();
        Title = title;
        Editor.Text = initialText;
        Loaded += (_, _) =>
        {
            Editor.Focus();
            Editor.CaretIndex = Editor.Text.Length;
        };
    }

    public string Text => Editor.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
