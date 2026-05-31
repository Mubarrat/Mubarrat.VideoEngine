using Mubarrat.VideoEngine.Immutable;

namespace Mubarrat.VideoEngine.Path;

public class PathBuilder(IEnumerable<PathSegment> segs)
{
    public PathBuilder() : this([]) {}

    public readonly List<PathSegment> Segments = [.. segs];

    public PathBuilder MoveTo(Point p)
    {
        Segments.Add(new MoveSegment(p));
        return this;
    }

    public PathBuilder LineTo(Point p)
    {
        Segments.Add(new LineSegment(p));
        return this;
    }

    public PathBuilder HorizontalLineTo(double x)
    {
        Segments.Add(new LineSegment(new(x, Segments[^1].Points[^1].Y)));
        return this;
    }

    public PathBuilder VerticalLineTo(double y)
    {
        Segments.Add(new LineSegment(new(Segments[^1].Points[^1].X, y)));
        return this;
    }

    public PathBuilder CubicTo(Point cp1, Point cp2, Point end)
    {
        Segments.Add(new CubicSegment(cp1, cp2, end));
        return this;
    }

    public PathBuilder CubicSmoothTo(Point cp2, Point end)
    {
        if (Segments.Count == 0)
            return CubicTo(new(), cp2, end);
        var last = Segments[^1];
        var current = last.Points[^1];
        return CubicTo(last is CubicSegment c ? (Point)(current * 2 - c.Control2) : current, cp2, end);
    }

    public PathBuilder QuadraticTo(Point cp, Point end)
    {
        Segments.Add(new QuadraticSegment(cp, end));
        return this;
    }

    public PathBuilder QuadraticSmoothTo(Point end)
    {
        if (Segments.Count == 0) return QuadraticTo(new(), end);
        var last = Segments[^1];
        var current = last.Points[^1];
        return QuadraticTo(last is QuadraticSegment q ? (Point)(current * 2 - q.Control) : current, end);
    }

    public PathBuilder ArcTo(double radiusX, double radiusY, double xAxisRotation, bool largeArcFlag, bool sweepFlag, Point end)
    {
        if (Segments.Count == 0)
            return this;

        var start = Segments[^1].Points[^1];

        foreach (var cubic in ArcToCubic(start, radiusX, radiusY, xAxisRotation, largeArcFlag, sweepFlag, end))
        {
            Segments.Add(cubic);
            start = cubic.End;
        }

        return this;
    }

    public PathBuilder Close()
    {
        if (Segments.Count == 0)
            return this;

        Point target = default;
        bool hasTarget = false;

        for (int i = Segments.Count - 1; i >= 0; i--)
        {
            if (Segments[i] is MoveSegment { Point: var point })
            {
                target = point;
                hasTarget = true;
                break;
            }
        }

        if (!hasTarget)
            return this;

        Point current = Segments[^1].Points[^1];
        const double closeTolerance = 0.01;
        if (current.DistanceTo(target) > closeTolerance)
            Segments.Add(new LineSegment(target));

        return this;
    }

    public override string ToString()
    {
        var s = string.Join(" | ", Segments);
        return $"PathBuilder[{s}]";
    }

    public static PathBuilder Rectangle(Rect rect) => Polygon(rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft);

    public static PathBuilder Line(Point start, Point end) => new PathBuilder().MoveTo(start).LineTo(end);

    public static readonly double CircleConstant = 4.0 / 3.0 * Math.Tan(Math.PI / 8.0);
    public static PathBuilder Circle(Point center, double radius)
    {
        double c = radius * CircleConstant;
        Point top = new(center.X, center.Y - radius), right = new(center.X + radius, center.Y),
            bottom = new(center.X, center.Y + radius), left = new(center.X - radius, center.Y);
        return CubicBezier(top, new(top.X + c, top.Y), new(right.X, right.Y - c), right)
            .CubicTo(new(right.X, right.Y + c), new(bottom.X + c, bottom.Y), bottom)
            .CubicTo(new(bottom.X - c, bottom.Y), new(left.X, left.Y + c), left)
            .CubicTo(new(left.X, left.Y - c), new(top.X - c, top.Y), top)
            .Close();
    }

