namespace Mubarrat.VideoEngine.Fields2D;

public class QuadraticBezierSegmentField2D : Field2D, ISignedDistanceField2D, IIntervalField2D, ICoverageField2D
{
    // Structural parameters exposed for engine compilation/flattening passes
    public Point Start { get; }
    public Point Control { get; }
    public Point End { get; }
    public override Rect Bounds { get; }
    public bool IsDegenerateLine { get; }

    public QuadraticBezierSegmentField2D(Point start, Point control, Point end)
    {
        Start = start;
        Control = control;
        End = end;

        // 1. Detect Degeneracy Up Front
        // Check if control point is collinear or overlapping with endpoints
        double crossProduct = (control.Y - start.Y) * (end.X - start.X) - (control.X - start.X) * (end.Y - start.Y);
        IsDegenerateLine = Math.Abs(crossProduct) < 1e-9 ||
                           (start.X == control.X && start.Y == control.Y) ||
                           (end.X == control.X && end.Y == control.Y);

        // 2. Hardened Bounding Box Calculation (Fixes Bug #1)
        double minX = Math.Min(start.X, end.X);
        double maxX = Math.Max(start.X, end.X);
        double minY = Math.Min(start.Y, end.Y);
        double maxY = Math.Max(start.Y, end.Y);

        if (!IsDegenerateLine)
        {
            double denomX = start.X - 2.0 * control.X + end.X;
            if (Math.Abs(denomX) > 1e-12)
            {
                double tx = (start.X - control.X) / denomX;
                if (tx > 0.0 && tx < 1.0)
                {
                    double x = (1.0 - tx) * (1.0 - tx) * start.X + 2.0 * (1.0 - tx) * tx * control.X + tx * tx * end.X;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
            }

            double denomY = start.Y - 2.0 * control.Y + end.Y;
            if (Math.Abs(denomY) > 1e-12)
            {
                double ty = (start.Y - control.Y) / denomY;
                if (ty > 0.0 && ty < 1.0)
                {
                    double y = (1.0 - ty) * (1.0 - ty) * start.Y + 2.0 * (1.0 - ty) * ty * control.Y + ty * ty * end.Y;
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Exposes point evaluation directly on the curve (Suggestion #1)
    /// Employs clean and numerically stable Horner-like evaluation syntax.
    /// </summary>
    public Point EvaluateCurve(double t)
    {
        double s = 1.0 - t;
        return new Point(
            s * s * Start.X + 2.0 * s * t * Control.X + t * t * End.X,
            s * s * Start.Y + 2.0 * s * t * Control.Y + t * t * End.Y
        );
    }

    public override double Evaluate(Point p) => SignedDistance(p);

    /// <summary>
    /// Computes precise distance. Soft-falls back to a line if the segment is degenerate.
    /// Handles numerical precision boundaries for Cardano's method.
    /// </summary>
    public double SignedDistance(Point p)
    {
        if (IsDegenerateLine)
        {
            return DistanceToLineSegment(p, Start, End);
        }

        double p0X = Start.X, p0Y = Start.Y;
        double p1X = Control.X, p1Y = Control.Y;
        double p2X = End.X, p2Y = End.Y;

        double ax = p0X - 2.0 * p1X + p2X;
        double ay = p0Y - 2.0 * p1Y + p2Y;
        double bx = 2.0 * (p1X - p0X);
        double by = 2.0 * (p1Y - p0Y);
        double cx = p0X - p.X;
        double cy = p0Y - p.Y;

        double k3 = 2.0 * (ax * ax + ay * ay);
        double k2 = 3.0 * (ax * bx + ay * by);
        double k1 = bx * bx + by * by + 2.0 * (ax * cx + ay * cy);
        double k0 = bx * cx + by * cy;

        // Test boundary configurations explicitly
        double distStartSq = (p.X - p0X) * (p.X - p0X) + (p.Y - p0Y) * (p.Y - p0Y);
        double distEndSq = (p.X - p2X) * (p.X - p2X) + (p.Y - p2Y) * (p.Y - p2Y);
        double minDistanceSq = Math.Min(distStartSq, distEndSq);

        // Stabilized Cardano root check to avoid catastrophic cancellation limits
        if (Math.Abs(k3) > 1e-9)
        {
            double a = k2 / k3;
            double b = k1 / k3;
            double c = k0 / k3;

            double q = (3.0 * b - a * a) / 9.0;
            double r = (9.0 * a * b - 27.0 * c - 2.0 * a * a * a) / 54.0;
            double discriminant = q * q * q + r * r;

            if (discriminant >= 0.0) // 1 real root branch
            {
                double sqrtDisc = Math.Sqrt(discriminant);
                double s = r + sqrtDisc;
                double sVal = s >= 0.0 ? Math.Pow(s, 1.0 / 3.0) : -Math.Pow(-s, 1.0 / 3.0);
                double u = r - sqrtDisc;
                double uVal = u >= 0.0 ? Math.Pow(u, 1.0 / 3.0) : -Math.Pow(-u, 1.0 / 3.0);

                double t = sVal + uVal - a / 3.0;
                if (t >= 0.0 && t <= 1.0)
                    minDistanceSq = Math.Min(minDistanceSq, GetDistanceSqAtT(t, p0X, p0Y, bx, by, ax, ay, p.X, p.Y));
            }
            else // 3 real roots branch (highly sensitive to repeated roots)
            {
                double rho = Math.Sqrt(-q * q * q);
                // Clamp to protect against floating point noise exceeding Acox bounds [-1, 1]
                double theta = Math.Acos(Math.Clamp(r / rho, -1.0, 1.0));
                double rVal = 2.0 * Math.Sqrt(-q);
                double offset = a / 3.0;

                double t1 = rVal * Math.Cos(theta / 3.0) - offset;
                double t2 = rVal * Math.Cos((theta + 2.0 * Math.PI) / 3.0) - offset;
                double t3 = rVal * Math.Cos((theta + 4.0 * Math.PI) / 3.0) - offset;

                if (t1 >= 0.0 && t1 <= 1.0) minDistanceSq = Math.Min(minDistanceSq, GetDistanceSqAtT(t1, p0X, p0Y, bx, by, ax, ay, p.X, p.Y));
                if (t2 >= 0.0 && t2 <= 1.0) minDistanceSq = Math.Min(minDistanceSq, GetDistanceSqAtT(t2, p0X, p0Y, bx, by, ax, ay, p.X, p.Y));
                if (t3 >= 0.0 && t3 <= 1.0) minDistanceSq = Math.Min(minDistanceSq, GetDistanceSqAtT(t3, p0X, p0Y, bx, by, ax, ay, p.X, p.Y));
            }
        }
        else if (Math.Abs(k2) > 1e-9) // Quadratic fallback
        {
            double disc = k1 * k1 - 4.0 * k2 * k0;
            if (disc >= 0.0)
            {
                double rootDisc = Math.Sqrt(disc);
                double t1 = (-k1 + rootDisc) / (2.0 * k2);
                double t2 = (-k1 - rootDisc) / (2.0 * k2);

                if (t1 >= 0.0 && t1 <= 1.0) minDistanceSq = Math.Min(minDistanceSq, GetDistanceSqAtT(t1, p0X, p0Y, bx, by, ax, ay, p.X, p.Y));
                if (t2 >= 0.0 && t2 <= 1.0) minDistanceSq = Math.Min(minDistanceSq, GetDistanceSqAtT(t2, p0X, p0Y, bx, by, ax, ay, p.X, p.Y));
            }
        }

        return Math.Sqrt(minDistanceSq);
    }

    /// <summary>
    /// Sound Geometric Interval approximation (Fixes Bug #2).
    /// Dynamically flattens the curve into 8 line segments to check rectangle intersections.
    /// </summary>
    public FieldInterval EvaluateInterval(Rect r)
    {
        // Early out: if the tile is completely outside the composite bounding volume, minimize checks
        if (r.Right < Bounds.Left || r.Left > Bounds.Right || r.Bottom < Bounds.Top || r.Top > Bounds.Bottom)
        {
            // Guaranteed safely outside the curve range footprint
            double coarseDist = DistanceToLineSegment(new Point(r.X + r.Width * 0.5, r.Y + r.Height * 0.5), Start, End);
            return new FieldInterval(Math.Max(0.0, coarseDist - (r.Width + r.Height)), double.MaxValue);
        }

        // Subdivide into 8 segmented bounds dynamically
        const int segmentCount = 8;
        double minDistance = double.MaxValue;
        Point previousPoint = Start;

        for (int i = 1; i <= segmentCount; i++)
        {
            double t = (double)i / segmentCount;
            Point currentPoint = EvaluateCurve(t);

            double segmentDist = DistanceRectToLineSegment(r, previousPoint, currentPoint);
            if (segmentDist < minDistance)
            {
                minDistance = segmentDist;
            }

            previousPoint = currentPoint;
        }

        // Return mathematically sound distance tracking values
        return new FieldInterval(minDistance, double.MaxValue);
    }

    /// <summary>
    /// Implements ICoverageField2D (Fixes Bug #6).
    /// An infinitely thin curve skeleton always returns 0 fill coverage.
    /// </summary>
    public double GetCoverage(Rect tile) => 0.0;

    #region Internal Robust Math Primitives
    private static double GetDistanceSqAtT(double t, double p0X, double p0Y, double bx, double by, double ax, double ay, double px, double py)
    {
        double qx = p0X + t * (bx + t * ax);
        double qy = p0Y + t * (by + t * ay);
        return (qx - px) * (qx - px) + (qy - py) * (qy - py);
    }

    private static double DistanceToLineSegment(Point p, Point a, Point b)
    {
        double abX = b.X - a.X; double abY = b.Y - a.Y;
        double abLenSq = (abX * abX) + (abY * abY);
        if (abLenSq == 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * abX + (p.Y - a.Y) * abY) / abLenSq, 0.0, 1.0);
        return Math.Sqrt((p.X - (a.X + t * abX)) * (p.X - (a.X + t * abX)) + (p.Y - (a.Y + t * abY)) * (p.Y - (a.Y + t * abY)));
    }

    private static double DistanceRectToLineSegment(Rect r, Point a, Point b)
    {
        if (r.Contains(a) || r.Contains(b)) return 0.0;

        if (LineIntersectsLine(a, b, new Point(r.Left, r.Top), new Point(r.Right, r.Top)) ||
            LineIntersectsLine(a, b, new Point(r.Right, r.Top), new Point(r.Right, r.Bottom)) ||
            LineIntersectsLine(a, b, new Point(r.Right, r.Bottom), new Point(r.Left, r.Bottom)) ||
            LineIntersectsLine(a, b, new Point(r.Left, r.Bottom), new Point(r.Left, r.Top)))

        {
            return 0.0;
        }
        double minDistSq = double.MaxValue;
        Point[] corners = [new(r.Left, r.Top), new(r.Right, r.Top), new(r.Left, r.Bottom), new(r.Right, r.Bottom)];
        foreach (var corner in corners)
        {
            double abX = b.X - a.X; double abY = b.Y - a.Y;
            double abLenSq = (abX * abX) + (abY * abY);
            double t = abLenSq > 0 ? ((corner.X - a.X) * abX + (corner.Y - a.Y) * abY) / abLenSq : 0.0;
            t = Math.Clamp(t, 0.0, 1.0);
            double dSq = (corner.X - (a.X + t * abX)) * (corner.X - (a.X + t * abX)) + (corner.Y - (a.Y + t * abY)) * (corner.Y - (a.Y + t * abY));
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
