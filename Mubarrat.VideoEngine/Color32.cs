using System.Numerics;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
public struct Color32 : ILerpable<Color32>, IEquatable<Color32>
{
    [FieldOffset(0)] public byte B;
    [FieldOffset(1)] public byte G;
    [FieldOffset(2)] public byte R;
    [FieldOffset(3)] public byte A;

    [FieldOffset(0)] public uint Value;

    public Color32(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }

    public Color32(uint value) => Value = value;

    public readonly Color32 Lerp(in Color32 target, double t) => new(R.Lerp(target.R, t), G.Lerp(target.G, t), B.Lerp(target.B, t), A.Lerp(target.A, t));

    public static void LerpVector(ReadOnlySpan<Color32> source, ReadOnlySpan<Color32> target, Span<Color32> result, ReadOnlySpan<double> t)
    {
        if (target.Length != result.Length) throw new ArgumentException("Target and result spans must have the same length.");
        int vectorSize = Vector<double>.Count;
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= target.Length - vectorSize; i += vectorSize)
            {
                Color32[] sources = source.Slice(i, vectorSize).ToArray();
                Color32[] targets = target.Slice(i, vectorSize).ToArray();
                Vector<double>
                    r = new(Array.ConvertAll(sources, c => c.R)),
                    g = new(Array.ConvertAll(sources, c => c.G)),
                    b = new(Array.ConvertAll(sources, c => c.B)),
                    a = new(Array.ConvertAll(sources, c => c.A)),
                    rt = new(Array.ConvertAll(targets, c => c.R)),
                    gt = new(Array.ConvertAll(targets, c => c.G)),
                    bt = new(Array.ConvertAll(targets, c => c.B)),
                    at = new(Array.ConvertAll(targets, c => c.A)),
                    tv = new(t),
                    rr = Vector.FusedMultiplyAdd(rt - r, tv, r),
                    gr = Vector.FusedMultiplyAdd(gt - g, tv, g),
                    br = Vector.FusedMultiplyAdd(bt - b, tv, b),
                    ar = Vector.FusedMultiplyAdd(at - a, tv, a);
                for (int j = 0; j < vectorSize; j++)
                    result[i + j] = new Color32((byte)rr[j], (byte)gr[j], (byte)br[j], (byte)ar[j]);
            }
        }
        for (; i < target.Length; i++) result[i] = source[i].Lerp(target[i], t[i]);
    }

    public static Color32 operator *(Color32 c, double t) => new((byte)(c.R * t), (byte)(c.G * t), (byte)(c.B * t), (byte)(c.A * t));
    public static Color32 operator +(Color32 src, Color32 dst) => Blend(src, dst);
    public void operator +=(Color32 src) => Blend(src, ref this);
    public static Color32 operator -(Color32 dst, Color32 src) => Unblend(dst, src);

    public static Color32 Blend(Color32 src, Color32 dst)
    {
        double aSrc = src.A / 255.0, aDst = dst.A / 255.0, outA = aSrc + aDst * (1 - aSrc);
        return outA == 0
            ? new(0)
            : new(
                (byte)((src.R * aSrc + dst.R * aDst * (1 - aSrc)) / outA),
                (byte)((src.G * aSrc + dst.G * aDst * (1 - aSrc)) / outA),
                (byte)((src.B * aSrc + dst.B * aDst * (1 - aSrc)) / outA),
                (byte)(outA * 255));
    }

    public static void Blend(Color32 src, ref Color32 dst) // inline blend without creating a new struct, useful for blending into an existing buffer
    {
        double aSrc = src.A / 255.0, aDst = dst.A / 255.0, outA = aSrc + aDst * (1 - aSrc);
        if (outA != 0)
        {
            dst.R = (byte)((src.R * aSrc + dst.R * aDst * (1 - aSrc)) / outA);
            dst.G = (byte)((src.G * aSrc + dst.G * aDst * (1 - aSrc)) / outA);
            dst.B = (byte)((src.B * aSrc + dst.B * aDst * (1 - aSrc)) / outA);
            dst.A = (byte)(outA * 255);
            return;
        }
        dst = default;
    }

    public static Color32 Unblend(Color32 dst, Color32 src)
    {
        double aSrc = src.A / 255.0;
        if (aSrc >= 1.0) return new Color32(0);
        double aDst = dst.A / 255.0, invAlpha = 1 / (1 - aSrc);
        return new Color32(
            (byte)Math.Clamp((dst.R - src.R * aSrc) * invAlpha, 0, 255),
            (byte)Math.Clamp((dst.G - src.G * aSrc) * invAlpha, 0, 255),
            (byte)Math.Clamp((dst.B - src.B * aSrc) * invAlpha, 0, 255),
            (byte)Math.Clamp((aDst - aSrc) * 255, 0, 255));
    }

    public static void BlendPremultiplied(Color32 src, ref Color32 dst)
    {
        byte invA = (byte)(255 - src.A);

        dst.R = (byte)Math.Min(255, src.R + (dst.R * invA + 127) / 255);
        dst.G = (byte)Math.Min(255, src.G + (dst.G * invA + 127) / 255);
        dst.B = (byte)Math.Min(255, src.B + (dst.B * invA + 127) / 255);

        // ✅ CORRECT alpha
        dst.A = (byte)Math.Min(255, src.A + (dst.A * invA + 127) / 255);
    }

    public readonly Color32 ToPremultiplied => new((byte)(R * A / 255), (byte)(G * A / 255), (byte)(B * A / 255), A);
    public void Premultiply() { R = (byte)(R * A / 255); G = (byte)(G * A / 255); B = (byte)(B * A / 255); }
    public readonly Color32 FromPremultiplied => A == 0 ? new(0) : new((byte)(R * 255 / A), (byte)(G * 255 / A), (byte)(B * 255 / A), A);
    public void Unpremultiply() { if (A == 0) { Value = 0; } else { R = (byte)(R * 255 / A); G = (byte)(G * 255 / A); B = (byte)(B * 255 / A); } }

    public readonly void Deconstruct(out byte r, out byte g, out byte b, out byte a) { r = R; g = G; b = B; a = A; }
    public readonly void Deconstruct(out byte r, out byte g, out byte b) { r = R; g = G; b = B; }

    public override readonly string ToString() => $"Color32(R={R}, G={G}, B={B}, A={A})";

    public readonly bool Equals(Color32 other) => Value == other.Value; // faster equality check by comparing the packed uint value instead of individual components

    public override readonly bool Equals(object? obj) => obj is Color32 color && Equals(color);

    public static bool operator ==(Color32 left, Color32 right) => left.Value == right.Value;

    public static bool operator !=(Color32 left, Color32 right) => left.Value != right.Value;

    public override readonly int GetHashCode() => Value.GetHashCode();

    public static implicit operator Color32(Vector4 vector) => new(
        (byte)vector.X,
        (byte)vector.Y,
        (byte)vector.Z,
        (byte)vector.W);

    public static implicit operator Vector4(Color32 color) => new(color.R, color.G, color.B, color.A);
}
