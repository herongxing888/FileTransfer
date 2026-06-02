# FileTransfer.App Smoke Checklist

Run this on a fresh build before tagging a release. Two Windows 10/11 machines on the same LAN. Both must allow the app through the firewall on UDP 47100 + TCP 47101.

## First-time pairing
- [ ] Launch on both machines. Both show "搜索附近设备".
- [ ] Within ~5 s each side's list shows the other device.
- [ ] Click "配对" on one side. Both sides pop a dialog with the SAME 4-digit code.
- [ ] Click "确认" on both. Both windows switch to the paired chat view.

## Text messaging
- [ ] Type a message on side A, press Enter. Side B sees the bubble.
- [ ] Send Chinese / emoji / multi-line (Shift+Enter then Enter). Renders correctly.

## File transfer
- [ ] Drag a 1 MB PDF onto side A's window. Side A shows "Sending" bubble with progress; side B shows "Receiving" bubble that completes.
- [ ] File appears in side B's `%USERPROFILE%\Downloads\FileTransfer\` with the correct name.
- [ ] Drag 3 files at once. They send serially in order.
- [ ] Click "📎 文件" and select 2 files. They queue and send.
- [ ] Cancel a 100 MB file mid-transfer. Both sides show the cancelled state.

## Clipboard image
- [ ] Take a screenshot on side A (Win+Shift+S, copy to clipboard). Focus side A's window and press Ctrl+V. (Note: for v1 this is a button-driven action — actual Ctrl+V handling deferred.)

## Settings
- [ ] Open settings (⚙). Change device name, save. Restart app, name persists.
- [ ] Browse a new receive directory, save. Send a file from the other side; arrives in the new directory.
- [ ] Toggle auto-start, save. Verify HKCU\Software\Microsoft\Windows\CurrentVersion\Run has "FileTransfer" entry. Uncheck and save; entry gone.
- [ ] Note "本机指纹" displays a 64-hex value.

## Unpair + repair
- [ ] Click "取消配对" in settings. App goes back to discovery view.
- [ ] Pair again with the same peer. Works.

## Disconnect handling
- [ ] Pull side B's Wi-Fi. Side A's status switches to "离线" within ~30 s.
- [ ] Reconnect side B. Side A returns to "已连接" within a few seconds.

## Crash / shutdown
- [ ] Close window (×). App exits. No background process lingers in Task Manager.
