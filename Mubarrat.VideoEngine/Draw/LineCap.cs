namespace Mubarrat.VideoEngine.Draw;

/// <summary>
/// Specifies how the ends of lines are drawn when drawing paths.
/// </summary>
public enum LineCap
{
    /// <summary>
    /// The ends of lines are squared off at the endpoints.
    /// </summary>
    Flat,

    /// <summary>
    /// The ends of lines are squared off with a square shape that extends beyond the endpoint.
    /// </summary>
    Square,

    /// <summary>
    /// The ends of lines are rounded with a semicircular shape.
    /// </summary>
    Round,

    /// <summary>
    /// The ends of lines are extended with a triangular shape.
    /// </summary>
    Triangle
}
