# FRFrame UI Runtime Extensions：RawGame 吸收后评估

## 1. 文档目的与评估基线

本文复核 `Assets/Scripts/AzurLane/RawGame` 中 UI 设计目标被吸收到 `Assets/FRFrame/Core/UI/Runtime/Extensions` 后的当前实现结果。RawGame 只作为业务目标来源，不作为 API、字段、Prefab 或 AnimationClip 的兼容标准；当前扩展按 Unity 标准组件、FRFrame 生命周期和实际业务复用价值重新设计。

评估基线：

- 原始来源：`Assets/Scripts/AzurLane/RawGame`
- 当前实现：`Assets/FRFrame/Core/UI/Runtime/Extensions`
- 业务验收：`Assets/FRFrame/Core/UI/Example/RuntimeExtensions` 的七个实验室场景
- 使用说明：`Assets/FRFrame/Core/UI/UIModule_Guide.md`

RawGame 部分文件来自反编译后的人工恢复，尤其 `LOutLine.cs` 缺少原始 UV、切线和 Shader 配套逻辑。因此这里只评估工程中现存源码，不把它等同于原游戏完整生产实现，也不虚构设备测试或性能数据。

## 2. 总体结论

当前 Extensions 已形成可供开发使用的 UI 扩展集合。此前 P0/P1/P2 中的核心工作已经落地：

- `UIMeshImage` 已实现真实 Triangles Raycast、Rect/None 模式、Stretch/Fit/Fill/None 映射和几何合法性过滤；
- `UISnapScrollPage` 已按实际页面 bounds 支持变宽页面、Start/Center/End、Clamp/Wrap，并明确 Wrap 不是视觉循环；
- 独立 `UIInfiniteCarousel` 已实现有限槽位首尾轮换、真正视觉无缝、自动播放、拖拽和 settle；
- `UIFontTextureRefresh` 已实现同帧 Font 合并、目标缓存、统计和显式 `RebuildTargets()`；
- 固定与动态虚拟列表已实现稳定 Key、多模板独立池、增量通知和锚点；动态列表另有批量尺寸 API、Fenwick Tree 与聊天头插/末端保持；
- `UIItem` 已把 Key、TemplateId、TypedData 和回收清理纳入统一生命周期；
- `UINoDrawingGraphic` 保持零绘制，并尊重标准 `Graphic.raycastTarget`。

这些结果的主要价值是可在业务实验室直接操作和验收，而不是自动化测试数量。当前没有基于目标设备的性能结论；容量、缓存和刷新预算仍应在具体项目场景中验证。

## 3. 吸收结果总表

