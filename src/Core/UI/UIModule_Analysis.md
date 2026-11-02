# UI 模块新功能详细设计

## 设计目标

`Assets/FRFrame/Core/UI` 当前已经具备完整的 UI 运行时、配置、绑定、编辑器和内置服务能力。本次设计目标是把前期分析中的所有建议点统一落地为可执行的完整方案，后续开发时不再区分优先级和阶段，直接按本文逐项实现。

总体原则：

- 保持现有 `FRFM.UI`、`UIManager`、`UIForm`、`UIConfigGroup`、绑定代码生成等核心使用方式不变。
- 修复已有 public 字段或枚举未生效的问题，避免业务调用产生误解。
- 明确 UI 资源 package、配置 group、assetPath 三者语义，适配当前 Resource 模块多 package 设计。
- 增强复杂 UI 场景需要的多实例、导航失败恢复、打开结果、诊断和服务状态查询。
- 编辑器工具优先保证校验准确、生成稳定、路径匹配明确。
- 不引入独立的新 UI 框架抽象，所有能力收敛到现有 UI 模块。

## 设计范围

运行时涉及目录：

- `Assets/FRFrame/Core/UI/Runtime/Core`
- `Assets/FRFrame/Core/UI/Runtime/Config`
- `Assets/FRFrame/Core/UI/Runtime/Services`
- `Assets/FRFrame/Core/UI/Runtime/Builtin`
- `Assets/FRFrame/Core/UI/Runtime/Binding`
- `Assets/FRFrame/Core/UI/Runtime/Extensions`

编辑器涉及目录：

- `Assets/FRFrame/Core/UI/Editor/Binding`
- `Assets/FRFrame/Core/UI/Editor/CodeGen`
- `Assets/FRFrame/Editor/UI/Config`
- `Assets/FRFrame/Editor/Diagnostics`

新增或扩展的能力包括：

1. 预加载模式语义补齐。
2. `UIFormState.Loading` 语义治理。
3. Dialog mask click 参数生效。
4. Toast 位置、优先级和重复合并。
5. Loading/Dialog cancellation 生命周期释放。
6. UI 配置 package 语义治理。
7. 多实例窗口 API。
8. 导航失败恢复和结果对象。
9. 遮挡语义拆分。
10. UI 运行时诊断 API 和 Editor 窗口。
11. Mask 层级结构优化。
12. SetInactive / Hidden 生命周期治理。
13. Slide 动画坐标修正。
14. 绑定代码生成稳定性优化。
15. UIConfig 校验收紧。
16. 配置编辑器路径匹配优化。
17. 内置服务状态快照。
18. 打开结果型 API。
19. UI 模块使用说明文档和项目级 skill。

## 一、预加载模式语义补齐

### 当前问题

`UIPreloadMode` 已存在多个枚举值，但 `UIManager.PreloadAsync()` 当前只显式区分 `AssetOnly`，其它值都走实例预热逻辑，导致 `AssetAndInstance` 没有独立语义。

### 设计方案

保留现有枚举名，明确语义：

```csharp
public enum UIPreloadMode
{
    AssetOnly,
    InstanceWarmup,
    AssetAndInstance
}
```

`UIManager.PreloadAsync(string formId, UIPreloadMode mode)` 行为：

- `AssetOnly`
  - 只加载并缓存 prefab 资源。
  - 不创建 UIForm 实例。
  - 记录到 preload cache，供 `IsPreloaded` 和诊断使用。
- `InstanceWarmup`
  - 创建 UIForm 实例并立即关闭到缓存或回收路径。
  - 不额外重复加载已经缓存的资源。
  - 用于减少首次打开实例化成本。
- `AssetAndInstance`
  - 先确保资源已加载并进入 preload cache。
  - 再执行实例预热。
  - 诊断中同时标记 `AssetPreloaded=true` 与 `InstanceWarmed=true`。

### 数据结构

新增或扩展：

