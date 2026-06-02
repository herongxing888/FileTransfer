namespace FileTransfer.App.ViewModels;

public enum AppState
{
    Unpaired,    // Not paired yet — show device discovery + pairing
    Pairing,     // Pairing code dialog up, waiting for both sides to confirm
    Offline,     // Paired but peer not connected
    Online,      // Paired and connected
}
