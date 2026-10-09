using System.Windows;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace DNSSpeedTester.Services;

/// <summary>
///     应用主题模式：浅色 / 深色 / 跟随系统。
/// </summary>
public enum AppThemeMode
{
    Light,
    Dark,
    System
}

/// <summary>
///     主题管理：应用浅色/深色主题，并在"跟随系统"模式下响应系统主题变化。
/// </summary>
public static class ThemeService
{
    private static readonly PaletteHelper Palette = new();
    private static bool _initialized;

    public static AppThemeMode CurrentMode { get; private set; } = AppThemeMode.System;

    /// <summary>订阅系统主题变化（应用启动时调用一次）。</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>取消订阅（应用退出时调用）。</summary>
    public static void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    /// <summary>应用指定主题模式。</summary>
    public static void Apply(AppThemeMode mode)
    {
        CurrentMode = mode;
        ApplyBaseTheme(mode switch
        {
            AppThemeMode.Light => false,
            AppThemeMode.Dark => true,
            _ => IsSystemDarkTheme()
        });
    }

    private static void ApplyBaseTheme(bool isDark)
    {
        var theme = Palette.GetTheme();
        theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        Palette.SetTheme(theme);
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (CurrentMode != AppThemeMode.System || e.Category != UserPreferenceCategory.General) return;

        // 系统事件在独立线程触发，主题资源只能在 UI 线程修改
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.Invoke(() =>
        {
            if (CurrentMode == AppThemeMode.System) ApplyBaseTheme(IsSystemDarkTheme());
        });
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }
}