    public static PathBuilder Ellipse(Point center, double radiusX, double radiusY)
    {
        double cx = radiusX * CircleConstant, cy = radiusY * CircleConstant;
        Point top = new(center.X, center.Y - radiusY), right = new(center.X + radiusX, center.Y),
            bottom = new(center.X, center.Y + radiusY), left = new(center.X - radiusX, center.Y);
        return CubicBezier(top, new(top.X + cx, top.Y), new(right.X, right.Y - cy), right)
            .CubicTo(new(right.X, right.Y + cy), new(bottom.X + cx, bottom.Y), bottom)
            .CubicTo(new(bottom.X - cx, bottom.Y), new(left.X, left.Y + cy), left)
            .CubicTo(new(left.X, left.Y - cy), new(top.X - cx, top.Y), top)
            .Close();
    }

    public static PathBuilder Polygon(params Point[] points)
    {
        var p = new PathBuilder();
        if (points.Length == 0) return p;
        p.MoveTo(points[0]);
        for (int i = 1; i < points.Length; i++)
            p.LineTo(points[i]);
        return p.Close();
    }

    public static PathBuilder CubicBezier(Point p0, Point p1, Point p2, Point p3) => new PathBuilder().MoveTo(p0).CubicTo(p1, p2, p3);

    public static PathBuilder QuadraticBezier(Point p0, Point p1, Point p2) => new PathBuilder().MoveTo(p0).QuadraticTo(p1, p2);

    public static PathBuilder Arc(Point start, double radiusX, double radiusY, double xAxisRotation, bool largeArcFlag, bool sweepFlag, Point end) =>
        new PathBuilder().MoveTo(start).ArcTo(radiusX, radiusY, xAxisRotation, largeArcFlag, sweepFlag, end);

    public static PathBuilder RoundedRectangle(Rect rect, double radius)
    {
        double x0 = rect.X;
        double y0 = rect.Y;
        double x1 = rect.X + rect.Width;
        double y1 = rect.Y + rect.Height;

        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);

        double width = x1 - x0;
        double height = y1 - y0;

        if (width == 0 || height == 0)
            return new PathBuilder().MoveTo(new(x0, y0));

        radius = Math.Clamp(radius, 0, Math.Min(width, height) / 2);
        if (radius == 0)
            return Rectangle(new(x0, y0, width, height));

        double c = radius * CircleConstant;

        // Points along rectangle edges
        Point tl = new(x0 + radius, y0);
        Point tr = new(x1 - radius, y0);
        Point br = new(x1 - radius, y1);
        Point bl = new(x0 + radius, y1);

        // Exact corner points
        Point topRightCorner = new(x1, y0 + radius);
        Point bottomRightCorner = new(x1, y1 - radius);
        Point bottomLeftCorner = new(x0, y1 - radius);
        Point topLeftCorner = new(x0, y0 + radius);

