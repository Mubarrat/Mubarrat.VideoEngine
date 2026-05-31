using Mubarrat.OpenType;
using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Latex;

namespace Mubarrat.VideoEngine.Objects;

public sealed class LatexBlock : FrameworkObject
{
    private Drawing? cachedDrawing;
    private Rect cachedBounds;

    private int renderVersion;
    private int cachedRenderVersion;

    public string Latex { get => (string?)this[LatexProperty] ?? string.Empty; set => this[LatexProperty] = value ?? string.Empty; }
    public static readonly Property LatexProperty = new(nameof(Latex), typeof(string), string.Empty, AffectsMeasure: true, AffectsArrange: true);

    public FontFace? FontFace { get => (FontFace?)this[FontFaceProperty]; set => this[FontFaceProperty] = value; }
    public static readonly Property FontFaceProperty = new(nameof(FontFace), typeof(FontFace), AffectsMeasure: true, AffectsArrange: true);

    public double FontSize { get => (double)this[FontSizeProperty]; set => this[FontSizeProperty] = Math.Max(0, value); }
    public static readonly Property FontSizeProperty = new(nameof(FontSize), typeof(double), 48d, AffectsMeasure: true, AffectsArrange: true);

    public IBrush Foreground { get => (IBrush)this[ForegroundProperty]; set => this[ForegroundProperty] = value ?? throw new ArgumentNullException(nameof(value)); }
    public static readonly Property ForegroundProperty = new(nameof(Foreground), typeof(IBrush), new SolidColorBrush(0, 0, 0), AffectsArrange: true);

    public override Size OnMeasure(Size availableSize)
    {
        EnsureDrawing();
        if (cachedRenderVersion != renderVersion)
            return Size.Zero;

        return new Size(Math.Max(0, cachedBounds.Width), Math.Max(0, cachedBounds.Height));
    }

    public override Drawing ToDrawing()
    {
        EnsureDrawing();

        if (cachedRenderVersion != renderVersion || cachedDrawing is null)
        {
            return new GroupDrawing
            {
                Drawings = [],
                Transform = Matrix2D.Identity,
                Opacity = Opacity,
                Name = Name
            };
        }

        Drawing drawing = (Drawing)cachedDrawing.Clone();
        drawing.Transform *= LayoutTransform * RenderTransform;
        drawing.Opacity *= Opacity;

        if (!string.IsNullOrWhiteSpace(Name))
            drawing.Name = Name;

        return drawing;
    }

    protected override void OnPropertyChanged(Property property, object? oldValue, object? newValue)
    {
        base.OnPropertyChanged(property, oldValue, newValue);

        if (property == LatexProperty || property == FontFaceProperty || property == FontSizeProperty || property == ForegroundProperty)
        {
            renderVersion++;
        }
    }

    private void EnsureDrawing()
    {
        if (cachedRenderVersion == renderVersion)
            return;

        lock (this)
        {
            if (cachedRenderVersion == renderVersion)
                return;

            cachedDrawing = null;
            cachedBounds = Rect.NaN;

            if (string.IsNullOrWhiteSpace(Latex) || FontFace is null || FontSize <= 0)
            {
                cachedRenderVersion = renderVersion;
                return;
            }

            MathAtom atom = LatexAtomizer.Atomize(Latex);
            atom.Metrics = FontMetrics.Create(FontFace, FontSize);
            atom.Style = MathStyle.Display;
            atom.PropertyDown();
            atom.LayoutDown();

            cachedDrawing = atom.OnDraw();
            cachedDrawing.Fill = Foreground;
            cachedBounds = atom.Size.Width > 0 || atom.Size.Height > 0
                ? new Rect(atom.Location, atom.Size)
                : (cachedDrawing?.Bounds ?? Rect.NaN);
            cachedRenderVersion = renderVersion;
        }
    }
}
