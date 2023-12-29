using Loyc;
using Loyc.Syntax;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace DDCGraphingCalc;

using number = Double; // Change this line to make a calculator for a different data type 

internal class CalcRange
{
    public number Hi;
    public number Lo;
    public int PxCount;

    public number StepSize;

    // Generate a constructor and three public fields
    public CalcRange( number lo, number hi, int pxCount )
    {
        Lo = lo;
        Hi = hi;
        PxCount = pxCount;
        StepSize = (Hi - Lo) / Math.Max( PxCount - 1, 1 );
    }

    public number ValueToPx( number value )
    {
        return (value - Lo) / (Hi - Lo) * PxCount;
    }

    public number PxToValue( int px )
    {
        return (number)px / PxCount * (Hi - Lo) + Lo;
    }

    public number PxToDelta( int px )
    {
        return (number)px / PxCount * (Hi - Lo);
    }

    public CalcRange DraggedBy( int dPx )
    {
        return new CalcRange( Lo - PxToDelta( dPx ), Hi - PxToDelta( dPx ), PxCount );
    }

    public CalcRange ZoomedBy( number ratio )
    {
        double mid = (Hi + Lo) / 2, halfSpan = (Hi - Lo) * ratio / 2;
        return new CalcRange( mid - halfSpan, mid + halfSpan, PxCount );
    }
}

// "alt class" generates an entire class hierarchy with base class CalculatorCore and 
// read-only fields. Each "alternative" (derived class) is marked with the word "alt".
internal abstract class CalculatorCore
{
    private static readonly Symbol sy_x = (Symbol)"x", sy_y = (Symbol)"y";

    private static readonly Random _r = new();

    // Base class constructor and fields
    public CalculatorCore( LNode Expr, Dictionary<Symbol, LNode> Vars, CalcRange XRange )
    {
        this.Expr = Expr;
        this.Vars = Vars;
        this.XRange = XRange;
    }

    public LNode Expr { get; }
    public Dictionary<Symbol, LNode> Vars { get; }
    public CalcRange XRange { get; }

    [EditorBrowsable( EditorBrowsableState.Never )]
    public LNode Item1 => Expr;

    [EditorBrowsable( EditorBrowsableState.Never )]
    public Dictionary<Symbol, LNode> Item2 => Vars;

    [EditorBrowsable( EditorBrowsableState.Never )]
    public CalcRange Item3 => XRange;

    public object Results { get; protected set; }
    public abstract CalculatorCore WithExpr( LNode newValue );
    public abstract CalculatorCore WithVars( Dictionary<Symbol, LNode> newValue );
    public abstract CalculatorCore WithXRange( CalcRange newValue );

    public abstract object Run();
    public abstract number? GetValueAt( int x, int y );

    public static CalculatorCore New( LNode expr, Dictionary<Symbol, LNode> vars, CalcRange xRange, CalcRange yRange )
    {
        // Find out if the expression uses the variable "y" (or is an equation with '=' or '==')
        // As an (unnecessary) side effect, this throws if an unreferenced var is used
        bool isEquation = expr.Calls( CodeSymbols.Assign, 2 ) || expr.Calls( CodeSymbols.Eq, 2 ), usesY = false;
        if (!isEquation)
        {
            LNode zero = LNode.Literal( (double)0 );
            Func<Symbol, double> lookup = null;
            lookup = name => name == sy_x || (usesY |= name == sy_y) ? 0 : Eval( vars[name], lookup );
            Eval( expr, lookup );
        }

        if (isEquation || usesY)
        {
            return new Calculator3D( expr, vars, xRange, yRange );
        }

        return new Calculator2D( expr, vars, xRange );
    }

