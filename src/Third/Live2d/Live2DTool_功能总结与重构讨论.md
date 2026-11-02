# Live2D Motion Runtime 与交互系统重构设计文档

## 1. 设计目标

当前 `Assets/FRFrame/Third/Live2d` 目录实现了一套不依赖 Unity Animator 的 Live2D motion3 播放与触摸交互工具。它直接解析 `.motion3.json`，在运行时写入 `CubismParameter` / `CubismPart`，并通过 Base / Overlay / Effect 等概念完成待机、主界面状态、点击反馈、拖动反馈、常驻特效等 motion 的组合播放。

这份文档的目标是把现有实现、当前资源特征、已确认的取舍和后续完整重写方案合并为一份详细设计文档。当前不要求渐进式兼容旧资产；如果新的整体方案更清晰，可以接受完全重写。

最终系统应满足：

- 直接播放 Cubism `.motion3.json`，不依赖 Unity AnimatorController。
- 完美支持当前 `Assets/GameRes/AzurLane/Art/Live2d` 下的资源结构和 motion 命名。
- 主要服务 UI RawImage 上显示的 Live2D。
- 使用配置化触摸区域，不依赖额外挂碰撞组件。
- 支持矩形触摸区域；如果成本合理，预留多边形区域扩展。
- 支持 motion 分层、淡入淡出、打断策略、队列、随机 idle、touch/drag feedback。
- 支持 motion 随机权重、冷却、避免重复、顺序播放等可配置选择策略。
- 支持 motion3 UserData 事件，用于音效、外部回调等。
- 支持与外部状态联动。
- 提供更清晰的 MotionReference，减少纯字符串引用带来的脆弱性。
- 编辑器工具优先考虑 UI Toolkit，但必须保留播放中准确录制 RawImage 归一化触摸区域的能力。

## 2. 当前资源特征

参考资源：

- 复杂资源：`Assets/GameRes/AzurLane/Art/Live2d/mojiaduoer_4`
- 简单资源：`Assets/GameRes/AzurLane/Art/Live2d/adaerbote_2`

### 2.1 简单资源形态

`adaerbote_2.model3.json` 中的 motion 比较典型：

- `idle`
- `main_1` / `main_2` / `main_3`
- `login`
- `home`
- `mail`
- `mission`
- `mission_complete`
- `complete`
- `wedding`
- `touch_head`
- `touch_body`
- `touch_special`
- `effect`

这种资源适合默认规则：

- `idle` 作为待机循环。
- `effect` 作为常驻或可暂停特效层。
- `touch_*` 作为触摸反馈，一次性播放。
- `main_*`、`home`、`mail`、`mission` 等作为 base 状态或一次性 base motion。

### 2.2 复杂资源形态

`mojiaduoer_4.model3.json` 的 motion 数量很多，包含：

- `idle`、`idle1`、`idle2`、一直到多个编号 idle。
- `touch_idle1`、`touch_idle2`、一直到大量编号 touch_idle。
- `touch_drag1` 到多个编号 drag。
- 常规 `touch_head`、`touch_body`、`touch_special`。
- `change_in`、`change_out`、`main_1` ~ `main_4`、`effect` 等。

这说明系统不能只按简单固定枚举理解资源，而应该支持：

- 同一语义下有大量候选 motion。
- motion 组选择策略需要配置权重、冷却、避免重复或顺序播放。
- `touch_idle*` 可能不是“同时很多 overlay”，而是“很多可选的触摸反馈候选”。
- 同时播放的 Overlay Track 数量目前只需要 1 个即可；大量 touch motion 是候选数量，不是并发层数量。

## 3. 当前实现功能总结

### 3.1 motion3 解析与曲线求值

现有 `Motion3JsonData.cs` 实现：

- 解析 motion3 json 的 Meta、Curves、UserData。
- 支持 Linear、Bezier、Stepped、InverseStepped segment。
- 生成运行时 `Motion3RuntimeData`。
- 根据时间求曲线值。
- 当前实际应用 Parameter 和 PartOpacity。

需要保留并加强：

- 曲线求值能力。
- motion 级与 curve 级 fade。
- UserData 解析。

需要改进：

- segment 构建的边界检查。
- 曲线数据结构优化，减少运行时对象和线性查找开销。
- UserData 要进入播放事件系统。
- `Model` target 是否支持需要明确；如果短期不用，可作为未实现 target 显式忽略并记录一次性 warning。

### 3.2 MotionLibrary

现有 `Live2DMotionLibrary.cs` 实现：

- 收集 motion TextAsset。
- 按 motionName 建 runtime cache。
- 自动猜测分类。
- 支持 loop / fade / overlay 策略配置。
- 支持按分类随机 motion。

需要保留：

- 一个资源级 MotionLibrary / MotionSet 资产。
- 扫描 `model3.json` 或 motions 文件夹自动生成 motion 条目。
- 支持 motion 分类和默认规则。

需要改进：

- motion 条目不应只存裸字符串；应有稳定的 MotionId / MotionReference。
- 分类规则应可配置，而不是只靠硬编码命名。
- 同一语义 motion 应组成 MotionGroup，例如 IdleGroup、TouchHeadGroup、TouchIdleGroup、DragGroup。
- MotionGroup 需要选择策略：随机、加权、顺序、ShuffleBag、冷却、避免重复。

