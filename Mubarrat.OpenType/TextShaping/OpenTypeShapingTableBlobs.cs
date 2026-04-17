namespace Mubarrat.OpenType.TextShaping;

internal readonly record struct OpenTypeShapingTableBlobs(
    ReadOnlyMemory<byte> Cmap,
    ReadOnlyMemory<byte> Head,
    ReadOnlyMemory<byte> Hhea,
    ReadOnlyMemory<byte> Hmtx,
    ReadOnlyMemory<byte> Gsub,
    ReadOnlyMemory<byte> Gpos,
    ReadOnlyMemory<byte> Gdef,
    ReadOnlyMemory<byte> Kern,
    ReadOnlyMemory<byte> Fvar,
    ReadOnlyMemory<byte> Avar,
    ReadOnlyMemory<byte> Hvar)
{
    internal static OpenTypeShapingTableBlobs FromFace(FontFace face)
    {
        face.TryGetShapingTableBlob("cmap", out var cmap);
        face.TryGetShapingTableBlob("head", out var head);
        face.TryGetShapingTableBlob("hhea", out var hhea);
        face.TryGetShapingTableBlob("hmtx", out var hmtx);
        face.TryGetShapingTableBlob("gsub", out var gsub);
        face.TryGetShapingTableBlob("gpos", out var gpos);
        face.TryGetShapingTableBlob("gdef", out var gdef);
        face.TryGetShapingTableBlob("kern", out var kern);
        face.TryGetShapingTableBlob("fvar", out var fvar);
        face.TryGetShapingTableBlob("avar", out var avar);
        face.TryGetShapingTableBlob("hvar", out var hvar);

        return new OpenTypeShapingTableBlobs(cmap, head, hhea, hmtx, gsub, gpos, gdef, kern, fvar, avar, hvar);
    }
}
