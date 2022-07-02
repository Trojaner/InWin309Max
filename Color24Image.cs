using System;
using System.Runtime.InteropServices;

namespace InWin309Max;

[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly struct Color24Image
{
    public const int PixelCount = InwinPanelDevice.Width * InwinPanelDevice.Height;
    public const int Size = PixelCount * Color24.Size;

    [FieldOffset(0x00)]
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = PixelCount)]
    private readonly Color24[] _pixels;

    public Color24Image()
    {
        _pixels = new Color24[PixelCount];
    }

    public Color24 this[byte x, byte y]
    {
        get => GetPixel(x, y);
        set => SetPixel(x, y, value);
    }

    private Color24 GetPixel(byte x, byte y)
    {
        return _pixels[GetIndex(x, y)];
    }

    private void SetPixel(byte x, byte y, Color24 color)
    {
        _pixels[GetIndex(x, y)] = color;
    }

    private int GetIndex(byte x, byte y)
    {
        return y * InwinPanelDevice.Width + x;
    }

    public byte[] ToByteArray()
    {
        var bytes = new byte[Size];
        unsafe
        {
            fixed (Color24* p = _pixels)
            {
                Marshal.Copy((IntPtr)p, bytes, 0, Size);
            }
        }
        return bytes;
    }
}