### 3.3 播放核心

现有 `Live2DMotionPlayer.cs` 实现：

- 缓存 Cubism 参数和部件。
- Base / Overlay / Effect 三层播放。
- Current / Previous cross fade。
- Base pending 与 idle fallback。
- Overlay 阻挡与打断。
- Effect pause/resume/stop。
- 每帧 reset default 后按层应用曲线。

需要保留的语义：

- Base：角色当前主状态，例如 idle、home、mail、mission、main_*。
- Overlay：短促交互反馈，例如 touch_head、touch_body、touch_drag*、touch_idle*。
- Effect：常驻效果，例如 effect。

需要澄清：

- “多个 overlay”有两种含义：
  - 候选 motion 很多：需要支持，复杂资源会有几十个 touch_idle / touch_drag。
  - 同时并发播放多个 overlay track：当前不需要，先设计为同一时间只有一个 Overlay Track。
- 如果未来需要同时播放多个 overlay，例如表情层和身体反馈层分开，可以扩展为多个 overlay layer，但不是当前刚需。

需要改进：

- 播放内核和调度策略拆开。
- MotionTrack / MotionLayer 独立成核心类型。
- 播放事件标准化：Started、Completed、Interrupted、Stopped、UserData。
- Driver 不再协程轮询 overlay 是否结束，而订阅播放事件。
- LayerDefinition 数据化，但默认仍提供 Base / Overlay / Effect。

### 3.4 触摸交互

现有 `Live2DTouchRuntime.cs` 实现：

- RawImage 上监听 pointer 事件。
- 屏幕坐标转 RawImage normalized 坐标。
- 根据当前 base motion 选择 TouchLayout。
- 在 layout 中按 priority 选择命中 area。
- Tap / Drag 触发 area 配置的 motion。
- OnGUI 绘制调试区域。

需要保留：

- 以 RawImage 为主要输入目标。
- 使用 0~1 归一化坐标配置区域。
- 根据当前 base motion 切换不同 layout。
- Tap / Drag 的基本识别。

需要改进：

- 触摸区域从 `tapMotions` / `dragMotions` 升级为事件绑定模型。
- 命中形状先支持 Rect，预留 Polygon。
- 不再保留基于额外 Collider2D 组件的旧触摸方案。
- Debug 绘制与运行时识别逻辑拆开。

## 4. 已确认的设计取舍

### 4.1 旧版碰撞组件触摸方案废弃

旧方案包括：

- `Live2DTouchMotionController`
- `Live2DUIHitArea`
- `Live2DRawImageHitTester`
- `Live2DUIEventDebugProbe`

这些方案依赖额外 UI 节点或 Collider2D 组件来做命中。当前确认不再需要兼容，可以直接删除。

新的触摸系统只保留：

- RawImage 屏幕坐标 -> normalized 坐标。
- 配置资产中的区域命中。
- 矩形区域为主。
- 多边形作为数据形状扩展，不依赖额外挂组件。

### 4.2 当前只服务 RawImage Live2D

当前只需要服务 UI RawImage 上显示的 Live2D。世界空间 Live2D 暂不作为当前目标。

世界空间 Live2D 的例子：

- 角色作为 2D 场景里的实际对象，站在场景坐标中，玩家点击世界空间角色。
- AR / 3D 房间中把 Live2D 放在某个平面上，通过相机射线点选。
- 战斗场景中 Live2D 不是 UI，而是参与世界坐标排序、遮挡、镜头缩放。

这些场景通常需要 Camera Raycast、Collider、Sorting、世界坐标命中等能力。当前项目目标是 UI RawImage 展示，所以没必要为这些场景增加复杂度。以后如果真需要，可以另建 `WorldLive2DInputAdapter`，不影响 RawImage 主链路。

### 4.3 矩形区域优先，多边形可扩展

当前矩形已经够用。多边形的价值在于：

- 头发、身体、手臂等区域可能不适合矩形。
- 重叠区域可以更精确。

但多边形不能要求额外组件，否则配置成本会变高。推荐设计：

```text
TouchAreaShape
- Rect
- Polygon
```

Rect 数据：

```text
normalizedRect: Rect
```

Polygon 数据：

```text
normalizedPoints: List<Vector2>
```

多边形仍然基于 RawImage normalized 坐标，在编辑器中点选/拖拽顶点，不需要 Collider2D。

### 4.4 允许外部状态联动

虽然当前具体业务场景未完全确定，但 motion 播放应该可受外部状态影响。例如：

- 好感度不同，点击头部播放不同组。
- 当前 UI 页面不同，禁用某些区域或改用不同 motion group。
- 某个任务完成后，mission 点击播放特殊反馈。
- 角色换装状态不同，使用不同 layout 或 motion group。
- 播放剧情时禁止普通触摸 overlay。

因此交互绑定需要支持条件系统，但不要一开始做成复杂脚本语言。建议先提供接口和少量内置条件。

### 4.5 Overlay 并发数量先保持 1

当前资源中的几十个 touch motion 是候选数量，不是同时播放数量。播放模型先保持：

