# Save 模块初步设计

## 设计目标

`Assets/FRFrame/Core/Save` 作为 FRFrame 的统一存档模块，目标是管理游戏运行期需要持久化到本地的数据，包括设置、游戏进度、玩家数据、子游戏数据、临时缓存和存档槽信息。

你理解的方向是对的：一个完善的 Unity 存档模块通常不只保存“游戏进度”，也会保存：

- 全局设置：音量、画质、语言、帧率、按键配置、隐私授权等。
- 用户数据：账号维度的本地缓存、最近登录角色、引导状态、设备偏好。
- 存档槽：单机或局内进度，例如关卡进度、背包、角色属性、任务状态、地图探索。
- 子游戏数据：当前工程是多 package / 多子游戏结构，每个子游戏可以有自己的存档命名空间。
- 运行缓存：可删除、可重建的本地缓存，例如最近一次下载状态、调试开关。

但这些数据不应该全部混在一个文件或一个 API 里。Save 模块需要明确“存储域”和“生命周期”。

## 参考框架调研结论

### JKFrame SaveSystem

JKFrame 有相对完整的存档系统，主要能力：

- 有 `SaveItem` 概念，表示一个存档槽。
- 支持创建、删除、获取所有存档槽。
- 区分 `saveData` 和 `setting` 两类目录。
- 存档槽内可按文件名保存多个对象。
- 支持缓存已加载对象。
- 支持 Json 或 Binary 序列化。
- 存档系统数据中记录当前 id、存档列表和最后保存时间。

可借鉴点：

- 存档槽 `SaveSlot` / `SaveItem` 设计。
- 全局设置与游戏存档分目录。
- 一个存档槽内按数据块保存多个对象，而不是所有数据塞进一个大文件。
- 保存元数据，例如创建时间、更新时间、显示名、版本。
- 缓存已加载数据，减少频繁 IO。

需要调整点：

- JKFrame 是静态系统，FRFrame 应做成 `SaveManager : IManagerBase`。
- JKFrame 同步 IO 较多，FRFrame 应优先提供 `UniTask` 异步 API。
- BinaryFormatter 风格不适合作为默认方案，FRFrame 应默认 JSON，可选二进制。
- 需要考虑多 package / 子游戏命名空间。

### XHFramework SettingManager

XHFramework 的 `SettingManager` 偏向 PlayerPrefs 设置管理：

- 支持 bool / int / float / string。
- 支持 object 序列化到 string。
- 关闭时 `PlayerPrefs.Save()`。

可借鉴点：

- 轻量设置适合走 Key-Value。
- 全局设置和大型存档文件应分开。

问题：

- 它更像 Setting 模块，不是完整 Save 模块。
- `GetObject<T>` 实现里疑似直接反序列化 settingName，而不是从 PlayerPrefs 读出的字符串，这类设计不能照搬。

### TEngine / I2 PersistentStorage

TEngine 主要提供 PlayerPrefs 增强和文件工具，I2 Localization 的 PersistentStorage 提供 PlayerPrefs 和文件存取抽象。

可借鉴点：

- PlayerPrefs 可用于小型设置。
- 大数据应存文件。
- 文件路径应统一落在 `Application.persistentDataPath`。
- PlayerPrefs 字符串过大时可能遇到平台限制，不适合大型存档。

## 设计边界

Save 模块负责：

- 本地持久化路径管理。
- 全局设置读写。
- 用户级数据读写。
- 存档槽创建、删除、复制、查询。
- 存档槽内数据块读写。
- 序列化、反序列化、版本、迁移入口。
- 缓存、脏标记、自动保存、备份和诊断。

Save 模块不负责：

- 云存档同步。
- 账号登录和用户身份认证。
- 游戏配置表读取，配置表仍属于 Luban / Config 体系。
- 资源加载，资源仍由 Resource 模块负责。
- 业务数据结构定义，例如背包、任务、角色属性由业务模块提供。

后续可以扩展云存档适配层，但 Core 模块首版应先把本地存档做好。

## 核心概念

### SaveDomain

存储域决定数据的生命周期和路径。

```csharp
public enum SaveDomain
{
    GlobalSettings,
    User,
    Slot,
    Cache
}
```

语义：

