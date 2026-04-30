namespace Mubarrat.VideoEngine.Latex;

public sealed class LetexParser(IReadOnlyList<LatexToken> tokens)
{
    private readonly IReadOnlyList<LatexToken> tokens = tokens ?? [];
    private int position;

    public static LatexDocument Parse(string source)
        => new LetexParser(new LatexLexer(source).Lex()).ParseDocument();

    public LatexDocument ParseDocument()
    {
        position = 0;
        var body = ParseSequence(stopAtRightBrace: false, stopAtRightBracket: false, stopOnDollar: false, stopOnDoubleDollar: false, expectedEnvironmentName: null, stopOnCommandName: null);
        return new LatexDocument(body);
    }

    private LatexSequenceNode ParseSequence(bool stopAtRightBrace, bool stopAtRightBracket, bool stopOnDollar, bool stopOnDoubleDollar, string? expectedEnvironmentName, string? stopOnCommandName)
    {
        List<LatexNode> nodes = [];

        while (!IsAtEnd())
        {
            var current = Current;

            if (stopOnDoubleDollar && current.Type == LatexTokenType.DoubleDollar)
                break;
            if (stopOnDollar && current.Type == LatexTokenType.Dollar)
                break;
            if (stopAtRightBrace && current.Type == LatexTokenType.RightBrace)
                break;
            if (stopAtRightBracket && current.Type == LatexTokenType.RightBracket)
                break;
            if (expectedEnvironmentName is not null && IsMatchingEndEnvironment(expectedEnvironmentName))
                break;
            if (stopOnCommandName is not null && current.Type == LatexTokenType.Command && string.Equals(current.Value, stopOnCommandName, StringComparison.Ordinal))
                break;

            LatexNode node = ParseAtom();
            node = ParseScripts(node);
            nodes.Add(node);
        }

        return new LatexSequenceNode(nodes);
    }

    private LatexNode ParseAtom()
    {
        var token = Advance();

        return token.Type switch
        {
            LatexTokenType.Char => new LatexTextNode(token.Value),
            LatexTokenType.Command => ParseCommand(token.Value),
            LatexTokenType.Dollar => ParseMath(false),
            LatexTokenType.DoubleDollar => ParseMath(true),
            LatexTokenType.LeftBrace => ParseGroup(),
            LatexTokenType.LeftBracket => ParseOptionalGroup(),
            LatexTokenType.RightBrace or LatexTokenType.RightBracket => new LatexTextNode(token.Value),
            LatexTokenType.Caret => new LatexTextNode("^"),
            LatexTokenType.Underscore => new LatexTextNode("_"),
            LatexTokenType.Ampersand => new LatexCommandNode("&", null, []),
            _ => new LatexTextNode(string.Empty)
        };
    }

    private LatexNode ParseScripts(LatexNode @base)
    {
        while (!IsAtEnd())
        {
            if (Match(LatexTokenType.Caret))
            {
                var exponent = ParseScriptArgument();
                @base = new LatexSuperscriptNode(@base, exponent);
                continue;
            }

            if (Match(LatexTokenType.Underscore))
            {
                var subscript = ParseScriptArgument();
                @base = new LatexSubscriptNode(@base, subscript);
                continue;
            }

            break;
        }

        return @base;
    }

    private LatexNode ParseScriptArgument()
    {
        if (Match(LatexTokenType.LeftBrace))
            return ParseGroupBodyAndConsumeRightBrace();

        if (IsAtEnd())
            return new LatexTextNode(string.Empty);

        return ParseAtom();
    }

    private LatexMathNode ParseMath(bool isDisplay)
    {
        var body = ParseSequence(
            stopAtRightBrace: false,
            stopAtRightBracket: false,
            stopOnDollar: !isDisplay,
            stopOnDoubleDollar: isDisplay,
            expectedEnvironmentName: null,
            stopOnCommandName: null);

        if (isDisplay)
            Match(LatexTokenType.DoubleDollar);
        else
            Match(LatexTokenType.Dollar);

        return new LatexMathNode(body, IsDisplay: isDisplay);
    }

    private LatexGroupNode ParseGroup()
        => new(ParseGroupBodyAndConsumeRightBrace(), IsOptional: false);

    private LatexGroupNode ParseOptionalGroup()
    {
        var body = ParseSequence(stopAtRightBrace: false, stopAtRightBracket: true, stopOnDollar: false, stopOnDoubleDollar: false, expectedEnvironmentName: null, stopOnCommandName: null);
        if (Match(LatexTokenType.RightBracket)) { }
        return new LatexGroupNode(body, IsOptional: true);
    }

