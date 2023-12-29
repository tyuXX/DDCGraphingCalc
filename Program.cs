using System;
using System.Windows.Forms;

namespace DDCGraphingCalc;

internal static class Program
{
    [STAThread]
    private static void Main() // Entry point
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault( false );
        Application.Run( new CalcForm() );
    }
}