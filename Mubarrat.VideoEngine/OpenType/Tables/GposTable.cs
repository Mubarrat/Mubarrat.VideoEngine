using Mubarrat.VideoEngine.OpenType.CommonTables;

namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class GposTable : IOpenTypeTable
{
    public const string TableTag = "GPOS";

    public string Tag => TableTag;

    public GposHeader Header { get; private set; } = default!;
    public ScriptRecord[] ScriptList { get; private set; } = [];
    public FeatureRecord[] FeatureList { get; private set; } = [];
    public Lookup<IGposSubtable>[] LookupList { get; private set; } = [];
    public FeatureVariations? FeatureVariations { get; private set; }

    public readonly record struct GposHeader(ushort MajorVersion, ushort MinorVersion);

    public interface IGposSubtable : IOpenTypeCommonTable<IGposSubtable>
    {
        static IGposSubtable IOpenTypeCommonTable<IGposSubtable>.Parse(OpenTypeReader.TableScope scope, object? param) => Parse(scope, param);

        public static new IGposSubtable Parse(OpenTypeReader.TableScope scope, object? param)
        {
            if (param is not ushort lookupType)
                return null!;

            ushort format = scope.Reader.ReadUInt16();

            return ((LookupType)lookupType, format) switch
            {
                (LookupType.SingleAdjustment, 1) => SingleAdjustmentSubtable.ParseFormat1(scope),
                (LookupType.SingleAdjustment, 2) => SingleAdjustmentSubtable.ParseFormat2(scope),

                (LookupType.PairAdjustment, 1) => PairAdjustmentSubtable.ParseFormat1(scope),
                (LookupType.PairAdjustment, 2) => PairAdjustmentSubtable.ParseFormat2(scope),

                (LookupType.CursiveAttachment, 1) => CursiveAttachmentSubtable.Parse(scope),

                (LookupType.MarkToBaseAttachment, 1) => MarkToBaseAttachmentSubtable.Parse(scope),
                (LookupType.MarkToLigatureAttachment, 1) => MarkToLigatureAttachmentSubtable.Parse(scope),
                (LookupType.MarkToMarkAttachment, 1) => MarkToMarkAttachmentSubtable.Parse(scope),

                (LookupType.ContextualPositioning, _) => SequenceContext.Parse(scope, format),
                (LookupType.ChainedContextualPositioning, _) => ChainedSequenceContext.Parse(scope, format),

                (LookupType.PositioningExtension, 1) => ExtensionSubstitutionSubtable.Parse(scope),

                _ => throw new InvalidDataException($"Unsupported GPOS lookup type {lookupType} format {format}.")
            };
        }
    }

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

    public sealed record SingleAdjustmentSubtable(
        ushort Format,
        Coverage Coverage,
        ValueFormat ValueFormat,
        ValueRecord[] ValueRecords) : IGposSubtable
    {
        internal static IGposSubtable ParseFormat1(OpenTypeReader.TableScope scope)
        {
            ushort coverageOffset = scope.Reader.ReadUInt16();
            ValueFormat valueFormat = (ValueFormat)scope.Reader.ReadUInt16();
            return new SingleAdjustmentSubtable(1, scope.ParseCommonTable<Coverage>(coverageOffset), valueFormat, [ValueRecord.Read(scope, valueFormat)]);
        }

        internal static IGposSubtable ParseFormat2(OpenTypeReader.TableScope scope)
        {
            ushort coverageOffset = scope.Reader.ReadUInt16();
            ValueFormat valueFormat = (ValueFormat)scope.Reader.ReadUInt16();
            var values = new ValueRecord[scope.Reader.ReadUInt16()];
            for (int i = 0; i < values.Length; i++)
                values[i] = ValueRecord.Read(scope, valueFormat);
            return new SingleAdjustmentSubtable(2, scope.ParseCommonTable<Coverage>(coverageOffset), valueFormat, values);
        }
    }

    public sealed record PairAdjustmentSubtable(
        ushort Format,
        Coverage Coverage,
        ValueFormat ValueFormat1,
        ValueFormat ValueFormat2,
        PairSet[]? PairSets,
        ClassDef? ClassDef1,
        ClassDef? ClassDef2,
        ushort Class1Count,
        ushort Class2Count,
        PairClass1Record[]? Class1Records) : IGposSubtable
    {
        internal static IGposSubtable ParseFormat1(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ValueFormat valueFormat1 = (ValueFormat)scope.Reader.ReadUInt16(), valueFormat2 = (ValueFormat)scope.Reader.ReadUInt16();
            var pairSets = new PairSet[scope.Reader.ReadUInt16()];
            for (int i = 0; i < pairSets.Length; i++)
            {
                using (var pairSetScope = scope.EnterScope(scope.Reader.ReadOffset16()))
                {
                    var pairs = new PairValueRecord[pairSetScope.Reader.ReadUInt16()];
                    for (int p = 0; p < pairs.Length; p++)
                        pairs[p] = new PairValueRecord(pairSetScope.Reader.ReadUInt16(), ValueRecord.Read(pairSetScope, valueFormat1), ValueRecord.Read(pairSetScope, valueFormat2));
                    pairSets[i] = new PairSet(pairs);
                }
            }
            return new PairAdjustmentSubtable(1, coverage, valueFormat1, valueFormat2, pairSets, null, null, 0, 0, null);
        }

        internal static IGposSubtable ParseFormat2(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ValueFormat valueFormat1 = (ValueFormat)scope.Reader.ReadUInt16(), valueFormat2 = (ValueFormat)scope.Reader.ReadUInt16();
            ClassDef classDef1 = scope.ParseCommonTable<ClassDef>(scope.Reader.ReadUInt16()), classDef2 = scope.ParseCommonTable<ClassDef>(scope.Reader.ReadUInt16());
            ushort class1Count = scope.Reader.ReadUInt16(), class2Count = scope.Reader.ReadUInt16();
            var class1Records = new PairClass1Record[class1Count];
            for (int c1 = 0; c1 < class1Count; c1++)
            {
                var class2Records = new PairClass2Record[class2Count];
                for (int c2 = 0; c2 < class2Count; c2++)
                    class2Records[c2] = new PairClass2Record(ValueRecord.Read(scope, valueFormat1), ValueRecord.Read(scope, valueFormat2));
                class1Records[c1] = new PairClass1Record(class2Records);
            }
            return new PairAdjustmentSubtable(2, coverage, valueFormat1, valueFormat2, null, classDef1, classDef2, class1Count, class2Count, class1Records);
        }
    }

    public readonly record struct EntryExitRecord(AnchorTable? EntryAnchor, AnchorTable? ExitAnchor);

    public sealed record CursiveAttachmentSubtable(
        ushort Format,
        Coverage Coverage,
        EntryExitRecord[] EntryExitRecords) : IGposSubtable
    {
        internal static IGposSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            var records = new EntryExitRecord[scope.Reader.ReadUInt16()];
            for (int i = 0; i < records.Length; i++)
                records[i] = new EntryExitRecord(scope.ParseCommonTableOrDefault<AnchorTable>(scope.Reader.ReadUInt16()), scope.ParseCommonTableOrDefault<AnchorTable>(scope.Reader.ReadUInt16()));
            return new CursiveAttachmentSubtable(1, coverage, records);
        }
    }

    public readonly record struct MarkRecord(ushort MarkClass, AnchorTable MarkAnchor) : IOpenTypeCommonTable<MarkRecord>
    {
        public static MarkRecord Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            MarkClass: scope.Reader.ReadUInt16(),
            MarkAnchor: scope.ParseCommonTable<AnchorTable>(scope.Reader.ReadUInt16()));
    }

    public readonly record struct BaseRecord(AnchorTable?[] Anchors);
    public readonly record struct BaseArrayTable(BaseRecord[] BaseRecords);
    private static BaseArrayTable ParseBaseArray(OpenTypeReader.TableScope scope, ushort classCount)
    {
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

    public readonly record struct LigatureComponentRecord(AnchorTable?[] Anchors);
    public readonly record struct LigatureAttachRecord(LigatureComponentRecord[] Components);
    public readonly record struct LigatureArrayTable(LigatureAttachRecord[] Ligatures);
    private static LigatureArrayTable ParseLigatureArray(OpenTypeReader.TableScope scope, ushort classCount)
    {
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

    public readonly record struct Mark2Record(AnchorTable?[] Anchors);
    public readonly record struct Mark2ArrayTable(Mark2Record[] Mark2Records);
    private static Mark2ArrayTable ParseMark2Array(OpenTypeReader.TableScope scope, ushort classCount)
    {
        var marks = new Mark2Record[scope.Reader.ReadUInt16()];
        for (int i = 0; i < marks.Length; i++)
        {
            var anchors = new AnchorTable?[classCount];
            for (int c = 0; c < classCount; c++)
                anchors[c] = scope.ParseCommonTableOrDefault<AnchorTable>(scope.Reader.ReadUInt16());
            marks[i] = new Mark2Record(anchors);
        }
        return new Mark2ArrayTable(marks);
    }

    public sealed record MarkToBaseAttachmentSubtable(
        ushort Format,
        Coverage MarkCoverage,
        Coverage BaseCoverage,
        ushort MarkClassCount,
        MarkRecord[] MarkArray,
        BaseArrayTable BaseArray) : IGposSubtable
    {
        internal static IGposSubtable Parse(OpenTypeReader.TableScope scope)
        {
            Coverage markCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), baseCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ushort markClassCount = scope.Reader.ReadUInt16();
            var markArray = scope.ParseCommonListTableContiguous<MarkRecord>(scope.Reader.ReadUInt16());
            using (var baseArrayScope = scope.EnterScope(scope.Reader.ReadUInt16()))
                return new MarkToBaseAttachmentSubtable(1, markCoverage, baseCoverage, markClassCount, markArray, ParseBaseArray(baseArrayScope, markClassCount));
        }
    }

    public sealed record MarkToLigatureAttachmentSubtable(
        ushort Format,
        Coverage MarkCoverage,
        Coverage LigatureCoverage,
        ushort MarkClassCount,
        MarkRecord[] MarkArray,
        LigatureArrayTable LigatureArray) : IGposSubtable
    {
        internal static IGposSubtable Parse(OpenTypeReader.TableScope scope)
        {
            Coverage markCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), ligatureCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ushort markClassCount = scope.Reader.ReadUInt16();
            var markArray = scope.ParseCommonListTableContiguous<MarkRecord>(scope.Reader.ReadUInt16());
            using (var ligatureArrayScope = scope.EnterScope(scope.Reader.ReadUInt16()))
                return new MarkToLigatureAttachmentSubtable(1, markCoverage, ligatureCoverage, markClassCount, markArray, ParseLigatureArray(ligatureArrayScope, markClassCount));
        }
    }

    public sealed record MarkToMarkAttachmentSubtable(
        ushort Format,
        Coverage Mark1Coverage,
        Coverage Mark2Coverage,
        ushort MarkClassCount,
        MarkRecord[] Mark1Array,
        Mark2ArrayTable Mark2Array) : IGposSubtable
    {
        internal static IGposSubtable Parse(OpenTypeReader.TableScope scope)
        {
            Coverage mark1Coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), mark2Coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ushort markClassCount = scope.Reader.ReadUInt16();
            var mark1Array = scope.ParseCommonListTableContiguous<MarkRecord>(scope.Reader.ReadUInt16());
            using (var mark2ArrayScope = scope.EnterScope(scope.Reader.ReadUInt16()))
                return new MarkToMarkAttachmentSubtable(1, mark1Coverage, mark2Coverage, markClassCount, mark1Array, ParseMark2Array(mark2ArrayScope, markClassCount));
        }
    }

    public sealed record ExtensionSubstitutionSubtable(
        ushort Format,
        ushort ExtensionLookupType,
        IGposSubtable ExtensionSubtable) : IGposSubtable
    {
        internal static IGposSubtable Parse(OpenTypeReader.TableScope scope)
        {
            ushort extensionLookupType = scope.Reader.ReadUInt16();
            return new ExtensionSubstitutionSubtable(1, extensionLookupType, scope.ParseCommonTable<IGposSubtable>(scope.Reader.ReadUInt32(), extensionLookupType));
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
