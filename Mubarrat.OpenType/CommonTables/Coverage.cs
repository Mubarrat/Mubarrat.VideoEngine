namespace Mubarrat.OpenType.CommonTables;

public readonly record struct Coverage(ushort[]? GlyphArray, RangeRecord[]? Ranges) : IOpenTypeCommonTable<Coverage>
{
    public static Coverage Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        ushort format = scope.Reader.ReadUInt16();
        return format switch
        {
            1 => new(GlyphArray: scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()), Ranges: null),
            2 => new(GlyphArray: null, Ranges: RangeRecord.ParseListContiguous(scope)),
            _ => throw new NotSupportedException($"Unsupported Coverage format: {format}")
        };
    }

    public bool TryGetIndex(ushort glyphId, out ushort coverageIndex)
    {
        if (GlyphArray is not null)
            return FindFormat1(glyphId, out coverageIndex);

        if (Ranges is not null)
            return FindFormat2(glyphId, out coverageIndex);

        coverageIndex = 0;
        return false;
    }

    private bool FindFormat1(ushort glyphId, out ushort index)
    {
        var arr = GlyphArray!;

        int lo = 0;
        int hi = arr.Length - 1;

        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var g = arr[mid];

            if (g == glyphId)
            {
                index = (ushort)mid;
                return true;
            }

            if (g < glyphId) lo = mid + 1;
            else hi = mid - 1;
        }

        index = 0;
        return false;
    }

    private bool FindFormat2(ushort glyphId, out ushort index)
    {
        var ranges = Ranges!;

        int lo = 0;
        int hi = ranges.Length - 1;

        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var r = ranges[mid];

            if (glyphId < r.StartGlyphID)
            {
                hi = mid - 1;
                continue;
            }

            if (glyphId > r.EndGlyphID)
            {
                lo = mid + 1;
                continue;
            }

            index = (ushort)(r.StartCoverageIndex + (glyphId - r.StartGlyphID));
            return true;
        }

        index = 0;
        return false;
    }
}
