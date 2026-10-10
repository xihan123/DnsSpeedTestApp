using System.Windows;
using MaterialDesignThemes.Wpf;

namespace DNSSpeedTester.Services;

public static class AppDialogService
{
    private static readonly SemaphoreSlim DialogQueue = new(1, 1);

    public sealed record DialogMessage(string Title, string Message, bool IsConfirmation)
    {
        public string ConfirmText => IsConfirmation ? "确认删除" : "知道了";
    }

    public static async Task<bool> ConfirmAsync(string title, string message)
        => Equals(await ShowCoreAsync(new DialogMessage(title, message, true)), "confirm");

    public static async Task ShowAsync(string title, string message)
        => await ShowCoreAsync(new DialogMessage(title, message, false));

    private static async Task<object?> ShowCoreAsync(DialogMessage content)
    {
        // DialogHost 不支持同时打开多个对话框。
        await DialogQueue.WaitAsync();
        try
        {
            return await Application.Current.Dispatcher
                .InvokeAsync(() => DialogHost.Show(content, "RootDialog")).Task.Unwrap();
        }
        finally
        {
            DialogQueue.Release();
        }
    }
}
