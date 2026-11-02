# UI 模块详细说明

## 模块定位

`Assets/FRFrame/Core/UI` 是 FRFrame 的 UI 管理模块，负责 UI 根节点、分层、窗口配置、资源加载、窗口生命周期、遮罩、导航、内置 Toast/Dialog/Loading 服务、绑定代码生成和运行时诊断。

常用入口通过 `FRFM.UI` 访问：

```csharp
UIForm form = await FRFM.UI.OpenAsync("InventoryForm");
InventoryForm inventory = await FRFM.UI.OpenAsync<InventoryForm>();
FRFM.UI.Close("InventoryForm");
FRFM.UI.Toast.Show("获得道具");
UIDialogResult result = await FRFM.UI.Dialog.ShowAsync(new UIDialogParams { Title = "提示", Content = "确定退出？" });
```

## 初始化流程

`UIManager.InitializeAsync()` 会：

1. 等待 `ResourceManager` ready。
2. 创建 `UIRoot`、根 Canvas、UI Camera 和 EventSystem。
3. 创建固定 UI 分层：Background、Normal、Popup、Top、Overlay、System。
4. 在每个 group root 下创建独立 mask 节点。
5. 加载启动 UIConfigGroup。
6. 注册内置代码 UI：`UIToastForm`、`UIDialogForm`、`UILoadingForm`。
7. 创建 Toast、Dialog、Loading、Navigation 服务。

## UI 配置

### UIConfigGroup

`UIConfigGroup` 是一组 UI 配置：

```csharp
public string groupName = "Default";
public bool loadOnInitialize = true;
public string defaultPackageName;
public List<UIFormConfig> forms;
```

`defaultPackageName` 表示该组 UI 资源默认所在 YooAsset package。

### UIFormConfig

关键字段：

```csharp
public string formId;
public string logicType;
public string assetPath;
public string configGroup;
public string packageName;
public UIGroupLayer layer;
public bool allowMultiInstance;
public bool fullScreen;
public UIBlockingMode blockingMode;
public bool disableGameObjectWhenHidden;
public UIFormCloseMode closeMode;
public UICoverVisualMode coverVisualMode;
public bool useMask;
public bool closeOnMaskClick;
public UIMaskMode maskMode;
```

### package 解析规则

UI 资源加载 package 解析优先级：

1. `UIFormConfig.packageName`
2. `UIConfigGroup.defaultPackageName`
3. `UIFormConfig.configGroup` 兼容旧数据
4. `null`，交给 Resource 默认包

推荐新配置显式填写 `packageName` 或 `defaultPackageName`，不要继续依赖 `configGroup` 作为 package。

```csharp
var group = ScriptableObject.CreateInstance<UIConfigGroup>();
group.groupName = "BattleUI";
group.defaultPackageName = "BattlePackage";

group.forms.Add(new UIFormConfig
{
    formId = "BattleHudForm",
    logicType = "Game.UI.BattleHudForm",
    assetPath = "Assets/Game/UI/Battle/BattleHud.prefab",
    configGroup = "BattleUI",
    packageName = "",
    layer = UIGroupLayer.Normal
});
```

## 打开、关闭和结果 API

### 基础打开

```csharp
InventoryForm form = await FRFM.UI.OpenAsync<InventoryForm>(new InventoryOpenParam
{
    BagId = 1001
});
```

强类型参数可配合：

```csharp
public sealed class InventoryForm : UIForm, IUIOpenParamReceiver<InventoryOpenParam>
{
    private InventoryOpenParam _param;

    public void SetOpenParam(InventoryOpenParam param)
    {
        _param = param;
    }
}
```

### TryOpenAsync

结果型 API 适合不希望自己解析 null 原因的业务：

```csharp
UIOpenResult<InventoryForm> result = await FRFM.UI.TryOpenAsync<InventoryForm>();
if (!result.Succeeded)
{
    FRLog.Warning($"打开背包失败: {result.FailureReason}, {result.Message}");
    return;
}

InventoryForm form = result.Form;
```

### UIOpenOptions

`UIOpenOptions` 支持取消、超时、动画、置顶和 mask 覆盖：

