using System.Runtime.CompilerServices;

namespace Mubarrat.VideoEngine.Field;

public class LerpField2D(Field2D a, Field2D b, double t) : Field2D, ISignedDistanceField2D, IIntervalField2D, ICoverageField2D, IGradientField2D
{
    private const double GradientEpsilon = 1e-3;
    private const int CoverageSamples = 4;

    public Field2D A { get; } = a ?? throw new ArgumentNullException(nameof(a));
    public Field2D B { get; } = b ?? throw new ArgumentNullException(nameof(b));
    public double T { get; } = double.IsFinite(t) ? t : 0.0;

    public override Rect Bounds => GetBounds();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Evaluate(Point p)
    {
        if (T <= 0.0) return Sample(A, p);
        if (T >= 1.0) return Sample(B, p);
        return LerpValue(Sample(A, p), Sample(B, p), T);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double SignedDistance(Point p)
    {
        double value = Evaluate(p);
        if (!double.IsFinite(value) || value == 0.0) return value;

        double gradientLength = Gradient(p).Length;
        return double.IsFinite(gradientLength) && gradientLength > 1e-9
            ? value / gradientLength
            : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FieldInterval EvaluateInterval(Rect r)
    {
        if (T <= 0.0)
            return A is IIntervalField2D ia0 ? ia0.EvaluateInterval(r) : FieldInterval.Unknown();
        if (T >= 1.0)
            return B is IIntervalField2D ib1 ? ib1.EvaluateInterval(r) : FieldInterval.Unknown();

        if (A is IIntervalField2D ia && B is IIntervalField2D ib)
        {
            FieldInterval ar = ia.EvaluateInterval(r);
            FieldInterval br = ib.EvaluateInterval(r);
            return new FieldInterval(
                LerpValue(ar.Min, br.Min, T),
                LerpValue(ar.Max, br.Max, T));
        }

        return FieldInterval.Unknown();
    }

    public double GetCoverage(Rect pixel)
    {
        if (pixel.Width <= 0.0 || pixel.Height <= 0.0) return 0.0;

        FieldInterval range = EvaluateInterval(pixel);
        if (range.IsFullyAbove(0.0)) return 0.0;
        if (range.IsFullyBelow(0.0)) return 1.0;

        if (T <= 0.0 && A is ICoverageField2D ca) return ca.GetCoverage(pixel);
        if (T >= 1.0 && B is ICoverageField2D cb) return cb.GetCoverage(pixel);

        return CoverageMsaa(pixel);
    }

    public Vector2D Gradient(Point p)
    {
        if (T <= 0.0) return SampleGradient(A, p);
        if (T >= 1.0) return SampleGradient(B, p);

        Vector2D ga = SampleGradient(A, p);
        Vector2D gb = SampleGradient(B, p);
        return new Vector2D(
            LerpValue(ga.X, gb.X, T),
            LerpValue(ga.Y, gb.Y, T));
    }

    private Rect GetBounds()
    {
        if (T <= 0.0) return A.Bounds;
        if (T >= 1.0) return B.Bounds;

        Rect ab = A.Bounds;
        Rect bb = B.Bounds;
        if (!IsFiniteRect(ab) || !IsFiniteRect(bb)) return Rect.Universal;
        return Rect.Union(ab, bb);
    }

    private double CoverageMsaa(Rect pixel)
    {
        const double InvSamples = 1.0 / CoverageSamples;
        double covered = 0.0;

        for (int y = 0; y < CoverageSamples; y++)
        {
            double py = pixel.Top + (y + 0.5) * pixel.Height * InvSamples;
            for (int x = 0; x < CoverageSamples; x++)
            {
                double px = pixel.Left + (x + 0.5) * pixel.Width * InvSamples;
                if (Evaluate(new Point(px, py)) <= 0.0)
                    covered += 1.0;
            }
        }

        return covered * (InvSamples * InvSamples);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Sample(Field2D field, Point p)
        => field is ISignedDistanceField2D sdf ? sdf.SignedDistance(p) : field.Evaluate(p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2D SampleGradient(Field2D field, Point p)
    {
        if (field is IGradientField2D gradientField)
            return gradientField.Gradient(p);

        return EstimateGradient(field, p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2D EstimateGradient(Field2D field, Point p)
    {
        double eps = GradientEpsilon;
        double lx = Sample(field, new Point(p.X - eps, p.Y));
        double rx = Sample(field, new Point(p.X + eps, p.Y));
        double ty = Sample(field, new Point(p.X, p.Y - eps));
        double by = Sample(field, new Point(p.X, p.Y + eps));

        return new Vector2D(
            (rx - lx) / (eps * 2.0),
            (by - ty) / (eps * 2.0));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double LerpValue(double a, double b, double t)
    {
        if (t <= 0.0) return a;
        if (t >= 1.0) return b;
        if (a == b) return a;
        if (double.IsFinite(a) && double.IsFinite(b))
            return Math.FusedMultiplyAdd(b - a, t, a);

        double value = a * (1.0 - t) + b * t;
        if (!double.IsNaN(value)) return value;
        if (double.IsPositiveInfinity(a) || double.IsPositiveInfinity(b)) return double.PositiveInfinity;
        if (double.IsNegativeInfinity(a) || double.IsNegativeInfinity(b)) return double.NegativeInfinity;
        return value;
    }

    private static bool IsFiniteRect(Rect r)
        => double.IsFinite(r.X) && double.IsFinite(r.Y)
        && double.IsFinite(r.Width) && double.IsFinite(r.Height);
}
