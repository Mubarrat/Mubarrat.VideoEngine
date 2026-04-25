using Mubarrat.OpenType.Tables;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Mubarrat.VideoEngine.Path;

/// <summary>
/// Compiles Type2 charstring programs from CFF tables into IL for efficient execution.
/// The compiled delegates take a <see cref="Type2BuildState"/> which provides methods
/// for constructing the glyph path. The compilation process handles all Type2 operators,
/// including subroutine calls, and caches the compiled delegates for reuse. This allows
/// for fast rendering of CFF-based fonts by avoiding interpretation overhead.
/// </summary>
public static class Type2IlCompiler
{
    private static readonly ConditionalWeakTable<CffTable, ConcurrentDictionary<ushort, Action<Type2BuildState>>> CompiledGlyphCache = [];

    private static readonly MethodInfo MoveToRelativeMethod =
        typeof(Type2BuildState).GetMethod(nameof(Type2BuildState.MoveToRelative), BindingFlags.Instance | BindingFlags.Public)!;

    private static readonly MethodInfo LineToRelativeMethod =
        typeof(Type2BuildState).GetMethod(nameof(Type2BuildState.LineToRelative), BindingFlags.Instance | BindingFlags.Public)!;

    private static readonly MethodInfo CurveToRelativeMethod =
        typeof(Type2BuildState).GetMethod(nameof(Type2BuildState.CurveToRelative), BindingFlags.Instance | BindingFlags.Public)!;

    private static readonly MethodInfo EnsureClosedMethod =
        typeof(Type2BuildState).GetMethod(nameof(Type2BuildState.EnsureClosed), BindingFlags.Instance | BindingFlags.Public)!;

    public static Action<Type2BuildState> CompileGlyph(CffTable cff, ushort glyphId)
    {
        ArgumentNullException.ThrowIfNull(cff);

        if (glyphId >= (uint)cff.CharStrings.Length)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        var glyphCache = CompiledGlyphCache.GetValue(cff, static _ => new ConcurrentDictionary<ushort, Action<Type2BuildState>>());
        return glyphCache.GetOrAdd(glyphId, static (id, table) => Compile(table.CharStrings[id], table), cff);
    }

    public static Action<Type2BuildState> Compile(ReadOnlySpan<byte> program, CffTable cff)
    {
        ArgumentNullException.ThrowIfNull(cff);

        var dm = new DynamicMethod(
            name: "Type2Glyph",
            returnType: typeof(void),
            parameterTypes: [typeof(Type2BuildState)],
            m: typeof(Type2IlCompiler).Module,
            skipVisibility: true);

        ILGenerator il = dm.GetILGenerator();

        Span<double> operandStorage = stackalloc double[64];
        var operands = new OperandBuffer(operandStorage);

        int hintCount = 0;
        CompileInto(il, program, cff, ref operands, ref hintCount, depth: 0);

        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate<Action<Type2BuildState>>();
    }

