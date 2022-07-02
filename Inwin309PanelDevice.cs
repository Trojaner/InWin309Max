using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Device.Net;
using Hid.Net.Windows;
using Usb.Net.Windows;

namespace InWin309Max;

public class InwinPanelDevice : IDisposable
{
    public bool IsInitialized => _device is { IsInitialized: true };
        
    private readonly ushort _usbVid;
    private readonly ushort _usbPid;

    private byte[] _status;
    private byte[] _hourglass;
    private IDevice _device;

    public const int Width = 8;
    public const int Height = 18;

    private const int StatusModeIdx = 1;
    private const int StatusLightIdx = 2;
    private const int StatusSpeedIdx = 3;
    // 4-15 is reserved for mode colors
    private const int StatusLampIdx = 16;
    private const int StatusMouseModeIdx = 17;
    private const int StatusFanPwmIdx = 18;
    private const int StatusPwmBypassIdx = 19;
    private const int StatusMotherboardInIdx = 20;
    private const int StatusDataSourceIdx = 21;
    private const int StatusPwmInIdx = 22;
    private const int PacketSize = 65;

    public InwinPanelDevice(ushort usbVid = 0xFF00, ushort usbPid = 0x20C)
    {
        _usbVid = usbVid;
        _usbPid = usbPid;
    }

    public async Task<bool> InitializeAsync()
    {
        if (!await FindHidDeviceAsync())
        {
            return false;
        }

        await _device.InitializeAsync();

        if (!_device.IsInitialized)
        {
            return false;
        }

        _status = new byte[24];
        _hourglass = new byte[4];

        return true;
    }

    private async Task<bool> FindHidDeviceAsync()
    {
        var hidFactory =
            new FilterDeviceDefinition(vendorId: _usbVid, productId: _usbPid, usagePage: 65280)
                .CreateWindowsHidDeviceFactory();

        var deviceDefinitions =
            (await hidFactory.GetConnectedDeviceDefinitionsAsync().ConfigureAwait(false)).ToList();

        if (deviceDefinitions.Count == 0)
        {
            //No devices were found
            return false;
        }

        _device = await hidFactory.GetDeviceAsync(deviceDefinitions[0]).ConfigureAwait(false);
        return _device != null;
    }

    private async Task<bool> FindUsbDeviceAsync()
    {
        var usbFactory =
            new FilterDeviceDefinition(vendorId: _usbVid, productId: _usbPid)
                .CreateWindowsUsbDeviceFactory();

        var deviceDefinitions =
            (await usbFactory.GetConnectedDeviceDefinitionsAsync().ConfigureAwait(false)).ToList();

        if (deviceDefinitions.Count == 0)
        {
            //No devices were found
            return false;
        }

        _device = await usbFactory.GetDeviceAsync(deviceDefinitions[1]).ConfigureAwait(false);
        return _device != null;
    }

    public void Dispose()
    {
        if (_device != null)
        {
            _device.Dispose();
            _device = null;
        }
    }

    private void UpdateStatus(byte[] newStatus)
    {
        if (newStatus[1] < 0x10)
        {
            Buffer.BlockCopy(newStatus, 0, _status, 0, 24);
        }
    }

    public async Task SetLightUpDownAsync(bool isUp)
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        if (isUp)
        {
            if (_status[StatusLightIdx] == 8)
            {
                _status[StatusLightIdx] = 4;
            }

            packet[1] = 0x2A;
            if (_status[StatusLightIdx] > 0)
            {
                _status[StatusLightIdx]--;
            }
        }
        else
        {
            packet[1] = 0x28;

            if (_status[StatusLightIdx] < 4)
            {
                _status[StatusLightIdx]++;
            }
            else
            {
                if (_status[StatusLightIdx] == 0)
                {
                    _status[StatusLightIdx] = 8;
                }
            }
        }

        packet[2] = packet[1];
        packet[3] = 0;

