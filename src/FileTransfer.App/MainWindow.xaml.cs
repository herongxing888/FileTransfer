using System.Windows;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        await vm.DropFilesCommand.ExecuteAsync(paths);
    }
}
