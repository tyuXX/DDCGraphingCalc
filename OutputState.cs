using Loyc;
using Loyc.Collections;
using Loyc.Syntax;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;

namespace DDCGraphingCalc;

// Holds output data and output bitmap, and performs rendering
internal class OutputState
{
    private static readonly Color[] SeriesColors =
    {
        Color.DarkGreen, Color.Teal, Color.MediumBlue, Color.MediumPurple, Color.DeepPink, Color.Orange, Color.Brown,
        Color.Black,
        Color.LawnGreen, Color.DarkTurquoise, Color.DodgerBlue, Color.Fuchsia, Color.Red, Color.Salmon, Color.PeachPuff
    };

    private static readonly Color[] ColorBands =
    {
        Color.Orange, Color.Red, Color.Fuchsia, Color.RoyalBlue, Color.White, Color.Goldenrod, Color.Lime, Color.Blue,
        Color.Black
    };

    private static Color[] HeatColors;
    public DirectBitmap Bitmap;
    public List<CalculatorCore> Calcs;

    public Font Font = new( FontFamily.GenericSansSerif, 10 );
    public GraphRange XRange, YRange, ZRange;

    public OutputState( List<CalculatorCore> calcs, GraphRange xRange, GraphRange yRange, GraphRange zRange,
        DirectBitmap bitmap )
    {
        Calcs = calcs;
        XRange = xRange;
        YRange = yRange;
        ZRange = zRange;
        Bitmap = bitmap;
    }

    internal void RenderAll()
    {
        using (var g = Graphics.FromImage( Bitmap.Bitmap ))
        {
            g.Clear( Color.White );

            // Y-range can be autodetected only if there are no "3D" functions already using Y
            if (YRange.AutoRange && Calcs.All( c => c.Results is double[] ))
            {
                YRange.Lo = -0.5;
                YRange.Hi = 1;
                Calcs.ForEach( c => FindMinMax( (double[])c.Results, ref YRange.Lo, ref YRange.Hi ) );
            }

            // Draw at most one "heat map"
            var heatMap = Calcs.FirstOrDefault( c => (c as Calculator3D)?.EquationMode == false );
            if (heatMap != null)
            {
                MaybeChooseAutoZRange( ZRange, (double[,])heatMap.Results );
                RenderXYFunc( (double[,])heatMap.Results, false, Color.Transparent );
            }

            var seriesIndex = 0;
            foreach (var calc in Calcs)
            {
                RenderSeries( calc, seriesIndex++, g );
            }

            DrawGridLines( g );
        }
    }

    private void RenderSeries( CalculatorCore calc, int seriesIndex, Graphics g )
    {
        sbyte[,] bins = null;
        using (var pen = MakePen( calc.Expr, seriesIndex ))
        {
            if (calc.Results is double[] doubles)
            {
                RenderXFunc( g, doubles, pen );
            }
            else
            {
                var data = (double[,])calc.Results;
                if (((Calculator3D)calc).EquationMode)
                {
                    RenderXYFunc( data, true, pen.Color );
                }
                else
                {
                    bins = bins ?? new sbyte[data.GetLength( 0 ), data.GetLength( 1 )];
                    double lo, interval = ChooseGridSpacing( out lo, ZRange, data );
                    RenderContourLines( data, bins, pen.Color, lo, interval );
                    DrawText( g, "Contour interval: {0}".Localized( interval ), Bitmap.Width / 2, 5, pen.Color,
                        StringAlignment.Center, StringAlignment.Near );
                }
            }
        }
    }

    private SizeF DrawText( Graphics g, string text, float x, float y, Color color, StringAlignment hAlign,
        StringAlignment vAlign )
    {
        using (Brush brush = new SolidBrush( color ))
        {
            var size = g.MeasureString( text, Font );
            x = Align( x, size.Width, hAlign );
            y = Align( y, size.Height, vAlign );
            using (var rectbrush = new SolidBrush( Color.FromArgb( 128, Color.White ) ))
            {
                g.FillRectangle( rectbrush, x, y, size.Width, size.Height );
            }

            g.DrawString( text, Font, Brushes.White, x - 1, y );
            g.DrawString( text, Font, Brushes.White, x + 1, y );
            g.DrawString( text, Font, Brushes.White, x, y - 1 );
            g.DrawString( text, Font, Brushes.White, x, y + 1 );
            g.DrawString( text, Font, brush, x, y );
            return size;
        }
    }

    private static float Align( float x, float size, StringAlignment align )
    {
        return align == StringAlignment.Center ? x - size / 2 :
            align == StringAlignment.Far ? x - size : x;
    }

