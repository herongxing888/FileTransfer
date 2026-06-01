# FileTransfer.App — WPF UI 设计

**日期**:2026-06-01
**状态**:已与用户确认,待进入实施计划
**上下文**:FileTransfer.Core 头-less 库已在 main 完成,涵盖 Node(已配对运行)和 PairingService(首次配对)。本设计稿描述把这两个 Core 模块挂到 WPF UI 上的工程方案。**功能范围**完全沿用原始设计稿 [2026-05-27-file-transfer-design.md](2026-05-27-file-transfer-design.md);本文聚焦"工程怎么做"。

## 目标与范围

把 Core 库的 `Node` / `PairingService` 通过 MVVM 绑定给 WPF 主窗口,实现设计稿描述的完整 v1 体验:

- 启动时根据 AppConfig 路由到 Unpaired(搜索设备 + 配对)或 Paired(聊天)路径
- 主窗口聊天式 UI:文字输入、文件拖拽、剪贴板贴图、文件进度气泡、取消传输
- 配对码弹窗(模态)+ 设置弹窗(模态)
- 单窗口、退出即清空、无托盘
- ViewModel 单测覆盖业务逻辑;XAML 不测,靠手动冒烟

**v1 不包含**(沿用原设计稿):多设备、跨网、断点续传、历史持久化、托盘、移动端。

## 解决方案选项与决策

| 选项 | 选了什么 | 为什么 |
|---|---|---|
| MVVM 框架 | CommunityToolkit.Mvvm 8.x | 源生成器消除 INPC/ICommand 样板;微软官方;一个 NuGet 依赖 |
| DI 容器 | 不引入,手写 Composition Root | 单窗口应用 ~30 行 App.xaml.cs 足够;`Microsoft.Extensions.DependencyInjection` 是负担 |
| 线程 marshal | `IDispatcher` 抽象 + WpfDispatcher/ImmediateDispatcher | Core 事件在后台线程触发;ViewModel 测试需要同步直跑 ObservableCollection 操作 |
| UI 测试 | ViewModel-only(xUnit),XAML 不测 | WPF UI 自动化测试维护成本高;ViewModel 测试覆盖业务逻辑足够;XAML 靠手动冒烟 |
| 文件传输并发 | 串行队列(队首 → 发送 → 完成 → 下一个) | 原设计稿明确;UI 进度条更简单 |
| 剪贴板图片 | 转 PNG → 临时文件 → 走 FileOffer 流程 | 原设计稿明确;复用文件传输管线 |
| 多文件拖拽 | 全部入串行队列 | 与文件并发策略一致 |
| 消息列表类型 | `ObservableCollection<object>`,Text/File/Image VM 混存 | XAML 用 DataTemplateSelector 按 VM 类型选模板,标准 WPF 做法 |
| 启动后台服务 | App.xaml.cs Composition Root 直接 new + Start | 没有"启动失败重试"复杂性;启动错就弹错误对话框直接退出 |

## 项目结构

