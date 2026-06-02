using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FileTransfer.App.ViewModels;

public enum FileMessageState { Sending, Sent, Receiving, Received, Cancelled, Failed }

public sealed partial class FileMessageViewModel : ObservableObject
{
    private readonly Func<Guid, Task>? _onCancel;

    public Guid Id { get; }
    public string Name { get; }
    public long Size { get; }
    public string Mime { get; }
    public bool IsOutgoing { get; }
    public DateTime Timestamp { get; }

    [ObservableProperty] private FileMessageState _state;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _resolvedPath;
    [ObservableProperty] private string? _failureReason;

    public bool IsImage => Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                            && State == FileMessageState.Received;

    public FileMessageViewModel(
        Guid id, string name, long size, string mime, bool isOutgoing,
        Func<Guid, Task>? onCancel = null)
    {
        Id = id;
        Name = name;
        Size = size;
        Mime = mime;
        IsOutgoing = isOutgoing;
        Timestamp = DateTime.Now;
        _state = isOutgoing ? FileMessageState.Sending : FileMessageState.Receiving;
        _onCancel = onCancel;
    }

    public void UpdateProgress(long received, long total)
        => Progress = total <= 0 ? 0 : (double)received / total;

    public void MarkSent()
    {
        Progress = 1.0;
        State = FileMessageState.Sent;
    }

    public void MarkReceived(string finalPath)
    {
        Progress = 1.0;
        ResolvedPath = finalPath;
        State = FileMessageState.Received;
        OnPropertyChanged(nameof(IsImage));
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        State = FileMessageState.Failed;
    }

    public void MarkCancelled() => State = FileMessageState.Cancelled;

    [RelayCommand]
    private Task CancelAsync() => _onCancel?.Invoke(Id) ?? Task.CompletedTask;
}