    private LatexSequenceNode ParseGroupBodyAndConsumeRightBrace()
    {
        var body = ParseSequence(stopAtRightBrace: true, stopAtRightBracket: false, stopOnDollar: false, stopOnDoubleDollar: false, expectedEnvironmentName: null, stopOnCommandName: null);
        if (Match(LatexTokenType.RightBrace)) { }
        return body;
    }

    private LatexCommandNode ParseCommand(string name)
    {
        if (string.Equals(name, "(", StringComparison.Ordinal))
            return ParseMathDelimitedCommand(isDisplay: false, closingCommand: ")");

        if (string.Equals(name, "[", StringComparison.Ordinal))
            return ParseMathDelimitedCommand(isDisplay: true, closingCommand: "]");

        if (string.Equals(name, "left", StringComparison.Ordinal))
            return ParseLeftRightCommand();

        if (string.Equals(name, "begin", StringComparison.Ordinal))
            return ParseBeginEnvironmentAsCommand(name);

        LatexGroupNode? optionalArgument = null;
        List<LatexGroupNode> requiredArguments = [];

        while (true)
        {
            if (optionalArgument is null && Match(LatexTokenType.LeftBracket))
            {
                optionalArgument = ParseOptionalGroup();
                continue;
            }

            if (Match(LatexTokenType.LeftBrace))
            {
                requiredArguments.Add(new LatexGroupNode(ParseGroupBodyAndConsumeRightBrace(), IsOptional: false));
                continue;
            }

            break;
        }

        LatexPackage package = LatexAmsCatalog.ResolveCommandPackage(name);
        return package == LatexPackage.Unknown
            ? new LatexCommandNode(name, optionalArgument, requiredArguments)
            : new LatexKnownCommandNode(name, optionalArgument, requiredArguments, package);
    }

    private LatexCommandNode ParseMathDelimitedCommand(bool isDisplay, string closingCommand)
    {
        var body = ParseSequence(
            stopAtRightBrace: false,
            stopAtRightBracket: false,
            stopOnDollar: false,
            stopOnDoubleDollar: false,
            expectedEnvironmentName: null,
            stopOnCommandName: closingCommand);

        MatchCommand(closingCommand);
        return new LatexMathDelimitedCommandNode(body, isDisplay, OpeningCommand: isDisplay ? "[" : "(", ClosingCommand: closingCommand);
    }

    private LatexCommandNode ParseLeftRightCommand()
    {
        string leftDelimiter = ParseDelimiterToken();

        var body = ParseSequence(
            stopAtRightBrace: false,
            stopAtRightBracket: false,
            stopOnDollar: false,
            stopOnDoubleDollar: false,
            expectedEnvironmentName: null,
            stopOnCommandName: "right");

        if (!MatchCommand("right"))
            throw new FormatException("Expected \\right for \\left expression.");

        string rightDelimiter = ParseDelimiterToken();
        return new LatexLeftRightNode(leftDelimiter, rightDelimiter, body);
    }

    private string ParseDelimiterToken()
    {
        if (IsAtEnd())
            return string.Empty;

        var token = Advance();
        return token.Type switch
        {
            LatexTokenType.Command => "\\" + token.Value,
            LatexTokenType.LeftBrace => "{",
            LatexTokenType.RightBrace => "}",
            LatexTokenType.LeftBracket => "[",
            LatexTokenType.RightBracket => "]",
            LatexTokenType.Caret => "^",
            LatexTokenType.Underscore => "_",
            LatexTokenType.Ampersand => "&",
            LatexTokenType.Dollar => "$",
            LatexTokenType.DoubleDollar => "$$",
            _ => token.Value
        };
    }

