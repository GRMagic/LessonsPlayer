using System.ComponentModel;
using System.Windows;
using LessonsPlayer.App.Input;
using LessonsPlayer.Presentation.ViewModels;

namespace LessonsPlayer.App.Views;

/// <summary>
/// Janela principal. O code-behind só faz a ponte de eventos que o XAML não expressa bem
/// (fechar com confirmação assíncrona, arrastar e soltar, atalhos globais); a lógica fica no ViewModel.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly StartupArguments _startupArguments;
    private bool _closeConfirmed;

    public MainWindow(MainViewModel viewModel, StartupArguments startupArguments)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _startupArguments = startupArguments;
        DataContext = viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += (_, e) => KeyboardShortcuts.Handle(e, _viewModel);
        PreviewTextInput += (_, e) => KeyboardShortcuts.HandleText(e, _viewModel);
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Focus(); // garante que os atalhos de teclado cheguem à janela
        await _viewModel.InitializeAsync(_startupArguments.Paths);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        // Cancela este fechamento, pergunta (pode salvar de forma assíncrona) e fecha de novo se confirmado.
        e.Cancel = true;
        if (await _viewModel.PrepareToCloseAsync())
        {
            _closeConfirmed = true;
            await Dispatcher.InvokeAsync(Close); // Close() não pode ser chamado dentro do próprio Closing
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await _viewModel.OpenPathsAsync(paths);
        }
    }
}
