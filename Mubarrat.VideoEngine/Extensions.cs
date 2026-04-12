using System.Runtime.CompilerServices;

namespace Mubarrat.VideoEngine;

public static class Extensions
{
    extension(double d)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Lerp(double target, double t) => Math.FusedMultiplyAdd(target - d, t, d);
    }

    extension(byte b)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Lerp(byte target, double t)
        {
            double v = Math.FusedMultiplyAdd(target - b, t, b);
            return (byte)Math.Clamp((int)(v + 0.5), 0, 255);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GCD(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LCM(int a, int b) => a * b / GCD(a, b);

    extension(IEnumerable<Point> points)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Point Average() => new(points.Average(p => p.X), points.Average(p => p.Y));
    }

    extension<T>(T value)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T With(Action<T> action)
        {
            action(value);
            return value;
        }
    }
}