    private LatexCommandNode ParseBeginEnvironmentAsCommand(string beginName)
    {
        if (!Match(LatexTokenType.LeftBrace))
            return new LatexCommandNode(beginName, null, []);

        LatexGroupNode nameGroup = new(ParseGroupBodyAndConsumeRightBrace(), IsOptional: false);
        string environmentName = FlattenText(nameGroup.Body).Trim();

        LatexGroupNode? optionalArgument = null;
        List<LatexGroupNode> requiredArguments = [nameGroup];

        if (Match(LatexTokenType.LeftBracket))
            optionalArgument = ParseOptionalGroup();

        while (Match(LatexTokenType.LeftBrace))
            requiredArguments.Add(new LatexGroupNode(ParseGroupBodyAndConsumeRightBrace(), IsOptional: false));

        var body = ParseSequence(
            stopAtRightBrace: false,
            stopAtRightBracket: false,
            stopOnDollar: false,
            stopOnDoubleDollar: false,
            expectedEnvironmentName: environmentName,
            stopOnCommandName: null);

        if (!TryConsumeEndEnvironment(environmentName))
            throw new FormatException($"Unterminated environment: {environmentName}");

        return CreateEnvironmentNode(environmentName, optionalArgument, requiredArguments, body);
    }

    private static LatexCommandNode CreateEnvironmentNode(string name, LatexGroupNode? optionalArgument, IReadOnlyList<LatexGroupNode> arguments, LatexSequenceNode body)
    {
        LatexPackage package = LatexAmsCatalog.ResolveEnvironmentPackage(name);

        return name switch
        {
            _ when LatexAmsCatalog.IsTabularLikeEnvironment(name)
                => package == LatexPackage.Unknown
                    ? new LatexTabularEnvironmentNode(name, optionalArgument, arguments, body, SplitRowsAndCells(body))
                    : new LatexKnownTabularEnvironmentNode(name, optionalArgument, arguments, body, SplitRowsAndCells(body), package),
            _ => package == LatexPackage.Unknown
                ? new LatexEnvironmentNode(name, optionalArgument, arguments, body)
                : new LatexKnownEnvironmentNode(name, optionalArgument, arguments, body, package)
        };
    }

    private static IReadOnlyList<LatexRowNode> SplitRowsAndCells(LatexSequenceNode body)
    {
        List<LatexRowNode> rows = [];
        List<LatexCellNode> currentRow = [];
        List<LatexNode> currentCell = [];

        static bool IsCommand(LatexNode node, string command)
            => node is LatexCommandNode c && string.Equals(c.Name, command, StringComparison.Ordinal);

        void PushCell()
        {
            currentRow.Add(new LatexCellNode(new LatexSequenceNode([.. currentCell])));
            currentCell.Clear();
        }

        void PushRow()
        {
            PushCell();
            rows.Add(new LatexRowNode([.. currentRow]));
            currentRow.Clear();
        }

        for (int i = 0; i < body.Items.Count; i++)
        {
            var node = body.Items[i];

            if (IsCommand(node, "\\"))
            {
                PushRow();
                continue;
            }

            if (IsCommand(node, "&"))
            {
                PushCell();
                continue;
            }

            currentCell.Add(node);
        }

        if (currentCell.Count > 0 || currentRow.Count > 0 || rows.Count == 0)
            PushRow();

        return rows;
    }

    private bool IsMatchingEndEnvironment(string expectedEnvironmentName)
    {
        if (Current.Type != LatexTokenType.Command || !string.Equals(Current.Value, "end", StringComparison.Ordinal))
            return false;

        if (position + 1 >= tokens.Count || tokens[position + 1].Type != LatexTokenType.LeftBrace)
            return false;

        if (!TryReadEnvironmentName(position + 2, out var endName, out _))
            return false;

        return string.Equals(endName, expectedEnvironmentName, StringComparison.Ordinal);
    }

    private bool MatchCommand(string command)
    {
        if (Current.Type != LatexTokenType.Command || !string.Equals(Current.Value, command, StringComparison.Ordinal))
            return false;

        position++;
        return true;
    }

    private bool TryConsumeEndEnvironment(string expectedEnvironmentName)
    {
        if (!IsMatchingEndEnvironment(expectedEnvironmentName))
            return false;

        position++; // end
        position++; // {
        _ = TryReadEnvironmentName(position, out _, out int consumed);
        position += consumed;
        if (Current.Type == LatexTokenType.RightBrace)
            position++;

        return true;
    }

    private bool TryReadEnvironmentName(int startIndex, out string name, out int consumed)
    {
        consumed = 0;
        List<string> chunks = [];
        int i = startIndex;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (token.Type == LatexTokenType.RightBrace)
                break;

            if (token.Type is LatexTokenType.Char or LatexTokenType.Command)
                chunks.Add(token.Value);
            else
                return Fail(out name, out consumed);

            i++;
            consumed++;
        }

