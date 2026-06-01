using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Tests.Protocol;

public class MessageTypeTests
{
    [Fact]
    public void PairingConfirm_HasReservedByteValue()
    {
        Assert.Equal((byte)0x02, (byte)MessageType.PairingConfirm);
    }

    [Fact]
    public void PairingReject_HasReservedByteValue()
    {
        Assert.Equal((byte)0x03, (byte)MessageType.PairingReject);
    }
}
