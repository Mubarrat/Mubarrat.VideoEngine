namespace Mubarrat.VideoEngine.Fields2D;

public class TransformField2D(Field2D child, Matrix2D transform) : Field2D, IUnaryField2D, IIntervalField2D, ICoverageField2D
{
    public Field2D Child { get; } = child ?? throw new ArgumentNullException(nameof(child));

    // The forward matrix transforming the shape (Local -> World)
    public Matrix2D Transform { get; } = transform;

    // The inverse matrix transforming sample coordinates (World -> Local)
    public Matrix2D InverseTransform { get; } = transform.Inverse;

    public override Rect Bounds { get; } = child.Bounds * transform;

    /// <summary>
    /// Transforms the incoming world sample point into local space via the Inverse Matrix.
    /// Row Vector Convention: Point * Matrix
    /// </summary>
    public override double Evaluate(Point p)
    {
        return Child.Evaluate(p * InverseTransform);
    }

    /// <summary>
    /// Transforms the screen tile footprint into local coordinate space to test interval safety bounds.
    /// </summary>
    public FieldInterval EvaluateInterval(Rect r)
    {
        if (Child is IIntervalField2D intervalChild)
        {
            // Transform the world-space pixel tile bounding box into an axis-aligned bounding box 
            // in local space using row vector multiplication to safely query the child interval.
            return intervalChild.EvaluateInterval(r * InverseTransform);
        }

        return new FieldInterval(double.NegativeInfinity, double.PositiveInfinity);
    }

    /// <summary>
    /// Computes transformed sub-pixel coverage.
    /// Employs a local filtering width derivation matching your specific row layout.
    /// </summary>
    public double GetCoverage(Rect pixel)
    {
        // 1. Core macro-bounds optimization check
        if (pixel.Right <= Bounds.Left || pixel.Left >= Bounds.Right || pixel.Bottom <= Bounds.Top || pixel.Top >= Bounds.Bottom)
        {
            return 0.0;
        }

        // 2. If the child supports direct coverage calculations, transform the pixel footprint context
        if (Child is ICoverageField2D coverageChild)
        {
            // Find the center of the world space pixel
            double worldCenterX = pixel.X + pixel.Width * 0.5;
            double worldCenterY = pixel.Y + pixel.Height * 0.5;
            Point worldCenter = new(worldCenterX, worldCenterY);

            // Map pixel center to the local coordinate system (Row Vector: Point * Matrix)
            Point localCenter = worldCenter * InverseTransform;

            // Matching your explicit layout matrix mapping:
            // Row 1 elements (ScaleX, SkewX) determine how the local X basis spans.
            // Row 2 elements (SkewY, ScaleY) determine how the local Y basis spans.
            double localWidth = pixel.Width * Math.Sqrt(InverseTransform.ScaleX * InverseTransform.ScaleX + InverseTransform.SkewX * InverseTransform.SkewX);
            double localHeight = pixel.Height * Math.Sqrt(InverseTransform.SkewY * InverseTransform.SkewY + InverseTransform.ScaleY * InverseTransform.ScaleY);

            // Construct a local tracking frame rectangle to pass downward
            Rect localPixelRect = new Rect(
                localCenter.X - localWidth * 0.5,
                localCenter.Y - localHeight * 0.5,
                localWidth,
                localHeight
            );

            return coverageChild.GetCoverage(localPixelRect);
        }

        // Fallback: Sample center if the child is missing a coverage interface completely
        Point centerPt = new(pixel.X + pixel.Width * 0.5, pixel.Y + pixel.Height * 0.5);
        return Evaluate(centerPt) <= 0.0 ? 1.0 : 0.0;
    }
}