- `GlobalSettings`：不依赖用户和存档槽，例如音量、语言、画质。
- `User`：依赖当前用户，但不依赖具体存档槽，例如账号本地偏好、已读公告、本地引导记录。
- `Slot`：具体存档槽数据，例如进度、背包、任务、关卡。
- `Cache`：可删除、可重建，不保证长期稳定。

### SaveScope

类似 ResourceScope / AudioScope，用于绑定一组默认上下文。

你的项目是多子游戏结构，因此 Save 模块也建议提供 Scope：

```csharp
using var saveScope = FRFM.Save.CreateScope("Battle", new SaveScopeOptions
{
    PackageName = "BattlePackage",
    UserId = "user_001",
    SlotId = "slot_001"
});

await saveScope.SaveAsync("PlayerProgress", progressData);
PlayerProgress data = await saveScope.LoadAsync<PlayerProgress>("PlayerProgress");
```

Scope 负责默认：

- 子游戏 / package 名。
- 用户 id。
- 存档槽 id。
- 存储域。
- 缓存和脏标记。

这样业务不需要每次都传 userId、slotId、packageName。

### SaveKey

底层唯一标识建议结构化，而不是简单字符串拼接。

```csharp
public readonly struct SaveKey
{
    public SaveDomain Domain { get; }
    public string PackageName { get; }
    public string UserId { get; }
    public string SlotId { get; }
    public string DataName { get; }
}
```

示例：

```text
GlobalSettings / AudioSettings
User / user_001 / GuideState
Slot / BattlePackage / user_001 / slot_001 / PlayerProgress
Cache / BattlePackage / DownloadState
```

### SaveSlot

存档槽元数据。

```csharp
public sealed class SaveSlotInfo
{
    public string SlotId;
    public string DisplayName;
    public string PackageName;
    public string UserId;
    public string AppVersion;
    public int DataVersion;
    public DateTime CreateTime;
    public DateTime UpdateTime;
    public string PreviewImagePath;
    public Dictionary<string, string> Tags;
}
```

用途：

- 存档列表显示。
- 排序。
- 判断版本兼容。
- 复制、删除、备份。
- 存档封面图或简略状态展示。

## 推荐目录结构

建议所有数据都在：

```text
Application.persistentDataPath/FRFrameSave
```

内部结构：

```text
FRFrameSave
├── GlobalSettings
│   ├── AudioSettings.json
│   ├── GraphicsSettings.json
│   └── InputSettings.json
├── Users
│   └── user_001
│       ├── UserProfile.json
│       ├── GuideState.json
│       └── Packages
│           └── BattlePackage
│               └── UserBattleState.json
├── Slots
│   └── user_001
│       └── BattlePackage
│           ├── slot_001
│           │   ├── SlotMeta.json
│           │   ├── PlayerProgress.json
│           │   ├── Inventory.json
│           │   ├── QuestState.json
│           │   └── WorldState.json
│           └── slot_002
└── Cache
    └── BattlePackage
        └── DownloadState.json
```

这种结构适合多 package / 子游戏：每个子游戏的数据天然隔离，但公共设置和用户数据仍可共享。

## 序列化策略

首版推荐默认 JSON。

原因：

- 可读，便于开发期排查。
- 适合框架调试和版本迁移。
- 对游戏进度、设置、玩家数据都足够通用。

建议抽象接口：

```csharp
public interface ISaveSerializer
{
    string FileExtension { get; }
    byte[] Serialize<T>(T value);
    T Deserialize<T>(byte[] bytes);
}
```

默认实现：

- `JsonSaveSerializer`：默认。
- 后续可扩展 `BinarySaveSerializer`。
- 后续可扩展 `EncryptedSaveSerializer` 或包装层。

注意：Unity `JsonUtility` 对 Dictionary、多态、复杂结构支持弱。若工程已有 Newtonsoft.Json，建议默认用 Newtonsoft.Json；如果不想引入依赖，首版可以提供接口并先使用 JsonUtility，但要在文档里说明限制。

## 写入策略

### 原子写入

存档文件不能直接覆盖写入，避免写到一半崩溃导致主存档损坏。

推荐流程：

```text
Serialize data
    ↓
写入 xxx.tmp
    ↓
如果 xxx 存在，复制为 xxx.bak
    ↓
用 tmp 替换正式文件
    ↓
删除 tmp
```

读取时：

