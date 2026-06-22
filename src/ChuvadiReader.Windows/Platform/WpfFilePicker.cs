using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Microsoft.Win32;

namespace ChuvadiReader.Windows.Platform;

/// <summary>Native Windows file/folder dialogs (built into WPF — no extra package).</summary>
public sealed class WpfFilePicker : IFilePicker
{
    private readonly SaveFolderService _saveFolders;

    public WpfFilePicker(SaveFolderService saveFolders) => _saveFolders = saveFolders;

    public Task<string?> PickDocumentAsync(CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<string?>(null);
        }

        var picked = dispatcher.Invoke(() =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open a document",
                Filter =
                    "Documents (*.pdf;*.docx;*.xlsx)|*.pdf;*.docx;*.xlsx|" +
                    "PDF (*.pdf)|*.pdf|" +
                    "Word (*.docx)|*.docx|" +
                    "Excel (*.xlsx)|*.xlsx|" +
                    "All files (*.*)|*.*",
                CheckFileExists = true,
            };

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        });

        return Task.FromResult(picked);
    }

    public Task<IReadOnlyList<string>> PickDocumentsAsync(CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<IReadOnlyList<string>>(System.Array.Empty<string>());
        }

        var picked = dispatcher.Invoke<IReadOnlyList<string>>(() =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "Add PDF documents",
                Filter = "PDF (*.pdf)|*.pdf|All files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true,
            };

            return dlg.ShowDialog() == true ? dlg.FileNames : System.Array.Empty<string>();
        });

        return Task.FromResult(picked);
    }

    public Task<string?> PickSavePdfAsync(string suggestedName, CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<string?>(null);
        }

        var picked = dispatcher.Invoke(() =>
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save PDF",
                Filter = "PDF (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = suggestedName,
                InitialDirectory = _saveFolders.ResolveSaveFolder(),
                AddExtension = true,
                OverwritePrompt = true,
            };

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        });

        return Task.FromResult(picked);
    }

    public Task<string?> PickSaveImageAsync(string suggestedName, CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<string?>(null);
        }

        var picked = dispatcher.Invoke(() =>
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save image",
                Filter = "PNG image (*.png)|*.png",
                DefaultExt = ".png",
                FileName = suggestedName,
                InitialDirectory = _saveFolders.ResolveSaveFolder(),
                AddExtension = true,
                OverwritePrompt = true,
            };

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        });

        return Task.FromResult(picked);
    }

    public Task<string?> PickFolderAsync(CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<string?>(null);
        }

        var picked = dispatcher.Invoke(() =>
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Choose output folder",
                InitialDirectory = _saveFolders.ResolveSaveFolder(),
            };

            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        });

        return Task.FromResult(picked);
    }

    public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<IReadOnlyList<string>>(System.Array.Empty<string>());
        }

        var picked = dispatcher.Invoke<IReadOnlyList<string>>(() =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "Fetch images",
                Filter =
                    "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp)|" +
                    "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|" +
                    "All files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true,
            };

            return dlg.ShowDialog() == true ? dlg.FileNames : System.Array.Empty<string>();
        });

        return Task.FromResult(picked);
    }
}
