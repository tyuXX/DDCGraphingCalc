using DDCGraphingCalc.Properties;
using Loyc;
using Loyc.Collections;
using Loyc.Syntax;
using Loyc.Syntax.Les;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DDCGraphingCalc;

public partial class CalcForm : Form
{
    private readonly BackgroundWorker _bw = new();

    private bool _dragging;
    private Point _dragStartPoint;
    private CalcRange _originalXRange, _originalYRange;
    private string _originalZRange, _originalRanges;
    private OutputState _outState; // current data, ranges, and bitmap used by GUI thread
    private DirectBitmap _prevBitmap; // bitmap used in most recent frame (often same as current)

    private bool _refreshRequested;

    public CalcForm()
    {
        InitializeComponent();

        // Prepare to calculate and draw on a background thread
        _bw.DoWork += ( s, e ) =>
        {
            var state = (OutputState)(e.Result = e.Argument);
            try
            {
                foreach (var c in state.Calcs)
                {
                    c.Run(); // do calculations
                }

                state.RenderAll();
            }
            catch
            {
                if (state.Bitmap != _outState?.Bitmap)
                {
                    state.Bitmap.Dispose(); // the new bitmap won't be used
                }

                throw;
            }
        };
        _bw.RunWorkerCompleted += ( s, e ) =>
        {
            if (panelError.Visible = e.Error != null)
            {
                txtError.Text = e.Error.Message;
            }
            else
            {
                _outState = (OutputState)e.Result;
                if (_prevBitmap != _outState.Bitmap)
                {
                    using (_prevBitmap)
                    {
                        _prevBitmap = _outState.Bitmap;
                    }
                }

                graphPanel.Image = _prevBitmap.Bitmap;
            }

            if (_refreshRequested)
            {
                _refreshRequested = false;
                RefreshDisplay();
            }
        };
        MouseWheel += MW;
    }

    private void MW( object obj, MouseEventArgs e )
    {
        if (e.Delta > 0)
        {
            Zoom( 1 / Math.Sqrt( 2 ) );
        }
        else
        {
            Zoom( Math.Sqrt( 2 ) );
        }
    }

    private void CalcForm_Load( object sender, EventArgs e )
    {
        // Load settings
        SetUpComboBox( cbFormulas, "Formulas", "sin(x) + x**2/10 - 1   // Winding road\n"
                                              + "sqrt(x**2+y**2) - cos(atan(y,x) * 5); // Flower\n"
                                              + "@navy x^2+y^2<=4^2;  @goldenrod y < 7-x^2 && y>(x<1.3 ? -((x-1.3)^2)/4 : - ((x-1.3)^2)); @black sqrt(x**2+(y-3)**2)*1.5 - cos(atan(y-3,x)*5-1.4)/2<1; // Starfleet\n"
                                              + "x^2+y^2==4^2   // Circle\n"
                                              + "(x**2+y**2-1)**3 == x**2*y**3 // Heart\n"
                                              + "@purple rnd()*abs(x*y)<0.25  // Noisy star\n"
                                              + "x^2+y^2 < 4 || (+y < 0.5 && +x < 4) || (+y < 5 && +x in (4,5))  // Tie fighter\n"
                                              + "4 % x\n" + "\n" );
        SetUpComboBox( cbVariables, "Variables", "x=1\n"
                                                + "x=1; r=sqrt(x**2+y**2); theta=mod(atan(y,x),tau)" );
        SetUpComboBox( cbRanges, "Ranges", "-10..10;\n"
                                          + "-5..5; \n"
                                          + "-2..2; \n"
                                          + "-10..10; -5..10 \n"
                                          + "-5..5; -5..5 \n"
                                          + "-2..2; -2..2 \n"
                                          + "-1..1; -1..1 " );

        cbFormulas.SelectAll();

        RefreshDisplay();
    }

    private void SetUpComboBox( ComboBox comboBox, string cfgSection, string defaultList )
    {
        var savedData = Settings.Default[cfgSection]?.ToString();
        if (string.IsNullOrEmpty( savedData ))
        {
            savedData = defaultList;
        }

        comboBox.Items.Clear();
        foreach (var item in savedData!.Split( '\n' ))
        {
            comboBox.Items.Add( item );
        }

        comboBox.Text = comboBox.Items.Cast<string>().FirstOrDefault() ?? string.Empty;

        comboBox.KeyPress += ( s, e ) =>
        {
            if (e.KeyChar == '\r')
            {
                AddHistory( ref comboBox, comboBox.Text, true );
                Settings.Default.Save();
            }
        };
        comboBox.Resize += ( s, e ) =>
        {
            // Workaround for ComboBox bug (Text changes on resize!)
            comboBox.Text = comboBox.Tag as string ?? comboBox.Text;
        };
        comboBox.TextChanged += ( s, e ) =>
        {
            // Part of workaround for the first ComboBox bug
            RefreshDisplay();
            // Another ComboBox bug/oddity: when AddHistory removes old temp item, 
            // Text temporarily becomes string.Empty. Fix by calling AddHistory after refresh
            AddHistory( ref comboBox, comboBox.Text, false );
            lblMouseOver.Visible = false; // Mouseover info out of date; hide it until mouse moves
        };
        // Mouse wheel events stupidly go to the control with focus
        comboBox.MouseWheel += ( s, e ) =>
        {
            Zoom( Math.Pow( 2, Math.Ceiling( e.Delta / 120.0 ) / 2 ) );
            ((HandledMouseEventArgs)e).Handled = true; // why is the type of e wrong?
        };
    }

