# Live2D 工具功能使用说明

本文档介绍 `Assets/FRFrame/Third/Live2d` 下新 Live2D 系统的功能和使用方式。系统目标是在不依赖 Unity AnimatorController 的情况下，直接播放 Cubism `.motion3.json`，并用 RawImage 归一化区域完成点击、拖动、长按、悬停等交互反馈。

示例资源使用：

```text
Assets/GameRes/AzurLane/Art/Live2d/aijier_4
```

该资源包含 `idle`、`idle1`~`idle6`、`main_1`~`main_3`、`touch_head`、`touch_body`、`touch_special`、`touch_idle1`~`touch_idle8`、`touch_drag1`~`touch_drag3`、`effect`、`login`、`home`、`mail`、`mission` 等 motion，适合覆盖待机、基础状态、触摸反馈、拖动反馈和特效层的完整用法。

## 1. 核心资产

### 1.1 Live2DMotionLibrary

`Live2DMotionLibrary` 是单个 Live2D 模型的 motion 数据库。它保存：

- `modelName`：模型名，例如 `aijier_4`。
- `model3Json`：模型的 `.model3.json`。
- `motionFolderPath`：motions 文件夹路径。
- `clips`：所有可播放 `.motion3.json` 条目。
- `groups`：动作分组和选择策略。

每个 `Live2DMotionClip` 表示一个 motion 文件，主要字段：

- `motionId`：运行时播放用 ID，默认来自文件名，例如 `touch_head`。
- `motionAsset`：对应 `.motion3.json` 的 `TextAsset`。
- `category`：默认分类，如 `Idle`、`Base`、`Touch`、`Drag`、`TouchIdle`、`Effect`。
- `loopMode`：循环策略，支持 `UseSource`、`ForceLoop`、`ForceOnce`。
- `defaultLayer`：默认播放层。
- `interruptPolicy`：默认打断策略。
- `fadeIn` / `fadeOut`：可覆盖 motion 内置淡入淡出。
- `events`：外部补充的 Timeline Event。

### 1.2 Live2DMotionGroup

`Live2DMotionGroup` 用于把同类 motion 聚合起来，例如把多个 idle 或 touch_idle 放到一个组里。运行时可以从组里按策略选一个 motion 播放。

组字段：

- `groupId`：组 ID，例如 `idle_base`、`touch`、`drag`、`effect`。
- `selectionMode`：选择策略。
- `groupCooldown`：组级冷却。
- `avoidRepeatLast`：避免连续重复上一次 motion。
- `repeatBlockCount`：当前用于控制是否阻止上一条重复。
- `fallbackGroupId`：当前组不可用时回退到另一个组。
- `candidates`：候选 motion 列表。

选择策略：

- `First`：总是选第一个可用候选。
- `Random`：等概率随机。
- `WeightedRandom`：按 `weight` 加权随机。
- `Sequence`：按顺序循环。
- `ShuffleBag`：洗牌袋，一轮内尽量不重复。

每个候选 `Live2DMotionCandidate` 支持：

- `motion`：`Live2DMotionReference`，指向具体 motion。
- `weight`：权重。
- `cooldown`：候选自身冷却。
- `enabled`：是否启用。

### 1.3 Live2DInteractionProfile

`Live2DInteractionProfile` 描述 RawImage 上的交互区域和触发动作。它保存：

- `motionLibrary`：绑定的 MotionLibrary。
- `defaultLayoutId`：默认布局 ID。
- `layouts`：交互布局列表。
- `layoutBindings`：按当前 base motion 切换 layout 的规则。
- `rawImageTransform`：RawImage 位置/缩放预设。
- `modelTransform`：CubismModel 位置/旋转/缩放预设。

一个 `Live2DInteractionLayout` 包含多个 `Live2DTouchArea`。每个区域使用 RawImage 归一化坐标，支持：

- `Rect`：使用 `normalizedRect`。
- `Polygon`：使用 `normalizedPoints`。

命中规则：忽略禁用区域，按 shape 判断命中，如果多个区域命中，选择 `priority` 最高的区域。

