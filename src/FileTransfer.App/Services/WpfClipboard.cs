using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FileTransfer.App.Services;

public sealed class WpfClipboard : IClipboard
{
    public string? GrabImageAsPng()
    {
        if (!Clipboard.ContainsImage()) return null;
        BitmapSource? src = Clipboard.GetImage();
        if (src is null) return null;

        string path = Path.Combine(
            Path.GetTempPath(),
            $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var stream = File.OpenWrite(path);
        encoder.Save(stream);
        return path;
    }
}