    // Parse the list of variables provided in the GUI
    public static Dictionary<Symbol, LNode> ParseVarList( IEnumerable<LNode> varList )
    {
        var vars = new Dictionary<Symbol, LNode>();
        foreach (var assignment in varList)
        {
            {
                LNode expr, var;
                if (assignment.Calls( CodeSymbols.Assign, 2 ) && (var = assignment.Args[0]) != null &&
                    (expr = assignment.Args[1]) != null)
                {
                    if (!var.IsId)
                    {
                        throw new ArgumentException( "Left-hand side of '=' must be a variable name: {0}"
                            .Localized( var ) );
                    }

                    // For efficiency, try to evaluate the expression in advance
                    try
                    {
                        expr = LNode.Literal( Eval( expr, vars ) );
                    }
                    catch
                    {
                    } // it won't work if expression uses X or Y

                    vars.Add( var.Name, expr );
                }
                else
                {
                    throw new ArgumentException( "Expected assignment expression: {0}".Localized( assignment ) );
                }
            }
            ;
        }

        return vars;
    }

    public static number Eval( LNode expr, Dictionary<Symbol, LNode> vars )
    {
        Func<Symbol, number> lookup = null;
        lookup = name => Eval( vars[name], lookup );
        return Eval( expr, lookup );
    }

