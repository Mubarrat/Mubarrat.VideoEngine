namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class CffTable : IOpenTypeTable
{
    public string Tag => "CFF ";

    public byte Major { get; private set; }
    public byte Minor { get; private set; }
    public byte HeaderSize { get; private set; }
    public byte OffsetSize { get; private set; }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Major = scope.Reader.ReadUInt8();
        Minor = scope.Reader.ReadUInt8();
        HeaderSize = scope.Reader.ReadUInt8();
        OffsetSize = scope.Reader.ReadUInt8();
        tables.Add(this);
    }
}
