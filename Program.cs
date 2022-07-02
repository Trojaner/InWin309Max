using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace InWin309Max;

internal class Program
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SwHide = 0x00;
    private const int SwShow = 0x05;

    private static InwinPanelDevice _panelDevice;
    private static AudioVisualizer _spectrum;

    ~Program()
    {
        _panelDevice?.Dispose();
    }

    private static async Task Main(string[] args)
    {
        var handle = GetConsoleWindow();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ShowWindow(handle, args.Contains("--hide") ? SwHide : SwShow);
        }

        _panelDevice = new InwinPanelDevice();
        if (!await _panelDevice.InitializeAsync())
        {
            Console.WriteLine("Failed to connect to panel.");
            return;
        }

        Console.WriteLine("Connected to panel.");

        _spectrum = new AudioVisualizer(_panelDevice);

        // Initialization routine
        await _panelDevice.SetRtcAsync();
        await _panelDevice.LoadStatusAsync();
        await _panelDevice.SetDataSourceAsync(0);

        /* Custom logic here */

        await DrawGifAsync("assets/Matrix.gif");
    }

    private static async Task DrawGifAsync(string path)
    {
        await SwitchModeAsync(PanelMode.Image, 0);

        while (true)
        {
            var image = await Image.LoadAsync<Rgba32>(path);
                
            for (var i = 0; i < image.Frames.Count; i++)
            {
                var frame = image.Frames[i];
                var metadata = frame.Metadata.GetGifMetadata();
                if (metadata.FrameDelay > 0)
                {
                    await Task.Delay(metadata.FrameDelay * 10);
                }

                var encodedImage = new Color24Image();

                for (byte x = 0; x < frame.Width; x++)
                {
                    for (byte y = 0; y < frame.Height; y++)
                    {
                        var color = frame[x, y];
                        encodedImage[x, y] = new Color24(color.R, color.G, color.B);
                    }
                }

                await _panelDevice.WriteImageAsync(0, encodedImage);
            }
        }
    }

    private static async Task DrawImageAsync(string path)
    {
        await SwitchModeAsync(PanelMode.Image, 0);
            
        var encodedImage = new Color24Image();

        var image = await Image.LoadAsync<Rgba32>(path);

        if (image.Height > 18 || image.Width > 8)
        {
            throw new Exception("Image must be 8x18 at max");
        }

        for (byte x = 0; x < image.Width; x++)
        {
            for (byte y = 0; y < image.Height; y++)
            {
                var color = image[x, y];
                encodedImage[x, y] = new Color24(color.R, color.G, color.B);
            }
        }

        await _panelDevice.WriteImageAsync(0, encodedImage);
        await _panelDevice.SaveImagesAsync();
    }

    private static async Task SwitchModeAsync(PanelMode mode, byte colorId)
    {
        await _panelDevice.SetModeAsync(mode, colorId);

        if (mode == PanelMode.AudioSpectrum)
        {
            _spectrum.Start();
        }
        else
        {
            _spectrum.Stop();
        }
    }

    private static async Task LoopModesAsync()
    {
        while (true)
        {
            for (byte i = 0; i < 12; i++)
            {
                for (byte j = 0; j < 3; j++)
                {
                    Console.WriteLine($"Mode: {i}, Color: {j}");

                    await _panelDevice.SetModeAsync((PanelMode)i, j);
                    await Task.Delay(800);
                }
            }
        }
    }
}