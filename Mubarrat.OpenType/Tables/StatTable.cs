namespace Mubarrat.OpenType.Tables;

public sealed class StatTable : IOpenTypeTable
{
    public const string TableTag = "STAT";

    public string Tag => TableTag;

    public HeaderInfo Header { get; private set; } = default;
    public AxisRecord[] DesignAxes { get; private set; } = [];
    public AxisValueTable[] AxisValues { get; private set; } = [];

    public readonly record struct HeaderInfo(
        ushort MajorVersion,
        ushort MinorVersion,
        ushort DesignAxisSize,
        ushort DesignAxisCount,
        uint DesignAxesOffset,
        ushort AxisValueCount,
        uint OffsetToAxisValueOffsets,
        ushort ElidedFallbackNameId);

    public readonly record struct AxisRecord(string AxisTag, ushort AxisNameId, ushort AxisOrdering);

    [Flags]
    public enum AxisValueFlags : ushort
    {
        None = 0,
        OlderSiblingFontAttribute = 0x0001,
        ElidableAxisValueName = 0x0002
    }

    public abstract record AxisValueTable(ushort Format, AxisValueFlags Flags, ushort ValueNameId)
    {
        public static AxisValueTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort format = scope.Reader.ReadUInt16();
            return format switch
            {
                1 => AxisValueFormat1.ParseFormat1(scope),
                2 => AxisValueFormat2.ParseFormat2(scope),
                3 => AxisValueFormat3.ParseFormat3(scope),
                4 => AxisValueFormat4.ParseFormat4(scope),
                _ => throw new InvalidDataException($"Unsupported STAT axis value format {format}.")
            };
        }
    }

    public sealed record AxisValueFormat1(ushort AxisIndex, AxisValueFlags Flags, ushort ValueNameId, float Value)
        : AxisValueTable(1, Flags, ValueNameId)
    {
        internal static AxisValueFormat1 ParseFormat1(OpenTypeReader.TableScope scope)
            => new(scope.Reader.ReadUInt16(), (AxisValueFlags)scope.Reader.ReadUInt16(), scope.Reader.ReadUInt16(), scope.Reader.ReadFixed());
    }

    public sealed record AxisValueFormat2(
        ushort AxisIndex,
        AxisValueFlags Flags,
        ushort ValueNameId,
        float NominalValue,
        float RangeMinValue,
        float RangeMaxValue)
        : AxisValueTable(2, Flags, ValueNameId)
    {
        internal static AxisValueFormat2 ParseFormat2(OpenTypeReader.TableScope scope)
            => new(
                scope.Reader.ReadUInt16(),
                (AxisValueFlags)scope.Reader.ReadUInt16(),
                scope.Reader.ReadUInt16(),
                scope.Reader.ReadFixed(),
                scope.Reader.ReadFixed(),
                scope.Reader.ReadFixed());
    }

    public sealed record AxisValueFormat3(
        ushort AxisIndex,
        AxisValueFlags Flags,
        ushort ValueNameId,
        float Value,
        float LinkedValue)
        : AxisValueTable(3, Flags, ValueNameId)
    {
        internal static AxisValueFormat3 ParseFormat3(OpenTypeReader.TableScope scope)
            => new(
                scope.Reader.ReadUInt16(),
                (AxisValueFlags)scope.Reader.ReadUInt16(),
                scope.Reader.ReadUInt16(),
                scope.Reader.ReadFixed(),
                scope.Reader.ReadFixed());
    }

    public readonly record struct AxisValue(ushort AxisIndex, float Value);

    public sealed record AxisValueFormat4(AxisValueFlags Flags, ushort ValueNameId, AxisValue[] AxisValues)
        : AxisValueTable(4, Flags, ValueNameId)
    {
        internal static AxisValueFormat4 ParseFormat4(OpenTypeReader.TableScope scope)
        {
            ushort axisCount = scope.Reader.ReadUInt16();
            AxisValueFlags flags = (AxisValueFlags)scope.Reader.ReadUInt16();
            ushort valueNameId = scope.Reader.ReadUInt16();

            var axisValues = new AxisValue[axisCount];
            for (int i = 0; i < axisValues.Length; i++)
                axisValues[i] = new AxisValue(scope.Reader.ReadUInt16(), scope.Reader.ReadFixed());

            return new AxisValueFormat4(flags, valueNameId, axisValues);
        }
    }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort majorVersion = scope.Reader.ReadUInt16();
        ushort minorVersion = scope.Reader.ReadUInt16();
        ushort designAxisSize = scope.Reader.ReadUInt16();
        ushort designAxisCount = scope.Reader.ReadUInt16();
        uint designAxesOffset = scope.Reader.ReadOffset32();
        ushort axisValueCount = scope.Reader.ReadUInt16();
        uint offsetToAxisValueOffsets = scope.Reader.ReadOffset32();
        ushort elidedFallbackNameId = minorVersion >= 1 ? scope.Reader.ReadUInt16() : (ushort)0;

        if (majorVersion != 1)
            throw new InvalidDataException($"Unsupported STAT major version {majorVersion}.");

        if (designAxisSize < 8 && designAxisCount > 0)
            throw new InvalidDataException($"STAT designAxisSize must be at least 8. Found {designAxisSize}.");

        Header = new HeaderInfo(
            majorVersion,
            minorVersion,
            designAxisSize,
            designAxisCount,
            designAxesOffset,
            axisValueCount,
            offsetToAxisValueOffsets,
            elidedFallbackNameId);

        DesignAxes = ParseDesignAxes(scope, designAxisCount, designAxisSize, designAxesOffset);
        AxisValues = ParseAxisValues(scope, axisValueCount, offsetToAxisValueOffsets);

        tables.Add(this);
    }

    private static AxisRecord[] ParseDesignAxes(OpenTypeReader.TableScope scope, ushort designAxisCount, ushort designAxisSize, uint designAxesOffset)
    {
        if (designAxisCount == 0 || designAxesOffset == 0)
            return [];

        var axes = new AxisRecord[designAxisCount];
        using var axesScope = scope.EnterScope(designAxesOffset);
        for (int i = 0; i < axes.Length; i++)
        {
            string axisTag = axesScope.Reader.ReadTag();
            ushort axisNameId = axesScope.Reader.ReadUInt16();
            ushort axisOrdering = axesScope.Reader.ReadUInt16();

            int extraBytes = designAxisSize - 8;
            if (extraBytes > 0)
                axesScope.Reader.Seek(extraBytes, SeekOrigin.Current);

            axes[i] = new AxisRecord(axisTag, axisNameId, axisOrdering);
        }

        return axes;
    }

    private static AxisValueTable[] ParseAxisValues(OpenTypeReader.TableScope scope, ushort axisValueCount, uint offsetToAxisValueOffsets)
    {
        if (axisValueCount == 0 || offsetToAxisValueOffsets == 0)
            return [];

        using var offsetsScope = scope.EnterScope(offsetToAxisValueOffsets);
        var axisValues = new AxisValueTable[axisValueCount];
        for (int i = 0; i < axisValues.Length; i++)
        {
            ushort axisValueOffset = offsetsScope.Reader.ReadOffset16();
            using var axisValueScope = offsetsScope.EnterScope(axisValueOffset);
            axisValues[i] = AxisValueTable.Parse(axisValueScope);
        }
        return axisValues;
    }
}