```
FileTransfer.sln                                        ← 已有,新增 2 个项目
src/
  FileTransfer.Core/                                    ← 已有,不动
  FileTransfer.App/                                     ← 新增 WPF
    FileTransfer.App.csproj                             OutputType=WinExe, TargetFramework=net8.0-windows, UseWPF=true
    App.xaml / App.xaml.cs                              Composition Root:启动时 new 服务和 MainViewModel
    MainWindow.xaml / MainWindow.xaml.cs                DataContext=MainViewModel,内容区按 State 切 DataTemplate
    Views/
      UnpairedView.xaml                                 UserControl:搜索设备 + 配对入口
      PairedView.xaml                                   UserControl:聊天列表 + 输入框 + 状态栏
      PairingCodeDialog.xaml                            Window(模态):显示 4 位码 + 确认/取消
      SettingsDialog.xaml                               Window(模态):设备名/接收目录/开机自启/取消配对/本机指纹
    ViewModels/
      MainViewModel.cs                                  顶级状态机:Unpaired/Pairing/Online/Offline
      DeviceCandidateViewModel.cs                       发现列表的一行
      TextMessageViewModel.cs                           一条文字气泡
      FileMessageViewModel.cs                           一条文件气泡(进度/取消/状态);Mime=image/* + Received 时切到缩略图样式
      PairingCodeDialogViewModel.cs                     配对码弹窗
      SettingsViewModel.cs                              设置弹窗
    Services/
      IDispatcher.cs / WpfDispatcher.cs                 marshal 到 UI 线程
      IFilePicker.cs / WpfFilePicker.cs                 OpenFileDialog 包装
      IFolderPicker.cs / WpfFolderPicker.cs             FolderBrowserDialog 包装
      IClipboard.cs / WpfClipboard.cs                   剪贴板贴图(Bitmap → PNG → 临时文件)
      IAutoStartRegistry.cs / WpfAutoStartRegistry.cs   HKCU\...\Run 注册表读写
    Converters/                                         XAML 值转换器(文件大小/时间戳/进度可见性)

tests/
  FileTransfer.Core.Tests/                              ← 已有,不动
  FileTransfer.App.Tests/                               ← 新增
    FileTransfer.App.Tests.csproj                       TargetFramework=net8.0-windows
    Fakes/
      ImmediateDispatcher.cs                            IDispatcher 同步实现:Invoke 当场跑
      FakeFilePicker.cs / FakeFolderPicker.cs           可预设返回路径
      FakeClipboard.cs                                  可预设返回 Bitmap
      FakeAutoStartRegistry.cs                          内存里读写
    ViewModels/
      MainViewModelTests.cs
      FileMessageViewModelTests.cs
      PairingCodeDialogViewModelTests.cs
      SettingsViewModelTests.cs
```

**边界依据**:Services 都通过窄接口暴露,Wpf 实现仅在生产用,测试用 Fakes 完全脱离 WPF API;ViewModel 仅依赖接口,不引用 `Application.Current` / `MessageBox` 等。

## NuGet 依赖

新增仅一个:

- `CommunityToolkit.Mvvm` 8.x —— `[ObservableProperty]` / `[RelayCommand]` 源生成器

**不引入**:`Microsoft.Extensions.DependencyInjection`、`Prism`、`MahApps.Metro`、`Rx.NET`。

## 线程模型

Core 的所有事件(`StatusChanged` / `TextReceived` / `FileOfferReceived` / `FileProgress` / `FileCompleted` / `TransferFailed` / `PairingCandidateReady` / `PairingCompleted` / `PairingFailed`)从**后台线程**触发。所有 ViewModel 状态变更和 ObservableCollection 操作必须在 UI 线程。

```csharp
public interface IDispatcher
{
    void Invoke(Action action);          // 同步 marshal
    Task InvokeAsync(Func<Task> work);   // async marshal,事件处理器可 await
}

// 生产:Application.Current.Dispatcher.Invoke / InvokeAsync
public sealed class WpfDispatcher : IDispatcher { ... }

// 测试:action() 直接同步跑
public sealed class ImmediateDispatcher : IDispatcher { ... }
```

ViewModel 订阅 Core 事件的标准模式:

```csharp
_node.TextReceived += text =>
    _dispatcher.Invoke(() => Messages.Add(new TextMessageViewModel(text, IsOutgoing: false)));

_node.FileProgress += (id, recv, total) =>
    _dispatcher.Invoke(() => GetFileVm(id)?.UpdateProgress(recv, total));
```

测试用 `ImmediateDispatcher` 让事件同步触发,断言可以直接读 `Messages` 集合。

## MainViewModel 状态机

```
                       App.xaml.cs 启动
                              │
                              ▼
                  读取 AppConfig (DefaultPath, DpapiProtector)
                              │
                  PeerFingerprint 是否存在?
                ┌─────────────┴─────────────┐
                否                           是
                ▼                            ▼
        State = Unpaired              构造 Node + StartAsync
        PairingService 起来            ┌──────┴──────┐
                │                  Offline       Online
       发现设备 → 用户点配对          ↕(Node.StatusChanged 切换)
                │
                ▼
        State = Pairing
        PairingCandidateReady → 弹配对码窗
                │
        ┌───────┴───────┐
       Completed       Failed
        │              │
        ▼              ▼
   写 AppConfig    弹错误,回 Unpaired
   Dispose 旧 svc
   起 Node
   State = Online/Offline
```

