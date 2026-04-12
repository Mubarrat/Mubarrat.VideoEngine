namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public record class FeatureParam(string tag) : IOpenTypeCommonTable<FeatureParam>
{
    public static FeatureParam? Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        if (param is not string tag)
            return null;

        switch (tag)
        {
            case ['c', 'v', var d2, var d3] when char.IsDigit(d2) && char.IsDigit(d3):
                return new CharacterVariantFeatureParam(
                    Tag: tag,
                    Format: scope.Reader.ReadUInt16(),
                    FeatUiLabelNameId: scope.Reader.ReadUInt16(),
                    FeatUiTooltipTextNameId: scope.Reader.ReadUInt16(),
                    SampleTextNameId: scope.Reader.ReadUInt16(),
                    NumNamedParameters: scope.Reader.ReadUInt16(),
                    FirstParamUiLabelNameId: scope.Reader.ReadUInt16(),
                    Character: scope.Reader.ReadUInt24Array(scope.Reader.ReadUInt16())
                );
            case "size":
                return new SizeFeatureParam(
                    DesignSize: scope.Reader.ReadUInt16(),
                    SubfamilyId: scope.Reader.ReadUInt16(),
                    SubfamilyNameId: scope.Reader.ReadUInt16(),
                    RangeStart: scope.Reader.ReadUInt16(),
                    RangeEnd: scope.Reader.ReadUInt16());
            case ['s', 's', var d2, var d3] when char.IsDigit(d2) && char.IsDigit(d3) && ((d2 - '0') * 10 + (d3 - '0')) is > 0 and < 21:
                return new StylisticSetFeatureParam(
                    Tag: tag,
                    Version: scope.Reader.ReadUInt16(),
                    UiLabelNameId: scope.Reader.ReadUInt16());
            default:
                return new FeatureParam(tag);
        }
    }
}

public record class CharacterVariantFeatureParam(
    string Tag,
    ushort Format,
    ushort FeatUiLabelNameId,
    ushort FeatUiTooltipTextNameId,
    ushort SampleTextNameId,
    ushort NumNamedParameters,
    ushort FirstParamUiLabelNameId,
    uint[] Character) : FeatureParam(Tag);

public record class SizeFeatureParam(
    ushort DesignSize,
    ushort SubfamilyId,
    ushort SubfamilyNameId,
    ushort RangeStart,
    ushort RangeEnd) : FeatureParam("size");

public record class StylisticSetFeatureParam(
    string Tag,
    ushort Version,
    ushort UiLabelNameId) : FeatureParam(Tag);
