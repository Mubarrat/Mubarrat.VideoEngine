namespace Mubarrat.VideoEngine.Field;

public class CircleField2D : Field2D, ISignedDistanceField2D, IIntervalField2D, ICoverageField2D, IGradientField2D
{
    // Properties are public to allow engine optimization and shader generation passes
    public Point Center { get; }
    public double Radius { get; }
    public override Rect Bounds { get; }

    public CircleField2D(Point center, double radius)
    {
        Center = center;
        Radius = Math.Max(0.0, radius);

        // Precise bounding box encapsulating the circle profile
        double diameter = Radius * 2.0;
        Bounds = new Rect(Center.X - Radius, Center.Y - Radius, diameter, diameter);
    }

    /// <summary>
    /// Implicit base evaluation. Negative = Solid Inside, 0 = Edge, Positive = Empty Outside.
    /// </summary>
    public override double Evaluate(Point p) => SignedDistance(p);

    /// <summary>
    /// Computes the exact signed distance from point P to the circle boundary shell.
    /// </summary>
    public double SignedDistance(Point p)
    {
        double dx = p.X - Center.X;
        double dy = p.Y - Center.Y;
        double distanceToCenter = Math.Sqrt((dx * dx) + (dy * dy));

        // Subtracting the radius shifts the boundary (0.0) precisely to the circle circumference
        return distanceToCenter - Radius;
    }

    /// <summary>
    /// Bulletproof interval evaluation using analytical minimum and maximum distance tracking.
    /// This eliminates the need for expensive geometric flattening loops.
    /// </summary>
    public FieldInterval EvaluateInterval(Rect r)
    {
        // Early out: if the tile is completely outside the circle's bounding box footprint, skip deep checks
        if (r.Right < Bounds.Left || r.Left > Bounds.Right || r.Bottom < Bounds.Top || r.Top > Bounds.Bottom)
        {
            double coarseDx = Math.Max(0.0, Math.Max(Bounds.Left - r.Right, r.Left - Bounds.Right));
            double coarseDy = Math.Max(0.0, Math.Max(Bounds.Top - r.Bottom, r.Top - Bounds.Bottom));
            return new FieldInterval(Math.Sqrt(coarseDx * coarseDx + coarseDy * coarseDy), double.MaxValue);
        }

        // 1. Calculate the minimum absolute distance from the rect edges to the circle center
        double minDx = Math.Max(0.0, Math.Max(r.Left - Center.X, Center.X - r.Right));
        double minDy = Math.Max(0.0, Math.Max(r.Top - Center.Y, Center.Y - r.Bottom));
        double minDistanceToCenter = Math.Sqrt((minDx * minDx) + (minDy * minDy));

        // 2. Calculate the maximum distance from the rect to the circle center
        // The furthest point from a center inside a rectangle is always one of the 4 sharp corners
        double maxDx = Math.Max(Math.Abs(r.Left - Center.X), Math.Abs(r.Right - Center.X));
        double maxDy = Math.Max(Math.Abs(r.Top - Center.Y), Math.Abs(r.Bottom - Center.Y));
        double maxDistanceToCenter = Math.Sqrt((maxDx * maxDx) + (maxDy * maxDy));

        // Subtract the radius to convert center-point metrics into implicit field thresholds
        return new FieldInterval(minDistanceToCenter - Radius, maxDistanceToCenter - Radius);
    }

    public double GetCoverage(Rect pixel)
    {
        double x0 = pixel.Left;
        double x1 = pixel.Right;
        double y0 = pixel.Top;
        double y1 = pixel.Bottom;

        double cx = Center.X;
        double cy = Center.Y;
        double r = Radius;

        double pixelArea = (x1 - x0) * (y1 - y0);

        // ---- reject
        if (x1 < cx - r || x0 > cx + r || y1 < cy - r || y0 > cy + r)
            return 0.0;

        // ---- full containment
        if (ContainsFully(pixel, cx, cy, r))
            return 1.0;

        // ---- clamp x to circle domain
        double lx = Math.Max(x0, cx - r);
        double rx = Math.Min(x1, cx + r);

        if (lx >= rx)
            return 0.0;

        double area =
            XIntegral(rx, cx, r) -
            XIntegral(lx, cx, r);

        // normalize by rectangle area
        return Math.Clamp(area / pixelArea, 0.0, 1.0);
    }

    private static bool ContainsFully(Rect pixel, double cx, double cy, double r)
    {
        double r2 = r * r;
        return DistanceSquared(pixel.Left, pixel.Top, cx, cy) <= r2
            && DistanceSquared(pixel.Right, pixel.Top, cx, cy) <= r2
            && DistanceSquared(pixel.Left, pixel.Bottom, cx, cy) <= r2
            && DistanceSquared(pixel.Right, pixel.Bottom, cx, cy) <= r2;
    }

    private static double XIntegral(double x, double cx, double r)
    {
        double h = x - cx;

        if (h <= -r) return 0.0;
        if (h >= r) return Math.PI * r * r;

        double t = Math.Sqrt(r * r - h * h);

        return
            h * t
            + r * r * Math.Asin(h / r);
    }

    private static double DistanceSquared(double x, double y, double cx, double cy)
    {
        double dx = x - cx;
        double dy = y - cy;
        return dx * dx + dy * dy;
    }

    public Vector2D Gradient(Point p)
    {
        double dx = p.X - Center.X;
        double dy = p.Y - Center.Y;

        double lenSq = dx * dx + dy * dy;

        if (lenSq < 1e-20)
            return new Vector2D(1, 0); // stable fallback at center

        double invLen = 1.0 / Math.Sqrt(lenSq);

        // gradient of (|p-c| - r) == normalized direction from center
        return new Vector2D(dx * invLen, dy * invLen);
    }
}
