using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GsubTable
{
    public sealed record AlternateSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        AlternateSet[] AlternateSets) : IGsubSubtable
    {
        public bool TryGetAlternateGlyph(ushort glyphId, out ushort alternateGlyphId)
        {
            alternateGlyphId = glyphId;

            if (!Coverage.TryGetIndex(glyphId, out ushort coverageIndex))
                return false;
            if (coverageIndex >= AlternateSets.Length)
                return false;

            var alternates = AlternateSets[coverageIndex].AlternateGlyphIds;
            if (alternates.Length == 0)
                return false;

            alternateGlyphId = alternates[0];
            return true;
        }

        internal static AlternateSubstitutionSubtable Parse(OpenTypeReader.TableScope scope) => new(1,
            Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()),
            AlternateSets: AlternateSet.ParseListFromOffsets16(scope));

        bool IGsubSubtable.Apply(GsubTable gsub, List<OpenTypeTextShaper.GlyphInfo> buffer, int? atIndex, int depth, int recursionLimit)
        {
            bool changed = false;
            int start = atIndex ?? 0;
            int end = atIndex ?? (buffer.Count - 1);

            for (int i = start; i <= end && i < buffer.Count; i++)
            {
                if (!TryGetAlternateGlyph(buffer[i].GlyphId, out ushort alternateGlyphId))
                    continue;

                if (alternateGlyphId == buffer[i].GlyphId)
                    continue;

                buffer[i] = buffer[i] with { GlyphId = alternateGlyphId };
                changed = true;
            }

            return changed;
        }
    }
}