```csharp
var options = UIOpenOptions.FromUserData(param, cancellationToken);
options.Timeout = 5f;
options.BringToFront = true;
options.CloseOnMaskClickOverride = true;
options.MaskModeOverride = UIMaskMode.Dim;
options.MaskColorOverride = new Color(0f, 0f, 0f, 0.7f);

UIForm form = await FRFM.UI.OpenAsync("DialogForm", options);
```

这些覆盖只作用于本次打开的实例，不会修改共享配置。

### 关闭

```csharp
FRFM.UI.Close("InventoryForm");
await FRFM.UI.CloseAsync("InventoryForm");
FRFM.UI.Close(form.SerialId);
await FRFM.UI.CloseAsync(form.SerialId);
```

`resident=true` 或 `closeMode=Cache` 的窗口关闭时会隐藏并保留实例；其它窗口会释放实例。

## 多实例窗口

当 `allowMultiInstance=true` 时，同一个 `formId` 可以打开多个实例。

```csharp
UIForm first = await FRFM.UI.OpenAsync("RewardPopup", rewardA);
UIForm second = await FRFM.UI.OpenAsync("RewardPopup", rewardB);

UIForm top = FRFM.UI.GetTop("RewardPopup");
FRFM.UI.CloseTop("RewardPopup");
FRFM.UI.CloseAll("RewardPopup");

if (FRFM.UI.TryGetBySerialId(second.SerialId, out UIForm found))
    found.Close();
```

`GetTop` 按排序层级返回最上方实例，比 `Get` 更适合多实例窗口。

## 预加载

`UIPreloadMode` 有三种语义：

```csharp
await FRFM.UI.PreloadAsync("InventoryForm", UIPreloadMode.AssetOnly);
await FRFM.UI.PreloadAsync("InventoryForm", UIPreloadMode.InstanceWarmup);
await FRFM.UI.PreloadAsync("InventoryForm", UIPreloadMode.AssetAndInstance);
```

- `AssetOnly`：只预加载 prefab 资源，不创建 UI 实例。
- `InstanceWarmup`：创建实例并进入 `Preloaded` 状态。
- `AssetAndInstance`：先预加载资源，再预热实例。

查询和释放：

```csharp
bool ready = FRFM.UI.IsPreloaded("InventoryForm");
FRFM.UI.ReleasePreload("InventoryForm");
FRFM.UI.ReleaseAllPreloads();
```

如果窗口实现 `IUIPreloadHandler`，实例预热时会调用：

```csharp
public sealed class InventoryForm : UIForm, IUIPreloadHandler
{
    public async UniTask OnPreloadAsync(CancellationToken cancellationToken)
    {
        await LoadStaticIconsAsync(cancellationToken);
    }
}
```

## 生命周期状态

`UIFormState`：

- `Loading`：实例已创建，初始化尚未完成。
- `Initialized`：完成绑定和 OnInit。
- `Preloaded`：实例预热完成，尚未打开。
- `Opened`：可见并参与 Update。
- `Hidden`：隐藏但保留实例。
- `Closing`：正在关闭动画。
- `Closed`：已执行 OnClose。
- `Disposed`：已释放。

常用重写点：

```csharp
protected override void OnInit(object userData) { }
protected override void OnOpen(object userData) { }
protected override void OnShow(object userData) { }
protected override void OnHide() { }
protected override void OnClose(object userData) { }
protected override void OnDispose() { }
protected override void OnUpdate(float deltaTime, float unscaledDeltaTime) { }
```

`disableGameObjectWhenHidden=true` 时，`InternalHide` 会在 `OnHide` 后 `SetActive(false)`，再次 `Show` 会恢复激活。

## 遮挡、暂停和隐藏

`UIBlockingMode` 控制一个窗口是否遮挡底层 UI：

```csharp
public enum UIBlockingMode
{
    None,
    FullScreenOnly,
    Always
}
```

- `None`：不遮挡底层。
- `FullScreenOnly`：仅 `fullScreen=true` 时遮挡底层。
- `Always`：无论是否全屏都遮挡底层，适合强模态弹窗。

被遮挡窗口的可见策略由 `coverVisualMode` 决定：

- `KeepVisible`：保持显示。
- `HideByCanvasGroup`：透明并关闭射线。
- `SetInactive`：禁用 GameObject。

