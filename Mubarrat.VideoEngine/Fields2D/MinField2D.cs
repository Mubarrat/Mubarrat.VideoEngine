namespace Mubarrat.VideoEngine.Fields2D;

public class MinField2D : Field2D, IBinaryField2D, IIntervalField2D, ICoverageField2D
{
    public Field2D Left { get; }
    public Field2D Right { get; }
    public override Rect Bounds { get; }

    public MinField2D(Field2D left, Field2D right)
    {
        Left = left; Right = right;
        double minX = Math.Min(left.Bounds.Left, right.Bounds.Left);
        double minY = Math.Min(left.Bounds.Top, right.Bounds.Top);
        double maxX = Math.Max(left.Bounds.Right, right.Bounds.Right);
        double maxY = Math.Max(left.Bounds.Bottom, right.Bounds.Bottom);
        Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public override double Evaluate(Point p) => Math.Min(Left.Evaluate(p), Right.Evaluate(p));

    public FieldInterval EvaluateInterval(Rect r)
    {
        FieldInterval leftRange = Left is IIntervalField2D leftInterval ? leftInterval.EvaluateInterval(r) : new FieldInterval(double.MinValue, double.MaxValue);
        FieldInterval rightRange = Right is IIntervalField2D rightInterval ? rightInterval.EvaluateInterval(r) : new FieldInterval(double.MinValue, double.MaxValue);
        return new FieldInterval(Math.Min(leftRange.Min, rightRange.Min), Math.Min(leftRange.Max, rightRange.Max));
    }

    /// <summary>
    /// Union Coverage: Evaluates sub-pixel blend logic via algebraic inclusion-exclusion properties.
    /// </summary>
    public double GetCoverage(Rect pixel)
    {
        double covL = Left is ICoverageField2D leftCov ? leftCov.GetCoverage(pixel) : (Left.Evaluate(new Point(pixel.X + pixel.Width * 0.5, pixel.Y + pixel.Height * 0.5)) <= 0 ? 1.0 : 0.0);
        double covR = Right is ICoverageField2D rightCov ? rightCov.GetCoverage(pixel) : (Right.Evaluate(new Point(pixel.X + pixel.Width * 0.5, pixel.Y + pixel.Height * 0.5)) <= 0 ? 1.0 : 0.0);

        // Algebraic Union formula: L + R - (L * R)
        double unionCoverage = covL + covR - (covL * covR);
        return Math.Clamp(unionCoverage, 0.0, 1.0);
    }
}