```csharp
public sealed class UIPreloadInfo
{
    public string FormId;
    public UIPreloadMode Mode;
    public bool AssetPreloaded;
    public bool InstanceWarmed;
    public string PackageName;
    public string AssetPath;
    public float LastPreloadTime;
}
```

`UIManager` 内部 preload 字典改为保存 `UIPreloadInfo`，而不是单纯记录 formId。

### 兼容性

现有调用保持不变。原本传入 `InstanceWarmup` 的调用仍会预热实例；`AssetAndInstance` 开始具备明确行为。

## 二、UIFormState.Loading 语义治理

### 当前问题

`UIFormState.Loading` 已定义但没有形成稳定使用。加载状态主要由 `UIManager` loading operation 字典维护。

### 设计方案

保留并正式使用 `UIFormState.Loading`，但限定其含义：

- 资源还没实例化出来时，不存在 `UIForm` 实例，因此不能设置 form 状态。
- prefab 或代码根已创建、`UIForm` 组件已获取之后，到 `InternalInitialize` 完成前，状态为 `Loading`。
- `BindInit`、参数应用、注册 group、`InternalOpen` 完成后进入 `Opening` / `Opened`。
- 创建失败时直接 Dispose，不暴露半初始化 form。

### API 调整

`UIForm` 增加内部方法：

```csharp
internal void InternalSetLoading();
```

或在现有状态设置链路中允许 `UIManager` 设置：

```csharp
form.State = UIFormState.Loading;
```

如果 `State` 当前为 private set，则通过 internal 方法完成。

### 诊断展示

`UIFormSnapshot.State` 必须包含 Loading 状态。

`UIManagerSnapshot.LoadingForms` 同时展示：

- 还没有 form 实例的 loading operation。
- 已有 form 实例且状态为 Loading 的 form。

## 三、Dialog CloseOnMaskClick 生效

### 当前问题

`UIDialogParams.CloseOnMaskClick` 字段存在，但没有传递到实例级遮罩逻辑。

### 设计方案

增加实例级遮罩覆盖，不修改共享 `UIFormConfig`。

`UIOpenOptions` 增加：

```csharp
public bool? CloseOnMaskClickOverride;
public bool? UseMaskOverride;
public UIMaskMode? MaskModeOverride;
public Color? MaskColorOverride;
```

`UIForm` 增加只读运行时属性：

```csharp
public bool UseMask => _useMaskOverride ?? Config.useMask;
public bool CloseOnMaskClick => _closeOnMaskClickOverride ?? Config.closeOnMaskClick;
public UIMaskMode MaskMode => _maskModeOverride ?? Config.maskMode;
public Color MaskColor => _maskColorOverride ?? Config.maskColor;
```

内部初始化时应用 open options：

```csharp
internal void ApplyOpenOptions(UIOpenOptions options);
```

`UIMaskService` 不再直接读取 `form.Config.closeOnMaskClick`，改为读取 `form.CloseOnMaskClick`。

### Dialog 接入

`UIDialogService.ShowAsync(UIDialogParams args)` 打开 Dialog 时：

```csharp
var options = new UIOpenOptions
{
    UserData = args,
    CloseOnMaskClickOverride = args.CloseOnMaskClick
};
```

如果 `UIDialogParams` 后续需要覆盖 mask 颜色或模式，也走同一路径。

## 四、Toast Position、Priority 和重复合并

### 当前问题

`UIToastRequest.Position` 和 `Priority` 当前未被服务实际消费。

### 设计方案

### UIToastRequest 扩展

```csharp
public sealed class UIToastRequest
{
    public string Message;
    public float Duration;
    public UIToastPosition Position;
    public int Priority;
    public bool MergeSameMessage;
    public float MergeWindowSeconds;
}
```

默认值：

- `Position = UIToastPosition.Top`
- `Priority = 0`
- `MergeSameMessage = true`
- `MergeWindowSeconds = 0.5f`

### 队列规则

