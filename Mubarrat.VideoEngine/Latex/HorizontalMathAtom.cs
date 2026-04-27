namespace Mubarrat.VideoEngine.Latex;

public sealed class HorizontalMathAtom(IReadOnlyList<MathAtom> children) : MathAtom(MathAtomType.Inner)
{
    public IReadOnlyList<MathAtom> Children = children ?? [];

    protected override IEnumerable<MathAtom> ChildrenIterator => Children;

    public override void OnProperty()
    {
        foreach (var child in Children)
        {
            child.Metrics = Metrics;
            child.Style = Style;
            child.IsCramped = IsCramped;
        }
    }

    public override void OnLayout()
    {
        double x = 0, maxAscent = 0, maxDescent = 0;
        foreach (var child in Children)
        {
            maxAscent = Math.Max(maxAscent, child.Baseline);
            maxDescent = Math.Max(maxDescent, child.Size.Height - child.Baseline);
        }
        if (Style is MathStyle.Display or MathStyle.Text)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                MathAtom? child = Children[i];
                child.Location = new(x + (i == 0
                    ? 0
                    : MathSpacingEngine.GetAbsoluteSpacing(
                        MathSpacingEngine.GetSpacing(Children[i - 1].Type, child.Type, i == 1),
                            Metrics ?? throw new InvalidOperationException("Metrics must be set for HorizontalMathAtom"))), maxAscent - child.Baseline);
                x = child.Bounds.Right;
            }
        }
        else
        {
            foreach (var child in Children)
            {
                child.Location = new(x, maxAscent - child.Baseline);
                x += child.Size.Width;
            }
        }
        Size = new(x, maxAscent + maxDescent);
        Baseline = maxAscent;
    }
}