如果 `pauseCovered=true`，被遮挡窗口会触发 `OnPause`，恢复时触发 `OnResume`。

## Mask

配置级 mask：

```csharp
new UIFormConfig
{
    useMask = true,
    closeOnMaskClick = true,
    maskMode = UIMaskMode.Dim,
    maskColor = new Color(0f, 0f, 0f, 0.6f)
};
```

实例级覆盖：

```csharp
var options = UIOpenOptions.FromUserData(data);
options.UseMaskOverride = true;
options.CloseOnMaskClickOverride = false;
options.MaskModeOverride = UIMaskMode.CustomColor;
options.MaskColorOverride = new Color(0f, 0f, 0f, 0.85f);
await FRFM.UI.OpenAsync("PopupForm", options);
```

Mask 节点挂在对应 UIGroup root 下，排序在目标窗口下方一层，诊断中可看到每层 mask 的目标窗口和 sorting order。

`UIMaskMode.Blur` 会在窗口显示前捕获并冻结完整背景，使用 1/2 分辨率执行双向 GPU 模糊；`maskColor` 是模糊画面上的叠加颜色。屏幕尺寸变化时会重新捕获。

## 导航

`UINavigator` 适合 Normal 层页面栈：

```csharp
await FRFM.UI.Navigation.PushAsync<HomeForm>();
await FRFM.UI.Navigation.PushAsync<ShopForm>(shopParam, UINavClosePolicy.HidePrevious);
FRFM.UI.Navigation.Back();
FRFM.UI.Navigation.BackToRoot();
```

`UINavClosePolicy`：

- `HidePrevious`：新页面打开前隐藏前一页；若打开失败会恢复前一页。
- `ClosePrevious`：新页面打开成功后关闭前一页，避免失败时丢页面。
- `KeepPrevious`：保留前一页。

结果型导航：

```csharp
UINavigationResult<ShopForm> result = await FRFM.UI.Navigation.TryPushAsync<ShopForm>(shopParam);
if (!result.Succeeded)
{
    FRLog.Warning($"导航失败: {result.FailureReason}");
    return;
}
```

## Toast 服务

```csharp
FRFM.UI.Toast.Show("保存成功");
FRFM.UI.Toast.Show(new UIToastRequest
{
    Message = "获得金币 x100",
    Duration = 1.5f,
    Position = UIToastPosition.Top,
    Priority = 10,
    MergeSameMessage = true
});
```

队列规则：

- `Priority` 高的先显示。
- `MergeSameMessage=true` 时，相同 `Message + Position` 的排队请求会合并。
- `Position` 支持 Top、Center、Bottom。

## Dialog 服务

```csharp
UIDialogResult result = await FRFM.UI.Dialog.ShowAsync(new UIDialogParams
{
    Title = "删除道具",
    Content = "确定删除该道具？",
    ConfirmText = "删除",
    CancelText = "取消",
    ShowCancel = true,
    CloseOnMaskClick = false,
    Timeout = 10f
});

if (result == UIDialogResult.Confirm)
    DeleteItem();
```

队列弹窗：

```csharp
await FRFM.UI.Dialog.EnqueueAsync(new UIDialogParams { Title = "提示1", Content = "第一条" });
await FRFM.UI.Dialog.EnqueueAsync(new UIDialogParams { Title = "提示2", Content = "第二条" });
```

`CloseOnMaskClick` 会通过 `UIOpenOptions.CloseOnMaskClickOverride` 作用到当前 Dialog 实例。

## Loading 服务

```csharp
await FRFM.UI.Loading.ShowAsync(new UILoadingRequest
{
    Message = "正在下载资源...",
    ShowProgress = true,
    Progress = 0f,
    Timeout = 30f
});

FRFM.UI.Loading.SetProgress(0.5f);
FRFM.UI.Loading.SetMessage("解压资源...");
FRFM.UI.Loading.Hide();
```

作用域写法：

```csharp
using (FRFM.UI.Loading.ShowScope("请求服务器..."))
{
    await RequestServerAsync();
}
```

`ForceHide()` 会清空引用计数并关闭 Loading。

## 绑定代码生成

