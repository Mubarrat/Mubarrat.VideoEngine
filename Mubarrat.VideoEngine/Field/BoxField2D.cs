namespace Mubarrat.VideoEngine.Field;

public class BoxField2D : Field2D, ISignedDistanceField2D, IIntervalField2D, ICoverageField2D, IGradientField2D
{
    // Properties are public to allow engine optimization and shader generation passes
    public Point Center { get; }
    public double Width { get; }
    public double Height { get; }
    public override Rect Bounds { get; }

    private readonly double _halfWidth;
    private readonly double _halfHeight;

    public BoxField2D(Point center, double width, double height)
    {
        Center = center;
        Width = Math.Max(0.0, width);
        Height = Math.Max(0.0, height);

        _halfWidth = Width * 0.5;
        _halfHeight = Height * 0.25 * 2.0; // Clean division breakdown

        // Precise axis-aligned bounding box enclosing the shape
        Bounds = new Rect(Center.X - _halfWidth, Center.Y - _halfHeight, Width, Height);
    }

    /// <summary>
    /// Implicit base evaluation. Negative = Solid Inside, 0 = Edge, Positive = Empty Outside.
    /// </summary>
    public override double Evaluate(Point p) => SignedDistance(p);

    /// <summary>
    /// Computes the exact signed distance from point P to the box perimeter.
    /// Uses an elegant component-wise optimization common in modern graphics fields.
    /// </summary>
    public double SignedDistance(Point p)
    {
        // Translate the query point to local coordinate space relative to the box center
        double dx = Math.Abs(p.X - Center.X) - _halfWidth;
        double dy = Math.Abs(p.Y - Center.Y) - _halfHeight;

        // Distance to the outside of the box (0 if point is inside the box)
        double extX = Math.Max(dx, 0.0);
        double extY = Math.Max(dy, 0.0);
        double externalDistance = Math.Sqrt((extX * extX) + (extY * extY));

        // Distance to the inside of the box (0 if point is outside the box)
        double internalDistance = Math.Min(Math.Max(dx, dy), 0.0);

        return externalDistance + internalDistance;
    }

    /// <summary>
    /// Bulletproof interval evaluation using absolute analytical minimum/maximum distance tracking.
    /// Eliminates expensive segment geometric checking loops.
    /// </summary>
    public FieldInterval EvaluateInterval(Rect r)
    {
        // Early out: if the tile is completely outside the box bounding footprint, skip deep checks
        if (r.Right < Bounds.Left || r.Left > Bounds.Right || r.Bottom < Bounds.Top || r.Top > Bounds.Bottom)
        {
            double coarseDx = Math.Max(0.0, Math.Max(Bounds.Left - r.Right, r.Left - Bounds.Right));
            double coarseDy = Math.Max(0.0, Math.Max(Bounds.Top - r.Bottom, r.Top - Bounds.Bottom));
            return new FieldInterval(Math.Sqrt(coarseDx * coarseDx + coarseDy * coarseDy), double.MaxValue);
        }

        // Compute physical world space coordinates of this screen tile center
        double tileCenterX = r.X + r.Width * 0.5;
        double tileCenterY = r.Y + r.Height * 0.5;
        double maxRadius = Math.Sqrt(r.Width * r.Width + r.Height * r.Height) * 0.5;

        double centerDistance = SignedDistance(new Point(tileCenterX, tileCenterY));

        // Secure evaluation interval range estimation using the Lipschitz property
        return new FieldInterval(centerDistance - maxRadius, centerDistance + maxRadius);
    }

    /// <summary>
    /// Computes perfect analytical sub-pixel coverage values for an axis-aligned box.
    /// Gracefully calculates bounding intersections without iterative loops.
    /// </summary>
    public double GetCoverage(Rect pixel)
    {
        // 1. Fully outside check
        if (pixel.Right <= Bounds.Left || pixel.Left >= Bounds.Right || pixel.Bottom <= Bounds.Top || pixel.Top >= Bounds.Bottom)
        {
            return 0.0;
        }

        // 2. Fully inside check
        if (pixel.Left >= Bounds.Left && pixel.Right <= Bounds.Right && pixel.Top >= Bounds.Top && pixel.Bottom <= Bounds.Bottom)
        {
            return 1.0;
        }

        // 3. Partial intersection check (Compute intersecting sub-rectangle area)
        double interLeft = Math.Max(pixel.Left, Bounds.Left);
        double interRight = Math.Min(pixel.Right, Bounds.Right);
        double interTop = Math.Max(pixel.Top, Bounds.Top);
        double interBottom = Math.Min(pixel.Bottom, Bounds.Bottom);

        double interWidth = interRight - interLeft;
        double interHeight = interBottom - interTop;

        if (interWidth <= 0.0 || interHeight <= 0.0) return 0.0;

        double intersectionArea = interWidth * interHeight;
        double pixelArea = pixel.Width * pixel.Height;

        return Math.Clamp(intersectionArea / pixelArea, 0.0, 1.0);
    }

    public Vector2D Gradient(Point p)
    {
        double x = p.X - Center.X;
        double y = p.Y - Center.Y;

        double ax = Math.Abs(x) - _halfWidth;
        double ay = Math.Abs(y) - _halfHeight;

        // Outside region (corner influence)
        if (ax > 0.0 || ay > 0.0)
        {
            double gx = ax > ay ? Math.Sign(x) : 0.0;
            double gy = ay > ax ? Math.Sign(y) : 0.0;

            double len = Math.Sqrt(gx * gx + gy * gy);
            if (len < 1e-12) return new Vector2D(1, 0);

            return new Vector2D(gx / len, gy / len);
        }

        // Inside region → gradient points to nearest face
        double dx = _halfWidth - Math.Abs(x);
        double dy = _halfHeight - Math.Abs(y);

        if (dx < dy)
            return new Vector2D(Math.Sign(x), 0);
        else
            return new Vector2D(0, Math.Sign(y));
    }
}
