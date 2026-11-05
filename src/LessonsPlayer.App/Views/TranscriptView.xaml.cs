using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LessonsPlayer.Presentation.ViewModels;

namespace LessonsPlayer.App.Views;

/// <summary>
/// Lógica só de view: rola a lista para manter o trecho ativo centralizado ("seguir áudio"),
/// pausando o acompanhamento por alguns segundos quando o usuário rola manualmente.
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
        DataContextChanged += OnDataContextChanged;
        SegmentList.PreviewMouseWheel += (_, _) => MarkManualScroll();
        SegmentList.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler((_, _) => MarkManualScroll()));
    }

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
