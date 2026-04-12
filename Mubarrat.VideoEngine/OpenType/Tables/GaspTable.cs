namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class GaspTable : IOpenTypeTable
{
    public const string TableTag = "gasp";

    public string Tag => TableTag;

    public ushort Version { get; private set; }
    public GaspRangeRecord[] Ranges { get; private set; } = [];

    [Flags]
    public enum GaspBehavior : ushort
    {
        Gridfit = 0x0001,
        DoGray = 0x0002,
        SymmetricGridfit = 0x0004,
        SymmetricSmoothing = 0x0008
    }

    public readonly record struct GaspRangeRecord(ushort RangeMaxPpem, GaspBehavior Behavior);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadUInt16();
        ushort numRanges = scope.Reader.ReadUInt16();

        if (Version > 1)
            throw new InvalidDataException($"Unsupported gasp version {Version}.");

        Ranges = new GaspRangeRecord[numRanges];
        for (int i = 0; i < Ranges.Length; i++)
            Ranges[i] = new GaspRangeRecord(scope.Reader.ReadUInt16(), (GaspBehavior)scope.Reader.ReadUInt16());

        tables.Add(this);
    }
}
