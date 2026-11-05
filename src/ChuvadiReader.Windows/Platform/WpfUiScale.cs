using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ChuvadiReader.Core.Window;

namespace ChuvadiReader.Windows.Platform;

/// <summary>Interface size as a WebView2 zoom of the whole window. The person's own zoom gestures
/// stay off (MainWindow disables them); only this setting changes the zoom.</summary>
public sealed class WpfUiScale : IUiScale
{
    private Microsoft.Web.WebView2.Wpf.IWebView2? _view;
    private System.Windows.Threading.Dispatcher? _dispatcher;
    private double _factor = 1.0;

    public bool IsSupported => true;

    /// <summary>Called by MainWindow once the WebView exists; applies any scale set before then.</summary>
    public void Attach(Microsoft.Web.WebView2.Wpf.IWebView2 view, System.Windows.Threading.Dispatcher dispatcher)
    {
        _view = view;
        _dispatcher = dispatcher;
        Apply();
    }

    public void SetScale(int percent)
    {
        _factor = percent switch
        {
            125 => 1.25,
            150 => 1.5,
            _ => 1.0,
        };
        Apply();
    }

    private void Apply()
    {
        var view = _view;
        var d = _dispatcher;
        if (view is null || d is null)
        {
            return;
        }

        var factor = _factor;
        void Set()
        {
            try
            {
                if (Math.Abs(view.ZoomFactor - factor) > 0.001)
                {
                    view.ZoomFactor = factor;
                }
            }
            catch
            {
                // the WebView may be closing; the next launch applies the stored value
            }
        }

        if (d.CheckAccess())
        {
            Set();
        }
        else
        {
            d.BeginInvoke(Set);
        }
    }
}

/// <summary>Opens http, https and mailto links in the default browser / mail app.</summary>
public sealed class WpfExternalLinks : IExternalLinks
{
    public bool IsAvailable => true;

    public Task<bool> OpenAsync(string url)
    {
        if (!IExternalLinks.IsAllowed(url))
        {
            return Task.FromResult(false);
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
