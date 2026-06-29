namespace Mubarrat.VideoEngine.Path;

public interface IPathSegment : ILerpable<IPathSegment>
{
    Point Start { get; }

    Point End { get; }

    Rect Bounds { get; }

    public static IPathSegment operator *(IPathSegment segment, Matrix2D matrix2D) => segment switch
    {
        MoveSegment m => new MoveSegment(m.Point * matrix2D),
        LineSegment l => new LineSegment(l.Start * matrix2D, l.End * matrix2D),
        QuadraticSegment q => new QuadraticSegment(q.Start * matrix2D, q.Control * matrix2D, q.End * matrix2D),
        CubicSegment c => new CubicSegment(c.Start * matrix2D, c.Control1 * matrix2D, c.Control2 * matrix2D, c.End * matrix2D),
        _ => throw new NotSupportedException($"Matrix multiplication is not supported for {segment.GetType().Name}"),
    };
    public static IPathSegment operator /(IPathSegment segment, Matrix2D matrix2D) => segment * matrix2D.Inverse;
}