### 1.4 Live2DInteractionBinding 和 Action

每个触摸区域可以有多个 Binding。Binding 定义“某个触发类型发生时，执行哪些动作”。

触发类型：

- `Tap`
- `DragStart`
- `DragRepeat`
- `DragEnd`
- `LongPress`
- `HoverEnter`
- `HoverExit`
- `Swipe`

Binding 字段：

- `trigger`：触发类型。
- `enabled`：是否启用。
- `cooldown`：触发冷却。
- `conditions`：条件列表。
- `actions`：动作列表。

Action 类型：

- `PlayMotion`：播放指定 motion。
- `PlayMotionGroup`：从 motion group 中选择并播放。
- `PlayParameterAnimation`：播放自定义参数动画。
- `StopLayer`：停止指定层。
- `SwitchLayout`：预留的布局切换动作字段。
- `SendEvent`：输出事件日志，后续可接业务事件系统。
- `PlayAudio`：播放音频。
- `SetParameterValue`：直接设置 CubismParameter，并作为持久参数在 motion 层之后覆盖。
- `ToggleParameterValue`：按当前参数值在 Off/On 两个值之间切换，例如 0/1 开关。

## 2. 运行时组件

### 2.1 Live2DMotionController

`Live2DMotionController` 是播放调度入口，建议挂在含 `CubismModel` 的对象上，或挂在能找到子级 `CubismModel` 的对象上。

关键引用：

- `cubismModel`：目标 CubismModel。
- `motionLibrary`：动作库。
- `interactionProfile`：交互配置。
- `audioEventHandler`：实现 `ILive2DAudioEventHandler` 的组件。
- `externalStateProvider`：实现 `ILive2DExternalStateProvider` 的业务状态组件。

启动配置：

- `rebuildLibraryOnAwake`：Awake 时重建 motion runtime cache。
- `playIdleOnStart`：启动后播放待机组。
- `startupIdleGroupId`：默认 `idle_base`。
- `playEffectOnStart`：启动后播放特效组。
- `startupEffectGroupId`：默认 `effect`。

默认层策略：

- `Base`：角色主状态，如 idle、main、home、mail、mission。
- `Overlay`：普通触摸反馈。
- `OverlayExpression`：表情类叠加反馈。
- `OverlayBody`：身体类叠加反馈。
- `OverlayEffect`：额外叠加特效。
- `Effect`：常驻背景/场景特效。

应用顺序：

```text
Effect -> Base -> Overlay -> OverlayExpression -> OverlayBody -> OverlayEffect
```

常用代码调用：

```csharp
controller.PlayGroup("idle_base", Live2DLayerId.Base, Live2DInterruptPolicy.ImmediateCrossFade, "Manual");
controller.PlayGroup("touch", Live2DLayerId.Overlay, Live2DInterruptPolicy.ImmediateCrossFade, "Tap");
controller.PlayMotion("login", Live2DLayerId.Base, Live2DInterruptPolicy.ImmediateCrossFade, "Login");
```

### 2.2 Live2DRawImageInputRuntime

`Live2DRawImageInputRuntime` 挂在显示 Live2D 的 `RawImage` 上，用于把 pointer screen position 转成 RawImage normalized 坐标，并执行交互配置。

关键引用：

- `controller`：Live2DMotionController。
- `profile`：Live2DInteractionProfile。
- `targetRawImage`：目标 RawImage。

手势配置：

- `tapMaxDuration`
- `tapMaxMovePixels`
- `dragStartThresholdPixels`
- `dragRepeatInterval`
- `dragOnlyInsideRawImage`
- `enableLongPress`
- `longPressDuration`
- `enableHover`
- `enableSwipe`
- `swipeMinDistance`
- `acceptTapWhenOverlayPlaying`

调试和录制：

- `showEditorAreaOverlay`：编辑器/播放中在 GameView 显示触摸区域叠加，只看区域时开启即可。
- `enableEditorAreaEditing`：允许在 GameView 的 RawImage 上拖拽录制区域；关闭时只显示区域，不拦截鼠标创建新区域。
- `editorRecordingLayoutId`：录制到哪个 layout。
- `editorRecordingAreaPrefix`：新区域命名前缀。
- `editorRecordingShapeType`：录制为 Rect 或 Polygon。

