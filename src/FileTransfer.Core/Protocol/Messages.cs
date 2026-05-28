namespace FileTransfer.Core.Protocol;

public sealed class HelloMessage
{
    public string DeviceName { get; set; } = "";
    public int ProtocolVersion { get; set; } = 1;
}

public sealed class TextMessage
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
}

public sealed class FileOffer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Mime { get; set; } = "application/octet-stream";
}

public sealed class FileDone
{
    public Guid Id { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class FileCancel
{
    public Guid Id { get; set; }
    public string Reason { get; set; } = "";
}
