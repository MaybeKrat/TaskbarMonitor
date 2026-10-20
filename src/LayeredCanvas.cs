using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static TaskbarMonitor.Native;

namespace TaskbarMonitor;

/// <summary>Рисует кадр средствами WPF и выводит его в окно с попиксельной прозрачностью.</summary>
internal sealed class LayeredCanvas : IDisposable
{
    RenderTargetBitmap? _target;
    IntPtr _dc, _bitmap, _oldBitmap, _bits;
    int _width, _height;

    public void Present(IntPtr hwnd, int w, int h, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) draw(dc);

        if (_target == null || _target.PixelWidth != w || _target.PixelHeight != h)
            _target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        else
            _target.Clear();
        _target.Render(visual);

        EnsureBitmap(w, h);
        _target.CopyPixels(new Int32Rect(0, 0, w, h), _bits, w * h * 4, w * 4);
        var size = new SIZE { cx = w, cy = h };
        var origin = new POINT();
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(hwnd, IntPtr.Zero, IntPtr.Zero, ref size, _dc, ref origin, 0, ref blend, ULW_ALPHA);
    }

    void EnsureBitmap(int w, int h)
    {
        if (_bitmap != IntPtr.Zero && _width == w && _height == h) return;
        Free();
        var header = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        _dc = CreateCompatibleDC(IntPtr.Zero);
        _bitmap = CreateDIBSection(IntPtr.Zero, ref header, 0, out _bits, IntPtr.Zero, 0);
        _oldBitmap = SelectObject(_dc, _bitmap);
        _width = w;
        _height = h;
    }

    void Free()
    {
        if (_dc != IntPtr.Zero) { SelectObject(_dc, _oldBitmap); DeleteDC(_dc); _dc = IntPtr.Zero; }
        if (_bitmap != IntPtr.Zero) { DeleteObject(_bitmap); _bitmap = IntPtr.Zero; }
    }

    public void Dispose() => Free();
}
