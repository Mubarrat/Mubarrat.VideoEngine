using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Immutable;

[StructLayout(LayoutKind.Sequential)]
public struct Subpath(params Edge[] edges) : ILerpable<Subpath>
{
    private const int MaxLerpSamples = 2048;

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
            return new Subpath(Array.ConvertAll(target.Edges, e => center.Lerp(e, t))).SanitizeForRasterizer();
        }

        if (target.Edges.Length == 0)
        {
            var center = new Edge(CenterPoint);
            return new Subpath(Array.ConvertAll(Edges, e => e.Lerp(center, t))).SanitizeForRasterizer();
        }

        if (Edges.SequenceEqual(target.Edges)) // Rare but cheap case
            return target;

        bool thisClosed = IsClosed(Edges);
        bool targetClosed = IsClosed(target.Edges);
        bool morphClosed = thisClosed && targetClosed;

        int count = ComputeSampleCount(Edges.Length, target.Edges.Length);
        int pointCount = morphClosed ? count : count + 1;

        var a = SamplePoints(pointCount, thisClosed);
        var b = target.SamplePoints(pointCount, targetClosed);

        if (morphClosed)
        {
            if (IsClockwise(a) != IsClockwise(b))
                Array.Reverse(b);

            RotateInPlace(b, FindBestCyclicShift(a, b));
        }
        else if (thisClosed)
            RotateInPlace(a, FindBestCyclicShift(b, a));
        else if (targetClosed)
            RotateInPlace(b, FindBestCyclicShift(a, b));

        // Lerp
        var result = new Point[pointCount];
        for (int i = 0; i < pointCount; i++)
            result[i] = a[i].Lerp(b[i], t);

        var edges = new Edge[count];

        if (morphClosed)
        {
            for (int i = 0; i < count; i++)
            {
                var p0 = result[i];
                var p1 = result[(i + 1) % count];
                edges[i] = new Edge(p0, p1);
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                var p0 = result[i];
                var p1 = result[i + 1];
                edges[i] = new Edge(p0, p1);
            }
        }

        return new Subpath(edges).SanitizeForRasterizer();
    }

    public readonly Subpath SanitizeForRasterizer(double minEdgeLength = 1e-9)
    {
        if (Edges.Length == 0)
            return this;

        double minEdgeLengthSquared = minEdgeLength * minEdgeLength;
        bool closed = IsClosed(Edges);

        var sanitized = new List<Edge>(Edges.Length);
        for (int i = 0; i < Edges.Length; i++)
        {
            var edge = Edges[i];
            if (!IsFinite(edge.Point1) || !IsFinite(edge.Point2))
                continue;

            if (edge.LengthSquared <= minEdgeLengthSquared)
                continue;

            sanitized.Add(edge);
        }

        if (sanitized.Count == 0)
            return new Subpath([]);

        var result = sanitized.ToArray();

        if (closed)
        {
            var first = result[0];
            var last = result[^1];
            result[^1] = new Edge(last.Point1, first.Point1);

            if (result[^1].LengthSquared <= minEdgeLengthSquared)
            {
                Array.Resize(ref result, result.Length - 1);
                if (result.Length == 0)
                    return new Subpath([]);

                last = result[^1];
                result[^1] = new Edge(last.Point1, result[0].Point1);
            }
        }

        return new Subpath(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFinite(Point p)
        => double.IsFinite(p.X) && double.IsFinite(p.Y);

    private readonly Point[] SamplePoints(int count, bool closed)
    {
        if (count <= 0)
            return [];

        if (Edges.Length == 0)
            return new Point[count];

        int edgeCount = Edges.Length;
        var points = new Point[count];
        var lengths = new double[edgeCount];
        double totalLength = 0;

        for (int i = 0; i < edgeCount; i++)
        {
            double len = Edges[i].Length;
            lengths[i] = len;
            totalLength += len;
        }

        if (totalLength <= double.Epsilon)
        {
            var p = Edges[0].Point1;
            Array.Fill(points, p);
            if (!closed)
            {
                points[0] = Edges[0].Point1;
                points[^1] = Edges[^1].Point2;
            }

            return points;
        }

        int totalSegments = closed ? count : count - 1;
        if (totalSegments <= 0)
        {
            points[0] = Edges[0].Point1;
            return points;
        }

        var segmentsPerEdge = AllocateSegmentsPerEdge(lengths, totalSegments);
        int pointIndex = 0;

        if (closed)
        {
            for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
            {
                var edge = Edges[edgeIndex];
                int segmentCount = segmentsPerEdge[edgeIndex];

                for (int s = 0; s < segmentCount && pointIndex < count; s++)
                {
                    double localT = s / (double)segmentCount;
                    points[pointIndex++] = edge.Point1.Lerp(edge.Point2, localT);
                }
            }
        }
        else
        {
            points[pointIndex++] = Edges[0].Point1;

            for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
            {
                var edge = Edges[edgeIndex];
                int segmentCount = segmentsPerEdge[edgeIndex];

                for (int s = 1; s <= segmentCount && pointIndex < count; s++)
                {
                    double localT = s / (double)segmentCount;
                    points[pointIndex++] = edge.Point1.Lerp(edge.Point2, localT);
                }
            }

            points[0] = Edges[0].Point1;
            points[^1] = Edges[^1].Point2;
        }

        return points;
    }

    private static bool IsClosed(Edge[] edges)
        => edges.Length > 0 && edges[0].Point1 == edges[^1].Point2;

    private static bool IsClockwise(Point[] pts)
    {
        if (pts.Length < 3)
            return false;

        double area2 = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            area2 += ((Vector2D)pts[i]).Cross((Vector2D)pts[(i + 1) % pts.Length]);
        }

        return area2 > 0;
    }

    private static int FindBestCyclicShift(Point[] reference, Point[] candidate)
    {
        int n = reference.Length;
        if (n == 0 || candidate.Length != n)
            return 0;

        if (n <= 12)
            return FindBestCyclicShiftExhaustive(reference, candidate);

        var referenceCurvature = BuildCurvatureWeights(reference);
        var candidateCurvature = BuildCurvatureWeights(candidate);

        int coarseStride = Math.Max(1, n >> 4);
        int sampleStep = Math.Max(1, n >> 4);

        int coarseBestShift = 0;
        double coarseBestScore = double.MaxValue;

        for (int shift = 0; shift < n; shift += coarseStride)
        {
            double score = EvaluateShiftScore(reference, candidate, referenceCurvature, candidateCurvature, shift, sampleStep, coarseBestScore);
            if (score < coarseBestScore)
            {
                coarseBestScore = score;
                coarseBestShift = shift;
            }
        }

        int refineRadius = Math.Max(3, coarseStride);

        int bestShift = coarseBestShift;
        double bestScore = double.MaxValue;

        for (int delta = -refineRadius; delta <= refineRadius; delta++)
        {
            int shift = Mod(coarseBestShift + delta, n);
            double score = EvaluateShiftScore(reference, candidate, referenceCurvature, candidateCurvature, shift, 1, bestScore);
            if (score < bestScore)
            {
                bestScore = score;
                bestShift = shift;
            }
        }

        double zeroScore = EvaluateShiftScore(reference, candidate, referenceCurvature, candidateCurvature, 0, 1, bestScore);
        if (zeroScore <= bestScore * 1.000000001d)
            return 0;

        return bestShift;
    }

    private static int FindBestCyclicShiftExhaustive(Point[] reference, Point[] candidate)
    {
        int n = reference.Length;
        int bestShift = 0;
        double bestScore = double.MaxValue;

        for (int shift = 0; shift < n; shift++)
        {
            double score = 0;
            for (int i = 0; i < n; i++)
            {
                var a = reference[i];
                var b = candidate[(i + shift) % n];

                double dx = a.X - b.X;
                double dy = a.Y - b.Y;
                score += (dx * dx) + (dy * dy);
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestShift = shift;
            }
        }

        return bestShift;
    }

    private static double[] BuildCurvatureWeights(Point[] pts)
    {
        int n = pts.Length;
        var curvature = new double[n];

        if (n < 3)
            return curvature;

        for (int i = 0; i < n; i++)
        {
            var prev = pts[(i - 1 + n) % n];
            var cur = pts[i];
            var next = pts[(i + 1) % n];

            double ax = cur.X - prev.X;
            double ay = cur.Y - prev.Y;
            double bx = next.X - cur.X;
            double by = next.Y - cur.Y;

            double al2 = (ax * ax) + (ay * ay);
            double bl2 = (bx * bx) + (by * by);

            if (al2 <= double.Epsilon || bl2 <= double.Epsilon)
                continue;

            double cross = Math.Abs((ax * by) - (ay * bx));
            curvature[i] = cross / Math.Sqrt(al2 * bl2);
        }

        return curvature;
    }

    private static double EvaluateShiftScore(
        Point[] reference,
        Point[] candidate,
        double[] referenceCurvature,
        double[] candidateCurvature,
        int shift,
        int step,
        double earlyExit)
    {
        const double CurvatureMatchWeight = 0.45;
        const double CurvatureMismatchPenalty = 0.20;

        int n = reference.Length;
        double score = 0;

        for (int i = 0; i < n; i += step)
        {
            int j = i + shift;
            if (j >= n)
                j -= n;

            var a = reference[i];
            var b = candidate[j];

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dist2 = (dx * dx) + (dy * dy);

            double ka = referenceCurvature[i];
            double kb = candidateCurvature[j];
            double boost = 1d + (CurvatureMatchWeight * (ka + kb));
            double mismatch = ka - kb;

            score += (dist2 * boost) + (CurvatureMismatchPenalty * mismatch * mismatch);

            if (score >= earlyExit)
                return score;
        }

        return score;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static int ComputeSampleCount(int sourceCount, int targetCount)
    {
        int baseline = Math.Max(sourceCount, targetCount);
        if (baseline <= 0)
            return 0;

        int cap = Math.Max(baseline, Math.Min(MaxLerpSamples, baseline * 8));

        long gcd = GreatestCommonDivisor(sourceCount, targetCount);
        long lcm = (sourceCount / gcd) * (long)targetCount;

        if (lcm > cap)
            return cap;

        return (int)lcm;
    }

    private static int[] AllocateSegmentsPerEdge(double[] lengths, int totalSegments)
    {
        int edgeCount = lengths.Length;
        var counts = new int[edgeCount];
        if (edgeCount == 0)
            return counts;

        if (totalSegments <= edgeCount)
        {
            for (int i = 0; i < totalSegments; i++)
                counts[i] = 1;

            return counts;
        }

        for (int i = 0; i < edgeCount; i++)
            counts[i] = 1;

        int remaining = totalSegments - edgeCount;
        double totalLength = 0;

        for (int i = 0; i < edgeCount; i++)
            totalLength += lengths[i];

        if (totalLength <= double.Epsilon)
        {
            for (int i = 0; i < remaining; i++)
                counts[i % edgeCount]++;

            return counts;
        }

        double carry = 0;
        int allocated = 0;

        for (int i = 0; i < edgeCount; i++)
        {
            carry += remaining * (lengths[i] / totalLength);
            int add = (int)carry;
            if (add > 0)
            {
                counts[i] += add;
                allocated += add;
                carry -= add;
            }
        }

        for (int i = 0; allocated < remaining; i++)
        {
            counts[i % edgeCount]++;
            allocated++;
        }

        return counts;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            int t = a % b;
            a = b;
            b = t;
        }

        return Math.Abs(a);
    }

    private static void RotateInPlace(Point[] pts, int shift)
    {
        int n = pts.Length;
        if (n == 0)
            return;

        shift %= n;
        if (shift < 0)
            shift += n;
        if (shift == 0)
            return;

        Array.Reverse(pts);
        Array.Reverse(pts, 0, n - shift);
        Array.Reverse(pts, n - shift, shift);
    }

    public readonly Point CenterPoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Edges.Length == 0 ? Point.NaN : new(Edges.Average(x => x.MidPoint.X), Edges.Average(x => x.MidPoint.Y));
    }

    public readonly Rect Bounds => Edges.Length == 0 ? Rect.NaN : Edges.Select(x => new Rect(x.Point1, x.Point2)).Aggregate((a, b) => a.Union(b));

    public readonly double Perimeter => Edges.Length == 0 ? 0 : Edges.Sum(e => e.Length);

    public readonly struct Accumulator<T> where T : INumber<T>
    {
        public readonly T[] Cum;

        public Accumulator(T[] values)
        {
            Cum = new T[values.Length + 1];
            T sum = T.Zero;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
                Cum[i + 1] = sum;
            }
        }
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
