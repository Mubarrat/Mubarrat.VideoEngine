using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Immutable;

[StructLayout(LayoutKind.Sequential)]
public struct Subpath(params Edge[] edges) : ILerpable<Subpath>
{
    public Edge[] Edges = edges ?? [];

    public readonly Subpath Lerp(in Subpath target, double t)
    {
        if (t <= 0) return this;
        if (t >= 1) return target;

        if (Edges.Length == 0 && target.Edges.Length == 0)
            return this;

        // Collapse / expand
        if (Edges.Length == 0)
        {
            var center = new Edge(target.CenterPoint);
            return new(Array.ConvertAll(target.Edges, e => center.Lerp(e, t)));
        }

        if (target.Edges.Length == 0)
        {
            var center = new Edge(CenterPoint);
            return new(Array.ConvertAll(Edges, e => e.Lerp(center, t)));
        }

        int count = Math.Max(Edges.Length + 1, target.Edges.Length + 1);

        var a = SamplePoints(count);
        var b = target.SamplePoints(count);

        // Normalize winding
        if (IsClockwise(a) != IsClockwise(b))
            Array.Reverse(b);

        // Align start (O(n))
        if (Edges[0].Point1 == Edges[^1].Point2)
            RotateInPlace(a, FindBestStart(b, a));
        else if (target.Edges[0].Point1 == target.Edges[^1].Point2)
            RotateInPlace(b, FindBestStart(a, b));
        // Do not rotate if they are both open, as it would cause unnecessary distortion

        // Lerp
        var result = new Point[count];
        for (int i = 0; i < count; i++)
            result[i] = a[i].Lerp(b[i], t);
        var edges = new Edge[count - 1];
        for (int i = 0; i < count - 1; i++)
            edges[i] = new Edge(result[i], result[i + 1]);
        return new Subpath(edges);
    }

    private readonly Point[] SamplePoints(int count)
    {
        if (Edges.Length == 0) return new Point[count];
        var table = new LengthTable(Edges);
        var result = new Point[count];
        for (int i = 0; i < count; i++)
        {
            double d = i / (double)(count - 1) * table.CumLengths[^1];
            int idx = Math.Max(0, LowerBound(table.CumLengths, d) - 1);
            var e = Edges[idx];
            result[i] = e.Point1.Lerp(e.Point2, (d - table.CumLengths[idx]) / e.Length);
        }
        return result;
    }

    private readonly Point SamplePointAtDistance(double s)
    {
        if (Edges.Length == 0) return new();
        double accumulated = 0;
        foreach (Edge e in Edges)
        {
            if (s <= accumulated + e.Length) return e.Point1.Lerp(e.Point2, (s - accumulated) / e.Length);
            accumulated += e.Length;
        }
        return Edges[^1].Point2;
    }

    private static bool IsClockwise(Point[] pts) => Enumerable.Range(0, pts.Length)
        .Sum(i => ((Vector2D)pts[i]).Cross((Vector2D)pts[(i + 1) % pts.Length])) > 0;

    private static int FindBestStart(Point[] a, Point[] b)
    {
        int best = 0;
        double bestDist = double.MaxValue;
        Point anchor = a[0];
        for (int i = 0; i < b.Length; i++)
        {
            double d = anchor.DistanceTo(b[i]);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private static Point[] Rotate(Point[] pts, int shift)
    {
        int n = pts.Length;
        var res = new Point[n];
        for (int i = 0; i < n; i++)
            res[i] = pts[(i + shift) % n];
        return res;
    }

    private static void RotateInPlace(Point[] pts, int shift)
    {
        int n = pts.Length;
        shift %= n;
        Array.Reverse(pts);
        Array.Reverse(pts, 0, n - shift);
        Array.Reverse(pts, n - shift, shift);
    }

    public readonly Point CenterPoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(Edges.Average(x => x.MidPoint.X), Edges.Average(x => x.MidPoint.Y));
    }

    public readonly Rect Bounds => Edges.Select(x => new Rect(x.Point1, x.Point2)).Aggregate((a, b) => a.Union(b));

    private readonly struct LengthTable
    {
        public readonly double[] CumLengths;

        public LengthTable(Edge[] edges)
        {
            CumLengths = new double[edges.Length + 1];
            for (int i = 0; i < edges.Length; i++)
                CumLengths[i + 1] = CumLengths[i] + edges[i].Length;
        }
    }

    private static int LowerBound(double[] arr, double value)
    {
        int lo = 0, hi = arr.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (arr[mid] < value) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    public static Subpath operator *(Subpath subpath, Matrix2D matrix) => new(Array.ConvertAll(subpath.Edges, e => e * matrix));
    public static Subpath operator /(Subpath subpath, Matrix2D matrix) => new(Array.ConvertAll(subpath.Edges, e => e / matrix));
    public void operator *=(Matrix2D matrix)
    {
        for (int i = 0; i < Edges.Length; i++)
            Edges[i] *= matrix;
    }
    public void operator /=(Matrix2D matrix)
    {
        for (int i = 0; i < Edges.Length; i++)
            Edges[i] /= matrix;
    }
}
