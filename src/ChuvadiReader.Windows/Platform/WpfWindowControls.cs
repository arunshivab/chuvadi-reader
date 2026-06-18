using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ChuvadiReader.Core.Window;

namespace ChuvadiReader.Windows.Platform;

/// <summary>Implements the custom-chrome window buttons + drag against the real WPF window.</summary>
public sealed class WpfWindowControls : IWindowControls
{
    private const int WmNcLButtonDown = 0xA1;
    private const int HtCaption = 0x2;

    private Window? _window;

    public event Action? StateChanged;

    public bool IsMaximized => _window?.WindowState == WindowState.Maximized;

    public void Attach(Window window)
    {
        _window = window;
        _window.StateChanged += (_, _) => StateChanged?.Invoke();
    }

    public void Minimize()
        => _window?.Dispatcher.Invoke(() => _window.WindowState = WindowState.Minimized);

    public void ToggleMaximize()
        => _window?.Dispatcher.Invoke(() =>
            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized);

    public void Close()
        => _window?.Dispatcher.Invoke(() => _window.Close());

    public void BeginDrag()
        => _window?.Dispatcher.Invoke(() =>
        {
            if (_window.WindowState == WindowState.Maximized)
            {
                return; // don't drag while maximised
            }

            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            // Hand the move to Windows itself — reliable even through WebView2,
            // which captures the mouse and defeats WPF's own caption drag.
            ReleaseCapture();
            SendMessage(hwnd, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        });

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