UI 绑定工具读取 `UIBindData.BindInfos` 生成 `*.Bind.cs`，生成前会按 `fieldName + path` 排序，减少层级调整或 Inspector 序列化顺序造成的无意义 diff。

生成文件包含：

- 字段声明。
- `BindInit` 绑定。
- `BindEvent` / `UnbindEvent`。
- 事件 wrapper 和 partial 回调。
- 绑定空值校验。

业务逻辑写在同名 partial 文件中，不要修改 `*.Bind.cs`。

## UI Config Editor

菜单：`FRFrame/UI/UI Config Editor`

能力：

- 创建和编辑 `UIConfigGroup`。
- 编辑 formId、logicType、assetPath、configGroup、packageName、layer、mask、animation、closeMode 等配置。
- 校验重复 formId、logicType 是否存在、prefab 是否存在、mask 语义、blocking 语义等问题。
- 搜索未配置 UI 类型并批量加入配置。

## 运行时诊断

代码获取快照：

```csharp
UIManagerSnapshot snapshot = FRFM.UI.CreateSnapshot();
foreach (UIFormSnapshot form in snapshot.Forms)
{
    FRLog.Info($"{form.FormId}#{form.SerialId} {form.State} {form.Layer} {form.PackageName}");
}
```

Editor 窗口：`FRFrame/Diagnostics/UI Diagnostics`

可查看：

- Opened/Hidden/Preloaded/Loading 数量。
- Toast/Dialog/Loading 服务状态。
- Group form 数量和排序基准。
- 每个 UIForm 的 serialId、state、package、mask、sortingOrder。
- 每层 Mask 的 active、目标窗口和排序。
- 预加载记录和 loading operation。

## Runtime Extensions

运行时扩展位于 `Runtime/Extensions`。在 `ShopExampleForm` 打开“扩展示例”，进入 **UI Runtime 业务实验室** 可切换七个可操作场景；它们是业务验收入口，不只是组件陈列：

| 场景 | 可操作内容 | 主要验收点 |
| --- | --- | --- |
| TacticalMap | 切换 Rect/Triangles 命中、Stretch/Fit/Fill、圆/扇/环/矩形几何并点击地图 | `UIMeshImage` 的运行时 Geometry、真实三角形命中与 bounds-to-rect 适配 |
| Inventory | 获得/删除、排序、移动、过滤、`ScrollToKey` | 固定列表多模板池、稳定 Key、增量通知、首可见 Key 锚点和选择状态 |
| GuildChat | 单条/批量追加、头插历史、展开可见项、回到底部、自动跟随 | 动态列表多模板、批量尺寸更新、Fenwick 索引、聊天头插与末端跟随 |
| Banner | Previous/Next/MoveTo、暂停、改间隔、切换 1/2/5 条、拖拽和点击 | `UIInfiniteCarousel` 有限槽位轮换、真正视觉无缝、自动播放和拖拽 settle |
| SnapPage | 前后翻页、快速重入、对齐、Clamp/Wrap、增删页面、改变页宽 | 普通分页按真实页面布局定位，以及 selected/settled 时序 |
| FontRefresh | 请求字体 A、同帧 A+B、RefreshAll、动态创建 Text 后重建目标 | UGUI 字体通知合并、目标缓存、统计和显式 `RebuildTargets()`；TMP 为对照组 |
| CommonInteraction | 动态 Toggle、长按购买、跑马灯、RawImage 模式、双指缩放 | 常见交互的取消、重入、布局与生命周期组合 |

FRFrame Core 不提供统一 Outline 扩展。TMP 文本优先使用 TMP SDF Material Outline，静态图标优先使用美术预制描边 Sprite，大图或特殊效果由业务模块提供目标平台验证过的 Shader；Core 不统一 Shader 属性、描边宽度或材质实例生命周期。

### Mesh Image

`UIMeshImage` 是 `Texture + Mesh/Geometry` 的 `MaskableGraphic`，不是 `Image` 的 Sprite/Type/Fill 替代品。

```csharp
meshImage.SetTexture(texture);
meshImage.SetMesh(mesh);
// 或运行时数据
meshImage.SetGeometry(vertices, uvs, triangles);
meshImage.FitMode = UIMeshFitMode.Fit;
meshImage.RaycastMode = UIMeshRaycastMode.Triangles;
```

