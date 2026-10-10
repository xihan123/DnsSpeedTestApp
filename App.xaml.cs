using System.IO;
using System.Windows;
using System.Windows.Threading;
using DNSSpeedTester.Services;

namespace DNSSpeedTester;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 注册全局未捕获异常处理
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        DispatcherUnhandledException += HandleDispatcherException;

        // 订阅系统主题变化（"跟随系统"模式下生效）
        ThemeService.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.Shutdown();
        base.OnExit(e);
    }

    private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // 注意：AppDomain.CurrentDomain.UnhandledException 无法阻止进程终止。
        // 当 e.IsTerminating 为 true 时，CLR 仍会在本处理器返回后强制结束进程，此处仅能尽量记录日志。
        if (e.ExceptionObject is Exception ex)
        {
            LogExceptionToFile(ex);
            if (!e.IsTerminating) ShowErrorMessage($"发生未处理的异常: {ex.Message}");
        }
    }

    private void HandleDispatcherException(object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        LogExceptionToFile(e.Exception);
        ShowErrorMessage($"发生未处理的异常: {e.Exception.Message}");
        e.Handled = true;
    }

    private void ShowErrorMessage(string message)
    {
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await AppDialogService.ShowAsync("错误", message);
            }
            catch (Exception ex)
            {
                // 窗口尚未加载或正在退出时，保留日志，避免错误弹框再次触发异常。
                LogExceptionToFile(ex);
            }
        }));
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
