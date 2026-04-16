using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;
using static Mubarrat.OpenType.Tables.GsubTable;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GsubTable
{
    public sealed record LigatureSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        LigatureSet[] LigatureSets) : IGsubSubtable
    {
        public bool TryFindLigature(ReadOnlySpan<ushort> glyphIds, out ushort ligatureGlyphId, out int consumedGlyphCount)
        {
            ligatureGlyphId = 0;
            consumedGlyphCount = 0;

            if (glyphIds.Length == 0)
                return false;

            if (!Coverage.TryGetIndex(glyphIds[0], out ushort coverageIndex))
                return false;
            if (coverageIndex >= LigatureSets.Length)
                return false;

            var ligatures = LigatureSets[coverageIndex].Ligatures;
            int bestLength = 0;
            ushort bestGlyph = 0;

            for (int i = 0; i < ligatures.Length; i++)
            {
                var candidate = ligatures[i];
                int requiredLength = candidate.ComponentGlyphIds.Length + 1;

                if (requiredLength <= bestLength || requiredLength > glyphIds.Length)
                    continue;

                bool matched = true;
                for (int c = 0; c < candidate.ComponentGlyphIds.Length; c++)
                {
                    if (glyphIds[c + 1] == candidate.ComponentGlyphIds[c])
                        continue;

                    matched = false;
                    break;
                }

                if (!matched)
                    continue;

                bestLength = requiredLength;
                bestGlyph = candidate.LigatureGlyph;
            }

            if (bestLength <= 0)
                return false;

            ligatureGlyphId = bestGlyph;
            consumedGlyphCount = bestLength;
            return true;
        }

        internal static LigatureSubstitutionSubtable Parse(OpenTypeReader.TableScope scope) => new(1,
            Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()),
            LigatureSets: LigatureSet.ParseListFromOffsets16(scope));

        bool IGsubSubtable.Apply(GsubTable gsub, List<OpenTypeTextShaper.GlyphInfo> buffer, int? atIndex, int depth, int recursionLimit)
        {
            bool changed = false;
            int start = atIndex ?? 0;
            int end = atIndex ?? (buffer.Count - 1);

            for (int i = start; i <= end && i < buffer.Count; i++)
            {
                var tailSpan = OpenTypeTextShaper.ToGlyphIds(buffer).AsSpan(i);
                if (!TryFindLigature(tailSpan, out ushort bestGlyph, out int bestLength))
                    continue;

                int mergedCluster = buffer[i].Cluster;
                for (int c = 1; c < bestLength; c++)
                {
                    if (buffer[i + c].Cluster < mergedCluster)
                        mergedCluster = buffer[i + c].Cluster;
                }

                buffer[i] = buffer[i] with { GlyphId = bestGlyph, Cluster = mergedCluster };
                if (bestLength > 1)
                    buffer.RemoveRange(i + 1, bestLength - 1);

                changed = true;

                if (atIndex is not null)
                    return true;

                end -= bestLength - 1;
            }

            return changed;
        }
    }
}