    internal static Pen MakePen( LNode expr, int seriesIndex = -1 ) // -1 = axis line
    {
        float lineWidth = (seriesIndex + 1) % 3 + 1;
        DashStyle dash = DashStyle.Solid, dash_;
        var color = Color.MidnightBlue;
        if (seriesIndex > -1)
        {
            color = SeriesColors[seriesIndex % SeriesColors.Length];
        }

        foreach (var attr in expr.Attrs.Where( a => a.IsId ))
        // Try to interpret identifier as a color or dash style
        {
            if (!TryInterpretAsColor( attr, ref color ))
            {
                if (Enum.TryParse( attr.Name.Name, true, out dash_ ))
                {
                    dash = dash_;
                }
            }
        }

        if (seriesIndex > -1)
        {
            foreach (var attr in expr.Attrs.Where( a => a.IsLiteral ))
            // Interpret literal as a line width
            {
                lineWidth = Convert.ToInt32( attr.Value, null );
            }
        }

        return new Pen( color, lineWidth ) { DashStyle = dash, EndCap = LineCap.DiamondAnchor };
    }

    internal static bool TryInterpretAsColor( LNode attr, ref Color color )
    {
        if (attr.IsId)
        {
            var p = typeof( Color ).GetProperty( attr.Name.Name,
                BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Static );
            if (p != null)
            {
                color = (Color)p.GetMethod.Invoke( null, null );
                return true;
            }
        }

        return false;
    }

    private void RenderXFunc( Graphics g, double[] data, Pen pen )
    {
        if (data.Length == 0 || YRange.Lo >= YRange.Hi)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var points = new PointF[data.Length];
        int x;
        for (x = 0; x < data.Length; x++)
        {
            var y = (float)(YRange.PxCount - 1 - YRange.ValueToPx( data[x] ));
            points[x] = new PointF( x, y );
        }

        DrawLinesSafe( g, pen, points );
    }

    // Same as g.DrawLines(), except it ignores NaN/inf instead of throwing
    private void DrawLinesSafe( Graphics g, Pen pen, PointF[] points )
    {
        int x, start = 0;
        for (x = 0; x < points.Length; x++)
            if (float.IsNaN( points[x].Y ) || float.IsInfinity( points[x].Y ))
            {
                if (x > start)
                {
                    DrawLinesWorkaround( g, pen, points.Slice( start, x - start ).ToArray() );
                }

                start = x + 1;
            }

        if (x > start)
        {
            DrawLinesWorkaround( g, pen, points.Slice( start ).ToArray() );
        }
    }

    // Same as g.DrawLines(), except it works and doesn't throw when given a 
    // single point or large values of Y
    private void DrawLinesWorkaround( Graphics g, Pen pen, PointF[] points )
    {
        for (var i = 0; i < points.Length; i++)
        {
            if (points[i].Y > 10000000)
            {
                points[i].Y = 10000000;
            }

            if (points[i].Y < -10000000)
            {
                points[i].Y = -10000000;
            }
        }

        if (points.Length == 1)
        {
            g.DrawLines( pen, new[] { points[0], new PointF( points[0].X + 0.1f, points[0].Y ) } );
        }
        else if (points.Length > 1)
        {
            g.DrawLines( pen, points );
        }
    }

    private void RenderXYFunc( double[,] data, bool booleanMode, Color trueColor )
    {
        if (HeatColors == null)
        {
            HeatColors = new Color[(ColorBands.Length - 1) * 16];
            for (var band = 0; band < ColorBands.Length - 1; band++)
            {
                Color lo = ColorBands[band], hi = ColorBands[band + 1];
                for (var hii = 0; hii < 16; hii++)
                {
                    var loi = 16 - hii;
                    HeatColors[band * 16 + hii] = Color.FromArgb( (lo.R * loi + hi.R * hii) >> 4,
                        (lo.G * loi + hi.G * hii) >> 4,
                        (lo.B * loi + hi.B * hii) >> 4 );
                }
            }
        }

        var nanColor = Color.FromArgb( trueColor.R / 2 + 64, trueColor.G / 2 + 64, trueColor.B / 2 + 64 );
        for (var y = 0; y < data.GetLength( 0 ); y++)
            for (var x = 0; x < data.GetLength( 1 ); x++)
            {
                var d = data[y, x];
                if (!booleanMode)
                {
                    ZRange.PxCount = HeatColors.Length;
                    var z = ZRange.ValueToPx( data[y, x] );
                    Color c;
                    if (double.IsNaN( z ))
                    {
                        c = Color.DarkGray; // which is lighter than "Gray"
                    }
                    else if (double.IsInfinity( z ))
                    {
                        c = Color.Purple;
                    }
                    else
                    {
                        var z2 = (int)ZRange.ValueToPx( data[y, x] ).PutInRange( 0, HeatColors.Length - 1 );
                        c = HeatColors[z2];
                    }

                    Bitmap.SetPixel( x, Bitmap.Height - 1 - y, c );
                }
                else if (!(d == 0))
                {
                    Bitmap.SetPixel( x, Bitmap.Height - 1 - y, double.IsNaN( d ) ? nanColor : trueColor );
                }
            }
    }

