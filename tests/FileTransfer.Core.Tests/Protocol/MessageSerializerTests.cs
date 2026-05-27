using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Tests.Protocol;

public class MessageSerializerTests
{
    [Fact]
    public void TextMessage_RoundTrips()
    {
        var msg = new TextMessage { Id = Guid.NewGuid(), Text = "你好, world" };

        byte[] bytes = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<TextMessage>(bytes);

        Assert.Equal(msg.Id, back.Id);
        Assert.Equal(msg.Text, back.Text);
    }

    [Fact]
    public void FileOffer_RoundTrips()
    {
        var offer = new FileOffer { Id = Guid.NewGuid(), Name = "report.pdf", Size = 2400000, Mime = "application/pdf" };

        var back = MessageSerializer.Deserialize<FileOffer>(MessageSerializer.Serialize(offer));

        Assert.Equal(offer.Name, back.Name);
        Assert.Equal(offer.Size, back.Size);
        Assert.Equal(offer.Mime, back.Mime);
    }

    [Fact]
    public void HelloMessage_RoundTrips()
    {
        var hello = new HelloMessage { DeviceName = "DESKTOP-XYZ", ProtocolVersion = 1 };

        var back = MessageSerializer.Deserialize<HelloMessage>(MessageSerializer.Serialize(hello));

        Assert.Equal("DESKTOP-XYZ", back.DeviceName);
        Assert.Equal(1, back.ProtocolVersion);
    }
}
