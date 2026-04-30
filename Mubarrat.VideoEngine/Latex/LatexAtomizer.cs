namespace Mubarrat.VideoEngine.Latex;

public sealed class LatexAtomizer
{
    public static MathAtom Atomize(string latex) => ToMathAtom(LetexParser.Parse(latex ?? string.Empty).Body);

    private static MathAtom ToMathAtom(LatexNode node)
    {
        return node switch
        {
            LatexSequenceNode sequence => BuildHorizontal(sequence.Items.Select(ToMathAtom)),
            LatexGroupNode group => ToMathAtom(group.Body),
            LatexMathNode math => ToMathAtom(math.Body),
            LatexTabularEnvironmentNode table => BuildMatrixAtom(table),
            LatexEnvironmentNode environment => ToMathAtom(environment.Body),
            LatexTextNode text => BuildTextAtoms(text.Text),
            LatexSuperscriptNode sup => MergeScripts(ToMathAtom(sup.Base), superscript: ToMathAtom(sup.Exponent), subscript: null),
            LatexSubscriptNode sub => MergeScripts(ToMathAtom(sub.Base), superscript: null, subscript: ToMathAtom(sub.Subscript)),
            LatexLeftRightNode leftRight => new DelimitedMathAtom(
                ToMathAtom(leftRight.Body),
                MapDelimiterTokenToText(leftRight.LeftDelimiter),
                MapDelimiterTokenToText(leftRight.RightDelimiter)),
            LatexCommandNode command => BuildCommandAtom(command),
            _ => new SymbolMathAtom(string.Empty)
        };
    }

    private static MatrixMathAtom BuildMatrixAtom(LatexTabularEnvironmentNode table)
    {
        List<IReadOnlyList<MathAtom>> rows = [];
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var sourceRow = table.Rows[r];
            List<MathAtom> cells = [];
            for (int c = 0; c < sourceRow.Cells.Count; c++)
                cells.Add(ToMathAtom(sourceRow.Cells[c].Content));

            rows.Add(cells);
        }

        if (rows.Count == 0)
            rows.Add([new SymbolMathAtom(string.Empty)]);

        (string left, string right) = table.Name switch
        {
            "pmatrix" or "pmatrix*" => ("(", ")"),
            "bmatrix" or "bmatrix*" => ("[", "]"),
            "Bmatrix" or "Bmatrix*" => ("{", "}"),
            "vmatrix" or "vmatrix*" => ("|", "|"),
            "Vmatrix" or "Vmatrix*" => ("‖", "‖"),
            "cases" => ("{", string.Empty),
            _ => (string.Empty, string.Empty)
        };