        await WriteAsync(packet, 100);
    }

    public async Task SetLightAsync(int value)
    {
        var light = GetLight();
        if (light == value)
        {
            return;
        }

        while (light != value)
        {
            await SetLightUpDownAsync(light <= value);
            await LoadStatusAsync();
            light = GetLight();
        }
    }

    public async Task SetSpeedUpDownAsync(bool up)
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;

        if (up)
        {
            if (_status[StatusSpeedIdx] > 0)
            {
                _status[StatusSpeedIdx]--;
            }
            packet[1] = 0x3;
        }
        else
        {
            if (_status[StatusSpeedIdx] < 6)
            {
                _status[StatusSpeedIdx]++;
            }

            packet[1] = 0x9;
        }

        packet[2] = packet[1];
        packet[3] = 0x0;

        await WriteAsync(packet, 100);
    }

    public async Task SetSpeedAsync(int value)
    {
        var speed = GetSpeed();
        if (speed != value)
        {
            while (speed != value)
            {
                await SetSpeedUpDownAsync(speed <= value);
                await LoadStatusAsync();
                speed = GetSpeed();
            }
        }
    }

    public async Task SetModeAsync(PanelMode mode, byte colorId)
    {
        var packet = new byte[PacketSize];
        if ((byte)mode == 46)
        {
            mode = PanelMode.AudioSpectrum;
        }

        _status[4 + (byte)mode] = colorId;
        packet[0] = 0x1;
        packet[1] = (byte)(mode + 48);
        packet[2] = colorId;
        packet[3] = 0;

        await WriteAsync(packet, 100);
        _status[StatusModeIdx] = (byte)(mode + 1);
    }

    public async Task LoadStatusAsync()
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0xBB;
        packet[2] = 0x1;
        packet[3] = 0x0;

        var rx = await WriteAndReadAsync(packet, 50);
        Buffer.BlockCopy(rx, 0, _status, 0, 24);
    }

    public byte GetSpeed()
    {
        return (byte)(6 - _status[StatusSpeedIdx]);
    }

    public byte GetLight()
    {
        return (byte)(6 - _status[StatusLightIdx]);
    }

    public PanelMode GetMode()
    {
        return (PanelMode)(_status[StatusModeIdx] - 1);
    }

    public byte GetModeColorId(int mode)
    {
        if (mode == 46)
        {
            mode = 3;
        }

        return _status[4 + mode];
    }

    public async Task SetRtcAsync()
    {
        var packet = new byte[PacketSize];
        var now = DateTime.Now;
        var currentInfo = DateTimeFormatInfo.CurrentInfo;
        var calendar = currentInfo.Calendar;
        var dayOfWeek = (int)calendar.GetDayOfWeek(now);

        packet[0] = 0x1;
        packet[1] = 0xBB;
        packet[2] = 0x2;
        packet[3] = (byte)(now.Second / 10 * 16 + now.Second % 10);
        packet[4] = (byte)(now.Minute / 10 * 16 + now.Minute % 10);
        packet[5] = (byte)(now.Hour / 10 * 16 + now.Hour % 10);
        packet[6] = (byte)dayOfWeek;
        packet[7] = (byte)(now.Day + 6);
        packet[8] = (byte)(now.Month / 10 * 16 + now.Month % 10);
        packet[9] = (byte)((now.Year - 2000) / 10 * 16 + (now.Year - 2000) % 10);

        await WriteAsync(packet, 100);
    }

    public async Task ReadHourGlassAsync()
    {
        var package = new byte[PacketSize];
        package[0] = 0x1;
        package[1] = 0xBB;
        package[2] = 0x6;
        package[3] = 0x0;

        var rx = await WriteAndReadAsync(package, 50);
        Buffer.BlockCopy(rx, 0, _hourglass, 0, 4);
    }

    public byte GetHourglassTimeout()
    {
        return _hourglass[1];
    }

    public byte GetHourglassInterval()
    {
        return _hourglass[2];
    }

    public byte GetHourglassPictureBitmap()
    {
        return _hourglass[3];
    }

    public async Task SetHourglassAsync(byte timeout, byte interval, byte picBitmap)
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0xBB;
        packet[2] = 0x7;

        packet[3] = timeout;
        _hourglass[1] = timeout;

        packet[4] = interval;
        _hourglass[2] = interval;

        packet[5] = picBitmap;
        _hourglass[3] = picBitmap;

        await WriteAsync(packet, 100);
    }

    public async Task WriteSpectrumAsync(byte[] band)
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0xBB;
        packet[2] = 0x11;

        const int packetOffset = 3;
        for (var i = 0; i < Width; i++)
        {
            packet[i + packetOffset] = band[i];
        }

        var rx = await WriteAndReadAsync(packet, 300);
        if (rx.Length == 0)
        {
            return;
        }

        if (rx[1] != 0x4F || rx[2] != 75)
        {
            Buffer.BlockCopy(rx, 0, _status, 0, 24);
        }
    }

    public async Task WriteImageAsync(byte pictureId, Color24Image image)
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0xBB;
        packet[2] = 0x8;
        packet[3] = pictureId;

        var imageData = image.ToByteArray();

        // Each image is split up into 8 chunks and each chunk is sent as a separate command.
        for (byte x = 0; x < Width; x++)
        {
            const int packetOffset = 11;
            const int chunkSize = 54;
                
            packet[4] = x;

            Buffer.BlockCopy(imageData, x * chunkSize, packet, packetOffset, chunkSize);
            packet[8] = packet[64]; // set control byte (?)

            await WriteAsync(packet, 100);
            await Task.Delay(300);
        }
    }

    public async Task<Color24Image> ReadPictureAsync(int id)
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0xBB;
        packet[2] = 0x10;
        packet[3] = (byte)id;

        byte[] imagePixels;
        unsafe
        {
            imagePixels = new byte[Width * Height * sizeof(Color24)];
        }

        for (var y = 0; y < Width; y++)
        {
            packet[4] = (byte)y;

            var rx = await WriteAndReadAsync(packet, 50);

            unsafe
            {
                var length = Height * sizeof(Color24);
                Buffer.BlockCopy(rx, 0xB, imagePixels, y * length, length);
            }
        }

        Color24Image image;
        unsafe
        {
            fixed (byte* ptr = imagePixels)
            {
                image = Unsafe.Read<Color24Image>(ptr);
            }
        }

        return image;
    }

    public async Task SaveImagesAsync()
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0xBB;
        packet[2] = 0x9;

        await WriteAsync(packet, 500);
    }

    private void SetStatusValue(int index, byte value)
    {
        _status[index] = value;
    }

    private byte GetStatusValue(int index)
    {
        return _status[index];
    }

    public byte GetMouseMode()
    {
        return _status[StatusMouseModeIdx];
    }

    public byte GetLightOff()
    {
        return _status[StatusLampIdx];
    }

    public async Task SetLightOffAsync(byte offOn)
    {
        _status[StatusLampIdx] = offOn;
        await UpdateStatusAsync();
    }

    public byte GetPwmLevel()
    {
        return _status[StatusFanPwmIdx];
    }

    public async Task SetPwmLevelAsync(int level)
    {
        _status[StatusFanPwmIdx] = (byte)level;
        await UpdateStatusAsync();
    }

    public byte GetPwmBypass()
    {
        return _status[StatusPwmBypassIdx];
    }

    public async Task SetPwmBypass(byte pass)
    {
        _status[StatusPwmBypassIdx] = pass;
        await UpdateStatusAsync();
    }

    public byte GetPwmIn()
    {
        return _status[StatusPwmInIdx];
    }

    public byte GetMotherboardIn()
    {
        return _status[StatusMotherboardInIdx];
    }

    public byte GetDataSource()
    {
        return _status[StatusDataSourceIdx];
    }

    public async Task SetDataSourceAsync(int ch)
    {
        _status[StatusDataSourceIdx] = (byte)ch;
        await UpdateStatusAsync();
    }

    public async Task UpdateStatusAsync()
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0xBB;
        packet[2] = 0x13;

        Buffer.BlockCopy(_status, 1, packet, 3, 22);
        await WriteAndReadAsync(packet, 50);
    }

    public async Task UpdateDefaultAsync()
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0xBB;
        packet[2] = 0x14;
        Buffer.BlockCopy(_status, 1, packet, 3, 22);

        await WriteAndReadAsync(packet, 50);
    }

    public async Task SetFanPwmUpDownAsync(bool up)
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        if (up)
        {
            if (_status[StatusFanPwmIdx] < 12)
            {
                _status[StatusFanPwmIdx]++;
            }

            packet[1] = 0x1E;
        }
        else
        {
            if (_status[StatusFanPwmIdx] > 3)
            {
                _status[StatusFanPwmIdx]--;
            }

            packet[1] = 0xF;
        }

        packet[2] = packet[1];
        packet[3] = 0;
        await WriteAsync(packet, 100);
    }

    public async Task SetFanPwmAsync(int value)
    {
        var pwmLevel = GetPwmLevel();
        if (pwmLevel == value)
        {
            return;
        }

        while (pwmLevel != value)
        {
            await SetFanPwmUpDownAsync(pwmLevel <= value);
            await LoadStatusAsync();
            pwmLevel = GetPwmLevel();
        }
    }

    public async Task ToggleLampAsync()
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0xAA;
        packet[2] = packet[1];
        packet[3] = 0;

        await WriteAsync(packet, 100);

        _status[StatusLampIdx] ^= 1;
    }

    public async Task ChangeDataSourceAsync()
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x1;
        packet[1] = 0x1C;
        packet[2] = packet[1];
        packet[3] = 0;

        await WriteAsync(packet, 100);

        _status[StatusDataSourceIdx] ^= 1;
    }

    private async Task WriteAsync(byte[] data, int delay)
    {
        if (_device == null)
        {
            throw new ObjectDisposedException(nameof(InwinPanelDevice));
        }

        await _device.WriteAsync(data).ConfigureAwait(false);

        if (delay > 0)
        {
            await Task.Delay(delay);
        }
    }

    private async Task<byte[]> WriteAndReadAsync(byte[] data, int delay)
    {
        if (_device == null)
        {
            throw new ObjectDisposedException(nameof(InwinPanelDevice));
        }
            
        var rx = await _device.WriteAndReadAsync(data);
        await Task.Delay(delay);
        return rx.Data;
    }
}