        return CubicBezier(topLeftCorner, new(topLeftCorner.X, topLeftCorner.Y - c), new(tl.X - c, tl.Y), tl)
            .LineTo(tr)
            .CubicTo(new(tr.X + c, tr.Y), new(topRightCorner.X, topRightCorner.Y - c), topRightCorner)
            .LineTo(bottomRightCorner)
            .CubicTo(new(bottomRightCorner.X, bottomRightCorner.Y + c), new(br.X + c, br.Y), br)
            .LineTo(bl)
            .CubicTo(new(bl.X - c, bl.Y), new(bottomLeftCorner.X, bottomLeftCorner.Y + c), bottomLeftCorner)
            .Close();
    }

    public static PathBuilder Star(Point center, int points, double outerRadius, double innerRadius)
    {
        double angleStep = Math.PI / points;
        Point[] verts = new Point[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            double r = (i % 2 == 0) ? outerRadius : innerRadius, angle = i * angleStep - Math.PI / 2;
            verts[i] = new(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle));
        }
        return Polygon(verts);
    }

    public Path2D Build(bool isNonZeroFill = true)
    {
        if (Segments.Count == 0)
            return new Path2D(isNonZeroFill);
        List<Subpath> subpaths = new(4);
        List<Edge> edges = new(64);
        Point current = default;
        for (int i = 0; i < Segments.Count; i++)
        {
            var seg = Segments[i];

            switch (seg)
            {
                case MoveSegment m:
                    {
                        if (edges.Count > 0)
                        {
                            subpaths.Add(new Subpath([.. edges]));
                            edges.Clear();
                        }
                        current = m.Point;
                        break;
                    }

                case LineSegment l:
                    edges.Add(new Edge(current, current = l.Point));
                    break;

                case QuadraticSegment q:
                    {
                        FlattenQuadratic(current, q.Control, q.End, edges);
                        current = q.End;
                        break;
                    }

                case CubicSegment c:
                    {
                        FlattenCubic(current, c.Control1, c.Control2, c.End, edges);
                        current = c.End;
                        break;
                    }
            }
        }
        if (edges.Count > 0)
            subpaths.Add(new Subpath([.. edges]));
        return new Path2D(isNonZeroFill, [.. subpaths]);
    }

    private static void FlattenQuadratic(Point p0, Point p1, Point p2, List<Edge> edges)
        => FlattenQuadratic(p0, p1, p2, edges, 0);

    private const double FlattenTolerance = 0.01;
    private const int MaxFlattenDepth = 20;

    private static void FlattenQuadratic(Point p0, Point p1, Point p2, List<Edge> edges, int depth)
    {
        if (depth >= MaxFlattenDepth || IsQuadraticFlatEnough(p0, p1, p2))
        {
            edges.Add(new Edge(p0, p2));
            return;
        }

        var p01 = Midpoint(p0, p1);
        var p12 = Midpoint(p1, p2);
        var p012 = Midpoint(p01, p12);

        FlattenQuadratic(p0, p01, p012, edges, depth + 1);
        FlattenQuadratic(p012, p12, p2, edges, depth + 1);
    }

    private static void FlattenCubic(Point p0, Point p1, Point p2, Point p3, List<Edge> edges)
        => FlattenCubic(p0, p1, p2, p3, edges, 0);

    private static void FlattenCubic(Point p0, Point p1, Point p2, Point p3, List<Edge> edges, int depth)
    {
        if (depth >= MaxFlattenDepth || IsCubicFlatEnough(p0, p1, p2, p3))
        {
            edges.Add(new Edge(p0, p3));
            return;
        }

        var p01 = Midpoint(p0, p1);
        var p12 = Midpoint(p1, p2);
        var p23 = Midpoint(p2, p3);
        var p012 = Midpoint(p01, p12);
        var p123 = Midpoint(p12, p23);
        var p0123 = Midpoint(p012, p123);

        FlattenCubic(p0, p01, p012, p0123, edges, depth + 1);
        FlattenCubic(p0123, p123, p23, p3, edges, depth + 1);
    }

    private static Point Midpoint(in Point a, in Point b) => new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

    private static bool IsQuadraticFlatEnough(Point p0, Point p1, Point p2)
    {
        double chord = p0.DistanceTo(p2);
        double midpointError = DistanceToLine(QuadraticPoint(p0, p1, p2, 0.5), p0, p2);
        return DistanceToLine(p1, p0, p2) <= FlattenTolerance && midpointError <= FlattenTolerance && chord >= 0;
    }

    private static bool IsCubicFlatEnough(Point p0, Point p1, Point p2, Point p3)
    {
        double midpointError = DistanceToLine(CubicPoint(p0, p1, p2, p3, 0.5), p0, p3);
        return Math.Max(DistanceToLine(p1, p0, p3), DistanceToLine(p2, p0, p3)) <= FlattenTolerance
            && midpointError <= FlattenTolerance;
    }

    private static Point QuadraticPoint(Point p0, Point p1, Point p2, double t)
    {
        double u = 1 - t;
        double x = (u * u * p0.X) + (2 * u * t * p1.X) + (t * t * p2.X);
        double y = (u * u * p0.Y) + (2 * u * t * p1.Y) + (t * t * p2.Y);
        return new Point(x, y);
    }

    private static Point CubicPoint(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double u = 1 - t;
        double uu = u * u;
        double tt = t * t;
        double x = (uu * u * p0.X) + (3 * uu * t * p1.X) + (3 * u * tt * p2.X) + (tt * t * p3.X);
        double y = (uu * u * p0.Y) + (3 * uu * t * p1.Y) + (3 * u * tt * p2.Y) + (tt * t * p3.Y);
        return new Point(x, y);
    }

    private static double DistanceToLine(Point p, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;

        if (dx == 0 && dy == 0)
            return p.DistanceTo(a);

        double num = Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx);
        double den = Math.Sqrt((dx * dx) + (dy * dy));
        return num / den;
    }

    private static IEnumerable<CubicSegment> ArcToCubic(
        Point start,
        double rx, double ry,
        double rotation,
        bool largeArc,
        bool sweep,
        Point end)
    {
        if (rx == 0 || ry == 0 || start == end)
        {
            yield return new CubicSegment(start, end, end);
            yield break;
        }

        double angle = rotation * Math.PI / 180.0;
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        // Step 1: transform
        double dx = (start.X - end.X) / 2;
        double dy = (start.Y - end.Y) / 2;

        double x1p = cos * dx + sin * dy;
        double y1p = -sin * dx + cos * dy;

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);

        double rxSq = rx * rx;
        double rySq = ry * ry;
        double x1pSq = x1p * x1p;
        double y1pSq = y1p * y1p;

        // Fix radii
        double lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1)
        {
            double s = Math.Sqrt(lambda);
            rx *= s;
            ry *= s;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        // Step 2: center
        double sign = (largeArc == sweep) ? -1 : 1;

        double sq = Math.Max(0,
            (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) /
            (rxSq * y1pSq + rySq * x1pSq));

        double coef = sign * Math.Sqrt(sq);

        double cxp = coef * (rx * y1p / ry);
        double cyp = coef * (-ry * x1p / rx);

        double cx = cos * cxp - sin * cyp + (start.X + end.X) / 2;
        double cy = sin * cxp + cos * cyp + (start.Y + end.Y) / 2;

        // Step 3: angles
        static double Angle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            double ang = Math.Acos(Math.Clamp(dot / len, -1, 1));
            if (ux * vy - uy * vx < 0) ang = -ang;
            return ang;
        }

        double theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double delta = Angle(
            (x1p - cxp) / rx, (y1p - cyp) / ry,
            (-x1p - cxp) / rx, (-y1p - cyp) / ry);

        if (!sweep && delta > 0) delta -= Math.Tau;
        if (sweep && delta < 0) delta += Math.Tau;

        int segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2));
        double step = delta / segments;

        for (int i = 0; i < segments; i++)
        {
            double t1 = theta1 + i * step;
            double t2 = t1 + step;

            yield return ArcSegmentToCubic(cx, cy, rx, ry, angle, t1, t2);
        }
    }

    private static CubicSegment ArcSegmentToCubic(
        double cx, double cy,
        double rx, double ry,
        double angle,
        double t1, double t2)
    {
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        double dt = (t2 - t1) / 3;

        Point P(double t)
        {
            double ct = Math.Cos(t);
            double st = Math.Sin(t);

            return new Point(
                cx + rx * ct * cos - ry * st * sin,
                cy + rx * ct * sin + ry * st * cos
            );
        }

        var p0 = P(t1);
        var p3 = P(t2);

        var p1 = P(t1 + dt);
        var p2 = P(t2 - dt);

        return new CubicSegment(p1, p2, p3);
    }
}