- Base layer：同时 1 个 current + 1 个 fading previous。
- Overlay layer：同时 1 个 current + 1 个 fading previous。
- Effect layer：同时 1 个 current + 1 个 fading previous。

候选很多的问题通过 MotionGroup 和 SelectionPolicy 解决。

未来如果需要多 overlay，可以扩展为：

- OverlayExpression
- OverlayBody
- OverlayEffect

但当前不作为第一版目标。

### 4.6 随机选择策略需要配置清晰

每个 MotionGroup 或 InteractionBinding 应可配置：

- First：总是第一个。
- Random：等概率随机。
- WeightedRandom：按权重随机。
- Sequence：按顺序循环。
- ShuffleBag：洗牌袋，播完一轮前尽量不重复。

每个候选 MotionCandidate 可配置：

- weight
- cooldown
- tags
- conditions
- override fade
- override interrupt

全局或组级可配置：

- avoidRepeatLast
- repeatBlockCount
- groupCooldown

### 4.7 UserData 事件作为可选输入源，不作为唯一事件方案

motion3 UserData 理论上可用于：

- 播放音效。
- 触发口型、表情或 UI 动效。
- 通知外部业务某个动作阶段。
- 派发剧情事件。

但当前资源不确定是否实际包含 UserData，也不确定 Value 格式。不能假设 moc3 / motion3 内部一定提供可用事件。因此设计上应同时支持两类事件来源：

1. **Motion 内置 UserData**：如果 motion3.json 中存在 UserData，则运行时按时间派发。
2. **外部自定义 Timeline Event**：在自定义配置资产中手动配置时间点和事件内容，例如音效、业务事件、参数动画触发。

MotionTrack 派发 UserData 事件时应包含：

```text
motionId
layerId
time
value
trackInstanceId
```

如果 motion loop，需要明确 UserData 每轮循环是否重新触发。默认应每轮触发一次。

外部自定义事件不依赖 motion 文件存在，适合当前“资源里没有 UserData，但交互反馈仍需要音效或参数变化”的场景。

### 4.8 MotionReference 优于纯字符串

纯 motionName 的问题：

- 不同资源命名差异大。
- 重命名不安全。
- 手写字符串容易错。
- 跨资源复用配置困难。

推荐引入 MotionReference：

```text
MotionReference
- libraryGuid / modelKey
- motionId
- displayName
```

在 Unity 中可序列化为：

```csharp
[Serializable]
public struct Live2DMotionReference
{
    public Live2DMotionLibrary library;
    public string motionId;
}
```

motionId 可以默认来自 model3.json 的 motion group key 或文件名归一化结果，例如 `touch_head`、`idle20`。在同一个 library 内唯一即可。

优势：

- Inspector 可以做下拉选择。
- Validator 可以检查引用是否存在。
- motion 改名时可以由工具辅助迁移。
- InteractionProfile 可以明确绑定某个 Library。

是否需要完全避免字符串？不现实。底层仍然需要 motionId 字符串作为 key，但业务配置不直接手写字符串，而通过 MotionReference 选择。

## 5. 新架构草案

```text
Live2D Motion Runtime

Assets / Data
- Live2DMotionLibrary
- Live2DMotionClip
- Live2DMotionGroup
- Live2DMotionReference
- Live2DInteractionProfile
- Live2DLayout
- Live2DTouchArea
- Live2DInteractionBinding
- Live2DInteractionAction

Playback Core
- Live2DMotionMixer
- Live2DMotionLayer
- Live2DMotionTrack
- Live2DCubismBindingCache
- Live2DMotionCurveEvaluator

Scheduling
- Live2DMotionController
- Live2DMotionRequest
- Live2DLayerPolicy
- Live2DMotionSelectionPolicy
- Live2DConditionEvaluator

Input / Interaction
- Live2DRawImageInputRuntime
- Live2DPointerSession
- Live2DInteractionRecognizer
- Live2DTouchHitTester
- Live2DTouchAreaShape

Events
- MotionStarted
- MotionCompleted
- MotionInterrupted
- MotionStopped
- MotionUserData
- CustomTimelineEvent
- InteractionTriggered

Custom Parameter Animation
- Live2DParameterAnimationAsset
- Live2DParameterAnimationClip
- Live2DParameterCurve
- Live2DParameterKeyframe

Editor
- Live2DMotionLibraryBuilderWindow
- Live2DInteractionProfileEditorWindow
- Live2DTouchAreaOverlay
- Live2DValidator
```

## 6. 数据设计

### 6.1 Live2DMotionLibrary

职责：描述一个 Live2D 模型可用的 motions、motion groups、默认 layer 策略。

字段建议：

```text
modelName
model3Json
motionRootFolder
clips[]
groups[]
defaultLayerRules[]
```

### 6.2 Live2DMotionClip

职责：单个 motion3 文件的元数据与运行时缓存入口。

```text
motionId
motionName
displayName
motionAsset
sourceFilePath
category
loopMode
fadeIn
fadeOut
defaultLayer
interruptPolicy
tags[]
runtimeData
```

category 建议不是最终播放规则，只是默认分组提示：

```text
Idle
Base
Touch
Drag
TouchIdle
Effect
Transition
Custom
```

### 6.3 Live2DMotionGroup

职责：把大量同类 motion 聚合起来，并配置选择策略。分组必须支持自定义，不应只依赖命名规则。

