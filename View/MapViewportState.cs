using System.Windows;

namespace iBarter.View {
    /// <summary>
    /// Keeps map-camera math independent of WPF controls so zooming can retain
    /// the world point under the pointer and pan can be tested without a view.
    /// </summary>
    public sealed class MapViewportState {
        public const double MinScale = 0.65;
        public const double MaxScale = 2.50;

        public double Scale { get; private set; } = 1;
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }

        public void ZoomAt(Point anchor, double multiplier) {
            if (!Double.IsFinite(multiplier) || multiplier <= 0) return;

            double nextScale = Math.Clamp(Scale * multiplier, MinScale, MaxScale);
            if (Math.Abs(nextScale - Scale) < 0.000001) return;

            double ratio = nextScale / Scale;
            OffsetX = anchor.X - (anchor.X - OffsetX) * ratio;
            OffsetY = anchor.Y - (anchor.Y - OffsetY) * ratio;
            Scale = nextScale;
        }

        public void PanBy(Vector delta) {
            OffsetX += delta.X;
            OffsetY += delta.Y;
        }

        public void Reset() {
            Scale = 1;
            OffsetX = 0;
            OffsetY = 0;
        }
    }
}
