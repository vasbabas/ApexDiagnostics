using System;
using System.Windows;
using System.Windows.Media;

namespace ApexDiagnostics.Controls
{
    public class CircularGauge : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register("MaxValue", typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register("Stroke", typeof(Brush), typeof(CircularGauge),
                new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(CircularGauge),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register("Unit", typeof(string), typeof(CircularGauge),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0) return;

            double cx = width / 2;
            double cy = height / 2;
            double radius = Math.Min(width, height) / 2 - 10;
            if (radius <= 0) return;

            // Draw Sci-Fi Outer Calibration Ring
            var penOuterRule = new Pen(new SolidColorBrush(Color.FromArgb(45, 88, 166, 255)), 1);
            DrawArc(dc, cx, cy, radius + 6, 130, 410, penOuterRule);

            // Draw Background Track Arc (270 degrees total, from 135 to 405)
            var penBg = new Pen(new SolidColorBrush(Color.FromRgb(21, 26, 33)), 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            DrawArc(dc, cx, cy, radius, 135, 405, penBg);

            // Draw Value Arc
            double valPercent = Math.Max(0.0, Math.Min(1.0, Value / MaxValue));
            double endAngle = 135 + 270 * valPercent;

            if (valPercent > 0.01)
            {
                // Draw Subtle Neon Vector Glow underneath active arc
                var glowBrush = Stroke.Clone();
                glowBrush.Opacity = 0.28;
                var penGlow = new Pen(glowBrush, 14) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                DrawArc(dc, cx, cy, radius, 135, endAngle, penGlow);

                // Draw Primary Progress Arc
                var penVal = new Pen(Stroke, 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                DrawArc(dc, cx, cy, radius, 135, endAngle, penVal);
            }

            // Draw Text
            var typeFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            
            // Value + Unit
            string textStr = $"{Value:F0}{Unit}";
            var formattedVal = new FormattedText(
                textStr,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeFace,
                24,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(formattedVal, new Point(cx - formattedVal.Width / 2, cy - formattedVal.Height / 2 - 8));

            // Label
            var formattedLabel = new FormattedText(
                Label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                10,
                new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(10, width - 24),
                TextAlignment = TextAlignment.Center
            };

            dc.DrawText(formattedLabel, new Point(cx - formattedLabel.MaxTextWidth / 2, cy + 12));
        }

        private void DrawArc(DrawingContext dc, double cx, double cy, double radius, double startAngle, double endAngle, Pen pen)
        {
            double startRad = startAngle * Math.PI / 180;
            double endRad = endAngle * Math.PI / 180;

            var startPt = new Point(cx + radius * Math.Cos(startRad), cy + radius * Math.Sin(startRad));
            var endPt = new Point(cx + radius * Math.Cos(endRad), cy + radius * Math.Sin(endRad));

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(startPt, false, false);
                ctx.ArcTo(endPt, new Size(radius, radius), 0, (endAngle - startAngle) > 180, SweepDirection.Clockwise, true, false);
            }
            dc.DrawGeometry(null, pen, geom);
        }
    }
}