播放中录制区域时，直接在 GameView 的 RawImage 上按住鼠标拖出矩形，松开后会写入 `profile`。

### 2.3 Live2DSimpleAudioEventHandler

`Live2DSimpleAudioEventHandler` 是默认音频处理器，实现 `ILive2DAudioEventHandler`。

字段：

- `audioSource`：播放用 AudioSource，未指定时自动添加。
- `audioPackageName`：FRFrame 资源包名，可为空。
- `loadAudioKeyFromFRFrameResource`：是否通过 `FRFM.Resource.LoadAsset<AudioClip>` 加载 `audioKey`。
- `masterVolume`
- `sfxVolume`
- `logResourceKeyOnly`

播放优先级：

1. 如果 `fallbackClip` 不为空，直接播放 fallbackClip。
2. 如果有 `audioKey` 且资源模块 ready，通过 FRFrame ResourceManager 同步加载 AudioClip。
3. 如果仍没有 clip，并开启 `logResourceKeyOnly`，输出日志。

## 3. 自定义参数动画

`Live2DParameterAnimationAsset` 用于没有 `.motion3.json` 的局部反馈，例如只改某个参数开关或做一段参数曲线。

创建菜单：

```text
Create / FRFrame / Live2D / Parameter Animation
```

字段：

- `animationId`：动画 ID。
- `displayName`：显示名。
- `mode`：`SimpleSet`、`CurveAnimation`、`Mixed`。
- `duration`：持续时间。
- `loopMode`：循环策略。
- `fadeIn` / `fadeOut`：淡入淡出。
- `defaultLayerId`：默认建议层。
- `simpleParameters`：简单参数设置。
- `curves`：曲线参数动画。
- `events`：Timeline Event。

简单参数设置适合开关：

- `parameterId`：例如某个 CubismParameter ID。
- `value`：常见为 0 或 1。
- `applyImmediately`：立即设置并作为持久参数值参与后续 ResetToDefault 后的重新应用。
- `blendMode`：`Override`、`Additive`、`Multiply`。

曲线模式使用 Unity `AnimationCurve`，适合渐变、反弹、周期变化等反馈。

在交互中使用时，给 Binding 添加 `PlayParameterAnimation` action，并指定：

- `parameterAnimation`
- `layerId`
- `interruptPolicy`
- `parameterApplyMode`

`parameterApplyMode` 为 `Immediate` 时立即设置简单参数；为 `AsTrack` 时进入 MotionMixer，按时间、层和淡入淡出播放。

## 4. 条件系统

交互 Binding 可以配置多个 `Live2DInteractionCondition`。所有条件都通过才执行 actions。

内置条件资产：

- `Live2DBaseMotionCondition`：判断当前 Base motion。
- `Live2DOverlayMotionCondition`：判断当前 Overlay motion。
- `Live2DBaseMotionInGroupCondition`：判断当前 Base motion 是否属于某个 group。
- `Live2DOverlayPlayingCondition`：判断是否有 Overlay/OverlayExpression/OverlayBody/OverlayEffect 正在播放。
- `Live2DAreaCondition`：判断 areaId。
- `Live2DTriggerCondition`：判断 trigger 类型。
- `Live2DExternalBoolCondition`：读取外部 bool 状态。
- `Live2DExternalIntCondition`：读取外部 int 状态并比较。
- `Live2DExternalFloatCondition`：读取外部 float 状态并比较。
- `Live2DExternalStringCondition`：读取外部 string 状态并比较。

外部状态组件需要实现：

```csharp
public interface ILive2DExternalStateProvider
{
    bool TryGetBool(string key, out bool value);
    bool TryGetInt(string key, out int value);
    bool TryGetFloat(string key, out float value);
    bool TryGetString(string key, out string value);
}
```

然后把该组件拖到 `Live2DMotionController.externalStateProvider`。

## 5. 编辑器工具

### 5.1 Motion Library Builder

