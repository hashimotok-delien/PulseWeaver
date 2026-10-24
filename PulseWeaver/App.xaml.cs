using System.Windows;

namespace PulseWeaver;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startMinimized = e.Args.Any(argument =>
            argument.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-m", StringComparison.OrdinalIgnoreCase));

        AppLog.Info($"起動しました (引数: {(e.Args.Length == 0 ? "なし" : string.Join(" ", e.Args))})");

        var window = new MainWindow(startMinimized);
        MainWindow = window;
        window.Show();
    }
}