- `UIMeshFitMode.None` 保留调用方局部坐标；`Stretch` 将 bounds 分轴铺满 rect；`Fit` 等比完整放入并允许留边；`Fill` 等比铺满并允许超出，通常配合 Mask。
- `UIMeshRaycastMode.None` 不命中；`Rect` 使用标准 Graphic 矩形命中；`Triangles` 先经过 `Graphic` 的 `raycastTarget`、CanvasGroup/Mask 等过滤，再按与渲染相同的映射测试有效三角形。反向 winding 可命中，退化或越界三角形会忽略。
- Mesh、Geometry、FitMode 或 Rect 尺寸变化会使映射缓存失效；Texture 变化只标记材质。同一 Mesh 实例被原地修改后调用 `RefreshMesh()`。`ClearMesh()` 清空自定义几何，此时渲染回退到默认 Quad。
- 它不提供 Sprite、`SetNativeSize`、pixelsPerUnit 或 Image fill；需要这些语义时继续使用 `Image`。

### 普通分页与真正无缝轮播

`UISnapScrollPage` 是标准 `ScrollRect` 的轻量分页器。`RefreshPages(preserveSelection)` 会在布局重建后读取 Content 的直接激活子节点，按每页实际 bounds 计算 Start/Center/End 目标，因此支持 LayoutGroup spacing、padding 和变宽页面。运行时增删页面或改变尺寸后由业务显式调用 `RefreshPages()`，组件不会每帧扫描层级。

```csharp
snap.indexMode = UIPageIndexMode.Clamp;
snap.alignment = UIPageAlignment.Center;
snap.RefreshPages();
snap.MoveToPage(index, animated: true);
```

- `SelectedPage` 在目标确定并触发 `onPageChanged` 时更新；`SettledPage` 仅在立即定位或 Tween 正常完成并触发 `onPageSettled` 时更新。
- `StopMovement()`、新移动、开始拖拽、Disable 或 Destroy 会取消旧 Tween，旧目标不会迟到触发 settled。
- `UIPageIndexMode.Clamp` 截断索引；`Wrap` 只把越界索引取模到另一端。Wrap 不复制页面、不轮换槽位，**不是视觉无缝循环**。旧 `enableLoop` 仅为 obsolete 兼容入口，新代码使用 `indexMode`。

`UIInfiniteCarousel` 用于真正无缝的横向 Banner。它创建奇数个有限槽位，拖动或移动跨过一个 viewport 宽度时轮换首尾槽位并重新绑定数据，Content 不会走到数据边界。

```csharp
carousel.Configure(viewport, content, itemPrefab, newSlotCount: 5);
carousel.SetData(data, context => BindBanner(context));
carousel.SetAutoPlay(true, 3f);
carousel.Next();
carousel.MoveToIndex(index);
```

- `UIInfiniteCarouselItemContext` 提供 `Slot`、`RectTransform`、`Data`、`DataIndex`、`SlotIndex`、`IsCenter`；绑定回调应完整覆盖复用槽位的文本、颜色、缩放等视觉状态。
- `onIndexChanged` 在跨槽位并更新中心数据时触发；`onSettled` 在立即移动或动画正常到位后触发。拖拽期间可连续轮换槽位，松手按 `dragThreshold` 前进/后退或回弹。
- `SetAutoPlay` 使用 unscaled time；拖拽和 settle 期间不推进，`PauseAutoPlay`/`ResumeAutoPlay` 显式控制。Disable 会取消 Tween 与拖拽，但不会改写自动播放配置；页面隐藏时应暂停，显示时恢复。
- 当前仅支持横向、每槽一个 viewport 宽度；不提供纵向、变宽槽位或外部 ScrollRect 惯性。`SetData` 会复制数据引用列表，数据变化后应再次调用它。

### 固定尺寸虚拟列表

`UIVirtualList` 支持纵向、横向及两种固定网格，并支持稳定 Key、多模板独立池、增量通知和锚点恢复。动态业务应实现：

```csharp
public interface IUIVirtualListDataSource
{
    int Count { get; }
    long GetKey(int index);
    object GetData(int index);
    string GetTemplateId(int index);
}
```

