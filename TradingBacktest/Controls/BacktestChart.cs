using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TradingBacktest.Abstractions;
using TradingBacktest.Models;
using TradingBacktest.Services;

namespace TradingBacktest.Controls
{
    /// <summary>
    /// Draws the backtest result:
    ///   * top pane    - close plus every indicator that shares the price scale, with the
    ///                  buy and sell markers;
    ///   * middle pane - indicators on their own scale (RSI and friends), so an oscillator
    ///                  between 0 and 100 is not squashed against prices in the thousands;
    ///   * bottom pane - the equity curve against buy &amp; hold.
    ///
    /// Implemented as a plain FrameworkElement that renders itself in OnRender, so the
    /// project needs no charting package - which matters, because the whole solution has
    /// to build with Visual Studio 2015 and C# 6.
    ///
    /// The wheel zooms in time around the pointer, the left button pans, and a double click
    /// fits the whole range again. Only the horizontal axis zooms; the price and equity
    /// scales refit to whatever is on screen, which is what makes a zoom worth doing.
    /// A new result resets the view, so a run is never read at a stale zoom level.
    /// </summary>
    public sealed class BacktestChart : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            "Result",
            typeof(BacktestResult),
            typeof(BacktestChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnResultChanged));

        private const double LeftPad = 66;    // price scale labels
        private const double RightPad = 78;   // own-scale labels
        private const double TopPad = 10;
        private const double BottomPad = 20;  // date labels
        private const double PaneGap = 34;    // room for the legend between the panes
        private const double PricePaneShare = 0.58;
        private const double OscillatorPaneShare = 0.18;

        private static readonly Brush BackgroundBrush = Frozen(Color.FromRgb(0xFA, 0xFB, 0xFC));
        private static readonly Brush PaneBrush = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush GridBrush = Frozen(Color.FromRgb(0xE6, 0xEA, 0xEE));
        private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x6B, 0x76, 0x83));
        private static readonly Brush PriceBrush = Frozen(Color.FromRgb(0x37, 0x41, 0x4B));
        private static readonly Brush EquityBrush = Frozen(Color.FromRgb(0x7B, 0x1F, 0xA2));
        private static readonly Brush BenchmarkBrush = Frozen(Color.FromRgb(0x90, 0xA4, 0xAE));
        private static readonly Brush BuyBrush = Frozen(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly Brush SellBrush = Frozen(Color.FromRgb(0xC6, 0x28, 0x28));
        private static readonly Brush TooltipBrush = Frozen(Color.FromArgb(0xF7, 0xFF, 0xFF, 0xFF));

        // Candles: black wicks, and the same green and red the trade markers use, so a
        // rising bar and a buy marker read as the same idea.
        private static readonly Brush WickBrush = Frozen(Color.FromRgb(0x00, 0x00, 0x00));
        private static readonly Pen WickPen = FrozenPen(WickBrush, 1.0);
        private static readonly Brush TooltipBorderBrush = Frozen(Color.FromRgb(0xBF, 0xC7, 0xD0));

        private static readonly Pen PricePen = FrozenPen(PriceBrush, 1.4);
        private static readonly Pen EquityPen = FrozenPen(EquityBrush, 1.8);
        private static readonly Pen GridPen = FrozenPen(GridBrush, 1.0);
        private static readonly Pen BenchmarkPen = FrozenDashedPen(BenchmarkBrush, 1.0);
        private static readonly Pen CrosshairPen = FrozenPen(Frozen(Color.FromRgb(0x90, 0x9C, 0xA8)), 1.0);

        private static readonly Dictionary<Color, Pen> LinePens = new Dictionary<Color, Pen>();
        private static Brush _equityFillBrush;

        private const int MinVisibleBars = 20;
        private const double ZoomStep = 1.18;

        /// <summary>
        /// How much room a bar needs before it is drawn as a candle rather than as part of
        /// the close line. Below this the bodies blur into one another and the line is the
        /// honest picture.
        /// </summary>
        private const double MinCandleBarWidth = 3.0;

        private int _hoverIndex = -1;
        private double _pixelsPerDip = 1.0;

        // The visible slice of bars. The wheel changes _span, dragging changes _firstIndex,
        // and _span of zero means "the whole range". Both are folded back into what the
        // data can show every time the window is recomputed, so a resize, a shorter period
        // or a new result can never leave the chart looking at bars that are not there.
        private int _span;
        private int _firstIndex;
        private bool _dragging;
        private double _dragStartX;
        private int _dragStartFirst;
        private Rect _plot;
        private Rect _pricePane;
        private Rect _oscillatorPane;
        private Rect _equityPane;
        private bool _hasOscillator;
        private bool _candles;
        private double _priceMin;
        private double _priceMax;
        private double _oscillatorMin;
        private double _oscillatorMax;
        private double _equityMin;
        private double _equityMax;

        public BacktestChart()
        {
            ClipToBounds = true;
            Focusable = false;
        }

        public BacktestResult Result
        {
            get { return (BacktestResult)GetValue(ResultProperty); }
            set { SetValue(ResultProperty, value); }
        }

        /// <summary>A new run always shows the whole period, whatever the last one was at.</summary>
        private static void OnResultChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            var chart = source as BacktestChart;
            if (chart != null)
            {
                chart.ResetZoom();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

            BacktestResult result = Result;
            if (ActualWidth < 120 || ActualHeight < 120)
            {
                return;
            }

            if (result == null || result.Candles == null || result.Candles.Count < 2)
            {
                DrawPlaceholder(dc, result == null
                    ? "Set the ticker, the data folder and the period, then press Start."
                    : "Not enough bars in the selected period.");
                return;
            }

            Layout(result);

            // A hover left over from before a zoom or a pan may no longer be on screen.
            if (_hoverIndex < _firstIndex || _hoverIndex >= _windowEnd)
            {
                _hoverIndex = -1;
            }

            Cursor wanted = _span < result.Candles.Count ? Cursors.Hand : Cursors.Arrow;
            if (!ReferenceEquals(Cursor, wanted))
            {
                Cursor = wanted;
            }

            DrawPaneBackground(dc, _pricePane);
            DrawPaneBackground(dc, _equityPane);
            DrawPriceScales(dc, result);
            DrawEquityScales(dc, result);
            DrawDateAxis(dc, result);

            DrawEquityCurve(dc, result);
            DrawPriceCurve(dc, result);
            DrawTrades(dc, result);

            if (_hasOscillator)
            {
                DrawPaneBackground(dc, _oscillatorPane);
                DrawOscillatorPane(dc, result);
            }

            DrawLegend(dc, result);
            DrawZoomHint(dc);
            DrawCrosshair(dc, result);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            Point position = e.GetPosition(this);

            if (_dragging && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                Pan(position);
                e.Handled = true;
                return;
            }

            int index = HitTest(position);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Zooms in time around the pointer, so the bar under the cursor stays where it is.
        /// One notch is <see cref="ZoomStep"/>, about 18% closer or further out.
        /// </summary>
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            Zoom(e.GetPosition(this), e.Delta / 120);
            e.Handled = true;
        }

        /// <summary>
        /// Zooms in time around <paramref name="position"/>, so the bar under the pointer
        /// stays where it is instead of sliding away. <paramref name="notches"/> is positive
        /// for the wheel moving up. Kept apart from the input handler so the arithmetic can
        /// be checked without a mouse.
        /// </summary>
        private void Zoom(Point position, double notches)
        {
            BacktestResult result = Result;
            if (result == null || result.Candles.Count < MinVisibleBars || notches == 0)
            {
                return;
            }

            if (_span < 1)
            {
                ComputeWindow(result);
            }

            double across = _plot.Width > 0 ? Clamp((position.X - _plot.X) / _plot.Width) : 0.5;
            double anchor = _firstIndex + across * (_span - 1);

            double scaled = Math.Round(_span / Math.Pow(ZoomStep, notches));
            int span = (int)Math.Max(
                Math.Min(MinVisibleBars, result.Candles.Count),
                Math.Min(result.Candles.Count, scaled));

            if (span == _span)
            {
                return;  // already as far as it goes; do not shake the window about
            }

            _span = span;
            _firstIndex = (int)Math.Round(anchor - across * (_span - 1));
            ComputeWindow(result);
            _hoverIndex = HitTest(position);
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (e.ClickCount == 2)
            {
                _dragging = false;
                if (IsMouseCaptured)
                {
                    ReleaseMouseCapture();
                }

                ResetZoom();
                e.Handled = true;
                return;
            }

            BacktestResult result = Result;
            if (result == null || result.Candles.Count < MinVisibleBars)
            {
                return;
            }

            if (_span < 1)
            {
                ComputeWindow(result);
            }

            if (_span >= result.Candles.Count)
            {
                return;  // the whole period is already on screen, so there is nothing to drag
            }

            _dragging = true;
            _dragStartX = e.GetPosition(this).X;
            _dragStartFirst = _firstIndex;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_dragging)
            {
                _dragging = false;
                if (IsMouseCaptured)
                {
                    ReleaseMouseCapture();
                }

                e.Handled = true;
            }
        }

        /// <summary>Slides the window by however far the pointer has travelled.</summary>
        private void Pan(Point position)
        {
            BacktestResult result = Result;
            if (result == null || _plot.Width < 1)
            {
                return;
            }

            double travelled = -(position.X - _dragStartX) / _plot.Width * (_span - 1);
            _firstIndex = _dragStartFirst + (int)Math.Round(travelled);
            ComputeWindow(result);
            _hoverIndex = HitTest(position);
            InvalidateVisual();
        }

        /// <summary>Puts the whole period back on screen.</summary>
        private void ResetZoom()
        {
            _span = 0;
            _firstIndex = 0;
            _hoverIndex = -1;
            InvalidateVisual();
        }

        // ------------------------------------------------------------------ layout

        private void Layout(BacktestResult result)
        {
            ComputeWindow(result);

            _plot = new Rect(
                LeftPad,
                TopPad,
                Math.Max(10, ActualWidth - LeftPad - RightPad),
                Math.Max(10, ActualHeight - TopPad - BottomPad));

            // One candle per bar, but only once the bars are wide enough to tell apart.
            _candles = _plot.Width / Math.Max(1, _span - 1) >= MinCandleBarWidth;

            _hasOscillator = false;
            for (int i = 0; i < result.Lines.Count; i++)
            {
                if (!result.Lines[i].UsesPriceScale)
                {
                    _hasOscillator = true;
                    break;
                }
            }

            // Each gap is taken out of the shared height, so the date axis ends up directly
            // under the last pane instead of on top of it.
            int gaps = _hasOscillator ? 3 : 2;
            double usable = Math.Max(20, _plot.Height - PaneGap * gaps);
            double priceHeight = usable * (_hasOscillator ? PricePaneShare : PricePaneShare * 1.15);
            double oscillatorHeight = _hasOscillator ? usable * OscillatorPaneShare : 0;

            _pricePane = new Rect(_plot.X, _plot.Y, _plot.Width, priceHeight);

            double equityY = _plot.Y + priceHeight + PaneGap;
            if (_hasOscillator)
            {
                _oscillatorPane = new Rect(_plot.X, equityY, _plot.Width, oscillatorHeight);
                equityY += oscillatorHeight + PaneGap;
            }

            _equityPane = new Rect(_plot.X, equityY, _plot.Width, _plot.Bottom - equityY);

            ComputePriceRange(result);
            ComputeOscillatorRange(result);
            ComputeEquityRange(result);
        }

        /// <summary>Exclusive index one past the last visible bar.</summary>
        private int _windowEnd
        {
            get { return _firstIndex + _span; }
        }

        /// <summary>
        /// Folds the requested span and offset into what the data can actually show. Called
        /// from every layout, so the window stays valid after a resize, a shorter period or
        /// a new result rather than pointing at bars that are not there.
        /// </summary>
        private void ComputeWindow(BacktestResult result)
        {
            if (result == null)
            {
                _span = 0;
                _firstIndex = 0;
                return;
            }

            int count = result.Candles.Count;
            int requested = _span <= 0 ? count : _span;
            _span = Math.Max(Math.Min(MinVisibleBars, count), Math.Min(count, requested));
            _firstIndex = count > _span ? Math.Max(0, Math.Min(count - _span, _firstIndex)) : 0;
        }

        private void ComputePriceRange(BacktestResult result)
        {
            double min = double.MaxValue;
            double max = double.MinValue;

            for (int i = _firstIndex; i < _windowEnd; i++)
            {
                Candle candle = result.Candles[i];
                if (candle.Low < min)
                {
                    min = candle.Low;
                }

                if (candle.High > max)
                {
                    max = candle.High;
                }
            }

            for (int i = 0; i < result.Lines.Count; i++)
            {
                IndicatorLine line = result.Lines[i];
                if (!line.UsesPriceScale)
                {
                    continue;
                }

                min = Math.Min(min, MinFinite(line.Values, _firstIndex, _windowEnd, double.MaxValue));
                max = Math.Max(max, MaxFinite(line.Values, _firstIndex, _windowEnd, double.MinValue));
            }

            _priceMin = min;
            _priceMax = max;

            if (_priceMax - _priceMin < 1e-9)
            {
                _priceMin -= 1;
                _priceMax += 1;
            }

            double pad = (_priceMax - _priceMin) * 0.06;
            _priceMin -= pad;
            _priceMax += pad;
        }

        private void ComputeOscillatorRange(BacktestResult result)
        {
            if (FindOscillatorRange(result, _firstIndex, _windowEnd))
            {
                return;
            }

            // Nothing of the oscillator is on screen - a zoom into the first bars of the
            // history, where it has no values yet. Fall back to the full period's scale
            // rather than snapping to a flat 0..100 that says nothing.
            FindOscillatorRange(result, 0, result.Candles.Count);
        }

        private bool FindOscillatorRange(BacktestResult result, int from, int to)
        {
            double min = double.MaxValue;
            double max = double.MinValue;

            for (int i = 0; i < result.Lines.Count; i++)
            {
                IndicatorLine line = result.Lines[i];
                if (line.UsesPriceScale)
                {
                    continue;
                }

                min = Math.Min(min, MinFinite(line.Values, from, to, double.MaxValue));
                max = Math.Max(max, MaxFinite(line.Values, from, to, double.MinValue));
            }

            bool found = min <= max;
            if (!found)
            {
                min = 0;
                max = 100;
            }

            // A floor keeps an oscillator readable when it barely moves over the window.
            if (max - min < 1e-9)
            {
                min -= 1;
                max += 1;
            }

            double pad = (max - min) * 0.10;
            _oscillatorMin = min - pad;
            _oscillatorMax = max + pad;
            return found;
        }

        private void ComputeEquityRange(BacktestResult result)
        {
            // Capital stays in the range whatever the zoom, so the break-even line is
            // always on screen to measure against.
            double min = Math.Min(result.InitialCapital, MinFinite(result.Equity, _firstIndex, _windowEnd, double.MaxValue));
            double max = Math.Max(result.InitialCapital, MaxFinite(result.Equity, _firstIndex, _windowEnd, double.MinValue));
            min = Math.Min(min, MinFinite(result.BuyAndHoldEquity, _firstIndex, _windowEnd, double.MaxValue));
            max = Math.Max(max, MaxFinite(result.BuyAndHoldEquity, _firstIndex, _windowEnd, double.MinValue));

            _equityMin = min;
            _equityMax = max;

            if (_equityMax - _equityMin < 1e-9)
            {
                _equityMin -= 1;
                _equityMax += 1;
            }

            double pad = (_equityMax - _equityMin) * 0.12;
            _equityMin -= pad;
            _equityMax += pad;
        }

        // ------------------------------------------------------------------ scales

        /// <summary>
        /// Maps a bar index to a horizontal position. Only the visible window is spread
        /// across the plot, so zooming really does magnify the bars rather than squeezing
        /// them against the edge.
        /// </summary>
        private double X(int index)
        {
            if (_span < 2)
            {
                return _plot.X;
            }

            return _plot.X + _plot.Width * (index - _firstIndex) / (_span - 1);
        }

        private double PriceY(double price)
        {
            double ratio = (_priceMax - price) / (_priceMax - _priceMin);
            return _pricePane.Y + _pricePane.Height * Clamp(ratio);
        }

        private double OscillatorY(double value)
        {
            double ratio = (_oscillatorMax - value) / (_oscillatorMax - _oscillatorMin);
            return _oscillatorPane.Y + _oscillatorPane.Height * Clamp(ratio);
        }

        private double EquityY(double value)
        {
            double ratio = (_equityMax - value) / (_equityMax - _equityMin);
            return _equityPane.Y + _equityPane.Height * Clamp(ratio);
        }

        private int HitTest(Point point)
        {
            BacktestResult result = Result;
            if (result == null || result.Candles.Count < 2)
            {
                return -1;
            }

            if (point.Y < _plot.Y - 12 || point.Y > _plot.Bottom + 12)
            {
                return -1;
            }

            if (_span < 1)
            {
                ComputeWindow(result);
            }

            if (_span < 1 || _plot.Width < 1)
            {
                return -1;
            }

            double ratio = Clamp((point.X - _plot.X) / _plot.Width);
            int index = _firstIndex + (int)Math.Round(ratio * (_span - 1));
            return Math.Max(_firstIndex, Math.Min(_windowEnd - 1, index));
        }

        // ------------------------------------------------------------------ drawing

        private void DrawPlaceholder(DrawingContext dc, string message)
        {
            FormattedText text = Text(message, 13, LabelBrush);
            dc.DrawText(
                text,
                new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
        }

        private void DrawPaneBackground(DrawingContext dc, Rect pane)
        {
            dc.DrawRectangle(PaneBrush, null, pane);
        }

        private void DrawPriceScales(DrawingContext dc, BacktestResult result)
        {
            double step = NiceStep(_priceMax - _priceMin, 5);
            double first = Math.Ceiling(_priceMin / step) * step;

            for (double value = first; value <= _priceMax; value += step)
            {
                double y = PriceY(value);
                dc.DrawLine(GridPen, new Point(_pricePane.X, y), new Point(_pricePane.Right, y));
                DrawText(dc, value.ToString("0.##", CultureInfo.CurrentCulture), 10.5, LabelBrush, _pricePane.X - 6, y, false, true);
            }
        }

        private void DrawEquityScales(DrawingContext dc, BacktestResult result)
        {
            double step = NiceStep(_equityMax - _equityMin, 3);
            double first = Math.Ceiling(_equityMin / step) * step;

            for (double value = first; value <= _equityMax; value += step)
            {
                double y = EquityY(value);
                dc.DrawLine(GridPen, new Point(_equityPane.X, y), new Point(_equityPane.Right, y));
                DrawText(dc, CompactMoney(value), 10.5, LabelBrush, _equityPane.Right + 6, y, false, false);
            }

            // Capital line: the break-even reference for the strategy.
            double baseY = EquityY(result.InitialCapital);
            if (baseY > _equityPane.Y && baseY < _equityPane.Bottom)
            {
                dc.DrawLine(BenchmarkPen, new Point(_equityPane.X, baseY), new Point(_equityPane.Right, baseY));
                DrawText(dc, "capital", 10, LabelBrush, _equityPane.X + 4, baseY - 13, false, false);
            }
        }

        private void DrawDateAxis(DrawingContext dc, BacktestResult result)
        {
            int marks = Math.Min(6, _span);
            double step = marks > 1 ? _plot.Width / (marks - 1) : _plot.Width;

            for (int i = 0; i < marks; i++)
            {
                int index = _firstIndex + (int)Math.Round(i * (_span - 1) / (double)Math.Max(1, marks - 1));
                if (index >= _windowEnd)
                {
                    index = _windowEnd - 1;
                }

                string label = result.Candles[index].Date.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
                double x = X(index);
                bool rightAligned = i == marks - 1;
                DrawText(dc, label, 10.5, LabelBrush, rightAligned ? x - 4 : x, _plot.Bottom + 4, false, rightAligned);
            }
        }

        private void DrawPriceCurve(DrawingContext dc, BacktestResult result)
        {
            if (_candles)
            {
                DrawCandles(dc, result);
            }
            else
            {
                DrawCloseLine(dc, result);
            }

            for (int i = 0; i < result.Lines.Count; i++)
            {
                IndicatorLine line = result.Lines[i];
                if (!line.UsesPriceScale)
                {
                    continue;
                }

                DrawLine(dc, line, PriceY, PenFor(line));
            }
        }

        /// <summary>The close line, for when the bars are too thin to draw as candles.</summary>
        private void DrawCloseLine(DrawingContext dc, BacktestResult result)
        {
            int from = _firstIndex;
            int to = _windowEnd;
            var points = new List<Point>(to - from);
            for (int i = from; i < to; i++)
            {
                points.Add(new Point(X(i), PriceY(result.Candles[i].Close)));
            }

            DrawPolyline(dc, points, PricePen);
        }

        /// <summary>
        /// One candle per visible bar: a black wick from the bar's high to its low, and a
        /// body from open to close, green when the bar closed up and red when it closed
        /// down. A bar that opened and closed at the same price gets a one-pixel body, which
        /// is what a doji should look like.
        /// </summary>
        private void DrawCandles(DrawingContext dc, BacktestResult result)
        {
            double spacing = _plot.Width / Math.Max(1, _span - 1);

            // Bodies narrower than the gap between bars, or neighbours run together and
            // the chart reads as a block of colour rather than as bars.
            double wanted = spacing * 0.62;
            int body = (int)Math.Round(wanted);
            if (body < 1)
            {
                body = 1;
            }

            for (int i = _firstIndex; i < _windowEnd; i++)
            {
                Candle candle = result.Candles[i];
                double x = X(i);

                // Snapped to a whole pixel, or a one-pixel line smears across two of them.
                double wick = Math.Round(x) + 0.5;
                dc.DrawLine(WickPen, new Point(wick, PriceY(candle.High)), new Point(wick, PriceY(candle.Low)));

                double openY = PriceY(candle.Open);
                double closeY = PriceY(candle.Close);
                int left = (int)Math.Round(x) - body / 2;
                int height = (int)Math.Round(Math.Abs(closeY - openY));
                if (height < 1)
                {
                    height = 1;
                }

                dc.DrawRectangle(
                    candle.Close >= candle.Open ? BuyBrush : SellBrush,
                    null,
                    new Rect(left, Math.Min(openY, closeY), body, height));
            }
        }

        private void DrawOscillatorPane(DrawingContext dc, BacktestResult result)
        {
            double step = NiceStep(_oscillatorMax - _oscillatorMin, 2);
            double first = Math.Ceiling(_oscillatorMin / step) * step;

            for (double value = first; value <= _oscillatorMax; value += step)
            {
                double y = OscillatorY(value);
                dc.DrawLine(GridPen, new Point(_oscillatorPane.X, y), new Point(_oscillatorPane.Right, y));
                DrawText(dc, value.ToString("0.#", CultureInfo.CurrentCulture), 10.5, LabelBrush, _oscillatorPane.Right + 6, y, false, false);
            }

            for (int i = 0; i < result.Lines.Count; i++)
            {
                IndicatorLine line = result.Lines[i];
                if (!line.UsesPriceScale)
                {
                    DrawLine(dc, line, OscillatorY, PenFor(line));
                }
            }

            // Guide lines the plug-ins asked for (RSI's 30 and 70, for one). The chart does
            // not know what an RSI is.
            for (int i = 0; i < result.Series.Count; i++)
            {
                IndicatorSeries series = result.Series[i];
                if (series.Lines.Count > 0 && series.Lines[0].UsesPriceScale)
                {
                    continue;
                }

                IList<IndicatorLevel> levels = series.Result.Levels;
                for (int j = 0; j < levels.Count; j++)
                {
                    double y = OscillatorY(levels[j].Value);
                    if (y <= _oscillatorPane.Y || y >= _oscillatorPane.Bottom)
                    {
                        continue;
                    }

                    dc.DrawLine(BenchmarkPen, new Point(_oscillatorPane.X, y), new Point(_oscillatorPane.Right, y));
                    if (levels[j].Label.Length > 0)
                    {
                        DrawText(dc, levels[j].Label, 10, LabelBrush, _oscillatorPane.X + 4, y - 7, false, false);
                    }
                }
            }
        }

        private void DrawLine(DrawingContext dc, IndicatorLine line, Func<double, double> mapY, Pen pen)
        {
            int from = Math.Max(0, _firstIndex);
            int to = Math.Min(line.Values.Length, _windowEnd);
            var points = new List<Point>(to - from);
            for (int i = from; i < to; i++)
            {
                if (line.HasValueAt(i))
                {
                    points.Add(new Point(X(i), mapY(line.Values[i])));
                }
            }

            DrawPolyline(dc, points, pen);
        }

        private void DrawEquityCurve(DrawingContext dc, BacktestResult result)
        {
            int from = _firstIndex;
            int to = _windowEnd;

            var benchmark = new List<Point>(to - from);
            for (int i = from; i < to; i++)
            {
                benchmark.Add(new Point(X(i), EquityY(result.BuyAndHoldEquity[i])));
            }

            DrawPolyline(dc, benchmark, BenchmarkPen);

            var equity = new List<Point>(to - from);
            for (int i = from; i < to; i++)
            {
                equity.Add(new Point(X(i), EquityY(result.Equity[i])));
            }

            // Soft fill under the curve makes the gain/loss area readable at a glance.
            StreamGeometry area = BuildArea(equity, _equityPane.Bottom);
            if (area != null)
            {
                dc.DrawGeometry(EquityFillBrush, null, area);
            }

            DrawPolyline(dc, equity, EquityPen);
        }

        private void DrawTrades(DrawingContext dc, BacktestResult result)
        {
            for (int i = 0; i < result.Trades.Count; i++)
            {
                Trade trade = result.Trades[i];
                if (trade.BarIndex < _firstIndex || trade.BarIndex >= _windowEnd)
                {
                    continue;
                }

                bool isBuy = trade.Side == TradeSide.Buy;
                double x = X(trade.BarIndex);
                double y = isBuy
                    ? PriceY(result.Candles[trade.BarIndex].Low) + 9
                    : PriceY(result.Candles[trade.BarIndex].High) - 9;

                DrawTriangle(dc, new Point(x, y), isBuy, isBuy ? BuyBrush : SellBrush);
            }
        }

        private void DrawLegend(DrawingContext dc, BacktestResult result)
        {
            double x = _pricePane.X + 8;
            double y = _pricePane.Y + 6;
            if (_candles)
            {
                x += DrawCandleLegendEntry(dc, x, y);
            }
            else
            {
                x += DrawLegendEntry(dc, x, y, PricePen, "close");
            }

            for (int i = 0; i < result.Lines.Count; i++)
            {
                IndicatorLine line = result.Lines[i];
                if (!line.UsesPriceScale)
                {
                    continue;
                }

                x += DrawLegendEntry(dc, x, y, PenFor(line), line.Label);
            }

            if (_hasOscillator)
            {
                double ox = _oscillatorPane.X + 8;
                double oy = _oscillatorPane.Y + 6;
                for (int i = 0; i < result.Lines.Count; i++)
                {
                    IndicatorLine line = result.Lines[i];
                    if (line.UsesPriceScale)
                    {
                        continue;
                    }

                    ox += DrawLegendEntry(dc, ox, oy, PenFor(line), line.Label, false, true);
                }
            }

            double ex = _pricePane.X + 8;
            double ey = _equityPane.Y + 6;
            ex += DrawLegendEntry(dc, ex, ey, EquityPen, "equity");
            ex += DrawLegendEntry(dc, ex, ey, BenchmarkPen, "buy & hold");
            ex += DrawLegendEntry(dc, ex, ey, FrozenPen(BuyBrush, 1.2), "buy", true);
            DrawLegendEntry(dc, ex, ey, FrozenPen(SellBrush, 1.2), "sell", true);
        }

        /// <summary>
        /// Draws one legend item and returns the width it used. <paramref name="backing"/>
        /// puts a plate behind the text, which the oscillator band needs because its line
        /// runs right across the top of the pane.
        /// </summary>
        private double DrawLegendEntry(DrawingContext dc, double x, double y, Pen pen, string label, bool markerOnly = false, bool backing = false)
        {
            double start = x;
            dc.DrawLine(pen, new Point(x, y + 6), new Point(x + 14, y + 6));
            x += 19;

            if (markerOnly)
            {
                DrawTriangle(dc, new Point(x + 5, y + 6), label == "buy", pen.Brush);
                x += 16;
            }
            else
            {
                FormattedText text = Text(label, 10.5, LabelBrush);
                if (backing)
                {
                    dc.DrawRectangle(PaneBrush, null, new Rect(x - 2, y - 1, text.Width + 4, text.Height + 2));
                }

                dc.DrawText(text, new Point(x, y));
                x += text.Width + 14;
            }

            return x - start;
        }

        /// <summary>
        /// The legend swatch for candles: one green body and one red, so the key matches
        /// what is on the chart instead of showing a line that is no longer drawn.
        /// </summary>
        private double DrawCandleLegendEntry(DrawingContext dc, double x, double y)
        {
            double start = x;
            dc.DrawRectangle(BuyBrush, null, new Rect(x, y + 1, 4, 10));
            dc.DrawRectangle(SellBrush, null, new Rect(x + 6, y + 1, 4, 10));

            FormattedText text = Text("candles", 10.5, LabelBrush);
            dc.DrawText(text, new Point(x + 19, y));
            return x + 19 + text.Width + 14 - start;
        }

        private void DrawCrosshair(DrawingContext dc, BacktestResult result)
        {
            if (_hoverIndex < 0 || _hoverIndex >= result.Candles.Count)
            {
                return;
            }

            double x = X(_hoverIndex);
            Candle candle = result.Candles[_hoverIndex];
            double priceY = PriceY(candle.Close);
            double equityY = EquityY(result.Equity[_hoverIndex]);

            dc.DrawLine(CrosshairPen, new Point(x, _pricePane.Y), new Point(x, _pricePane.Bottom));
            dc.DrawLine(CrosshairPen, new Point(x, _equityPane.Y), new Point(x, _equityPane.Bottom));
            dc.DrawEllipse(null, FrozenPen(PriceBrush, 2.4), new Point(x, priceY), 3.5, 3.5);
            dc.DrawEllipse(null, FrozenPen(EquityBrush, 2.4), new Point(x, equityY), 3.5, 3.5);

            var lines = new List<string>
            {
                candle.Date.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
                "close  " + NumberParser.FormatMoney(candle.Close)
            };

            for (int i = 0; i < result.Lines.Count; i++)
            {
                IndicatorLine line = result.Lines[i];
                string value = line.HasValueAt(_hoverIndex) ? NumberParser.FormatMoney(line.Values[_hoverIndex]) : "-";
                lines.Add(Pad(line.Label) + " " + value);
            }

            lines.Add("equity " + NumberParser.FormatMoney(result.Equity[_hoverIndex]));

            for (int i = 0; i < result.Trades.Count; i++)
            {
                if (result.Trades[i].BarIndex == _hoverIndex)
                {
                    Trade trade = result.Trades[i];
                    string side = trade.Side == TradeSide.Buy ? "buy" : "sell";
                    string profit = double.IsNaN(trade.Profit)
                        ? string.Empty
                        : "  " + NumberParser.FormatSignedPercent(trade.Profit / (trade.Price * trade.Quantity) * 100.0);
                    lines.Add(side + " " + trade.Quantity + " @ " + NumberParser.FormatMoney(trade.Price) + profit);
                }
            }

            DrawTooltip(dc, lines, x);
        }

        /// <summary>
        /// Says what the mouse does, but only once there is something to do - otherwise the
        /// chart is not advertising an interaction the reader cannot use yet.
        /// </summary>
        private void DrawZoomHint(DrawingContext dc)
        {
            BacktestResult result = Result;
            if (result == null || _span >= result.Candles.Count)
            {
                return;
            }

            string message = _dragging ? "drag to pan" : "wheel to zoom, drag to pan, double-click to fit";
            FormattedText text = Text(message, 10, LabelBrush);
            dc.DrawText(text, new Point(Math.Max(_plot.X, _plot.Right - text.Width - 8), _pricePane.Bottom - text.Height - 4));
        }

        private static string Pad(string text)
        {
            return text.Length >= 11 ? text : text.PadRight(11);
        }

        private void DrawTooltip(DrawingContext dc, List<string> lines, double anchorX)
        {
            var texts = new List<FormattedText>(lines.Count);
            double width = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                FormattedText text = Text(lines[i], 11, i == 0 ? PriceBrush : LabelBrush);
                texts.Add(text);
                width = Math.Max(width, text.Width);
            }

            double boxWidth = width + 18;
            double boxHeight = texts.Count * 15 + 12;
            double x = anchorX + 12;
            if (x + boxWidth > ActualWidth - 4)
            {
                x = anchorX - 12 - boxWidth;
            }

            double y = _pricePane.Y + 24;
            y = Math.Max(4, Math.Min(y, ActualHeight - boxHeight - 4));

            dc.DrawRoundedRectangle(TooltipBrush, new Pen(TooltipBorderBrush, 1), new Rect(x, y, boxWidth, boxHeight), 4, 4);
            for (int i = 0; i < texts.Count; i++)
            {
                dc.DrawText(texts[i], new Point(x + 9, y + 6 + i * 15));
            }
        }

        // ------------------------------------------------------------------ primitives

        private void DrawText(DrawingContext dc, string text, double size, Brush brush, double x, double y, bool centered, bool rightAligned)
        {
            FormattedText formatted = Text(text, size, brush);
            double drawX = x;
            if (centered)
            {
                drawX = x - formatted.Width / 2;
            }
            else if (rightAligned)
            {
                drawX = x - formatted.Width;
            }

            dc.DrawText(formatted, new Point(drawX, y - formatted.Height / 2));
        }

        private FormattedText Text(string text, double size, Brush brush)
        {
            return new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                brush,
                _pixelsPerDip);
        }

        private static Pen PenFor(IndicatorLine line)
        {
            lock (LinePens)
            {
                Pen pen;
                if (LinePens.TryGetValue(line.Color, out pen))
                {
                    return pen;
                }

                var brush = new SolidColorBrush(line.Color);
                brush.Freeze();
                pen = new Pen(brush, 1.4);
                pen.Freeze();
                LinePens[line.Color] = pen;
                return pen;
            }
        }

        private void DrawPolyline(DrawingContext dc, List<Point> points, Pen pen)
        {
            if (points.Count < 2)
            {
                return;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], false, false);
                for (int i = 1; i < points.Count; i++)
                {
                    context.LineTo(points[i], true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        private StreamGeometry BuildArea(List<Point> points, double baseline)
        {
            if (points.Count < 2)
            {
                return null;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(new Point(points[0].X, baseline), true, true);
                for (int i = 0; i < points.Count; i++)
                {
                    context.LineTo(points[i], true, false);
                }

                context.LineTo(new Point(points[points.Count - 1].X, baseline), true, false);
            }

            geometry.Freeze();
            return geometry;
        }

        private static void DrawTriangle(DrawingContext dc, Point center, bool pointUp, Brush fill)
        {
            const double HalfWidth = 5;
            const double Height = 5.5;

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                if (pointUp)
                {
                    context.BeginFigure(new Point(center.X, center.Y - Height), true, true);
                    context.LineTo(new Point(center.X - HalfWidth, center.Y + Height * 0.7), true, false);
                    context.LineTo(new Point(center.X + HalfWidth, center.Y + Height * 0.7), true, false);
                }
                else
                {
                    context.BeginFigure(new Point(center.X, center.Y + Height), true, true);
                    context.LineTo(new Point(center.X - HalfWidth, center.Y - Height * 0.7), true, false);
                    context.LineTo(new Point(center.X + HalfWidth, center.Y - Height * 0.7), true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(fill, null, geometry);
        }

        private static string CompactMoney(double value)
        {
            double abs = Math.Abs(value);
            if (abs >= 1000000)
            {
                return (value / 1000000).ToString("0.#", CultureInfo.CurrentCulture) + "M";
            }

            if (abs >= 10000)
            {
                return (value / 1000).ToString("0.#", CultureInfo.CurrentCulture) + "K";
            }

            return value.ToString("0", CultureInfo.CurrentCulture);
        }

        private static double Clamp(double value)
        {
            return value < 0 ? 0 : (value > 1 ? 1 : value);
        }

        private static double MinFinite(double[] values, int from, int to, double fallback)
        {
            double min = fallback;
            for (int i = Math.Max(0, from); i < Math.Min(values.Length, to); i++)
            {
                double value = values[i];
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value < min)
                {
                    min = value;
                }
            }

            return min;
        }

        private static double MaxFinite(double[] values, int from, int to, double fallback)
        {
            double max = fallback;
            for (int i = Math.Max(0, from); i < Math.Min(values.Length, to); i++)
            {
                double value = values[i];
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        private static double MinFinite(IList<double> values, int from, int to, double fallback)
        {
            double min = fallback;
            for (int i = Math.Max(0, from); i < Math.Min(values.Count, to); i++)
            {
                double value = values[i];
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value < min)
                {
                    min = value;
                }
            }

            return min;
        }

        private static double MaxFinite(IList<double> values, int from, int to, double fallback)
        {
            double max = fallback;
            for (int i = Math.Max(0, from); i < Math.Min(values.Count, to); i++)
            {
                double value = values[i];
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        private static double MinFinite(double[] values, double fallback)
        {
            double min = fallback;
            for (int i = 0; i < values.Length; i++)
            {
                double value = values[i];
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value < min)
                {
                    min = value;
                }
            }

            return min;
        }

        private static double MaxFinite(double[] values, double fallback)
        {
            double max = fallback;
            for (int i = 0; i < values.Length; i++)
            {
                double value = values[i];
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        private static double MinFinite(IList<double> values, double fallback)
        {
            double min = fallback;
            for (int i = 0; i < values.Count; i++)
            {
                double value = values[i];
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value < min)
                {
                    min = value;
                }
            }

            return min;
        }

        private static double MaxFinite(IList<double> values, double fallback)
        {
            double max = fallback;
            for (int i = 0; i < values.Count; i++)
            {
                double value = values[i];
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        /// <summary>Rounds a raw tick spacing to 1, 2 or 5 times a power of ten.</summary>
        private static double NiceStep(double range, int targetTicks)
        {
            if (range <= 0 || targetTicks <= 0)
            {
                return 1.0;
            }

            double raw = range / targetTicks;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double normalized = raw / magnitude;

            double step;
            if (normalized <= 1)
            {
                step = 1;
            }
            else if (normalized <= 2)
            {
                step = 2;
            }
            else if (normalized <= 5)
            {
                step = 5;
            }
            else
            {
                step = 10;
            }

            return step * magnitude;
        }

        private static Brush EquityFillBrush
        {
            get
            {
                if (_equityFillBrush == null)
                {
                    var gradient = new LinearGradientBrush();
                    gradient.StartPoint = new Point(0, 0);
                    gradient.EndPoint = new Point(0, 1);
                    gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x7B, 0x1F, 0xA2), 0));
                    gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0x7B, 0x1F, 0xA2), 1));
                    gradient.Freeze();
                    _equityFillBrush = gradient;
                }

                return _equityFillBrush;
            }
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }

        private static Pen FrozenDashedPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness);
            pen.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
            pen.Freeze();
            return pen;
        }
    }
}
