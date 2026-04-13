namespace Mubarrat.OpenType.Tables;

public sealed class FvarTable : IOpenTypeTable
{
    public const string TableTag = "fvar";

    public string Tag => TableTag;

    public HeaderInfo Header { get; private set; } = default;
    public AxisRecord[] Axes { get; private set; } = [];
    public InstanceRecord[] Instances { get; private set; } = [];

    public readonly record struct HeaderInfo(
        uint Version,
        ushort OffsetToData,
        ushort CountSizePairs,
        ushort AxisCount,
        ushort AxisSize,
        ushort InstanceCount,
        ushort InstanceSize);

    [Flags]
    public enum AxisFlags : ushort
    {
        None = 0,
        Hidden = 0x0001
    }

    public readonly record struct AxisRecord(
        string AxisTag,
        float MinValue,
        float DefaultValue,
        float MaxValue,
        AxisFlags Flags,
        ushort AxisNameId);

    public readonly record struct InstanceRecord(
        ushort SubfamilyNameId,
        ushort Flags,
        float[] Coordinates,
        ushort? PostScriptNameId);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        uint version = scope.Reader.ReadVersion16Dot16();
        ushort offsetToData = scope.Reader.ReadUInt16();
        ushort countSizePairs = scope.Reader.ReadUInt16();
        ushort axisCount = scope.Reader.ReadUInt16();
        ushort axisSize = scope.Reader.ReadUInt16();
        ushort instanceCount = scope.Reader.ReadUInt16();
        ushort instanceSize = scope.Reader.ReadUInt16();

        if (version != 0x00010000)
            throw new InvalidDataException($"Unsupported fvar version 0x{version:X8}.");

        if (axisSize < 20 && axisCount > 0)
            throw new InvalidDataException($"Invalid fvar axis size {axisSize}.");

        int baseInstanceSize = 4 + axisCount * 4;
        if (instanceSize < baseInstanceSize && instanceCount > 0)
            throw new InvalidDataException($"Invalid fvar instance size {instanceSize}.");

        Header = new HeaderInfo(version, offsetToData, countSizePairs, axisCount, axisSize, instanceCount, instanceSize);

        using var dataScope = scope.EnterScope(offsetToData);

        Axes = new AxisRecord[axisCount];
        for (int i = 0; i < Axes.Length; i++)
        {
            string axisTag = dataScope.Reader.ReadTag();
            float minValue = dataScope.Reader.ReadFixed();
            float defaultValue = dataScope.Reader.ReadFixed();
            float maxValue = dataScope.Reader.ReadFixed();
            AxisFlags flags = (AxisFlags)dataScope.Reader.ReadUInt16();
            ushort axisNameId = dataScope.Reader.ReadUInt16();

            int extra = axisSize - 20;
            if (extra > 0)
                dataScope.Reader.Seek(extra, SeekOrigin.Current);

            Axes[i] = new AxisRecord(axisTag, minValue, defaultValue, maxValue, flags, axisNameId);
        }

        Instances = new InstanceRecord[instanceCount];
        bool hasPostScriptNameId = instanceSize >= baseInstanceSize + 2;

        for (int i = 0; i < Instances.Length; i++)
        {
            ushort subfamilyNameId = dataScope.Reader.ReadUInt16();
            ushort flags = dataScope.Reader.ReadUInt16();

            float[] coordinates = new float[axisCount];
            for (int axisIndex = 0; axisIndex < coordinates.Length; axisIndex++)
                coordinates[axisIndex] = dataScope.Reader.ReadFixed();

            ushort? postScriptNameId = hasPostScriptNameId ? dataScope.Reader.ReadUInt16() : null;

            int consumed = baseInstanceSize + (hasPostScriptNameId ? 2 : 0);
            int trailing = instanceSize - consumed;
            if (trailing > 0)
                dataScope.Reader.Seek(trailing, SeekOrigin.Current);

            Instances[i] = new InstanceRecord(subfamilyNameId, flags, coordinates, postScriptNameId);
        }

        tables.Add(this);
    }
}