### MainViewModel 关键属性(`[ObservableProperty]` 生成)

| 属性 | 类型 | 用途 |
|------|------|------|
| `State` | `enum AppState { Unpaired, Pairing, Online, Offline }` | UI 按这个切顶级 DataTemplate |
| `PeerName` | `string` | 顶部"对方: XXX"显示 |
| `Devices` | `ObservableCollection<DeviceCandidateViewModel>` | Unpaired 时的设备列表 |
| `Messages` | `ObservableCollection<object>` | Paired 时的聊天列表 |
| `InputText` | `string` | 输入框双向绑定 |
| `OwnFingerprint` | `string` | 设置页"本机指纹"调试显示 |
| `ConnectionLabel` | `string` | 派生:基于 State + PeerName 的状态栏文字 |

### MainViewModel 关键命令(`[RelayCommand]` 生成)

| 命令 | 触发 | 行为 |
|------|------|------|
| `RequestPairingCommand(DeviceCandidateViewModel)` | 用户点设备行的"配对" | `pairingService.RequestPairingAsync(peer)` |
| `SendTextCommand` | Enter 或点"发送" | `node.SendTextAsync(InputText)`;加 TextMessageViewModel 到 Messages |
| `PickFileCommand` | 点"📎+文件" | `filePicker.PickAsync()` → 入串行队列 |
| `PasteImageCommand` | Ctrl+V | `clipboard.GetBitmap()` → 存临时 PNG → 入队 |
| `DropFilesCommand(string[])` | 拖拽放下 | 多路径全部入队 |
| `OpenSettingsCommand` | 点 ⚙ | 弹设置窗 |
| `CancelTransferCommand(Guid)` | 文件气泡点"取消" | `node.CancelTransferAsync(id)` |
| `OpenReceivedFileCommand(string path)` | 接收完毕的文件气泡点击 | `Process.Start("explorer.exe", $"/select,{path}")` |

## 配对码弹窗流程

```
PairingService.PairingCandidateReady(code, peer)   (后台线程)
   ↓ _dispatcher.Invoke
MainViewModel:
   _ = ShowPairingDialogAsync(code, peer.DeviceName);
        ↓
   var vm = new PairingCodeDialogViewModel(code, peer.DeviceName);
   var dialog = new PairingCodeDialog { DataContext = vm, Owner = mainWindow };
   bool? result = dialog.ShowDialog();
        ↓
   result == true  → await pairingService.ConfirmAsync()
   result == false → await pairingService.RejectAsync()
   result == null  → (用户关窗) → await pairingService.RejectAsync()
   ↓
等 PairingCompleted / PairingFailed (订阅在 MainViewModel 启动时已挂)
```

`PairingCodeDialogViewModel` 只暴露:
- `Code` (string, 4 位)
- `PeerName` (string)
- `ConfirmCommand` → DialogResult=true → 关窗
- `RejectCommand` → DialogResult=false → 关窗

测试断言 dialog 关闭时 DialogResult 与命令对应。

## 文件传输 UI

### 串行队列

`MainViewModel` 内部一个 `Queue<PendingSend>`(`PendingSend = (string path, MessageType kind)`),一个 `_pumping: bool` 标志。

`Pump()` 异步循环(由入队动作触发):
1. 取队首,若空且 `_pumping = false` 退出。
2. 在 `Messages` 加 `FileMessageViewModel(state=Sending, path=peek)`。
3. `var id = await node.SendFileAsync(peek.path)`。
4. 记录 `id → vm` 映射,等 `FileProgress` / `FileCompleted` / `TransferFailed` 推进它的状态。
5. 进入第 1 步。

### 接收侧

`Node.FileOfferReceived` → 加 `FileMessageViewModel(state=Receiving, name=offer.Name, size=offer.Size)`,记 `offer.Id → vm`。
`Node.FileProgress(id, recv, total)` → `vm.UpdateProgress(recv, total)`。
`Node.FileCompleted(id, path)` → `vm.MarkReceived(path)`。
`Node.TransferFailed(id, reason)` → `vm.MarkFailed(reason)`,删 `.part` 由 Core 已做。

### FileMessageViewModel 状态

