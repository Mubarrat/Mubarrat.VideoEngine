using Mubarrat.OpenType;
using Mubarrat.OpenType.Tables;
using Mubarrat.OpenType.TextShaping;
using Mubarrat.VideoEngine.Immutable;
using System.Buffers.Binary;
using System.Numerics;
using System.Globalization;

namespace Mubarrat.VideoEngine.Path;

public static class ShapingResultPathExtensions
{
    /// <summary>
    /// Converts a shaped text run into a <see cref="PathBuilder"/> using the glyph outlines from the font.
    /// </summary>
    public static PathBuilder ToPathBuilder(this ShapingResult shapingResult, FontMetrics metrics, Point origin, bool flipY = true)
    {
        ArgumentNullException.ThrowIfNull(shapingResult);
        ArgumentNullException.ThrowIfNull(metrics);

        bool hasGlyf = metrics.Face.Tables.TryGet(out GlyfTable? glyf);
        bool hasCff = metrics.Face.Tables.TryGet(out CffTable? cff);

        if (!hasGlyf && !hasCff)
            throw new InvalidOperationException("The font does not contain supported outlines (glyf or CFF). ");

        var builder = new PathBuilder();
        double penX = origin.X;
        double penY = origin.Y;
        double ppem = metrics.FontSize;

        for (int i = 0; i < shapingResult.Glyphs.Count; i++)
        {
            var glyph = shapingResult.Glyphs[i];
            double glyphOriginX = penX + glyph.XOffset;
            double glyphOriginY = penY + glyph.YOffset;

            if (hasGlyf && glyph.GlyphId < glyf!.Glyphs.Length)
            {
                var contours = glyf.GetContours(glyph.GlyphId);

                double scale = metrics.Scale;

                for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
                {
                    var contour = contours[contourIndex].Points;
                    AppendContour(builder, contour, glyphOriginX, glyphOriginY, scale, flipY);
                }
            }
            else if (hasCff && glyph.GlyphId < cff!.CharStrings.Length)
            {
                AppendCffGlyph(builder, cff, glyph.GlyphId, glyphOriginX, glyphOriginY, metrics.Scale, flipY);
            }

            penX += glyph.XAdvance;
            penY += glyph.YAdvance;
        }

        return builder;
    }

    /// <summary>
    /// Converts a shaped text run into a <see cref="PathBuilder"/> using origin (0,0).
    /// </summary>
    public static PathBuilder ToPathBuilder(this ShapingResult shapingResult, FontMetrics metrics, bool flipY = true)
        => shapingResult.ToPathBuilder(metrics, Point.Zero, flipY);

    /// <summary>
    /// Converts a shaped text run to <see cref="Path2D"/> through <see cref="PathBuilder"/>.
    /// </summary>
    public static Path2D ToPath2D(this ShapingResult shapingResult, FontMetrics metrics, Point origin, bool isNonZeroFill = true, bool flipY = true)
        => shapingResult.ToPathBuilder(metrics, origin, flipY).Build(isNonZeroFill);

    /// <summary>
    /// Converts a shaped text run to <see cref="Path2D"/> through <see cref="PathBuilder"/> using origin (0,0).
    /// </summary>
    public static Path2D ToPath2D(this ShapingResult shapingResult, FontMetrics metrics, bool isNonZeroFill = true, bool flipY = true)
        => shapingResult.ToPathBuilder(metrics, Point.Zero, flipY).Build(isNonZeroFill);

