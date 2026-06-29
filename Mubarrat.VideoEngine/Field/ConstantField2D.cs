namespace Mubarrat.VideoEngine.Field;

public class ConstantField2D(double value) : Field2D, IIntervalField2D, ICoverageField2D
{
    public static readonly ConstantField2D Empty = new(double.PositiveInfinity);

    public double Value { get; } = value;
    public override Rect Bounds { get; } = Rect.Empty;

    public override double Evaluate(Point p) => Value;

    public FieldInterval EvaluateInterval(Rect r) => new(Value, Value);

    public double GetCoverage(Rect pixel) => Value <= 0.0 ? 1.0 : 0.0;
}
