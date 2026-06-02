namespace FileTransfer.App.ViewModels;

public sealed class TextMessageViewModel
{
    public string Text { get; }
    public bool IsOutgoing { get; }
    public DateTime Timestamp { get; }

    public TextMessageViewModel(string text, bool isOutgoing)
    {
        Text = text;
        IsOutgoing = isOutgoing;
        Timestamp = DateTime.Now;
    }
}
