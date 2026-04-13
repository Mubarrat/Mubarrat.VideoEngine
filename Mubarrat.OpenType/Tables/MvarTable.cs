using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class MvarTable : IOpenTypeTable
{
    public string Tag => "MVAR";

    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }
    public ushort ValueRecordSize { get; private set; }
    public ushort ValueRecordCount { get; private set; }
    public ValueRecord[] ValueRecords { get; private set; } = [];
    public ItemVariationStore ItemVariationStore { get; private set; } = default!;

    public readonly record struct ValueRecord(string ValueTag, ushort DeltaSetOuterIndex, ushort DeltaSetInnerIndex);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        MajorVersion = scope.Reader.ReadUInt16();
        MinorVersion = scope.Reader.ReadUInt16();
        scope.Reader.ReadUInt16();
        ValueRecordSize = scope.Reader.ReadUInt16();
        ValueRecordCount = scope.Reader.ReadUInt16();
        ushort itemVariationStoreOffset = scope.Reader.ReadOffset16();

        int recordSize = ValueRecordSize == 0 ? 8 : ValueRecordSize;
        ValueRecords = new ValueRecord[ValueRecordCount];
        for (int i = 0; i < ValueRecords.Length; i++)
        {
            long start = scope.Reader.Position;
            string valueTag = scope.Reader.ReadTag();
            ushort outer = scope.Reader.ReadUInt16();
            ushort inner = scope.Reader.ReadUInt16();
            ValueRecords[i] = new ValueRecord(valueTag, outer, inner);

            int consumed = checked((int)(scope.Reader.Position - start));
            int trailing = recordSize - consumed;
            if (trailing > 0)
                scope.Reader.Seek(trailing, SeekOrigin.Current);
        }

        ItemVariationStore = scope.ParseCommonTable<ItemVariationStore>(itemVariationStoreOffset);
        tables.Add(this);
    }
}