| RawGame 来源/目标 | 当前实现 | 实现结果 | 推荐等级与边界 |
| --- | --- | --- | --- |
| `EventTriggerListener`、`UIEventListener`、`UIEventTrigger` | `UIPointerEventListener` | 多播、`IDisposable`、FRFrame 事件作用域；不保留 Collider Mouse、Inspector 字符串事件全集 | 常规推荐；不是旧 API 全集兼容 |
| `UILongPressTrigger` | `UILongPressTrigger` | 移动/移出取消、重复、进度、click/long press 时序与禁用清理 | 常规推荐 |
| `UIToggleEvent` | `UIToggleBinder`、`UIToggleGroupBinder` | 单项视觉与组选择拆分，支持动态 `RefreshToggles` | 常规推荐 |
| `LScrollBar` | `UIScrollbarDragEvents` | 组合标准 Scrollbar，拖拽事件携带 value | 常规推荐 |
| `LScrollRect` 系列 | `UIVirtualList`、`UIDynamicVirtualList`、`UIItem` | 固定/网格/动态尺寸、稳定 Key、多模板、增量锚点、Fenwick 与完整回收生命周期 | 高频业务推荐；不是旧 ScrollRect 替代类 |
| `LScrollPage` | `UISnapScrollPage` | 真实页面位置、变宽页面、对齐、Clamp/Wrap、selected/settled | 标准分页推荐；Wrap 不是视觉无缝 |
| 无缝商品轮播目标 | `UIInfiniteCarousel` | 有限槽位轮换、自动播放、拖拽、点击与真正视觉无缝 | Banner 推荐；当前仅横向等宽槽位 |
| `RichText` | `UIRichText` 系列 | TMP Parser/Config/Sprite Resolver、链接与缓存 | 新业务推荐；不兼容旧 UGUI Text/标签 API |
| `ScrollText` | `UIScrollText` | UGUI/TMP、四种模式、取消重入、统一 Tween | 常规推荐；Viewport/Mask 由调用方配置 |
| `UIFontDirty` | `UIFontTextureRefresh` | UGUI Text 目标缓存、同帧字体合并、同步入口与统计 | 常规推荐；动态层级需显式重建，TMP 不适用 |
| `NoDrawingGraphic` | `UINoDrawingGraphic` | Mesh 清空、标准 Graphic raycast 语义 | 透明热区推荐；不宣称绝对最低重建成本 |
| `RawImageCoverFitter` | `UIRawImageFitter` | Contain/Cover/Fill/NativeSize、UV 裁切、容器模型 | 常规推荐 |
| `NotchAdapt` | `UISafeAreaAdapter` | 四边独立、Padding、Editor 预览、变化事件 | 推荐；会接管目标锚点和 offset |
| `DftAniEvent` | `UIAnimationEventBridge` | 多参数、命名监听、once、UnityEvent | 新动画推荐；旧 Clip 函数名需迁移 |
| `MeshImage` | `UIMeshImage` | Texture + Mesh/Geometry、真实三角形命中和 bounds-to-rect | 自定义 UI Mesh 推荐；不是 Sprite Image |
| `PinchZoom` | `UIPinchZoom` | EnhancedTouch、Viewport、平移、回弹、父 ScrollRect 协调 | 图片/地图推荐；不支持旋转和三指 |
| `PaintingScaler` | `UIAspectFitter` | 通用 Contain/Cover/FitWidth/FitHeight/Fill | 比例适配推荐；不含多相框插值 |
| `LOutLine` | 无 | 删除统一扩展，改用 TMP SDF、描边 Sprite 或业务 Shader | 符合 Core 边界 |

`Live2dChar`、`ParticlePlexus`、重复事件系统、调试示例、空 `LSlider` 等仍不进入 UI Core。它们属于插件、特效、重复基础设施或无有效实现，不因来源存在而吸收。

## 4. 已完成的重点能力

### 4.1 UIMeshImage：真实 Mesh 渲染、Fit 与命中

当前类型定位为“在 UGUI 中渲染调用方提供的 Texture + Mesh/Geometry”，没有恢复 `Image` 的 Sprite、Type、Fill 或 pixelsPerUnit 体系。

实现结果：

- `UIMeshRaycastMode.None/Rect/Triangles` 已取代误导性 bool；
- `Triangles` 先经过标准 Graphic raycast 过滤，再将屏幕点转换到局部坐标，对渲染所用的映射后顶点逐三角形命中；边界和双 winding 可命中；
- `UIMeshFitMode.None/Stretch/Fit/Fill` 已实现，bounds 零轴有安全处理；
- UV 不足使用零值，越界索引、非完整三元组与退化三角形被忽略；
- Mesh、运行时 Geometry、Rect 和 Fit 变化使映射缓存失效；同一 Mesh 实例原地修改后可调用 `RefreshMesh()`，Texture 变化只触发材质刷新；
- TacticalMap 场景可切换圆、扇、环、矩形，直接对比 Rect/Triangles 与 Stretch/Fit/Fill。