    private static void AddHistory( ref ComboBox comboBox, string text, bool permanent )
    {
        text = text.TrimEnd();
        if (permanent)
        {
            var i = comboBox.Items.IndexOf( text );
            if (i > -1)
            {
                comboBox.Items.RemoveAt( i );
            }
        }
        else
        {
            text += " "; // Mark first item "temporary" by adding a space after the text
        }

        if (comboBox.Items.Count > 0)
        {
            if (comboBox.Items[0].ToString() == text)
            {
                return; // avoid a weird glitch in cbRanges where Text becomes empty
            }

            if (comboBox.Items[0].ToString().EndsWith( " " ))
            {
                comboBox.Items.RemoveAt( 0 );
            }
        }

        comboBox.Items.Insert( 0, text );

        while (comboBox.Items.Count > 100)
            comboBox.Items.RemoveAt( 100 );
    }

    private void CalcForm_FormClosed( object sender, FormClosedEventArgs e )
    {
        SaveComboBox( cbFormulas, "Formulas" );
        SaveComboBox( cbVariables, "Variables" );
        SaveComboBox( cbRanges, "Ranges", false );
        Settings.Default.Save();
    }

    private void SaveComboBox( ComboBox comboBox, string cfgSection, bool saveTextAsPermanent = true )
    {
        AddHistory( ref comboBox, comboBox.Text, saveTextAsPermanent );
        Settings.Default[cfgSection] = string.Join( "\n", comboBox.Items.Cast<string>() );
    }

    private OutputState PrepareCalculators()
    {
        try
        {
            // Parse the three combo boxes and build a dictionary of variables
            var exprs = ParseExprs( "Formula", cbFormulas.Text );
            var variables = ParseExprs( "Variables", string.Format( CultureInfo.InvariantCulture,
                "pi={0};tau={1};e={2};phi=1.6180339887498948; {3}", Math.PI, Math.PI * 2, Math.E, cbVariables.Text ) );
            var ranges = ParseExprs( "Range", cbRanges.Text );
            var varDict = CalculatorCore.ParseVarList( variables );

            // Get display range.
            var size = graphPanel.ClientSize;
            LNode xRangeExpr = ranges.TryGet( 0, null )!, yRangeExpr = ranges.TryGet( 1, null )!;
            var xRange = GraphRange.New( "x", xRangeExpr, size.Width, varDict );
            var yRange = GraphRange.New( "y", yRangeExpr ?? xRangeExpr, size.Height, varDict );
            var zRange = GraphRange.New( "z", ranges.TryGet( 2, null )!, 0, varDict );
            yRange.AutoRange = yRangeExpr == null; // For functions of x only; ignored if y is used

            var calcs = exprs.Select( e => CalculatorCore.New( e, varDict, xRange, yRange ) ).ToList();

            panelError.Visible = false;

            return new OutputState( calcs, xRange, yRange, zRange, _prevBitmap );
        }
        catch (Exception exc)
        {
            ShowError( $"(Immediate) {exc.Message}" );
            return null;
        }
    }

    private void RefreshDisplay()
    {
        var os = PrepareCalculators();
        if (os != null)
        {
            // Refresh result label immediately
            string resultText = null;
            try
            {
                foreach (var c in os.Calcs)
                {
                    resultText = resultText == null ? string.Empty : resultText + ", ";
                    resultText += CalculatorCore.Eval( c.Expr, c.Vars );
                }

                txtResult.Enabled = true;
            }
            catch (Exception e)
            {
                if (resultText == null)
                {
                    resultText = e.Message;
                }

                txtResult.Enabled = false;
            }

            txtResult.Text = resultText;

            if (_bw.IsBusy)
            {
                _refreshRequested = true;
                return;
            }

            os.Bitmap = new DirectBitmap( graphPanel.ClientSize );

            _bw.RunWorkerAsync( os );
        }
    }

    private void ShowError( string error )
    {
        if (panelError.Visible = error != null)
        {
            txtError.Text = error;
        }
    }

