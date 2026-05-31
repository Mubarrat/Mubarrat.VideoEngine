using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

[StructLayout(LayoutKind.Sequential)]
public struct ImageBrush : IBrush, ILerpable<ImageBrush>, IEquatable<ImageBrush>
{
    public int Width;
    public int Height;
    public Memory<Color32> Pixels;

    public ImageBrush(int width, int height, Memory<Color32> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (pixels.Length != width * height) throw new ArgumentException("Pixels length must equal width * height.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public readonly Color32 Sample(double x, double y)
    {
        var span = Pixels.Span;
        if (span.IsEmpty || Width == 0 || Height == 0) return default;
        // Bilinear sample: map 0..1 to 0..(size-1) and interpolate between the four nearest pixels.
        double u = x * (Width - 1), v = y * (Height - 1);
        int ix = (int)Math.Floor(u), iy = (int)Math.Floor(v);
        double fx = u - ix, fy = v - iy;
        ix = Math.Clamp(ix, 0, Width - 1);
        iy = Math.Clamp(iy, 0, Height - 1);
        int ix1 = Math.Clamp(ix + 1, 0, Width - 1), iy1 = Math.Clamp(iy + 1, 0, Height - 1);
        return span[iy * Width + ix].Lerp(span[iy * Width + ix1], fx)
            .Lerp(span[iy1 * Width + ix].Lerp(span[iy1 * Width + ix1], fx), fy);
    }

    public readonly ImageBrush Lerp(in ImageBrush other, double t)
    {
        var spanA = Pixels.Span;
        var spanB = other.Pixels.Span;
        if (spanA.IsEmpty || spanB.IsEmpty) throw new ArgumentException("Cannot lerp empty ImageBrush.", nameof(other));
        if (Width != other.Width || Height != other.Height) throw new ArgumentException("Cannot lerp ImageBrushes of different sizes.", nameof(other));
        var result = new Color32[spanA.Length];
        for (int i = 0; i < result.Length; i++) result[i] = spanA[i].Lerp(spanB[i], t);
        return new ImageBrush(Width, Height, result);
    }

    public readonly bool Equals(ImageBrush other)
    {
        if (Width != other.Width || Height != other.Height) return false;
        var a = Pixels.Span;
        var b = other.Pixels.Span;
        if (a.IsEmpty && b.IsEmpty) return true;
        if (a.IsEmpty || b.IsEmpty) return false;
        return a.SequenceEqual(b);
    }

    public override readonly bool Equals(object? obj) => obj is ImageBrush other && Equals(other);

    public static bool operator ==(ImageBrush left, ImageBrush right) => left.Equals(right);

    public static bool operator !=(ImageBrush left, ImageBrush right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(Width, Height, Pixels.Length);
}
