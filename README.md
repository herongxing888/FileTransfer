# FileTransfer

LAN file-transfer app for two Windows machines. .NET 8 + WPF + custom TLS protocol.

## Build and run

Prerequisites: .NET 8 SDK on Windows 10/11.

```powershell
dotnet build
dotnet test
dotnet run --project src/FileTransfer.App
```

## First-time use

1. Install on both machines. First launch creates a self-signed cert in `%APPDATA%\FileTransfer\config.json`.
2. Allow the app through Windows Firewall for **private network** when prompted (UDP 47100, TCP 47101).
3. Launch on both. Each shows the other in the discovery list.
4. Click "配对" on one side. Compare the 4-digit code on both screens; if they match, click "确认" on both.
5. You're now paired. Type messages, drag files, or click 📎 to send.

## Architecture

- `FileTransfer.Core` — headless library: protocol framing, TLS transport, file sender/receiver, Node (paired runtime), PairingService (first-time pairing).
- `FileTransfer.App` — WPF + MVVM UI (CommunityToolkit.Mvvm).
- Tests:
  - `FileTransfer.Core.Tests` — 65 xUnit tests, loopback end-to-end coverage of the protocol stack.
  - `FileTransfer.App.Tests` — xUnit tests for every ViewModel, using IDispatcher/IFilePicker/etc. fakes.

XAML is verified by manual smoke (see [docs/smoke-checklist.md](docs/smoke-checklist.md)).
