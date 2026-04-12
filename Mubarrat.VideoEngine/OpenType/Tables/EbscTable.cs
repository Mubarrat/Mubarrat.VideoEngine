using Mubarrat.VideoEngine.OpenType.CommonTables;

namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class EbscTable : IOpenTypeTable
{
    public string Tag => "EBSC";

    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }
    public uint NumSizes { get; private set; }
    public BitmapScaleRecord[] Strikes { get; private set; } = [];

    public readonly record struct BitmapScaleRecord(
        SbitLineMetrics HorizontalMetrics,
        SbitLineMetrics VerticalMetrics,
        byte PpemX,
        byte PpemY,
        byte SubstitutePpemX,
        byte SubstitutePpemY);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        MajorVersion = scope.Reader.ReadUInt16();
        MinorVersion = scope.Reader.ReadUInt16();
        NumSizes = scope.Reader.ReadUInt32();

        Strikes = new BitmapScaleRecord[checked((int)NumSizes)];
        for (int i = 0; i < Strikes.Length; i++)
        {
            Strikes[i] = new BitmapScaleRecord(
                SbitLineMetrics.Read(scope.Reader),
                SbitLineMetrics.Read(scope.Reader),
                scope.Reader.ReadUInt8(),
                scope.Reader.ReadUInt8(),
                scope.Reader.ReadUInt8(),
                scope.Reader.ReadUInt8());
        }

        tables.Add(this);
    }
}
