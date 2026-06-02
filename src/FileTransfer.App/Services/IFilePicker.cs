namespace FileTransfer.App.Services;

public interface IFilePicker
{
    /// Returns the absolute paths the user chose, or an empty array if they cancelled.
    /// Multiple selection is allowed (matches the "drop multiple" UX).
    Task<IReadOnlyList<string>> PickAsync();
}
