namespace Mubarrat.VideoEngine.Field;

/// <summary>
/// Marks a field that can be evaluated in 2D space. This interface defines the basic functionality for any 2D field, including evaluation at specific points and retrieving the field's bounds.
/// </summary>
public abstract class Field2D : ILerpable<Field2D>
{
    /// <summary>
    /// Evaluates the field at a given point in 2D space. The evaluation returns a double value that represents the field's value at that specific point.
    /// </summary>
    /// <param name="p">The point at which to evaluate the field.</param>
    /// <returns>The value of the field at the specified point.</returns>
    public abstract double Evaluate(Point p);

    /// <summary>
    /// Gets the bounds of the field in 2D space. This property defines the rectangular area that encompasses the field's influence or extent.
    /// </summary>
    public abstract Rect Bounds { get; }

    public Field2D Lerp(in Field2D other, double t) => new LerpField2D(this, other, t);
}

/// <summary>
/// Marks a field that can evaluate an interval over a given rectangle.
/// </summary>
public interface IIntervalField2D
{
    /// <summary>
    /// Evaluates the field over a given rectangle and returns the minimum and maximum values of the field within that rectangle.
    /// </summary>
    /// <param name="r">The rectangle over which to evaluate the field.</param>
    /// <returns>The minimum and maximum values of the field within the rectangle.</returns>
    FieldInterval EvaluateInterval(Rect r);
}

/// <summary>
/// Marks a field that can calculate the signed distance from a point to the field.
/// </summary>
public interface ISignedDistanceField2D
{
    /// <summary>
    /// Calculates the signed distance from a given point to the field. The signed distance is negative if the point is inside the field, positive if outside, and zero if on the boundary.
    /// </summary>
    /// <param name="p">The point for which to calculate the signed distance.</param>
    /// <returns>The signed distance.</returns>
    double SignedDistance(Point p);
}

/// <summary>
/// Marks a field that can calculate the coverage of the field over a given pixel (rectangle).
/// </summary>
public interface ICoverageField2D
{
    /// <summary>
    /// Calculates the coverage of the field over a given pixel (rectangle). The coverage is a value between 0 and 1, where 0 means no coverage and 1 means full coverage of the pixel by the field.
    /// </summary>
    /// <param name="pixel">The rectangle (pixel) for which to calculate the coverage.</param>
    /// <returns>The coverage.</returns>
    double GetCoverage(Rect pixel);
}

/// <summary>
/// Marks a field that can calculate the gradient of the field at a given point.
/// </summary>
public interface IGradientField2D
{
    /// <summary>
    /// Calculates the gradient of the field at a given point. The gradient is a vector that points in the direction of the greatest rate of increase of the field and whose magnitude is the rate of increase in that direction.
    /// </summary>
    /// <param name="p">The point at which to calculate the gradient.</param>
    /// <returns>The gradient vector.</returns>
    Vector2D Gradient(Point p);
}

/// <summary>
/// Marks a field that operates on a single child field. This interface defines the basic functionality for any unary field, including access to its child field.
/// </summary>
public interface IUnaryField2D
{
    /// <summary>
    /// Gets the child field that this unary field operates on.
    /// </summary>
    Field2D Child { get; }
}

/// <summary>
/// Marks a field that operates on two child fields. This interface defines the basic functionality for any binary field, including access to its left and right child fields.
/// </summary>
public interface IBinaryField2D
{
    /// <summary>
    /// Gets the left child field that this binary field operates on.
    /// </summary>
    Field2D Left { get; }

    /// <summary>
    /// Gets the right child field that this binary field operates on.
    /// </summary>
    Field2D Right { get; }
}
