namespace Mubarrat.VideoEngine.Field;

public class NegativeField2D(Field2D child) : Field2D, IUnaryField2D, IIntervalField2D, ICoverageField2D
{
    public Field2D Child { get; } = child ?? throw new ArgumentNullException(nameof(child));
    public override Rect Bounds => Child.Bounds;

    public override double Evaluate(Point p) => -Child.Evaluate(p);

    public FieldInterval EvaluateInterval(Rect r)
    {
        if (Child is IIntervalField2D intervalChild)
        {
            FieldInterval childInterval = intervalChild.EvaluateInterval(r);
            return new FieldInterval(-childInterval.Max, -childInterval.Min);
        }
        return new FieldInterval(double.MinValue, double.MaxValue);
    }

    /// <summary>
    /// Inversion Coverage: The absolute mathematical complement of the child's coverage.
    /// </summary>
    public double GetCoverage(Rect pixel)
    {
        if (Child is ICoverageField2D coverageChild)
        {
            return 1.0 - coverageChild.GetCoverage(pixel);
        }

        // Fallback: Sample the pixel center if the child is missing a coverage interface
        return Evaluate(new Point(pixel.X + pixel.Width * 0.5, pixel.Y + pixel.Height * 0.5)) <= 0 ? 1.0 : 0.0;
    }
}
