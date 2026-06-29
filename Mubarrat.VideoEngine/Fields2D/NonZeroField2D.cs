namespace Mubarrat.VideoEngine.Fields2D;

public class NonZeroField2D : Field2D, IIntervalField2D, ICoverageField2D
{
    public Field2D[] Children { get; }
    public override Rect Bounds { get; }

    public NonZeroField2D(params Field2D[] children)
    {
        Children = children?.Where(child => child is not null).ToArray() ?? [];
        Bounds = Children.Length == 0
            ? Rect.Empty
            : Children.Select(child => child.Bounds).Aggregate((left, right) => left.Union(right));
    }

    public override double Evaluate(Point p)
        => Children.Length == 0 ? double.PositiveInfinity : Children.Min(child => child.Evaluate(p));

    public FieldInterval EvaluateInterval(Rect r)
    {
        if (Children.Length == 0)
            return FieldInterval.Unknown();

        FieldInterval range = Children[0] is IIntervalField2D firstInterval
            ? firstInterval.EvaluateInterval(r)
            : FieldInterval.Unknown();

        for (int i = 1; i < Children.Length; i++)
        {
            FieldInterval childRange = Children[i] is IIntervalField2D childInterval
                ? childInterval.EvaluateInterval(r)
                : FieldInterval.Unknown();
            range = FieldInterval.Union(range, childRange);
        }

        return range;
    }

    public double GetCoverage(Rect pixel)
    {
        if (Children.Length == 0)
            return 0.0;

        double coverage = 0.0;
        foreach (Field2D child in Children)
        {
            double childCoverage = child is ICoverageField2D coverageChild
                ? coverageChild.GetCoverage(pixel)
                : (child.Evaluate(pixel.Center) <= 0.0 ? 1.0 : 0.0);
            coverage = coverage + childCoverage - (coverage * childCoverage);
        }

        return Math.Clamp(coverage, 0.0, 1.0);
    }
}
