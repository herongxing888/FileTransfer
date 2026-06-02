using Microsoft.Win32;

namespace FileTransfer.App.Services;

public sealed class WpfFilePicker : IFilePicker
{
    public Task<IReadOnlyList<string>> PickAsync()
    {
        var dlg = new OpenFileDialog { Multiselect = true };
        bool? ok = dlg.ShowDialog();
        IReadOnlyList<string> result = ok == true ? dlg.FileNames : Array.Empty<string>();
        return Task.FromResult(result);
    }
}
