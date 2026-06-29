using Mubarrat.VideoEngine.Fields2D;

namespace Mubarrat.VideoEngine.Path;

public class PathBuilder(IEnumerable<IPathSegment> segs)
{
    private readonly List<IPathSegment> segments = [.. segs ?? []];

    public PathBuilder() : this([]) { }

    public override string ToString() => $"PathBuilder[{string.Join(" | ", segments)}]";

    public PathBuilder MoveTo(Point point) { segments.Add(new MoveSegment(point)); return this; }

    public PathBuilder LineTo(Point point) { segments.Add(new LineSegment(segments[^1].End, point)); return this; }
    public PathBuilder HorizontalLineTo(double x) => LineTo(new(x, segments[^1].End.Y));
    public PathBuilder VerticalLineTo(double y) => LineTo(new(segments[^1].End.X, y));
    public static PathBuilder Line(Point start, Point end) => new PathBuilder().MoveTo(start).LineTo(end);

    public PathBuilder QuadraticTo(Point control, Point end) { segments.Add(new QuadraticSegment(segments[^1].End, control, end)); return this; }
    public static PathBuilder QuadraticBezier(Point start, Point control, Point end) => new PathBuilder().MoveTo(start).QuadraticTo(control, end);

    public PathBuilder CubicTo(Point control1, Point control2, Point end) { segments.Add(new CubicSegment(segments[^1].End, control1, control2, end)); return this; }
    public static PathBuilder CubicBezier(Point start, Point control1, Point control2, Point end) => new PathBuilder().MoveTo(start).CubicTo(control1, control2, end);

    public PathBuilder QuadraticSmoothTo(Point end)
    {
        if (segments.Count == 0) return QuadraticTo(new(), end);
        var last = segments[^1];
        var current = last.End;
        return QuadraticTo(last is QuadraticSegment q ? (Point)(current * 2 - q.Control) : current, end);
    }

    public PathBuilder CubicSmoothTo(Point control2, Point end)
    {
        if (segments.Count == 0) return CubicTo(new(), control2, end);
        var last = segments[^1];
        var current = last.End;
        return CubicTo(last is CubicSegment c ? (Point)(current * 2 - c.Control2) : current, control2, end);
    }

    //public PathBuilder ArcTo(double radiusX, double radiusY, double xAxisRotation, bool largeArcFlag, bool sweepFlag, Point end)
    //{
    //    if (segments.Count == 0) return this;
    //    segments.Add(new ArcSegment(radiusX, radiusY, xAxisRotation, largeArcFlag, sweepFlag, end));
    //    return this;
    //}

    public PathBuilder Close()
    {
        if (segments.Count == 0) return this;

        Point target = default;
        bool hasTarget = false;
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (segments[i] is MoveSegment { Point: var pt }) { target = pt; hasTarget = true; break; }
        }
        if (!hasTarget) return this;

        Point current = segments[^1].End;
        if (current.DistanceTo(target) > 0.01)
            segments.Add(new LineSegment(current, target));

