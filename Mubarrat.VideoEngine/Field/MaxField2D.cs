namespace Mubarrat.VideoEngine.Field;

public class MaxField2D : Field2D, IBinaryField2D, IIntervalField2D, ICoverageField2D
{
    public Field2D Left { get; }
    public Field2D Right { get; }
    public override Rect Bounds { get; }

    public MaxField2D(Field2D left, Field2D right)
    {
        Left = left; Right = right;
        double minX = Math.Max(left.Bounds.Left, right.Bounds.Left);
        double minY = Math.Max(left.Bounds.Top, right.Bounds.Top);
        double maxX = Math.Min(left.Bounds.Right, right.Bounds.Right);
        double maxY = Math.Min(left.Bounds.Bottom, right.Bounds.Bottom);

        if (maxX < minX || maxY < minY) Bounds = default;
        else Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public override double Evaluate(Point p) => Math.Max(Left.Evaluate(p), Right.Evaluate(p));

    public FieldInterval EvaluateInterval(Rect r)
    {
        FieldInterval leftRange = Left is IIntervalField2D leftInterval ? leftInterval.EvaluateInterval(r) : new FieldInterval(double.MinValue, double.MaxValue);
        FieldInterval rightRange = Right is IIntervalField2D rightInterval ? rightInterval.EvaluateInterval(r) : new FieldInterval(double.MinValue, double.MaxValue);
        return new FieldInterval(Math.Max(leftRange.Min, rightRange.Min), Math.Max(leftRange.Max, rightRange.Max));
    }

    /// <summary>
    /// Intersection Coverage: Multiplicative intersection profiling for sub-pixel features.
    /// </summary>
    public double GetCoverage(Rect pixel)
    {
        // If the pixel is completely outside the combined macro bounds, short-circuit instantly
        if (pixel.Right <= Bounds.Left || pixel.Left >= Bounds.Right || pixel.Bottom <= Bounds.Top || pixel.Top >= Bounds.Bottom)
        {
            return 0.0;
        }

        double covL = Left is ICoverageField2D leftCov ? leftCov.GetCoverage(pixel) : (Left.Evaluate(new Point(pixel.X + pixel.Width * 0.5, pixel.Y + pixel.Height * 0.5)) <= 0 ? 1.0 : 0.0);
        double covR = Right is ICoverageField2D rightCov ? rightCov.GetCoverage(pixel) : (Right.Evaluate(new Point(pixel.X + pixel.Width * 0.5, pixel.Y + pixel.Height * 0.5)) <= 0 ? 1.0 : 0.0);

        // Algebraic Intersection formula: L * R
        return Math.Clamp(covL * covR, 0.0, 1.0);
    }
}
