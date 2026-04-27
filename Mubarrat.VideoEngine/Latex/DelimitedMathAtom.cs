using Mubarrat.VideoEngine.Draw;

namespace Mubarrat.VideoEngine.Latex;

public sealed class DelimitedMathAtom(MathAtom body, string leftDelimiter = "", string rightDelimiter = "") : MathAtom(MathAtomType.Inner)
{
    public string LeftDelimiter = leftDelimiter ?? string.Empty;
    public string RightDelimiter = rightDelimiter ?? string.Empty;
    public MathAtom Body = body ?? new SymbolMathAtom();

    protected override IEnumerable<MathAtom> ChildrenIterator => [Body];

    public override void OnProperty()
    {
        Body.Style = Style;
        Body.Metrics = Metrics;
        Body.IsCramped = IsCramped;
    }

    private double RequiredHeight;
    public override void OnLayout()
    {
        if (Metrics is not { } m)
            throw new InvalidOperationException("Metrics must be set before layout.");
        var c = MathTable.MathConstants;
        double axis = c.AxisHeight * m.Scale;
        double bodyTop = Body.Baseline;
        double bodyBottom = Body.Height - Body.Baseline;
        double aboveAxis = bodyTop - axis;
        double belowAxis = bodyBottom + axis;
        double halfRequired = Math.Max(aboveAxis, belowAxis);
        RequiredHeight = 2 * halfRequired;
        Helpers.GetVerticalGlyphInfo(LeftDelimiter[0], m, RequiredHeight, out var leftWidth, out var leftHeight);
        Helpers.GetVerticalGlyphInfo(RightDelimiter[0], m, RequiredHeight, out var rightWidth, out var rightHeight);
        Width = leftWidth + Body.Width + rightWidth;
        Height = Math.Max(RequiredHeight, Body.Height);
        Body.Location = new(
            leftWidth,
            halfRequired + axis - bodyTop
        );
        Baseline = Body.Y + Body.Baseline;
    }

    public override Drawing OnDraw()
    {
        if (Metrics is not { } m)
            throw new InvalidOperationException("Metrics must be set before draw.");
        var c = MathTable.MathConstants;
        double axis = c.AxisHeight * m.Scale;
        var drawing = (GroupDrawing)base.OnDraw();
        var left = Helpers.GetVerticalGlyph(LeftDelimiter[0], m, RequiredHeight);
        var right = Helpers.GetVerticalGlyph(RightDelimiter[0], m, RequiredHeight);
        Rect lBounds = left.Bounds, rBounds = right.Bounds;
        double centerY = Baseline - axis;
        drawing.Drawings.Add(new PathDrawing
        {
            Path = left,
            Transform = Matrix2D.Translate(
                0,
                centerY - (lBounds.Y + lBounds.Height * 0.5)
            )
        });
        drawing.Drawings.Add(new PathDrawing
        {
            Path = right,
            Transform = Matrix2D.Translate(
                Body.Bounds.Right,
                centerY - (rBounds.Y + rBounds.Height * 0.5)
            )
        });
        return drawing;
    }
}
