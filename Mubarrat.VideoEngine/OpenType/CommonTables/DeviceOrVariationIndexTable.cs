namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public enum DeviceDeltaFormat : ushort
{
    Local2BitDeltas = 0x0001,
    Local4BitDeltas = 0x0002,
    Local8BitDeltas = 0x0003,
    VariationIndex = 0x8000
}

public abstract record DeviceOrVariationIndexTable(DeviceDeltaFormat DeltaFormat) : IOpenTypeCommonTable<DeviceOrVariationIndexTable>
{
    public static DeviceOrVariationIndexTable Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        ushort startSize = scope.Reader.ReadUInt16();
        ushort endSize = scope.Reader.ReadUInt16();
        DeviceDeltaFormat deltaFormat = (DeviceDeltaFormat)scope.Reader.ReadUInt16();

        if (deltaFormat == DeviceDeltaFormat.VariationIndex)
            return new VariationIndexTable(deltaFormat, startSize, endSize);

        int count = endSize >= startSize ? (endSize - startSize + 1) : 0;
        var deltas = new short[count];

        int bitsPerValue = deltaFormat switch
        {
            DeviceDeltaFormat.Local2BitDeltas => 2,
            DeviceDeltaFormat.Local4BitDeltas => 4,
            DeviceDeltaFormat.Local8BitDeltas => 8,
            _ => throw new InvalidDataException($"Unsupported Device delta format {deltaFormat}.")
        };

        int valuesPerWord = 16 / bitsPerValue;
        int mask = (1 << bitsPerValue) - 1;
        int signBit = 1 << (bitsPerValue - 1);

        int index = 0;
        while (index < count)
        {
            ushort packed = scope.Reader.ReadUInt16();

            for (int slot = valuesPerWord - 1; slot >= 0 && index < count; slot--)
            {
                int raw = (packed >> (slot * bitsPerValue)) & mask;
                if ((raw & signBit) != 0)
                    raw |= ~mask;
                deltas[index++] = (short)raw;
            }
        }

        return new DeviceTable(deltaFormat, startSize, endSize, deltas);
    }
}

public sealed record DeviceTable(
    DeviceDeltaFormat DeltaFormat,
    ushort StartSize,
    ushort EndSize,
    short[] DeltaValues) : DeviceOrVariationIndexTable(DeltaFormat);

public sealed record VariationIndexTable(
    DeviceDeltaFormat DeltaFormat,
    ushort DeltaSetOuterIndex,
    ushort DeltaSetInnerIndex) : DeviceOrVariationIndexTable(DeltaFormat);
