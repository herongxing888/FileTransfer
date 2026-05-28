namespace FileTransfer.Core.Protocol;

public enum MessageType : byte
{
    Hello = 0x01,
    PairingConfirm = 0x02,
    PairingReject = 0x03,
    Text = 0x10,
    FileOffer = 0x20,
    FileChunk = 0x21,
    FileDone = 0x22,
    FileCancel = 0x23,
    Ping = 0xF0,
    Pong = 0xF1,
}
