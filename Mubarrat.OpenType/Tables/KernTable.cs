namespace Mubarrat.OpenType.Tables;

public sealed class KernTable : IOpenTypeTable
{
    public const string TableTag = "kern";

    public string Tag => "kern";

    public bool IsAppleVersion { get; private set; }
    public uint Version { get; private set; }
    public KernSubtableHeader[] Subtables { get; private set; } = [];
    private Dictionary<uint, short>[] Format0PairMaps { get; set; } = [];

    public readonly record struct KernSubtableHeader(uint Length, ushort Coverage, ushort? TupleIndex);

    public int GetKerningAdjustment(ushort leftGlyphId, ushort rightGlyphId)
    {
        if (Format0PairMaps.Length == 0)
            return 0;

        int total = 0;
        uint key = ((uint)leftGlyphId << 16) | rightGlyphId;

        for (int i = 0; i < Format0PairMaps.Length; i++)
        {
            if (Format0PairMaps[i].TryGetValue(key, out short value))
                total += value;
        }

        return total;
    }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        var format0Maps = new List<Dictionary<uint, short>>();

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
                long subtableStart = scope.Reader.Position;

                scope.Reader.ReadUInt16();
                ushort length = scope.Reader.ReadUInt16();
                ushort coverage = scope.Reader.ReadUInt16();
                Subtables[i] = new KernSubtableHeader(length, coverage, null);

                ushort format = (ushort)(coverage >> 8);
                if (format == 0 && length >= 14)
                {
                    ushort nPairs = scope.Reader.ReadUInt16();
                    scope.Reader.ReadUInt16();
                    scope.Reader.ReadUInt16();
                    scope.Reader.ReadUInt16();

                    var pairMap = new Dictionary<uint, short>(nPairs);
                    for (int p = 0; p < nPairs; p++)
                    {
                        ushort left = scope.Reader.ReadUInt16();
                        ushort right = scope.Reader.ReadUInt16();
                        short value = scope.Reader.ReadInt16();
                        uint key = ((uint)left << 16) | right;
                        pairMap[key] = value;
                    }

                    format0Maps.Add(pairMap);
                }

                scope.Reader.Seek(subtableStart + length, SeekOrigin.Begin);
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
                long subtableStart = scope.Reader.Position;

                uint length = scope.Reader.ReadUInt32();
                ushort coverage = scope.Reader.ReadUInt16();
                ushort tupleIndex = scope.Reader.ReadUInt16();
                Subtables[i] = new KernSubtableHeader(length, coverage, tupleIndex);

                ushort format = (ushort)(coverage >> 8);
                if (format == 0 && length >= 16)
                {
                    ushort nPairs = scope.Reader.ReadUInt16();
                    scope.Reader.ReadUInt16();
                    scope.Reader.ReadUInt16();
                    scope.Reader.ReadUInt16();

                    var pairMap = new Dictionary<uint, short>(nPairs);
                    for (int p = 0; p < nPairs; p++)
                    {
                        ushort left = scope.Reader.ReadUInt16();
                        ushort right = scope.Reader.ReadUInt16();
                        short value = scope.Reader.ReadInt16();
                        uint key = ((uint)left << 16) | right;
                        pairMap[key] = value;
                    }

                    format0Maps.Add(pairMap);
                }

                scope.Reader.Seek(subtableStart + length, SeekOrigin.Begin);
            }
        }

        Format0PairMaps = [.. format0Maps];

        tables.Add(this);
    }
}
