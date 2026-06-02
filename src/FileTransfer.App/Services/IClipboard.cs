namespace FileTransfer.App.Services;

public interface IClipboard
{
    /// If the clipboard contains an image, saves it as a PNG to a temp file and
    /// returns the absolute path. Returns null if no image is available.
    /// The caller owns the file and may move/delete it (file-transfer pipeline
    /// will move it into the receive directory eventually).
    string? GrabImageAsPng();
}
