# FRFrame Network 选型结论与验证计划

> 状态：选型方向已确认，具体依赖以 POC 通过为准  
> 更新日期：2026-11-23  
> 适用平台：Unity Android / iOS 优先，Windows 兼顾  
> 服务端：独立 .NET 10 进程，不依赖 Unity Runtime

## 1. 已确认的架构约束

FRFrame Network 的目标是“通用与多人平衡”：既能支撑普通业务联网，也能为实时多人玩法提供可扩展能力；不把整个网络模块绑定到某一个实时同步框架。

### 1.1 服务端硬约束

**服务端必须是独立 .NET 10 Server，不能依赖 Unity Runtime。**

允许的服务端形态：

- .NET 10 Console / Worker Service。
- ASP.NET Core Host（需要 HTTP API 时）。
- 未来拆分为 Gate、Room、Battle 等独立 .NET 进程。

不接受：

- 以 Unity Dedicated Server 作为唯一服务端形态。
- 服务端核心依赖 UnityEngine、MonoBehaviour、PlayerLoop 或 Unity Package Runtime。
- 为了复用客户端代码而把 Unity Runtime 带进服务端。

### 1.2 双端共享边界

从第一天开始共享：

- 协议定义、DTO、OpCode、错误码和协议版本。
- 纯 C# 消息编解码。
- 与 Unity 无关的校验、规则和可复用领域逻辑。
- 需要客户端预测与服务端一致执行的纯模拟逻辑（按具体玩法决定）。

明确不共享：

- UnityEngine 类型和 MonoBehaviour 生命周期。
- Unity UI、资源、场景、表现层代码。
- Unity 主线程调度器和 FRFrame Unity Manager。
- 服务端进程生命周期、数据库和运维设施。

共享的目标是“同一份协议和纯 C# 契约”，不要求客户端与服务端所有运行时代码完全相同。

---

## 2. 当前选型结论

| 层次 | 当前结论 | 状态 |
|---|---|---|
| 服务端 Runtime | .NET 10 LTS | **已确认** |
| UDP Transport | LiteNetLib 2.1.5 | **首选候选，进入 POC** |
| Unity Transport | 不作为核心 Transport | **从主线排除** |
| 协议/序列化 | Protobuf 与 MessagePack-CSharp v4 做小型对照 | **尚未冻结** |
| MemoryPack | 不用于公网不可信输入 | **排除默认网络方案** |
| 实时同步 | LiteEntitySystem 可选适配模块 | **不进入 Network Core** |
| HTTP | UnityWebRequest 与 HttpClient 分别实现 | **采用分离实现** |

### 2.1 为什么首选 LiteNetLib

LiteNetLib 2.1.5 的官方项目目标框架为 net8.0;netstandard2.1，官方 README 声明支持 Unity 2021.2+，包括 Android、iOS 和桌面平台。2.1.5 发布记录将项目标记为 AOT compatible。