`UIToastService` 内部队列从 FIFO 改为优先级队列：

- priority 高的先显示。
- priority 相同时保持入队顺序。
- 如果 `MergeSameMessage=true`，短时间内相同 message + position 的请求合并。
- 合并时刷新 duration，增加 repeat count。

内部结构：

```csharp
internal sealed class UIToastQueueItem
{
    public UIToastRequest Request;
    public int Sequence;
    public int RepeatCount;
    public float EnqueueTime;
}
```

### UIToastForm 接口

`UIToastForm` 支持请求参数：

```csharp
public void SetRequest(UIToastRequest request, int repeatCount);
```

位置应用规则：

- Top：anchorMin/anchorMax 顶部，pivot y = 1。
- Center：居中。
- Bottom：底部，pivot y = 0。

若已有布局节点，则只调整 Toast content root，不改变整个 form root。

## 五、Loading / Dialog cancellation 生命周期释放

### 当前问题

Loading timeout source 和 cancellation registration 没有集中释放。Dialog 的注册释放也可统一。

### 设计方案

### Loading 上下文

新增内部类型：

```csharp
internal sealed class UILoadingContext : IDisposable
{
    public int Id;
    public UILoadingRequest Request;
    public CancellationTokenSource TimeoutSource;
    public CancellationTokenRegistration ExternalRegistration;
    public bool IsDisposed;

    public void Dispose();
}
```

`UILoadingService` 维护：

```csharp
private readonly Dictionary<int, UILoadingContext> _contexts;
private int _nextContextId;
private int _visibleCount;
```

行为：

- `ShowAsync` 创建 context。
- `Hide()` 释放最近或全部有效 context。
- `ShowScope()` 返回的 IDisposable Dispose 时释放对应 context。
- timeout 触发后释放 context 并刷新显示。
- `UIManager.Destroy()` 或服务 Dispose 时释放所有 context。

### Dialog 上下文

新增内部类型：

```csharp
internal sealed class UIDialogContext : IDisposable
{
    public UIDialogParams Params;
    public CancellationTokenSource TimeoutSource;
    public CancellationTokenRegistration ExternalRegistration;
    public void Dispose();
}
```

`ShowAsync` 用 try/finally 保证释放。

## 六、UI 配置 package 语义治理

### 当前问题

`UIFormConfig.configGroup` 既像配置组名，又被当作 Resource packageName 使用，语义混乱。

### 设计方案

### UIFormConfig 扩展

新增字段：

```csharp
public string packageName;
```

含义：该 form 资源所在 YooAsset package。为空时使用所属 config group 的默认 package。

### UIConfigGroup 扩展

新增字段：

```csharp
public string defaultPackageName;
```

含义：该组下 form 的默认 YooAsset package。

### 资源解析规则

新增工具方法：

```csharp
internal string ResolvePackageName(UIFormConfig config)
```

优先级：

1. `config.packageName`
2. `UIConfigGroup.defaultPackageName`
3. `config.configGroup` 兼容旧数据
4. null，交给 Resource 默认包

如果走第 3 条 fallback，输出一次 warning：

```text
UI config uses configGroup as package fallback. Please set packageName/defaultPackageName explicitly.
```

### UIManager 加载调整

当前：

```csharp
new ResourceLoadOptions { PackageName = config.configGroup }
```

改为：

```csharp
new ResourceLoadOptions { PackageName = ResolvePackageName(config) }
```

### UIConfig Editor 调整

配置组编辑区增加：

- `Default Package Name` 输入框。
- 从 YooAsset package 配置中选择 package 的下拉框，如果当前项目能读取配置。

Form 编辑区增加：

- `Package Name Override` 输入框。
- 空值表示使用 group 默认 package。

批量设置支持：

- 设置 default package。
- 批量设置 selected forms package override。
- 清空 selected forms package override。

Validator 检查：

- assetPath 非空且解析 packageName 为空时 warning。
- packageName 存在但未在 YooAsset 配置中找到时 warning 或 error，取决于配置编辑器能否读取包配置。

