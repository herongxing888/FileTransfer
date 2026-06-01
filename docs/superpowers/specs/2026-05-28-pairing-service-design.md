# PairingService — 首次配对模块设计

**日期**:2026-05-28
**状态**:已与用户确认,待进入实施计划
**上下文**:FileTransfer.Core 头-less 库已在 [2026-05-27-file-transfer-core.md](../plans/2026-05-27-file-transfer-core.md) 中完成实现并合入 `main`。当前 `Node` 仅支持**已配对**场景(`NodeOptions.PeerFingerprint` 必填)。本模块补齐设计稿 [2026-05-27-file-transfer-design.md](2026-05-27-file-transfer-design.md) 描述的**首次配对**流程。WPF UI 是下一阶段的独立设计稿,不在本文范围。

## 目标与范围

让两台同 LAN 内、**互不知道对方指纹**的 Windows 主机,通过一次"点配对 + 看 4 位数字码 + 双方点确认"的交互,安全建立长期配对关系。流程结束的产物是:**双方都拿到对端真实证书指纹和 deviceName,UI 持久化到 AppConfig 后即可构造 `Node` 进入常规运行**。

**v1 范围内:**

- 两台设备的 1-对-1 配对(与设计稿"用户只 2 台"约束一致)
- 自动发现陌生设备(任意指纹)并暴露给 UI
- 任意一台机器都能发起;对端无需任何本地操作就能进入"显示配对码"步骤
- 4 位配对码 MITM 防御
- 双方点确认/拒绝的协议级握手(避免 A 持久化对端指纹时 B 还没确认)

**不在范围内:**

- WPF UI 实现(下一阶段)
- 多台设备同时配对的选择列表交互(Core 暴露候选事件即可,UI 自行实现)
- 配对码的可访问性(语音播报等)
- 通过手动输入 IP + 指纹的"无 UDP 兜底"路径(设计稿未要求,YAGNI)

## 模块结构

```
src/FileTransfer.Core/
  Pairing/                              ← 新模块目录
    PairingService.cs                   ← 主编排器
    PairingState.cs                     ← enum
    PairingResult.cs                    ← record
    PeerCandidate.cs                    ← record
    PairingFailureReason.cs             ← enum
    PairingServiceOptions.cs            ← record(或 class)
  Transport/
    TransportListener.cs                ← 改:peerFingerprint 可空
    TransportConnector.cs               ← 改:peerFingerprint 可空
    Connection.cs                       ← 加 PeerFingerprint 属性
  Protocol/
    MessageType.cs                      ← 加 PairingConfirm = 0x02, PairingReject = 0x03

tests/FileTransfer.Core.Tests/
  Pairing/
    PairingServiceTests.cs              ← loopback 端到端
  Transport/
    TlsHandshakeTests.cs                ← 加 unpinned-mode 测试
```

## 协议加项

`MessageType` 在现有空闲槽内新增两个值:

| Type | 名字 | payload | 用途 |
|------|------|---------|------|
| `0x02` | `PairingConfirm` | 空 | 本侧用户点了"确认" |
| `0x03` | `PairingReject` | 空 | 本侧用户点了"拒绝" |

HELLO(`0x01`,既有 `HelloMessage`)继续作为 TLS 握手后第一条强制消息,携带 `DeviceName` 和 `ProtocolVersion`(本期固定为 `1`)。HELLO 之后才允许发 `PairingConfirm` / `PairingReject`。

## Transport 改造:不钉指纹模式

`TransportListener` 与 `TransportConnector` 现在的 `peerFingerprint` 参数从 `string` 改为 `string?`:

- 不为 null:既有行为(`RemoteCertificateValidationCallback` 校验对端证书 SHA256 == 该值,不等则拒绝)
- 为 null:仍走完整 TLS 握手,但 `RemoteCertificateValidationCallback` 永远返回 `true`(我们信任 LAN 内任何 self-signed,真实身份验证由"配对码 + 用户确认"步骤完成)

