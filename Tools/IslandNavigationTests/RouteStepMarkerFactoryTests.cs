using System.Threading;
using iBarter.View;
using Xunit;

namespace IslandNavigationTests;

public sealed class RouteStepMarkerFactoryTests {
    [Fact]
    public void Step_marker_uses_a_centered_text_block_inside_the_circle_grid() {
        Exception? exception = null;
        bool isCentered = false;
        var thread = new Thread(() => {
            try {
                var marker = RouteStepMarkerFactory.CreateMarker(
                    12, 20, System.Windows.Media.Brushes.Orange, routeSelected: true);
                var text = Assert.IsType<System.Windows.Controls.TextBlock>(
                    marker.Children[1]);
                Assert.IsType<System.Windows.Shapes.Ellipse>(marker.Children[0]);
                isCentered = text.HorizontalAlignment == System.Windows.HorizontalAlignment.Center
                    && text.VerticalAlignment == System.Windows.VerticalAlignment.Center
                    && text.Padding == new System.Windows.Thickness(0)
                    && marker.Width == 20
                    && marker.Height == 20
                    && text.Text == "12";
            }
            catch (Exception ex) {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(exception);
        Assert.True(isCentered);
    }

    [Fact]
    public void Step_number_label_centers_its_content_without_theme_padding() {
        Exception? exception = null;
        bool isCentered = false;
        var thread = new Thread(() => {
            try {
                var label = RouteStepMarkerFactory.CreateStepNumber(7, 20);
                isCentered = label.HorizontalContentAlignment == System.Windows.HorizontalAlignment.Center
                    && label.VerticalContentAlignment == System.Windows.VerticalAlignment.Center
                    && label.Padding == new System.Windows.Thickness(0)
                    && label.Width == 20
                    && label.Height == 20
                    && Equals("7", label.Content);
            }
            catch (Exception ex) {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(exception);
        Assert.True(isCentered);
    }
}
