using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Pure value↔geometry mapping + hit-testing for the PMT key×velocity zone editor. X is the
/// MIDI key (0..127, left→right), Y is velocity (0..127, loud at top). No Avalonia dependency.</summary>
public static class PmtZoneMapping
{
    public const int Min = 0, Max = 127;

    public readonly record struct Rect(double X, double Y, double W, double H);
    public enum Handle { None, Body, Left, Right, Top, Bottom }

    public static int Clamp(int v) => v < Min ? Min : v > Max ? Max : v;

    public static double KeyToX(int key, double w) => Clamp(key) / 127.0 * w;
    public static int XToKey(double x, double w)
        => w <= 0 ? Min : Clamp((int)Math.Round(x / w * 127.0, MidpointRounding.AwayFromZero));

    public static double VelToY(int vel, double h) => (1.0 - Clamp(vel) / 127.0) * h;
    public static int YToVel(double y, double h)
        => h <= 0 ? Min : Clamp((int)Math.Round((1.0 - y / h) * 127.0, MidpointRounding.AwayFromZero));

    /// <summary>Pixel rectangle for a zone. Tolerates lo/hi being swapped.</summary>
    public static Rect ToRect(int keyLo, int keyHi, int velLo, int velHi, double w, double h)
    {
        var x = KeyToX(Math.Min(keyLo, keyHi), w);
        var x2 = KeyToX(Math.Max(keyLo, keyHi), w);
        var yTop = VelToY(Math.Max(velLo, velHi), h);
        var yBot = VelToY(Math.Min(velLo, velHi), h);
        return new Rect(x, yTop, x2 - x, yBot - yTop);
    }

    /// <summary>Which part of one rectangle is under (px,py): an edge within <paramref name="margin"/>,
    /// else the body if inside, else None. Edges win over body.</summary>
    public static Handle HitRect(double px, double py, Rect r, double margin)
    {
        var inX = px >= r.X - margin && px <= r.X + r.W + margin;
        var inY = py >= r.Y - margin && py <= r.Y + r.H + margin;
        if (!inX || !inY) return Handle.None;
        if (Math.Abs(px - r.X) <= margin) return Handle.Left;
        if (Math.Abs(px - (r.X + r.W)) <= margin) return Handle.Right;
        if (Math.Abs(py - r.Y) <= margin) return Handle.Top;
        if (Math.Abs(py - (r.Y + r.H)) <= margin) return Handle.Bottom;
        if (px > r.X && px < r.X + r.W && py > r.Y && py < r.Y + r.H) return Handle.Body;
        return Handle.None;
    }
}
