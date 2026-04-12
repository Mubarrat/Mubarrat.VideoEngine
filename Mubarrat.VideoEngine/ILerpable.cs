namespace Mubarrat.VideoEngine;

public interface ILerpable<T>
{
    T Lerp(in T other, double t);
}