适用边界：它适合战术地图范围、非矩形点击区和自定义程序化 Mesh；需要 Sprite、NativeSize 或 Image Fill 时继续使用 `Image`。空自定义几何会回退绘制默认 Quad，调用方若要彻底隐藏还应禁用组件或对象。

### 4.2 UISnapScrollPage：准确普通分页

当前实现读取 Content 的直接激活子 RectTransform，在强制布局后按每页相对 Viewport 的实际 bounds 计算目标，不再以首项尺寸乘 index。已支持：

- 横向/纵向、LayoutGroup padding/spacing、变宽或变高页面；
- Start/Center/End 对齐与 Content 可滚动范围 Clamp；
- `SelectedPage`/`SettledPage` 和 `onPageChanged`/`onPageSettled` 的目标、到位时序；
- Nearest、NextOnThreshold、Velocity 三种拖拽判定；
- `UIPageIndexMode.Clamp/Wrap`；
- 快速重入、Stop、拖拽开始、Disable、Destroy 对旧 Tween 的取消。

SnapPage 实验室可改变当前页宽、动态增删页面、切换对齐与 Clamp/Wrap。`Wrap` 只是索引解析和边缘目标选择，不复制首尾页，不应称为视觉循环。旧 `enableLoop` 仅保留 obsolete 兼容入口。

### 4.3 UIInfiniteCarousel：真正无缝 Banner

真正无缝轮播已独立纳入并实现，不再作为未来事项。组件使用奇数个有限槽位围绕中心排布；偏移跨过一个 viewport 宽度时，将首槽移到尾部或反向轮换，更新中心数据索引并重新绑定槽位，所以数据首尾之间没有 Content 边界跳转。

现有能力：

- `Configure`、`SetData<T>`、`Next`、`Previous`、`MoveToIndex`；
- 拖拽中连续槽位轮换，松手按阈值前进/后退或回弹；
- `onIndexChanged`、`onSettled`、`onItemClicked` 及中心项上下文；
- unscaled time 自动播放，`SetAutoPlay`、`PauseAutoPlay`、`ResumeAutoPlay`；拖拽和 settle 时自动暂停计时；
- 1/2/N 数据量与动态重新 SetData。

Banner 实验室可操作前后移动、指定索引、暂停/继续、改变间隔、切换数据量、拖拽与中心/旁卡点击。

当前限制：只支持横向、每槽一个 Viewport 宽度，不支持纵向或变宽槽位；它管理自身拖拽而不是依赖 ScrollRect 惯性。`SetData` 复制列表中的对象引用，外部集合改变后应再次调用 `SetData`。绑定回调必须重置复用槽位的全部业务视觉。

### 4.4 UIFontTextureRefresh：缓存目标与合并通知

组件仍只处理动态字体的 UGUI `Text`，TMP 使用自身 FontAsset/atlas 机制。当前已实现：

- Font.textureRebuilt 和 `RequestRefresh(Font)` 进入 pending `HashSet`；
- `LateUpdate` 一次处理同帧的多字体/重复通知；
- 缓存 `targetRoot` 下 Text，按匹配字体刷新；
- `Refresh(Font)`、`RefreshAll()` 同步入口；
- `PendingFontCount`、`LastScannedCount`、`LastRefreshedCount` 诊断数据；
- Enable/Disable 订阅对称。

FontRefresh 实验室可排队字体 A、同帧 A+B、同步 RefreshAll，并在动态创建 Text 后调用 Rebuild。当前限制也必须明确：目标层级是显式缓存；运行时新增/删除 Text、改变 Root 或 includeInactive 后必须调用 `RebuildTargets()`。组件刻意不为任意动态层级每帧重扫。

### 4.5 UINoDrawingGraphic：标准透明热区

`OnPopulateMesh` 只清空 VertexHelper，因此不绘制几何。组件没有在 Awake 或 Update 强制 `raycastTarget`，完全尊重 `Graphic.raycastTarget`；设为 false 后不参与 GraphicRaycaster。它仍遵循 CanvasGroup、Mask 和父层级的标准 UGUI 过滤。

