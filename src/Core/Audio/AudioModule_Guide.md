# Audio 模块使用指南

## 模块入口

Audio 模块通过 `FRFM.Audio` 使用：

```csharp
await FRFM.Audio.PlayUISoundAsync("Audio/UI/Click");
```

模块初始化顺序为：

```text
ResourceManager -> AudioManager -> UIManager
```

因此 Audio 可以安全使用 Resource 模块加载 AudioClip，UI 初始化后也可以直接播放 UI 音效。

## 推荐用法：AudioScope

当前工程偏向“一个子游戏一个 package”，因此子游戏、战斗、关卡、剧情等业务内推荐先创建 `AudioScope`：

```csharp
public sealed class BattleAudioController : System.IDisposable
{
    private readonly AudioScope _audioScope = FRFM.Audio.CreateScope("Battle", "BattlePackage");
    private AudioHandle _fireLoop;

    public async UniTask EnterAsync(Transform fireTransform)
    {
        await _audioScope.PlayMusicAsync("Audio/Bgm/BattleTheme", fadeOut: 0.5f, fadeIn: 0.5f);

        _fireLoop = await _audioScope.PlaySoundAsync("Audio/Sfx/FireLoop", new AudioPlayOptions
        {
            Loop = true,
            FollowTarget = fireTransform,
            SpatialBlend = 1f,
            Volume = 0.8f
        });
    }

    public async UniTask PlayHitAsync(Vector3 position)
    {
        await _audioScope.PlaySoundAsync("Audio/Sfx/Hit", new AudioPlayOptions
        {
            Position = position,
            SpatialBlend = 1f,
            Priority = 10
        });
    }

    public void Dispose()
    {
        _audioScope.Dispose();
    }
}
```

`AudioScope` 的 `packageName` 会作为 string location API 的默认 package。Scope Dispose 时会停止该 Scope 播放的声音，并释放该 Scope 预加载的 AudioClip。

## 跨 package 播放

Scope 内大多数资源可以只写 location。跨包资源使用 `ResourceKey`：

```csharp
await audioScope.PlayAsync(
    new ResourceKey("SharedPackage", "Audio/Common/Confirm"),
    new AudioPlayOptions { Type = AudioType.UISound });
```

不要依赖全局 CurrentPackage 之类的状态，避免多个子系统并发播放时串包。

## 直接使用 FRFM.Audio

适合默认包或公共资源：

```csharp
await FRFM.Audio.PlayMusicAsync("Audio/Bgm/Main", fadeOut: 0.5f, fadeIn: 0.5f);
await FRFM.Audio.PlayUISoundAsync("Audio/UI/Click");
await FRFM.Audio.PlaySoundAsync("Audio/Sfx/Explosion", new AudioPlayOptions
{
    PackageName = "BattlePackage",
    Position = transform.position,
    SpatialBlend = 1f
});
```

如果是非默认包资源，优先使用 `ResourceKey` 或 `AudioScope`。

## 常用播放接口

```csharp
await FRFM.Audio.PlayMusicAsync(location, fadeOut, fadeIn, packageName);
await FRFM.Audio.PlayAmbienceAsync(location, options);
await FRFM.Audio.PlaySoundAsync(location, options);
await FRFM.Audio.PlayUISoundAsync(location, volume, packageName);
await FRFM.Audio.PlayVoiceAsync(location, options);
await FRFM.Audio.PlayAsync(new ResourceKey(packageName, location), options);
```

直接播放已有 AudioClip：

```csharp
AudioHandle handle = FRFM.Audio.Play(audioClip, new AudioPlayOptions
{
    Type = AudioType.Sound,
    Volume = 0.8f
});
```

直接传入 `AudioClip` 时，Audio 模块不会释放该 clip。

## AudioHandle 控制

