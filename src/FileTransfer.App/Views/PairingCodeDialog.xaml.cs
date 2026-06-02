using System.Windows;

namespace FileTransfer.App.Views;

public partial class PairingCodeDialog : Window
{
    public PairingCodeDialog() { InitializeComponent(); }
    private void OnConfirmClicked(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void OnRejectClicked(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