## 七、多实例窗口 API 增强

### 当前问题

多实例窗口缺少 top、all、serialId 维度操作。

### 设计方案

`UIManager` 新增 API：

```csharp
UIForm GetTop(string formId);
T GetTop<T>() where T : UIForm;
IReadOnlyList<UIForm> GetAllOpen(string formId);
bool CloseTop(string formId);
bool CloseTop<T>() where T : UIForm;
int CloseAll(string formId);
int CloseAll<T>() where T : UIForm;
bool TryGetBySerialId(int serialId, out UIForm form);
UniTask<bool> CloseAsync(int serialId);
bool Close(int serialId);
```

### 排序规则

`GetTop(formId)` 不再依赖 `_formsById[formId][0]`，而是：

1. 找到该 formId 所有未 disposed 的 form。
2. 按 layer sorting order 和 group 内 depth 判断顶层。
3. 返回最上方且 state 为 Opened/Opening/Hidden 中可操作的实例。

`CloseTop(formId)` 使用同样规则。

`CloseAll(formId)` 对当前快照列表逐个关闭，避免关闭过程中修改集合导致迭代异常。

### 诊断

`UIFormSnapshot` 增加：

- `SerialId`
- `FormId`
- `Layer`
- `Depth`
- `SortingOrder`
- `IsTopOfFormId`

## 八、导航失败恢复和结果对象

### 当前问题

`UINavigator.PushAsync()` 在打开新页面失败时不会恢复之前被 Hide 的页面。

### 设计方案

### 导航结果

新增：

```csharp
public sealed class UINavigationResult
{
    public bool Success;
    public UIForm Form;
    public UIForm PreviousForm;
    public UINavigationFailureReason FailureReason;
    public string Error;
    public bool PreviousRestored;
}

public enum UINavigationFailureReason
{
    None,
    OpenFailed,
    Canceled,
    Timeout,
    PreviousCloseFailed
}
```

保留现有 `PushAsync<T>()` 返回 form 的 API，同时新增：

```csharp
UniTask<UINavigationResult> TryPushAsync<T>(UINavigationOptions options = null) where T : UIForm;
UniTask<UINavigationResult> TryReplaceAsync<T>(UINavigationOptions options = null) where T : UIForm;
```

### Push 恢复规则

- `HidePrevious`
  - 打开新页面前记录 previous。
  - 新页面打开失败时，如果 previous 仍存在，调用 `Show(previous)`。
  - `PreviousRestored=true`。
- `ClosePrevious`
  - 默认改为“新页面打开成功后再关闭 previous”。
  - 如果必须保持旧行为，增加选项 `ClosePreviousBeforeOpen`，默认 false。
- `KeepPrevious`
  - 失败时无需恢复。

### Back 规则

Back 关闭栈顶失败时，不弹出导航栈；关闭成功后再移除 entry。

## 九、遮挡语义拆分

### 当前问题

`fullScreen` 同时承担视觉全屏和遮挡下方窗口生命周期的语义。

### 设计方案

新增枚举：

```csharp
public enum UIBlockingMode
{
    None,
    FullScreenOnly,
    Always,
    MaskOnly
}
```

`UIFormConfig` 新增：

```csharp
public UIBlockingMode blockingMode = UIBlockingMode.FullScreenOnly;
```

语义：

- `None`：不遮挡下方窗口。
- `FullScreenOnly`：当 `fullScreen=true` 时遮挡下方窗口。
- `Always`：无论 fullScreen 是否为 true，都遮挡下方窗口。
- `MaskOnly`：仅当 useMask 为 true 时遮挡下方窗口。

`UIGroup.ApplyCoverState()` 改为调用：

```csharp
bool BlocksUnderlying(UIForm form)
```

兼容：

- 旧配置未设置时默认 `FullScreenOnly`，保持原行为。
- 配置编辑器新增 blockingMode 字段。