```text
优先读取正式文件
    ↓
失败则读取 .bak
    ↓
仍失败则返回默认值或失败结果
```

### 脏标记与自动保存

对于频繁变化的数据，不建议每次变化都立刻写磁盘。

设计：

```csharp
saveScope.SetDirty("PlayerProgress", progressData);
await saveScope.FlushAsync();
```

自动保存策略：

- 间隔 N 秒自动 Flush。
- 应用切后台时 Flush。
- 游戏退出或模块 Destroy 时 Flush。
- 关键节点手动 Flush，例如通关、获得重要道具、充值结果落地。

## API 设计草案

### SaveManager

```csharp
public sealed class SaveManager : IManagerBase
{
    public override int Priority => 30;

    public SaveScope CreateScope(string name, SaveScopeOptions options = null);

    public UniTask SaveAsync<T>(SaveKey key, T data, SaveWriteOptions options = null);
    public UniTask<SaveLoadResult<T>> LoadAsync<T>(SaveKey key, SaveLoadOptions options = null);
    public UniTask<bool> ExistsAsync(SaveKey key);
    public UniTask<bool> DeleteAsync(SaveKey key);

    public UniTask SaveSettingAsync<T>(string name, T data);
    public UniTask<T> LoadSettingAsync<T>(string name, T defaultValue = default);

    public UniTask<SaveSlotInfo> CreateSlotAsync(string packageName, string userId, string displayName = null);
    public UniTask<IReadOnlyList<SaveSlotInfo>> GetSlotsAsync(string packageName, string userId);
    public UniTask<bool> DeleteSlotAsync(string packageName, string userId, string slotId);
    public UniTask<bool> CopySlotAsync(string packageName, string userId, string sourceSlotId, string targetSlotId);
}
```

Priority 建议为 30：

- 不强依赖 Resource。
- 但通常希望 Log、Event、Pool、Fsm、Resource、Audio、UI 都可以先初始化。
- UI 设置界面可能使用 Save，实际可在 UI 初始化后调用。

如果后续 UI 初始化时需要读取设置，也可以把 Save 提前到 12 或 15。首版文档建议讨论后再定。

### SaveScope

```csharp
public sealed class SaveScope : IDisposable
{
    public string Name { get; }
    public string PackageName { get; }
    public string UserId { get; }
    public string SlotId { get; }

    public UniTask SaveAsync<T>(string dataName, T data, SaveDomain domain = SaveDomain.Slot);
    public UniTask<T> LoadAsync<T>(string dataName, T defaultValue = default, SaveDomain domain = SaveDomain.Slot);
    public void SetDirty<T>(string dataName, T data, SaveDomain domain = SaveDomain.Slot);
    public UniTask FlushAsync();
    public UniTask<bool> DeleteAsync(string dataName, SaveDomain domain = SaveDomain.Slot);
    public void Dispose();
}
```

### 设置快捷 API

轻量设置可以提供便捷 API：

```csharp
FRFM.Save.SetInt("FrameRate", 60);
FRFM.Save.GetInt("FrameRate", 60);
FRFM.Save.SetFloat("MusicVolume", 0.8f);
FRFM.Save.GetFloat("MusicVolume", 1f);
FRFM.Save.SetBool("Vibration", true);
FRFM.Save.GetBool("Vibration", true);
FRFM.Save.SavePrefs();
```

但建议内部仍统一走 Save 模块命名空间，避免直接散落 PlayerPrefs key。

## 典型使用场景

### 保存全局设置

```csharp
await FRFM.Save.SaveSettingAsync("AudioSettings", new AudioSettingsData
{
    MasterVolume = 0.8f,
    MusicVolume = 0.6f,
    UISoundVolume = 1f
});
```

### 加载全局设置

```csharp
AudioSettingsData settings = await FRFM.Save.LoadSettingAsync(
    "AudioSettings",
    AudioSettingsData.Default);
```

### 创建子游戏存档槽

```csharp
SaveSlotInfo slot = await FRFM.Save.CreateSlotAsync("BattlePackage", userId, "第一章");
```

### 使用 Scope 保存游戏进度

```csharp
using var scope = FRFM.Save.CreateScope("Battle", new SaveScopeOptions
{
    PackageName = "BattlePackage",
    UserId = userId,
    SlotId = slot.SlotId
});

await scope.SaveAsync("PlayerProgress", progress);
await scope.SaveAsync("Inventory", inventory);
```

