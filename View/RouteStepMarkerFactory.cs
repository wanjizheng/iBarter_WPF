using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace iBarter.View;

internal static class RouteStepMarkerFactory {
    internal static Label CreateStepNumber(int step, double diameter) {
        return new Label {
            Content = step.ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Width = diameter,
            Height = diameter,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }
}