## 十、UI 运行时诊断 API 和窗口

### 当前问题

UI 缺少专门诊断 API 和 Editor 可视化窗口。

### 设计方案

### 快照类型

新增文件建议：

`Assets/FRFrame/Core/UI/Runtime/Core/UIDiagnosticsTypes.cs`

类型：

```csharp
public sealed class UIManagerSnapshot
{
    public int OpenFormCount;
    public int LoadingCount;
    public int PreloadCount;
    public IReadOnlyList<UIGroupSnapshot> Groups;
    public IReadOnlyList<UIFormSnapshot> Forms;
    public IReadOnlyList<UILoadingOperationSnapshot> LoadingOperations;
    public IReadOnlyList<UIPreloadSnapshot> Preloads;
    public UINavigationSnapshot Navigation;
    public UIMaskSnapshot Mask;
    public UIServiceSnapshot Services;
}

public sealed class UIGroupSnapshot
{
    public UILayer Layer;
    public string Name;
    public int BaseSortingOrder;
    public int FormCount;
    public string TopFormId;
    public IReadOnlyList<int> SerialIds;
}

public sealed class UIFormSnapshot
{
    public int SerialId;
    public string FormId;
    public string TypeName;
    public UILayer Layer;
    public UIFormState State;
    public int Depth;
    public int SortingOrder;
    public bool Visible;
    public bool IsPaused;
    public bool IsCovered;
    public bool UseMask;
    public bool CloseOnMaskClick;
    public string PackageName;
    public string AssetPath;
}

public sealed class UINavigationSnapshot
{
    public int Count;
    public IReadOnlyList<UINavEntrySnapshot> Entries;
}

public sealed class UIMaskSnapshot
{
    public IReadOnlyList<UIMaskLayerSnapshot> Layers;
}

public sealed class UIServiceSnapshot
{
    public UIToastServiceSnapshot Toast;
    public UIDialogServiceSnapshot Dialog;
    public UILoadingServiceSnapshot Loading;
}
```

### UIManager API

```csharp
UIManagerSnapshot CreateSnapshot();
void GetAllFormSnapshots(List<UIFormSnapshot> results);
```

各服务和组件提供内部 snapshot 方法。

### Editor 窗口

新增：

`Assets/FRFrame/Editor/Diagnostics/FRFrameUIDiagnosticsWindow.cs`

菜单：

```text
FRFrame/Diagnostics/UI Diagnostics
```

窗口内容：

- 总览：打开数量、loading 数量、preload 数量。
- Groups：每层窗口顺序、top form、sorting order。
- Forms：formId、serialId、type、state、layer、depth、mask、package、assetPath。
- Navigation：栈列表。
- Mask：每层 mask 是否显示、target form、sorting order。
- Loading Operations：formId、elapsed、timeout、canceled。
- Preloads：formId、mode、asset/instance 状态。
- Services：Toast/Dialog/Loading 队列和当前状态。

操作按钮：

- Refresh。
- Close selected form。
- Close all by formId。
- Release selected preload。
- Clear navigation stack。
- Copy snapshot JSON。

所有修改型按钮只影响运行时状态，不写项目文件。

## 十一、UIMaskService 层级结构优化

### 当前问题

mask 挂在 UIRoot 下，逻辑上属于 layer，但 Hierarchy 不直观。

### 设计方案

优先方案：mask 放入对应 `UIGroup` root 下。

`UIMaskService.Initialize` 从：

```csharp
Initialize(Transform uiRoot, IReadOnlyDictionary<UILayer, UIGroup> groups)
```

调整为可访问 group root：

```csharp
Initialize(IReadOnlyDictionary<UILayer, UIGroup> groups)
```

每个 mask：

- parent = group.Root
- sibling index 放在当前 group root 下最前或按 sorting order 控制。
- 名称：`Mask_{Layer}`。

如果调整 parent 影响排序，则保留现有 sorting order 逻辑，同时诊断窗口展示 mask 所属 group。

