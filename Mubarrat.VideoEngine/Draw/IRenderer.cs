namespace Mubarrat.VideoEngine.Draw;

/// <summary>
/// Renderer abstraction. Implementations may use CPU or GPU backends.
/// </summary>
public interface IRenderer : System.IDisposable
{
    void Draw(Drawing drawing);
}