- 仓库与发布记录：[LiteNetLib](https://github.com/RevenantX/LiteNetLib)
- 2.1.5 Release：[2.1.5](https://github.com/RevenantX/LiteNetLib/releases/tag/2.1.5)
- NuGet：[LiteNetLib](https://www.nuget.org/packages/LiteNetLib/)

它提供：

- Reliable Ordered / Reliable Unordered / Reliable Sequenced。
- Unreliable Sequenced 和普通 UDP。
- 多通道、自动分片、MTU 检测。
- IPv4 / IPv6。
- NAT hole punching。
- 丢包与延迟模拟。
- 连接统计。
- 局域网 Multicast 发现。

这些能力适合作为 FRFrame 的底层数据报传输，而无需把 RPC、业务消息或实体同步直接交给第三方框架。

### 2.2 .NET 10 兼容性注意

LiteNetLib 2.1.5 当前项目文件声明目标为 net8.0;netstandard2.1，没有单独的 net10.0 Target Framework。

这不等于它不能用于 .NET 10；但在正式冻结依赖前必须通过 .NET 10 Console Server 完成：

1. NuGet restore。
2. 编译与运行。
3. 连接、收发、断线和重连。
4. 长时间运行与多连接测试。

Unity 客户端按 LiteNetLib 官方建议，优先采用源码或 OpenUPM 集成，不要仅以预编译 DLL 的 Editor 编译结果作为兼容性证明。

**AOT compatible 标记不等于 Android/iOS IL2CPP 真机已验证。** 两个平台必须分别构建和运行。

### 2.3 Unity Transport 的定位调整

Unity Transport 2.7 的安全连接、DTLS 和 UDP 网络迁移能力值得关注，但它是 Unity 生态中的 Transport Package，当前没有满足本项目“纯 .NET 10 Server、不依赖 Unity Runtime”的已验证证据。

因此：

- 不将 Unity Transport 作为 FRFrame Network Core 的主 Transport。
- 不为了使用 Unity Transport 引入 Unity Dedicated Server。
- 未来如果出现独立、无 Unity Runtime 的服务端适配实现，再单独评估；在此之前不把它列为必须支持的后端。

---

## 3. 协议与序列化

协议方案不能只按 Benchmark 排名决定，还要看不可信输入处理、协议演进、IL2CPP、代码生成和跨端共享。

### 3.1 Protobuf：稳定协议候选

- 官方项目：[Protocol Buffers](https://protobuf.dev/)
- C# 生成代码：[C# Generated Code Guide](https://protobuf.dev/reference/csharp/csharp-generated/)

优点：

- .proto 是明确、可审查和可版本控制的协议源。
- 字段编号和保留规则适合长期演进。
- 通过代码生成得到客户端和服务端共享的 C# 消息类型。
- 适合未来增加其他语言的服务端、工具和测试客户端。

待验证：

- Unity 6000 Android / iOS IL2CPP。
- .NET 10 服务端。
- 新旧版本字段兼容。
- 未知字段、非法输入和超大消息的处理。

Google 官方资料没有把 Unity 作为一等支持平台，因此不能跳过 IL2CPP POC。

### 3.2 MessagePack-CSharp v4：高性能候选

- 项目：[MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)
- v4 分支：[v4 README](https://github.com/MessagePack-CSharp/MessagePack-CSharp/tree/v4)
- 当前 Release Candidate：[v4.0.0-rc.1](https://github.com/MessagePack-CSharp/MessagePack-CSharp/releases/tag/v4.0.0-rc.1)

v4 的方向是安全优先、性能优先和 Schema 演进优先；项目文件包含 netstandard2.0、netstandard2.1、net9.0、net10.0、net11.0 等目标，并提供 Source Generator / AOT 相关支持。

但截至本报告更新日期，v4 仍是 Release Candidate。它适合进入 POC，不应仅凭发布说明或 Benchmark 直接作为生产依赖冻结。

Unity 侧需要验证源生成、IL2CPP、泛型集合和真实消息类型。服务端侧需要验证 .NET 10 构建、AOT 选项和安全限制。

### 3.3 MemoryPack：公网协议排除

MemoryPack 官方 README 当前明确不再建议将其用于来自不可信来源的数据，例如公网端点或用户提供的文件，并建议这类场景考虑 MessagePack for C# v4。

- [MemoryPack 官方 README](https://github.com/Cysharp/MemoryPack)

因此：

- 不将 MemoryPack 作为公网客户端消息的默认反序列化方案。
- 可以在可信进程内部或受控数据场景另行评估。
- 不能因为网络层加密就信任客户端输入；合法客户端也可能主动构造恶意消息。

### 3.4 当前决策

**不在本阶段冻结唯一序列化器。**

用同一组协议定义和测试消息，对 Protobuf 与 MessagePack v4 RC 做小型对照。若 MessagePack v4 正式稳定且通过 Unity IL2CPP / .NET 10 测试，再决定是否采用；若需要最明确的 schema-first 多语言契约，Protobuf 仍然是优先方案。

高频实时输入与实体状态可以使用紧凑专用结构或实时同步框架自身的编码，不必强制所有消息走同一序列化器。

---

## 4. 推荐模块边界

~~~
FRFrame.Network
├── Abstractions
│   ├── ITransport
│   ├── IConnection
│   ├── TransportEvent
│   └── Channel / Delivery
├── Protocol
│   ├── ProtoFiles
│   ├── Generated
│   ├── OpCode
│   ├── ErrorCode
│   └── ProtocolVersion
├── Messaging
│   ├── Dispatcher
│   ├── RequestResponse
│   ├── Notification
│   └── TimeoutCancellation
├── Transport.LiteNetLib
├── Http
│   ├── UnityWebRequestClient
│   └── HttpAbstractions
├── UnityAdapter
│   ├── MainThreadDispatch
│   └── PlayerLoop integration
└── Realtime (optional)
    └── LiteEntitySystem adapter

Server (.NET 10)
├── Host
├── Network
├── Protocol (shared)
├── Application
└── Infrastructure
~~~

建议把共享协议和纯 C# 核心放在可被 Unity 与 .NET 10 同时引用的独立程序集/项目中。Unity 侧可使用 netstandard2.1 兼容资产；服务端以 net10.0 为主目标。具体 Target Framework 布局以 POC 验证为准。

Network Core 不应依赖：

- UnityEngine。
- MonoBehaviour。
- Unity Transport。
- LiteEntitySystem。
- Fantasy。
- UI、资源、场景和表现层。

Transport 负责连接和数据报收发；Messaging 负责消息路由、RequestId、超时与取消；Protocol 负责契约和编解码。HTTP 是独立请求/响应通道，不伪装成 UDP/TCP Transport。

---

## 5. 通道与可靠性原则

不要把所有业务流量塞进同一个 Reliable Ordered 通道。

| 流量 | 建议语义 | 原因 |
|---|---|---|
| 登录后会话控制、关键指令 | Reliable Ordered | 需要可靠、有序处理 |
| RPC 请求/响应 | Reliable | RequestId 配对、超时与取消 |
| 聊天和系统通知 | Reliable，可独立通道 | 不应阻塞实时输入 |
| 高频玩家输入 | Unreliable Sequenced 或专用输入协议 | 新输入通常比过期输入更有价值 |
| 远端状态快照 | Unreliable Sequenced | 最新快照优先 |
| 大资源、回放、文件 | 独立分块通道或 HTTP | 避免阻塞实时流 |

UDP 数据报天然有消息边界，不要机械套用 TCP 的长度前缀。若未来增加 TCP/WebSocket 字节流 Transport，必须另行定义帧边界、长度限制和分片策略。

---

## 6. 安全要求

LiteNetLib 提供可选 CRC32C 校验，但校验和不等于加密、身份认证或防篡改。

生产方案必须另行确定经过审查的安全协议/实现，并覆盖：

- 服务器身份验证。
- 会话密钥协商。
- AEAD 加密与完整性认证。
- 防重放。
- 连接 Token、会话过期和权限验证。
- 包大小、频率和资源消耗限制。
- OpCode 白名单与业务参数校验。

不要把 LiteNetLib 示例中的 AES-CBC Layer 直接作为生产安全层；不要自行拼凑密码学协议。安全机制是 POC 通过前的阻断项，不是“后续再补”的普通优化项。

---

## 7. HTTP 边界

Unity 客户端使用 UnityWebRequest；独立 .NET 10 服务端使用 HttpClient / ASP.NET Core。

HTTP 适合：

- 登录、账号与平台认证。
- 商店、支付、活动等请求/响应业务。
- 大资源下载。
- 管理和运维 API。

LiteNetLib 长连接适合：

- 会话内消息。
- 房间消息。
- 低延迟推送。
- 高频输入和状态同步。

双方可以共享 DTO、错误码、日志字段和协议约定，但不需要共用一个 Transport 接口。

---

## 8. 实时同步框架的边界

LiteEntitySystem 保留为可选实时同步适配层，不进入 Network Core。

- 项目：[LiteEntitySystem](https://github.com/RevenantX/LiteEntitySystem)
- 它提供客户端预测、预测实体、延迟补偿、插值、SyncVar、RPC 和 SyncGroups。
- 默认 Transport 使用 LiteNetLib 2.x，也允许自定义 Transport。
- 当前官方限制文档定义 MaxPlayers = 254（服务器 ID 0，客户端 ID 1–254）。
- MaxSyncedEntityCount = 64000 是同步实体上限，不是玩家数。
- SyncGroups 是按玩家/实体控制特定同步字段和 RPC 可见性的机制，不等于完整 AOI 空间索引。

只有在某个玩法确实需要预测、回滚、实体状态同步时，才引入该适配层。普通业务联网不应被迫依赖它。

---

## 9. POC 验收清单

在正式实现 Network Core 前，先做一个最小双端验证工程。

### 9.1 构建与互通

- [ ] .NET 10 Console Server 能 restore、build、run。
- [ ] Unity 6000 Editor 客户端能连接。
- [ ] Windows Standalone 客户端能连接。
- [ ] Android IL2CPP 真机能连接。
- [ ] iOS IL2CPP 真机能连接。
- [ ] 同一份协议定义可供 Unity 与 .NET 10 使用。
- [ ] Unity → Server、Server → Unity 双向编解码一致。
- [ ] 新旧协议版本兼容测试通过。

### 9.2 网络行为

- [ ] 连接、断开、超时、重连。
- [ ] Reliable Ordered / Reliable Unordered / Sequenced / Unreliable 行为。
- [ ] MTU、分片与超大包拒绝策略。
- [ ] IPv4、IPv6、IPv6-only。
- [ ] NAT 环境。
- [ ] WiFi ↔ 蜂窝网络切换。
- [ ] App 后台 10 秒、30 秒、2 分钟、5 分钟后恢复。
- [ ] 服务端重启后客户端恢复策略。
- [ ] 长时间运行和多连接稳定性。

### 9.3 弱网与性能

- [ ] 延迟：50 / 100 / 200 / 300 ms。
- [ ] 丢包：1% / 5% / 10% / 20%。
- [ ] 抖动、突发丢包、带宽限制。
- [ ] RTT P50 / P95 / P99。
- [ ] 收发吞吐、包体大小、CPU、内存和 GC Alloc。
- [ ] 重传量、连接建立时间、断线恢复时间。
- [ ] 服务端多连接下的内存和 CPU 曲线。

### 9.4 安全阻断项

- [ ] 明确服务器身份验证与密钥协商。
- [ ] AEAD 与防重放。
- [ ] 认证失败、过期 Token、重放包测试。
- [ ] 非法 OpCode、超大包、畸形数据测试。
- [ ] 频率限制与连接资源上限。

POC 结果必须记录设备型号、OS、Unity 版本、.NET SDK、包版本、测试参数和原始数据。没有真机和弱网数据，不给出性能胜负结论。

---

## 10. 实施顺序

1. **锁定服务端工程边界**：独立 .NET 10 Console/Worker，不引用 Unity。
2. **LiteNetLib POC**：先验证 Unity ↔ .NET 10 双端连接、收发和生命周期。
3. **协议 POC**：Protobuf 与 MessagePack v4 做同消息对照；完成 IL2CPP 和 .NET 10 测试。
4. **移动网络 POC**：后台恢复、网络切换、IPv6-only、NAT、弱网。
5. **安全方案评审**：在正式上线前完成，不自行实现密码学原语。
6. **再冻结 Network Core API**：基于 POC 决定接口和通道，不提前设计过度抽象。

## 11. 官方资料与源码

- [FRFrame Network 主仓库](https://github.com/fraisty/FraistyGameFrame)
- [LiteNetLib 源码](https://github.com/RevenantX/LiteNetLib)
- [LiteNetLib 2.1.5 Release](https://github.com/RevenantX/LiteNetLib/releases/tag/2.1.5)
- [LiteNetLib NuGet](https://www.nuget.org/packages/LiteNetLib/)
- [LiteEntitySystem](https://github.com/RevenantX/LiteEntitySystem)
- [LiteEntitySystem SyncGroups](https://github.com/RevenantX/LiteEntitySystem/blob/master/docfx_project/articles/sync/sync-groups.md)
- [LiteEntitySystem Limits](https://github.com/RevenantX/LiteEntitySystem/blob/master/docfx_project/articles/integration/limits.md)
- [Google Protocol Buffers](https://protobuf.dev/)
- [Protobuf C# Generated Code](https://protobuf.dev/reference/csharp/csharp-generated/)
- [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)
- [MessagePack-CSharp v4 Release Candidate](https://github.com/MessagePack-CSharp/MessagePack-CSharp/releases/tag/v4.0.0-rc.1)
- [MessagePack-CSharp v4 README](https://github.com/MessagePack-CSharp/MessagePack-CSharp/tree/v4)
- [MemoryPack](https://github.com/Cysharp/MemoryPack)
- [.NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy)

---

> 本文档记录的是选型方向和验证计划，不代表 Android/iOS 真机、弱网或性能 POC 已通过。LiteNetLib 是当前首选候选，只有通过上述验收后才冻结为生产依赖。
