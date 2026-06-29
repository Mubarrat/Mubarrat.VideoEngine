using Mubarrat.VideoEngine.Field;

namespace Mubarrat.VideoEngine.Path;

internal sealed class CompiledField2D(CompiledShape shape, Rect bounds) : Field2D, ISignedDistanceField2D, IIntervalField2D, ICoverageField2D, IGradientField2D
{
    public override Rect Bounds => bounds;

    public override double Evaluate(Point p) => SignedDistance(p);

    public double SignedDistance(Point p)
    {
        double dist = shape.Distance(p.X, p.Y);
        int w = shape.Winding(p.X, p.Y);
        return w != 0 ? -dist : dist;
    }

    public FieldInterval EvaluateInterval(Rect r)
    {
        double cx = r.X + r.Width * 0.5;
        double cy = r.Y + r.Height * 0.5;
        double dist = shape.Distance(cx, cy);
        double half = Math.Sqrt(r.Width * r.Width + r.Height * r.Height) * 0.5;
        return new FieldInterval(dist - half, dist + half);
    }

    public double GetCoverage(Rect pixel)
    {
        double d = SignedDistance(pixel.Center);
        return Math.Clamp(0.5 - d, 0, 1);
    }

    public Vector2D Gradient(Point p)
    {
        const double eps = 0.5;
        double f = SignedDistance(p);
        double fx = SignedDistance(new(p.X + eps, p.Y));
        double fy = SignedDistance(new(p.X, p.Y + eps));
        double gx = (fx - f) / eps;
        double gy = (fy - f) / eps;
        double len = Math.Sqrt(gx * gx + gy * gy);
        if (len < 1e-9) len = 1e-9;
        return new Vector2D(gx / len, gy / len);
    }
}