    private static void AppendContour(
        PathBuilder builder,
        GlyfTable.GlyphOutlinePoint[] contour,
        double glyphOriginX,
        double glyphOriginY,
        double scale,
        bool flipY)
    {
        if (contour is null || contour.Length == 0)
            return;

        static Vector2 Midpoint(in Vector2 a, in Vector2 b) => (a + b) * 0.5f;

        Point Transform(in Vector2 position) => new(
            glyphOriginX + (position.X * scale),
            glyphOriginY + (position.Y * scale * (flipY ? -1 : 1)));

        static bool NearlyEqual(in Vector2 a, in Vector2 b)
            => MathF.Abs(a.X - b.X) <= 1e-6f && MathF.Abs(a.Y - b.Y) <= 1e-6f;

        int startIndex = FindStartIndex(contour);
        Vector2 startPoint = GetStartPoint(contour, startIndex);

        builder.MoveTo(Transform(startPoint));

        Vector2? pendingControl = null;
        Vector2 current = startPoint;

        for (int step = 1; step <= contour.Length; step++)
        {
            int index = (startIndex + step) % contour.Length;
            var point = contour[index];
            bool isClosingStep = step == contour.Length;

            if (point.OnCurve)
            {
                if (pendingControl is Vector2 control)
                {
                    builder.QuadraticTo(Transform(control), Transform(point.Position));
                    current = point.Position;
                    pendingControl = null;
                }
                else
                {
                    if (!(isClosingStep && NearlyEqual(point.Position, startPoint)))
                    {
                        builder.LineTo(Transform(point.Position));
                        current = point.Position;
                    }
                }

                continue;
            }

            if (pendingControl is Vector2 previousControl)
            {
                Vector2 implied = Midpoint(previousControl, point.Position);
                builder.QuadraticTo(Transform(previousControl), Transform(implied));
                current = implied;
            }

            pendingControl = point.Position;
        }

        if (pendingControl is Vector2 lastControl)
        {
            builder.QuadraticTo(Transform(lastControl), Transform(startPoint));
        }

        builder.Close();
    }

    private static int FindStartIndex(GlyfTable.GlyphOutlinePoint[] contour)
    {
        for (int i = 0; i < contour.Length; i++)
        {
            if (contour[i].OnCurve)
                return i;
        }

        return 0;
    }

    private static Vector2 GetStartPoint(GlyfTable.GlyphOutlinePoint[] contour, int startIndex)
    {
        var start = contour[startIndex];

        if (start.OnCurve)
            return start.Position;

        var prev = contour[(startIndex - 1 + contour.Length) % contour.Length];
        return (prev.Position + start.Position) * 0.5f;
    }

    private static void AppendCffGlyph(PathBuilder builder, CffTable cff, int glyphId, double glyphOriginX, double glyphOriginY, double scale, bool flipY)
    {
        var state = new Type2BuildState(builder, cff, glyphOriginX, glyphOriginY, scale, flipY);
        ExecuteType2CharString(cff.CharStrings[glyphId], state, depth: 0);
        state.EnsureClosed();
    }

