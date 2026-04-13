using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class BaseTable : IOpenTypeTable
{
    public const string TableTag = "BASE";

    public string Tag => TableTag;

    public BaseHeader Header { get; private set; } = default;
    public AxisTable? HorizAxis { get; private set; }
    public AxisTable? VertAxis { get; private set; }
    public ItemVariationStore? ItemVariationStore { get; private set; }

    public readonly record struct BaseHeader(ushort MajorVersion, ushort MinorVersion);

    public readonly record struct BaseTagListTable(string[] BaselineTags) : IOpenTypeCommonTable<BaseTagListTable>
    {
        public static BaseTagListTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            var tags = new string[scope.Reader.ReadUInt16()];
            for (int i = 0; i < tags.Length; i++)
                tags[i] = scope.Reader.ReadTag();
            return new BaseTagListTable(tags);
        }
    }

    public readonly record struct AxisTable(
        BaseTagListTable? BaseTagList,
        BaseScriptListTable BaseScriptList) : IOpenTypeCommonTable<AxisTable>
    {
        public static AxisTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            BaseTagListTable? baseTagList = scope.ParseCommonTableOrDefault<BaseTagListTable>(scope.Reader.ReadOffset16());
            return new AxisTable(baseTagList, scope.ParseCommonTable<BaseScriptListTable>(scope.Reader.ReadOffset16(), baseTagList?.BaselineTags.Length ?? 0));
        }
    }

    public readonly record struct BaseScriptListTable(BaseScriptRecord[] BaseScriptRecords) : IOpenTypeCommonTable<BaseScriptListTable>
    {
        public static BaseScriptListTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort baseTagCount = param is ushort count ? count : (ushort)0;
            ushort baseScriptCount = scope.Reader.ReadUInt16();

            var records = scope.ParseCommonListTableContiguous<BaseScriptRecord>(baseScriptCount, baseTagCount);
            return new BaseScriptListTable(records);
        }
    }

    public readonly record struct BaseScriptRecord(string BaseScriptTag, BaseScriptTable BaseScript) : IOpenTypeCommonTable<BaseScriptRecord>
    {
        public static BaseScriptRecord Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort baseTagCount = param is ushort count ? count : (ushort)0;

            string tag = scope.Reader.ReadTag();
            ushort offset = scope.Reader.ReadOffset16();

            return new BaseScriptRecord(tag, scope.ParseCommonTable<BaseScriptTable>(offset, baseTagCount));
        }
    }

    public readonly record struct BaseScriptTable(
        BaseValuesTable? BaseValues,
        MinMaxTable? DefaultMinMax,
        BaseLangSysRecord[] BaseLangSysRecords) : IOpenTypeCommonTable<BaseScriptTable>
    {
        public static BaseScriptTable Parse(OpenTypeReader.TableScope scope, object? param = null) => new(scope.ParseCommonTableOrDefault<BaseValuesTable>(scope.Reader.ReadOffset16(), param is ushort count ? count : (ushort)0), scope.ParseCommonTableOrDefault<MinMaxTable>(scope.Reader.ReadOffset16()), scope.ParseCommonListTableContiguous<BaseLangSysRecord>(scope.Reader.ReadUInt16()));
    }

    public readonly record struct BaseLangSysRecord(string BaseLangSysTag, MinMaxTable MinMax) : IOpenTypeCommonTable<BaseLangSysRecord>
    {
        public static BaseLangSysRecord Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            string tag = scope.Reader.ReadTag();
            ushort offset = scope.Reader.ReadOffset16();

            return new BaseLangSysRecord(tag, scope.ParseCommonTable<MinMaxTable>(offset));
        }
    }

    public readonly record struct BaseValuesTable(ushort DefaultBaselineIndex, BaseCoordTable[] BaseCoords) : IOpenTypeCommonTable<BaseValuesTable>
    {
        public static BaseValuesTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort baseTagCount = param is ushort count ? count : (ushort)0;

            ushort defaultBaselineIndex = scope.Reader.ReadUInt16();
            ushort baseCoordCount = scope.Reader.ReadUInt16();

            if (baseTagCount != 0 && baseCoordCount != baseTagCount)
                throw new InvalidDataException($"BaseValues baseCoordCount ({baseCoordCount}) does not match BaseTagList count ({baseTagCount}).");

            var baseCoords = scope.ParseCommonListTableFromOffsets16<BaseCoordTable>(baseCoordCount);
            return new BaseValuesTable(defaultBaselineIndex, baseCoords);
        }
    }

    public readonly record struct MinMaxTable(
        BaseCoordTable? MinCoord,
        BaseCoordTable? MaxCoord,
        FeatMinMaxRecord[] FeatMinMaxRecords) : IOpenTypeCommonTable<MinMaxTable>
    {
        public static MinMaxTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            var minCoord = scope.ParseCommonTableOrDefault<BaseCoordTable>(scope.Reader.ReadOffset16());
            var maxCoord = scope.ParseCommonTableOrDefault<BaseCoordTable>(scope.Reader.ReadOffset16());

            ushort featMinMaxCount = scope.Reader.ReadUInt16();
            var featMinMaxRecords = scope.ParseCommonListTableContiguous<FeatMinMaxRecord>(featMinMaxCount);

            return new MinMaxTable(minCoord, maxCoord, featMinMaxRecords);
        }
    }

    public readonly record struct FeatMinMaxRecord(
        string FeatureTag,
        BaseCoordTable? MinCoord,
        BaseCoordTable? MaxCoord) : IOpenTypeCommonTable<FeatMinMaxRecord>
    {
        public static FeatMinMaxRecord Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            string featureTag = scope.Reader.ReadTag();
            var minCoord = scope.ParseCommonTableOrDefault<BaseCoordTable>(scope.Reader.ReadOffset16());
            var maxCoord = scope.ParseCommonTableOrDefault<BaseCoordTable>(scope.Reader.ReadOffset16());

            return new FeatMinMaxRecord(featureTag, minCoord, maxCoord);
        }
    }

    public abstract record BaseCoordTable(ushort Format) : IOpenTypeCommonTable<BaseCoordTable>
    {
        public static BaseCoordTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort format = scope.Reader.ReadUInt16();

            return format switch
            {
                1 => BaseCoordFormat1Table.ParseFormat1(scope),
                2 => BaseCoordFormat2Table.ParseFormat2(scope),
                3 => BaseCoordFormat3Table.ParseFormat3(scope),
                _ => throw new InvalidDataException($"Unsupported BaseCoord format {format}.")
            };
        }
    }

    public sealed record BaseCoordFormat1Table(ushort Format, short Coordinate) : BaseCoordTable(Format)
    {
        internal static BaseCoordTable ParseFormat1(OpenTypeReader.TableScope scope)
            => new BaseCoordFormat1Table(1, scope.Reader.ReadInt16());
    }

    public sealed record BaseCoordFormat2Table(
        ushort Format,
        short Coordinate,
        ushort ReferenceGlyph,
        ushort BaseCoordPoint) : BaseCoordTable(Format)
    {
        internal static BaseCoordTable ParseFormat2(OpenTypeReader.TableScope scope)
        {
            short coordinate = scope.Reader.ReadInt16();
            ushort referenceGlyph = scope.Reader.ReadUInt16();
            ushort baseCoordPoint = scope.Reader.ReadUInt16();

            return new BaseCoordFormat2Table(2, coordinate, referenceGlyph, baseCoordPoint);
        }
    }

    public sealed record BaseCoordFormat3Table(
        ushort Format,
        short Coordinate,
        DeviceOrVariationIndexTable? DeviceOrVariationTable) : BaseCoordTable(Format)
    {
        internal static BaseCoordTable ParseFormat3(OpenTypeReader.TableScope scope)
        {
            short coordinate = scope.Reader.ReadInt16();
            ushort deviceOffset = scope.Reader.ReadOffset16();

            return new BaseCoordFormat3Table(
                3,
                coordinate,
                scope.ParseCommonTableOrDefault<DeviceOrVariationIndexTable>(deviceOffset));
        }

        public float Resolve(BaseTable owner, VariationInstance instance)
        {
            float delta = owner.ItemVariationStore is not null
                && DeviceOrVariationTable is VariationIndexTable variationIndex
                    ? owner.ItemVariationStore?.Resolve(variationIndex, instance) ?? 0f
                    : 0f;

            return Coordinate + delta;
        }
    }

    public float ResolveBaseCoord(BaseCoordTable? baseCoord, VariationInstance instance)
        => baseCoord switch
        {
            null => 0f,
            BaseCoordFormat1Table f1 => f1.Coordinate,
            BaseCoordFormat2Table f2 => f2.Coordinate,
            BaseCoordFormat3Table f3 => f3.Resolve(this, instance),
            _ => throw new InvalidDataException("Unknown BaseCoord table.")
        };

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort major = scope.Reader.ReadUInt16();
        ushort minor = scope.Reader.ReadUInt16();

        if (major != 1 || (minor != 0 && minor != 1))
            throw new InvalidDataException($"Unsupported BASE version {major}.{minor}.");

        Header = new BaseHeader(major, minor);

        HorizAxis = scope.ParseCommonTableOrDefault<AxisTable>(scope.Reader.ReadOffset16());
        VertAxis = scope.ParseCommonTableOrDefault<AxisTable>(scope.Reader.ReadOffset16());

        if (minor >= 1)
        {
            uint itemVarStoreOffset = scope.Reader.ReadOffset32();
            ItemVariationStore = itemVarStoreOffset == 0
                ? null
                : scope.ParseCommonTable<ItemVariationStore>(checked((long)itemVarStoreOffset));
        }

        tables.Add(this);
    }
}