`Connection` 增加只读属性 `PeerFingerprint`(类型 `string?`,握手前为 null,握手成功后被 Listener/Connector 写入)。`Node` 不依赖此属性(已知钉的指纹);`PairingService` 强依赖它来拿对端真实指纹。

`Fingerprint.Compute(cert.RawData)` 在握手后由 Listener/Connector 内部统一调用一次,产物写入 `Connection.PeerFingerprint`,保证 Core 内"如何从证书提指纹"只有一个真理点。

## 状态机

PairingService 内部单一 active session(任何时刻最多一对):

```
                       [StartAsync]
                          │
                          ▼
                       ┌─────┐ ── UDP 发现任意陌生设备 → 抛 PeerDiscovered
                       │Idle │
                       └─────┘
                          │
   ┌──────────────────────┴──────────────────────┐
   │                                             │
本侧 UI 调 RequestPairingAsync(peer)         本侧 listener 收到对端连入
   │                                             │
   └──────────────────────┬──────────────────────┘
                          ▼
                   ┌────────────┐
                   │Negotiating │   TLS 握手(不钉)+ HELLO 交换
                   └────────────┘
                          │
                          ▼ (HELLO ok, ProtocolVersion 匹配)
                ┌────────────────────┐
                │ AwaitingDecision   │   抛 PairingCandidateReady(code, peer)
                │ 等本地 + 对端确认  │   计 DecisionTimeout
                └────────────────────┘
                  │              │
        双方都 confirm    任一 reject / 超时 / 断线 / 协议不匹配
                  ▼              ▼
            ┌──────────┐    ┌────────┐
            │Completed │    │ Failed │
            └──────────┘    └────────┘
```

转移触发与所发事件:

| 当前态 | 输入 | 下一态 | 抛事件 |
|---|---|---|---|
| `Idle` | UDP 发现 | `Idle` | `PeerDiscovered(peer)` |
| `Idle` | `RequestPairingAsync(peer)` | `Negotiating` | — |
| `Idle` | 收到对端 TLS 连入 | `Negotiating` | — |
| `Negotiating` | HELLO 收发成功且版本匹配 | `AwaitingDecision` | `PairingCandidateReady(code, peer)` |
| `Negotiating` | TLS 失败 / 协议版本不匹配 / 连接断 | `Failed` | `PairingFailed(reason, detail)` |
| `AwaitingDecision` | 本侧 `ConfirmAsync` + 对端 `PairingConfirm` | `Completed` | `PairingCompleted(result)` |
| `AwaitingDecision` | 任一侧 reject / 超时 / 断线 | `Failed` | `PairingFailed(reason, detail)` |

## 兜底仲裁(同时双向拨号 race)

**正常单击不触发**——任意一台机器的用户点"配对",该机就主动拨号,对端被动接受。指纹大小不影响"谁能发起"。

仲裁只在双方同一瞬间各自点了"配对" 这种极小概率的并发 race 中触发。**规则与现有 `Node` 完全一致**:

> **指纹字典序较小者的 outgoing dial 胜出**(对端会接受);较大者的 outgoing dial 被对端拒绝。等价地:指纹小者扮演拨号方,指纹大者扮演接受方,两端共用同一条 TLS 流。

实现层面:`Connection.PeerFingerprint` 在 TLS 握手完成后填好。若 PairingService 同时持有 (own-outgoing) 和 (incoming) 两条已完成 TLS 的连接指向同一对端:保留 "我=拨号方" 的那条当且仅当 `string.CompareOrdinal(myFp, peerFp) < 0`,否则保留 (incoming) 那条;另一条 Dispose。两端各自独立应用此规则,因 `CompareOrdinal` 全序、对称,结论必定一致。

## 配对码与 HELLO

配对码沿用 `Fingerprint.PairingCode(myFp, peerFp)`(已实现):两边指纹字典序排序后拼接、SHA256、取前 14 bit 模 10000、补 0 到 4 位。对称,两边算出一致。

HELLO 在 TLS 之后第一条发,沿用既有 `HelloMessage { DeviceName, ProtocolVersion }`。PairingService:

