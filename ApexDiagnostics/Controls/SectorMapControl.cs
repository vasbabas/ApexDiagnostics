using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ApexDiagnostics.Controls
{
    public class SectorMapControl : FrameworkElement
    {
        public static readonly DependencyProperty TotalSectorsProperty =
            DependencyProperty.Register("TotalSectors", typeof(long), typeof(SectorMapControl),
                new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

        public long TotalSectors
        {
            get => (long)GetValue(TotalSectorsProperty);
            set => SetValue(TotalSectorsProperty, value);
        }

        public static readonly DependencyProperty ScannedSectorsProperty =
            DependencyProperty.Register("ScannedSectors", typeof(long), typeof(SectorMapControl),
                new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

        public long ScannedSectors
        {
            get => (long)GetValue(ScannedSectorsProperty);
            set => SetValue(ScannedSectorsProperty, value);
        }

        public static readonly DependencyProperty MapDataProperty =
            DependencyProperty.Register("MapData", typeof(byte[]), typeof(SectorMapControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public byte[]? MapData
        {
            get => (byte[])GetValue(MapDataProperty);
            set => SetValue(MapDataProperty, value);
        }

        private const int MapSize = 100; 
        private long _lastRedrawTick;

        public void UpdateSector(long sector, byte status)
        {
            if (TotalSectors <= 0 || MapData == null) return;

            int index = (int)((double)sector / TotalSectors * MapSize);
            if (index >= 0 && index < MapSize)
            {
                if (status > MapData[index])
                {
                    MapData[index] = status;
                    
                    // Throttle redraws
                    long now = DateTime.Now.Ticks;
                    if (now - _lastRedrawTick > 1000000) // 100ms
                    {
                        _lastRedrawTick = now;
                        InvalidateVisual();
                    }
                }
            }
        }

        public void Reset()
        {
            if (MapData != null)
            {
                Array.Clear(MapData, 0, MapData.Length);
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double width = ActualWidth;
            double height = ActualHeight;

            if (width < 10 || height < 10) return;

            int cols = 10;
            int rows = 10;

            double cellW = width / cols;
            double cellH = height / rows;

            if (MapData == null) return;

            var brushes = new Brush[]
            {
                new SolidColorBrush(Color.FromRgb(33, 38, 45)),   // Unscanned (Grey)
                new SolidColorBrush(Color.FromRgb(63, 185, 80)),  // Good (Green)
                new SolidColorBrush(Color.FromRgb(227, 179, 65)), // Slow (Yellow)
                new SolidColorBrush(Color.FromRgb(240, 136, 62)), // Delayed (Orange)
                new SolidColorBrush(Color.FromRgb(248, 81, 73)),  // Weak (LightRed)
                new SolidColorBrush(Color.FromRgb(139, 0, 0)),    // Bad (DarkRed)
                new SolidColorBrush(Color.FromRgb(188, 140, 255)) // Timeout (Purple)
            };

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    
                    // Check if this block is currently being scanned or completed
                    long blockStartSector = (long)((double)idx / MapSize * TotalSectors);
                    long blockEndSector = (long)((double)(idx + 1) / MapSize * TotalSectors);
                    
                    bool isScanning = TotalSectors > 0 && ScannedSectors >= blockStartSector && ScannedSectors < blockEndSector;
                    bool isCompleted = TotalSectors > 0 && ScannedSectors >= blockEndSector;

                    // If overall scan is completely done
                    if (TotalSectors > 0 && ScannedSectors >= TotalSectors)
                    {
                        isCompleted = true;
                        isScanning = false;
                    }

                    byte status = 0; // Default: Unscanned
                    if (idx < MapData.Length)
                    {
                        byte realStatus = MapData[idx];
                        if (isCompleted)
                        {
                            status = realStatus;
                        }
                        else if (isScanning)
                        {
                            // If a slow/weak/bad/timeout status occurred, show it immediately.
                            // Otherwise keep it grey (0) so it doesn't color green prematurely.
                            status = realStatus > 1 ? realStatus : (byte)0;
                        }
                    }

                    if (status >= brushes.Length) status = (byte)(brushes.Length - 1);

                    dc.DrawRectangle(brushes[status], null, new Rect(c * cellW, r * cellH, cellW - 1, cellH - 1));
                    
                    if (isScanning)
                    {
                        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(88, 166, 255)), 1.5), new Rect(c * cellW, r * cellH, cellW - 1, cellH - 1));
                    }
                }
            }
        }
    }
}
