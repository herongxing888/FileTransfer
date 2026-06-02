namespace FileTransfer.App.Services;

public interface IFolderPicker
{
    /// Returns the chosen directory, or null if cancelled.
    Task<string?> PickAsync(string? initialDirectory = null);
}
