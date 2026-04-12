namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct ClassDef(ClassRange[] Ranges) : IOpenTypeCommonTable<ClassDef>
{
    public static ClassDef Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        ushort format = scope.Reader.ReadUInt16();
        return format switch
        {
            1 => ParseFormat1(scope),
            2 => new(ClassRange.ParseListContiguous(scope)),
            _ => throw new NotSupportedException($"Unsupported ClassDef format: {format}")
        };
    }

    private static ClassDef ParseFormat1(OpenTypeReader.TableScope scope)
    {
        ushort start = scope.Reader.ReadUInt16();
        var ranges = new ClassRange[scope.Reader.ReadUInt16()];
        for (int i = 0; i < ranges.Length; i++)
            ranges[i] = new((ushort)(start + i), (ushort)(start + i), scope.Reader.ReadUInt16());
        return new(ranges);
    }

    public bool TryGetClass(ushort glyphId, out ushort classValue)
    {
        var ranges = Ranges;

        if (ranges is null)
        {
            classValue = 0;
            return false;
        }

        int lo = 0, hi = ranges.Length - 1;

        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            ref readonly var r = ref ranges[mid];

            if (glyphId < r.StartGlyphID) hi = mid - 1;
            else if (glyphId > r.EndGlyphID) lo = mid + 1;
            else
            {
                classValue = r.Class;
                return true;
            }
        }

        classValue = 0;
        return false;
    }
}
