using System;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Threading.Tasks;
using Accord.Math;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace InWin309Max
{
	public class AudioVisualizer : IDisposable
    {
        private static readonly int[] DefaultBand = { 63, 128, 260, 600, 1100, 2100, 4000, 8000 };
        
        private const int SampleSize = 2048;
        private const int SampleBytes = 4;
        private readonly InwinPanelDevice _device;
        
        private readonly int[] _band;
        private readonly int[,] _queue;
        
        private Collection<MMDevice> _devices;
        private BufferedWaveProvider[] _bwp;
        private MMDeviceCollection _deviceCol;
        private IWaveIn _wi;
        
        private bool _enabled;
        private int[] _spectrum;
        private int _speakerIndex;
        private int _outToggle;
        private int _inToggle;
        private int _qIndex;
        private int _lastDeviceSelect;
        
        public AudioVisualizer(InwinPanelDevice device)
        {
            _device = device;
            _queue = new int[InwinPanelDevice.Width, SampleBytes];
			_band = new int[InwinPanelDevice.Width];
            
			RefreshDeviceList();
		}

        private async Task TimerLoop()
        {
            while (_enabled)
            {
                _spectrum = UpdateSpectrum();
                
                if (_spectrum != null)
                {
                    if (_device.GetMode() == InwinPanelEffect.AudioSpectrum)
                    {
                        RefreshDeviceList();
                        
                        var band = new byte[InwinPanelDevice.Width];
                        for (var i = 0; i < band.Length; i++)
                        {
                            band[i] = (byte)_spectrum[i];
                        }

                        await _device.WriteSpectrumAsync(band);
                    }
                    else
                    {
                        Stop();
                    }
                }

                await Task.Delay(200);
            }            
        }

		public void Dispose()
        {
            Stop();
        }

		private void RefreshDeviceList()
		{
			var idx = 0;
			_devices = new Collection<MMDevice>();
			var mmdeviceEnumerator = new MMDeviceEnumerator();
            if (!mmdeviceEnumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Console))
            {
                return;
            }
            
            var defaultAudioEndpoint = mmdeviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            _deviceCol = mmdeviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var mmdevice in _deviceCol)
            {
                _devices.Add(mmdevice);
                if (mmdevice.ID == defaultAudioEndpoint.ID)
                {
                    _speakerIndex = idx;
                }
                idx++;
            }
        }

		private void RecalculateBandIndex()
		{
			for (var i = 0; i < InwinPanelDevice.Width; i++)
			{
                _band[i] = DefaultBand[i] * SampleSize / (_wi.WaveFormat.SampleRate * 2);
            }
        }

		private void InitWasapiLoopback(int index)
        {
            if (_devices.Count == 0 || _lastDeviceSelect == index)
            {
                return;
            }

            _lastDeviceSelect = index;
            _wi = new WasapiLoopbackCapture(_deviceCol[index]);
            _wi.DataAvailable += OnSampleDataAvailable;
            _bwp = new BufferedWaveProvider[2];
            
            for (var i = 0; i < 2; i++)
            {
                _bwp[i] = new BufferedWaveProvider(_wi.WaveFormat)
                {
                    BufferLength = SampleSize * SampleBytes,
                    DiscardOnBufferOverflow = true
                };
            }
            
            RecalculateBandIndex();
        }

		private void OnSampleDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_inToggle != _outToggle)
            {
                return;
            }
            
            _bwp[_inToggle].AddSamples(e.Buffer, 0, SampleSize * SampleBytes);
            _inToggle ^= 1;
        }

		private int[] UpdateSpectrum()
		{
			var samples = new byte[SampleSize * SampleBytes];
            if (_inToggle == _outToggle)
            {
                return null;
            }

            _bwp[_outToggle].Read(samples, 0, samples.Length);
            _outToggle = _inToggle;
            
            if (samples.Length == 0)
            {
                return null;
            }

            var data = new double[samples.Length / SampleBytes];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (samples[i * 4 + 3] << 24) | (samples[i * 4 + 2] << 16) |
                          (samples[i * 4 + 1] << 8) | samples[i * 4];
            }

            var fft = Fft(data);
            var band = new int[InwinPanelDevice.Width];
            for (var x = 0; x < band.Length; x++)
            {
                _queue[x, _qIndex] = (int)fft[_band[x]];
                var bandHeight = 0;
                for (var l = 0; l < 4; l++)
                {
                    bandHeight += _queue[x, l];
                }

                bandHeight /= 4;
                bandHeight -= 300;

                if (bandHeight < 0)
                {
                    band[x] = 0;
                }
                else
                {
                    band[x] = bandHeight / 16;
                }
            }

            _qIndex++;
            _qIndex &= 0x3;
            return band;
        }

		private double[] Fft(double[] data)
		{
			var transformedData = new double[data.Length];
			var fft = new Complex[data.Length];
			for (var i = 0; i < data.Length; i++)
			{
				fft[i] = new Complex(data[i], 0.0);
			}
            
			FourierTransform.FFT(fft, FourierTransform.Direction.Forward);
			for (var j = 0; j < InwinPanelDevice.Width; j++)
			{
				var band = _band[j];
				transformedData[band] = fft[band].Magnitude;
				transformedData[band] += 10.0;
				transformedData[band] = Math.Log10(transformedData[band]) * 50.0;
			}
            
			return transformedData;
		}

		public void Start()
        {
            if (_devices.Count == 0)
			{
				RefreshDeviceList();
                return;
            }

            if (!_enabled)
            {
                InitWasapiLoopback(_speakerIndex);
                _wi.StartRecording();

                _enabled = true;
                Task.Run(TimerLoop);
            }
        }

		public void Stop()
		{
            if (!_enabled || _devices.Count == 0)
            {
                return;
            }

            _enabled = false;

            _wi?.StopRecording();
            _inToggle = _outToggle = 0;
                    
            for (var i = 0; i < InwinPanelDevice.Width; i++)
            {
                for (var j = 0; j < SampleBytes; j++)
                {
                    _queue[i, j] = 0;
                }
            }
        }

		public void ChangeSpeakerLoopback(int id)
        {
            if (_devices.Count == 0)
            {
                return;
            }
            
            Stop();
            _speakerIndex = id;
            
            InitWasapiLoopback(_speakerIndex);
                
            if (_enabled)
            {
                Start();
            }
        }
    }
}