        return new(rows, left, right);
    }

    private static string MapDelimiterTokenToText(string token)
    {
        if (string.IsNullOrEmpty(token) || token == ".")
            return string.Empty;

        if (token.Length == 1)
            return token;

        return token[0] == '\\'
            ? MapCommandToText(token[1..])
            : token;
    }

    private static MathAtom BuildHorizontal(IEnumerable<MathAtom> atoms)
    {
        List<MathAtom> flattened = [];

        foreach (MathAtom atom in atoms)
        {
            if (atom is HorizontalMathAtom horizontal)
                flattened.AddRange(horizontal.Children);
            else
                flattened.Add(atom);
        }

        if (flattened.Count == 0)
            return new SymbolMathAtom(string.Empty);

        if (flattened.Count == 1)
            return flattened[0];

        return new HorizontalMathAtom(flattened);
    }

    private static MathAtom BuildTextAtoms(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new SymbolMathAtom(string.Empty);

        if (text.Length == 1)
        {
            MathAtomType type = ClassifyAtom(text[0]);
            return new SymbolMathAtom(NormalizeMathSymbolText(text, type)) { Type = type };
        }

        return new HorizontalMathAtom(Array.ConvertAll(text.ToCharArray(), c =>
        {
            MathAtomType type = ClassifyAtom(c);
            return new SymbolMathAtom(NormalizeMathSymbolText(c.ToString(), type)) { Type = type };
        }));
    }

    private static MathAtom BuildCommandAtom(LatexCommandNode command)
    {
        if (string.Equals(command.Name, "n", StringComparison.Ordinal) && command.RequiredArguments.Count >= 1)
        {
            MathAtom mathAtom = ToMathAtom(command.RequiredArguments[0].Body);
            if (mathAtom.Name is null)
                mathAtom.Name = LetexParser.FlattenText((command.OptionalArgument ?? command.RequiredArguments[0]).Body);
            else
                mathAtom = new HorizontalMathAtom([mathAtom]) { Name = LetexParser.FlattenText((command.OptionalArgument ?? command.RequiredArguments[0]).Body) };
            return mathAtom;
        }

        if (string.Equals(command.Name, "sqrt", StringComparison.Ordinal) && command.RequiredArguments.Count >= 1)
        {
            MathAtom radicand = ToMathAtom(command.RequiredArguments[0].Body);
            MathAtom? degree = command.OptionalArgument is null ? null : ToMathAtom(command.OptionalArgument.Body);
            return new RadicalMathAtom(radicand, degree);
        }

        if (string.Equals(command.Name, "frac", StringComparison.Ordinal) && command.RequiredArguments.Count >= 2)
        {
            MathAtom numerator = ToMathAtom(command.RequiredArguments[0].Body);
            MathAtom denominator = ToMathAtom(command.RequiredArguments[1].Body);
            return new FractionMathAtom(numerator, denominator);
        }

        if ((string.Equals(command.Name, "binom", StringComparison.Ordinal)
            || string.Equals(command.Name, "dbinom", StringComparison.Ordinal)
            || string.Equals(command.Name, "tbinom", StringComparison.Ordinal))
            && command.RequiredArguments.Count >= 2)
        {
            MathAtom numerator = ToMathAtom(command.RequiredArguments[0].Body);
            MathAtom denominator = ToMathAtom(command.RequiredArguments[1].Body);
            return new StackMathAtom(numerator, denominator);
        }

        if (string.Equals(command.Name, "overset", StringComparison.Ordinal) && command.RequiredArguments.Count >= 2)
        {
            MathAtom over = ToMathAtom(command.RequiredArguments[0].Body);
            MathAtom basis = ToMathAtom(command.RequiredArguments[1].Body);
            return new OverUnderMathAtom(basis, Under: null, Over: over);
        }

        if (string.Equals(command.Name, "underset", StringComparison.Ordinal) && command.RequiredArguments.Count >= 2)
        {
            MathAtom under = ToMathAtom(command.RequiredArguments[0].Body);
            MathAtom basis = ToMathAtom(command.RequiredArguments[1].Body);
            return new OverUnderMathAtom(basis, Under: under, Over: null);
        }

        if (command.RequiredArguments.Count >= 1)
        {
            if (string.Equals(command.Name, "substack", StringComparison.Ordinal))
                return ToMathAtom(command.RequiredArguments[0].Body);

            if (command.Name is "overleftarrow" or "overrightarrow" or "overleftrightarrow")
            {
                MathAtom basis = ToMathAtom(command.RequiredArguments[0].Body);
                return new StretchOverUnderMathAtom(basis, MapCommandToText(command.Name), IsOver: true);
            }

            if (command.Name is "underleftarrow" or "underrightarrow" or "underleftrightarrow")
            {
                MathAtom basis = ToMathAtom(command.RequiredArguments[0].Body);
                return new StretchOverUnderMathAtom(basis, MapCommandToText(command.Name), IsOver: false);
            }
        }

        string commandText = MapCommandToText(command.Name);
        MathAtomType commandType = ResolveOperatorAtomType(commandText);
        List<MathAtom> atoms = [new SymbolMathAtom(NormalizeMathSymbolText(commandText, commandType)) { Type = commandType }];

        foreach (LatexGroupNode argument in command.RequiredArguments)
            atoms.Add(ToMathAtom(argument.Body));

        return BuildHorizontal(atoms);
    }

    private static ScriptsMathAtom MergeScripts(MathAtom basis, MathAtom? superscript, MathAtom? subscript)
    {
        return basis is ScriptsMathAtom existing
            ? new(existing.Base, subscript ?? existing.Subscript, superscript ?? existing.Superscript)
            : new(basis, subscript, superscript);
    }

    private static MathAtomType ClassifyAtom(char c)
    {
        if (IsOpeningDelimiter(c))
            return MathAtomType.Opening;

        if (IsClosingDelimiter(c))
            return MathAtomType.Closing;

        if (IsPunctuation(c))
            return MathAtomType.Punctuation;

        if (IsRelationSymbol(c))
            return MathAtomType.Relation;

        if (IsOperatorSymbol(c))
            return MathAtomType.BinaryOperator;

        return MathAtomType.Ordinary;
    }

    private static readonly (char Start, char End)[] OpeningDelimiterRanges =
    [
        ('(', '('),
        ('[', '['),
        ('{', '{'),
        ('\u2308', '\u230A'), // ⌈ ⌉ ⌊ ⌋ (opening included)
        ('\u27E6', '\u27EF'), // mathematical brackets and white tortoise shell brackets
        ('\u2983', '\u2997')  // miscellaneous opening fence characters
    ];

    private static readonly (char Start, char End)[] ClosingDelimiterRanges =
    [
        (')', ')'),
        (']', ']'),
        ('}', '}'),
        ('\u2309', '\u230B'), // ⌉ ⌋ and related
        ('\u27E7', '\u27EF'),
        ('\u2984', '\u2998')
    ];

    private static readonly (char Start, char End)[] RelationRanges =
    [
        ('\u2190', '\u21FF'), // arrows
        ('\u27F0', '\u27FF'), // supplemental arrows-a
        ('\u2900', '\u297F'), // supplemental arrows-b
        ('\u2A6D', '\u2AFF')  // mostly relation operators in supplemental mathematical operators
    ];

    private static readonly (char Start, char End)[] OperatorRanges =
    [
        ('\u2200', '\u22EF'), // mathematical operators (excluding mostly-relation tail)
        ('\u27C0', '\u27EF'), // miscellaneous mathematical symbols-a
        ('\u2980', '\u29FF'), // miscellaneous mathematical symbols-b + delimiters/technical symbols
        ('\u2A00', '\u2A6C')  // supplemental mathematical operators (operator-dense section)
    ];

    private static bool IsOpeningDelimiter(char c)
        => IsInAnyRange(c, OpeningDelimiterRanges);

    private static bool IsClosingDelimiter(char c)
        => IsInAnyRange(c, ClosingDelimiterRanges);

    private static bool IsPunctuation(char c)
        => c is ',' or ';' or ':' or '،' or '؛';

    private static bool IsLargeOperatorSymbol(char c)
        => c is '∑' or '∏' or '∐' or '∫' or '∬' or '∭' or '∮' or '∯' or '∰' or '∱' or '∲' or '∳';

    private static bool IsAccentSymbol(char c)
        => c is 'ˆ' or 'ˇ' or 'ˉ' or '˜' or '¯' or '˘' or '˙' or '¨' or '˚' or '˝' or '˛' or '`' or '´';

    private static bool IsRelationSymbol(char c) =>
        c is '=' or '<' or '>' or '≤' or '≥' or '≠' or '≈' or '≅' or '∼' or '≃' or '≡'
            || IsInAnyRange(c, RelationRanges);

    private static bool IsOperatorSymbol(char c) =>
        c is '+' or '-' or '*' or '/' or '×' or '÷' or '·' or '±' or '∓' or '∪' or '∩' or '⊕' or '⊗'
            || IsInAnyRange(c, OperatorRanges);

    private static MathAtomType ResolveOperatorAtomType(string text)
    {
        if (!string.IsNullOrEmpty(text) && text.Length == 1)
        {
            char c = text[0];
            if (IsLargeOperatorSymbol(c))
                return MathAtomType.LargeOperator;
            if (IsAccentSymbol(c))
                return MathAtomType.Accent;
            if (IsRelationSymbol(c))
                return MathAtomType.Relation;
            if (IsOperatorSymbol(c))
                return MathAtomType.BinaryOperator;

            return ClassifyAtom(c);
        }

        return MathAtomType.Operator;
    }

    private static string NormalizeMathSymbolText(string text, MathAtomType type)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (text == "-")
            return "−";

        if (type == MathAtomType.Ordinary && text.Length == 1)
        {
            string? italic = TryConvertLatinToMathItalic(text[0]);
            if (italic is not null)
                return italic;
        }

        return text;
    }

    private static string? TryConvertLatinToMathItalic(char c)
    {
        if (c is >= 'A' and <= 'Z')
            return char.ConvertFromUtf32(0x1D434 + (c - 'A'));

        if (c is >= 'a' and <= 'z')
        {
            if (c == 'h')
                return "ℎ";

            return char.ConvertFromUtf32(0x1D44E + (c - 'a'));
        }

        return null;
    }

    private static bool IsInAnyRange(char value, ReadOnlySpan<(char Start, char End)> ranges)
    {
        for (int i = 0; i < ranges.Length; i++)
        {
            var (start, end) = ranges[i];
            if (value >= start && value <= end)
                return true;
        }

        return false;
    }

    private static string MapCommandToText(string command) => command switch
    {
        // Trig
        "sin" => "sin",
        "cos" => "cos",
        "tan" => "tan",
        "cot" => "cot",
        "sec" => "sec",
        "csc" => "csc",

        // Hyperbolic
        "sinh" => "sinh",
        "cosh" => "cosh",
        "tanh" => "tanh",

        // Log / calculus
        "log" => "log",
        "ln" => "ln",
        "exp" => "exp",
        "lim" => "lim",
        "limsup" => "limsup",
        "liminf" => "liminf",
        "sup" => "sup",
        "inf" => "inf",
        "max" => "max",
        "min" => "min",

        // Algebra / linear algebra
        "det" => "det",
        "dim" => "dim",
        "ker" => "ker",
        "hom" => "hom",
        "rank" => "rank",
        "trace" => "tr",
        "arg" => "arg",

        // Probability / number theory
        "Pr" => "Pr",
        "gcd" => "gcd",
        "lcm" => "lcm",

        // Operators
        "cdot" => "·",
        "times" => "×",
        "div" => "÷",
        "pm" => "±",
        "mp" => "∓",

        // Relations
        "le" or "leq" => "≤",
        "ge" or "geq" => "≥",
        "neq" => "≠",
        "approx" => "≈",
        "equiv" => "≡",
        "propto" => "∝",
        "in" => "∈",
        "notin" => "∉",
        "subset" => "⊂",
        "supset" => "⊃",
        "subseteq" => "⊆",
        "supseteq" => "⊇",

        // Infinity
        "infty" => "∞",

        // Greek letters (lowercase)
        "alpha" => "α",
        "beta" => "β",
        "gamma" => "γ",
        "delta" => "δ",
        "epsilon" => "ε",
        "theta" => "θ",
        "lambda" => "λ",
        "mu" => "μ",
        "pi" => "π",
        "sigma" => "σ",
        "phi" => "φ",
        "omega" => "ω",

        // Big operators
        "sum" => "∑",
        "prod" => "∏",
        "int" => "∫",
        "iint" => "∬",
        "iiint" => "∭",
        "oint" => "∮",

        // Arrows
        "leftarrow" => "←",
        "rightarrow" => "→",
        "leftrightarrow" => "↔",
        "Leftarrow" => "⇐",
        "Rightarrow" => "⇒",
        "Leftrightarrow" => "⇔",
        "uparrow" => "↑",
        "downarrow" => "↓",

        "overleftarrow" => "←",
        "overrightarrow" => "→",
        "overleftrightarrow" => "↔",
        "underleftarrow" => "←",
        "underrightarrow" => "→",
        "underleftrightarrow" => "↔",

        // Logic
        "land" => "∧",
        "lor" => "∨",
        "neg" => "¬",
        "forall" => "∀",
        "exists" => "∃",

        _ => "\\" + command
    };
}