- 发出本侧 HELLO 后**必须**等到对端 HELLO 才允许进入 `AwaitingDecision`。
- 对端 HELLO 的 `ProtocolVersion` 不等于本地常量(本期 `1`)→ `PairingFailed(ProtocolMismatch, "peer version=N")`。

## PairingService 公共 API

```csharp
namespace FileTransfer.Core.Pairing;

public sealed class PairingServiceOptions
{
    public required string DeviceName { get; init; }
    public required X509Certificate2 OwnCertificate { get; init; }
    public int UdpPort { get; init; } = 47100;
    public int TcpPort { get; init; } = 47101;
    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan DecisionTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed record PeerCandidate(
    IPAddress Address, int TcpPort, string Fingerprint, string DeviceName);

public sealed record PairingResult(
    string PeerFingerprint, string PeerDeviceName);

public enum PairingFailureReason
{
    LocallyRejected,      // 本方通过 RejectAsync 终止
    PeerRejected,         // 对端发了 PairingReject
    LocalTimeout,         // DecisionTimeout 触发
    TlsHandshakeFailed,   // TLS 握手 / 证书解析错
    ConnectionLost,       // HELLO 后断线
    ProtocolMismatch,     // HELLO.ProtocolVersion 不匹配
}

public enum PairingState
{
    Idle, Negotiating, AwaitingDecision, Completed, Failed
}

public sealed class PairingService : IDisposable
{
    public PairingState State { get; }
    public string OwnFingerprint { get; }   // UI "显示本机指纹" 用

    // 事件在后台线程触发,UI 自行 Dispatch 到 UI 线程
    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string /*pairingCode*/, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string /*detail*/>? PairingFailed;

    public PairingService(PairingServiceOptions options);
    public Task StartAsync();
    public Task RequestPairingAsync(PeerCandidate peer);
    public Task ConfirmAsync();
    public Task RejectAsync(string reason = "");
    public void Stop();
    public void Dispose();
}
```

约束:

- `RequestPairingAsync` 只能在 `Idle` 调,否则抛 `InvalidOperationException`
- `ConfirmAsync` / `RejectAsync` 只能在 `AwaitingDecision` 调
- `PairingCompleted` / `PairingFailed` 抛出后服务自动回到 `Idle` 之外的终态;UI 应 `Dispose` 后另起新实例,而不是复用

## UI 集成流程(下一阶段实施)

```csharp
// 启动(读 AppConfig,PeerFingerprint==null → 走配对路径)
var svc = new PairingService(new PairingServiceOptions {
    DeviceName     = config.DeviceName,
    OwnCertificate = config.GetCertificate(protector),
});
await svc.StartAsync();

svc.PeerDiscovered += peer => UIDispatch(() => Devices.Add(peer));

// 路径 A:用户点设备行的"配对"
async void OnPairClick(PeerCandidate peer) => await svc.RequestPairingAsync(peer);

// 路径 B 同样触发此事件:对端用户先点了"配对",本侧被动接受
svc.PairingCandidateReady += (code, peer) =>
    UIDispatch(() => ShowPairingDialog(code, peer.DeviceName));

async void OnConfirmClick() => await svc.ConfirmAsync();
async void OnRejectClick()  => await svc.RejectAsync();

svc.PairingCompleted += result => UIDispatch(() => {
    config.PeerFingerprint = result.PeerFingerprint;
    config.PeerDeviceName  = result.PeerDeviceName;
    config.Save(path, protector);
    svc.Dispose();
    StartNode(config);
});

svc.PairingFailed += (reason, detail) =>
    UIDispatch(() => ShowError(reason, detail));   // UI 可让用户重试
```

## 测试策略

### PairingServiceTests(loopback,各占独立 UDP/TCP 端口)