```text
groupId
displayName
candidates[]
selectionMode
groupCooldown
avoidRepeatLast
repeatBlockCount
fallbackGroup
```

Candidate：

```text
motionReference
weight
cooldown
enabled
conditions[]
```

示例：

```text
Group: idle_base
- idle
- idle1
- idle2
- main_1
- main_2
Selection: ShuffleBag
Loop: true

Group: main
- main_1
- main_2
- main_3
Selection: Sequence / ShuffleBag

Group: touch_head
- touch_head
Selection: First

Group: touch_idle
- touch_idle1
- touch_idle2
- touch_idle3
...
Selection: WeightedRandom / ShuffleBag

Group: touch_drag
- touch_drag1
- touch_drag2
...
Selection: Cooldown Random
```

默认扫描时可以把 `idle*` 和 `main*` 放入 `idle_base` 或 Base 候选组，但必须允许用户在编辑器中创建任意自定义 group、移动 candidate、修改 group 用途和选择策略。

### 6.4 Live2DInteractionProfile

职责：描述 RawImage 上的交互布局、区域和事件绑定。

```text
boundMotionLibrary
defaultLayoutId
layouts[]
layoutBindings[]
rawImageTransformPreset
modelTransformPreset
```

layoutBindings：

```text
baseMotionOrGroup
layoutId
conditions[]
```

当前仍可支持“当前 base motion -> layout”，但最好允许将来扩展到“当前状态 -> layout”。

### 6.5 Live2DTouchArea

```text
areaId
name
enabled
priority
shape
bindings[]
```

shape：

```text
ShapeType.Rect
normalizedRect

ShapeType.Polygon
normalizedPoints[]
```

### 6.6 Live2DInteractionBinding

```text
trigger
consumePolicy
cooldown
conditions[]
actions[]
```

trigger：

```text
Tap
DragStart
DragRepeat
DragEnd
LongPress
HoverEnter
HoverExit
```

第一版至少实现：

- Tap
- DragRepeat

预留但可不马上实现：

- LongPress
- HoverEnter / HoverExit
- Swipe

### 6.7 Live2DInteractionAction

第一版建议支持：

```text
PlayMotion
PlayMotionGroup
PlayParameterAnimation
StopLayer
SwitchLayout
SendEvent
PlayAudio
SetParameterValue
```

PlayMotion / PlayMotionGroup 字段：

```text
motionReference or groupId
layerId
interruptPolicy
fadeOverride
onCompleteAction
```

PlayParameterAnimation 字段：

```text
parameterAnimationReference
layerId
interruptPolicy
speed
fadeInOverride
fadeOutOverride
applyMode
```

`layerId` 必须可配置。ParameterAnimation 可能作为短反馈播放在 Overlay，也可能作为 Effect 循环，也可能作为 Base 层的一部分常驻。

SendEvent 可用于外部状态系统：

```text
eventName
payload
```

PlayAudio 可用于不依赖 motion3 UserData 的音效反馈：

```text
audioKey        // 优先使用 FRFrame 资源管理 key
fallbackClip    // 可选，仅用于编辑器预览或临时测试
volume
pitch
```

音频引用以资源管理 key 为主，方便后续通过 FRFrame 资源模块加载；`fallbackClip` 只作为开发期兜底，不作为最终资源绑定方式。

SetParameterValue 适合瞬时参数变化；持续变化应使用 PlayParameterAnimation。

`applyMode` 用于简单参数反馈：

```text
Immediate       // 触发时立即设置参数值，不创建持续 Track
AsTrack         // 作为一段可淡入淡出的参数动画播放
```

## 6.8 Live2DParameterAnimationAsset

有些一次性反馈没有对应 `.motion3.json` 文件，只能通过直接修改 Live2D parameter 实现。因此需要新增自定义参数动画资产。

该资产应是 ScriptableObject，用于描述简单参数设置或复杂参数曲线：

```text
animationId
displayName
mode
duration
loopMode
fadeIn
fadeOut
simpleParameters[]
curves[]
events[]
defaultLayerId
```

mode：

```text
SimpleSet       // 简单参数开关或赋值
CurveAnimation  // 复杂关键帧/曲线动画
Mixed           // 同时包含简单赋值和曲线
```

简单参数设置用于“部件开关”这类需求，编辑器只需要填少量字段：

```text
parameterId
value          // 常见为 0 或 1，也允许任意 float
applyImmediately
blendMode
```

`applyImmediately` 为 true 时，触发后立即设置参数值，不需要创建持续 Track；适合开关、表情状态、某些一次性显示隐藏。为 false 时，将它作为 duration 内的一条常量曲线播放，可参与 fade 和 layer 混合。

复杂曲线结构：

```text
parameterId
blendMode
keyframes[]
segments[]
```

Keyframe：

```text
time
value
tangentMode / interpolation
```

Segment：

```text
segmentType: Linear / Bezier / Stepped / InverseStepped
startTime
startValue
endTime
endValue
controlPoint1
controlPoint2
```

blendMode 建议支持：

```text
Override      // 直接向目标值混合
Additive      // 在当前参数值基础上叠加
Multiply      // 乘法影响，较少用
```

第一版最重要的是 Override 和 Additive。

