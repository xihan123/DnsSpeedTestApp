using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using DNSSpeedTester.Models;
using MaterialDesignThemes.Wpf;

namespace DNSSpeedTester;

public partial class MainWindow : Window
{
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void RootDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not DialogHost { IsOpen: true } host) return;
        host.CurrentSession?.Close("cancel");
        e.Handled = true;
    }

    private void DnsServers_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (e.Column.SortMemberPath != nameof(DnsServer.Latency)) return;

        e.Handled = true;
        var grid = (DataGrid)sender;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            // 空延迟始终放在末尾。
            view.SortDescriptions.Add(new SortDescription(nameof(DnsServer.HasLatency), ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(nameof(DnsServer.Latency), direction));
        }

        foreach (var column in grid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
    }

    // 修复无边框窗口 (WindowStyle=None + AllowsTransparency) 最大化时遮挡任务栏、忽略工作区的问题。
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            AdjustMaxSizeToWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void AdjustMaxSizeToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var work = monitorInfo.rcWork;
        var full = monitorInfo.rcMonitor;

        mmi.ptMaxPosition.X = Math.Abs(work.left - full.left);
        mmi.ptMaxPosition.Y = Math.Abs(work.top - full.top);
        mmi.ptMaxSize.X = Math.Abs(work.right - work.left);
        mmi.ptMaxSize.Y = Math.Abs(work.bottom - work.top);
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            MaximizeRestoreButton_Click(sender, e);
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaximizeIcon.Kind = PackIconKind.WindowMaximize;
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaximizeIcon.Kind = PackIconKind.WindowRestore;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
