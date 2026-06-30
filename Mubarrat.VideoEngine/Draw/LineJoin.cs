namespace Mubarrat.VideoEngine.Draw;

/// <summary>
/// Specifies how the corners of lines are joined when drawing paths.
/// </summary>
public enum LineJoin
{
    /// <summary>
    /// The corners of lines are not joined, resulting in separate strokes.
    /// </summary>
    None,

    /// <summary>
    /// The corners of lines are joined with a mitered edge, resulting in a sharp corner.
    /// </summary>
    Miter,

    /// <summary>
    /// The corners of lines are joined with a beveled edge, resulting in a flattened corner.
    /// </summary>
    Bevel,

    /// <summary>
    /// The corners of lines are joined with a rounded edge, resulting in a smooth corner.
    /// </summary>
    Round
}