编辑器需要同时支持两种编辑体验：

- 简单模式：表单式填写 parameterId、value、applyImmediately、blendMode，不需要打开曲线编辑器。
- 曲线模式：提供曲线编辑视图，用于多关键帧、Bezier、渐变变化等复杂动画。

表格关键帧只作为曲线模式的辅助输入，不需要设计得太重；复杂编辑以曲线视图为主。

ParameterAnimation 与 motion3 播放应共用同一套 Track / Layer / Fade / Event 机制。也就是说，运行时不应把它做成单独 Update 脚本，而应把它转换为可播放的 `ILive2DPlayableClip`：

```text
ILive2DPlayableClip
- Duration
- Loop
- Curves
- Events
- Evaluate(time)
```

motion3 clip 和 parameter animation clip 都可以进入 MotionMixer，只是数据来源不同。

### 6.9 自定义 Timeline Event

为了不依赖 motion3 UserData，自定义参数动画和 motion 配置都可以附加外部事件：

```text
time
eventType
stringValue
objectReference
floatValue
```

事件类型：

```text
PlayAudio
SendEvent
SetParameter
Custom
```

这样可以支持：

- 播放某个音效。
- 在参数动画播放到某个时间点派发业务事件。
- 在 motion 没有 UserData 时手动补事件。
- 将来为某个 motion 配置额外事件，而不改 motion3.json。

### 6.10 音频事件接入

当前 `Assets/FRFrame/Core` 中没有现成音频模块。Live2D 系统不应强依赖一个尚未完善的音频框架，但需要让自定义事件能触发声音。

建议分两层：

1. Live2D 内部只派发 `PlayAudio` 事件，事件数据包含 AudioClip、音量、音调、混音组或 channel key。
2. 如果项目需要立即可用，可以在 `Assets/FRFrame/Core` 下实现一个最小音频服务，负责播放一次性 SFX。

最小音频服务能力：

```text
FRAudioService
- PlaySfx(string audioKey, float volume = 1, float pitch = 1)
- PlaySfx(AudioClip clip, float volume = 1, float pitch = 1) // 编辑器预览或临时测试
- StopSfx(handle)
- masterVolume
- sfxVolume
```

Live2D 不直接 new AudioSource，也不把音频逻辑写死在播放核心里。默认通过接口调用：

```csharp
public interface ILive2DAudioEventHandler
{
    void PlayAudio(Live2DAudioEventData data);
}
```

`Live2DAudioEventData` 中以 `audioKey` 为主，后续由 FRFrame 资源模块解析和加载。

如果场景中没有音频处理器，则只派发事件并记录可选 debug log。这样后续接入正式 FRFrame 音频模块时不需要改 Live2D 核心。

## 7. 播放核心设计

### 7.1 Layer

默认提供三层：

```text
Effect
Base
Overlay
```

含义：

- Effect：场景或背景相关的特殊效果层，是否循环、是否启动时自动播放都必须可配置；这一层保留，但不强制所有资源使用。
- Base：角色主要姿态或状态，例如 idle、main_*、home、mail、mission，也可以播放 login 这类一次性动作。
- Overlay：点击、拖动等短反馈，同一时间只播放一个；大量 touch_idle / touch_drag 是候选数量，不是并发数量。

LayerPolicy：

```text
layerId
priority
defaultInterruptPolicy
allowQueue
fallbackGroup
applyOrder
```

默认 applyOrder：

```text
Effect -> Base -> Overlay
```

### 7.2 Track 生命周期

```text
Created
FadingIn
Playing
FadingOut
Completed
Interrupted
Stopped
```

完成原因：

```text
NaturalComplete
LoopRestart
Interrupted
Stopped
Replaced
```

### 7.3 MotionRequest

所有播放请求统一通过 MotionRequest：

```text
requestId
source
motionReference or groupId
layerId
interruptPolicy
fadeInOverride
fadeOutOverride
priority
callbacks
```

source 示例：

```text
Startup
IdleFallback
UserTap
UserDrag
ExternalCode
UserData
```

### 7.4 UserData 与自定义事件派发

MotionTrack 持有事件 cursor：

- 非循环 clip：每个事件触发一次。
- 循环 clip：每轮循环重置 cursor，每轮触发一次。
- 如果一帧跨过多个事件，需要全部按时间顺序触发。
- 事件来源包括 motion3 UserData 和外部配置的 Timeline Event。

事件：

```csharp
public event Action<Live2DPlayableEvent> PlayableEventTriggered;
```

`Live2DPlayableEvent` 需要能区分来源：

```text
source: MotionUserData / CustomTimelineEvent
eventType
time
stringValue
objectReference
trackInstanceId
```

### 7.5 自定义参数动画播放

自定义参数动画和 motion3 一样进入 MotionMixer。运行时流程：

1. InteractionBinding 触发 `PlayParameterAnimation`。
2. Controller 生成 MotionRequest，clip 类型为 ParameterAnimation。
3. Mixer 在指定 layer 创建 Track。
4. Track 按时间 Evaluate 参数曲线。
5. BindingCache 根据 parameterId 找到 CubismParameter。
6. 按 clip/layer/curve 的 blendMode 和 fade weight 应用值。
7. 播放过程中派发自定义 Timeline Event。