    private void RenderContourLines( double[,] data, sbyte[,] bins, Color color, double lo, double interval )
    {
        if (color.A == 0)
        {
            return; // Transparent: user turned off contour lines
        }

        var frequency = 1.0 / interval;
        int w = data.GetLength( 1 ), h = data.GetLength( 0 );
        for (var y = 0; y < data.GetLength( 0 ); y++)
            for (var x = 0; x < data.GetLength( 1 ); x++)
            {
                var d = data[y, x];
                bins[y, x] = (sbyte)(d < ZRange.Lo ? -2 : d >= ZRange.Hi ? 127 : (int)Math.Floor( (d - lo) * frequency ));
            }

        for (var y = 0; y < data.GetLength( 0 ) - 1; y++)
            for (var x = 0; x < data.GetLength( 1 ) - 1; x++)
            {
                var b = bins[y, x];
                if (b != bins[y, x + 1] || b != bins[y + 1, x] || b != bins[y + 1, x + 1])
                {
                    Bitmap.SetPixel( x, YRange.PxCount - 1 - y, color );
                }
            }
    }

    private static double Min( double a, double b )
    {
        return double.IsNaN( a ) ? b : a > b ? b : a;
    }

    private static double Max( double a, double b )
    {
        return double.IsNaN( a ) ? b : a < b ? b : a;
    }

    private static void FindMinMax( double[] data, ref double min, ref double max )
    {
        foreach (var d in data)
        {
            if (!double.IsInfinity( d ))
            {
                min = Min( min, d );
                max = Max( max, d );
            }
        }
    }

    private static void FindMinMax( double[,] data, ref double min, ref double max )
    {
        // Seems like there ought to be a way to do this in fewer LoC.
        // Better to have used jagged arrays?
        for (var y = 0; y < data.GetLength( 0 ); y++)
            for (var x = 0; x < data.GetLength( 1 ); x++)
            {
                var d = data[y, x];
                if (!double.IsInfinity( d ))
                {
                    min = Min( min, d );
                    max = Max( max, d );
                }
            }
    }

    internal string GetMouseOverText( Point pt )
    {
        int x = pt.X, y = YRange.PxCount - 1 - pt.Y;
        var xval = XRange.PxToValue( x );
        var yval = YRange.PxToValue( y );
        if (Calcs.Count == 1)
        {
            var val = Calcs[0].GetValueAt( x, y );
            if (val != null)
            {
                if (Calcs[0] is Calculator3D)
                {
                    return "{0} @ ({1:G4}, {2:G4})".Localized( val.Value, xval, yval );
                }

                return "{0} @ X = {1:G8}".Localized( val.Value, xval );
            }
        }
        else if (Calcs.Count >= 2)
        {
            var fmt = Calcs.Count == 2 ? "G8" : "G5";
            return "{1} @ X = {0:G4}".Localized( xval,
                string.Join( "; ", Calcs.Select( c => (c.GetValueAt( x, y ) ?? double.NaN).ToString( fmt ) ) ) );
        }

        return "";
    }

    #region Interval detection and grid line drawing

    private static double ChooseGridSpacing( out double lo, GraphRange range, double[,] data = null )
    {
        lo = range.Lo;
        MaybeChooseAutoZRange( range, data );
        return ChooseGridSpacing( ref lo, range.Hi, range.RoughLineCount );
    }

    private static double ChooseGridSpacing( ref double lo, double hi, int roughLines )
    {
        var dif = hi - lo;
        if (dif <= 0)
        {
            return 1; // avoid unexpected results from negative range
        }

        // Start with an interval that is too large, and reduce it until we have enough lines.
        var interval = Math.Pow( 10, Math.Ceiling( Math.Log10( dif ) ) );
        var third = 0;
        for (double roughLinesNow = 1; roughLinesNow < roughLines; third = (third + 1) % 3)
        {
            // interval is multiplied cumulatively by 0.1 every three steps
            var ratio = third == 2 ? 0.4 : 0.5;
            interval *= ratio;
            roughLinesNow /= ratio;
            if (interval > dif)
            {
                roughLinesNow = 1;
            }
        }

        lo = Math.Ceiling( lo / interval ) * interval;
        return interval;
    }