菜单：

```text
Tools / Live2D / New Runtime / Library Builder
Tools / Live2D / New Runtime / Build Library From Selected Folder
```

推荐流程：

1. 在 Project 面板选中 `Assets/GameRes/AzurLane/Art/Live2d/aijier_4` 文件夹。
2. 执行 `Tools / Live2D / New Runtime / Build Library From Selected Folder`。
3. 确认 `Model Folder` 为 `aijier_4`。
4. 勾选 `Create Interaction Profile`。
5. 勾选 `Overwrite Clips` 和 `Overwrite Default Groups`。
6. 默认勾选 `Create Default Touch Area`，会在默认 layout 中生成一个覆盖整张 RawImage 的 `default_touch` 区域，Tap 时播放 `all_motions` group 的随机 motion，方便批量生成后直接测试。
7. 如需重置已有 `default_touch`，勾选 `Overwrite Default Touch Area`；不勾选时会保留已有区域配置。
8. 默认勾选 `Auto Attach Controller To Prefab`，让工具自动给同目录 prefab 挂载并填充 `Live2DMotionController`。
9. 如果需要默认音频处理器，勾选 `Add Simple Audio Handler`。
10. 点击 `Build`。

批量构建：

1. 在 `Batch Build / Root Folder` 选择一个根目录，例如 `Assets/GameRes/AzurLane/Art/Live2d`。
2. 点击 `Build All Child Model Folders`。
3. 工具会递归查找所有包含 `.model3.json` 的子目录，并对每个目录执行和单目录 `Build` 相同的生成、默认分组、InteractionProfile、默认触摸区域和 prefab 自动挂载流程。
4. 如需反向清理，单目录点击 `Delete Generated Assets And Prefab Components`；批量根目录点击 `Delete All Child Generated Assets And Prefab Components`，会删除生成的 MotionLibrary、InteractionProfile，并从同目录 prefab 移除 `Live2DMotionController` 和 `Live2DSimpleAudioEventHandler`。

工具会生成或更新固定路径资产，重复点击不会生成 `1`、`2` 之类的副本：

```text
aijier_4_Live2DMotionLibrary.asset
aijier_4_Live2DInteractionProfile.asset
```

如果同目录存在 `aijier_4.prefab`，工具会自动查找其中的 `CubismModel`，在模型对象上添加或复用 `Live2DMotionController`，并填入 `cubismModel`、`motionLibrary`、`interactionProfile`。启用 `Add Simple Audio Handler` 时，还会添加或复用 `Live2DSimpleAudioEventHandler` 并填入 `audioEventHandler`。

默认分组规则：

- `idle_base`：`idle*` 和 `main*`。
- `base_once`：非 touch / drag / effect 的 base motion，例如 `login`、`home`、`mail`、`mission`。
- `touch`：`touch_*` 和 `touch_idle*`。
- `drag`：`touch_drag*`。
- `effect`：`effect*`。
- `all_motions`：所有 clips，用于 `default_touch` 的全量随机测试。

对 `aijier_4`，构建后大致会得到：

```text
idle_base:
- idle
- idle1
- idle2
- idle3
- idle4
- idle5
- idle6
- main_1
- main_2
- main_3

touch:
- touch_head
- touch_body
- touch_special
- touch_idle1 ... touch_idle8

drag:
- touch_drag1
- touch_drag2
- touch_drag3

effect:
- effect

base_once:
- complete
- login
- home
- mail
- mission
- mission_complete
- wedding
```

这些只是默认结果，可以按需要手动改组、权重、冷却和策略。

### 5.2 MotionReference 下拉

任何 `Live2DMotionReference` 字段都会显示为：

```text
[Library ObjectField] [Motion Dropdown]
```

先选择 `Live2DMotionLibrary`，右侧会列出该 Library 中的 clips。这样配置 `PlayMotion` 或候选 motion 时不需要手写 motionId。

### 5.3 InteractionProfile Inspector 校验

选中 `Live2DInteractionProfile`，Inspector 下方提供 `Validate Profile` 按钮。它会检查：