```csharp
public enum FileMessageState { Sending, Sent, Receiving, Received, Cancelled, Failed }
```

属性:`Name`、`Size`、`Progress`(0-1.0)、`State`、`ResolvedPath`(收到后才有)、`FailureReason`。
命令:`CancelCommand` → `node.CancelTransferAsync(id)`。
派生属性(`OnPropertyChanged` 触发):`ShowProgressBar`、`ShowCancelButton`、`ShowOpenButton`、`BubbleColor`。

### 图片气泡

`ImageMessageViewModel` 是 `FileMessageViewModel` 收到后的"升级版":若 `Mime` 以 `image/` 开头**且** State==Received,XAML 用 `DataTemplateSelector` 把这条切到 `ImageBubbleTemplate`(显示缩略图 + 点击打开)。或者更简单:`MainViewModel` 在 `MarkReceived` 时检查 Mime,如果是图片就**替换**集合里的 VM 为 `ImageMessageViewModel`。我们选**简单方案:替换 VM**,不引入 DataTemplateSelector 复杂性;`Messages` 元素就两种类型 `TextMessageViewModel` 和 `FileMessageViewModel`,XAML 用两个 DataTemplate 即可,图片细节由 `FileMessageViewModel` 内部处理(`IsImage` 属性 + thumbnail 字段)。

## 设置弹窗

`SettingsViewModel` 双向绑定字段:

| 字段 | 类型 | 写回 |
|------|------|------|
| `DeviceName` | `string` | `AppConfig.DeviceName` |
| `ReceiveDirectory` | `string` | `AppConfig.ReceiveDirectory`("浏览..." 调 `IFolderPicker`) |
| `AutoStart` | `bool` | `AppConfig.AutoStart` + `IAutoStartRegistry.Set` |
| `OwnFingerprintDisplay` | `string` (只读) | 从 `Node.OwnFingerprint` 或 `PairingService.OwnFingerprint` 取,只读显示 |

命令:
- `BrowseReceiveDirectoryCommand` → `folderPicker.PickAsync()`
- `SaveCommand` → 写 AppConfig + 写注册表 + 关窗(DialogResult=true)
- `CancelCommand` → 关窗(DialogResult=false)
- `UnpairCommand` → 确认弹窗 → 删 `AppConfig.PeerFingerprint` + `PeerDeviceName` → 通知 MainViewModel:Dispose Node、起 PairingService、State → Unpaired

`UnpairCommand` 触发 `Unpaired` 事件,`MainViewModel` 订阅这个事件做模式切换。

## 错误处理

| 场景 | UI 行为 |
|------|---------|
| 启动时端口被占 | App.xaml.cs 捕获 SocketException,弹错误对话框,Application.Shutdown() |
| 配对失败(reason) | PairingCodeDialog 已经关闭或未弹出,主窗口提示"配对失败:{reason}",回到 Unpaired |
| 传输失败(reason) | 对应 FileMessageViewModel.State = Failed,气泡红色显示 reason |
| sha256 不匹配 | TransferFailed 携带 "sha256 mismatch" 之类的 reason → FileMessageViewModel 红色 "文件损坏" |
| 取消配对 | 确认对话框,确认后 Dispose Node,起 PairingService,清空 Messages |
| 关窗 | App.Exit → Dispose Node + PairingService(StatusChanged 未订阅就走) |

未捕获异常:`AppDomain.CurrentDomain.UnhandledException` + `Application.Current.DispatcherUnhandledException` 兜底 → 写日志(`%APPDATA%\FileTransfer\crash.log`)+ MessageBox + Shutdown。

## 测试策略

`FileTransfer.App.Tests` 用 xUnit + ImmediateDispatcher + Fakes,完全脱离 WPF 渲染。

### 关键测试覆盖

