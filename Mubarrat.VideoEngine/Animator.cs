namespace Mubarrat.VideoEngine;

public delegate T Animator<T>(in T from, in T to, double t);

public static class Animators
{
    public static T Morph<T>(in T from, in T to, double t) where T : ILerpable<T> => from.Lerp(to, t);

    public static Half Morph(in Half from, in Half to, double t) => (Half)Math.FusedMultiplyAdd((double)(to - from), t, (double)from);
    public static float Morph(in float from, in float to, double t) => (float)Math.FusedMultiplyAdd(to - from, t, from);
    public static double Morph(in double from, in double to, double t) => Math.FusedMultiplyAdd(to - from, t, from);
    public static decimal Morph(in decimal from, in decimal to, double t) => from + (to - from) * (decimal)t;
    public static byte Morph(in byte from, in byte to, double t) => (byte)(from + (byte)Math.Round((to - from) * t, MidpointRounding.AwayFromZero));
    public static sbyte Morph(in sbyte from, in sbyte to, double t) => (sbyte)(from + (sbyte)Math.Round((to - from) * t, MidpointRounding.AwayFromZero));
    public static ushort Morph(in ushort from, in ushort to, double t) => (ushort)(from + (ushort)Math.Round((to - from) * t, MidpointRounding.AwayFromZero));
    public static short Morph(in short from, in short to, double t) => (short)(from + (short)Math.Round((to - from) * t, MidpointRounding.AwayFromZero));
    public static uint Morph(in uint from, in uint to, double t) => from + (uint)Math.Round((to - from) * t, MidpointRounding.AwayFromZero);
    public static int Morph(in int from, in int to, double t) => from + (int)Math.Round((to - from) * t, MidpointRounding.AwayFromZero);
    public static ulong Morph(in ulong from, in ulong to, double t) => from + (ulong)Math.Round((to - from) * t, MidpointRounding.AwayFromZero);
    public static long Morph(in long from, in long to, double t) => from + (long)Math.Round((to - from) * t, MidpointRounding.AwayFromZero);
}