- 是否绑定 MotionLibrary。
- layout 是否存在。
- defaultLayoutId 是否有效。
- polygon 是否至少 3 个点。
- PlayMotion 引用是否存在。
- PlayMotionGroup 的 group 是否存在。
- PlayParameterAnimation 是否为空。

### 5.4 Touch Area Editor

菜单：

```text
Tools / Live2D / New Runtime / Touch Area Editor
```

用途：尽量在一个窗口内完成 `Live2DInteractionProfile` 的 layout、area、binding、action、motion 预览和 transform 预设编辑，减少手动展开 SO 字段。

基础用法：

1. 打开窗口。
2. 点击 `自动关联场景对象`，窗口会优先查找场景中的 `Live2DMotionController`、`Live2DRawImageInputRuntime`、`RawImage`，并自动带出 `Interaction Profile` 和 `Motion Library`。
3. 也可以用 `从 Selection 关联`，从当前选中的场景对象向父级/子级查找 Live2D 相关组件。
4. 在 `Layout 编辑` 中新建、复制、删除 layout，修改 `layoutId` / `displayName`，或把当前 layout 设为默认 layout。
5. 在 `Base Motion -> Layout 绑定` 中，将某个 base motion 绑定到某个 layout；PlayMode 下可点击 `绑定当前 Base Motion 到当前 Layout` 快速建立当前动作和布局的关系。
6. 在 `区域画布` 中点击 `Add Rect` 或 `Add Polygon` 添加区域。画布会按 RawImage 宽高比居中显示，不再拉伸成固定黑色区域。
7. `区域画布` 标题右侧可以点击 `隐藏内嵌画布` / `显示内嵌画布`，打开独立画布后可隐藏主编辑器里的内嵌画布，减少界面占用。
8. 点击 `刷新 Game 快照` 可以优先抓取关联 RawImage 的 Texture/RenderTexture 作为画布背景，失败时回退到 GameView 截图；`显示 Game 快照` 用于开关背景显示。
9. 点击 `打开独立画布` 可以打开一个独立编辑窗口，用更大的等比例画布编辑同一套 Profile。
10. Rect 区域选中后，可以在画布中拖动内部移动，拖动边或角缩放；关闭 `拖拽编辑 Rect` 后只显示区域，不进入编辑状态。
11. 开启 `拖拽创建 Rect` 后，可以在预览画布中直接拖出一个新的 Rect 区域。
12. 在 `区域与交互编辑` 中编辑 area 的 `areaId`、`displayName`、`enabled`、`priority`、shape 和 polygon 点位。
13. 在 `Binding / Action 编辑` 中为区域添加 Tap、Drag 等触发，配置 `PlayMotion`、`PlayMotionGroup`、`PlayParameterAnimation`、`PlayAudio`、`SetParameterValue` 等 action。
14. `PlayMotion` 会提供 motion 下拉，并可勾选 `结束后播放指定 Motion`，用于触摸动作自然结束后切到指定 motion，例如 `idle1 -> touch_idle1 -> idle2`。
15. `PlayMotionGroup` 会提供 group 下拉；`SwitchLayout` 会提供 layout 下拉；`SetParameterValue`、`ToggleParameterValue` 和参数动画会通过独立选择窗口选择 Parameter Id，支持搜索、滚轮滚动，并会读取同目录 `.cdi3.json` 显示参数中文/显示名，实际保存仍是参数 Id。
16. 同一个 Binding 可以添加多个 Action，所以一个点击区域可以同时设置或切换多个参数；在 Action 工具栏中可用 `Add Set Param` / `Add Toggle Param` 快速追加参数动作。
17. `PlayParameterAnimation` 可以直接点击 `新建` 在当前 Profile 同目录创建参数动画资产，并在编辑器窗口内快速编辑 Simple Parameters、时长、淡入淡出、循环和默认层。
18. 在 `Motion 预览与关联` 中可以搜索并播放 motion。PlayMode 下点击 motion 按钮会通过当前 `Live2DMotionController` 直接播放，方便观察动作后再绑定对应 layout 或触摸反馈。
19. 在 `RawImage / Model Transform` 中可以把当前 RawImage 的 `anchoredPosition/localScale` 保存到 profile，也可以把 profile 里的预设应用回 RawImage；CubismModel 的 `localPosition/localEulerAngles/localScale` 同理。
20. 修改完成后点击 `保存 Profile Asset`。