    private CalcRange DecodeRange( string rangeName, LNode range, int width )
    {
        if (range == null)
        {
            return new CalcRange( -1, 1, width );
        }

        if (range.Calls( CodeSymbols.Colon, 2 ) && range[0].IsId &&
            String.Compare( range[0].Name.Name, rangeName, StringComparison.OrdinalIgnoreCase ) == 0)
        {
            range = range[1];
        }

        if (range.Calls( CodeSymbols.Sub, 2 ) || range.Calls( CodeSymbols.DotDot, 2 ))
        {
            return new CalcRange( Convert.ToDouble( range[0].Value ),
                Convert.ToDouble( range[1].Value ), width );
        }

        if (range.Calls( "'..-", 2 ))
        {
            return new CalcRange( Convert.ToDouble( range[0].Value ),
                -Convert.ToDouble( range[1].Value ), width );
        }

        throw new FormatException( "Invalid range for {axis}: {range}".Localized( "axis", rangeName, "range", range ) );
    }

    private static List<LNode> ParseExprs( string fieldName, string text )
    {
        // Separate things like *- into two separate operators (* -), and change ^ to **
        text = Regex.Replace( text, @"([-+*/%^&|<>=?.])([-~!+])", "$1 $2" );
        text = Regex.Replace( text, @"\^", "**" );

        var errorHandler = MessageSink.FromDelegate( ( severity, ctx, fmt, args ) =>
        {
            if (severity >= Severity.Error)
            {
                var msg = fieldName + ": " + fmt.Localized( args );
                if (ctx is SourceRange range)
                {
                    msg += $"\r\n{text}\r\n{new string( '-', range.Start.Column - 1 )}^";
                }

                throw new LogException( severity, ctx, msg );
            }
        } );
        return Les3LanguageService.Value.Parse( text, errorHandler ).ToList();
    }

    private void picPanel_Resize( object sender, EventArgs e )
    {
        RefreshDisplay();
    }

    private void picPanel_MouseDown( object sender, MouseEventArgs e )
    {
        if (_outState != null)
        {
            _dragging = graphPanel.Capture = e.Button == MouseButtons.Left;
            _dragStartPoint = e.Location;
            _originalRanges = cbRanges.Text;
            _originalXRange = _outState.XRange;
            _originalYRange = _outState.YRange;
            _originalZRange = _outState.ZRange.RangeExpr?.Range.SourceText.ToString() ?? string.Empty;
        }
    }

    private void picPanel_MouseMove( object sender, MouseEventArgs e )
    {
        if (_dragging)
        {
            lblMouseOver.Visible = false;
            var newX = _originalXRange.DraggedBy( e.Location.X - _dragStartPoint.X );
            var newY = _originalYRange.DraggedBy( -(e.Location.Y - _dragStartPoint.Y) );
            SetRanges( newX, newY, _originalZRange );
        }
        else
        {
            lblMouseOver.Text = _outState?.GetMouseOverText( e.Location ) ?? string.Empty;
            lblMouseOver.Visible = lblMouseOver.Text.Length != 0;
        }
    }

    private void picPanel_MouseUp( object sender, MouseEventArgs e )
    {
        _dragging = graphPanel.Capture = false;
    }

    private void picPanel_MouseLeave( object sender, EventArgs e )
    {
        lblMouseOver.Visible = false;
        if (_dragging)
        {
            _dragging = false;
            cbRanges.Text = _originalRanges;
        }
    }

    private void btnZoomIn_Click( object sender, EventArgs e )
    {
        Zoom( 1 / Math.Sqrt( 2 ) );
    }

    private void btnZoomOut_Click( object sender, EventArgs e )
    {
        Zoom( Math.Sqrt( 2 ) );
    }

    private void btnCopy_Click( object sender, EventArgs e )
    {
        if (graphPanel.Image != null && cbFormulas.Text != null)
        {
            Clipboard.SetImage( graphPanel.Image );
            Clipboard.SetText( cbFormulas.Text );
        }
    }

    private void SetRanges( CalcRange xRange, CalcRange yRange, string zRangeText )
    {
        // Refreshes display as side effect
        var newRanges = string.Format( CultureInfo.InvariantCulture, "{0:G8}..{1:G8}; {2:G8}..{3:G8}; {4}",
            xRange.Lo, xRange.Hi, yRange.Lo, yRange.Hi, zRangeText );
        Trace.WriteLine( newRanges );
        cbRanges.Text = newRanges;
    }

    private void Zoom( double ratio )
    {
        if (_outState != null)
        {
            var zRange = _outState.ZRange.RangeExpr?.Range.SourceText.ToString() ?? string.Empty;
            var newX = _outState.XRange.ZoomedBy( ratio );
            var newY = _outState.YRange.ZoomedBy( ratio );
            SetRanges( newX, newY, zRange );
        }
    }
}