用 `ConfigureTemplates(IEnumerable<UIVirtualListTemplate>)` 声明 `templateId/prefab/itemLogicType/maxCacheCount`；每个模板独立缓存，`itemLogicType` 必须派生 `UIItem`。Key 必须在当前数据源中唯一且跨排序、插入和删除保持稳定；重复 Key 会记录错误并只保留首次索引。`SetDataList<T>` 生成 index Key，只适合全量、静态身份数据。

外部数据源采用“**先改数据，再通知列表**”的约定：

```csharp
source.Items.Insert(index, data);
list.InsertRange(index, 1);
source.Items.RemoveAt(index);
list.RemoveRange(index, 1);
// ReplaceRange / Move 同理：先完成 source 修改
```

这些通知重建 Key 映射、内容尺寸和可见项，而不是替业务修改数据。`AnchorMode` 可选 `None`、`PreserveScrollOffset`、`PreserveFirstVisibleKey`、`FollowEndIfAtEnd`。背包排序/头部插入推荐稳定 Key + `PreserveFirstVisibleKey`；聊天末端追加可短暂使用 `FollowEndIfAtEnd`。`ScrollToKey`、`TryGetVisibleItem` 和 `UIVirtualListAlignment` 用于按业务身份定位。

### 动态尺寸虚拟列表

`UIDynamicVirtualList` 是纵向单列动态尺寸列表，共享稳定 Key、多模板池和增量通知语义。通过 `SetSizeProvider((index, data) => size)` 提供估算/已知高度；可选 `IUIDynamicItemMeasurer` 只测量可见 Item。

单项更新使用 `NotifyItemSizeChanged`；多项变化必须批处理，避免每项都刷新布局：

```csharp
list.BeginSizeUpdate();
for (int i = start; i <= end; i++)
    list.NotifyItemSizeChanged(i, newSizes[i]);
list.EndSizeUpdate();
```

内部 Fenwick Tree 使单项尺寸更新、前缀和及按 offset 查索引为对数复杂度；插入/删除通知会批量重建尺寸索引。`UIDynamicSizeAnchorMode.PreserveFirstVisibleKey` 适合展开消息或字体变化，`PreserveEnd` 仅在更新前位于末端时保持末端。聊天加载历史应先头插外部数据，再用 `PreserveFirstVisibleKey + InsertRange` 保持阅读位置；追加前用 `IsAtEnd()` 判断是否跟随，并可调用 `ScrollToBottom()`。

当前动态列表只做纵向单列，不是瀑布流。字体、本地化或 viewport 宽度令大量高度失效时调用 `InvalidateAllSizes()`；不要每帧重建尺寸缓存。

### UIItem 生命周期与资源边界

两种列表的顺序一致：

1. 创建：实例化模板，附加/取得对应 `UIItem`，执行 `InternalInitialize`，再触发 `onItemCreated`。
2. 可见：激活 GameObject，写入 `Index/Key/TemplateId/Data/TypedData`，依次调用 `OnSetData`、`OnRefreshItem`、`OnItemVisible`，最后触发列表 `onItemVisible`。
3. 已可见刷新：`RefreshItem` 更新 Index/Data 并调用 set/refresh，不重复调用 visible。
4. 回收：先触发列表 `onItemRecycled`，再调用 Item 的 `OnItemRecycled`；此时旧 Index/Key/Data 仍可读取。随后清除选择、数据、Key、TemplateId，禁用并进入对应模板池，超出缓存上限则 dispose 并销毁。
5. `Clear()` 会停止 Tween、清数据、回收可见项并销毁全部缓存；Disable 会解绑 ScrollRect、停止 Tween 并回收可见项，Enable 后重绑刷新。

异步图片和资源生命周期由 `UIItem` 管理，不由列表代管。Item 应在 visible/set data 时启动请求，在 recycled/dispose 时取消请求并释放句柄；迟到结果必须核对当前 `Key` 或取消令牌，防止复用串图。

### 字体刷新与透明热区

