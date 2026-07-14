using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace iBarter.Controls;

public sealed class FluentIconButton : Button {
    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(
        nameof(IconData), typeof(Geometry), typeof(FluentIconButton));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(FluentIconButton), new PropertyMetadata(string.Empty));

    public Geometry? IconData {
        get => (Geometry?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public string Label {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}
