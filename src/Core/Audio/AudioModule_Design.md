# Audio 模块详细设计

## 设计目标

`Assets/FRFrame/Core/Audio` 是 FRFrame 的统一音频模块，负责游戏运行期的音乐、环境音、普通音效、UI 音效和语音播放。

模块目标不是简单封装 Unity `AudioSource`，而是提供完整的音频生命周期、资源生命周期、分组混音、通道调度、多 package 语义和诊断能力。

总体目标：

- 通过 `FRFM.Audio` 统一访问音频能力。
- 支持 `Music`、`Ambience`、`Sound`、`UISound`、`Voice` 五类音频。
- 支持 AudioMixer 分类音量、静音、总音量控制。
- 支持异步加载 AudioClip，并在播放结束、停止、抢占、销毁时释放资源引用。
- 支持 `AudioScope`，为“一个子游戏一个 package”的项目提供默认 package 和生命周期边界。
- 支持 2D / 3D 音效、固定世界坐标和 Transform 跟随播放。
- 支持有限通道池、优先级抢占、淡入淡出、暂停恢复。
- 支持运行时诊断快照，方便排查音频播放和资源释放问题。

## 参考框架结论

### TEngine

TEngine 的音频模块采用“模块 - 分类 - 代理”结构：

- `AudioModule` 负责模块生命周期、AudioMixer、资源加载和全局控制。
- `AudioCategory` 按音频类型划分，每类维护多个 `AudioAgent`。
- `AudioAgent` 封装单个 `AudioSource`、播放状态、资源句柄和淡出逻辑。
- `AudioGroupConfig` 配置分类音量、通道数和 3D 衰减参数。

FRFrame 借鉴其分类、Mixer、Agent 池和状态驱动设计，但改为适配 `IManagerBase`、`ResourceKey` 和 `ResourceHandle<AudioClip>`。

### XHFramework

XHFramework 的音频系统强调播放 id、播放参数和优先级抢占。

FRFrame 借鉴其：

- 每次播放生成唯一 id。
- 播放返回 `AudioHandle`。
- 使用 `AudioPlayOptions` 承载播放参数。
- 通道不足时按 priority 选择可抢占 Agent。
- 加载中的声音可以被停止，加载完成后不再播放。

### JKFrame

JKFrame 的音频 API 简单，BGM 和 OneShot 易用。

FRFrame 借鉴其业务便捷性：

- `PlayMusicAsync`
- `PlaySoundAsync`
- `PlayUISoundAsync`
- BGM 默认支持淡出旧音乐、淡入新音乐。

## 架构总览

模块采用四层结构：

```text
AudioManager
  ├── AudioScope
  ├── AudioCategory
  │     └── AudioAgent
  └── AudioDiagnosticsSnapshot
```

职责划分：

- `AudioManager`：模块入口，负责初始化、播放 API、音量控制、预加载、诊断和销毁。
- `AudioScope`：绑定默认 package 和生命周期，适合子游戏、战斗、关卡、剧情等业务边界。
- `AudioCategory`：音频分类，管理一组 AudioAgent，处理分类音量、静音、暂停和抢占。
- `AudioAgent`：单个 AudioSource 播放代理，处理播放、淡入淡出、Transform 跟随、资源释放。
- `AudioHandle`：播放句柄，业务通过它停止、暂停、恢复、调节单个声音。
- `AudioPlayOptions`：播放参数集合。

## 目录结构

```text
Assets/FRFrame/Core/Audio
├── Runtime
│   ├── AudioAgent.cs
│   ├── AudioAgentState.cs
│   ├── AudioCategory.cs
│   ├── AudioDiagnosticsSnapshot.cs
│   ├── AudioEvents.cs
│   ├── AudioGroupConfig.cs
│   ├── AudioHandle.cs
│   ├── AudioManager.cs
│   ├── AudioModuleConfig.cs
│   ├── AudioPlayOptions.cs
│   ├── AudioScope.cs
│   ├── AudioStopReason.cs
│   └── AudioType.cs
├── Editor
├── AudioModule_Design.md
└── AudioModule_Guide.md
```

## 模块生命周期

### 初始化

`AudioManager` 继承 `IManagerBase`：

```csharp
public sealed class AudioManager : IManagerBase
{
    public override int Priority => 15;
}
```

优先级为 15，原因：

- `ResourceManager.Priority = 10`，Audio 依赖 Resource 加载 AudioClip，因此必须晚于 Resource。
- `UIManager.Priority = 20`，UI 初始化或打开后可能立即播放 UI 音效，因此 Audio 应早于 UI。

初始化流程：

```text
FRFM.RegisterAllManagers()
    ↓
注册 ResourceManager、AudioManager、UIManager
    ↓
ResourceManager 初始化完成
    ↓
AudioManager.InitializeAsync()
    ↓
等待 ResourceManager ready
    ↓
创建 FRFrameObject/Audio 根节点
    ↓
加载 Resources/FRFrameAudioConfig
    ↓
创建各 AudioCategory 和预分配 AudioAgent
    ↓
应用 MasterVolume 和分类音量
```

