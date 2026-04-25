using Mubarrat.OpenType;

namespace Mubarrat.VideoEngine.Latex;

public static class MathSpacingEngine
{
    private static readonly MathSpacing[,] Table =
    {
        //Ord,    Op,     LargeOp, Bin,    Rel,   Accent, Open,  Close, Punct, Inner
        { None,   Thin,   Thin,    Medium, Thick, None,   None,  None,  Thin,  Thin  }, // Ord
        { Thin,   Thin,   Thin,    None,   Thick, None,   None,  None,  Thin,  Thin  }, // Op
        { Thin,   Thin,   Thin,    None,   Thick, None,   None,  None,  Thin,  Thin  }, // LargeOp
        { Medium, Medium, Medium,  None,   None,  None,   None,  None,  None,  None  }, // Bin
        { Thick,  Thick,  Thick,   Thick,  None,  None,   Thick, None,  Thick, Thick }, // Rel
        { None,   None,   None,    None,   None,  None,   None,  None,  None,  None  }, // Accent
        { None,   None,   None,    None,   None,  None,   None,  None,  None,  None  }, // Open
        { None,   Thin,   Thin,    Medium, Thick, None,   None,  None,  Thin,  Thin  }, // Close
        { Thin,   Thin,   Thin,    None,   Thin,  None,   Thin,  Thin,  Thin,  Thin  }, // Punct
        { Thin,   Thin,   Thin,    Medium, Thick, None,   Thin,  None,  Thin,  Thin  }, // Inner
    };

    public static MathSpacing GetSpacing(MathAtomType left, MathAtomType right, bool isStart = false)
    {
        left = NormalizeLeft(left, isStart);
        right = NormalizeRight(left, right);

        return Table[(int)left, (int)right];
    }

    private static MathAtomType NormalizeLeft(MathAtomType left, bool isStart) => isStart && left == MathAtomType.BinaryOperator ? MathAtomType.Ordinary : left;

    private static MathAtomType NormalizeRight(MathAtomType left, MathAtomType right) =>
        // TeX rule: binary operators degrade to ordinary in certain contexts
        right == MathAtomType.BinaryOperator
            ? left switch
            {
                MathAtomType.Opening or
                MathAtomType.Relation or
                MathAtomType.BinaryOperator or
                MathAtomType.Operator or
                MathAtomType.Punctuation
                    => MathAtomType.Ordinary,

                _ => right
            }
            : right;

    private const MathSpacing None = MathSpacing.None;
    private const MathSpacing Thin = MathSpacing.Thin;
    private const MathSpacing Medium = MathSpacing.Medium;
    private const MathSpacing Thick = MathSpacing.Thick;

    public static double GetAbsoluteSpacing(MathSpacing spacing, FontMetrics metrics)
    {
        double mu = metrics.FontSize / 18.0;
        return spacing switch
        {
            MathSpacing.None => 0,
            MathSpacing.Thin => 3 * mu,
            MathSpacing.Medium => 4 * mu,
            MathSpacing.Thick => 5 * mu,
            _ => 0
        };
    }
}
