using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ApexDiagnostics.Controls
{
    public class LiveGraph : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty =
            DependencyProperty.Register("Values", typeof(IEnumerable<double>), typeof(LiveGraph),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable<double> Values
        {
            get => (IEnumerable<double>)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register("Stroke", typeof(Brush), typeof(LiveGraph),
                new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public static readonly DependencyProperty FillProperty =
            DependencyProperty.Register("Fill", typeof(Brush), typeof(LiveGraph),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register("MaxValue", typeof(double), typeof(LiveGraph),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (Values == null) return;
            var values = Values.ToList();
            if (values.Count < 2) return;

            double width = ActualWidth;
            double height = ActualHeight;

            if (width <= 0 || height <= 0) return;

            double max = double.IsNaN(MaxValue) ? values.Max() : MaxValue;
            if (max <= 0) max = 1;

            double xStep = width / (values.Count - 1);
            
            var points = new List<Point>();
            for (int i = 0; i < values.Count; i++)
            {
                double x = i * xStep;
                double y = height - (Math.Min(max, values[i]) / max * height);
                points.Add(new Point(x, y));
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], false, false);
                context.PolyLineTo(points.Skip(1).ToList(), true, true);
            }

            dc.DrawGeometry(null, new Pen(Stroke, 1.5), geometry);

            if (Fill != null)
            {
                var fillGeometry = new StreamGeometry();
                using (StreamGeometryContext context = fillGeometry.Open())
                {
                    context.BeginFigure(new Point(0, height), true, true);
                    context.LineTo(points[0], true, false);
                    context.PolyLineTo(points.Skip(1).ToList(), true, false);
                    context.LineTo(new Point(width, height), true, false);
                }
                dc.DrawGeometry(Fill, null, fillGeometry);
            }
        }
    }
}