它需要支持两类用途：

- **独立反馈**：没有 motion3 文件，点击后只播放参数动画。
- **叠加反馈**：播放 motion3 的同时叠加一段参数动画，例如表情、眼神、局部反应。

如果需要同时执行 motion 和参数动画，InteractionBinding 的 actions 可以顺序执行多个 action，例如：

```text
Tap Head:
- PlayMotionGroup: touch_head
- PlayParameterAnimation: blush_small
- PlayAudio: touch_head_voice
```

## 8. 输入与命中设计

### 8.1 RawImage 坐标

输入系统只需要把 pointer screen position 转成 RawImage normalized 坐标：

```text
screenPoint -> localPoint -> normalized 0~1
```

Rect 和 Polygon 都使用 normalized 坐标，因此不需要额外场景组件。

### 8.2 Hit Test

```text
Live2DTouchHitTester
- GetCurrentLayout()
- Hit(normalizedPoint)
```

命中规则：

1. 忽略 disabled area。
2. 判断 shape contains point。
3. 如果多个命中，选 priority 最高。
4. priority 相同可选列表后者或配置 tie-break。

### 8.3 Pointer Recognizer

第一版识别：

- Tap：按下到抬起，时长和移动距离在阈值内。
- DragStart：第一次超过拖动阈值。
- DragRepeat：移动超过阈值后，按 interval 或 area change 触发。
- DragEnd：拖动结束。
- LongPress：按住超过配置时长且移动距离未超限。
- HoverEnter / HoverExit：鼠标或指针进入/离开区域。
- Swipe：抬起时根据位移和方向判断滑动。

这些功能除非实现成本明显过高，否则都纳入完整设计；具体可以通过开关控制是否启用。

配置：

```text
tapMaxDuration
tapMaxMovePixels
dragStartThresholdPixels
dragRepeatInterval
dragTriggerMode
longPressDuration
hoverEnabled
swipeMinDistance
swipeDirectionMode
acceptTapWhenOverlayPlaying
allowDragInterruptOverlay
```

这些配置可以放在 InteractionProfile 或 Runtime Component 上。

## 9. UI Toolkit 编辑器可行性

目标：迁移运行时触摸编辑器到 UI Toolkit，同时保留“播放 Live2D 时准确录制触摸矩形归一化区域”的能力。

可行方案：

### 9.1 推荐工具形态

使用 EditorWindow + UI Toolkit 面板，配合 GameView/SceneView overlay 式绘制。

核心点：录制区域不依赖 UI Toolkit 控件本身的坐标，而仍然使用运行时 RawImage 的 RectTransform 做转换：

```text
鼠标屏幕点 -> RawImage RectTransform -> normalized rect
```

只要使用同一套 `TryScreenPointToNormalized` 逻辑，UI Toolkit 不会降低区域录制精度。

### 9.2 两种实现路径

#### 路径 A：保留轻量 Runtime Capture 组件

运行时组件只负责：

- 获取 RawImage screen rect。
- 把鼠标点转换为 normalized。
- 绘制简易 overlay。
- 暴露当前 hover / selected area。

UI Toolkit EditorWindow 负责：

- profile 数据编辑。
- motion picker。
- layout/area 列表。
- 保存、验证。

优点：

- 播放中录制区域最稳定。
- Runtime 和 EditorWindow 职责清楚。
- 不要求 UI Toolkit 直接嵌入 GameView。

#### 路径 B：EditorWindow 直接驱动录制

EditorWindow 在 PlayMode 下找到选中的 `Live2DRawImageInputRuntime`，读取它的 RawImage 和 profile，然后处理输入。

优点：组件少。

缺点：GameView 输入与 EditorWindow 焦点交互可能更麻烦。

建议选择路径 A：保留非常薄的 runtime capture/debug 层，把复杂 UI 全部迁到 UI Toolkit。

### 9.3 编辑体验目标

编辑器应支持：

- 播放中选择当前 Live2D 实例。
- EditMode 下选择 prefab / scene 中的 RawImage 并预览触摸区域。
- 查看当前 base / overlay / effect motion。
- 查看当前 layout。
- 在 RawImage 上拖拽创建 Rect area。
- 拖动/缩放 Rect area。
- 创建 Polygon area，添加、移动、删除顶点。
- 从 MotionLibrary 下拉选择 motion、motion group 或 parameter animation。
- 配置 Tap / Drag / LongPress / Hover / Swipe binding。
- 配置权重、冷却、选择策略。
- 创建和编辑自定义 MotionGroup，把 `idle*`、`main*` 或任意 motion 放入指定 group。
- 配置自定义参数动画的简单参数赋值模式。
- 配置自定义参数动画的曲线编辑视图，优先使用 UI Toolkit 的 CurveField；如果当前 Unity 版本不可用或使用成本高，则使用 IMGUI/EditorGUILayout.CurveField 嵌入。
- 配置自定义 Timeline Event 和 PlayAudio 事件，音频引用使用资源管理 key。
- 实时验证引用和分类。
- 保存到 InteractionProfile asset。

EditMode 预览的准确性取决于 RawImage RectTransform 和目标 profile 数据是否可见；PlayMode 录制仍作为权威结果，因为它能使用实际运行时 Canvas、相机、缩放和模型姿态。