### 读取进度

```csharp
PlayerProgress progress = await scope.LoadAsync("PlayerProgress", PlayerProgress.CreateDefault());
```

### 脏标记批量保存

```csharp
scope.SetDirty("PlayerProgress", progress);
scope.SetDirty("QuestState", questState);
await scope.FlushAsync();
```

## 数据版本与迁移

存档模块需要考虑版本升级。

建议每个数据块可选实现：

```csharp
public interface ISaveVersionedData
{
    int Version { get; }
}
```

模块级迁移接口：

```csharp
public interface ISaveMigration
{
    string DataName { get; }
    int FromVersion { get; }
    int ToVersion { get; }
    object Migrate(object oldData);
}
```

首版可以只保留迁移入口，不急着实现复杂迁移链。至少 `SaveSlotInfo` 要记录整体 `DataVersion` 和 `AppVersion`。

## 安全与防篡改

存档是否需要加密取决于项目类型。

建议分层：

1. 首版默认 JSON 明文，便于开发。
2. 提供可插拔 `ISaveProcessor`：压缩、加密、校验。
3. 需要防篡改时加入 hash / HMAC。
4. 不把密钥硬编码成“安全保证”，客户端本地密钥只能提高篡改成本，不能绝对防破解。

可选接口：

```csharp
public interface ISaveProcessor
{
    byte[] BeforeWrite(SaveKey key, byte[] bytes);
    byte[] AfterRead(SaveKey key, byte[] bytes);
}
```

## 缓存策略

建议 `SaveManager` 内部维护缓存：

```text
SaveKey -> object
SaveKey -> dirty state
SaveKey -> last load/write time
```

规则：

- `LoadAsync` 命中缓存直接返回。
- `SaveAsync` 写磁盘成功后更新缓存。
- `SetDirty` 只更新缓存和 dirty 标记。
- `FlushAsync` 批量写 dirty 数据。
- 删除数据时同步移除缓存。

## 事件与诊断

### 事件

建议提供：

- `SaveStarted`
- `SaveCompleted`
- `SaveFailed`
- `LoadStarted`
- `LoadCompleted`
- `LoadFailed`
- `SlotCreated`
- `SlotDeleted`
- `DirtyChanged`

首版可先用 C# event，后续需要跨模块广播再接 `FRFM.Event`。

### 诊断快照

`SaveDiagnosticsSnapshot` 建议包含：

- 根目录路径。
- 当前用户 id。
- 存档槽数量。
- 缓存数量。
- dirty 数量。
- 最近保存 / 加载记录。
- 写入失败次数。
- 备份恢复次数。
- 各 domain 占用空间。

Editor 窗口后续可提供：

- 打开存档目录。
- 查看存档槽列表。
- 查看 dirty 数据。
- 手动 Flush。
- 删除 Cache。
- 导出指定 Slot。

## 与其它模块关系

### Audio

Audio 设置可保存到 `GlobalSettings/AudioSettings`，例如音量、静音状态。

### UI

设置界面可以读写 Save 模块。UI 打开时不应直接读写 PlayerPrefs。

### Resource

Save 模块不保存资源对象，只保存资源引用信息，例如 itemId、resourceLocation、packageName。资源实际加载仍由 Resource 模块处理。

### Event

存档完成、加载失败、槽位变化可通过事件通知 UI 或业务系统。

### Log

所有读写失败、备份恢复、迁移失败应通过 `FRLog` 输出。

## 初步结论

FRFrame Save 模块建议采用：

```text
SaveManager + SaveScope + SaveKey + SaveSlotInfo + Serializer + Atomic File Storage
```

总体设计建议：

- 全局设置、用户数据、存档槽数据、缓存数据分 domain。
- 多子游戏数据通过 `PackageName` 隔离。
- 业务优先使用 `SaveScope`，像 AudioScope 一样减少重复传参。
- 存档槽内按数据块保存，而不是所有业务数据塞进一个大文件。
- 默认 JSON + 原子写入 + bak 备份。
- 提供缓存和 dirty/Flush，避免频繁 IO。
- 首版先做本地存档，不急着做云同步。

这套结构可以覆盖设置、游戏进度、玩家数据，也能适配你当前“一个子游戏一个 package”的整体设计。
