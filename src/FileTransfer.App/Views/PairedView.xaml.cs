using System.Windows.Controls;
using System.Windows.Input;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Views;

public partial class PairedView : UserControl
{
    public PairedView() { InitializeComponent(); }

    private async void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
        if (DataContext is not MainViewModel vm) return;
        e.Handled = true;
        await vm.SendTextCommand.ExecuteAsync(null);
    }
}
