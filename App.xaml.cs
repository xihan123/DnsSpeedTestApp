using System.Windows;
using System.Windows.Threading;

namespace DNSSpeedTester;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 注册全局未捕获异常处理
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        DispatcherUnhandledException += HandleDispatcherException;
    }

    private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) ShowErrorMessage($"发生未处理的异常: {ex.Message}");
    }

    private void HandleDispatcherException(object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        ShowErrorMessage($"发生未处理的异常: {e.Exception.Message}");
        e.Handled = true;
    }

    private static void ShowErrorMessage(string message)
    {
        MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}