    private static void CompileInto(
        ILGenerator il,
        ReadOnlySpan<byte> program,
        CffTable cff,
        ref OperandBuffer operands,
        ref int hintCount,
        int depth)
    {
        if (depth > 64)
            throw new InvalidDataException("CFF subroutine recursion limit exceeded.");

        int ip = 0;

        while (ip < program.Length)
        {
            byte op = program[ip++];

            if (TryReadType2Number(program, ref ip, op, out double number))
            {
                operands.Push(number);
                continue;
            }

            switch (op)
            {
                case 1:
                case 3:
                case 18:
                case 23:
                    ConsumeWidthForHints(ref operands);
                    hintCount += operands.Count >> 1;
                    operands.Clear();
                    break;

                case 19:
                case 20:
                    ConsumeWidthForHints(ref operands);
                    hintCount += operands.Count >> 1;
                    operands.Clear();

                    int hintMaskBytes = (hintCount + 7) >> 3;
                    if ((uint)hintMaskBytes > (uint)(program.Length - ip))
                        throw new EndOfStreamException();

                    ip += hintMaskBytes;
                    break;

                case 21:
                    EmitMoveTo(il, ref operands, requiredArgs: 2, mode: MoveMode.Relative);
                    break;

                case 22:
                    EmitMoveTo(il, ref operands, requiredArgs: 1, mode: MoveMode.Horizontal);
                    break;

                case 4:
                    EmitMoveTo(il, ref operands, requiredArgs: 1, mode: MoveMode.Vertical);
                    break;

                case 5:
                    EmitRLineTo(il, ref operands);
                    break;

                case 6:
                    EmitAlternatingLineTo(il, ref operands, horizontalFirst: true);
                    break;

                case 7:
                    EmitAlternatingLineTo(il, ref operands, horizontalFirst: false);
                    break;

                case 8:
                    EmitRRCurveTo(il, ref operands);
                    break;

                case 24:
                    EmitRCurveLine(il, ref operands);
                    break;

                case 25:
                    EmitRLineCurve(il, ref operands);
                    break;

                case 26:
                    EmitVVCurveTo(il, ref operands);
                    break;

                case 27:
                    EmitHHCurveTo(il, ref operands);
                    break;

                case 30:
                case 31:
                    EmitVHOrHVCurveTo(il, ref operands, vh: op == 30);
                    break;

                case 10:
                case 29:
                    {
                        RequireArgCount(operands.Count, 1);

                        int subrIndex = (int)Math.Truncate(operands.Pop());

                        ReadOnlySpan<byte> subr = op == 10
                            ? ResolveSubr(cff.LocalSubroutines, subrIndex)
                            : ResolveSubr(cff.GlobalSubroutines, subrIndex);

                        if (!subr.IsEmpty)
                            CompileInto(il, subr, cff, ref operands, ref hintCount, depth + 1);

                        break;
                    }

                case 11:
                    return;

                case 14:
                    ConsumeWidthForEndChar(ref operands);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, EnsureClosedMethod);
                    operands.Clear();
                    return;

                case 12:
                    EmitEscaped(il, program, ref ip, cff, ref operands, ref hintCount, depth);
                    break;

                default:
                    throw new InvalidDataException($"Unsupported Type2 operator {op.ToString(CultureInfo.InvariantCulture)}.");
            }
        }
    }

    private static void EmitEscaped(
        ILGenerator il,
        ReadOnlySpan<byte> program,
        ref int ip,
        CffTable cff,
        ref OperandBuffer operands,
        ref int hintCount,
        int depth)
    {
        if (ip >= program.Length)
            throw new EndOfStreamException();

        byte op = program[ip++];

        switch (op)
        {
            case 34: // hflex
                RequireArgCount(operands.Count, 7);
                EmitCallCurve(il,
                    operands[0], 0,
                    operands[1], operands[2],
                    operands[3], 0);
                EmitCallCurve(il,
                    operands[4], 0,
                    operands[5], -operands[2],
                    operands[6], 0);
                operands.Clear();
                break;

            case 35: // flex
                RequireArgCount(operands.Count, 13);
                EmitCallCurve(il,
                    operands[0], operands[1],
                    operands[2], operands[3],
                    operands[4], operands[5]);
                EmitCallCurve(il,
                    operands[6], operands[7],
                    operands[8], operands[9],
                    operands[10], operands[11]);
                operands.Clear();
                break;

            case 36: // hflex1
                RequireArgCount(operands.Count, 9);
                EmitCallCurve(il,
                    operands[0], operands[1],
                    operands[2], operands[3],
                    operands[4], 0);
                EmitCallCurve(il,
                    operands[5], 0,
                    operands[6], operands[7],
                    operands[8], 0);
                operands.Clear();
                break;

            case 37: // flex1
                RequireArgCount(operands.Count, 11);
                EmitFlex1(il, operands);
                operands.Clear();
                break;

            default:
                throw new InvalidDataException($"Unsupported escaped Type2 operator {op.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static void EmitMoveTo(ILGenerator il, ref OperandBuffer operands, int requiredArgs, MoveMode mode)
    {
        ConsumeWidthForMove(ref operands, requiredArgs);
        RequireArgCount(operands.Count, requiredArgs);

        il.Emit(OpCodes.Ldarg_0);

        switch (mode)
        {
            case MoveMode.Relative:
                il.Emit(OpCodes.Ldc_R8, operands[0]);
                il.Emit(OpCodes.Ldc_R8, operands[1]);
                break;

            case MoveMode.Horizontal:
                il.Emit(OpCodes.Ldc_R8, operands[0]);
                il.Emit(OpCodes.Ldc_R8, 0.0);
                break;

            case MoveMode.Vertical:
                il.Emit(OpCodes.Ldc_R8, 0.0);
                il.Emit(OpCodes.Ldc_R8, operands[0]);
                break;

            default:
                throw new InvalidOperationException();
        }

        il.Emit(OpCodes.Call, MoveToRelativeMethod);
        operands.Clear();
    }

    private static void EmitRLineTo(ILGenerator il, ref OperandBuffer operands)
    {
        int count = operands.Count;
        RequireEvenCount(count, "Invalid rlineto operands.");

        ReadOnlySpan<double> values = operands.AsSpan();

        for (int i = 0; i < count; i += 2)
        {
            EmitCallLine(il, values[i], values[i + 1]);
        }

        operands.Clear();
    }

    private static void EmitAlternatingLineTo(ILGenerator il, ref OperandBuffer operands, bool horizontalFirst)
    {
        int count = operands.Count;
        ReadOnlySpan<double> values = operands.AsSpan();
        bool horizontal = horizontalFirst;

        for (int i = 0; i < count; i++)
        {
            double d = values[i];

            if (horizontal)
                EmitCallLine(il, d, 0);
            else
                EmitCallLine(il, 0, d);

            horizontal = !horizontal;
        }

        operands.Clear();
    }

    private static void EmitRRCurveTo(ILGenerator il, ref OperandBuffer operands)
    {
        int count = operands.Count;
        if ((count % 6) != 0)
            throw new InvalidDataException("Invalid rrcurveto operands.");

        ReadOnlySpan<double> values = operands.AsSpan();

        for (int i = 0; i < count; i += 6)
        {
            EmitCallCurve(il,
                values[i + 0], values[i + 1],
                values[i + 2], values[i + 3],
                values[i + 4], values[i + 5]);
        }

        operands.Clear();
    }

    private static void EmitRCurveLine(ILGenerator il, ref OperandBuffer operands)
    {
        int count = operands.Count;
        if (count < 8 || ((count - 2) % 6) != 0)
            throw new InvalidDataException("Invalid rcurveline operands.");

        ReadOnlySpan<double> values = operands.AsSpan();

        for (int i = 0; i <= count - 8; i += 6)
        {
            EmitCallCurve(il,
                values[i + 0], values[i + 1],
                values[i + 2], values[i + 3],
                values[i + 4], values[i + 5]);
        }

        EmitCallLine(il, values[count - 2], values[count - 1]);
        operands.Clear();
    }

    private static void EmitRLineCurve(ILGenerator il, ref OperandBuffer operands)
    {
        int count = operands.Count;
        if (count < 8 || ((count - 2) % 6) != 0)
            throw new InvalidDataException("Invalid rlinecurve operands.");

        ReadOnlySpan<double> values = operands.AsSpan();

        for (int i = 0; i <= count - 8; i += 2)
        {
            EmitCallLine(il, values[i + 0], values[i + 1]);
        }

        EmitCallCurve(il,
            values[count - 6], values[count - 5],
            values[count - 4], values[count - 3],
            values[count - 2], values[count - 1]);

        operands.Clear();
    }

    private static void EmitVVCurveTo(ILGenerator il, ref OperandBuffer operands)
    {
        int count = operands.Count;
        if (count < 4)
            throw new InvalidDataException("Invalid vvcurveto operands.");

        ReadOnlySpan<double> values = operands.AsSpan();

        int index = 0;
        double dx1 = 0;

        if ((count & 1) == 1)
            dx1 = values[index++];

        while (index < count)
        {
            EmitCallCurve(il,
                dx1,
                values[index++],
                values[index++],
                values[index++],
                0,
                values[index++]);

            dx1 = 0;
        }

        operands.Clear();
    }

    private static void EmitHHCurveTo(ILGenerator il, ref OperandBuffer operands)
    {
        int count = operands.Count;
        if (count < 4)
            throw new InvalidDataException("Invalid hhcurveto operands.");

        ReadOnlySpan<double> values = operands.AsSpan();

        int index = 0;
        double dy1 = 0;

        if ((count & 1) == 1)
            dy1 = values[index++];

        while (index < count)
        {
            EmitCallCurve(il,
                values[index++],
                dy1,
                values[index++],
                values[index++],
                values[index++],
                0);

            dy1 = 0;
        }

        operands.Clear();
    }

    private static void EmitVHOrHVCurveTo(ILGenerator il, ref OperandBuffer operands, bool vh)
    {
        int count = operands.Count;
        ReadOnlySpan<double> values = operands.AsSpan();
        int index = 0;

        while (index + 3 < count)
        {
            if (vh)
            {
                double dy1 = values[index++];
                double dx2 = values[index++];
                double dy2 = values[index++];
                double dx3 = values[index++];
                double dy3 = (count - index) == 1 ? values[index++] : 0;

                EmitCallCurve(il, 0, dy1, dx2, dy2, dx3, dy3);
            }
            else
            {
                double dx1 = values[index++];
                double dx2 = values[index++];
                double dy2 = values[index++];
                double dy3 = values[index++];
                double dx3 = (count - index) == 1 ? values[index++] : 0;

                EmitCallCurve(il, dx1, 0, dx2, dy2, dx3, dy3);
            }

            vh = !vh;
        }

        if (index != count)
            throw new InvalidDataException("Invalid vhcurveto/hvcurveto operands.");

        operands.Clear();
    }

    private static void EmitFlex1(ILGenerator il, OperandBuffer operands)
    {
        ReadOnlySpan<double> values = operands.AsSpan();

        double d0 = values[0];
        double d1 = values[1];
        double d2 = values[2];
        double d3 = values[3];
        double d4 = values[4];
        double d5 = values[5];
        double d6 = values[6];
        double d7 = values[7];
        double d8 = values[8];
        double d9 = values[9];
        double d10 = values[10];

        double xSum = d0 + d2 + d4 + d6 + d8;
        double ySum = d1 + d3 + d5 + d7 + d9;

        if (Math.Abs(xSum) > Math.Abs(ySum))
            EmitCallCurve(il, d0, d1, d2, d3, d4, d5 + d10);
        else
            EmitCallCurve(il, d0, d1, d2, d3, d4 + d10, d5);

        EmitCallCurve(il, d6, d7, d8, d9, 0, 0);
    }

    private static void EmitCallLine(ILGenerator il, double dx, double dy)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_R8, dx);
        il.Emit(OpCodes.Ldc_R8, dy);
        il.Emit(OpCodes.Call, LineToRelativeMethod);
    }

    private static void EmitCallCurve(ILGenerator il, double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_R8, dx1);
        il.Emit(OpCodes.Ldc_R8, dy1);
        il.Emit(OpCodes.Ldc_R8, dx2);
        il.Emit(OpCodes.Ldc_R8, dy2);
        il.Emit(OpCodes.Ldc_R8, dx3);
        il.Emit(OpCodes.Ldc_R8, dy3);
        il.Emit(OpCodes.Call, CurveToRelativeMethod);
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
                if ((uint)ip >= (uint)program.Length)
                    throw new EndOfStreamException();

                value = ((b0 - 247) * 256) + program[ip++] + 108;
                return true;

            case >= 251 and <= 254:
                if ((uint)ip >= (uint)program.Length)
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

    private static void ConsumeWidthForHints(ref OperandBuffer operands)
    {
        if ((operands.Count & 1) == 1)
            operands.DropFirst();
    }

    private static void ConsumeWidthForMove(ref OperandBuffer operands, int requiredArgs)
    {
        if (operands.Count > requiredArgs)
            operands.DropFirst();
    }

    private static void ConsumeWidthForEndChar(ref OperandBuffer operands)
    {
        if (operands.Count is 1 or 5)
            operands.DropFirst();
    }

    private static void RequireArgCount(int count, int atLeast)
    {
        if (count < atLeast)
            throw new InvalidDataException("Invalid Type2 charstring operands.");
    }

    private static void RequireEvenCount(int count, string message)
    {
        if ((count & 1) != 0)
            throw new InvalidDataException(message);
    }

    private static ReadOnlySpan<byte> ResolveSubr(byte[][] subrs, int index)
    {
        if (subrs.Length == 0)
            return [];

        int bias = subrs.Length < 1240 ? 107
            : subrs.Length < 33900 ? 1131
            : 32768;

        int actual = index + bias;

        if ((uint)actual >= (uint)subrs.Length)
            return [];

        return subrs[actual];
    }

    private enum MoveMode
    {
        Relative,
        Horizontal,
        Vertical
    }

    private ref struct OperandBuffer
    {
        private Span<double> _buffer;
        private int _start;
        private int _count;

        public OperandBuffer(Span<double> buffer)
        {
            _buffer = buffer;
            _start = 0;
            _count = 0;
        }

        public readonly int Count => _count;

        public readonly double this[int index] => _buffer[_start + index];

        public readonly ReadOnlySpan<double> AsSpan() => _buffer.Slice(_start, _count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Push(double value)
        {
            if (_start + _count >= _buffer.Length)
                CompactOrThrow();

            _buffer[_start + _count++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Pop()
        {
            if (_count == 0)
                throw new InvalidOperationException("Operand stack underflow.");

            return _buffer[_start + --_count];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DropFirst()
        {
            if (_count == 0)
                throw new InvalidOperationException("Operand stack underflow.");

            _start++;
            _count--;

            if (_count == 0)
                _start = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        private void CompactOrThrow()
        {
            if (_count == 0)
            {
                _start = 0;
                return;
            }

            if (_start == 0)
                throw new InvalidOperationException("Operand stack overflow.");

            _buffer.Slice(_start, _count).CopyTo(_buffer);
            _start = 0;
        }
    }
}

public sealed class Type2BuildState(PathBuilder builder, double glyphOriginX, double glyphOriginY, double scale, bool flipY)
{
    private readonly double yScale = scale * (flipY ? -1 : 1);

    private bool openContour;
    private double x;
    private double y;

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

        builder.CubicTo(
            Transform(c1x, c1y),
            Transform(c2x, c2y),
            Transform(x, y));
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

    private Point Transform(double px, double py)
        => new(glyphOriginX + (px * scale), glyphOriginY + (py * yScale));
}
