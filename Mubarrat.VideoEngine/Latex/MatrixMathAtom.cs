namespace Mubarrat.VideoEngine.Latex;

public sealed class MatrixMathAtom(IReadOnlyList<IReadOnlyList<MathAtom>> rows, string leftDelimiter = "", string rightDelimiter = "") : MathAtom(MathAtomType.Inner)
{
    public IReadOnlyList<IReadOnlyList<MathAtom>> Rows = rows ?? [];
    public string LeftDelimiter = leftDelimiter ?? string.Empty;
    public string RightDelimiter = rightDelimiter ?? string.Empty;

    protected override IEnumerable<MathAtom> ChildrenIterator => Rows.SelectMany(row => row);

    public override void OnProperty()
    {
        var metrics = Metrics ?? throw new InvalidOperationException("Metrics must be set for RadicalMathAtom");
    }

}
