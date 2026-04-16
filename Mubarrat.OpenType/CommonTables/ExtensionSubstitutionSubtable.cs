using Mubarrat.OpenType.Tables;
using Mubarrat.OpenType.TextShaping;
using static Mubarrat.OpenType.Tables.GposTable;
using static Mubarrat.OpenType.Tables.GsubTable;

namespace Mubarrat.OpenType.CommonTables;

public sealed record ExtensionSubstitutionSubtable<T>(
    ushort Format,
    ushort ExtensionLookupType,
    T ExtensionSubtable) : IGposSubtable, IGsubSubtable where T : IOpenTypeCommonTable<T>
{
    internal static ExtensionSubstitutionSubtable<T> Parse(OpenTypeReader.TableScope scope)
    {
        ushort extensionLookupType = scope.Reader.ReadUInt16();
        return new(1, extensionLookupType, scope.ParseCommonTable<T>(scope.Reader.ReadUInt32(), extensionLookupType));
    }

    bool IGposSubtable.Apply(GposTable gpos, List<OpenTypeTextShaper.GlyphInfo> buffer, OpenTypeTextShaper.GlyphAdjustment[] adjustments, GdefTable? gdef, int? atIndex, int depth, int recursionLimit)
        => ((IGposSubtable)ExtensionSubtable).Apply(gpos, buffer, adjustments, gdef, atIndex, depth, recursionLimit);

    bool IGsubSubtable.Apply(GsubTable gsub, List<OpenTypeTextShaper.GlyphInfo> buffer, int? atIndex, int depth, int recursionLimit)
        => ((IGsubSubtable)ExtensionSubtable).Apply(gsub, buffer, atIndex, depth, recursionLimit);
}