        if (i >= tokens.Count || tokens[i].Type != LatexTokenType.RightBrace)
            return Fail(out name, out consumed);

        name = string.Concat(chunks);
        return true;
    }

    private static bool Fail(out string name, out int consumed)
    {
        name = string.Empty;
        consumed = 0;
        return false;
    }

    internal static string FlattenText(LatexSequenceNode sequence)
    {
        if (sequence.Items.Count == 0)
            return string.Empty;

        return string.Concat(sequence.Items.Select(GetNodeText));
    }

    internal static string GetNodeText(LatexNode node)
    {
        return node switch
        {
            LatexTextNode text => text.Text,
            LatexEnvironmentNode environment => environment.Name,
            LatexCommandNode command => command.Name,
            LatexGroupNode group => FlattenText(group.Body),
            LatexMathNode math => FlattenText(math.Body),
            LatexSuperscriptNode sup => GetNodeText(sup.Base) + GetNodeText(sup.Exponent),
            LatexSubscriptNode sub => GetNodeText(sub.Base) + GetNodeText(sub.Subscript),
            _ => string.Empty
        };
    }

    private bool Match(LatexTokenType type)
    {
        if (Current.Type != type)
            return false;

        position++;
        return true;
    }

    private LatexToken Advance()
    {
        var token = Current;
        if (!IsAtEnd())
            position++;
        return token;
    }

    private bool IsAtEnd() => Current.Type == LatexTokenType.EndOfFile;

    private LatexToken Current => position < tokens.Count ? tokens[position] : new LatexToken(LatexTokenType.EndOfFile, string.Empty);
}

public sealed record LatexDocument(LatexSequenceNode Body);

public abstract record LatexNode;

public sealed record LatexSequenceNode(IReadOnlyList<LatexNode> Items) : LatexNode;

public sealed record LatexTextNode(string Text) : LatexNode;

public sealed record LatexGroupNode(LatexSequenceNode Body, bool IsOptional) : LatexNode;

public record LatexCommandNode(string Name, LatexGroupNode? OptionalArgument, IReadOnlyList<LatexGroupNode> RequiredArguments) : LatexNode;

public sealed record LatexKnownCommandNode(string Name, LatexGroupNode? OptionalArgument, IReadOnlyList<LatexGroupNode> RequiredArguments, LatexPackage Package) : LatexCommandNode(Name, OptionalArgument, RequiredArguments);

public record LatexEnvironmentNode(string Name, LatexGroupNode? OptionalArgument, IReadOnlyList<LatexGroupNode> Arguments, LatexSequenceNode Body) : LatexCommandNode("begin", OptionalArgument, Arguments);

public sealed record LatexKnownEnvironmentNode(string Name, LatexGroupNode? OptionalArgument, IReadOnlyList<LatexGroupNode> Arguments, LatexSequenceNode Body, LatexPackage Package)
    : LatexEnvironmentNode(Name, OptionalArgument, Arguments, Body);

public record LatexTabularEnvironmentNode(string Name, LatexGroupNode? OptionalArgument, IReadOnlyList<LatexGroupNode> Arguments, LatexSequenceNode Body, IReadOnlyList<LatexRowNode> Rows) : LatexEnvironmentNode(Name, OptionalArgument, Arguments, Body);

public sealed record LatexKnownTabularEnvironmentNode(string Name, LatexGroupNode? OptionalArgument, IReadOnlyList<LatexGroupNode> Arguments, LatexSequenceNode Body, IReadOnlyList<LatexRowNode> Rows, LatexPackage Package)
    : LatexTabularEnvironmentNode(Name, OptionalArgument, Arguments, Body, Rows);

public sealed record LatexRowNode(IReadOnlyList<LatexCellNode> Cells);

public sealed record LatexCellNode(LatexSequenceNode Content);

public sealed record LatexMathNode(LatexSequenceNode Body, bool IsDisplay) : LatexNode;

public sealed record LatexMathDelimitedCommandNode(LatexSequenceNode Body, bool IsDisplay, string OpeningCommand, string ClosingCommand) : LatexCommandNode("math", null, []);

public sealed record LatexLeftRightNode(string LeftDelimiter, string RightDelimiter, LatexSequenceNode Body) : LatexCommandNode("left", null, []);

public sealed record LatexSuperscriptNode(LatexNode Base, LatexNode Exponent) : LatexNode;

public sealed record LatexSubscriptNode(LatexNode Base, LatexNode Subscript) : LatexNode;
