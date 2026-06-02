using System.Windows;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog() { InitializeComponent(); }
    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.ApplyAutoStart();
        DialogResult = true;
        Close();
    }
    private void OnCancelClicked(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
