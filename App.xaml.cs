using System.IO;
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
        // 注意：AppDomain.CurrentDomain.UnhandledException 无法阻止进程终止。
        // 当 e.IsTerminating 为 true 时，CLR 仍会在本处理器返回后强制结束进程，此处仅能尽量记录日志。
        if (e.ExceptionObject is Exception ex)
        {
            LogExceptionToFile(ex);
            ShowErrorMessage($"发生未处理的异常: {ex.Message}");
        }
    }

    private void HandleDispatcherException(object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        LogExceptionToFile(e.Exception);
        ShowErrorMessage($"发生未处理的异常: {e.Exception.Message}");
        e.Handled = true;
    }

    private static void ShowErrorMessage(string message)
    {
        MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void LogExceptionToFile(Exception ex)
    {
        try
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DNSSpeedTester");
            if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);

            var logFilePath = Path.Combine(appDataPath, "error_logs.txt");
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var logEntry = $"{timestamp} 未处理的异常: {ex}{Environment.NewLine}";
            File.AppendAllText(logFilePath, logEntry);
        }
        catch
        {
            // 写日志失败时忽略，避免在异常处理器中引发二次异常
        }
    }
}