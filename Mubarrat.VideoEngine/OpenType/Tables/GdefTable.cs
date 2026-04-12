using Mubarrat.VideoEngine.OpenType.CommonTables;

namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class GdefTable : IOpenTypeTable
{
    public const string TableTag = "GDEF";

    public string Tag => TableTag;

    public GdefHeader Header { get; private set; } = default;
    public ClassDef? GlyphClassDef { get; private set; }
    public AttachListTable? AttachList { get; private set; }
    public LigCaretListTable? LigCaretList { get; private set; }
    public ClassDef? MarkAttachClassDef { get; private set; }
    public MarkGlyphSetsTable? MarkGlyphSets { get; private set; }
    public ItemVariationStore? ItemVariationStore { get; private set; }

    public readonly record struct GdefHeader(ushort MajorVersion, ushort MinorVersion);

    public enum GlyphClass : ushort
    {
        BaseGlyph = 1,
        LigatureGlyph = 2,
        MarkGlyph = 3,
        ComponentGlyph = 4
    }

    public enum CaretValueFormat : ushort
    {
        DesignUnits = 1,
        ContourPoint = 2,
        DesignUnitsPlusDeviceOrVariation = 3
    }

    public readonly record struct AttachPointTable(ushort[] PointIndices) : IOpenTypeCommonTable<AttachPointTable>
    {
        public static AttachPointTable Parse(OpenTypeReader.TableScope scope, object? param = null) => new(scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()));
    }

    public readonly record struct AttachListTable(Coverage Coverage, AttachPointTable[] AttachPoints) : IOpenTypeCommonTable<AttachListTable>
    {
        public static AttachListTable Parse(OpenTypeReader.TableScope scope, object? param = null) => new(scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()), AttachPointTable.ParseListFromOffsets16(scope));
    }

    public readonly record struct LigGlyphTable(CaretValueTable[] CaretValues) : IOpenTypeCommonTable<LigGlyphTable>
    {
        public static LigGlyphTable Parse(OpenTypeReader.TableScope scope, object? param = null) => new(CaretValueTable.ParseListFromOffsets16(scope));
    }

    public readonly record struct LigCaretListTable(Coverage Coverage, LigGlyphTable[] LigGlyphs) : IOpenTypeCommonTable<LigCaretListTable>
    {
        public static LigCaretListTable Parse(OpenTypeReader.TableScope scope, object? param = null) => new LigCaretListTable(scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()), LigGlyphTable.ParseListFromOffsets16(scope));
    }

    public abstract record CaretValueTable(CaretValueFormat Format) : IOpenTypeCommonTable<CaretValueTable>
    {
        public static CaretValueTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            CaretValueFormat format = (CaretValueFormat)scope.Reader.ReadUInt16();
            return format switch
            {
                CaretValueFormat.DesignUnits => CaretValueFormat1Table.ParseFormat1(scope),
                CaretValueFormat.ContourPoint => CaretValueFormat2Table.ParseFormat2(scope),
                CaretValueFormat.DesignUnitsPlusDeviceOrVariation => CaretValueFormat3Table.ParseFormat3(scope),
                _ => new UnknownCaretValueTable(format)
            };
        }
    }

    public sealed record CaretValueFormat1Table(CaretValueFormat Format, short Coordinate) : CaretValueTable(Format)
    {
        internal static CaretValueFormat1Table ParseFormat1(OpenTypeReader.TableScope scope) => new(CaretValueFormat.DesignUnits, scope.Reader.ReadInt16());
    }

    public sealed record CaretValueFormat2Table(CaretValueFormat Format, ushort CaretValuePointIndex) : CaretValueTable(Format)
    {
        internal static CaretValueFormat2Table ParseFormat2(OpenTypeReader.TableScope scope) => new(CaretValueFormat.ContourPoint, scope.Reader.ReadUInt16());
    }

    public sealed record CaretValueFormat3Table(CaretValueFormat Format, short Coordinate, DeviceOrVariationIndexTable? DeviceOrVariationTable) : CaretValueTable(Format)
    {
        internal static CaretValueFormat3Table ParseFormat3(OpenTypeReader.TableScope scope) => new(CaretValueFormat.DesignUnitsPlusDeviceOrVariation, scope.Reader.ReadInt16(), scope.ParseCommonTableOrDefault<DeviceOrVariationIndexTable>(scope.Reader.ReadOffset16()));
    }

    public sealed record UnknownCaretValueTable(CaretValueFormat Format) : CaretValueTable(Format);

    public readonly record struct MarkGlyphSetsTable(Coverage[] Coverages) : IOpenTypeCommonTable<MarkGlyphSetsTable>
    {
        public static MarkGlyphSetsTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort format = scope.Reader.ReadUInt16();
            return format == 1
                ? new MarkGlyphSetsTable(Coverage.ParseListFromOffsets32(scope))
                : throw new InvalidDataException($"Unsupported MarkGlyphSets format {format}.");
        }
    }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort major = scope.Reader.ReadUInt16(), minor = scope.Reader.ReadUInt16();
        if (major != 1 || (minor != 0 && minor != 2 && minor != 3))
            throw new InvalidDataException($"Unsupported GDEF version {major}.{minor}.");
        Header = new GdefHeader(major, minor);
        GlyphClassDef = scope.ParseCommonTableOrDefault<ClassDef>(scope.Reader.ReadOffset16());
        AttachList = scope.ParseCommonTableOrDefault<AttachListTable>(scope.Reader.ReadOffset16());
        LigCaretList = scope.ParseCommonTableOrDefault<LigCaretListTable>(scope.Reader.ReadOffset16());
        MarkAttachClassDef = scope.ParseCommonTableOrDefault<ClassDef>(scope.Reader.ReadOffset16());
        if (minor >= 2) MarkGlyphSets = scope.ParseCommonTableOrDefault<MarkGlyphSetsTable>(scope.Reader.ReadOffset16());
        if (minor >= 3) ItemVariationStore = scope.ParseCommonTableOrDefault<ItemVariationStore>(scope.Reader.ReadOffset32());
        tables.Add(this);
    }
}
