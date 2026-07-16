using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace iBarter.View;

internal static class RouteStepMarkerFactory {
    internal static Grid CreateMarker(
        int step,
        double diameter,
        Brush stroke,
        bool routeSelected) {
        var marker = new Grid {
            Width = diameter,
            Height = diameter,
        };
        marker.Children.Add(new Ellipse {
            Fill = new SolidColorBrush(Color.FromArgb(
                routeSelected ? (byte)230 : (byte)150, 5, 22, 30)),
            Stroke = routeSelected ? Brushes.White : stroke,
            StrokeThickness = routeSelected ? 2.25 : 1.1,
        });
        marker.Children.Add(new TextBlock {
            Text = step.ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Padding = new Thickness(0),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return marker;
    }

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
