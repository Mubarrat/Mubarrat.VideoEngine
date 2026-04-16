using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GsubTable : IOpenTypeTable
{
    public const string TableTag = "GSUB";

    public string Tag => TableTag;

    public GsubHeader Header { get; private set; }
    public ScriptRecord[] ScriptList { get; private set; } = [];
    public FeatureRecord[] FeatureList { get; private set; } = [];
    public Lookup<IGsubSubtable>[] LookupList { get; private set; } = [];
    public FeatureVariations? FeatureVariations { get; private set; }

    public readonly record struct GsubHeader(ushort MajorVersion, ushort MinorVersion);

    public readonly record struct MultipleSequence(ushort[] SubstituteGlyphIds) : IOpenTypeCommonTable<MultipleSequence>
    {
        public static MultipleSequence Parse(OpenTypeReader.TableScope scope, object? param = null) => new(scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()));
    }

    public readonly record struct AlternateSet(ushort[] AlternateGlyphIds) : IOpenTypeCommonTable<AlternateSet>
    {
        public static AlternateSet Parse(OpenTypeReader.TableScope scope, object? param = null) => new(scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()));
    }

    public readonly record struct LigatureSet(Ligature[] Ligatures) : IOpenTypeCommonTable<LigatureSet>
    {
        public static LigatureSet Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            Ligatures: Ligature.ParseListFromOffsets16(scope, param));
    }

    public readonly record struct Ligature(ushort LigatureGlyph, ushort[] ComponentGlyphIds) : IOpenTypeCommonTable<Ligature>
    {
        public static Ligature Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            LigatureGlyph: scope.Reader.ReadUInt16(),
            ComponentGlyphIds: scope.Reader.ReadUInt16Array(Math.Max(scope.Reader.ReadUInt16() - 1, 0)));
    }

    public enum LookupType : ushort
    {
        SingleSubstitution = 1,
        MultipleSubstitution = 2,
        AlternateSubstitution = 3,
        LigatureSubstitution = 4,
        SequenceContext = 5,
        ChainedSequenceContext = 6,
        ExtensionSubstitution = 7,
        ReverseChainSingleSubstitution = 8
    }

    internal static double ReadF2Dot14(short value) => value / 16384.0;

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort major = scope.Reader.ReadUInt16(), minor = scope.Reader.ReadUInt16();
        Header = new GsubHeader(major, minor);
        ScriptList = scope.ParseCommonListTableContiguous<ScriptRecord>(scope.Reader.ReadUInt16());
        FeatureList = scope.ParseCommonListTableContiguous<FeatureRecord>(scope.Reader.ReadUInt16());
        LookupList = scope.ParseCommonListTableFromOffsets16<Lookup<IGsubSubtable>>(scope.Reader.ReadUInt16());
        if (major >= 1 && minor >= 1)
            FeatureVariations = scope.ParseCommonTable<FeatureVariations>(scope.Reader.ReadUInt32());
        tables.Add(this);
    }
}
