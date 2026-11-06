using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Licensing;
using ChuvadiReader.Core.Lifecycle;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Core.Storage;
using ChuvadiReader.Core.Theme;
using ChuvadiReader.Core.Window;
using ChuvadiReader.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChuvadiReader.Hosting;

/// <summary>How long app state lives.</summary>
public enum AppStateLifetime
{
    /// <summary>One person per process: Windows, macOS, Linux, Android.</summary>
    Process,

    /// <summary>One set of services per browser connection (Blazor Server circuit). Required for Web,
    /// otherwise every signed-in person would share the same tabs, recents and settings.</summary>
    PerConnection,
}

public static class ChuvadiReaderServices
{
    /// <summary>
    /// Registers every Core service exactly as the Windows host does today (App.xaml.cs, chuvadi-reader
    /// main c7b9e44), plus safe defaults for each platform seam. Call it first, then register the
    /// host's own implementations: a later registration of the same interface wins.
    /// </summary>
    public static IServiceCollection AddChuvadiReader(this IServiceCollection services, AppStateLifetime lifetime)
    {
        // Non-generic descriptors on purpose: no generic instantiations to JIT at startup.
        var life = lifetime == AppStateLifetime.Process ? ServiceLifetime.Singleton : ServiceLifetime.Scoped;

        // Same set as the Windows host registered on chuvadi-reader main c7b9e44 (App.xaml.cs).
        foreach (var (service, impl) in CoreServices)
        {
            services.Add(new ServiceDescriptor(service, impl, life));
        }

        // Platform seams: harmless defaults. Each host replaces the ones it supports.
        foreach (var (service, impl) in SeamDefaults)
        {
            services.TryAdd(new ServiceDescriptor(service, impl, life));
        }

        return services;
    }

    private static readonly (Type Service, Type Impl)[] CoreServices =
    {
        (typeof(SaveFolderService), typeof(SaveFolderService)),
        (typeof(CategoryService), typeof(CategoryService)),
        (typeof(ThemeService), typeof(ThemeService)),
        (typeof(UserProfileService), typeof(UserProfileService)),
        (typeof(RecentFilesService), typeof(RecentFilesService)),
        (typeof(PinnedService), typeof(PinnedService)),
        (typeof(WatchedFolderService), typeof(WatchedFolderService)),
        (typeof(LibraryService), typeof(LibraryService)),
        (typeof(TagService), typeof(TagService)),
        (typeof(FolderSearchService), typeof(FolderSearchService)),
        (typeof(DocumentPropertiesService), typeof(DocumentPropertiesService)),
        (typeof(ExportService), typeof(ExportService)),
        (typeof(RedactService), typeof(RedactService)),
        (typeof(StampService), typeof(StampService)),
        (typeof(DocxViewService), typeof(DocxViewService)),
        (typeof(XlsxViewService), typeof(XlsxViewService)),
        (typeof(FlattenService), typeof(FlattenService)),
        (typeof(RecoveryService), typeof(RecoveryService)),
        (typeof(PressService), typeof(PressService)),
        (typeof(IPdfReader), typeof(ChuvadiPdfReader)),
        (typeof(TabsService), typeof(TabsService)),
        (typeof(PdfToolsService), typeof(PdfToolsService)),
        (typeof(BenchComposer), typeof(BenchComposer)),
        (typeof(BenchService), typeof(BenchService)),
        (typeof(SessionService), typeof(SessionService)),
        (typeof(OpenDocumentService), typeof(OpenDocumentService)),
        (typeof(BenchDropService), typeof(BenchDropService)),
        (typeof(RedactRequestService), typeof(RedactRequestService)),
        (typeof(ShellService), typeof(ShellService)),
        (typeof(PreferencesService), typeof(PreferencesService)),
        (typeof(TextViewService), typeof(TextViewService)),
        (typeof(MeasureService), typeof(MeasureService)),
        (typeof(SignatureCheckService), typeof(SignatureCheckService)),
        (typeof(PasswordService), typeof(PasswordService)),
        (typeof(CertificateSignService), typeof(CertificateSignService)),
        (typeof(IEntitlements), typeof(DefaultEntitlements)),
        (typeof(IAppReadySignal), typeof(AppReadySignal)),
        (typeof(IAppCommands), typeof(ShellAppCommands)),
        (typeof(IncomingFiles), typeof(IncomingFiles)),
    };

    private static readonly (Type Service, Type Impl)[] SeamDefaults =
    {
        (typeof(IUiScale), typeof(NoopUiScale)),
        (typeof(IExternalLinks), typeof(NoExternalLinks)),
        (typeof(IWindowControls), typeof(NoopWindowControls)),
        (typeof(IPrintService), typeof(NoopPrintService)),
        (typeof(IAppInstance), typeof(NoAppInstance)),
        (typeof(IHostNotices), typeof(NoHostNotices)),
        (typeof(IDroppedFiles), typeof(NoDroppedFiles)),
        (typeof(IFileWriteBack), typeof(NoFileWriteBack)),
        (typeof(IAppPaths), typeof(Desktop.DesktopAppPaths)),
        (typeof(IAccount), typeof(LocalAccount)),
        (typeof(IAppLifecycle), typeof(NoAppLifecycle)),
        (typeof(IFileManager), typeof(NoFileManager)),
    };
}