注意：该窗口的画布是 normalized 编辑视图，适合编辑和微调配置；如果要在真实 Canvas、真实相机和 GameView 缩放下录制，可以使用 `Live2DRawImageInputRuntime.enableEditorAreaEditing` 在播放中录制。

## 6. 使用 aijier_4 配置完整示例

### 6.1 生成 Library 和 Profile

1. 选中：

```text
Assets/GameRes/AzurLane/Art/Live2d/aijier_4
```

2. 执行：

```text
Tools / Live2D / New Runtime / Build Library From Selected Folder
```

3. 点击 `Build`。

生成后检查 `aijier_4_Live2DMotionLibrary.asset`：

- `clips` 应包含 `idle`、`idle1`~`idle6`、`main_1`~`main_3`、`touch_head`、`touch_body`、`touch_special`、`touch_idle1`~`touch_idle8`、`touch_drag1`~`touch_drag3`、`effect` 等。
- `idle_base` 推荐使用 `ShuffleBag`，避免待机动作频繁重复。
- `touch` 推荐使用 `WeightedRandom` 或 `ShuffleBag`。
- `drag` 推荐使用 `Sequence` 或 `ShuffleBag`。
- `effect` 推荐 `First`。

### 6.2 场景对象配置

准备一个显示 Live2D 的 UI：

```text
Canvas
└── RawImage_Live2D
```

Live2D Cubism 模型可以放在合适的对象层级中，只要 `Live2DMotionController` 能引用到 `CubismModel` 即可。

给 CubismModel 所在对象添加：

```text
Live2DMotionController
Live2DSimpleAudioEventHandler（可选）
```

配置 `Live2DMotionController`：

- `cubismModel`：拖入 `aijier_4` 的 CubismModel。
- `motionLibrary`：拖入 `aijier_4_Live2DMotionLibrary.asset`。
- `interactionProfile`：拖入 `aijier_4_Live2DInteractionProfile.asset`。
- `audioEventHandler`：拖入同对象上的 `Live2DSimpleAudioEventHandler`。
- `playIdleOnStart`：开启。
- `startupIdleGroupId`：`idle_base`。
- `playEffectOnStart`：如果希望启动即播放背景特效，开启。
- `startupEffectGroupId`：`effect`。

给 `RawImage_Live2D` 添加：

```text
Live2DRawImageInputRuntime
```

配置：

- `controller`：拖入 Live2DMotionController。
- `profile`：拖入 `aijier_4_Live2DInteractionProfile.asset`。
- `targetRawImage`：拖入自身 RawImage。
- `debugDrawOverlay`：调试时开启。

### 6.3 创建触摸区域

打开：

```text
Tools / Live2D / New Runtime / Touch Area Editor
```

选择 `aijier_4_Live2DInteractionProfile.asset`，使用 `Default` layout。

建议先创建这些区域：

```text
Head
Body
Special
DragBody
```

如果用播放中录制：

1. 进入 PlayMode。
2. 选中带 `Live2DRawImageInputRuntime` 的 RawImage。
3. 开启 `enableEditorAreaEditing`。
4. 设置 `editorRecordingLayoutId = Default`。
5. 在 GameView 的 Live2D RawImage 上拖拽头部、身体等区域。
6. 松开鼠标后区域会写入 Profile。
7. 退出 PlayMode 前确认 Profile 已被标记保存。

录制后建议手动修改区域：

- `areaId`：`head`、`body`、`special`、`drag_body`。
- `displayName`：`Head`、`Body`、`Special`、`Drag Body`。
- `priority`：头部或特殊区域可高于身体，避免重叠误触。

### 6.4 配置点击反馈

在 `head` 区域添加 Binding：

```text
trigger: Tap
cooldown: 0.3
```

添加 Action：