| 测试 | 断言 |
|---|---|
| `HappyPath_BothConfirm` | 双方都到 `PairingCompleted`,各自 `PairingResult` 含对方真实指纹与 deviceName;两边曾抛出的 `pairingCode` 完全一致 |
| `PeerRejects_BothFail` | 一方 `RejectAsync` → 本方 `PairingFailed(LocallyRejected)`,对端 `PairingFailed(PeerRejected)` |
| `DecisionTimeout_BothFail` | 用 200 ms 的 `DecisionTimeout`,双方都不点 → 两侧都 `PairingFailed(LocalTimeout)` |
| `ThirdConnection_WhileBusy_IsDropped` | 在 active session 中第三方 PairingService 试图连入 → 现有 session 不受影响,完成正常 |
| `BothDialSimultaneously_ConvergesToOneSession` | 双方几乎同步 `RequestPairingAsync` → 最终仍以单一 candidate 走完(覆盖兜底仲裁) |
| `ProtocolMismatch_Fails` | 用 mock HELLO 注入不同 `ProtocolVersion`(或临时改本地常量)→ 双方 `PairingFailed(ProtocolMismatch)` |

### TlsHandshakeTests(新增项)

| 测试 | 断言 |
|---|---|
| `UnpinnedMode_PopulatesPeerFingerprint` | listener + connector 都传 `peerFingerprint: null`,握手成功后 `Connection.PeerFingerprint` 等于对端证书的 SHA256 hex(大写) |
| 既有钉指纹测试 | 不变,仍 PASS |

## 边界场景

| 场景 | 行为 |
|---|---|
| 协议版本不匹配 | `PairingFailed(ProtocolMismatch, "peer version=N")`,关连接 |
| TLS 握手中断 / 证书无法解析 | `PairingFailed(TlsHandshakeFailed, exception 简短描述)` |
| 双方同时拨号 race | 兜底仲裁(见上节):指纹小者 outgoing 胜出 |
| `Negotiating` / `AwaitingDecision` 中第二个 TLS 连入 | 关掉新连接,active session 不受影响 |
| 用户长时间不点确认/拒绝 | `DecisionTimeout` 后两侧 `PairingFailed(LocalTimeout)` |
| 已发 `PairingConfirm` 后对端掉线 | `PairingFailed(ConnectionLost)` |
| `Dispose` 期间已发起 `RequestPairingAsync` | 内部 CTS 取消,事件不再抛 |
| 接收到非 HELLO/Confirm/Reject 的意外帧 | 丢弃该帧,日志记录;不视为致命错(参考 Core 的 MessageRouter 同款隔离策略) |

## 决策记录

| 选项 | 选了什么 | 为什么 |
|------|---------|--------|
| 配对逻辑位置 | Core 内新增 PairingService,与 Node 平级 | UI 不碰 TLS/socket 细节;loopback 即可端到端单测;Core 模块边界与既有"窄接口"风格一致 |
| Transport 是否复制一份给配对 | 不复制,加 `peerFingerprint` 可空 | 避免 TLS 代码双份维护;差异仅一行 validation callback |
| 谁能发起配对 | 任意一方;指纹小大不影响 | 用户诉求;仲裁只做并发兜底,对正常单击透明 |
| 兜底仲裁方向 | 沿用 Node 现有 `LocalInitiates` 规则:小者拨号,大者接受 | 与既有代码一致,避免"配对完跨入 Node 阶段方向翻转"的认知成本 |
| 配对码长度 | 4 位十进制 | 设计稿已定;碰撞率 1/16384 对家用 LAN MITM 足够 |
| 配对消息是否带 reason payload | 不带,Reject 是空帧 | YAGNI;reason 只在本侧日志/UI 用,跨端无用 |
| 是否引入新 PairingState 字段持久化 | 不持久化 | 配对中断重启即从头走;无中间态可恢复 |
| AnnounceInterval | 配对阶段 2 s(运行阶段 5 s) | 配对时用户在 UI 上等,2 s 比 5 s 体感顺畅;不增加广播负担 |
| DecisionTimeout 默认 | 2 分钟 | 用户两台机器之间走动 + 看码,2 min 充裕;测试用 200 ms 覆盖 |
