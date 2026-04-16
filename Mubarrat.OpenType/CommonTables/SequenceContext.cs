using Mubarrat.OpenType.Tables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.CommonTables;

public abstract record SequenceContext : IOpenTypeCommonTable<SequenceContext>, GposTable.IGposSubtable, GsubTable.IGsubSubtable
{
    public abstract bool TryMatch(ushort[] glyphs, int startIndex, out SequenceMatch match);

    public static SequenceContext Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        if (param is not ushort format)
            return null!;
        return format switch
        {
            1 => SequenceContextFormat1.Parse(scope),
            2 => SequenceContextFormat2.Parse(scope),
            3 => SequenceContextFormat3.Parse(scope),
            _ => throw new NotSupportedException($"Unsupported SequenceContext format {format}")
        };
    }

    bool GposTable.IGposSubtable.Apply(GposTable gpos, List<OpenTypeTextShaper.GlyphInfo> buffer, OpenTypeTextShaper.GlyphAdjustment[] adjustments, GdefTable? gdef, int? atIndex, int depth, int recursionLimit)
    {
        bool changed = false;
        int i = 0;

        while (true)
        {
            var glyphIds = OpenTypeTextShaper.ToLookupGlyphIds(buffer, out var indexMap);
            if (i >= glyphIds.Length)
                break;

            if (!TryMatchSequenceContext(glyphIds, i, out var match))
            {
                i++;
                continue;
            }

            int advance = Math.Max(match.Length, 1);
            match = OpenTypeTextShaper.RemapSequenceMatch(match, indexMap, buffer.Count);
            changed |= OpenTypeTextShaper.ApplyGposMatchedLookups(gpos, gdef, match, buffer, adjustments, depth + 1, recursionLimit);
            i += advance;
        }

        return changed;
    }

    bool GsubTable.IGsubSubtable.Apply(GsubTable gsub, List<OpenTypeTextShaper.GlyphInfo> buffer, int? atIndex, int depth, int recursionLimit)
    {
        bool changed = false;
        int i = 0;

        while (i < buffer.Count)
        {
            var glyphIds = OpenTypeTextShaper.ToGlyphIds(buffer);
            if (!TryMatchSequenceContext(glyphIds, i, out var match))
            {
                i++;
                continue;
            }

            changed |= OpenTypeTextShaper.ApplyGsubMatchedLookups(gsub, match, buffer, depth + 1, recursionLimit);
            i += Math.Max(match.Length, 1);
        }

        return changed;
    }

    internal bool TryMatchSequenceContext(ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        if (startIndex < 0 || startIndex >= glyphs.Length)
        {
            match = default;
            return false;
        }

        try
        {
            return TryMatch(glyphs, startIndex, out match);
        }
        catch (IndexOutOfRangeException)
        {
            match = default;
            return false;
        }
    }
}
