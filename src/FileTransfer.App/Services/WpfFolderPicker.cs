using System.IO;
using Microsoft.Win32;

namespace FileTransfer.App.Services;

public sealed class WpfFolderPicker : IFolderPicker
{
    public Task<string?> PickAsync(string? initialDirectory = null)
    {
        var dlg = new OpenFolderDialog();
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        bool? ok = dlg.ShowDialog();
        return Task.FromResult(ok == true ? dlg.FolderName : null);
    }
}
