using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace PulseWeaver;

/// <summary>
/// 指定したアプリ（プロセスツリー）が再生している音だけを取り込む入力ソース。
/// Windows 11（Build 20348 以降）の Application Loopback を使用する。
/// </summary>
/// <remarks>
/// Application Loopback の IAudioClient は生成元のアパートメントに縛られ、
/// 別アパートメント（UI スレッド = STA など）から触ると E_NOINTERFACE で失敗する。
/// そのため生成から解放までを 1 本の MTA ワーカースレッドに閉じ込めている。
/// </remarks>
internal sealed class ProcessLoopbackCapture : IInputCapture
{
    private const string VirtualAudioDeviceProcessLoopback = @"VAD\Process_Loopback";

    private const int AudclntShareModeShared = 0;
    private const uint AudclntStreamFlagsLoopback = 0x00020000;
    private const uint AudclntBufferFlagsSilent = 0x00000002;

    private const int AudioclientActivationTypeProcessLoopback = 1;
    private const int ProcessLoopbackModeIncludeTargetProcessTree = 0;
    private const int ProcessLoopbackModeExcludeTargetProcessTree = 1;

    private const ushort WaveFormatExtensibleTag = 0xFFFE;
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private const int BytesPerSample = 4;
    private const int BytesPerFrame = Channels * BytesPerSample;
    private const long BufferDurationHns = 2_000_000;

    private static readonly Guid SubTypeIeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");

    private readonly int _processId;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _startRequested = new(false);
    private readonly ManualResetEventSlim _startCompleted = new(false);
    private readonly ManualResetEventSlim _idle = new(true);
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private Thread? _worker;
    private Exception? _initError;
    private Exception? _startError;
    private volatile bool _started;
    private volatile bool _exited;
    private volatile bool _running;
    private volatile bool _stopRequested;
    private byte[] _buffer = new byte[BytesPerFrame * 4096];
    private byte[] _silence = new byte[BytesPerFrame * 4096];
    private long _lastAliveCheck = Environment.TickCount64;
    private volatile bool _disposed;

    public ProcessLoopbackCapture(int processId, bool includeProcessTree)
    {
        IncludeProcessTree = includeProcessTree;
        ProcessIdForDisplay = processId;
        _processId = processId;

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            throw new AudioStartupException(
                "アプリ単位の取り込みは Windows 11（Build 20348 以降）でのみ利用できます。",
                new PlatformNotSupportedException("Application Loopback is unavailable."));
        }

