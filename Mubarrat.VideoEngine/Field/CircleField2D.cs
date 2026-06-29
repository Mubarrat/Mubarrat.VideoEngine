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

    /// <summary>
    /// Computes perfect analytical sub-pixel coverage values. 
    /// Gracefully bypasses pixel-by-pixel subdivision logic using the circle's implicit distance metrics.
    /// </summary>
    public double GetCoverage(Rect pixel)
    {
        // Find the closest point inside the pixel box to the circle center
        double closestX = Math.Clamp(Center.X, pixel.Left, pixel.Right);
        double closestY = Math.Clamp(Center.Y, pixel.Top, pixel.Bottom);

        // Calculate the maximum distance from the circle center to any corner of this pixel
        double maxDx = Math.Max(Math.Abs(pixel.Left - Center.X), Math.Abs(pixel.Right - Center.X));
        double maxDy = Math.Max(Math.Abs(pixel.Top - Center.Y), Math.Abs(pixel.Bottom - Center.Y));
        double maxCornerDistance = Math.Sqrt((maxDx * maxDx) + (maxDy * maxDy));

        // Case 1: The entire pixel box is buried inside the circle solid volume
        if (maxCornerDistance <= Radius)
        {
            return 1.0;
        }

        // Calculate the minimum distance from the pixel box edges to the circle center
        double minDx = Center.X < pixel.Left ? pixel.Left - Center.X : (Center.X > pixel.Right ? Center.X - pixel.Right : 0.0);
        double minDy = Center.Y < pixel.Top ? pixel.Top - Center.Y : (Center.Y > pixel.Bottom ? Center.Y - pixel.Bottom : 0.0);
        double minEdgeDistance = Math.Sqrt((minDx * minDx) + (minDy * minDy));

        // Case 2: The entire pixel box is completely outside the circle footprint
        if (minEdgeDistance >= Radius)
        {
            return 0.0;
        }

        // Case 3: The circle boundary slices directly through this single pixel unit.
        // We calculate the exact center distance of the pixel to determine the anti-aliased blend coefficient.
        double pixelCenterX = pixel.X + pixel.Width * 0.5;
        double pixelCenterY = pixel.Y + pixel.Height * 0.5;

        double centerDx = pixelCenterX - Center.X;
        double centerDy = pixelCenterY - Center.Y;
        double centerDistance = Math.Sqrt((centerDx * centerDx) + (centerDy * centerDy));

        // Shift metrics relative to the circle circumference threshold
        double edgeDistance = centerDistance - Radius;

        // Calculate the physical size/diagonal span of the pixel for filter scaling
        double pixelHalfSpan = Math.Sqrt(pixel.Width * pixel.Width + pixel.Height * pixel.Height) * 0.5;

        // Map coverage linearly from 1.0 (inside) to 0.0 (outside) over the sub-pixel diagonal region
        double coverage = 0.5 - (edgeDistance / (pixelHalfSpan * 2.0));

        return Math.Clamp(coverage, 0.0, 1.0);
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
