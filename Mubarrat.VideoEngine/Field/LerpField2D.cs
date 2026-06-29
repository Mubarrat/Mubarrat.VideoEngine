using System.Runtime.CompilerServices;

namespace Mubarrat.VideoEngine.Field;

public class LerpField2D(Field2D a, Field2D b, double t) : Field2D, ISignedDistanceField2D, IIntervalField2D, ICoverageField2D, IGradientField2D
{
    public Field2D A { get; } = a;
    public Field2D B { get; } = b;
    public double T { get; } = t;

    public override Rect Bounds => A.Bounds.Lerp(B.Bounds, T);

    // -------------------------
    // Evaluation (generic)
    // -------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Evaluate(Point p) => SampleA(p).Lerp(SampleB(p), T);

    // -------------------------
    // Signed distance morph
    // -------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double SignedDistance(Point p)
    {
        if (A is ISignedDistanceField2D sa && B is ISignedDistanceField2D sb)
            return sa.SignedDistance(p).Lerp(sb.SignedDistance(p), T);

        // fallback: gradient-space approximation
        return Evaluate(p);
    }

    // -------------------------
    // Interval morph
    // -------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FieldInterval EvaluateInterval(Rect r)
    {
        if (A is IIntervalField2D ia && B is IIntervalField2D ib)
        {
            var a = ia.EvaluateInterval(r);
            var b = ib.EvaluateInterval(r);
            return new FieldInterval(a.Min.Lerp(b.Min, T), a.Max.Lerp(b.Max, T));
        }

        return FieldInterval.Unknown();
    }

    // -------------------------
    // Helpers
    // -------------------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double SampleA(Point p)
    {
        if (A is ISignedDistanceField2D sdf)
            return sdf.SignedDistance(p);

        return A.Evaluate(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double SampleB(Point p)
    {
        if (B is ISignedDistanceField2D sdf)
            return sdf.SignedDistance(p);

        return B.Evaluate(p);
    }

    public double GetCoverage(Rect pixel)
    {
        if (A is ICoverageField2D ca && B is ICoverageField2D cb)
        {
            double a = ca.GetCoverage(pixel);
            double b = cb.GetCoverage(pixel);
            return a.Lerp(b, T);
        }

        // fallback: no analytical coverage available
        // approximate via center sample
        Point center = pixel.Center;
        double v = Evaluate(center);
        return Math.Clamp(0.5 - Math.Abs(v), 0.0, 1.0);
    }

    public Vector2D Gradient(Point p)
    {
        // 1. Perfect case: both gradient-aware
        if (A is IGradientField2D ga && B is IGradientField2D gb)
        {
            return ga.Gradient(p).Lerp(gb.Gradient(p), T);
        }

        // 2. SDF-aware reconstruction
        if (A is ISignedDistanceField2D sa && B is ISignedDistanceField2D sb)
        {
            return EstimateGradient(A, p).Lerp(EstimateGradient(B, p), T);
        }

        // 3. IMPORTANT FIX:
        // do NOT differentiate "this"
        // instead fallback to A/B blending
        var gA = EstimateGradient(A, p);
        var gB = EstimateGradient(B, p);

        return new Vector2D(
            gA.X.Lerp(gB.X, T),
            gA.Y.Lerp(gB.Y, T)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2D EstimateGradient(Field2D f, Point p)
    {
        double eps = 0.5;

        double v = f.Evaluate(p);
        double vx = f.Evaluate(new Point(p.X + eps, p.Y));
        double vy = f.Evaluate(new Point(p.X, p.Y + eps));

        double gx = (vx - v) / eps;
        double gy = (vy - v) / eps;

        return new Vector2D(gx, gy);
    }
}