## 十二、SetInactive 与 Hidden 生命周期治理

### 当前问题

`SetInactive` 遮挡模式会触发 Unity `OnDisable/OnEnable`，cache/resident hidden 窗口使用 CanvasGroup 隐藏但第三方组件仍可能 Update。

### 设计方案

### 配置扩展

`UIFormConfig` 新增：

```csharp
public bool disableGameObjectWhenHidden;
```

行为：

- `InternalHide()` 时，如果 `disableGameObjectWhenHidden=true`，在完成框架 `OnHide` 后 SetActive(false)。
- `InternalShow()` 时，先 SetActive(true)，再恢复 CanvasGroup 和调用 `OnShow`。

### 文档语义

`SetInactive` cover mode 标记为高级模式：

- 会触发 Unity 生命周期。
- 适合确实希望第三方组件暂停的窗口。
- 普通场景推荐 `CanvasGroup`。

### 状态保护

无论 GameObject 是否 active，`UIForm.State` 仍以框架状态为准。`OnEnable/OnDisable` 不驱动框架状态变化。

## 十三、默认 Slide 动画坐标修正

### 当前问题

`UIDefaultAnimationPlayer` 用 `Screen.width/height` 计算 Slide 位移，未考虑 CanvasScaler 和 RectTransform 坐标。

### 设计方案

新增工具方法：

```csharp
private static Vector2 GetSlideOffset(RectTransform rectTransform, UISlideDirection direction)
```

计算规则：

1. 优先取 root canvas 的 RectTransform rect size。
2. 若取不到 canvas，则取当前 form RectTransform rect size。
3. x 方向使用 canvasWidth + formWidth。
4. y 方向使用 canvasHeight + formHeight。
5. 所有位移使用 anchoredPosition 坐标系。

避免使用 `Screen.width/height`。

## 十四、绑定代码生成稳定性

### 当前问题

`UICodeGenerator` 每次直接覆盖 `*.Bind.cs`，内容不变也会触发刷新和版本噪音。

### 设计方案

### 写入方法

新增：

```csharp
private static bool WriteTextIfChanged(string path, string content)
```

行为：

- 统一 `\n` 换行。
- 文件不存在时写入。
- 文件存在且内容相同则不写入，不调用 AssetDatabase.Refresh。
- 内容不同才写入并返回 true。

### 绑定 hash

可选在生成文件头部加入：

```csharp
// BindingHash: xxxxxxxx
```

hash 来源：绑定字段名、类型、路径、事件设置、widget 标记。

### 预览 diff

编辑器 Inspector 增加按钮：

- `Preview Generated Diff`

可作为后续增强，不阻塞基础实现。

## 十五、UIConfig 校验规则收紧

### 当前问题

Validator 允许 UIWidget/UIItem 类配置为 form logicType，但运行时打开要求 UIForm。

### 设计方案

`UIConfigGroupDraftValidator` 调整：

- form 配置的 `logicType` 必须继承 `UIForm`。
- 不接受 `UIWidget`、`UIItem`、抽象类、泛型类型。
- `ScanUnconfiguredUITypes()` 与 validator 保持一致，只扫描非抽象 `UIForm`。

错误文案：

```text
logicType must inherit UIForm for UIFormConfig.
```

如果后续需要 Widget 配置，应新增独立 `UIWidgetConfig`，不在本次混入。

## 十六、配置编辑器资源路径匹配优化

### 当前问题

同名 Prefab 自动匹配只返回第一个候选，helper 命名 `ConvertAssetPathToResourcesRuntimePath` 容易误导。

### 设计方案

### 命名调整

新增准确方法：

```csharp
NormalizeAssetPath(string assetDatabasePath)
```

旧方法保留为 wrapper 并标记 obsolete 或仅内部迁移。

### 多候选处理

`FindBestRuntimeAssetPathByName` 改为：