```text
actionType: PlayMotion
motion: aijier_4_Live2DMotionLibrary / touch_head
layerId: Overlay
interruptPolicy: ImmediateCrossFade
```

也可以改为播放组：

```text
actionType: PlayMotionGroup
groupId: touch
layerId: Overlay
interruptPolicy: ImmediateCrossFade
```

如果希望头部点击更偏向 `touch_head`，可以在 `touch` group 中把 `touch_head` 权重调高，或者单独建 `touch_head` group。

在 `body` 区域添加 Tap Binding：

```text
actionType: PlayMotion
motion: touch_body
layerId: OverlayBody
interruptPolicy: ImmediateCrossFade
```

这里用 `OverlayBody` 的好处是，身体反馈可以和表情类 Overlay 分开扩展。

在 `special` 区域添加 Tap Binding：

```text
actionType: PlayMotion
motion: touch_special
layerId: OverlayEffect
interruptPolicy: ImmediateCrossFade
```

### 6.5 配置拖动反馈

在 `drag_body` 区域添加：

```text
trigger: DragStart
```

Action 可选：

```text
actionType: PlayMotionGroup
groupId: drag
layerId: OverlayBody
interruptPolicy: ImmediateCrossFade
```

再添加 `DragRepeat` Binding：

```text
trigger: DragRepeat
cooldown: 0.15
```

Action：

```text
actionType: PlayMotionGroup
groupId: drag
layerId: OverlayBody
interruptPolicy: ImmediateCrossFade
```

如果 `touch_drag1`、`touch_drag2`、`touch_drag3` 不希望连续重复，可设置 `drag` group：

```text
selectionMode: ShuffleBag
avoidRepeatLast: true
groupCooldown: 0.1
```

### 6.6 配置待机和基础状态

`aijier_4` 的 `idle`、`idle1`~`idle6`、`main_1`~`main_3` 默认会进入 `idle_base`。推荐：

```text
groupId: idle_base
selectionMode: ShuffleBag
avoidRepeatLast: true
```

`Live2DMotionController` 在 Base motion 自然结束后会自动调用：

```text
PlayGroup(startupIdleGroupId, Base, ImmediateCrossFade, "IdleFallback")
```

如果某些 `main_*` 不想作为待机循环，可以从 `idle_base` 移到自定义组，例如：

```text
groupId: main_state
candidates: main_1, main_2, main_3
selectionMode: Sequence
```

然后由业务代码或交互 action 播放：

```csharp
controller.PlayGroup("main_state", Live2DLayerId.Base, Live2DInterruptPolicy.QueueUntilFinish, "MainState");
```

### 6.7 配置 effect

如果 `effect.motion3.json` 是背景/场景常驻效果：

1. 在 `aijier_4_Live2DMotionLibrary.asset` 中确认 `effect` clip 的 `category = Effect`。
2. `loopMode` 可按资源实际效果设为 `UseSource` 或 `ForceLoop`。
3. 在 `Live2DMotionController` 开启：

```text
playEffectOnStart: true
startupEffectGroupId: effect
```

如果只想在某个交互时播放 effect，可关闭 `playEffectOnStart`，在 Binding 中添加：

```text
actionType: PlayMotionGroup
groupId: effect
layerId: Effect
interruptPolicy: ImmediateCrossFade
```

### 6.8 配置音频

给 `Live2DMotionController` 同对象添加 `Live2DSimpleAudioEventHandler`，并拖到 `audioEventHandler`。

如果音频走 FRFrame ResourceManager：

```text
loadAudioKeyFromFRFrameResource: true
audioPackageName: 按项目资源包配置填写，可为空
```

在触摸 Binding 的 actions 中添加：

```text
actionType: PlayAudio
audioKey: Audio/Live2D/aijier_4/touch_head
volume: 1
pitch: 1
```

如果只是临时测试，可以直接拖一个 `fallbackClip`。

motion 或参数动画的 Timeline Event 中也可以添加 `PlayAudio` 类型事件，播放到对应时间点时由 Controller 派发给 audio handler。

### 6.9 配置参数动画反馈

