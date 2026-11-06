using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LessonsPlayer.Presentation.ViewModels;

namespace LessonsPlayer.App.Views;

/// <summary>
/// Lógica só de view: rola a lista para manter o trecho ativo centralizado ("seguir áudio"),
/// pausando o acompanhamento por alguns segundos quando o usuário rola manualmente.
/// <para>
/// Rolagem manual: roda do mouse, clique nas setas ou na trilha da barra e arraste do polegar. O
/// <see cref="ScrollViewer"/> trata os eventos da própria barra (e os cliques viram comandos, sem evento
/// <c>Scroll</c>), por isso os handlers recebem também eventos já tratados.
/// </para>
/// </summary>
public partial class TranscriptView : UserControl
{
    private static readonly TimeSpan ManualScrollPause = TimeSpan.FromSeconds(4);

    private TranscriptViewModel? _viewModel;
    private ScrollViewer? _scrollViewer;
    private long _lastManualScrollTicks;

    public TranscriptView()
    {
        InitializeComponent();

        // Destino do foco ao clicar fora de uma palavra em edição (ver OnPreviewMouseDown); fora da ordem de Tab.
        Focusable = true;
        IsTabStop = false;
        FocusVisualStyle = null;
        DataContextChanged += OnDataContextChanged;
        SegmentList.AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) => MarkManualScroll()), handledEventsToo: true);
        SegmentList.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler((_, _) => MarkManualScroll()), handledEventsToo: true);
        SegmentList.AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), handledEventsToo: true);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<ScrollBar>(source) is not null)
        {
            MarkManualScroll(); // a edição continua aberta: a barra não pega o foco
            return;
        }

        // Clique fora de uma palavra (área vazia, horário do trecho) confirma a edição em andamento: nada ali
        // pega o foco, então a caixa de texto nunca o perderia. Clique em outra palavra já confirma pelo comando.
        // O foco vai para esta view: a janela não serve, porque é um escopo de foco e o devolveria à caixa.
        if (Keyboard.FocusedElement is TextBox editing
            && editing.IsDescendantOf(this)
            && FindAncestor<TextBox>(source) is null
            && !IsOnWord(source))
        {
            Focus();
        }
    }

    private static bool IsOnWord(DependencyObject? element)
    {
        for (var current = element; current is not null; current = GetParent(current))
        {
            if (current is FrameworkElement { DataContext: WordViewModel } and not ListBoxItem)
            {
                return true;
            }

            if (current is ListBoxItem)
            {
                return false; // chegou ao trecho sem passar por uma palavra
            }
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        for (var current = element; current is not null; current = GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Pai visual, ou lógico para elementos de texto (<c>Run</c>), que não são <see cref="Visual"/>.</summary>
    private static DependencyObject? GetParent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as TranscriptViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TranscriptViewModel.ActiveSegment):
                FollowActiveSegment();
                break;
            case nameof(TranscriptViewModel.FollowAudio) when _viewModel?.FollowAudio == true:
                _lastManualScrollTicks = 0;
                FollowActiveSegment();
                break;
        }
    }

    private void MarkManualScroll() => _lastManualScrollTicks = Environment.TickCount64;

    private void FollowActiveSegment()
    {
        if (_viewModel is not { FollowAudio: true, ActiveSegment: { } segment })
        {
            return;
        }

        var sinceManualScroll = TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastManualScrollTicks);
        if (_lastManualScrollTicks != 0 && sinceManualScroll < ManualScrollPause)
        {
            return;
        }

        ScrollToCenter(segment);
    }

    private void ScrollToCenter(SegmentViewModel segment)
    {
        _scrollViewer ??= FindDescendant<ScrollViewer>(SegmentList);
        SegmentList.ScrollIntoView(segment); // materializa o item virtualizado
        SegmentList.UpdateLayout();

        if (_scrollViewer is null
            || SegmentList.ItemContainerGenerator.ContainerFromItem(segment) is not FrameworkElement container
            || !container.IsDescendantOf(_scrollViewer))
        {
            return;
        }

        var top = container.TransformToAncestor(_scrollViewer).Transform(new Point(0, 0)).Y;
        var target = _scrollViewer.VerticalOffset + top - ((_scrollViewer.ViewportHeight - container.ActualHeight) / 2);
        _scrollViewer.ScrollToVerticalOffset(Math.Max(0, target));
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
