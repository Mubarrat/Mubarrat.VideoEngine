using Mubarrat.OpenType;
using Mubarrat.OpenType.Tables;
using Mubarrat.OpenType.TextShaping;
using Mubarrat.VideoEngine.Draw;

namespace Mubarrat.VideoEngine.Latex;

public abstract class MathAtom(MathAtomType type = MathAtomType.Ordinary)
{
    public MathAtomType Type = type;
    public ref Size Size => ref Bounds.Size;
    public ref Point Location => ref Bounds.Location;
    public FontMetrics? Metrics = null;
    public MathStyle Style = MathStyle.Text;
    public bool IsCramped = false;
    public double Baseline = 0;

    public ref double Width => ref Size.Width;
    public ref double Height => ref Size.Height;
    public ref double X => ref Location.X;
    public ref double Y => ref Location.Y;
    public Rect Bounds = Rect.Empty;

    protected virtual IEnumerable<MathAtom> ChildrenIterator => [];

    public bool IsExtendedShape => this is not SymbolMathAtom symbol
        || string.IsNullOrEmpty(symbol.Text)
        || (MathTable.MathGlyphInfo.ExtendedShapeCoverage is { } coverage
        && OpenTypeTextShaper.Shape(symbol.Text, Metrics.Value).Glyphs is { Count: not 0 } glyphs
        && coverage.TryGetIndex(glyphs[0].GlyphId, out _));

    public MathTable MathTable => (Metrics?.Face.Tables.TryGet(out MathTable mathTable) ?? false) ? mathTable : TexDefaults.MathTable;
    public HeadTable HeadTable => (Metrics?.Face.Tables.TryGet(out HeadTable headTable) ?? false) ? headTable : TexDefaults.HeadTable;
    public Os2Table? Os2Table => (Metrics?.Face.Tables.TryGet(out Os2Table os2Table) ?? false) ? os2Table : null;
    public VmtxTable? VmtxTable => (Metrics?.Face.Tables.TryGet(out VmtxTable vmtxTable) ?? false) ? vmtxTable : null;
    public HmtxTable? HmtxTable => (Metrics?.Face.Tables.TryGet(out HmtxTable hmtxTable) ?? false) ? hmtxTable : null;

    public virtual void OnProperty() { }

    public void PropertyDown()
    {
        OnProperty();
        foreach (var atom in ChildrenIterator)
            atom.PropertyDown();
    }

    public virtual void OnLayout() { }

    public void LayoutDown()
    {
        foreach (var atom in ChildrenIterator)
            atom.LayoutDown();
        OnLayout();
    }

    public virtual Drawing OnDraw() => new GroupDrawing
    {
        Drawings = [.. ChildrenIterator.Select(static child => child.OnDraw())],
        Transform = Matrix2D.Translate(Location)
    };

    protected static MathStyle DownScriptStyle(MathStyle style)
        => style switch
        {
            MathStyle.Display => MathStyle.Script,
            MathStyle.Text => MathStyle.Script,
            MathStyle.Script => MathStyle.ScriptScript,
            _ => MathStyle.ScriptScript
        };

    public FontMetrics ScaleMetrics(FontMetrics metrics, MathStyle style) => FontMetrics.Create(metrics.Face, Math.Max(1, metrics.FontSize * GetRelativeChildFactor(style)));

    public double GetFactor(MathStyle style) => style switch
    {
        MathStyle.Script => (double)MathTable.MathConstants.ScriptPercentScaleDown / 100,
        MathStyle.ScriptScript => (double)MathTable.MathConstants.ScriptScriptPercentScaleDown / 100,
        _ => 1
    };

    public double GetRelativeChildFactor(MathStyle style) => (style is MathStyle.Display or MathStyle.Text && Style is MathStyle.Display or MathStyle.Text)
        ? 1 : (GetFactor(style) / GetFactor(Style)); // reduces checks
}