例如希望点击后追加一个没有 motion 文件的参数反馈：

1. 创建 `Live2DParameterAnimationAsset`。
2. 设置：

```text
animationId: touch_blush
duration: 0.3
loopMode: ForceOnce
defaultLayerId: OverlayExpression
mode: SimpleSet
```

3. 在 `simpleParameters` 添加：

```text
parameterId: 实际 CubismParameter ID
value: 1
applyImmediately: false
blendMode: Override
```

4. 在 `head` 的 Tap Binding actions 中追加：

```text
actionType: PlayParameterAnimation
parameterAnimation: touch_blush
layerId: OverlayExpression
interruptPolicy: ImmediateCrossFade
parameterApplyMode: AsTrack
```

如果只是永久切换某个参数开关，把 `parameterApplyMode` 设为 `Immediate`，并在 simple parameter 上勾选 `applyImmediately`。

### 6.10 配置条件

例如只在当前 base 属于 `idle_base` 时允许点击头部：

1. 创建 `Live2DBaseMotionInGroupCondition`。
2. 设置：

```text
groupId: idle_base
invert: false
```

3. 把该 condition 加到 `head` Tap Binding 的 `conditions`。

例如剧情中禁止触摸：

1. 业务组件实现 `ILive2DExternalStateProvider`，让 `TryGetBool("live2d_touch_enabled", out value)` 返回当前状态。
2. 拖到 `Live2DMotionController.externalStateProvider`。
3. 创建 `Live2DExternalBoolCondition`：

```text
key: live2d_touch_enabled
compareMode: Equals
expectedValue: true
```

4. 加到所有需要受限制的 Binding。

## 7. 推荐检查清单

配置完成后按以下顺序检查：

1. `Live2DMotionLibrary.BuildRuntimeCache()` 能找到所有 motion。
2. `idle_base` 至少包含一个可播放 motion。
3. `Live2DMotionController` 能找到 `CubismModel`。
4. `Live2DRawImageInputRuntime.targetRawImage` 指向正确 RawImage。
5. GameView 中开启 `debugDrawOverlay` 后能看到触摸区域。
6. 点击区域后 Console 中没有 motion missing 或 audio missing 的错误。
7. Overlay 播放时不会错误打断 Base。
8. Base 一次性动作结束后能回到 `idle_base`。
9. `Validate Profile` 没有关键引用错误。
10. 如果使用 audioKey，确认 FRFrame ResourceManager 已初始化且资源 key 能加载 AudioClip。

## 8. 常见问题

### 点击没有反应

检查：

- RawImage 上是否有 `Live2DRawImageInputRuntime`。
- 场景中是否有 EventSystem。
- RawImage 是否开启 Raycast Target。
- `targetRawImage` 是否正确。
- `profile` 是否有 layout 和 area。
- 触摸点是否命中区域，可开启 `debugDrawOverlay`。
- Binding 的 trigger 是否是实际触发类型。
- Binding conditions 是否全部通过。

### motion 播放失败

检查：

- `motionLibrary` 是否赋值。
- 是否执行过 Builder 或运行时 `rebuildLibraryOnAwake`。
- `motionId` 是否存在于 clips。
- `motionAsset` 是否是 `.motion3.json` TextAsset。
- 目标 CubismParameter ID 是否能在当前 CubismModel 中找到。

### 音频 key 没声音

检查：

- `audioEventHandler` 是否赋值。
- `Live2DSimpleAudioEventHandler.loadAudioKeyFromFRFrameResource` 是否开启。
- `FRFM.Resource` 是否 ready。
- `audioPackageName` 是否正确。
- `audioKey` 是否能通过 ResourceManager 加载到 AudioClip。
- 可先用 `fallbackClip` 验证 AudioSource 和音量链路。

### 区域录制不准确

优先使用 PlayMode 下 `Live2DRawImageInputRuntime.enableEditorAreaEditing` 在真实 GameView 中录制。EditMode 的 Touch Area Editor 适合归一化布局粗调，PlayMode 录制更接近真实 Canvas、相机、缩放和 RawImage 状态。
