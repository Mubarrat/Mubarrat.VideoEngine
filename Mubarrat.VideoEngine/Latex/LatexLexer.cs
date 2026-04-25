using System.Collections;

namespace Mubarrat.VideoEngine.Latex;

public sealed class LatexLexer(string source)
{
    private readonly string source = source ?? string.Empty;

    public IReadOnlyList<LatexToken> Lex()
    {
        List<LatexToken> tokens = new(source.Length / 2 + 1);
        int i = 0;

        while (i < source.Length)
        {
            char c = source[i];

            if (c == '%')
            {
                i = SkipComment(i + 1);
                continue;
            }

            switch (c)
            {
                case '$':
                    if (i + 1 < source.Length && source[i + 1] == '$')
                    {
                        tokens.Add(new LatexToken(LatexTokenType.DoubleDollar, "$$"));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new LatexToken(LatexTokenType.Dollar, "$"));
                        i++;
                    }
                    continue;
                case '{':
                    tokens.Add(new LatexToken(LatexTokenType.LeftBrace, "{"));
                    i++;
                    continue;
                case '}':
                    tokens.Add(new LatexToken(LatexTokenType.RightBrace, "}"));
                    i++;
                    continue;
                case '[':
                    tokens.Add(new LatexToken(LatexTokenType.LeftBracket, "["));
                    i++;
                    continue;
                case ']':
                    tokens.Add(new LatexToken(LatexTokenType.RightBracket, "]"));
                    i++;
                    continue;
                case '^':
                    tokens.Add(new LatexToken(LatexTokenType.Caret, "^"));
                    i++;
                    continue;
                case '_':
                    tokens.Add(new LatexToken(LatexTokenType.Underscore, "_"));
                    i++;
                    continue;
                case '&':
                    tokens.Add(new LatexToken(LatexTokenType.Ampersand, "&"));
                    i++;
                    continue;
                case '\\':
                    i = ReadCommand(i, tokens);
                    continue;
                case var _ when char.IsWhiteSpace(c):
                    i++;
                    continue;
            }
            tokens.Add(new LatexToken(LatexTokenType.Char, c.ToString()));
            i++;
        }

        tokens.Add(new LatexToken(LatexTokenType.EndOfFile, string.Empty));
        return tokens;
    }

    private int SkipComment(int start)
    {
        int i = start;
        while (i < source.Length && source[i] != '\n' && source[i] != '\r')
            i++;
        return i;
    }

    private int ReadCommand(int start, ICollection<LatexToken> tokens)
    {
        int i = start + 1;
        if (i >= source.Length)
        {
            tokens.Add(new LatexToken(LatexTokenType.Char, "\\"));
            return i;
        }

        if (char.IsLetter(source[i]))
        {
            int from = i;
            while (i < source.Length && char.IsLetter(source[i]))
                i++;

            tokens.Add(new LatexToken(LatexTokenType.Command, source[from..i]));
            return i;
        }

        tokens.Add(new LatexToken(LatexTokenType.Command, source[i].ToString()));
        return i + 1;
    }
}

public enum LatexTokenType
{
    Char,
    Command,
    Dollar,
    DoubleDollar,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,
    Caret,
    Underscore,
    Ampersand,
    EndOfFile
}

public readonly record struct LatexToken(LatexTokenType Type, string Value);
