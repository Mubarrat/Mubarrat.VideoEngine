namespace Mubarrat.OpenType.Tables;

public sealed class MetaTable : IOpenTypeTable
{
    public string Tag => "meta";

    public uint Version { get; private set; }
    public uint Flags { get; private set; }
    public uint Reserved { get; private set; }
    public DataMapRecord[] DataMaps { get; private set; } = [];

    public readonly record struct DataMapRecord(string Tag, uint DataOffset, uint DataLength, byte[] Data);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadUInt32();
        Flags = scope.Reader.ReadUInt32();
        Reserved = scope.Reader.ReadUInt32();
        uint dataMapsCount = scope.Reader.ReadUInt32();

        DataMaps = new DataMapRecord[checked((int)dataMapsCount)];
        for (int i = 0; i < DataMaps.Length; i++)
        {
            string tag = scope.Reader.ReadTag();
            uint dataOffset = scope.Reader.ReadUInt32();
            uint dataLength = scope.Reader.ReadUInt32();

            byte[] data;
            if (dataLength == 0)
            {
                data = [];
            }
            else
            {
                using var dataScope = scope.EnterScope(dataOffset);
                data = dataScope.Reader.ReadBytes(checked((int)dataLength));
            }

            DataMaps[i] = new DataMapRecord(tag, dataOffset, dataLength, data);
        }

        tables.Add(this);
    }
}