```csharp
IReadOnlyList<string> FindRuntimeAssetPathCandidates(string prefabName);
string SelectBestRuntimeAssetPath(string prefabName, string packageName, string currentGroupPath);
```

选择规则：

1. packageName 对应目录优先。
2. 当前 UIConfigGroup 所在目录附近优先。
3. 路径包含 `/UI/` 的优先。
4. 多候选仍无法判断时，在编辑器中弹出选择列表。

UIConfig Editor 在自动填充 assetPath 时，如果多候选，显示选择对话框，不静默选择第一个。

## 十七、服务状态查询

### 当前问题

Toast/Dialog/Loading 服务状态不可查询。

### 设计方案

新增 snapshot：

```csharp
public sealed class UIToastServiceSnapshot
{
    public bool IsShowing;
    public string CurrentMessage;
    public int QueueCount;
    public IReadOnlyList<string> QueuedMessages;
}

public sealed class UIDialogServiceSnapshot
{
    public bool IsShowing;
    public string CurrentTitle;
    public int QueueCount;
}

public sealed class UILoadingServiceSnapshot
{
    public bool IsShowing;
    public int RefCount;
    public string Message;
    public float Progress;
    public int ContextCount;
    public bool HasTimeout;
}
```

服务 API：

```csharp
UIToastServiceSnapshot CreateSnapshot();
UIDialogServiceSnapshot CreateSnapshot();
UILoadingServiceSnapshot CreateSnapshot();
```

纳入 `UIServiceSnapshot` 和 UI Diagnostics。

## 十八、打开结果型 API

### 当前问题

`OpenAsync` 失败返回 null，失败原因不结构化。

### 设计方案

新增类型：

```csharp
public sealed class UIOpenResult<T> where T : UIForm
{
    public bool Success;
    public T Form;
    public string FormId;
    public int SerialId;
    public bool Canceled;
    public bool TimedOut;
    public bool FromCache;
    public string Error;
}
```

新增 API：

```csharp
UniTask<UIOpenResult<UIForm>> TryOpenAsync(string formId, UIOpenOptions options = null);
UniTask<UIOpenResult<T>> TryOpenAsync<T>(UIOpenOptions options = null) where T : UIForm;
UniTask<UIOpenResult<T>> TryOpenAsync<T, TParam>(TParam param, UIOpenOptions options = null) where T : UIForm;
```

现有 `OpenAsync` 继续调用内部打开流程，只返回 `Form`。`TryOpenAsync` 使用同一内部结果，避免双实现。

内部 loading operation 增加失败原因：

```csharp
string Error;
bool Canceled;
bool TimedOut;
bool FromCache;
```

## 十九、UI 模块说明文档和项目级 skill

### 设计方案

功能实现完成后补齐：

- `Assets/FRFrame/Core/UI/UIModule_Guide.md`
- `.claude/skills/frframe-ui/SKILL.md`

Guide 内容包括：

- UI 模块定位。
- `UIManager` API。
- `UIForm` 生命周期。
- UI 配置与 package 规范。
- 打开、关闭、隐藏、显示、预加载。
- 多实例窗口操作。
- 导航栈。
- 遮挡、遮罩、返回键。
- Toast/Dialog/Loading。
- 绑定代码生成流程。
- 安全区、虚拟列表、富文本、交互扩展。
- UI Diagnostics 使用。

Skill 内容包括：

- 触发条件。
- UIForm 模板。
- UIConfig 配置模板。
- 多 package UI 资源规范。
- 绑定生成工作流。
- 导航和服务使用模板。
- 常见注意事项。

## 二十、实施文件清单

预计新增文件：

```text
Assets/FRFrame/Core/UI/Runtime/Core/UIDiagnosticsTypes.cs
Assets/FRFrame/Core/UI/Runtime/Core/UIOpenResult.cs
Assets/FRFrame/Core/UI/Runtime/Core/UINavigationResult.cs
Assets/FRFrame/Editor/Diagnostics/FRFrameUIDiagnosticsWindow.cs
Assets/FRFrame/Core/UI/UIModule_Guide.md
.claude/skills/frframe-ui/SKILL.md
```

