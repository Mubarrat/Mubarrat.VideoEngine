using Mubarrat.OpenType.Tables;

namespace Mubarrat.VideoEngine.Latex;

public static class TexDefaults
{
    public static readonly MathTable MathTable = new()
    {
        MathConstants = MathConstants,
        MathGlyphInfo = default,
        MathVariants = default
    };

    public static readonly HeadTable HeadTable = new()
    {
        UnitsPerEm = 1000
        // We only care about the UnitsPerEm field of the head table,
        // as it is used to convert the font units to absolute units in the math layout engine.
        // The other fields are not relevant for our purposes and can be left with their default values.
    };

    private static readonly MathTable.MathConstantsData MathConstants = new(
        ScriptPercentScaleDown: 71,
        ScriptScriptPercentScaleDown: 50,
        DelimitedSubFormulaMinHeight: 0,
        DisplayOperatorMinHeight: 0,

        MathLeading: 0,
        AxisHeight: 250, // ~0.25em
        AccentBaseHeight: 0,
        FlattenedAccentBaseHeight: 0,

        SubscriptShiftDown: 200,
        SubscriptTopMax: 0,
        SubscriptBaselineDropMin: 0,

        SuperscriptShiftUp: 400,
        SuperscriptShiftUpCramped: 300,
        SuperscriptBottomMin: 0,
        SuperscriptBaselineDropMax: 0,

        SubSuperscriptGapMin: 150,
        SuperscriptBottomMaxWithSubscript: 0,

        SpaceAfterScript: 100,

        UpperLimitGapMin: 0,
        UpperLimitBaselineRiseMin: 0,

        LowerLimitGapMin: 0,
        LowerLimitBaselineDropMin: 0,

        StackTopShiftUp: 0,
        StackTopDisplayStyleShiftUp: 0,
        StackBottomShiftDown: 0,
        StackBottomDisplayStyleShiftDown: 0,

        StackGapMin: 0,
        StackDisplayStyleGapMin: 0,

        StretchStackTopShiftUp: 0,
        StretchStackBottomShiftDown: 0,
        StretchStackGapAboveMin: 0,
        StretchStackGapBelowMin: 0,

        FractionNumeratorShiftUp: 300,
        FractionNumeratorDisplayStyleShiftUp: 400,
        FractionDenominatorShiftDown: 300,
        FractionDenominatorDisplayStyleShiftDown: 400,
        FractionNumeratorGapMin: 100,
        FractionNumeratorDisplayStyleGapMin: 150,
        FractionRuleThickness: 50,
        FractionDenominatorGapMin: 100,
        FractionDenominatorDisplayStyleGapMin: 150,

        SkewedFractionHorizontalGap: 0,
        SkewedFractionVerticalGap: 0,

        OverbarVerticalGap: 200,
        OverbarRuleThickness: 50,
        OverbarExtraAscender: 0,

        UnderbarVerticalGap: 200,
        UnderbarRuleThickness: 50,
        UnderbarExtraDescender: 0,

        RadicalVerticalGap: 150,
        RadicalDisplayStyleVerticalGap: 250,
        RadicalRuleThickness: 50,
        RadicalExtraAscender: 0,
        RadicalKernBeforeDegree: 0,
        RadicalKernAfterDegree: 0,

        RadicalDegreeBottomRaisePercent: 60
    );
}