    private static void ExecuteType2CharString(ReadOnlySpan<byte> program, Type2BuildState state, int depth)
    {
        if (depth > 32)
            throw new InvalidDataException("CFF subroutine recursion limit exceeded.");

        int ip = 0;
        while (ip < program.Length)
        {
            byte op = program[ip++];
            if (TryReadType2Number(program, ref ip, op, out double number))
            {
                state.Stack.Add(number);
                continue;
            }

            switch (op)
            {
                case 1:
                case 3:
                case 18:
                case 23:
                    state.ConsumeWidthForHints();
                    state.HintCount += state.Stack.Count / 2;
                    state.Stack.Clear();
                    break;

                case 19:
                case 20:
                    state.ConsumeWidthForHints();
                    state.HintCount += state.Stack.Count / 2;
                    state.Stack.Clear();
                    ip += (state.HintCount + 7) / 8;
                    if (ip > program.Length)
                        throw new EndOfStreamException();
                    break;

                case 21:
                    state.ConsumeWidthForMove(2);
                    RequireArgCount(state.Stack, 2);
                    state.MoveToRelative(state.Stack[0], state.Stack[1]);
                    state.Stack.Clear();
                    break;

                case 22:
                    state.ConsumeWidthForMove(1);
                    RequireArgCount(state.Stack, 1);
                    state.MoveToRelative(state.Stack[0], 0);
                    state.Stack.Clear();
                    break;

                case 4:
                    state.ConsumeWidthForMove(1);
                    RequireArgCount(state.Stack, 1);
                    state.MoveToRelative(0, state.Stack[0]);
                    state.Stack.Clear();
                    break;

                case 5:
                    if ((state.Stack.Count & 1) != 0)
                        throw new InvalidDataException("Invalid rlineto operands.");
                    for (int i = 0; i < state.Stack.Count; i += 2)
                        state.LineToRelative(state.Stack[i], state.Stack[i + 1]);
                    state.Stack.Clear();
                    break;

                case 6:
                {
                    bool horizontal = true;
                    for (int i = 0; i < state.Stack.Count; i++)
                    {
                        double d = state.Stack[i];
                        state.LineToRelative(horizontal ? d : 0, horizontal ? 0 : d);
                        horizontal = !horizontal;
                    }
                    state.Stack.Clear();
                    break;
                }

                case 7:
                {
                    bool vertical = true;
                    for (int i = 0; i < state.Stack.Count; i++)
                    {
                        double d = state.Stack[i];
                        state.LineToRelative(vertical ? 0 : d, vertical ? d : 0);
                        vertical = !vertical;
                    }
                    state.Stack.Clear();
                    break;
                }

                case 8:
                    if (state.Stack.Count % 6 != 0)
                        throw new InvalidDataException("Invalid rrcurveto operands.");
                    for (int i = 0; i < state.Stack.Count; i += 6)
                        state.CurveToRelative(state.Stack[i], state.Stack[i + 1], state.Stack[i + 2], state.Stack[i + 3], state.Stack[i + 4], state.Stack[i + 5]);
                    state.Stack.Clear();
                    break;

                case 24:
                    if (state.Stack.Count < 8 || ((state.Stack.Count - 2) % 6) != 0)
                        throw new InvalidDataException("Invalid rcurveline operands.");
                    for (int i = 0; i <= state.Stack.Count - 8; i += 6)
                        state.CurveToRelative(state.Stack[i], state.Stack[i + 1], state.Stack[i + 2], state.Stack[i + 3], state.Stack[i + 4], state.Stack[i + 5]);
                    state.LineToRelative(state.Stack[^2], state.Stack[^1]);
                    state.Stack.Clear();
                    break;

                case 25:
                    if (state.Stack.Count < 8 || ((state.Stack.Count - 2) % 6) != 0)
                        throw new InvalidDataException("Invalid rlinecurve operands.");
                    for (int i = 0; i <= state.Stack.Count - 8; i += 2)
                        state.LineToRelative(state.Stack[i], state.Stack[i + 1]);
                    state.CurveToRelative(state.Stack[^6], state.Stack[^5], state.Stack[^4], state.Stack[^3], state.Stack[^2], state.Stack[^1]);
                    state.Stack.Clear();
                    break;

                case 26:
                {
                    if (state.Stack.Count < 4)
                        throw new InvalidDataException("Invalid vvcurveto operands.");
                    int index = 0;
                    double dx1 = 0;
                    if ((state.Stack.Count & 1) == 1)
                    {
                        dx1 = state.Stack[index++];
                    }
                    while (index < state.Stack.Count)
                    {
                        state.CurveToRelative(dx1, state.Stack[index++], state.Stack[index++], state.Stack[index++], 0, state.Stack[index++]);
                        dx1 = 0;
                    }
                    state.Stack.Clear();
                    break;
                }

                case 27:
                {
                    if (state.Stack.Count < 4)
                        throw new InvalidDataException("Invalid hhcurveto operands.");
                    int index = 0;
                    double dy1 = 0;
                    if ((state.Stack.Count & 1) == 1)
                    {
                        dy1 = state.Stack[index++];
                    }
                    while (index < state.Stack.Count)
                    {
                        state.CurveToRelative(state.Stack[index++], dy1, state.Stack[index++], state.Stack[index++], state.Stack[index++], 0);
                        dy1 = 0;
                    }
                    state.Stack.Clear();
                    break;
                }

                case 30:
                case 31:
                {
                    bool isVh = op == 30;
                    int index = 0;
                    while (index + 3 < state.Stack.Count)
                    {
                        if (isVh)
                        {
                            double dy1 = state.Stack[index++];
                            double dx2 = state.Stack[index++];
                            double dy2 = state.Stack[index++];
                            double dx3 = state.Stack[index++];
                            double dy3 = (state.Stack.Count - index) == 1 ? state.Stack[index++] : 0;
                            state.CurveToRelative(0, dy1, dx2, dy2, dx3, dy3);
                        }
                        else
                        {
                            double dx1 = state.Stack[index++];
                            double dx2 = state.Stack[index++];
                            double dy2 = state.Stack[index++];
                            double dy3 = state.Stack[index++];
                            double dx3 = (state.Stack.Count - index) == 1 ? state.Stack[index++] : 0;
                            state.CurveToRelative(dx1, 0, dx2, dy2, dx3, dy3);
                        }

                        isVh = !isVh;
                    }

                    if (index != state.Stack.Count)
                        throw new InvalidDataException("Invalid vhcurveto/hvcurveto operands.");

                    state.Stack.Clear();
                    break;
                }

                case 10:
                {
                    RequireArgCount(state.Stack, 1);
                    int subrIndex = (int)Math.Truncate(state.Stack[^1]);
                    state.Stack.RemoveAt(state.Stack.Count - 1);
                    ReadOnlySpan<byte> subr = state.GetLocalSubr(subrIndex);
                    if (!subr.IsEmpty)
                        ExecuteType2CharString(subr, state, depth + 1);
                    break;
                }

                case 29:
                {
                    RequireArgCount(state.Stack, 1);
                    int subrIndex = (int)Math.Truncate(state.Stack[^1]);
                    state.Stack.RemoveAt(state.Stack.Count - 1);
                    ReadOnlySpan<byte> subr = state.GetGlobalSubr(subrIndex);
                    if (!subr.IsEmpty)
                        ExecuteType2CharString(subr, state, depth + 1);
                    break;
                }

                case 11:
                    return;

                case 14:
                    state.ConsumeWidthForEndChar();
                    state.EnsureClosed();
                    state.Stack.Clear();
                    return;

                case 12:
                    ExecuteType2Escaped(program, ref ip, state);
                    break;

                default:
                    throw new InvalidDataException($"Unsupported Type2 operator {op.ToString(CultureInfo.InvariantCulture)}.");
            }
        }
    }