    // Evaluates an expression
    public static number Eval( LNode expr, Func<Symbol, number> lookup )
    {
        if (expr.IsLiteral)
        {
            if (expr.Value is number value)
            {
                return value;
            }

            return Convert.ToDouble( expr.Value );
        }

        if (expr.IsId)
        {
            return lookup( expr.Name );
        }

        // expr must be a function or operator
        if (expr.ArgCount == 2)
        {
            LNode a, b, hi, lo, tmp_10, tmp_11 = null;
            if (expr.Calls( CodeSymbols.Add, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) + Eval( b, lookup );
            }

            if (expr.Calls( CodeSymbols.Mul, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) * Eval( b, lookup );
            }

            if (expr.Calls( CodeSymbols.Sub, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) - Eval( b, lookup );
            }

            if (expr.Calls( CodeSymbols.Div, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) / Eval( b, lookup );
            }

            if (expr.Calls( CodeSymbols.Mod, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) % Eval( b, lookup );
            }

            if (expr.Calls( CodeSymbols.Exp, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Math.Pow( Eval( a, lookup ), Eval( b, lookup ) );
            }

            if (expr.Calls( CodeSymbols.Shr, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return G.ShiftRight( Eval( a, lookup ), (int)Eval( b, lookup ) );
            }

            if (expr.Calls( CodeSymbols.Shl, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return G.ShiftLeft( Eval( a, lookup ), (int)Eval( b, lookup ) );
            }

            if (expr.Calls( CodeSymbols.GT, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) > Eval( b, lookup ) ? 1 : (number)0;
            }

            if (expr.Calls( CodeSymbols.LT, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) < Eval( b, lookup ) ? 1 : (number)0;
            }

            if (expr.Calls( CodeSymbols.GE, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) >= Eval( b, lookup ) ? 1 : (number)0;
            }

            if (expr.Calls( CodeSymbols.LE, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) <= Eval( b, lookup ) ? 1 : (number)0;
            }

            if (expr.Calls( CodeSymbols.Eq, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) == Eval( b, lookup ) ? 1 : (number)0;
            }

            if (expr.Calls( CodeSymbols.Neq, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) != Eval( b, lookup ) ? 1 : (number)0;
            }

            if (expr.Calls( CodeSymbols.AndBits, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return (long)Eval( a, lookup ) & (long)Eval( b, lookup );
            }

            if (expr.Calls( CodeSymbols.OrBits, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return (long)Eval( a, lookup ) | (long)Eval( b, lookup );
            }

            if (expr.Calls( CodeSymbols.NullCoalesce, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                var a2 = Eval( a, lookup );
                return double.IsNaN( a2 ) | double.IsInfinity( a2 ) ? Eval( b, lookup ) : a2;
            }

            if ((expr.Calls( CodeSymbols.And, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null) ||
                (expr.Calls( (Symbol)"'and", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null))
            {
                return Eval( a, lookup ) != 0 ? Eval( b, lookup ) : 0;
            }

            if ((expr.Calls( CodeSymbols.Or, 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null) ||
                (expr.Calls( (Symbol)"'or", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null))
            {
                return Eval( a, lookup ) == 0 ? Eval( b, lookup ) : 1;
            }

            if (expr.Calls( (Symbol)"'xor", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Eval( a, lookup ) != 0 != (Eval( b, lookup ) != 0) ? 1 : (number)0;
            }

            if (expr.Calls( (Symbol)"xor", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return (long)Eval( a, lookup ) ^ (long)Eval( b, lookup );
            }

            if (expr.Calls( (Symbol)"min", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Math.Min( Eval( a, lookup ), Eval( b, lookup ) );
            }

            if (expr.Calls( (Symbol)"max", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Math.Max( Eval( a, lookup ), Eval( b, lookup ) );
            }

            if ((expr.Calls( (Symbol)"mod", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null) ||
                (expr.Calls( (Symbol)"'MOD", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null))
            {
                return Mod( Eval( a, lookup ), Eval( b, lookup ) );
            }

            if (expr.Calls( (Symbol)"atan", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Math.Atan2( Eval( a, lookup ), Eval( b, lookup ) );
            }

            if (expr.Calls( (Symbol)"log", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return Math.Log( Eval( a, lookup ), Eval( b, lookup ) );
            }

            if (expr.Calls( (Symbol)"'in", 2 ) && (a = expr.Args[0]) != null && (tmp_10 = expr.Args[1]) != null &&
                tmp_10.Calls( CodeSymbols.Tuple, 2 ) && (lo = tmp_10.Args[0]) != null && (hi = tmp_10.Args[1]) != null)
            {
                return Eval( a, lookup ).IsInRange( Eval( lo, lookup ), Eval( hi, lookup ) ) ? 1 : (number)0;
            }

            if ((expr.Calls( (Symbol)"'clamp", 2 ) && (a = expr.Args[0]) != null && (tmp_11 = expr.Args[1]) != null &&
                 tmp_11.Calls( CodeSymbols.Tuple, 2 ) && (lo = tmp_11.Args[0]) != null &&
                 (hi = tmp_11.Args[1]) != null) || (expr.Calls( (Symbol)"clamp", 3 ) && (a = expr.Args[0]) != null &&
                                                    (lo = expr.Args[1]) != null && (hi = expr.Args[2]) != null))
            {
                return Eval( a, lookup ).PutInRange( Eval( lo, lookup ), Eval( hi, lookup ) );
            }

            if ((expr.Calls( (Symbol)"'P", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null) ||
                (expr.Calls( (Symbol)"P", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null))
            {
                return P( (int)Math.Round( Eval( a, lookup ) ), (int)Math.Round( Eval( b, lookup ) ) );
            }

            if ((expr.Calls( (Symbol)"'C", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null) ||
                (expr.Calls( (Symbol)"C", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null))
            {
                return C( (ulong)Math.Round( Eval( a, lookup ) ), (ulong)Math.Round( Eval( b, lookup ) ) );
            }
        }

        {
            LNode a, b, c, tmp_12;
            if (expr.Calls( CodeSymbols.Sub, 1 ) && (a = expr.Args[0]) != null)
            {
                return -Eval( a, lookup );
            }

            if (expr.Calls( CodeSymbols.Add, 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Abs( Eval( a, lookup ) );
            }

            if (expr.Calls( CodeSymbols.Not, 1 ) && (a = expr.Args[0]) != null)
            {
                return Eval( a, lookup ) == 0 ? 1 : (number)0;
            }

            if (expr.Calls( CodeSymbols.NotBits, 1 ) && (a = expr.Args[0]) != null)
            {
                return ~(long)Eval( a, lookup );
            }

            if (expr.Calls( CodeSymbols.QuestionMark, 2 ) && (c = expr.Args[0]) != null &&
                (tmp_12 = expr.Args[1]) != null && tmp_12.Calls( CodeSymbols.Colon, 2 ) && (a = tmp_12.Args[0]) != null &&
                (b = tmp_12.Args[1]) != null)
            {
                return Eval( c, lookup ) != 0 ? Eval( a, lookup ) : Eval( b, lookup );
            }

            if (expr.Calls( (Symbol)"square", 1 ) && (a = expr.Args[0]) != null)
            {
                var n = Eval( a, lookup );
                return n * n;
            }

            if (expr.Calls( (Symbol)"sqrt", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Sqrt( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"sin", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Sin( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"cos", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Cos( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"tan", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Tan( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"asin", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Asin( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"acos", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Acos( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"atan", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Atan( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"sec", 1 ) && (a = expr.Args[0]) != null)
            {
                return 1 / Math.Cos( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"csc", 1 ) && (a = expr.Args[0]) != null)
            {
                return 1 / Math.Sin( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"cot", 1 ) && (a = expr.Args[0]) != null)
            {
                return 1 / Math.Tan( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"exp", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Exp( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"ln", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Log( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"log", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Log10( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"ceil", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Ceiling( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"floor", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Floor( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"sign", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Sign( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"abs", 1 ) && (a = expr.Args[0]) != null)
            {
                return Math.Abs( Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"rnd", 0 ))
            {
                return _r.NextDouble();
            }

            if (expr.Calls( (Symbol)"rnd", 1 ) && (a = expr.Args[0]) != null)
            {
                return _r.Next( (int)Eval( a, lookup ) );
            }

            if (expr.Calls( (Symbol)"rnd", 2 ) && (a = expr.Args[0]) != null && (b = expr.Args[1]) != null)
            {
                return _r.Next( (int)Eval( a, lookup ), (int)Eval( b, lookup ) );
            }

            if (expr.Calls( (Symbol)"fact", 1 ) && (a = expr.Args[0]) != null)
            {
                return Factorial( Eval( a, lookup ) );
            }
        }
        throw new ArgumentException( "Expression not understood: {0}".Localized( expr ) );
    }

    private static double Mod( double x, double y )
    {
        var m = x % y;
        return m + (m < 0 ? y : 0);
    }

    private static double Factorial( double n )
    {
        return n <= 1 ? 1 : n * Factorial( n - 1 );
    }

    private static double P( int n, int k )
    {
        return k <= 0 ? 1 : k > n ? 0 : n * P( n - 1, k - 1 );
    }

    private static double C( ulong n, ulong k )
    {
        if (k > n)
        {
            return 0;
        }

        k = Math.Min( k, n - k );
        double result = 1;
        for (ulong d = 1; d <= k; ++d)
        {
            result *= n--;
            result /= d;
        }

        return result;
    }
}

// Derived class for 2D graphing calculator
internal class Calculator2D : CalculatorCore
{
    private static readonly Symbol sy_x = (Symbol)"x";

    public Calculator2D( LNode Expr, Dictionary<Symbol, LNode> Vars, CalcRange XRange )
        : base( Expr, Vars, XRange )
    {
    }

    public override CalculatorCore WithExpr( LNode newValue )
    {
        return new Calculator2D( newValue, Vars, XRange );
    }

    public override CalculatorCore WithVars( Dictionary<Symbol, LNode> newValue )
    {
        return new Calculator2D( Expr, newValue, XRange );
    }

    public override CalculatorCore WithXRange( CalcRange newValue )
    {
        return new Calculator2D( Expr, Vars, newValue );
    }

    public override object Run()
    {
        var results = new number[XRange.PxCount];
        var x = XRange.Lo;

        Func<Symbol, number> lookup = null;
        lookup = name => name == sy_x ? x : Eval( Vars[name], lookup );

        for (var i = 0; i < results.Length; i++)
        {
            results[i] = Eval( Expr, lookup );
            x += XRange.StepSize;
        }

        return Results = results;
    }

    public override number? GetValueAt( int x, int _ )
    {
        var tmp_14 = (uint)x;
        var r = (number[])Results;
        return
            tmp_14 < (uint)r.Length ? r[x] : null;
    }
}

// Derived class for pseudo-3D and "equation" calculator
internal class Calculator3D : CalculatorCore
{
    private static readonly Symbol sy_x = (Symbol)"x", sy_y = (Symbol)"y";

    public Calculator3D( LNode Expr, Dictionary<Symbol, LNode> Vars, CalcRange XRange, CalcRange YRange )
        : base( Expr, Vars, XRange )
    {
        this.YRange = YRange;
    }

    public CalcRange YRange { get; }

    [EditorBrowsable( EditorBrowsableState.Never )]
    public CalcRange Item4 => YRange;

    public bool EquationMode { get; private set; }

    public override CalculatorCore WithExpr( LNode newValue )
    {
        return new Calculator3D( newValue, Vars, XRange, YRange );
    }

    public override CalculatorCore WithVars( Dictionary<Symbol, LNode> newValue )
    {
        return new Calculator3D( Expr, newValue, XRange, YRange );
    }

    public override CalculatorCore WithXRange( CalcRange newValue )
    {
        return new Calculator3D( Expr, Vars, newValue, YRange );
    }

    public Calculator3D WithYRange( CalcRange newValue )
    {
        return new Calculator3D( Expr, Vars, XRange, newValue );
    }

    public override object Run()
    {
        {
            var Expr_13 = Expr;
            LNode L, R;
            if ((Expr_13.Calls( CodeSymbols.Assign, 2 ) && (L = Expr_13.Args[0]) != null &&
                 (R = Expr_13.Args[1]) != null) || (Expr_13.Calls( CodeSymbols.Eq, 2 ) && (L = Expr_13.Args[0]) != null &&
                                                    (R = Expr_13.Args[1]) != null))
            {
                EquationMode = true;
                var results = RunCore( LNode.Call( CodeSymbols.Sub, LNode.List( L, R ) ).SetStyle( NodeStyle.Operator ), true );
                var results2 = new number[results.GetLength( 0 ) - 1, results.GetLength( 1 ) - 1];
                for (var i = 0; i < results.GetLength( 0 ) - 1; i++)
                    for (var j = 0; j < results.GetLength( 1 ) - 1; j++)
                    {
                        var sign = Math.Sign( results[i, j] );
                        if (sign == 0 || sign != Math.Sign( results[i + 1, j] ) ||
                            sign != Math.Sign( results[i, j + 1] ) ||
                            sign != Math.Sign( results[i + 1, j + 1] ))
                        {
                            results2[i, j] = 1;
                        }
                        else
                        {
                            results2[i, j] = 0;
                        }
                    }

                return Results = results2;
            }

            EquationMode = Expr.ArgCount == 2 && Expr.Name.IsOneOf(
                CodeSymbols.GT, CodeSymbols.LT, CodeSymbols.GE, CodeSymbols.LE, CodeSymbols.Neq, CodeSymbols.And,
                CodeSymbols.Or );
            return Results = RunCore( Expr, false );
        }
    }

    public number[,] RunCore( LNode expr, bool difMode )
    {
        var results = new number
            [YRange.PxCount + (difMode ? 1 : 0), XRange.PxCount + (difMode ? 1 : 0)];
        number x = XRange.Lo, startx = x;
        var y = YRange.Lo;
        if (difMode)
        {
            x -= XRange.StepSize / 2;
            y -= YRange.StepSize / 2;
        }

        Func<Symbol, number> lookup = null;
        lookup = name => name == sy_x ? x : name == sy_y ? y : Eval( Vars[name], lookup );

        for (var yi = 0; yi < results.GetLength( 0 ); yi++, x = startx)
        {
            for (var xi = 0; xi < results.GetLength( 1 ); xi++)
            {
                results[yi, xi] = Eval( expr, lookup );
                x += XRange.StepSize;
            }

            y += YRange.StepSize;
        }

        return results;
    }

    public override number? GetValueAt( int x, int y )
    {
        var tmp_15 = (uint)x;
        var r = (number[,])Results;
        return
            tmp_15 < (uint)r.GetLength( 1 ) &&
            (uint)y < (uint)r.GetLength( 0 )
                ? r[y, x]
                : null;
    }
}