该组件的价值是语义明确的透明热区，不是未经测量的“最高性能 Graphic”。当前不覆盖 Dirty API，也不宣称所有情况下比透明 Image 更省 CPU。

### 4.6 UIVirtualList：稳定 Key、多模板与增量锚点

固定列表支持 Vertical、Horizontal、GridVertical、GridHorizontal。当前新增能力已经落地：

- `IUIVirtualListDataSource` 提供 Count、稳定 long Key、Data、TemplateId；
- `UIVirtualListTemplate` 定义模板 Prefab、UIItem logic type 与独立缓存上限；
- 多模板池隔离，类型必须派生 `UIItem`；
- Key 到 index 映射、重复 Key 报错、`ScrollToKey`、`TryGetVisibleItem`；
- `InsertRange/RemoveRange/ReplaceRange/Move` 增量通知；
- `None/PreserveScrollOffset/PreserveFirstVisibleKey/FollowEndIfAtEnd` 锚点模式；
- Start/Center/End/Nearest 定位。

Inventory 实验室用 header/material/equipment 三模板演示随机获得、删除、排序、移动、过滤和 ScrollToKey，并展示 created/visible/recycled 计数与选择 Key。

重要数据边界：增量 API 是“数据已变化”的通知，不修改外部数据源。业务必须先 Insert/Remove/Sort 外部集合，再通知列表；否则 Count、Key 和 Data 仍是旧状态。`SetDataList<T>` 使用 index Key，只适合不依赖稳定身份的全量静态数据。

### 4.7 UIDynamicVirtualList：Fenwick、批量尺寸与聊天场景

动态列表是纵向单列实现，复用稳定 Key、多模板池和外部数据源通知约定，并使用 Fenwick Tree 保存 item size + spacing：

- 单项尺寸更新、前缀和与按 offset 查 index 使用对数路径；
- 插入/删除等结构变化批量重建尺寸索引；
- `BeginSizeUpdate/NotifyItemSizeChanged/EndSizeUpdate` 将多项尺寸变化合并为一次 Content/可见范围刷新；
- 支持 `NotifyItemSizesChanged`、`InvalidateSize`、`InvalidateAllSizes`、`RebuildSizeCache`；
- `UIDynamicSizeAnchorMode.PreserveFirstVisibleKey/PreserveEnd` 修正尺寸变化后的阅读位置；
- 可选 `IUIDynamicItemMeasurer` 测量可见项，不为测量实例化全列表；
- 提供 `ScrollToBottom`、`IsAtEnd`、`GetFirstVisibleKey`。

GuildChat 实验室用 self/other/system 三模板验收单条/批量追加、头插历史、展开可见项、回到底部和自动跟随。头插先更新外部数据，再用 `PreserveFirstVisibleKey + InsertRange` 保持阅读位置；追加先记录 `IsAtEnd`，仅在原本位于末端且业务开启 follow 时滚到底部。

当前限制：只支持纵向单列，不是瀑布流；高度由 size provider、通知或可见项 measurer 提供，不会自动可靠测量所有不可见项。字体、本地化或 viewport 宽度变化时业务需显式使尺寸失效。

### 4.8 UIItem 生命周期与异步资源责任

列表 Item 当前具有 `Index`、`Key`、`TemplateId`、`Data`、`TypedData`、`IsSelected`。可见时先写入身份和数据，再调用 set/refresh/visible；列表的 `onItemVisible` 最后触发。回收时先触发列表事件，再调用 Item recycled，因此业务此时仍可读取旧 Key/Data；随后选择、数据、TypedData、Key 和 TemplateId 被清空，Item 进入对应模板池或 dispose 销毁。

异步图片和资源加载明确由 `UIItem` 自己管理，不由列表吞并 Resource 模块职责。Item 应在 recycled/dispose 时取消请求、释放句柄，并让迟到结果校验当前 Key 或取消令牌，避免复用串图。