如果 `Resources/FRFrameAudioConfig` 不存在，会创建运行时默认配置，保证模块可用。

### 更新

`AudioManager.Update` 每帧驱动全部 `AudioCategory.Update`，再由 Category 驱动 Agent 状态：

- 淡入淡出计时。
- 非循环音播放结束检测。
- Transform 跟随位置同步。
- 播放结束后的资源释放。

### 销毁

销毁流程：

```text
Dispose 所有 AudioScope
    ↓
StopAll
    ↓
ReleaseAllPreloaded
    ↓
Destroy 所有 AudioCategory / AudioAgent
    ↓
清理句柄、加载中 id、诊断数据
    ↓
销毁 Audio 根节点
```

## 多 package 设计

项目整体是“一个子游戏一个 package”，因此 Audio 模块不推荐业务每次播放都手写 `packageName`。

底层标准仍然是 `ResourceKey(PackageName, Location)`，但业务层推荐使用 `AudioScope`：

```csharp
using var audioScope = FRFM.Audio.CreateScope("Battle", "BattlePackage");
await audioScope.PlayMusicAsync("Audio/Bgm/BattleTheme");
await audioScope.PlaySoundAsync("Audio/Sfx/Hit");
```

此时底层等价于：

```csharp
new ResourceKey("BattlePackage", "Audio/Sfx/Hit")
```

跨包资源仍显式使用 `ResourceKey`：

```csharp
await audioScope.PlayAsync(
    new ResourceKey("SharedPackage", "Audio/Common/Confirm"),
    new AudioPlayOptions { Type = AudioType.UISound });
```

API 分层：

```csharp
FRFM.Audio.PlaySoundAsync(ResourceKey key, options);   // 最明确，底层标准入口
FRFM.Audio.PlaySoundAsync(location, options);          // 默认包或少量公共资源
scope.PlaySoundAsync(location, options);               // 子游戏内推荐入口
```

不采用全局 `CurrentPackage`，避免多系统并发播放时串包。

## 核心类型

### AudioType

```csharp
public enum AudioType
{
    Music,
    Ambience,
    Sound,
    UISound,
    Voice
}
```

语义：

- `Music`：背景音乐，默认循环，切换时淡入淡出。
- `Ambience`：场景环境音，可多条并存。
- `Sound`：战斗、交互、世界音效。
- `UISound`：按钮、弹窗、提示等 UI 音效。
- `Voice`：角色语音、剧情对白。

### AudioPlayOptions

`AudioPlayOptions` 用于承载播放参数：

- `Type`：音频类型。
- `PackageName`：可选 package；在 `AudioScope` 内可省略。
- `Loop`：是否循环。
- `Volume`：单个声音音量。
- `FadeIn` / `FadeOut`：淡入淡出时间。
- `Priority`：抢占优先级。
- `Pitch` / `PanStereo` / `SpatialBlend`：AudioSource 参数。
- `MinDistance` / `MaxDistance`：3D 音效距离参数。
- `Position`：固定世界坐标播放。
- `FollowTarget`：跟随 Transform 播放。
- `ReleaseOnStop`：停止时是否释放模块加载的资源引用。
- `PersistentAcrossScene`：预留跨场景策略字段。

### AudioHandle

播放返回 `AudioHandle`：

```csharp
AudioHandle handle = await FRFM.Audio.PlaySoundAsync("Audio/Sfx/Hit");
handle.Pause();
handle.Resume();
handle.SetVolume(0.5f);
handle.Stop(0.2f);
```

`AudioHandle` 只保存 id 和 manager 引用，实际控制由 `AudioManager` 查找当前 Agent。

### AudioScope

`AudioScope` 是 Audio 模块的业务生命周期边界：

- 保存默认 package。
- 跟踪该 scope 播放出去的 AudioHandle。
- 跟踪该 scope 预加载的 AudioClip。
- Dispose 时停止所有未结束音频并释放预加载资源。

适用场景：

- 子游戏。
- 战斗局。
- 关卡。
- 剧情流程。
- 临时活动玩法。

### AudioCategory

每个 `AudioType` 对应一个 Category：

- 持有固定数量 `AudioAgent`。
- 保存分类音量和静音。
- 使用 AudioMixer 参数落地音量。
- 负责 Agent 选择和抢占策略。

### AudioAgent

每个 `AudioAgent` 对应一个 Unity `AudioSource`：

- 绑定 AudioClip。
- 保存当前 ResourceHandle。
- 处理淡入淡出。
- 处理自然播放结束。
- 处理 Transform 跟随。
- 停止或结束时释放 ResourceHandle。

## 通道与抢占策略

播放时选择 Agent 的顺序：

1. 优先使用 Idle Agent。
2. 没有 Idle 且 `AllowSteal=false`，播放失败。
3. 有可抢占 Agent 时，优先抢占 priority 更低的声音。
4. 同 priority 时，如果 `AvoidStealSamePriority=false`，抢占播放时间更长的声音。
5. 被抢占声音使用 `options.FadeOut`，没有配置时默认 0.15 秒。