```csharp
AudioHandle handle = await FRFM.Audio.PlaySoundAsync("Audio/Sfx/Loop", new AudioPlayOptions
{
    Loop = true
});

handle.SetVolume(0.5f);
handle.Pause();
handle.Resume();
handle.Stop(0.2f);
```

## 3D 音效

固定位置：

```csharp
await FRFM.Audio.PlaySoundAsync("Audio/Sfx/Explosion", new AudioPlayOptions
{
    Position = transform.position,
    SpatialBlend = 1f,
    MinDistance = 2f,
    MaxDistance = 50f
});
```

跟随目标：

```csharp
AudioHandle handle = await FRFM.Audio.PlaySoundAsync("Audio/Sfx/EngineLoop", new AudioPlayOptions
{
    Loop = true,
    FollowTarget = vehicleTransform,
    SpatialBlend = 1f
});
```

跟随目标销毁后，循环音会自动停止。

## 音量与静音

```csharp
FRFM.Audio.MasterVolume = 0.8f;
FRFM.Audio.MasterMute = false;

FRFM.Audio.SetVolume(AudioType.Music, 0.5f);
FRFM.Audio.SetMute(AudioType.UISound, false);

float musicVolume = FRFM.Audio.GetVolume(AudioType.Music);
bool uiMuted = FRFM.Audio.GetMute(AudioType.UISound);
```

如果配置了 AudioMixer，模块会写入：

```text
MasterVolume
MusicVolume
AmbienceVolume
SoundVolume
UISoundVolume
VoiceVolume
```

如果没有 Mixer，MasterVolume 会退化到 `AudioListener.volume`。

## 预加载

全局预加载：

```csharp
await FRFM.Audio.PreloadAsync("Audio/Sfx/Hit", AudioType.Sound, "BattlePackage");
FRFM.Audio.ReleasePreloaded("Audio/Sfx/Hit", "BattlePackage");
```

Scope 预加载：

```csharp
await audioScope.PreloadAsync("Audio/Sfx/Hit", AudioType.Sound);
```

Scope Dispose 会释放该 Scope 预加载的资源。

## 暂停与停止

```csharp
FRFM.Audio.PauseAll(AudioType.Sound);
FRFM.Audio.ResumeAll(AudioType.Sound);
FRFM.Audio.StopAll(AudioType.Sound, fadeOut: 0.1f);
FRFM.Audio.StopAll();
```

游戏暂停时推荐暂停 `Sound`、`Ambience`、`Voice`，是否暂停 `Music` 由业务决定。

## 诊断

运行时可获取快照：

```csharp
AudioDiagnosticsSnapshot snapshot = FRFM.Audio.CreateSnapshot();
```

Editor 菜单：

```text
FRFrame/Diagnostics/Audio Diagnostics
```

可查看：

- 播放中数量。
- 加载中数量。
- 预加载数量。
- 抢占次数。
- 播放失败次数。
- 每个分类的 Agent 状态和正在播放的资源。

## 配置

可选创建 `Resources/FRFrameAudioConfig.asset`，类型为 `AudioModuleConfig`。

默认分类建议：

| 分类 | Agent 数 | 用途 |
| --- | ---: | --- |
| Music | 2 | BGM 淡入淡出 |
| Ambience | 4 | 环境循环音 |
| Sound | 16 | 战斗和世界音效 |
| UISound | 8 | UI 反馈音 |
| Voice | 4 | 语音对白 |

如果不提供配置，模块会使用运行时默认配置。

## 注意事项

- 非默认包资源优先用 `AudioScope` 或 `ResourceKey`。
- 直接传 `AudioClip` 时，Audio 模块不负责释放 clip。
- 长循环音应保存 `AudioHandle`，或放进 `AudioScope` 生命周期内。
- 不要用 `AudioSource.PlayClipAtPoint` 绕过模块，否则无法统一音量、诊断和资源释放。
- 如果听不到声音，先检查 package、location、AudioMixer 暴露参数、分类静音和 MasterMute。
