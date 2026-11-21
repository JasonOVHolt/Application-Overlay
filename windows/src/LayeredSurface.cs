using System.Drawing.Imaging;
using static OverlayApp.NativeMethods;

namespace OverlayApp;

/// <summary>
/// Off-screen 32-bit image for a per-pixel-alpha (WS_EX_LAYERED) window: draw into it with GDI+, then
/// <see cref="Present"/> it with UpdateLayeredWindow. Used by the crop picker's selection layer.
/// </summary>
internal sealed class LayeredSurface : IDisposable
{
    private IntPtr _dib, _memDc, _bits;
    private Size _size;

    /// <summary>A bitmap over the surface (premultiplied ARGB), resized if needed. Dispose it before presenting.</summary>
    public Bitmap Begin(Size size)
    {
        if (_dib == IntPtr.Zero || _size != size)
        {
            Free();
            var header = new BITMAPINFOHEADER
            {
                biSize = System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size.Width,
                biHeight = -size.Height, // negative: top-down rows, matching GDI+'s layout
                biPlanes = 1,
                biBitCount = 32,
            };
            var screenDc = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(screenDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            _dib = CreateDIBSection(_memDc, ref header, 0, out _bits, IntPtr.Zero, 0);
            SelectObject(_memDc, _dib);
            _size = size;
        }
        return new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, _bits);
    }

    /// <summary>Shows the surface as <paramref name="hwnd"/>'s content at <paramref name="location"/> (screen coordinates).</summary>
    public void Present(IntPtr hwnd, Point location, byte alpha = 255)
    {
        if (_dib == IntPtr.Zero) return;
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            var dst = new POINT(location.X, location.Y);
            var size = new SIZE { cx = _size.Width, cy = _size.Height };
            var src = new POINT(0, 0);
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void Free()
    {
        if (_memDc != IntPtr.Zero) DeleteDC(_memDc);
        if (_dib != IntPtr.Zero) DeleteObject(_dib);
        _memDc = _dib = _bits = IntPtr.Zero;
    }

    public void Dispose() => Free();
}
