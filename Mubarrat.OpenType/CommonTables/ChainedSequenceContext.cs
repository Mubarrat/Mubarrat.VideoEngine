using Mubarrat.OpenType.Tables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.CommonTables;

public abstract record ChainedSequenceContext : IOpenTypeCommonTable<ChainedSequenceContext>, GposTable.IGposSubtable, GsubTable.IGsubSubtable
{
    public static ChainedSequenceContext Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        if (param is not ushort format)
            return null!;
        return format switch
        {
            1 => ChainedSequenceContextFormat1.Parse(scope),
            2 => ChainedSequenceContextFormat2.Parse(scope),
            3 => ChainedSequenceContextFormat3.Parse(scope),
            _ => throw new NotSupportedException($"ChainedSequenceContext format {format}")
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

            if (!TryMatchChainedContext(glyphIds, i, out var match))
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

        while (true)
        {
            var glyphIds = OpenTypeTextShaper.ToLookupGlyphIds(buffer, out var indexMap);
            if (i >= glyphIds.Length)
                break;

            if (!TryMatchChainedContext(glyphIds, i, out var match))
            {
                i++;
                continue;
            }

            int advance = Math.Max(match.Length, 1);
            match = OpenTypeTextShaper.RemapSequenceMatch(match, indexMap, buffer.Count);
            changed |= OpenTypeTextShaper.ApplyGsubMatchedLookups(gsub, match, buffer, depth + 1, recursionLimit);
            i += advance;
        }

        return changed;
    }

    internal abstract bool TryMatchChainedContext(ushort[] glyphs, int startIndex, out SequenceMatch match);
}
