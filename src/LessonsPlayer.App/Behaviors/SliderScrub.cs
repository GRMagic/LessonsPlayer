using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LessonsPlayer.App.Behaviors;

/// <summary>
/// Expõe, como propriedade vinculável, se o usuário está arrastando o thumb do slider.
/// Uso: <c>behaviors:SliderScrub.IsScrubbing="{Binding IsScrubbing, Mode=TwoWay}"</c>.
/// </summary>
public static class SliderScrub
{
    public static readonly DependencyProperty IsScrubbingProperty = DependencyProperty.RegisterAttached(
        "IsScrubbing",
        typeof(bool),
        typeof(SliderScrub),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnAttached));

    // Marca se os handlers já foram ligados ao slider.
    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached("IsHooked", typeof(bool), typeof(SliderScrub));

    public static bool GetIsScrubbing(DependencyObject element) => (bool)element.GetValue(IsScrubbingProperty);

    public static void SetIsScrubbing(DependencyObject element, bool value) => element.SetValue(IsScrubbingProperty, value);

    private static void OnAttached(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider || (bool)slider.GetValue(IsHookedProperty))
        {
            return;
        }

        slider.SetValue(IsHookedProperty, true);
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => SetIsScrubbing(slider, true)), handledEventsToo: true);
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => SetIsScrubbing(slider, false)), handledEventsToo: true);
    }
}
