using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class EblcTable : IOpenTypeTable
{
    public string Tag => "EBLC";
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
