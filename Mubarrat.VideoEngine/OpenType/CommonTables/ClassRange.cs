namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct ClassRange(ushort StartGlyphID, ushort EndGlyphID, ushort Class)
    : IOpenTypeCommonTable<ClassRange>
{
    public static ClassRange Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        StartGlyphID: scope.Reader.ReadUInt16(),
        EndGlyphID: scope.Reader.ReadUInt16(),
        Class: scope.Reader.ReadUInt16());
}
