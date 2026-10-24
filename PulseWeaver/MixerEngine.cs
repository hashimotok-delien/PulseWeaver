using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PulseWeaver;

internal sealed class MixerEngine : IDisposable
{
    private static readonly WaveFormat MixFormat = new(48_000, 16, 2);

    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly MMDevice _systemAudioDevice;
    private readonly MMDevice _microphoneDevice;
    private readonly MMDevice _outputDevice;
    private readonly WasapiLoopbackCapture _systemAudioCapture;
    private readonly WasapiCapture _microphoneCapture;
    private readonly WasapiOut _output;
    private readonly VolumeSampleProvider _systemVolume;
    private readonly VolumeSampleProvider _microphoneVolume;
    private bool _started;
    private bool _disposed;

    public MixerEngine(string systemAudioDeviceId, string microphoneDeviceId, string outputDeviceId)
    {
        _systemAudioDevice = _deviceEnumerator.GetDevice(systemAudioDeviceId);
        _microphoneDevice = _deviceEnumerator.GetDevice(microphoneDeviceId);
        _outputDevice = _deviceEnumerator.GetDevice(outputDeviceId);

        _systemAudioCapture = new WasapiLoopbackCapture(_systemAudioDevice);
        _microphoneCapture = new WasapiCapture(_microphoneDevice);

        var systemAudioProvider = CreateBufferedProvider(_systemAudioCapture.WaveFormat);
        var microphoneProvider = CreateBufferedProvider(_microphoneCapture.WaveFormat);
        _systemAudioCapture.DataAvailable += (_, args) =>
            systemAudioProvider.AddSamples(args.Buffer, 0, args.BytesRecorded);
        _microphoneCapture.DataAvailable += (_, args) =>
            microphoneProvider.AddSamples(args.Buffer, 0, args.BytesRecorded);

        _systemVolume = CreateVolumeProvider(systemAudioProvider);
        _microphoneVolume = CreateVolumeProvider(microphoneProvider);

        var mixer = new MixingSampleProvider(new[] { _systemVolume, _microphoneVolume })
        {
            ReadFully = true
        };
        var resampler = new MediaFoundationResampler(
            new SampleToWaveProvider16(mixer),
            MixFormat)
        {
            ResamplerQuality = 60
        };
        _output = new WasapiOut(_outputDevice, AudioClientShareMode.Shared, true, 100);
        _output.Init(resampler);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _systemAudioCapture.StartRecording();
        _microphoneCapture.StartRecording();
        _output.Play();
        _started = true;
    }

    public void SetVolumes(float systemAudio, float microphone)
    {
        _systemVolume.Volume = Math.Clamp(systemAudio, 0f, 1f);
        _microphoneVolume.Volume = Math.Clamp(microphone, 0f, 1f);
    }

    private static BufferedWaveProvider CreateBufferedProvider(WaveFormat format) =>
        new(format)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

    private static VolumeSampleProvider CreateVolumeProvider(BufferedWaveProvider provider)
    {
        var resampler = new MediaFoundationResampler(provider, MixFormat)
        {
            ResamplerQuality = 60
        };
        return new VolumeSampleProvider(resampler.ToSampleProvider());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started)
        {
            _systemAudioCapture.StopRecording();
            _microphoneCapture.StopRecording();
            _output.Stop();
        }

        _output.Dispose();
        _systemAudioCapture.Dispose();
        _microphoneCapture.Dispose();
        _systemAudioDevice.Dispose();
        _microphoneDevice.Dispose();
        _outputDevice.Dispose();
        _deviceEnumerator.Dispose();
    }
}
