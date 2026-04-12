namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class KernTable : IOpenTypeTable
{
    public string Tag => "kern";

    public bool IsAppleVersion { get; private set; }
    public uint Version { get; private set; }
    public KernSubtableHeader[] Subtables { get; private set; } = [];

    public readonly record struct KernSubtableHeader(uint Length, ushort Coverage, ushort? TupleIndex);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort major = scope.Reader.ReadUInt16();
        ushort minorOrCount = scope.Reader.ReadUInt16();

        if (major == 0)
        {
            IsAppleVersion = false;
            Version = 0;
            ushort nTables = minorOrCount;
            Subtables = new KernSubtableHeader[nTables];

            for (int i = 0; i < Subtables.Length; i++)
            {
                scope.Reader.ReadUInt16();
                ushort length = scope.Reader.ReadUInt16();
                ushort coverage = scope.Reader.ReadUInt16();
                Subtables[i] = new KernSubtableHeader(length, coverage, null);
                if (length >= 6)
                    scope.Reader.Seek(length - 6, SeekOrigin.Current);
            }
        }
        else
        {
            IsAppleVersion = true;
            Version = ((uint)major << 16) | minorOrCount;
            uint nTables = scope.Reader.ReadUInt32();
            Subtables = new KernSubtableHeader[checked((int)nTables)];

            for (int i = 0; i < Subtables.Length; i++)
            {
                uint length = scope.Reader.ReadUInt32();
                ushort coverage = scope.Reader.ReadUInt16();
                ushort tupleIndex = scope.Reader.ReadUInt16();
                Subtables[i] = new KernSubtableHeader(length, coverage, tupleIndex);
                if (length >= 8)
                    scope.Reader.Seek((long)length - 8, SeekOrigin.Current);
            }
        }

        tables.Add(this);
    }
}
