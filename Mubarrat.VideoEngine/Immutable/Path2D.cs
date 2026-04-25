using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Immutable;

[StructLayout(LayoutKind.Sequential)]
public struct Path2D(bool IsNonZeroFill, params Subpath[] subpaths) : ILerpable<Path2D>
{
    public bool IsNonZeroFill = IsNonZeroFill;
    public Subpath[] Subpaths = subpaths ?? [];

    public readonly Path2D Lerp(in Path2D target, double t)
    {
        if (t <= 0) return this;
        if (t >= 1) return target;

        bool fill = t < 0.5 ? IsNonZeroFill : target.IsNonZeroFill;
        
        switch (Subpaths?.Length ?? 0, target.Subpaths?.Length ?? 0)
        {
            case (0, 0): return new Path2D(fill);
            case (0, _): return new Path2D(fill, new Subpath([])).Lerp(target, t);
            case (_, 0): return Lerp(new Path2D(fill, new Subpath([])), t);
        }

        int n = Math.Max(Subpaths.Length, target.Subpaths.Length);

        var a = (Subpath[])Subpaths.Clone();
        var b = (Subpath[])target.Subpaths.Clone();

        // Sort both sides using same heuristic
        Array.Sort(a, CompareSubpath);
        Array.Sort(b, CompareSubpath);

        a = Pad(a, n);
        b = Pad(b, n);

        var result = new Subpath[n];

        for (int i = 0; i < n; i++)
            result[i] = a[i].Lerp(b[i], t);

        return new Path2D(fill, result);
    }

    private static Subpath[] Pad(Subpath[] source, int n)
    {
        var result = new Subpath[n];

        for (int i = 0; i < n; i++)
            result[i] = i < source.Length ? source[i] : new Subpath([]);

        return result;
    }

    private static int CompareSubpath(Subpath a, Subpath b)
    {
        var ca = a.CenterPoint;
        var cb = b.CenterPoint;

        // Primary: left → right
        int cmp = ca.X.CompareTo(cb.X);
        if (cmp != 0) return cmp;

        // Secondary: top → bottom
        cmp = ca.Y.CompareTo(cb.Y);
        if (cmp != 0) return cmp;

        // Tertiary: area (stable for overlaps)
        return a.Bounds.Size.Area.CompareTo(b.Bounds.Size.Area);
    }

    public readonly Point CenterPoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Subpaths.Select(sp => sp.CenterPoint).ToArray().Average();
    }

    public readonly Rect Bounds => Subpaths is { Length: not 0 } ? Subpaths.Select(x => x.Bounds).Aggregate((a, b) => a.Union(b)) : Rect.NaN;

    public static Path2D operator *(Path2D path, Matrix2D matrix) => new(path.IsNonZeroFill, Array.ConvertAll(path.Subpaths, sp => sp * matrix));
    public static Path2D operator /(Path2D path, Matrix2D matrix) => new(path.IsNonZeroFill, Array.ConvertAll(path.Subpaths, sp => sp / matrix));
    public void operator *=(Matrix2D matrix)
    {
        for (int i = 0; i < Subpaths.Length; i++)
            Subpaths[i] *= matrix;
    }
    public void operator /=(Matrix2D matrix)
    {
        for (int i = 0; i < Subpaths.Length; i++)
            Subpaths[i] /= matrix;
    }

    public static Path2D Combine(Path2D[] paths) => paths.Length == 0
        ? new()
        : new(paths.Sum(x => x.IsNonZeroFill ? 1 : 0) * 2 < paths.Length, [.. paths.SelectMany(p => p.Subpaths)]);
}
