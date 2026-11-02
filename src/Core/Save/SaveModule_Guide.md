# Save 模块使用指南

## 模块入口

Save 模块通过 `FRFM.Save` 使用：

```csharp
await FRFM.Save.SaveSettingAsync("AudioSettings", audioSettings);
AudioSettingsData settings = await FRFM.Save.LoadSettingAsync("AudioSettings", AudioSettingsData.Default);
```

模块默认把数据写入：

```text
Application.persistentDataPath/FRFrameSave
```

默认序列化器为 Newtonsoft.Json，文件格式为可读 JSON。

## 推荐用法：SaveScope

当前工程偏向“一个子游戏一个 package”，业务内推荐创建 `SaveScope`，避免每次传 `packageName`、`userId`、`slotId`：

```csharp
SaveSlotInfo slot = await FRFM.Save.CreateSlotAsync("BattlePackage", userId, "第一章");

using SaveScope saveScope = FRFM.Save.CreateScope("Battle", new SaveScopeOptions
{
    PackageName = "BattlePackage",
    UserId = userId,
    SlotId = slot.SlotId
});

await saveScope.SaveAsync("PlayerProgress", progress);
PlayerProgress loaded = await saveScope.LoadAsync("PlayerProgress", PlayerProgress.CreateDefault());
```

Scope 默认写入 `SaveDomain.Slot`，适合关卡进度、背包、任务、地图探索等存档槽数据。

## 全局设置

全局设置不绑定用户和存档槽：

```csharp
await FRFM.Save.SaveSettingAsync("GraphicsSettings", graphicsSettings);
GraphicsSettingsData data = await FRFM.Save.LoadSettingAsync("GraphicsSettings", GraphicsSettingsData.Default);
```

适合音量、语言、画质、帧率、输入配置等。

## 用户数据

用户数据绑定用户，但不绑定具体存档槽：

```csharp
await FRFM.Save.SaveUserAsync(userId, "GuideState", guideState);
GuideState state = await FRFM.Save.LoadUserAsync(userId, "GuideState", GuideState.Default);
```

如果用户数据只属于某个子游戏，可以传 package：

```csharp
await FRFM.Save.SaveUserAsync(userId, "UserBattleState", state, "BattlePackage");
```

## 脏标记和批量保存

频繁变化的数据不要每帧写磁盘，先标记 dirty，再在关键节点 Flush：

```csharp
saveScope.SetDirty("PlayerProgress", progress);
saveScope.SetDirty("QuestState", questState);
await saveScope.FlushAsync();
```

`FlushAsync` 会把所有 dirty 数据批量写入磁盘，并清理 dirty 状态。

## 存档槽

创建、查询、删除、复制存档槽：

```csharp
SaveSlotInfo slot = await FRFM.Save.CreateSlotAsync("BattlePackage", userId, "第一章");
IReadOnlyList<SaveSlotInfo> slots = await FRFM.Save.GetSlotsAsync("BattlePackage", userId);
await FRFM.Save.CopySlotAsync("BattlePackage", userId, slot.SlotId, "backup_slot");
await FRFM.Save.DeleteSlotAsync("BattlePackage", userId, slot.SlotId);
```

每个槽位会保存 `SlotMeta.json`，包含显示名、用户、package、创建时间、更新时间、版本和标签。

## 存储域

`SaveDomain` 决定路径和生命周期：

```csharp
public enum SaveDomain
{
    GlobalSettings,
    User,
    Slot,
    Cache
}
```

Scope 中可临时指定 domain：

```csharp
await saveScope.SaveAsync("DownloadState", downloadState, SaveDomain.Cache);
```

## 直接使用 SaveKey

框架底层 API 使用结构化 `SaveKey`：

```csharp
SaveKey key = new SaveKey(
    SaveDomain.Slot,
    "Inventory",
    packageName: "BattlePackage",
    userId: userId,
    slotId: slotId);

await FRFM.Save.SaveAsync(key, inventory);
SaveLoadResult<InventoryData> result = await FRFM.Save.LoadAsync<InventoryData>(key);
```

`SaveLoadResult<T>` 会返回是否成功、是否来自缓存、是否来自 `.bak` 备份以及错误信息。

## 原子写入和备份

Save 模块写入流程为：

```text
serialize -> 写入 .tmp -> 备份旧文件为 .bak -> 替换正式文件
```

读取正式文件失败时允许回退 `.bak`。默认 `SaveLoadOptions.AllowBackup = true`。

## 诊断

运行时可获取诊断快照：

```csharp
SaveDiagnosticsSnapshot snapshot = FRFM.Save.CreateSnapshot();
Debug.Log(snapshot.RootPath);
Debug.Log(snapshot.DirtyCount);
```

Editor 菜单提供诊断窗口：

```text
FRFrame/Diagnostics/Save Diagnostics
```

窗口可查看根目录、缓存数量、dirty 数量、失败次数，并支持手动 Flush。
