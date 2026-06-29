namespace Mubarrat.VideoEngine.Fields2D;

public class LineSegmentField2D(Point start, Point end) : Field2D, ISignedDistanceField2D, IIntervalField2D, ICoverageField2D
{
    // Pure geometric parameters
    public Point Start { get; } = start;
    public Point End { get; } = end;
    public override Rect Bounds { get; } = new Rect(start, end).Normalized;

    // A pure line has no interior area. Evaluating points directly always yields 
    // the distance to the skeleton, which is positive (outside) everywhere except the exact line string.
    public override double Evaluate(Point p) => SignedDistance(p);

    public double SignedDistance(Point p)
    {
        double abX = End.X - Start.X; double abY = End.Y - Start.Y;
        double apX = p.X - Start.X; double apY = p.Y - Start.Y;

        double abLenSq = (abX * abX) + (abY * abY);
        double t = abLenSq > 0 ? (apX * abX + apY * abY) / abLenSq : 0.0;
        t = Math.Clamp(t, 0.0, 1.0);

        double dx = p.X - (Start.X + t * abX);
        double dy = p.Y - (Start.Y + t * abY);

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public FieldInterval EvaluateInterval(Rect r)
    {
        double minDistance = DistanceRectToSegment(r, Start, End);
        return new FieldInterval(minDistance, double.PositiveInfinity);
    }

    // Calculates coverage for a pixel. For a pure line segment, coverage is always 0.0 since it has no area.
    public double GetCoverage(Rect pixel) => 0.0;

    #region Internal Clipping and Math Utilities
    private static double DistanceRectToSegment(Rect r, Point a, Point b)
    {
        if (r.Contains(a) || r.Contains(b)) return 0.0;
        if (LineIntersectsLine(a, b, new Point(r.Left, r.Top), new Point(r.Right, r.Top)) ||
            LineIntersectsLine(a, b, new Point(r.Right, r.Top), new Point(r.Right, r.Bottom)) ||
            LineIntersectsLine(a, b, new Point(r.Right, r.Bottom), new Point(r.Left, r.Bottom)) ||
            LineIntersectsLine(a, b, new Point(r.Left, r.Bottom), new Point(r.Left, r.Top))) return 0.0;

        double minDistSq = double.MaxValue;
        Point[] corners = [new(r.Left, r.Top), new(r.Right, r.Top), new(r.Left, r.Bottom), new(r.Right, r.Bottom)];
        foreach (var corner in corners)
        {
            double abX = b.X - a.X; double abY = b.Y - a.Y;
            double abLenSq = (abX * abX) + (abY * abY);
            double t = abLenSq > 0 ? ((corner.X - a.X) * abX + (corner.Y - a.Y) * abY) / abLenSq : 0.0;
            t = Math.Clamp(t, 0.0, 1.0);
            double dx = corner.X - (a.X + t * abX); double dy = corner.Y - (a.Y + t * abY);
            double dSq = (dx * dx) + (dy * dy);
            if (dSq < minDistSq) minDistSq = dSq;
        }
        return Math.Sqrt(minDistSq);
    }

    private static bool LineIntersectsLine(Point a1, Point a2, Point b1, Point b2)
    {
        double d = (a2.X - a1.X) * (b2.Y - b1.Y) - (a2.Y - a1.Y) * (b2.X - b1.X);
        if (d == 0) return false;
        double u = ((b1.X - a1.X) * (b2.Y - b1.Y) - (b1.Y - a1.Y) * (b2.X - b1.X)) / d;
        double v = ((b1.X - a1.X) * (a2.Y - a1.Y) - (b1.Y - a1.Y) * (a2.X - a1.X)) / d;
        return u >= 0 && u <= 1 && v >= 0 && v <= 1;
    }
    #endregion
}