## 10. 外部状态联动设计

第一版不实现复杂状态机，但要预留接口。

### 10.1 条件接口

```csharp
public interface ILive2DInteractionCondition
{
    bool Evaluate(Live2DInteractionContext context);
}
```

Context 包含：

```text
currentBaseMotion
currentOverlayMotion
currentLayout
triggerType
areaId
externalStateProvider
```

### 10.2 外部状态提供者

```csharp
public interface ILive2DExternalStateProvider
{
    bool TryGetBool(string key, out bool value);
    bool TryGetInt(string key, out int value);
    bool TryGetFloat(string key, out float value);
    bool TryGetString(string key, out string value);
}
```

这样可以让业务层注入：

- 好感度
- 当前 UI 页面
- 当前剧情状态
- 是否允许触摸
- 已解锁动作

### 10.3 内置条件

第一版可以提供：

- Always
- CurrentBaseIs
- CurrentBaseInGroup
- OverlayNotPlaying
- ExternalBoolEquals
- ExternalIntCompare

## 11. 旧代码删除建议

可删除或重写的旧方案文件：

- `Live2DTouchMotionController.cs`
- `Live2DUIHitArea.cs`
- `Live2DRawImageHitTester.cs`
- `Live2DUIEventDebugProbe.cs`

需要重写而不是小修的文件：

- `Live2DTouchRuntime.cs`
- `Live2DTouchProfile.cs`
- `Live2DTouchRuntimeEditorTool.cs`

可保留思路但重构的文件：

- `Motion3JsonData.cs`
- `Live2DMotionLibrary.cs`
- `Live2DMotionPlayer.cs`
- `Live2DMotionPlayerDriver.cs`
- Editor 下 Library builder / validator 相关工具。

## 12. 建议的新文件结构

```text
Assets/FRFrame/Third/Live2d/

Runtime/
  Data/
    Live2DMotionLibrary.cs
    Live2DMotionClip.cs
    Live2DMotionGroup.cs
    Live2DMotionReference.cs
    Live2DInteractionProfile.cs
    Live2DTouchArea.cs
    Live2DInteractionBinding.cs
    Live2DParameterAnimationAsset.cs
    Live2DParameterAnimationClip.cs
    Live2DParameterAnimationCurve.cs
    Live2DCustomTimelineEvent.cs
  Motion/
    Motion3JsonData.cs
    Live2DMotionRuntimeData.cs
    Live2DMotionCurveEvaluator.cs
    Live2DParameterAnimationEvaluator.cs
    Live2DPlayableClip.cs
    Live2DCubismBindingCache.cs
    Live2DMotionMixer.cs
    Live2DMotionLayer.cs
    Live2DMotionTrack.cs
    Live2DMotionController.cs
    Live2DMotionRequest.cs
  Interaction/
    Live2DRawImageInputRuntime.cs
    Live2DPointerSession.cs
    Live2DInteractionRecognizer.cs
    Live2DTouchHitTester.cs
    Live2DInteractionExecutor.cs
  Events/
    Live2DMotionEvents.cs
    Live2DInteractionEvents.cs

Editor/
  Live2DMotionLibraryBuilderWindow.cs
  Live2DInteractionProfileEditorWindow.cs
  Live2DTouchAreaOverlay.cs
  Live2DValidator.cs
  Live2DMotionReferenceDrawer.cs
  Live2DInteractionBindingDrawer.cs
```

建议直接新增 `Runtime` 子目录并按上面的结构完全重写。不需要为了保留旧 `.meta` 或旧文件路径而牺牲结构清晰度。

旧文件可在实现新结构后整体删除，保留必要 `.meta` 与资产迁移不是当前约束。

## 13. 完整重写范围建议

既然可以不考虑旧资产兼容，第一版直接实现新模型。除非某项实现成本明显过高，否则不做多版递进。

必须实现：

- MotionLibrary 扫描 model3.json / motions 文件夹。
- 根据名称只生成默认配置，不把名称规则写死为最终行为。
- MotionReference 下拉选择。
- MotionGroup 与选择策略：First、Random、WeightedRandom、Sequence、ShuffleBag。
- MotionGroup 自定义编辑，默认可把 `idle*` 和 `main*` 并入 idle/base group。
- Base / Overlay / Effect 三层播放，Effect 层可配置启用、循环和启动行为。
- Overlay 同时只播放一个，但候选 motion 数量不限制。
- Base 结束后按配置 fallback 到 idle group。
- motion3 UserData 事件派发。
- 自定义 Timeline Event 派发。
- 自定义 ParameterAnimation 资产、编辑器和运行时播放。
- ParameterAnimation 支持简单参数赋值和复杂曲线两种编辑模式。
- ParameterAnimation 可配置播放层：Overlay、Effect、Base 或自定义 Layer。
- PlayAudio 事件接口，音频引用使用 FRFrame 资源管理 key，并可选实现 FRFrame 最小音频服务。
- ParameterAnimation 第一版只支持 CubismParameter，不支持 PartOpacity。
- RawImage normalized Rect area。
- RawImage normalized Polygon area。
- Tap、DragStart、DragRepeat、DragEnd、LongPress、HoverEnter、HoverExit、Swipe。
- InteractionBinding：PlayMotion、PlayMotionGroup、PlayParameterAnimation、StopLayer、SwitchLayout、SendEvent、PlayAudio、SetParameterValue。
- UI Toolkit EditorWindow 管理 profile / layout / area / binding / parameter animation。
- EditMode 区域预览。
- PlayMode 拖拽创建和编辑矩形/多边形区域。
- Validator。

