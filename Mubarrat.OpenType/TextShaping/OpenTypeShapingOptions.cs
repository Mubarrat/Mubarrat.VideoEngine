namespace Mubarrat.OpenType.TextShaping;

/// <summary>
/// Optional shaping controls, including user-specified OpenType feature tags.
/// </summary>
public sealed record OpenTypeShapingOptions(
    IReadOnlyCollection<string>? ExtraFeatures = null,
    IReadOnlyCollection<string>? DisabledFeatures = null,
    bool? RightToLeft = null,
    string? LanguageTag = null,
    int? MaxContextLookupRecursion = null,
    IReadOnlyDictionary<string, float>? VariationCoordinates = null,
    bool ApplyIndicPreReordering = true,
    bool NormalizeInputToFormC = false,
    bool EnableLegacyKernFallback = true)
{
    internal static readonly OpenTypeShapingOptions Default = new();
}
