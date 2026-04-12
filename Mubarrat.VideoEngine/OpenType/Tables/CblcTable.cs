using Mubarrat.VideoEngine.OpenType.CommonTables;

namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class CblcTable : IOpenTypeTable
{
    public string Tag => "CBLC";
    public uint Version { get; private set; }
    public uint NumSizes { get; private set; }
    public EmbeddedBitmapSizeRecord[] BitmapSizes { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadVersion16Dot16();
        NumSizes = scope.Reader.ReadUInt32();
        BitmapSizes = new EmbeddedBitmapSizeRecord[checked((int)NumSizes)];
        for (int i = 0; i < BitmapSizes.Length; i++)
            BitmapSizes[i] = EmbeddedBitmapSizeRecord.Read(scope.Reader);
        tables.Add(this);
    }
}
