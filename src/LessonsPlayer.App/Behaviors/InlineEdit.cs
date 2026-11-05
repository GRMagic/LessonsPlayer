using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace LessonsPlayer.App.Behaviors;

/// <summary>
/// Comportamento de edição inline para um TextBox: recebe o foco (com o texto selecionado) quando fica
/// visível e executa <c>CommitCommand</c> ao perder o foco.
/// </summary>
public static class InlineEdit
{
    public static readonly DependencyProperty CommitCommandProperty = DependencyProperty.RegisterAttached(
        "CommitCommand", typeof(ICommand), typeof(InlineEdit), new PropertyMetadata(null, OnCommitCommandChanged));

    public static ICommand? GetCommitCommand(DependencyObject element) => (ICommand?)element.GetValue(CommitCommandProperty);

    public static void SetCommitCommand(DependencyObject element, ICommand? value) => element.SetValue(CommitCommandProperty, value);

    private static void OnCommitCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox || e.OldValue is not null)
        {
            return;
        }

        textBox.IsVisibleChanged += OnIsVisibleChanged;
        textBox.LostKeyboardFocus += OnLostKeyboardFocus;
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.IsVisible)
        {
            // Depois do layout, para o foco "pegar" no controle recém-exibido.
            textBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                textBox.Focus();
                textBox.SelectAll();
            });
        }
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Depois de confirmar a edição, o trecho é recriado e este TextBox sai da árvore. Ao perder o foco, o
        // DataContext dele passa a ser o sentinela "{DisconnectedItem}", que o comando tipado rejeitaria com exceção.
        if (sender is not TextBox { DataContext: { } item } textBox
            || ReferenceEquals(item, BindingOperations.DisconnectedSource)
            || GetCommitCommand(textBox) is not { } command)
        {
            return;
        }

        if (command.CanExecute(item))
        {
            command.Execute(item);
        }
    }
}
