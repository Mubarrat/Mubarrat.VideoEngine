namespace Mubarrat.OpenType.Tables;

public sealed class HdmxTable : IOpenTypeTable
{
    public string Tag => "hdmx";

    public ushort Version { get; private set; }
    public short NumRecords { get; private set; }
    public int SizeDeviceRecord { get; private set; }
    public DeviceRecord[] Records { get; private set; } = [];

    public readonly record struct DeviceRecord(byte PixelSize, byte MaxWidth, byte[] Widths);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped) => tables.Request<MaxpTable>((maxp, scope) =>
    {
        Version = scope.Reader.ReadUInt16();
        NumRecords = scope.Reader.ReadInt16();
        SizeDeviceRecord = scope.Reader.ReadInt32();

        int recordCount = Math.Max(NumRecords, (short)0);
        Records = new DeviceRecord[recordCount];
        for (int i = 0; i < Records.Length; i++)
        {
            long start = scope.Reader.Position;
            byte pixelSize = scope.Reader.ReadUInt8();
            byte maxWidth = scope.Reader.ReadUInt8();
            byte[] widths = scope.Reader.ReadBytes(maxp.NumGlyphs);

            int consumed = checked((int)(scope.Reader.Position - start));
            int padding = SizeDeviceRecord - consumed;
            if (padding > 0)
                scope.Reader.Seek(padding, SeekOrigin.Current);

            Records[i] = new DeviceRecord(pixelSize, maxWidth, widths);
        }

        tables.Add(this);
    });
}
