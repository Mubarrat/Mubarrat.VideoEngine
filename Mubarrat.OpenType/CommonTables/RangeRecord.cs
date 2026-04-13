namespace Mubarrat.OpenType.CommonTables;

public readonly record struct RangeRecord(
    ushort StartGlyphID,
    ushort EndGlyphID,
    ushort StartCoverageIndex) : IOpenTypeCommonTable<RangeRecord>
{
    public static RangeRecord Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        StartGlyphID: scope.Reader.ReadUInt16(),
        EndGlyphID: scope.Reader.ReadUInt16(),
        StartCoverageIndex: scope.Reader.ReadUInt16());
}
