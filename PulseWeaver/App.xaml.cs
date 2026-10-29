using System.Threading;
using System.Windows;

namespace PulseWeaver;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\PulseWeaver.SingleInstance";
    private const string ActivateEventName = @"Local\PulseWeaver.Activate";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private Thread? _activateThread;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;

            if (IsMinimizedRequested(e.Args))
            {
                AppLog.Info("既に起動しているため、自動起動による新しいインスタンスは終了します。");
                Shutdown();
                return;
            }

            if (RequestActivation())
            {
                AppLog.Info("既に起動しているため、新しいインスタンスを終了し既存のウィンドウを表示します。");
                Shutdown();
                return;
            }

            // 表示要求を送れない既存インスタンス（旧バージョンなど）が残っている場合は、
            // 何も起動しないより二重起動を許してでも起動する
            AppLog.Error("既存の PulseWeaver に表示要求を送信できなかったため、新しいインスタンスとして起動します。");
        }

        StartActivationListener();

        var startMinimized = IsMinimizedRequested(e.Args);

        AppLog.Info($"起動しました (引数: {(e.Args.Length == 0 ? "なし" : string.Join(" ", e.Args))})");

        var window = new MainWindow(startMinimized);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 待機スレッドを起こしてから破棄する
        _activateEvent?.Set();
        _activateEvent?.Dispose();
        _activateEvent = null;
        _activateThread = null;

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 自プロセスが所有していない場合は解放不要
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private static bool IsMinimizedRequested(string[] args) =>
        args.Any(argument =>
            argument.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-m", StringComparison.OrdinalIgnoreCase));

    private void StartActivationListener()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        _activateThread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _activateEvent.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                Dispatcher.BeginInvoke(new Action(ActivateMainWindow));
            }
        })
        {
            IsBackground = true,
            Name = "PulseWeaver.ActivationListener"
        };
        _activateThread.Start();
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is MainWindow window)
        {
            window.ShowFromTray();
        }
    }

    private static bool RequestActivation()
    {
        const int maxAttempts = 10;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var handle))
                {
                    using (handle)
                    {
                        handle.Set();
                    }

                    return true;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("既存の PulseWeaver に表示要求を送信できませんでした。", exception);
                return false;
            }

            // 既存インスタンスが受信待ちの準備中である可能性があるため少し待つ
            Thread.Sleep(100);
        }

        return false;
    }
}