    private static void ExecuteType2Escaped(ReadOnlySpan<byte> program, ref int ip, Type2BuildState state)
    {
        if (ip >= program.Length)
            throw new EndOfStreamException();

        byte op = program[ip++];
        switch (op)
        {
            case 34:
                RequireArgCount(state.Stack, 7);
                state.CurveToRelative(state.Stack[0], 0, state.Stack[1], state.Stack[2], state.Stack[3], 0);
                state.CurveToRelative(state.Stack[4], 0, state.Stack[5], -state.Stack[2], state.Stack[6], 0);
                state.Stack.Clear();
                break;

            case 35:
                RequireArgCount(state.Stack, 13);
                state.CurveToRelative(state.Stack[0], state.Stack[1], state.Stack[2], state.Stack[3], state.Stack[4], state.Stack[5]);
                state.CurveToRelative(state.Stack[6], state.Stack[7], state.Stack[8], state.Stack[9], state.Stack[10], state.Stack[11]);
                state.Stack.Clear();
                break;

            case 36:
                RequireArgCount(state.Stack, 9);
                state.CurveToRelative(state.Stack[0], state.Stack[1], state.Stack[2], state.Stack[3], state.Stack[4], 0);
                state.CurveToRelative(state.Stack[5], 0, state.Stack[6], state.Stack[7], state.Stack[8], 0);
                state.Stack.Clear();
                break;

            case 37:
                RequireArgCount(state.Stack, 11);
                {
                    double d0 = state.Stack[0];
                    double d1 = state.Stack[1];
                    double d2 = state.Stack[2];
                    double d3 = state.Stack[3];
                    double d4 = state.Stack[4];
                    double d5 = state.Stack[5];
                    double d6 = state.Stack[6];
                    double d7 = state.Stack[7];
                    double d8 = state.Stack[8];
                    double d9 = state.Stack[9];
                    double d10 = state.Stack[10];

                    double xSum = d0 + d2 + d4 + d6 + d8;
                    double ySum = d1 + d3 + d5 + d7 + d9;

                    if (Math.Abs(xSum) > Math.Abs(ySum))
                        state.CurveToRelative(d0, d1, d2, d3, d4, d5 + d10);
                    else
                        state.CurveToRelative(d0, d1, d2, d3, d4 + d10, d5);

                    state.CurveToRelative(d6, d7, d8, d9, 0, 0);
                }
                state.Stack.Clear();
                break;

            default:
                throw new InvalidDataException($"Unsupported escaped Type2 operator {op.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static bool TryReadType2Number(ReadOnlySpan<byte> program, ref int ip, byte b0, out double value)
    {
        value = 0;
        switch (b0)
        {
            case >= 32 and <= 246:
                value = b0 - 139;
                return true;

            case >= 247 and <= 250:
                if (ip >= program.Length)
                    throw new EndOfStreamException();
                value = ((b0 - 247) * 256) + program[ip++] + 108;
                return true;

            case >= 251 and <= 254:
                if (ip >= program.Length)
                    throw new EndOfStreamException();
                value = -((b0 - 251) * 256) - program[ip++] - 108;
                return true;

            case 28:
                if (ip + 1 >= program.Length)
                    throw new EndOfStreamException();
                value = (short)((program[ip] << 8) | program[ip + 1]);
                ip += 2;
                return true;

            case 255:
                if (ip + 3 >= program.Length)
                    throw new EndOfStreamException();
                value = BinaryPrimitives.ReadInt32BigEndian(program.Slice(ip, 4)) / 65536.0;
                ip += 4;
                return true;

            default:
                return false;
        }
    }

    private static void RequireArgCount(List<double> stack, int atLeast)
    {
        if (stack.Count < atLeast)
            throw new InvalidDataException("Invalid Type2 charstring operands.");
    }

    private sealed class Type2BuildState(PathBuilder builder, CffTable cff, double glyphOriginX, double glyphOriginY, double scale, bool flipY)
    {
        public readonly List<double> Stack = [];
        public int HintCount;
        private bool widthConsumed;
        private bool openContour;
        private double x;
        private double y;

        public void ConsumeWidthForHints()
        {
            if (widthConsumed)
                return;

            if ((Stack.Count & 1) == 1)
            {
                Stack.RemoveAt(0);
                widthConsumed = true;
            }
        }

        public void ConsumeWidthForMove(int requiredArgs)
        {
            if (widthConsumed)
                return;

            if (Stack.Count > requiredArgs)
            {
                Stack.RemoveAt(0);
                widthConsumed = true;
            }
        }

        public void ConsumeWidthForEndChar()
        {
            if (widthConsumed)
                return;

            if (Stack.Count == 1 || Stack.Count == 5)
            {
                Stack.RemoveAt(0);
                widthConsumed = true;
            }
        }

        public void MoveToRelative(double dx, double dy)
        {
            EnsureClosed();
            x += dx;
            y += dy;
            builder.MoveTo(Transform(x, y));
            openContour = true;
        }

        public void LineToRelative(double dx, double dy)
        {
            EnsureMove();
            x += dx;
            y += dy;
            builder.LineTo(Transform(x, y));
        }

        public void CurveToRelative(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            EnsureMove();
            double c1x = x + dx1;
            double c1y = y + dy1;
            double c2x = c1x + dx2;
            double c2y = c1y + dy2;
            x = c2x + dx3;
            y = c2y + dy3;

            builder.CubicTo(Transform(c1x, c1y), Transform(c2x, c2y), Transform(x, y));
        }

        public void EnsureClosed()
        {
            if (!openContour)
                return;

            builder.Close();
            openContour = false;
        }

        private void EnsureMove()
        {
            if (openContour)
                return;

            builder.MoveTo(Transform(x, y));
            openContour = true;
        }

        public ReadOnlySpan<byte> GetLocalSubr(int index)
            => ResolveSubr(cff.LocalSubroutines, index);

        public ReadOnlySpan<byte> GetGlobalSubr(int index)
            => ResolveSubr(cff.GlobalSubroutines, index);

        private static ReadOnlySpan<byte> ResolveSubr(byte[][] subrs, int index)
        {
            if (subrs.Length == 0)
                return [];

            int bias = GetSubrBias(subrs.Length);
            int actual = index + bias;
            if ((uint)actual >= (uint)subrs.Length)
                return [];

            return subrs[actual];
        }

        private static int GetSubrBias(int count)
            => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

        private Point Transform(double px, double py)
            => new(glyphOriginX + (px * scale), glyphOriginY + (py * scale * (flipY ? -1 : 1)));
    }
}
