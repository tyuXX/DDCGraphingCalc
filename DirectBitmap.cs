using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DDCGraphingCalc;

public class DirectBitmap : IDisposable // Thanks SaxxonPike
{
    public DirectBitmap( Size size ) : this( size.Width, size.Height )
    {
    }

    public DirectBitmap( int width, int height )
    {
        Bits = new int[height, width];
        BitsHandle = GCHandle.Alloc( Bits, GCHandleType.Pinned );
        Bitmap = new Bitmap( width, height, width * 4, PixelFormat.Format32bppPArgb, BitsHandle.AddrOfPinnedObject() );
    }

    public Bitmap Bitmap { get; private set; }
    public int[,] Bits { get; }
    public bool Disposed { get; private set; }
    public int Height => Bits.GetLength( 0 );
    public int Width => Bits.GetLength( 1 );
    public Size Size => new( Width, Height );

    protected GCHandle BitsHandle { get; }

    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;
        Bitmap.Dispose();
        Bitmap = null;
        BitsHandle.Free();
    }

    public void SetPixel( int x, int y, Color color )
    {
        Bits[y, x] = color.ToArgb();
    }

    public void SetPixel( int x, int y, int color )
    {
        Bits[y, x] = color;
    }
}