## 5. 其它已吸收能力

- `UIPointerEventListener`：覆盖常见 Click/Down/Up/Enter/Exit/Drag/Drop/Scroll/Select/Deselect/Move，支持多订阅与作用域释放；不补旧 Collider Mouse 和 Inspector 字符串事件。
- `UILongPressTrigger`：长按、click、release、cancel 和 progress 时序清晰，移动/移出/Disable 可取消。
- `UIToggleBinder/UIToggleGroupBinder`：适合商店页签和动态筛选；运行时增删后显式 `RefreshToggles()`。
- `UIScrollText`：UGUI/TMP 与四模式，Play/Stop/SetText 可安全取消重入；Mask 由业务布局提供。
- `UIRawImageFitter`：Contain/Cover/Fill/NativeSize，推荐 Container + RawImage。
- `UIPinchZoom`：EnhancedTouch、Viewport、平移、边界回弹和父 ScrollRect 协调；不支持旋转和三指。
- `UISafeAreaAdapter`：安全区本职能力更完整，但会接管锚点和 offset，不应随意叠加到已有复杂锚点布局。
- `UIAnimationEventBridge`：新动画事件更通用；旧 Clip 中的函数名不会自动映射。
- `UIRichText`：对 TMP 新业务有明确优势，但不是旧 `RichText : Text` 或 HTML 风格 API 的兼容升级。
- `UIAspectFitter`：处理通用比例适配，不承担 Painting 多相框快照和插值。

## 6. P0/P1/P2 实现结果

### P0：错误语义已收口

- **UIMeshImage：已完成。** 枚举 Raycast、真实三角形命中、FitMode、非法几何过滤与缓存均已实现；TacticalMap 可进行业务交互验收。
- **UISnapScrollPage：已完成。** Clamp/Wrap、Selected/Settled、实际页面位置和变宽页面均已实现；文档明确 Wrap 非视觉循环。

### P1：工程风险已处理到可用边界

- **UIFontTextureRefresh：已完成代码目标。** 多 Font 合并、目标缓存、统计、显式重建均已实现；没有声称真机性能或覆盖 TMP。
- **UIPinchZoom：当前实现具备完整启停与 ScrollRect 恢复语义。** 使用边界仍是不含旋转/三指；没有虚构多设备验证结果。
- **UIRichText：作为 TMP 新系统使用。** 缓存、warning、链接处理和配置失效路径已存在；不恢复旧 API。
- **UISafeAreaAdapter：作为标准安全区适配使用。** Editor 预览和变化事件可用，但本文不声称已完成特定设备矩阵验证。

### P2：高频业务能力已实现

- **UIVirtualList：已完成** 稳定 Key、多模板、增量通知、对齐和锚点保持。
- **UIDynamicVirtualList：已完成** 稳定 Key/多模板、批量尺寸、可见项测量、Fenwick 索引和聊天锚点。
- **UIInfiniteCarousel：已独立实现并纳入。** 真正无缝轮播、自动播放和拖拽已由 Banner 场景验收，不再列为未来能力。
- **Mesh Sprite/NativeSize：保持不纳入。** 这是明确组件边界，不是未完成缺陷。

## 7. 业务实验室验收入口

`ShopExampleForm` 的“扩展示例”页提供 **UI Runtime 业务实验室**，七个场景分别验证：

1. **TacticalMap**：程序化 Mesh、Triangles/Rect、Fit 模式和点击命中。
2. **Inventory**：固定列表三模板、稳定 Key、Insert/Remove/Replace/Move、选择与 ScrollToKey。
3. **GuildChat**：动态三模板、批量高度、头插历史、末端跟随和 Fenwick 可见范围。
4. **Banner**：真正无缝槽位轮换、拖拽、自动播放、数据量切换和点击上下文。
5. **SnapPage**：变宽页面、动态增删、对齐、Clamp/Wrap、快速重入和 selected/settled。
6. **FontRefresh**：同帧字体请求合并、缓存扫描统计、动态 Text 后 RebuildTargets，以及 TMP 对照边界。
7. **CommonInteraction**：Toggle、长按、跑马灯、RawImage 适配和 PinchZoom 的组合生命周期。

