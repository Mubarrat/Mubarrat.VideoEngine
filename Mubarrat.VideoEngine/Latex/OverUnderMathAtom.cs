namespace Mubarrat.VideoEngine.Latex;

public sealed class OverUnderMathAtom(MathAtom @base, MathAtom? Under, MathAtom? Over) : MathAtom(MathAtomType.Inner)
{
    public MathAtom Base = @base ?? new SymbolMathAtom();
    public MathAtom? Under = Under;
    public MathAtom? Over = Over;

    protected override IEnumerable<MathAtom> ChildrenIterator
    {
        get
        {
            yield return Base;
            if (Under is not null) yield return Under;
            if (Over is not null) yield return Over;
        }
    }

}