`UIFontTextureRefresh` 只服务动态字体的 UGUI `Text`，不处理 TMP。Font 事件先进入 pending `HashSet`，在 `LateUpdate` 合并同帧重复/多字体通知，再扫描缓存目标并只刷新匹配字体。`RequestRefresh(Font)` 走合并路径；`Refresh(Font)` 与 `RefreshAll()` 是同步强制入口；`LastScannedCount`、`LastRefreshedCount` 用于观察结果。

目标 Text 在 Enable 时缓存。运行时新增/删除 Text、改变 `targetRoot` 或 `includeInactive` 后必须显式调用 `RebuildTargets()`；组件刻意不监听任意动态层级并每帧重扫。TMP 使用自身 FontAsset/atlas 更新机制。

`UINoDrawingGraphic` 的 Mesh 始终为空，用作透明热区或只参与布局/事件的 Graphic。它不在 Awake 强制射线设置，完整尊重 `Graphic.raycastTarget`：设为 `false` 即不参与 GraphicRaycaster。它不宣称绝对零重建或比所有透明 Image 更高性能。

### 其它交互与显示扩展

- `UIScrollText` 支持 `HorizontalLoop`、`VerticalLoop`、`PingPong`、`Once`。`Play()`、`Stop(resetPosition)`、`SetText(text, play)` 均会正确取消旧播放；未溢出时不播放。
- `UILongPressTrigger` 区分 release/click/cancel；移出、禁用、`Cancel()` 或超过移动容差走取消路径，不再触发 release/click。
- `UIPinchZoom` 仅在两触点都命中 viewport 时开始；结束或 `CancelPinch()` 恢复父 ScrollRect 原状态，`ResetZoom(animated)` 才复位。不支持旋转或三指。
- `UIRawImageFitter` 推荐 `Container + RawImage`：Contain 完整显示并留边，Cover 铺满并裁切，Fill 拉伸。
- `UIToggleGroupBinder` 在运行时增删 Toggle 后调用 `RefreshToggles()`。
- `UIRichText` 的解析缓存由 `cacheParsedText/maxCachedEntries` 控制；配置原地改变后 `InvalidateCache(refresh)`，替换配置用 `SetConfig(config, refresh)`。标签必须严格嵌套，不支持交叉闭合。

### 初始化与刷新原则

- 在组件启用前完成必需引用和配置。列表通常按 `Configure`、`Initialize`、`ConfigureTemplates`、size provider、`SetDataSource` 的顺序设置；布局尚未稳定时等待 Canvas/layout rebuild 后再刷新。
- 不要每帧调用 `ForceRefresh`、`RefreshPages`、`RefreshToggles`、`RebuildTargets`、`InvalidateCache` 或重建尺寸缓存；批量改变后统一通知。
- 按可见量设置 `extraCount`、`Prewarm` 和每模板 cache 上限。当前实现没有提供设备性能结论，具体预算应由业务在目标场景和目标设备验证。

## 推荐实践

### 普通页面

```csharp
await FRFM.UI.Navigation.PushAsync<LobbyForm>(null, UINavClosePolicy.HidePrevious);
```

配置建议：

- `layer = Normal`
- `fullScreen = true`
- `blockingMode = FullScreenOnly`
- `closeMode = Cache`
- `coverVisualMode = HideByCanvasGroup`

### 弹窗

```csharp
await FRFM.UI.OpenAsync<RewardPopup>(rewardData);
```

配置建议：

- `layer = Popup`
- `fullScreen = false`
- `blockingMode = Always`
- `useMask = true`
- `maskMode = Dim`
- `closeOnMaskClick = true`
- `closeMode = Destroy`

### 常驻 HUD

```csharp
await FRFM.UI.OpenAsync<BattleHudForm>();
```

配置建议：

- `layer = Normal`
- `resident = true`
- `closeMode = Cache`
- `blockingMode = None`
- `coverVisualMode = KeepVisible`

### 多 package UI

```csharp
await FRFM.UI.LoadConfigGroupAssetAsync(
    "Assets/Game/Battle/Config/UIConfigGroup_Battle.asset",
    "BattlePackage");

await FRFM.UI.OpenAsync("BattleHudForm");
```

`UIConfigGroup_Battle.defaultPackageName = "BattlePackage"` 后，组内 form 可不再逐个填写 `packageName`。