`Music` 特殊处理：

- `PlayMusicAsync` 会先 StopAll Music。
- 旧音乐按 fadeOut 淡出，新音乐按 fadeIn 淡入。
- 默认 loop 和最高优先级。

## 资源生命周期

路径播放使用 Resource 模块：

```csharp
ResourceHandle<AudioClip> handle = await FRFM.Resource.LoadAssetHandleAsync<AudioClip>(key);
```

Agent 保存该 handle，并在以下时机释放：

- 自然播放结束。
- `handle.Stop()`。
- 分类或全局 `StopAll`。
- 被抢占。
- Scope Dispose。
- AudioManager Destroy。

直接传入 `AudioClip` 时，Audio 模块不释放该 clip：

```csharp
FRFM.Audio.Play(clip, options);
```

## 预加载设计

```csharp
await FRFM.Audio.PreloadAsync("Audio/Sfx/Hit", AudioType.Sound, "BattlePackage");
FRFM.Audio.ReleasePreloaded("Audio/Sfx/Hit", "BattlePackage");
FRFM.Audio.ReleaseAllPreloaded();
```

Scope 内推荐：

```csharp
await audioScope.PreloadAsync("Audio/Sfx/Hit", AudioType.Sound);
```

规则：

- 预加载持有一份 ResourceHandle。
- 播放命中预加载资源时仍会为本次播放申请自己的引用，保证播放结束释放不会影响预加载缓存。
- Scope Dispose 会释放该 Scope 预加载的资源。

## 音量与 AudioMixer

默认 Mixer 分组建议：

```text
Master
├── Music
├── Ambience
├── Sound
├── UISound
└── Voice
```

默认暴露参数：

```text
MasterVolume
MusicVolume
AmbienceVolume
SoundVolume
UISoundVolume
VoiceVolume
```

线性音量转 dB：

```csharp
volume <= 0.0001f ? -80f : Mathf.Log10(volume) * 20f
```

如果没有配置 Mixer，则 `MasterVolume` 会退化到 `AudioListener.volume`，分类音量只保存在模块状态中。

## 3D 音效

固定坐标：

```csharp
await FRFM.Audio.PlaySoundAsync("Audio/Sfx/Explosion", new AudioPlayOptions
{
    Position = transform.position,
    SpatialBlend = 1f
});
```

跟随目标：

```csharp
AudioHandle handle = await FRFM.Audio.PlaySoundAsync("Audio/Sfx/FireLoop", new AudioPlayOptions
{
    Loop = true,
    FollowTarget = fireTransform,
    SpatialBlend = 1f
});
```

`UISound` 默认应保持 2D，`Sound` 和 `Ambience` 可按业务设置 3D。

## 诊断设计

`FRFM.Audio.CreateSnapshot()` 返回：

- 当前播放数。
- 加载中数量。
- 预加载数量。
- 抢占次数。
- 播放失败次数。
- 每个分类的音量、静音、暂停、Agent 数。
- 每个 Agent 的 id、状态、location、package、clip、loop、priority、elapsed。

后续可基于该快照实现 Editor 诊断窗口。

## 事件

`AudioEvents` 提供：

- `PlayStarted`
- `PlayStopped`
- `LoadFailed`
- `VolumeChanged`
- `MuteChanged`

初版使用 C# event，业务需要跨系统广播时再桥接 `FRFM.Event`。

## 对外 API 汇总

```csharp
FRFM.Audio.CreateScope(name, packageName);
FRFM.Audio.PlayMusicAsync(location, fadeOut, fadeIn, packageName);
FRFM.Audio.PlayAmbienceAsync(location, options);
FRFM.Audio.PlaySoundAsync(location, options);
FRFM.Audio.PlayUISoundAsync(location, volume, packageName);
FRFM.Audio.PlayVoiceAsync(location, options);
FRFM.Audio.PlayAsync(ResourceKey key, options);
FRFM.Audio.Play(AudioClip clip, options);
FRFM.Audio.Stop(handle, fadeOut);
FRFM.Audio.StopAll(type, fadeOut);
FRFM.Audio.Pause(handle);
FRFM.Audio.Resume(handle);
FRFM.Audio.PauseAll(type);
FRFM.Audio.ResumeAll(type);
FRFM.Audio.SetVolume(type, volume);
FRFM.Audio.SetMute(type, mute);
FRFM.Audio.PreloadAsync(key, type);
FRFM.Audio.ReleasePreloaded(key);
FRFM.Audio.CreateSnapshot();
```

## 使用建议

- 子游戏内优先创建 `AudioScope`，不要每次手写 packageName。
- 跨包资源用 `ResourceKey` 显式表达。
- 长循环音保存 `AudioHandle`，退出时 Stop 或交给 Scope Dispose。
- UI 点击音可以直接 `PlayUISoundAsync`。
- 直接传 `AudioClip` 时，调用方自己负责 clip 来源和生命周期。
- 音量保存到本地配置由业务层负责，Audio 模块只提供 Capture/Apply 的扩展空间。
