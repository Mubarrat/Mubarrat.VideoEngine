using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GposTable : IOpenTypeTable
{
    public const string TableTag = "GPOS";

    public string Tag => TableTag;

    public GposHeader Header { get; private set; } = default!;
    public ScriptRecord[] ScriptList { get; private set; } = [];
    public FeatureRecord[] FeatureList { get; private set; } = [];
    public Lookup<IGposSubtable>[] LookupList { get; private set; } = [];
    public FeatureVariations? FeatureVariations { get; private set; }

    public readonly record struct GposHeader(ushort MajorVersion, ushort MinorVersion);

    public readonly record struct ValueRecord(
        short XPlacement,
        short YPlacement,
        short XAdvance,
        short YAdvance,
        DeviceOrVariationIndexTable? XPlaDevice,
        DeviceOrVariationIndexTable? YPlaDevice,
        DeviceOrVariationIndexTable? XAdvDevice,
        DeviceOrVariationIndexTable? YAdvDevice)
    {
        internal static ValueRecord Read(OpenTypeReader.TableScope scope, ValueFormat format) => new(
            format.HasFlag(ValueFormat.XPlacement) ? scope.Reader.ReadInt16() : default,
            format.HasFlag(ValueFormat.YPlacement) ? scope.Reader.ReadInt16() : default,
            format.HasFlag(ValueFormat.XAdvance) ? scope.Reader.ReadInt16() : default,
            format.HasFlag(ValueFormat.YAdvance) ? scope.Reader.ReadInt16() : default,
            format.HasFlag(ValueFormat.XPlaDevice) ? scope.ParseCommonTableOrDefault<DeviceOrVariationIndexTable>(scope.Reader.ReadUInt16()) : null,
            format.HasFlag(ValueFormat.YPlaDevice) ? scope.ParseCommonTableOrDefault<DeviceOrVariationIndexTable>(scope.Reader.ReadUInt16()) : null,
            format.HasFlag(ValueFormat.XAdvDevice) ? scope.ParseCommonTableOrDefault<DeviceOrVariationIndexTable>(scope.Reader.ReadUInt16()) : null,
            format.HasFlag(ValueFormat.YAdvDevice) ? scope.ParseCommonTableOrDefault<DeviceOrVariationIndexTable>(scope.Reader.ReadUInt16()) : null);
    }

    public readonly record struct PairValueRecord(ushort SecondGlyph, ValueRecord Value1, ValueRecord Value2);
    public readonly record struct PairSet(PairValueRecord[] Pairs);
    public readonly record struct PairClass2Record(ValueRecord Value1, ValueRecord Value2);
    public readonly record struct PairClass1Record(PairClass2Record[] Class2Records);

    public readonly record struct EntryExitRecord(AnchorTable? EntryAnchor, AnchorTable? ExitAnchor);

    public readonly record struct MarkRecord(ushort MarkClass, AnchorTable MarkAnchor) : IOpenTypeCommonTable<MarkRecord>
    {
        public static MarkRecord Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            MarkClass: scope.Reader.ReadUInt16(),
            MarkAnchor: scope.ParseCommonTable<AnchorTable>(scope.Reader.ReadUInt16()));
    }

    public readonly record struct BaseRecord(AnchorTable?[] Anchors);
    public readonly record struct BaseArrayTable(BaseRecord[] BaseRecords) : IOpenTypeCommonTable<BaseArrayTable>
    {
        public static BaseArrayTable Parse(OpenTypeReader.TableScope scope, object? param)
        {
            if (param is not ushort classCount)
                return default!;
            ushort baseCount = scope.Reader.ReadUInt16();
            var bases = new BaseRecord[baseCount];
            for (int i = 0; i < baseCount; i++)
            {
                var anchors = new AnchorTable?[classCount];
                for (int c = 0; c < classCount; c++)
                    anchors[c] = scope.ParseCommonTableOrDefault<AnchorTable>(scope.Reader.ReadUInt16());
                bases[i] = new BaseRecord(anchors);
            }
            return new BaseArrayTable(bases);
        }
    }

    public readonly record struct LigatureComponentRecord(AnchorTable?[] Anchors);
    public readonly record struct LigatureAttachRecord(LigatureComponentRecord[] Components);
    public readonly record struct LigatureArrayTable(LigatureAttachRecord[] Ligatures) : IOpenTypeCommonTable<LigatureArrayTable>
    {
        public static LigatureArrayTable Parse(OpenTypeReader.TableScope scope, object? param)
        {
            if (param is not ushort classCount)
                return default!;
            var ligatures = new LigatureAttachRecord[scope.Reader.ReadUInt16()];
            for (int i = 0; i < ligatures.Length; i++)
            {
                using (var ligatureScope = scope.EnterScope(scope.Reader.ReadUInt16()))
                {
                    ushort componentCount = scope.Reader.ReadUInt16();
                    var components = new LigatureComponentRecord[componentCount];
                    for (int comp = 0; comp < componentCount; comp++)
                    {
                        var anchors = new AnchorTable?[classCount];
                        for (int c = 0; c < classCount; c++)
                            anchors[c] = scope.ParseCommonTableOrDefault<AnchorTable>(ligatureScope.Reader.ReadUInt16());
                        components[comp] = new LigatureComponentRecord(anchors);
                    }
                    ligatures[i] = new LigatureAttachRecord(components);
                }
            }
            return new LigatureArrayTable(ligatures);
        }
    }

    public readonly record struct Mark2Record(AnchorTable?[] Anchors) : IOpenTypeCommonTable<Mark2Record>
    {
        public static Mark2Record Parse(OpenTypeReader.TableScope scope, object? param = null) => new(AnchorTable.ParseListFromOffsets16(scope, param));
    }

    public readonly record struct Mark2ArrayTable(Mark2Record[] Mark2Records) : IOpenTypeCommonTable<Mark2ArrayTable>
    {
        public static Mark2ArrayTable Parse(OpenTypeReader.TableScope scope, object? param)
        {
            var marks = new Mark2Record[scope.Reader.ReadUInt16()];
            for (int i = 0; i < marks.Length; i++)
                marks[i] = Mark2Record.Parse(scope, param);
            return new Mark2ArrayTable(marks);
        }
    }

    public abstract record AnchorTable(ushort Format) : IOpenTypeCommonTable<AnchorTable>
    {
        public static AnchorTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort format = scope.Reader.ReadUInt16();
            if (format == 1)
                return new AnchorFormat1(format, scope.Reader.ReadInt16(), scope.Reader.ReadInt16());
            if (format == 2)
                return new AnchorFormat2(format, scope.Reader.ReadInt16(), scope.Reader.ReadInt16(), scope.Reader.ReadUInt16());
            if (format == 3)
                return new AnchorFormat3(format, scope.Reader.ReadInt16(), scope.Reader.ReadInt16(), scope.ParseCommonTableOrDefault<DeviceOrVariationIndexTable>(scope.Reader.ReadUInt16()), scope.ParseCommonTableOrDefault<DeviceOrVariationIndexTable>(scope.Reader.ReadUInt16()));
            return new UnknownAnchorTable(format);
        }
    }

    public sealed record AnchorFormat1(ushort Format, short XCoordinate, short YCoordinate) : AnchorTable(Format);
    public sealed record AnchorFormat2(ushort Format, short XCoordinate, short YCoordinate, ushort AnchorPoint) : AnchorTable(Format);
    public sealed record AnchorFormat3(ushort Format, short XCoordinate, short YCoordinate, DeviceOrVariationIndexTable? XDevice, DeviceOrVariationIndexTable? YDevice) : AnchorTable(Format);
    public sealed record UnknownAnchorTable(ushort Format) : AnchorTable(Format);

    public enum LookupType : ushort
    {
        SingleAdjustment = 1,
        PairAdjustment = 2,
        CursiveAttachment = 3,
        MarkToBaseAttachment = 4,
        MarkToLigatureAttachment = 5,
        MarkToMarkAttachment = 6,
        ContextualPositioning = 7,
        ChainedContextualPositioning = 8,
        PositioningExtension = 9
    }

    [Flags]
    public enum ValueFormat : ushort
    {
        XPlacement = 0x0001,
        YPlacement = 0x0002,
        XAdvance = 0x0004,
        YAdvance = 0x0008,
        XPlaDevice = 0x0010,
        YPlaDevice = 0x0020,
        XAdvDevice = 0x0040,
        YAdvDevice = 0x0080
    }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort major = scope.Reader.ReadUInt16(), minor = scope.Reader.ReadUInt16();
        Header = new GposHeader(major, minor);
        ScriptList = scope.ParseCommonListTableContiguous<ScriptRecord>(scope.Reader.ReadUInt16());
        FeatureList = scope.ParseCommonListTableContiguous<FeatureRecord>(scope.Reader.ReadUInt16());
        LookupList = scope.ParseCommonListTableFromOffsets16<Lookup<IGposSubtable>>(scope.Reader.ReadUInt16());
        if (major >= 1 && minor >= 1)
            FeatureVariations = scope.ParseCommonTable<FeatureVariations>(scope.Reader.ReadUInt32());
        tables.Add(this);
    }
}