| 类 | 测试 |
|---|---|
| `MainViewModelTests` | 启动时 AppConfig 有/无指纹路由到正确 State;`RequestPairingCommand` 调 `pairingService.RequestPairingAsync`;`PairingCompleted` 切到 Online;`StatusChanged → Offline` 切到 Offline;`SendTextCommand` 调 `node.SendTextAsync` 并加 Outgoing 气泡;`TextReceived` 加 Incoming 气泡;`DropFilesCommand` 多文件串行入队 |
| `FileMessageViewModelTests` | `UpdateProgress` 更新 `Progress` 并触发 PropertyChanged;`MarkReceived` 切到 Received 并设 ResolvedPath;`MarkFailed` 切到 Failed 并设 FailureReason;`CancelCommand` 调 `node.CancelTransferAsync(id)`;Image-MIME + Received → `IsImage = true` + Thumbnail 加载 |
| `PairingCodeDialogViewModelTests` | `Code` 和 `PeerName` 暴露正确;`ConfirmCommand` 设 DialogResult=true;`RejectCommand` 设 DialogResult=false |
| `SettingsViewModelTests` | 加载时 AppConfig 字段进 VM;`SaveCommand` 写回 AppConfig 全部字段;`AutoStart` 切换调 `IAutoStartRegistry.Set`;`UnpairCommand` 触发 `Unpaired` 事件 + 清空 AppConfig 指纹 |
| `MainViewModelTests` (接收侧子组) | `FileOfferReceived` 加 Receiving 占位;`FileProgress` 找到对应 VM 更新;`FileCompleted(id, path)` 切 Received + ResolvedPath;`TransferFailed` 切 Failed |

XAML 完全不测。Window 启动 / 绑定字符串拼错 / DataTemplate 不匹配这类靠**手动冒烟**:写完后跑 `dotnet run`,过一遍主流程(首次配对 → 互发文字 → 互发 1 MB 文件 → 拖拽图片 → 改设备名 → 取消配对 → 重新配对 → 退出)。

## 实施切片预览

预计 **14-16 个 Task**,与 Core 计划同款 TDD 节奏:

1. Scaffold `FileTransfer.App` + `FileTransfer.App.Tests`(csproj、sln 引用、CommunityToolkit.Mvvm 包)
2. `IDispatcher` + `WpfDispatcher` + `ImmediateDispatcher` + 测试
3. `IFilePicker` / `IFolderPicker` / `IClipboard` 接口 + WPF 实现 + Fakes
4. `IAutoStartRegistry` + WPF 实现 + Fake
5. `AppState` 枚举 + `MainViewModel` 骨架 + 启动时路由(Unpaired vs Online)
6. `DeviceCandidateViewModel` + `RequestPairingCommand`(PeerDiscovered → 列表)
7. `PairingCodeDialogViewModel` + 弹窗集成(Confirm/Reject DialogResult)
8. 配对完成/失败 → State 切换 + AppConfig 持久化
9. `TextMessageViewModel` + `SendTextCommand` + 收发文字气泡
10. `FileMessageViewModel` + 发送侧串行队列
11. 接收侧 `FileMessageViewModel`(FileOffer/Progress/Completed/Failed)
12. 图片处理:`IsImage` + 缩略图加载 + 剪贴板贴图入队 + 拖拽多文件入队
13. `SettingsViewModel`(设备名/接收目录/开机自启/本机指纹/取消配对)
14. `App.xaml.cs` Composition Root + `MainWindow.xaml` + UnpairedView/PairedView DataTemplates
15. `PairingCodeDialog.xaml` + `SettingsDialog.xaml` + 值转换器
16. 手动冒烟清单 + README 装机指南 + 收尾

Tasks 1-4 是基础设施,5-13 是 ViewModel 层(每个都带 xUnit 测试),14-15 是 XAML 层(无测试,手动冒烟),16 是收尾文档。

## 风险与未解

- **手动冒烟可能漏 XAML 拼写错误**:WPF 绑定错误默认静默。计划在 16 号 Task 列冒烟清单覆盖每个绑定路径,实际验证靠手动。
- **首次配对时 Core 抛 SocketException(端口占用)的 UX**:启动时弹错误对话框直接退出。不做端口顺延(原设计稿提到过,但 v1 内 YAGNI——用户改设置或杀占用进程后重启)。这一点比原设计稿略简化,标注在此。
- **`Application.Current.Dispatcher` 在 UI 线程之外抓不到的场景**:测试时 ImmediateDispatcher 完全绕过这个问题;生产时 App.xaml.cs 启动顺序保证主窗口已创建后才订阅 Core 事件。
