using Mubarrat.OpenType;
using Mubarrat.OpenType.TextShaping;
using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Immutable;
using Mubarrat.VideoEngine.Path;

namespace Mubarrat.VideoEngine.Objects;

public sealed class TextBlock : FrameworkObject
{
    private Path2D cachedPath;
    private Rect cachedBounds;
    private bool hasCachedGeometry;

    public string Text { get => (string?)this[TextProperty] ?? string.Empty; set => this[TextProperty] = value ?? string.Empty; }
    public static readonly Property TextProperty = new(nameof(Text), typeof(string), string.Empty, AffectsMeasure: true, AffectsArrange: true);

    public FontFace? FontFace { get => (FontFace?)this[FontFaceProperty]; set => this[FontFaceProperty] = value; }
    public static readonly Property FontFaceProperty = new(nameof(FontFace), typeof(FontFace), AffectsMeasure: true, AffectsArrange: true);

    public double FontSize { get => (double)this[FontSizeProperty]; set => this[FontSizeProperty] = Math.Max(0, value); }
    public static readonly Property FontSizeProperty = new(nameof(FontSize), typeof(double), 16d, AffectsMeasure: true, AffectsArrange: true);

    public IBrush Foreground { get => (IBrush)this[ForegroundProperty]; set => this[ForegroundProperty] = value; }
    public static readonly Property ForegroundProperty = new(nameof(Foreground), typeof(IBrush), new SolidColorBrush(0, 0, 0), AffectsArrange: true);

    public bool IsNonZeroFill { get => (bool)this[IsNonZeroFillProperty]; set => this[IsNonZeroFillProperty] = value; }
    public static readonly Property IsNonZeroFillProperty = new(nameof(IsNonZeroFill), typeof(bool), true, AffectsMeasure: true, AffectsArrange: true);

    public override Size OnMeasure(Size availableSize)
    {
        EnsureGeometry();
        if (!hasCachedGeometry)
            return Size.Zero;

        return new Size(Math.Max(0, cachedBounds.Width), Math.Max(0, cachedBounds.Height));
    }

    public override Drawing ToDrawing()
    {
        EnsureGeometry();

        if (!hasCachedGeometry || Foreground is null)
        {
            return new GroupDrawing
            {
                Drawings = [],
                Transform = Matrix2D.Identity,
                Opacity = Opacity,
                Name = Name
            };
        }

        return new PathDrawing
        {
            Path = cachedPath,
            Fill = Foreground,
            Stroke = default,
            Transform = LayoutTransform * RenderTransform,
            Opacity = Opacity,
            Name = Name
        };
    }

    protected override void OnPropertyChanged(Property property, object? oldValue, object? newValue)
    {
        base.OnPropertyChanged(property, oldValue, newValue);

        if (property == TextProperty || property == FontFaceProperty || property == FontSizeProperty || property == IsNonZeroFillProperty)
            hasCachedGeometry = false;
    }

    private void EnsureGeometry()
    {
        lock (this)
        {
            if (hasCachedGeometry)
                return;

            hasCachedGeometry = false;
            cachedPath = default;
            cachedBounds = Rect.NaN;

            if (string.IsNullOrEmpty(Text) || FontFace is null || FontSize <= 0)
                return;

            var metrics = FontMetrics.Create(FontFace, FontSize);
            var shaping = OpenTypeTextShaper.Shape(Text, metrics);
            cachedPath = shaping.ToPath2D(metrics.FontSize, new Point(0, metrics.Ascent), IsNonZeroFill);
            cachedBounds = cachedPath.Bounds.Normalized;
            hasCachedGeometry = cachedPath.Subpaths.Length > 0;
        }
    }
}