        return this;
    }

    public static PathBuilder Rectangle(Rect rect) => Polygon(rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft);


    public static readonly double CircleConstant = 4.0 / 3.0 * Math.Tan(Math.PI / 8.0);
    public static PathBuilder Circle(Point center, double radius)
    {
        double c = radius * CircleConstant;
        Point top = new(center.X, center.Y - radius), right = new(center.X + radius, center.Y),
               bot = new(center.X, center.Y + radius), left = new(center.X - radius, center.Y);
        return CubicBezier(top, new(top.X + c, top.Y), new(right.X, right.Y - c), right)
            .CubicTo(new(right.X, right.Y + c), new(bot.X + c, bot.Y), bot)
            .CubicTo(new(bot.X - c, bot.Y), new(left.X, left.Y + c), left)
            .CubicTo(new(left.X, left.Y - c), new(top.X - c, top.Y), top)
            .Close();
    }

    public static PathBuilder Ellipse(Point center, double radiusX, double radiusY)
    {
        double cx = radiusX * CircleConstant, cy = radiusY * CircleConstant;
        Point top = new(center.X, center.Y - radiusY), right = new(center.X + radiusX, center.Y),
               bot = new(center.X, center.Y + radiusY), left = new(center.X - radiusX, center.Y);
        return CubicBezier(top, new(top.X + cx, top.Y), new(right.X, right.Y - cy), right)
            .CubicTo(new(right.X, right.Y + cy), new(bot.X + cx, bot.Y), bot)
            .CubicTo(new(bot.X - cx, bot.Y), new(left.X, left.Y + cy), left)
            .CubicTo(new(left.X, left.Y - cy), new(top.X - cx, top.Y), top)
            .Close();
    }

    public static PathBuilder Polygon(params Point[] points)
    {
        var p = new PathBuilder();
        if (points.Length == 0) return p;
        p.MoveTo(points[0]);
        for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
        return p.Close();
    }

    public PathBuilder PolygonTo(params Point[] points)
    {
        if (points.Length == 0) return this;
        for (int i = 0; i < points.Length; i++) LineTo(points[i]);
        return points.Length == 1 ? this : Close();
    }

    //public static PathBuilder Arc(Point start, double radiusX, double radiusY,
    //    double xAxisRotation, bool largeArcFlag, bool sweepFlag, Point end) =>
    //    new PathBuilder().MoveTo(start).ArcTo(radiusX, radiusY, xAxisRotation, largeArcFlag, sweepFlag, end);

    public static PathBuilder RoundedRectangle(Rect rect, double radius)
    {
        double x0 = rect.X, y0 = rect.Y, x1 = rect.X + rect.Width, y1 = rect.Y + rect.Height;
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);
        double w = x1 - x0, h = y1 - y0;
        if (w == 0 || h == 0) return new PathBuilder().MoveTo(new(x0, y0));
        radius = Math.Clamp(radius, 0, Math.Min(w, h) / 2);
        if (radius == 0) return Rectangle(new(x0, y0, w, h));

        double c = radius * CircleConstant;
        Point tl = new(x0 + radius, y0), tr = new(x1 - radius, y0),
               br = new(x1 - radius, y1), bl = new(x0 + radius, y1);
        Point trc = new(x1, y0 + radius), brc = new(x1, y1 - radius),
               blc = new(x0, y1 - radius), tlc = new(x0, y0 + radius);

        return CubicBezier(tlc, new(tlc.X, tlc.Y - c), new(tl.X - c, tl.Y), tl)
            .LineTo(tr)
            .CubicTo(new(tr.X + c, tr.Y), new(trc.X, trc.Y - c), trc)
            .LineTo(brc)
            .CubicTo(new(brc.X, brc.Y + c), new(br.X + c, br.Y), br)
            .LineTo(bl)
            .CubicTo(new(bl.X - c, bl.Y), new(blc.X, blc.Y + c), blc)
            .Close();
    }

    public static PathBuilder Star(Point center, int points, double outerRadius, double innerRadius)
    {
        double step = Math.PI / points;
        var verts = new Point[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            double r = (i % 2 == 0) ? outerRadius : innerRadius;
            double a = i * step - Math.PI / 2;
            verts[i] = new(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));
        }
        return Polygon(verts);
    }

    // ── Build ─────────────────────────────────────────────────────────────────
    public Path2D BuildPath(bool nonZero) => BuildPath(nonZero ? FillRule.NonZero : FillRule.EvenOdd);
    public Path2D BuildPath(FillRule fillRule = FillRule.NonZero)
    {
        var contours = new List<PathContour>();
        var current = new List<IPathSegment>(32);

        int count = segments.Count;

        for (int i = 0; i < count; i++)
        {
            var seg = segments[i];

            if (seg is MoveSegment)
            {
                if (current.Count > 0)
                {
                    contours.Add(new PathContour(current));
                    current = new List<IPathSegment>(32);
                }

                continue;
            }

            current.Add(seg);
        }

        if (current.Count > 0)
            contours.Add(new PathContour(current));

        return new Path2D(fillRule, contours);
    }

    public Field2D BuildField(FillRule fillRule = FillRule.NonZero) => BuildField(fillRule == FillRule.NonZero);
    public Field2D BuildField(bool nonZero)
    {
        var segs = segments.ToArray();
        return new CompiledField2D(CurveJitCompiler.Compile(segs, nonZero), Rect.Union(Array.ConvertAll(segs, x => x.Bounds)));
    }
}