    private static void MaybeChooseAutoZRange( GraphRange range, double[,] data )
    {
        if (data != null && range.AutoRange)
        {
            range.Lo = double.NaN;
            range.Hi = double.NaN;
            FindMinMax( data, ref range.Lo, ref range.Hi );
            if (double.IsNaN( range.Lo ))
            {
                range.Lo = -1;
                range.Hi = 1;
            }
        }
    }

    private void DrawGridLines( Graphics g )
    {
        // Draw vertical lines based on X axis
        double xLo, xInterval = ChooseGridSpacing( out xLo, XRange );
        for (var x = xLo; x <= XRange.Hi; x += xInterval)
        {
            var px = (float)XRange.ValueToPx( x );
            g.DrawLine( XRange.LinePen, px, 0, px, YRange.PxCount );
        }

        if (XRange.Lo <= 0 && 0 <= XRange.Hi)
        {
            var px = (float)XRange.ValueToPx( 0 );
            g.DrawLine( XRange.AxisPen, px, 0, px, YRange.PxCount );
        }

        // Draw horizontal lines based on Y axis
        double yLo, yInterval = ChooseGridSpacing( out yLo, YRange );
        for (var y = yLo; y <= YRange.Hi; y += yInterval)
        {
            var px = YRange.PxCount - 1 - (float)YRange.ValueToPx( y );
            g.DrawLine( YRange.LinePen, 0, px, XRange.PxCount, px );
        }

        if (YRange.Lo <= 0 && 0 <= YRange.Hi)
        {
            var px = YRange.PxCount - 1 - (float)YRange.ValueToPx( 0 );
            g.DrawLine( YRange.AxisPen, 0, px, XRange.PxCount, px );
        }

        // Draw numeric labels
        float lastWidth = 0, lastPx = -1000;
        for (var x = xLo; x <= XRange.Hi; x += xInterval)
        {
            var px = (float)XRange.ValueToPx( x );
            if (px > 20 && px - lastPx > lastWidth)
            {
                lastWidth = DrawText( g, x.ToString( "G7" ), px, Bitmap.Height - 4, XRange.AxisPen.Color,
                    StringAlignment.Center, StringAlignment.Far ).Width;
                lastPx = px;
            }
        }

        for (var y = yLo; y <= YRange.Hi; y += yInterval)
        {
            var px = Bitmap.Height - 1 - (float)YRange.ValueToPx( y );
            if (px > 10 && px < Bitmap.Height - 40)
            {
                DrawText( g, y.ToString( "G7" ), 4, px, YRange.AxisPen.Color, StringAlignment.Near,
                    StringAlignment.Center );
            }
        }
    }

    #endregion
}

internal class GraphRange : CalcRange
{
    public bool AutoRange;
    public Pen AxisPen;
    public string Label;
    public Pen LinePen;
    public LNode RangeExpr;
    public int RoughLineCount = 20;

    public GraphRange( double lo, double hi, int pxCount, Pen pen, string label ) : base( lo, hi, pxCount )
    {
        AxisPen = pen;
        Label = label ?? "";
        LinePen = new Pen( Color.FromArgb( 128, AxisPen.Color ), 1f ) { DashStyle = pen.DashStyle };
        AutoRange = lo >= hi;
    }

    public static GraphRange New( string rangeName, LNode range, int numPixels, Dictionary<Symbol, LNode> varDict )
    {
        if (range == null)
        {
            return new GraphRange( -1, 1, numPixels, Pens.MidnightBlue, null ) { AutoRange = true };
        }

        if (range.Calls( CodeSymbols.Colon, 2 ) && range[0].IsId &&
            string.Compare( range[0].Name.Name, rangeName, true ) == 0)
        {
            range = range[1]; // ignore axis prefix like "x:" or "y:"
        }

        if (range.Calls( CodeSymbols.Sub, 2 ) || range.Calls( CodeSymbols.DotDot, 2 ))
        {
            var lo = CalculatorCore.Eval( range[0], varDict );
            var hi = CalculatorCore.Eval( range[1], varDict );
            var pen = OutputState.MakePen( range );
            var label = range.Attrs.Select( a => a.Value as string ).FirstOrDefault( s => s != null );

            var result = new GraphRange( lo, hi, numPixels, pen, label ) { RangeExpr = range };

            foreach (var attr in range.Attrs)
            {
                if (attr.Value is int value)
                {
                    result.RoughLineCount = value;
                }
            }

            return result;
        }

        throw new FormatException( "Invalid range for {axis}: {range}".Localized( "axis", rangeName, "range", range ) );
    }
}