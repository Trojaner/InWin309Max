using System;
using System.Runtime.InteropServices;

namespace InWin309Max;

[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct Color24
{
    public const int Size = 0x3;

    public Color24(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public byte this[int index]
    {
        get
        {
            return index switch
            {
                0 => R,
                1 => G,
                2 => B,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }
    }

    [FieldOffset(0x00)] public byte R;
    [FieldOffset(0x01)] public byte G;
    [FieldOffset(0x02)] public byte B;
}