这些场景用于开发和策划可见的业务验收。自动化测试仍可承担回归保护，但不是本文评价扩展价值的主要依据。

## 8. 当前推荐等级与限制

### 常规推荐

- `UIPointerEventListener`
- `UILongPressTrigger`
- `UIToggleBinder` / `UIToggleGroupBinder`
- `UIScrollbarDragEvents`
- `UIVirtualList`
- `UIDynamicVirtualList`
- `UISnapScrollPage`
- `UIInfiniteCarousel`
- `UIScrollText`
- `UIFontTextureRefresh`
- `UINoDrawingGraphic`
- `UIRawImageFitter`
- `UISafeAreaAdapter`
- `UIAnimationEventBridge`
- `UIPinchZoom`
- `UIAspectFitter`
- `UIRichText`
- `UIMeshImage`

“常规推荐”表示 API 与生命周期已具备明确业务用途，不表示无边界：

- InfiniteCarousel 当前只横向且槽位等宽；
- Snap 的 Wrap 不是视觉循环；
- 外部虚拟列表数据源必须先修改，再发 Insert/Remove/Replace/Move 通知；
- 动态列表只支持纵向单列，高度和失效由业务提供；
- 动态新增/删除 UGUI Text 后必须显式 `UIFontTextureRefresh.RebuildTargets()`；TMP 不属于该组件；
- UIMeshImage 不提供 Sprite/Image.Type/NativeSize；
- 异步图片和资源加载由 UIItem 在 visible/recycled/dispose 生命周期中管理；
- SafeArea 会接管目标锚点/offset；PinchZoom 不支持旋转和三指；
- 当前文档没有设备性能数据或完整真机矩阵结论。

### 不由 Core 提供

- 统一 Outline：TMP SDF、描边 Sprite、业务 Shader 分别处理；
- Painting 多相框插值和具体角色展示系统；
- RawGame API、Prefab、序列化字段和旧 AnimationClip 的自动兼容层；
- 纵向/变宽无缝轮播、瀑布流，以及列表内部的资源加载器。

## 9. Outline 删除决定

框架不恢复 RawGame `LOutLine`，也不提供统一 `UIOutlineEx`/Shader Binder。原因保持不变：恢复源码缺少完整 Shader 和准确 UV 扩张链路；几何复制会增加顶点与 Overdraw；通用 Binder 无法替业务决定属性名、宽度单位、共享材质或材质实例生命周期。

替代路径：TMP 文本使用 SDF Material Outline；静态图标使用美术预制描边 Sprite；大图和特殊效果由具体业务模块提供经过目标平台验证的 Shader。该决定是稳定的 Core 边界，不是等待补完的临时状态。

## 10. 最终结论

RawGame 吸收的价值不在复刻旧类，而在把真实目标转成 API 语义准确、生命周期明确且可由业务操作验收的 FRFrame 扩展。此前文档中关于 Mesh Raycast、变宽分页、字体合并、多模板稳定 Key、动态 Fenwick、聊天头插和真正无缝轮播的设计项，现在均已有对应 Runtime 实现和实验室场景。

当前可以将这些组件作为常规扩展使用，同时遵守明确边界：普通 Snap 不冒充无缝轮播；InfiniteCarousel 只做横向等宽槽位；外部数据源先修改再通知；动态字体层级显式重建缓存；动态列表尺寸由业务维护；资源异步由 UIItem 管理。本文不以自动化测试数量代替业务价值，也不声称不存在的设备测试或性能数据。