预计修改文件：

```text
Assets/FRFrame/Core/UI/Runtime/Core/UIManager.cs
Assets/FRFrame/Core/UI/Runtime/Core/UIForm.cs
Assets/FRFrame/Core/UI/Runtime/Core/UIGroup.cs
Assets/FRFrame/Core/UI/Runtime/Core/UINavigator.cs
Assets/FRFrame/Core/UI/Runtime/Core/UIMaskService.cs
Assets/FRFrame/Core/UI/Runtime/Core/UIOpenOptions.cs
Assets/FRFrame/Core/UI/Runtime/Core/UIEnums.cs
Assets/FRFrame/Core/UI/Runtime/Core/UIDefaultAnimationPlayer.cs
Assets/FRFrame/Core/UI/Runtime/Config/UIFormConfig.cs
Assets/FRFrame/Core/UI/Runtime/Config/UIConfigGroup.cs
Assets/FRFrame/Core/UI/Runtime/Services/UIToastService.cs
Assets/FRFrame/Core/UI/Runtime/Services/UIToastRequest.cs
Assets/FRFrame/Core/UI/Runtime/Services/UIDialogService.cs
Assets/FRFrame/Core/UI/Runtime/Services/UIDialogParams.cs
Assets/FRFrame/Core/UI/Runtime/Services/UILoadingService.cs
Assets/FRFrame/Core/UI/Runtime/Builtin/UIToastForm.cs
Assets/FRFrame/Core/UI/Editor/CodeGen/UICodeGenerator.cs
Assets/FRFrame/Editor/UI/Config/UIConfigEditorWindow.cs
Assets/FRFrame/Editor/UI/Config/UIConfigGroupDraft*.cs
Assets/FRFrame/Editor/UI/Config/UIFormDraft*.cs
```

项目文件如显式 include 新文件，也需要同步更新对应 `.csproj`。

## 二十一、开发自查清单

实现完成后逐项检查：

- `AssetOnly`、`InstanceWarmup`、`AssetAndInstance` 行为不同且诊断可见。
- `UIFormState.Loading` 在实例初始化窗口中可观察。
- Dialog 设置 `CloseOnMaskClick` 后点击 mask 行为符合参数。
- Toast priority、position、merge 生效。
- Loading/Dialog timeout 和 cancellation registration 都可释放。
- UI resource package 使用 `packageName/defaultPackageName`，不再默认依赖 `configGroup`。
- 多实例 GetTop/CloseTop/CloseAll/serialId API 可用。
- Navigator Push 失败后 HidePrevious 前页恢复。
- `UIBlockingMode` 控制遮挡逻辑，旧配置保持兼容。
- UI Diagnostics 能显示 groups、forms、mask、navigation、loading、preload、services。
- Mask 层级或诊断展示清晰。
- Hidden 窗口可配置是否 SetActive(false)。
- Slide 动画在 CanvasScaler 下位移正确。
- 绑定代码内容不变时不写文件。
- UIConfig validator 只允许 UIForm。
- 同名 prefab 自动匹配不再静默选错。
- 服务 snapshot 可被 UI Diagnostics 使用。
- TryOpenAsync 返回结构化失败原因。
- UI Guide 和 frframe-ui skill 已补齐。
- `dotnet build FRFrame.Core.csproj` 和 `dotnet build FRFrame.Editor.csproj` 通过。

## 结论

本设计将 UI 模块优化从“建议清单”转为完整开发方案。后续实现应优先保持 API 兼容，并在现有框架结构内补齐语义、配置、诊断和编辑器工具能力。完成后，UI 模块将与 Resource 的多 package 模型、Event/FSM/Log 的诊断体系、项目级 skill 工作流保持一致，能够支撑后续具体游戏逻辑的大规模 UI 开发。