可作为低优先级但仍纳入设计：

- 多 Overlay Layer。
- 复杂外部状态条件编辑器。
- MotionReference 自动重命名迁移。
- 世界空间输入。

## 14. 已确认实现结论

1. `touch_idle*` 是交互反馈候选，不属于 idle；idle 应主要是 `idle*`，`main*` 可作为 base 候选。
2. `main_*`、`home`、`mail`、`mission` 等既可能作为一次性，也可能作为 base 状态；必须靠配置决定，命名规则只给默认值。
3. `main*` 默认可以并入 idle/base group，但系统必须支持用户自定义 group。
4. `login` 这类动作基本可默认配置为一次性。
5. `effect` 看起来更像场景或背景特殊效果，但是否循环、是否需要 Effect 层都应可配置。
6. UserData 不一定存在，也不确定格式；不能依赖它作为唯一事件系统。
7. EditMode 预览和 PlayMode 录制都需要支持，实际效果实现后再评估。
8. 有些触摸反馈没有 motion 文件，必须支持自定义参数动画。
9. 参数动画需要支持简单参数赋值和复杂曲线两种编辑方式。
10. 简单参数动画用表单填写 parameterId、value、applyImmediately；复杂参数动画使用曲线编辑视图。
11. 曲线编辑优先使用 UI Toolkit `CurveField`；如果当前 Unity 版本不可用或成本高，就用 IMGUI `CurveField` 嵌入。
12. 参数动画播放层必须可配置，可以是 Overlay、Effect、Base 或其他自定义层。
13. ParameterAnimation 第一版只支持 CubismParameter；PartOpacity 后续有需要再加。
14. PlayAudio 使用 FRFrame 资源管理 key，不直接绑定最终 AudioClip；可保留 fallback clip 用于编辑器预览。
15. 当前 FRFrame 没有音频模块；Live2D 先通过事件接口支持 PlayAudio，可选实现一个最小 FRFrame 音频服务。
16. 新实现直接使用 `Runtime/`、`Editor/` 分目录完全重写，不考虑旧 `.meta` 路径稳定性。

## 15. 开发实施顺序

建议按以下顺序实现，保证每一步都能单独验证：

1. **清理旧方案与建立目录**：创建 Runtime / Editor 子目录，移除旧 Collider2D / UIHitArea 旁路触摸方案。
2. **Motion 数据层**：实现 MotionLibrary、MotionClip、MotionReference、MotionGroup、model3.json / motions 文件夹扫描。
3. **motion3 运行时解析**：迁移并加强 Motion3JsonData、曲线求值、UserData 解析。
4. **播放核心**：实现 CubismBindingCache、PlayableClip、MotionTrack、MotionLayer、MotionMixer。
5. **调度控制层**：实现 MotionController、MotionRequest、LayerPolicy、Base fallback、Overlay interrupt、Effect 可配置启动。
6. **ParameterAnimation**：实现简单参数赋值、曲线参数动画、事件、可配置播放层。
7. **交互数据层**：实现 InteractionProfile、Layout、TouchArea、InteractionBinding、InteractionAction。
8. **RawImage 输入运行时**：实现 normalized 坐标、Rect / Polygon hit test、Tap / Drag / LongPress / Hover / Swipe recognizer。
9. **事件与音频接口**：实现 CustomTimelineEvent、PlayAudio 事件数据、ILive2DAudioEventHandler，可选最小 FRAudioService。
10. **UI Toolkit 编辑器**：实现 Library Builder、Profile Editor、MotionReference 下拉、Group 编辑、区域编辑、ParameterAnimation 编辑。
11. **PlayMode / EditMode 区域编辑**：实现播放中录制、EditMode 预览、矩形/多边形创建与编辑。
12. **Validator 与调试**：实现引用检查、motion 分类提示、group 检查、area 检查、运行时状态面板。
13. **用典型资源验证**：分别使用 `adaerbote_2` 和 `mojiaduoer_4` 验证简单资源和大量候选 motion 资源。

## 16. 当前结论

后续重构可以按“完全重写但保留正确语义”的方式推进。旧的 Collider2D / UIHitArea 旁路方案直接删除，不做兼容。新系统的核心是：

- 以 MotionLibrary + MotionGroup 管理差异很大的 AzurLane Live2D motion 资源。
- 以 MotionMixer + MotionController 分离播放内核和调度策略。
- 以 RawImage normalized TouchArea 作为唯一主输入命中方案。
- 以 InteractionBinding 取代固定 tapMotions / dragMotions 字段。
- 以 MotionReference + Editor 下拉减少字符串引用问题。
- 以 ParameterAnimation 补足没有 motion3 文件的触摸反馈。
- 以 UI Toolkit EditorWindow 承担主要编辑体验，同时保留轻量运行时 capture 能力来保证播放中录制区域准确。