        if (processId <= 0)
        {
            throw new AudioStartupException(
                "取り込むアプリが選択されていません。",
                new ArgumentException("processId must be positive."));
        }

        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = $"ProcessLoopback-{processId}"
        };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(15)))
        {
            _stopRequested = true;
            throw new AudioStartupException(
                "アプリ単位の取り込みを開始できませんでした（応答がありません）。",
                new TimeoutException("Process loopback initialization timed out."));
        }

        if (_initError is not null)
        {
            throw _initError;
        }
    }

    public bool IncludeProcessTree { get; }

    public int ProcessIdForDisplay { get; }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started || _exited)
        {
            return;
        }

        _started = true;
        _startError = null;
        _startCompleted.Reset();
        _startRequested.Set();

        if (!_startCompleted.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new AudioStartupException(
                "アプリ単位の取り込みを開始できませんでした（応答がありません）。",
                new TimeoutException("Process loopback start timed out."));
        }

        if (_startError is not null)
        {
            _started = false;
            throw _startError is AudioStartupException
                ? _startError
                : new AudioStartupException(
                    $"アプリ単位の取り込みを開始できませんでした。{AudioErrorDescriber.Describe(_startError)}",
                    _startError);
        }
    }

    public void StopRecording()
    {
        _started = false;
        _stopRequested = true;
        _running = false;
        _idle.Wait(2000);
    }

    private void WorkerLoop()
    {
        try
        {
            try
            {
                Activate(_processId, IncludeProcessTree);
            }
            catch (Exception exception)
            {
                _initError = exception is AudioStartupException
                    ? exception
                    : new AudioStartupException(
                        $"アプリ単位の取り込みを初期化できませんでした。{AudioErrorDescriber.Describe(exception)}",
                        exception);
                return;
            }

            _ready.Set();

            while (true)
            {
                if (_disposed)
                {
                    break;
                }

                if (!_startRequested.Wait(100))
                {
                    continue;
                }

                _startRequested.Reset();
                if (_disposed)
                {
                    break;
                }

                RunCapture();
                _idle.Set();
            }
        }
        finally
        {
            _exited = true;
            _ready.Set();
            _startCompleted.Set();
            ReleaseComObjects();
            _idle.Set();
        }
    }

    private void RunCapture()
    {
        if (_disposed)
        {
            return;
        }

        _idle.Reset();
        _stopRequested = false;
        _lastAliveCheck = Environment.TickCount64;

        try
        {
            var hr = _audioClient!.Start();
            Marshal.ThrowExceptionForHR(hr);
        }
        catch (Exception exception)
        {
            _startError = exception;
            _startCompleted.Set();
            return;
        }

        _running = true;
        _startCompleted.Set();

        CaptureLoop();

        _running = false;
        try
        {
            _audioClient?.Stop();
        }
        catch (Exception exception)
        {
            AppLog.Error("アプリ単位の取り込みを停止できませんでした。", exception);
        }
    }

    private void ReleaseComObjects()
    {
        if (_captureClient is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_captureClient);
            }
            catch (Exception)
            {
                // 解放できなくても続行する
            }

            _captureClient = null;
        }

        if (_audioClient is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_audioClient);
            }
            catch (Exception)
            {
                // 解放できなくても続行する
            }

            _audioClient = null;
        }
    }

    private void Activate(int processId, bool includeProcessTree)
    {
        var parametersSize = Marshal.SizeOf<AudioclientActivationParams>();
        var parametersPtr = Marshal.AllocHGlobal(parametersSize);
        var propVariantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
        IActivateAudioInterfaceAsyncOperation? operation = null;

        try
        {
            var parameters = new AudioclientActivationParams
            {
                ActivationType = AudioclientActivationTypeProcessLoopback,
                ProcessLoopbackParams = new AudioclientProcessLoopbackParams
                {
                    TargetProcessId = (uint)processId,
                    ProcessLoopbackMode = includeProcessTree
                        ? ProcessLoopbackModeIncludeTargetProcessTree
                        : ProcessLoopbackModeExcludeTargetProcessTree
                }
            };
            Marshal.StructureToPtr(parameters, parametersPtr, false);

            var propVariant = new PropVariantBlob
            {
                vt = 65, // VT_BLOB
                blobSize = (uint)parametersSize,
                blobData = parametersPtr
            };
            Marshal.StructureToPtr(propVariant, propVariantPtr, false);

            var handler = new ActivationCompletionHandler();
            var audioClientId = typeof(IAudioClient).GUID;
            ActivateAudioInterfaceAsync(VirtualAudioDeviceProcessLoopback, ref audioClientId, propVariantPtr, handler, out operation);

            if (!handler.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new AudioStartupException(
                    "アプリ単位の取り込みを開始できませんでした（応答がありません）。",
                    new TimeoutException("ActivateAudioInterfaceAsync timed out."));
            }

            if (handler.AudioClient is null)
            {
                var error = handler.Error ?? Marshal.GetExceptionForHR(handler.Result) ?? new InvalidOperationException("Unknown error.");
                throw new AudioStartupException(
                    $"アプリ単位の取り込みを開始できませんでした。{AudioErrorDescriber.Describe(error)}",
                    error);
            }

            _audioClient = handler.AudioClient;
        }
        finally
        {
            Marshal.FreeHGlobal(parametersPtr);
            Marshal.FreeHGlobal(propVariantPtr);
            if (operation is not null)
            {
                Marshal.ReleaseComObject(operation);
            }
        }

        InitializeClient();
    }

    private void InitializeClient()
    {
        var format = new WaveFormatExtensible
        {
            Format = new WaveFormatEx
            {
                wFormatTag = WaveFormatExtensibleTag,
                nChannels = Channels,
                nSamplesPerSec = SampleRate,
                nAvgBytesPerSec = SampleRate * BytesPerFrame,
                nBlockAlign = BytesPerFrame,
                wBitsPerSample = BytesPerSample * 8,
                cbSize = 22
            },
            wValidBitsPerSample = BytesPerSample * 8,
            dwChannelMask = 0x3, // SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT
            SubFormat = SubTypeIeeeFloat
        };

        var formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatExtensible>());
        try
        {
            Marshal.StructureToPtr(format, formatPtr, false);
            var hr = _audioClient!.Initialize(
                AudclntShareModeShared,
                AudclntStreamFlagsLoopback,
                BufferDurationHns,
                0,
                formatPtr,
                IntPtr.Zero);
            Marshal.ThrowExceptionForHR(hr);
        }
        catch (Exception exception) when (exception is not AudioStartupException)
        {
            throw new AudioStartupException(
                $"アプリ単位の取り込みを初期化できませんでした。{AudioErrorDescriber.Describe(exception)}",
                exception);
        }
        finally
        {
            Marshal.FreeHGlobal(formatPtr);
        }

        var captureClientId = typeof(IAudioCaptureClient).GUID;
        var serviceResult = _audioClient!.GetService(ref captureClientId, out var service);
        if (serviceResult < 0 || service is not IAudioCaptureClient captureClient)
        {
            var error = Marshal.GetExceptionForHR(serviceResult) ?? new InvalidOperationException("IAudioCaptureClient を取得できませんでした。");
            throw new AudioStartupException(
                $"アプリ単位の取り込みを初期化できませんでした。{AudioErrorDescriber.Describe(error)}",
                error);
        }

        _captureClient = captureClient;
    }

    private void CaptureLoop()
    {
        try
        {
            while (_running)
            {
                if (_captureClient!.GetNextPacketSize(out var packetFrames) < 0)
                {
                    break;
                }

                if (packetFrames == 0)
                {
                    Thread.Sleep(4);
                    continue;
                }

                while (packetFrames > 0 && _running)
                {
                    var hr = _captureClient.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    if (hr < 0)
                    {
                        break;
                    }

                    var bytes = (int)frames * BytesPerFrame;
                    if (bytes > 0)
                    {
                        if ((flags & AudclntBufferFlagsSilent) != 0 || data == IntPtr.Zero)
                        {
                            EnsureBuffer(ref _silence, bytes);
                            Array.Clear(_silence, 0, bytes);
                            RaiseData(_silence, bytes);
                        }
                        else
                        {
                            EnsureBuffer(ref _buffer, bytes);
                            Marshal.Copy(data, _buffer, 0, bytes);
                            RaiseData(_buffer, bytes);
                        }
                    }

                    _captureClient.ReleaseBuffer(frames);

                    if (_captureClient.GetNextPacketSize(out packetFrames) < 0)
                    {
                        break;
                    }
                }

                var now = Environment.TickCount64;
                if (now - _lastAliveCheck >= 1000)
                {
                    _lastAliveCheck = now;
                    if (!IsProcessAlive())
                    {
                        break;
                    }
                }
            }

            if (!_disposed && !_stopRequested)
            {
                RecordingStopped?.Invoke(this, new StoppedEventArgs());
            }
        }
        catch (Exception exception)
        {
            AppLog.Error($"アプリ単位の取り込みが停止しました（PID {_processId}）。", exception);
            if (!_disposed)
            {
                RecordingStopped?.Invoke(this, new StoppedEventArgs(exception));
            }
        }
    }

    private static void EnsureBuffer(ref byte[] buffer, int required)
    {
        if (buffer.Length < required)
        {
            buffer = new byte[Math.Max(required, buffer.Length * 2)];
        }
    }

    private void RaiseData(byte[] buffer, int bytes) => DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));

    private bool IsProcessAlive()
    {
        try
        {
            using var process = Process.GetProcessById(_processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _started = false;
        _stopRequested = true;
        _running = false;
        _startRequested.Set();

        var worker = _worker;
        _worker = null;
        if (worker is null || worker == Thread.CurrentThread)
        {
            return;
        }

        try
        {
            worker.Join(3000);
        }
        catch (Exception exception)
        {
            AppLog.Error("取り込みスレッドの終了を待てませんでした。", exception);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation operation);

    [ComVisible(true)]
    private sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly ManualResetEventSlim _completed = new(false);

        public IAudioClient? AudioClient { get; private set; }

        public int Result { get; private set; }

        public Exception? Error { get; private set; }

        public bool Wait(TimeSpan timeout) => _completed.Wait(timeout);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                var hr = activateOperation.GetActivateResult(out var activateResult, out var activatedInterface);
                Result = hr < 0 ? hr : activateResult;

                if (Result >= 0 && activatedInterface is IAudioClient client)
                {
                    AudioClient = client;
                }
                else if (Result < 0)
                {
                    Error = Marshal.GetExceptionForHR(Result);
                }
            }
            catch (Exception exception)
            {
                Error = exception;
            }
            finally
            {
                _completed.Set();
            }
        }
    }

    [ComImport]
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]
        int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);

        [PreserveSig]
        int GetBufferSize(out uint bufferFrames);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out uint paddingFrames);

        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

        [PreserveSig]
        int GetMixFormat(out IntPtr deviceFormat);

        [PreserveSig]
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(IntPtr eventHandle);

        [PreserveSig]
        int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);

        [PreserveSig]
        int ReleaseBuffer(uint frames);

        [PreserveSig]
        int GetNextPacketSize(out uint frames);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioclientActivationParams
    {
        public int ActivationType;
        public AudioclientProcessLoopbackParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioclientProcessLoopbackParams
    {
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantBlob
    {
        public ushort vt;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public uint blobSize;
        public IntPtr blobData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatExtensible
    {
        public WaveFormatEx Format;
        public ushort wValidBitsPerSample;
        public uint dwChannelMask;
        public Guid SubFormat;
    }
}
