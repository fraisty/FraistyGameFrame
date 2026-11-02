using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
#if FRFRAME_URP_AVAILABLE
using UnityEngine.Rendering.Universal;
#endif
using Object = UnityEngine.Object;

namespace FRFrame
{
    public sealed class UIManager : IManagerBase
    {
        public override int Priority => 20;

        private sealed class UILoadingOperation
        {
            public string FormId;
            public int SerialId;
            public UniTaskCompletionSource<UIForm> Source;
            public CancellationTokenSource CancellationSource;
            public bool CancelRequested;
        }

        private readonly Dictionary<UIGroupLayer, UIGroup> _groups = new Dictionary<UIGroupLayer, UIGroup>();
        private readonly Dictionary<int, UIForm> _formsBySerialId = new Dictionary<int, UIForm>();
        private readonly Dictionary<string, List<UIForm>> _formsById = new Dictionary<string, List<UIForm>>();
        private readonly HashSet<int> _loadingSerialIds = new HashSet<int>();
        private readonly Dictionary<string, UILoadingOperation> _loadingFormsById = new Dictionary<string, UILoadingOperation>();
        private readonly Dictionary<int, UILoadingOperation> _loadingFormsBySerialId = new Dictionary<int, UILoadingOperation>();
        private readonly Dictionary<string, UIPreloadInfo> _preloadInfos = new Dictionary<string, UIPreloadInfo>();
        private readonly HashSet<string> _configGroupPackageFallbackWarnings = new HashSet<string>();
        private readonly List<UIForm> _updateForms = new List<UIForm>();

        private int _serialSeed;
        private GameObject _uiRootGo;
        private Canvas _rootCanvas;
        private Camera _uiCamera;
        private UIConfigRegistry _configRegistry;
        private UIMaskService _maskService;
        private CancellationTokenSource _sceneCancellationSource = new CancellationTokenSource();

        private UIToastService _toastService;
        private UIDialogService _dialogService;
        private UILoadingService _loadingService;
        private UINavigator _navigation;

        public UIConfigRegistry ConfigRegistry => _configRegistry;
        public UIToastService Toast => _toastService;
        public UIDialogService Dialog => _dialogService;
        public UILoadingService Loading => _loadingService;
        public UINavigator Navigation => _navigation;

        public override async UniTask InitializeAsync()
        {
            _configRegistry = new UIConfigRegistry();
            await FRFM.WaitReady<ResourceManager>();
            BuildUIRoot();
            BuildGroups();
            _maskService = new UIMaskService();
            _maskService.Initialize(this, _groups);
            await LoadInitializeConfigGroupsAsync();
            await UIBuiltinFontProvider.PreloadDefaultFontAsync();
            RegisterBuiltinCodeFormConfigs();
            BuildBuiltinServices();
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachUICameraToSceneMainCamera(SceneManager.GetActiveScene());
            FRLog.Info("UIManager initialized.");
        }

        public override void Update(float deltaTime, float unscaledDeltaTime)
        {
            _updateForms.Clear();
            foreach (var form in _formsBySerialId.Values)
            {
                if (form != null)
                    _updateForms.Add(form);
            }

            for (int i = 0; i < _updateForms.Count; i++)
            {
                UIForm form = _updateForms[i];
                if (form != null && _formsBySerialId.ContainsKey(form.SerialId))
                    form.InternalUpdate(deltaTime, unscaledDeltaTime);
            }

            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                Back();

            _toastService?.Update(deltaTime, unscaledDeltaTime);
            _loadingService?.Update(deltaTime, unscaledDeltaTime);
        }

        public override void Destroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            CancelAllLoading();
            var forms = new List<UIForm>(_formsBySerialId.Values);
            for (int i = 0; i < forms.Count; i++)
            {
                if (forms[i] != null)
                    DisposeForm(forms[i], null);
            }

            _formsBySerialId.Clear();
            _formsById.Clear();
            _loadingSerialIds.Clear();
            _loadingFormsById.Clear();
            _loadingFormsBySerialId.Clear();
            _preloadInfos.Clear();
            _updateForms.Clear();
            _maskService?.Dispose();
            _maskService = null;
            _groups.Clear();
            _configRegistry?.Clear();
            _sceneCancellationSource?.Dispose();

            if (_uiRootGo != null)
            {
                Object.Destroy(_uiRootGo);
                _uiRootGo = null;
            }

            FRLog.Info("UIManager destroyed.");
        }

        private void BuildUIRoot()
        {
            Transform root = FRFrameRoot.Get().GetOrCreateCompoent("UIRoot");
            _uiRootGo = root.gameObject;

            _rootCanvas = _uiRootGo.GetComponent<Canvas>();
            if (_rootCanvas == null)
                _rootCanvas = _uiRootGo.AddComponent<Canvas>();

            _rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _rootCanvas.worldCamera = GetOrCreateUICamera();
            _rootCanvas.planeDistance = 10f;
            _rootCanvas.overrideSorting = false;

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                _uiRootGo.SetLayerRecursively(uiLayer);

            if (_uiRootGo.GetComponent<UnityEngine.UI.CanvasScaler>() == null)
            {
                var scaler = _uiRootGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (_uiRootGo.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                _uiRootGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Object.DontDestroyOnLoad(eventSystem);
            }
        }

        private Camera GetOrCreateUICamera()
        {
            if (_uiCamera != null)
                return _uiCamera;

            Transform cameraTransform = FRFrameRoot.Get().transform.Find("UICamera");
            if (cameraTransform == null)
            {
                GameObject cameraGo = new GameObject("UICamera", typeof(Camera));
                cameraGo.transform.SetParent(FRFrameRoot.Get().transform, false);
                cameraTransform = cameraGo.transform;
            }

            _uiCamera = cameraTransform.GetComponent<Camera>();
            if (_uiCamera == null)
                _uiCamera = cameraTransform.gameObject.AddComponent<Camera>();

            _uiCamera.clearFlags = CameraClearFlags.Depth;
            _uiCamera.backgroundColor = Color.clear;
            _uiCamera.orthographic = true;
            _uiCamera.orthographicSize = 5f;
            _uiCamera.nearClipPlane = -50f;
            _uiCamera.farClipPlane = 50f;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                _uiCamera.cullingMask = 1 << uiLayer;
                _uiCamera.gameObject.layer = uiLayer;
            }
            else
            {
                _uiCamera.cullingMask = -1;
            }

            _uiCamera.allowHDR = true;
            _uiCamera.allowMSAA = true;

#if FRFRAME_URP_AVAILABLE
            UniversalAdditionalCameraData uiCameraData = _uiCamera.GetUniversalAdditionalCameraData();
            uiCameraData.renderType = CameraRenderType.Overlay;
            uiCameraData.renderPostProcessing = false;
#endif
            return _uiCamera;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AttachUICameraToSceneMainCamera(scene);
        }

        private void AttachUICameraToSceneMainCamera(Scene scene)
        {
#if FRFRAME_URP_AVAILABLE
            if (_uiCamera == null)
                return;

            Camera mainCamera = FindBaseCameraInScene(scene);
            if (mainCamera == null || mainCamera == _uiCamera)
                return;

            UniversalAdditionalCameraData mainCameraData = mainCamera.GetUniversalAdditionalCameraData();
            if (mainCameraData.renderType != CameraRenderType.Base)
                return;

            List<Camera> stack = mainCameraData.cameraStack;
            if (stack == null)
                return;

            stack.RemoveAll(c => c == null);
            if (!stack.Contains(_uiCamera))
                stack.Add(_uiCamera);
#endif
        }

#if FRFRAME_URP_AVAILABLE
        private Camera FindBaseCameraInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            GameObject[] roots = scene.GetRootGameObjects();
            Camera tagged = null;
            Camera fallback = null;
            for (int i = 0; i < roots.Length; i++)
            {
                Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
                for (int j = 0; j < cameras.Length; j++)
                {
                    Camera camera = cameras[j];
                    if (camera == null || camera == _uiCamera)
                        continue;

                    UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                    if (data.renderType != CameraRenderType.Base)
                        continue;

                    if (camera.CompareTag("MainCamera"))
                    {
                        tagged = camera;
                        break;
                    }

                    if (fallback == null)
                        fallback = camera;
                }

                if (tagged != null)
                    break;
            }

            return tagged != null ? tagged : fallback;
        }
#endif

        private void BuildGroups()
        {
            // Canvas Sorting Order 的有效范围是 Int16，层级基准必须控制在 32767 以内。
            CreateGroup(UIGroupLayer.Background, "Background", 0);
            CreateGroup(UIGroupLayer.Normal, "Normal", 5000);
            CreateGroup(UIGroupLayer.Popup, "Popup", 10000);
            CreateGroup(UIGroupLayer.Top, "Top", 15000);
            CreateGroup(UIGroupLayer.Overlay, "Overlay", 20000);
            CreateGroup(UIGroupLayer.System, "System", 25000);
        }

        private void CreateGroup(UIGroupLayer layer, string name, int sortingOrderBase)
        {
            Transform parent = _uiRootGo.transform.Find(name);
            if (parent == null)
            {
                GameObject go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(_uiRootGo.transform, false);
                var rect = go.transform as RectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                parent = go.transform;
            }

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                parent.gameObject.SetLayerRecursively(uiLayer);

            _groups[layer] = new UIGroup(layer, name, parent, sortingOrderBase);
        }

        private async UniTask LoadInitializeConfigGroupsAsync()
        {
            UIBootstrapConfig uiBootstrap = await BootstrapConfigLoader.LoadUIBootstrapAsync();
            string defaultConfigLocation = uiBootstrap == null || string.IsNullOrEmpty(uiBootstrap.defaultUIConfigLocation)
                ? "Assets/GameRes/Common/Config/UI/UIConfigGroup_Default.asset"
                : uiBootstrap.defaultUIConfigLocation;
            string packageName = uiBootstrap == null ? null : uiBootstrap.defaultPackageName;

            UIConfigGroup groups = await FRFM.Resource.LoadAssetAsync<UIConfigGroup>(defaultConfigLocation, new ResourceLoadOptions
            {
                PackageName = packageName,
                Residency = ResourceResidency.Resident
            });

            if (groups == null)
            {
                FRLog.Warning($"UIManager UIConfigGroup not found : {defaultConfigLocation}, package : {packageName}");
                return;
            }

            if (groups.loadOnInitialize)
                _configRegistry.RegisterGroup(groups);
        }

        public void LoadConfigGroup(UIConfigGroup group)
        {
            if (group != null && !_configRegistry.IsGroupLoaded(group.groupName))
                _configRegistry.RegisterGroup(group);
        }

        public async UniTask LoadConfigGroupAssetAsync(string location, string packageName = null)
        {
            if (string.IsNullOrEmpty(location))
                return;

            UIConfigGroup group = await FRFM.Resource.LoadAssetAsync<UIConfigGroup>(location, new ResourceLoadOptions
            {
                PackageName = packageName,
                Residency = ResourceResidency.Resident
            });

            if (group == null)
            {
                FRLog.Warning($"UIManager.LoadConfigGroupAssetAsync not found asset : {location}, package : {packageName}");
                return;
            }

            if (!_configRegistry.IsGroupLoaded(group.groupName))
                _configRegistry.RegisterGroup(group);
        }

        private void BuildBuiltinServices()
        {
            UIBuiltinConfig config = UIBuiltinConfig.Default;
            _toastService = new UIToastService(this, config);
            _dialogService = new UIDialogService(this, config);
            _loadingService = new UILoadingService(this, config);
            _navigation = new UINavigator(this);
        }

        private void RegisterBuiltinCodeFormConfigs()
        {
            RegisterCodeFormConfig<UIToastForm>(UIGroupLayer.Top, false, UIFormCloseMode.Destroy, false, UICoverVisualMode.KeepVisible, UIAnimationPreset.Fade, UIAnimationPreset.Fade);
            RegisterCodeFormConfig<UIDialogForm>(UIGroupLayer.Popup, true, UIFormCloseMode.Destroy, true, UICoverVisualMode.KeepVisible, UIAnimationPreset.FadeScale, UIAnimationPreset.Fade);
            RegisterCodeFormConfig<UILoadingForm>(UIGroupLayer.System, false, UIFormCloseMode.Cache, true, UICoverVisualMode.KeepVisible, UIAnimationPreset.Fade, UIAnimationPreset.Fade);
        }

        public void RegisterCodeFormConfig<T>(UIGroupLayer layer = UIGroupLayer.Normal, bool useMask = false, UIFormCloseMode closeMode = UIFormCloseMode.Cache, bool resident = false, UICoverVisualMode coverVisualMode = UICoverVisualMode.HideByCanvasGroup, UIAnimationPreset openAnimation = UIAnimationPreset.None, UIAnimationPreset closeAnimation = UIAnimationPreset.None, bool overrideExisting = false) where T : UIForm
        {
            Type type = typeof(T);
            if (!overrideExisting && _configRegistry.TryGet(type.Name, out _))
                return;

            RegisterConfig(new UIFormConfig
            {
                formId = type.Name,
                logicType = type.FullName,
                assetPath = string.Empty,
                configGroup = "Code",
                layer = layer,
                allowMultiInstance = false,
                closeMode = closeMode,
                resident = resident,
                coverVisualMode = coverVisualMode,
                useMask = useMask,
                maskMode = useMask ? UIMaskMode.Dim : UIMaskMode.None,
                closeOnMaskClick = false,
                openAnimation = openAnimation,
                closeAnimation = closeAnimation
            });
        }

        public void RegisterConfig(UIFormConfig config)
        {
            _configRegistry.Register(config);
        }

        public bool IsLoading(string formId)
        {
            return !string.IsNullOrEmpty(formId) && _loadingFormsById.ContainsKey(formId);
        }

        public bool IsLoading<T>() where T : UIForm => IsLoading(typeof(T).Name);
        public bool IsAnyLoading() => _loadingSerialIds.Count > 0;

        public UniTask<UIForm> OpenAsync(string formId, object userData = null)
        {
            return OpenAsync(formId, UIOpenOptions.FromUserData(userData));
        }

        public UniTask<UIForm> OpenAsync(string formId, object userData, CancellationToken cancellationToken)
        {
            return OpenAsync(formId, UIOpenOptions.FromUserData(userData, cancellationToken));
        }

        public async UniTask<UIForm> OpenAsync(string formId, UIOpenOptions options)
        {
            options = UIOpenOptions.Normalize(options);

            if (string.IsNullOrEmpty(formId))
            {
                FRLog.Error("UIManager.OpenAsync failed, formId is null or empty.");
                return null;
            }

            if (!_configRegistry.TryGet(formId, out UIFormConfig config))
            {
                FRLog.Error($"UIManager.OpenAsync failed, form config not found : {formId}");
                return null;
            }

            CancellationTokenSource linkedSource = CreateLinkedCancellation(options.CancellationToken, options.Timeout);
            CancellationToken cancellationToken = linkedSource.Token;

            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return null;

                if (!config.allowMultiInstance)
                {
                    UIForm existing = Get(formId);
                    if (existing != null)
                    {
                        await OpenExistingFormAsync(existing, options, cancellationToken);
                        return existing;
                    }

                    if (_loadingFormsById.TryGetValue(formId, out UILoadingOperation loadingOperation))
                    {
                        UIForm loadingForm = await loadingOperation.Source.Task;
                        if (loadingForm != null && !cancellationToken.IsCancellationRequested)
                            await OpenExistingFormAsync(loadingForm, options, cancellationToken);
                        return cancellationToken.IsCancellationRequested ? null : loadingForm;
                    }
                }

                int serialId = ++_serialSeed;
                UILoadingOperation operation = CreateLoadingOperation(formId, serialId, config, linkedSource);

                try
                {
                    GameObject instance = string.IsNullOrEmpty(config.assetPath)
                        ? CreateCodeFormRoot(formId, GetGroupRoot(config.layer))
                        : await FRFM.Resource.InstantiateAsync(config.assetPath, GetGroupRoot(config.layer), new ResourceLoadOptions
                        {
                            PackageName = ResolvePackageName(config),
                        });

                    if (instance == null || operation.CancelRequested || cancellationToken.IsCancellationRequested)
                    {
                        if (instance != null)
                            RecycleOrDestroyFormInstance(instance, config);
                        operation.Source?.TrySetResult(null);
                        return null;
                    }

                    UIForm form = CreateFormInstance(instance, config, formId, serialId, options.UserData);
                    if (form == null)
                    {
                        FRFM.Resource.Recycle(instance);
                        operation.Source?.TrySetResult(null);
                        return null;
                    }

                    RegisterForm(form, config);
                    form.ApplyOpenOptions(options);
                    ApplyOpenParam(form, options.UserData, options.StrongParamType);
                    await _maskService.PrepareBlurAsync(form);
                    form.InternalOpen(options.UserData);
                    RefreshAllVisualStates();

                    if (options.PlayAnimation)
                        await form.InternalPlayOpenAnimationAsync(cancellationToken).SuppressCancellationThrow();

                    operation.Source?.TrySetResult(form);
                    return form;
                }
                finally
                {
                    RemoveLoadingOperation(operation, config);
                }
            }
            finally
            {
                linkedSource?.Dispose();
            }
        }

        public UniTask<T> OpenAsync<T>(object userData = null) where T : UIForm
        {
            return OpenAsync<T>(userData, default);
        }

        public async UniTask<T> OpenAsync<T>(object userData, CancellationToken cancellationToken) where T : UIForm
        {
            UIForm form = await OpenAsync(typeof(T).Name, userData, cancellationToken);
            return form as T;
        }

        public async UniTask<UIOpenResult<T>> TryOpenAsync<T>(object userData = null, CancellationToken cancellationToken = default) where T : UIForm
        {
            if (!_configRegistry.TryGet(typeof(T).Name, out _))
                return UIOpenResult<T>.Failure(UIOpenFailureReason.ConfigNotFound, typeof(T).Name);

            T form = await OpenAsync<T>(userData, cancellationToken);
            if (form != null)
                return UIOpenResult<T>.Success(form);

            return cancellationToken.IsCancellationRequested
                ? UIOpenResult<T>.Failure(UIOpenFailureReason.Canceled, typeof(T).Name)
                : UIOpenResult<T>.Failure(UIOpenFailureReason.LoadFailed, typeof(T).Name);
        }

        public async UniTask<UIOpenResult<UIForm>> TryOpenAsync(string formId, UIOpenOptions options)
        {
            if (string.IsNullOrEmpty(formId))
                return UIOpenResult<UIForm>.Failure(UIOpenFailureReason.InvalidFormId);

            if (!_configRegistry.TryGet(formId, out _))
                return UIOpenResult<UIForm>.Failure(UIOpenFailureReason.ConfigNotFound, formId);

            UIForm form = await OpenAsync(formId, options);
            if (form != null)
                return UIOpenResult<UIForm>.Success(form);

            return options.CancellationToken.IsCancellationRequested
                ? UIOpenResult<UIForm>.Failure(UIOpenFailureReason.Canceled, formId)
                : UIOpenResult<UIForm>.Failure(UIOpenFailureReason.LoadFailed, formId);
        }

        public UniTask<T> OpenAsync<T, TParam>(TParam param) where T : UIForm
        {
            return OpenAsync<T, TParam>(param, CancellationToken.None);
        }

        public async UniTask<T> OpenAsync<T, TParam>(TParam param, CancellationToken cancellationToken) where T : UIForm
        {
            UIOpenOptions options = UIOpenOptions.FromUserData(param, cancellationToken);
            options.StrongParamType = typeof(TParam);
            UIForm form = await OpenAsync(typeof(T).Name, options);
            return form as T;
        }

        public async UniTask<T> OpenAsync<T, TParam>(TParam param, UIOpenOptions options) where T : UIForm
        {
            options.UserData = param;
            options.StrongParamType = typeof(TParam);
            UIForm form = await OpenAsync(typeof(T).Name, options);
            return form as T;
        }

        public UniTask<T> OpenCodeAsync<T>(object userData = null, UIGroupLayer layer = UIGroupLayer.Normal, CancellationToken cancellationToken = default) where T : UIForm
        {
            RegisterCodeFormConfig<T>(layer);
            return OpenAsync<T>(userData, cancellationToken);
        }

        public UniTask<T> OpenCodeAsync<T, TParam>(TParam param, UIGroupLayer layer = UIGroupLayer.Normal, CancellationToken cancellationToken = default) where T : UIForm
        {
            RegisterCodeFormConfig<T>(layer);
            return OpenAsync<T, TParam>(param, cancellationToken);
        }

        public UniTask<T> OpenCodeAsync<T, TParam>(TParam param, UIOpenOptions options, UIGroupLayer layer = UIGroupLayer.Normal) where T : UIForm
        {
            RegisterCodeFormConfig<T>(layer);
            return OpenAsync<T, TParam>(param, options);
        }

        public UIOpenHandle<T> OpenWithHandle<T>(object userData = null) where T : UIForm
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            UniTask<T> task = OpenAsync<T>(userData, cts.Token);
            return new UIOpenHandle<T>(task, cts.Cancel);
        }

        public UIOpenHandle<T> OpenWithHandle<T, TParam>(TParam param) where T : UIForm
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            UniTask<T> task = OpenAsync<T, TParam>(param, cts.Token);
            return new UIOpenHandle<T>(task, cts.Cancel);
        }

        public void Close(string formId, object userData = null)
        {
            if (CancelLoading(formId))
                return;

            UIForm form = Get(formId);
            if (form != null)
                Close(form, userData);
        }

        public void Close<T>(object userData = null) where T : UIForm => Close(typeof(T).Name, userData);

        public void Close(UIForm form, object userData = null)
        {
            CloseAsync(form, userData).Forget();
        }

        public UniTask CloseAsync(string formId, object userData = null, CancellationToken cancellationToken = default)
        {
            UIForm form = Get(formId);
            return form == null ? UniTask.CompletedTask : CloseAsync(form, userData, cancellationToken);
        }

        public async UniTask CloseAsync(UIForm form, object userData = null, CancellationToken cancellationToken = default)
        {
            if (form == null || form.State == UIFormState.Disposed || form.State == UIFormState.Closing)
                return;

            form.InternalBeginClose();
            await form.InternalPlayCloseAnimationAsync(cancellationToken).SuppressCancellationThrow();
            form.InternalClose(userData);

            if (form.Config != null && (form.Config.resident || form.Config.closeMode == UIFormCloseMode.Cache))
            {
                form.InternalHide();
                RefreshAllVisualStates();
                return;
            }

            DisposeForm(form, userData);
        }

        public void Hide(UIForm form)
        {
            if (form == null)
                return;

            form.InternalHide();
            RefreshAllVisualStates();
        }

        public void Hide(string formId)
        {
            Hide(Get(formId));
        }

        public void Hide<T>() where T : UIForm
        {
            Hide(typeof(T).Name);
        }

        public void Show(UIForm form, object userData = null)
        {
            if (form == null)
                return;

            if (form.Config != null && form.Config.bringToFrontOnOpen)
                form.Group?.BringToFront(form);

            form.InternalShow(userData);
            RefreshAllVisualStates();
        }

        public void Show(string formId, object userData = null)
        {
            Show(Get(formId), userData);
        }

        public void Show<T>(object userData = null) where T : UIForm
        {
            Show(typeof(T).Name, userData);
        }

        public async UniTask PreloadAsync(string formId, UIPreloadMode mode = UIPreloadMode.AssetOnly, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(formId) || !_configRegistry.TryGet(formId, out UIFormConfig config))
                return;

            UIPreloadInfo info = GetOrCreatePreloadInfo(formId, config, mode);
            info.Mode = mode;

            if ((mode == UIPreloadMode.AssetOnly || mode == UIPreloadMode.AssetAndInstance) && !string.IsNullOrEmpty(config.assetPath))
            {
                await FRFM.Resource.PreloadAssetAsync<GameObject>(config.assetPath, new ResourceLoadOptions
                {
                    PackageName = ResolvePackageName(config),
                    TrackReference = false,
                    AllowCache = true
                });

                if (cancellationToken.IsCancellationRequested)
                    return;

                info.AssetPreloaded = true;
                info.LastPreloadTime = Time.realtimeSinceStartup;
            }

            if (mode == UIPreloadMode.AssetOnly)
                return;

            if (Get(formId) != null)
            {
                info.InstanceWarmed = true;
                info.LastPreloadTime = Time.realtimeSinceStartup;
                return;
            }

            GameObject instance = string.IsNullOrEmpty(config.assetPath)
                ? CreateCodeFormRoot(formId, GetGroupRoot(config.layer))
                : await FRFM.Resource.InstantiateAsync(config.assetPath, GetGroupRoot(config.layer), new ResourceLoadOptions
                {
                    PackageName = ResolvePackageName(config),
                });

            if (instance == null || cancellationToken.IsCancellationRequested)
            {
                if (instance != null)
                    RecycleOrDestroyFormInstance(instance, config);
                return;
            }

            instance.SetActive(false);
            int serialId = ++_serialSeed;
            UIForm form = CreateFormInstance(instance, config, formId, serialId, null);
            if (form == null)
            {
                RecycleOrDestroyFormInstance(instance, config);
                return;
            }

            RegisterForm(form, config);
            form.ApplyOpenOptions(UIOpenOptions.Default);
            if (form is IUIPreloadHandler preloadHandler)
                await preloadHandler.OnPreloadAsync(cancellationToken).SuppressCancellationThrow();
            form.InternalPreload();
            info.InstanceWarmed = true;
            info.LastPreloadTime = Time.realtimeSinceStartup;
            RefreshAllVisualStates();
        }

        public UniTask PreloadAsync<T>(UIPreloadMode mode = UIPreloadMode.AssetOnly, CancellationToken cancellationToken = default) where T : UIForm
        {
            return PreloadAsync(typeof(T).Name, mode, cancellationToken);
        }

        public void ReleasePreload(string formId)
        {
            UIForm form = Get(formId);
            if (form != null && form.State == UIFormState.Preloaded)
                DisposeForm(form, null);
        }

        public void ReleaseAllPreloads()
        {
            List<UIForm> preloadedForms = new List<UIForm>();
            foreach (UIForm form in _formsBySerialId.Values)
            {
                if (form != null && form.State == UIFormState.Preloaded)
                    preloadedForms.Add(form);
            }

            for (int i = 0; i < preloadedForms.Count; i++)
                DisposeForm(preloadedForms[i], null);
        }

        public bool IsPreloaded(string formId)
        {
            if (!string.IsNullOrEmpty(formId) && _preloadInfos.TryGetValue(formId, out UIPreloadInfo info) && (info.AssetPreloaded || info.InstanceWarmed))
                return true;

            UIForm form = Get(formId);
            return form != null && form.State == UIFormState.Preloaded;
        }

        public bool Back()
        {
            if (CloseTopBackForm())
                return true;

            return _navigation != null && _navigation.Back();
        }

        public bool CloseTopBackForm(object userData = null)
        {
            for (int layer = (int)UIGroupLayer.System; layer >= (int)UIGroupLayer.Background; layer--)
            {
                if (!_groups.TryGetValue((UIGroupLayer)layer, out UIGroup group))
                    continue;

                UIForm form = group.GetTopBackCloseForm();
                if (form == null)
                    continue;

                if (form.InternalTryHandleBack())
                    return true;

                Close(form, userData);
                return true;
            }

            return false;
        }

        public bool CancelLoading(string formId)
        {
            if (string.IsNullOrEmpty(formId) || !_loadingFormsById.TryGetValue(formId, out UILoadingOperation operation))
                return false;

            operation.CancelRequested = true;
            operation.CancellationSource?.Cancel();
            operation.Source?.TrySetResult(null);
            return true;
        }

        public bool CancelLoading(int serialId)
        {
            if (!_loadingFormsBySerialId.TryGetValue(serialId, out UILoadingOperation operation))
                return false;

            operation.CancelRequested = true;
            operation.CancellationSource?.Cancel();
            operation.Source?.TrySetResult(null);
            return true;
        }

        public void CancelSceneLoading()
        {
            _sceneCancellationSource.Cancel();
            CancelAllLoading();
        }

        public void ResetSceneCancellation()
        {
            _sceneCancellationSource.Dispose();
            _sceneCancellationSource = new CancellationTokenSource();
        }

        public UIForm Get(string formId)
        {
            if (string.IsNullOrEmpty(formId))
                return null;

            return _formsById.TryGetValue(formId, out var list) && list.Count > 0 ? list[0] : null;
        }

        public T Get<T>() where T : UIForm => Get(typeof(T).Name) as T;

        public List<UIForm> GetAll(string formId)
        {
            if (string.IsNullOrEmpty(formId))
                return null;

            return _formsById.TryGetValue(formId, out var list) ? list : null;
        }

        public List<T> GetAll<T>() where T : UIForm
        {
            string formId = typeof(T).Name;
            List<T> result = new List<T>();
            if (_formsById.TryGetValue(formId, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is T t)
                        result.Add(t);
                }
            }
            return result;
        }

        public bool IsOpen(string formId)
        {
            if (string.IsNullOrEmpty(formId) || !_formsById.TryGetValue(formId, out var list))
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                UIForm form = list[i];
                if (form != null && form.State == UIFormState.Opened && form.IsVisible)
                    return true;
            }

            return false;
        }

        public bool IsOpen<T>() where T : UIForm => IsOpen(typeof(T).Name);

        public UIManagerSnapshot CreateSnapshot()
        {
            UIManagerSnapshot snapshot = new UIManagerSnapshot
            {
                NavigationCount = _navigation != null ? _navigation.Count : 0,
                Toast = _toastService?.CreateSnapshot(),
                Dialog = _dialogService?.CreateSnapshot(),
                Loading = _loadingService?.CreateSnapshot(),
                Masks = _maskService != null ? _maskService.CreateSnapshots() : new List<UIMaskSnapshot>()
            };

            foreach (var pair in _groups)
            {
                snapshot.Groups.Add(new UIGroupSnapshot
                {
                    Layer = pair.Key,
                    Name = pair.Value.Name,
                    FormCount = pair.Value.Forms.Count,
                    SortingOrderBase = pair.Value.SortingOrderBase
                });
            }

            foreach (UIForm form in _formsBySerialId.Values)
            {
                if (form == null)
                    continue;

                if (form.State == UIFormState.Opened)
                    snapshot.OpenedCount++;
                else if (form.State == UIFormState.Hidden)
                    snapshot.HiddenCount++;
                else if (form.State == UIFormState.Preloaded)
                    snapshot.PreloadedCount++;
                else if (form.State == UIFormState.Loading)
                    snapshot.LoadingCount++;

                snapshot.Forms.Add(new UIFormSnapshot
                {
                    SerialId = form.SerialId,
                    FormId = form.FormId,
                    LogicType = form.Config != null ? form.Config.logicType : null,
                    AssetPath = form.Config != null ? form.Config.assetPath : null,
                    ConfigGroup = form.Config != null ? form.Config.configGroup : null,
                    PackageName = form.Config != null ? ResolvePackageName(form.Config) : null,
                    Layer = form.Config != null ? form.Config.layer : UIGroupLayer.Normal,
                    State = form.State,
                    SortingOrder = form.SortingOrder,
                    Depth = form.Group != null ? form.Group.GetDepth(form) : -1,
                    IsTopOfFormId = GetTop(form.FormId) == form,
                    IsVisible = form.IsVisible,
                    IsPaused = form.IsPaused,
                    IsCovered = form.IsCovered,
                    UseMask = form.UseMask,
                    MaskMode = form.MaskMode,
                    CloseOnMaskClick = form.CloseOnMaskClick
                });
            }

            foreach (UILoadingOperation operation in _loadingFormsBySerialId.Values)
            {
                snapshot.LoadingForms.Add(new UILoadingFormSnapshot
                {
                    FormId = operation.FormId,
                    SerialId = operation.SerialId,
                    CancelRequested = operation.CancelRequested,
                    HasFormInstance = _formsBySerialId.ContainsKey(operation.SerialId)
                });
            }
            snapshot.LoadingCount += _loadingFormsBySerialId.Count;

            foreach (UIPreloadInfo info in _preloadInfos.Values)
            {
                snapshot.Preloads.Add(new UIPreloadSnapshot
                {
                    FormId = info.FormId,
                    Mode = info.Mode,
                    AssetPreloaded = info.AssetPreloaded,
                    InstanceWarmed = info.InstanceWarmed,
                    PackageName = info.PackageName,
                    AssetPath = info.AssetPath,
                    LastPreloadTime = info.LastPreloadTime
                });
            }

            return snapshot;
        }

        public UIForm GetTop(string formId)
        {
            if (string.IsNullOrEmpty(formId) || !_formsById.TryGetValue(formId, out var list))
                return null;

            UIForm top = null;
            int topOrder = int.MinValue;
            for (int i = 0; i < list.Count; i++)
            {
                UIForm form = list[i];
                if (form == null || form.State == UIFormState.Disposed)
                    continue;

                int order = form.SortingOrder;
                if (top == null || order >= topOrder)
                {
                    top = form;
                    topOrder = order;
                }
            }

            return top;
        }

        public T GetTop<T>() where T : UIForm => GetTop(typeof(T).Name) as T;

        public bool TryGetBySerialId(int serialId, out UIForm form)
        {
            return _formsBySerialId.TryGetValue(serialId, out form);
        }

        public void CloseTop(string formId, object userData = null)
        {
            UIForm form = GetTop(formId);
            if (form != null)
                Close(form, userData);
        }

        public void CloseTop<T>(object userData = null) where T : UIForm => CloseTop(typeof(T).Name, userData);

        public void CloseAll(string formId, object userData = null)
        {
            if (CancelLoading(formId))
                return;

            List<UIForm> forms = GetAll(formId);
            if (forms == null)
                return;

            List<UIForm> snapshot = new List<UIForm>(forms);
            for (int i = snapshot.Count - 1; i >= 0; i--)
                Close(snapshot[i], userData);
        }

        public void CloseAll<T>(object userData = null) where T : UIForm => CloseAll(typeof(T).Name, userData);

        public void Close(int serialId, object userData = null)
        {
            if (CancelLoading(serialId))
                return;

            if (_formsBySerialId.TryGetValue(serialId, out UIForm form))
                Close(form, userData);
        }

        public UniTask CloseAsync(int serialId, object userData = null, CancellationToken cancellationToken = default)
        {
            return _formsBySerialId.TryGetValue(serialId, out UIForm form) ? CloseAsync(form, userData, cancellationToken) : UniTask.CompletedTask;
        }

        internal void RemoveFromNavigation(UIForm form)
        {
            _navigation?.Remove(form);
        }

        private async UniTask OpenExistingFormAsync(UIForm form, UIOpenOptions options, CancellationToken cancellationToken)
        {
            ApplyOpenParam(form, options.UserData, options.StrongParamType);
            form.ApplyOpenOptions(options);
            if (form.Config != null && (options.BringToFront ?? form.Config.bringToFrontOnOpen))
                form.Group?.BringToFront(form);

            bool willOpen = form.State == UIFormState.Preloaded ||
                            form.State == UIFormState.Hidden ||
                            !form.gameObject.activeSelf ||
                            form.State != UIFormState.Opened;
            if (willOpen)
                await _maskService.PrepareBlurAsync(form);

            if (form.State == UIFormState.Preloaded)
                form.InternalOpen(options.UserData);
            else if (form.State == UIFormState.Hidden || !form.gameObject.activeSelf)
                form.InternalShow(options.UserData);
            else if (form.State != UIFormState.Opened)
                form.InternalOpen(options.UserData);

            RefreshAllVisualStates();
            if (options.PlayAnimation)
                await form.InternalPlayOpenAnimationAsync(cancellationToken).SuppressCancellationThrow();
        }

        private GameObject CreateCodeFormRoot(string formId, Transform parent)
        {
            GameObject instance = new GameObject(formId, typeof(RectTransform));
            instance.transform.SetParent(parent, false);
            RectTransform rect = instance.transform as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                instance.SetLayerRecursively(uiLayer);
            return instance;
        }

        private UIForm CreateFormInstance(GameObject instance, UIFormConfig config, string formId, int serialId, object userData)
        {
            if (!(instance.transform is RectTransform))
                FRLog.Warning($"UIManager.OpenAsync warning, ui root is not RectTransform : {formId}, asset : {config.assetPath}");

            Type logicType = ResolveLogicType(config.logicType);
            if (logicType == null)
            {
                FRLog.Error($"UIManager.OpenAsync failed, logic type not found : {config.logicType}");
                return null;
            }

            UIForm form = instance.GetComponent(logicType) as UIForm;
            if (form == null)
                form = instance.AddComponent(logicType) as UIForm;

            if (form == null)
            {
                FRLog.Error($"UIManager.OpenAsync failed, form component create failed : {config.logicType}");
                return null;
            }

            UIGroup group = _groups[config.layer];
            form.SerialId = serialId;
            form.Group = group;
            form.InternalSetLoading();
            form.InternalInitialize(new UIFormOpenContext { FormId = formId, Config = config, UserData = userData });
            instance.transform.SetParent(group.Root, false);
            instance.SetLayerRecursively(group.Root.gameObject.layer);
            return form;
        }

        private void RegisterForm(UIForm form, UIFormConfig config)
        {
            if (!_formsById.TryGetValue(form.FormId, out var list))
            {
                list = new List<UIForm>();
                _formsById.Add(form.FormId, list);
            }
            list.Add(form);
            _formsBySerialId.Add(form.SerialId, form);
            UIGroup group = _groups[config.layer];
            group.Add(form);
            if (config.bringToFrontOnOpen)
                group.BringToFront(form);
        }

        private void DisposeForm(UIForm form, object userData)
        {
            if (form == null)
                return;

            RemoveFromNavigation(form);
            form.Group?.Remove(form);
            _formsBySerialId.Remove(form.SerialId);
            if (_formsById.TryGetValue(form.FormId, out var list))
            {
                list.Remove(form);
                if (list.Count == 0)
                    _formsById.Remove(form.FormId);
            }

            form.InternalDispose();
            if (form.gameObject != null)
                RecycleOrDestroyFormInstance(form.gameObject, form.Config);

            RefreshAllVisualStates();
        }

        private void RecycleOrDestroyFormInstance(GameObject instance, UIFormConfig config)
        {
            if (instance == null)
                return;

            // Destroy-mode forms must not be returned to the resource pool. A pooled
            // UIForm has already been disposed (including event unbinding), while
            // UIForm.IsInitialized remains true, so reusing it skips BindEvent and
            // produces a dead UI on the next open.
            if (config == null ||
                string.IsNullOrEmpty(config.assetPath) ||
                config.closeMode == UIFormCloseMode.Destroy)
            {
                if (string.IsNullOrEmpty(config?.assetPath))
                    Object.Destroy(instance);
                else
                    FRFM.Resource.DestroyInstance(instance);
            }
            else
            {
                FRFM.Resource.Recycle(instance);
            }
        }

        private UILoadingOperation CreateLoadingOperation(string formId, int serialId, UIFormConfig config, CancellationTokenSource cancellationSource)
        {
            UILoadingOperation operation = new UILoadingOperation
            {
                FormId = formId,
                SerialId = serialId,
                CancellationSource = cancellationSource,
                Source = config.allowMultiInstance ? null : new UniTaskCompletionSource<UIForm>()
            };
            _loadingSerialIds.Add(serialId);
            _loadingFormsBySerialId.Add(serialId, operation);
            if (!config.allowMultiInstance)
                _loadingFormsById.Add(formId, operation);
            return operation;
        }

        private void RemoveLoadingOperation(UILoadingOperation operation, UIFormConfig config)
        {
            if (operation == null)
                return;

            _loadingSerialIds.Remove(operation.SerialId);
            _loadingFormsBySerialId.Remove(operation.SerialId);
            if (!config.allowMultiInstance)
                _loadingFormsById.Remove(operation.FormId);
        }

        private CancellationTokenSource CreateLinkedCancellation(CancellationToken cancellationToken, float timeout)
        {
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sceneCancellationSource.Token);
            if (timeout > 0f)
                linked.CancelAfterSlim(TimeSpan.FromSeconds(timeout));
            return linked;
        }

        private void ApplyOpenParam(UIForm form, object userData, Type strongParamType)
        {
            if (form == null || userData == null)
                return;

            Type userDataType = userData.GetType();
            Type[] interfaces = form.GetType().GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                Type interfaceType = interfaces[i];
                if (!interfaceType.IsGenericType || interfaceType.GetGenericTypeDefinition() != typeof(IUIOpenParamReceiver<>))
                    continue;

                Type paramType = interfaceType.GetGenericArguments()[0];
                if (!paramType.IsAssignableFrom(userDataType))
                    continue;

                interfaceType.GetMethod(nameof(IUIOpenParamReceiver<object>.SetOpenParam))?.Invoke(form, new[] { userData });
                return;
            }

            if (strongParamType != null)
                FRLog.Warning($"UIManager.OpenAsync warning, form {form.GetType().Name} does not implement IUIOpenParamReceiver<{strongParamType.Name}>.");
        }

        private void CancelAllLoading()
        {
            var operations = new List<UILoadingOperation>(_loadingFormsBySerialId.Values);
            for (int i = 0; i < operations.Count; i++)
            {
                operations[i].CancelRequested = true;
                operations[i].CancellationSource?.Cancel();
                operations[i].Source?.TrySetResult(null);
            }
        }

        private void RefreshAllVisualStates()
        {
            RefreshAllCoverStates();
            _maskService?.Refresh(_groups);
        }

        private void RefreshAllCoverStates()
        {
            bool covered = false;
            for (int layer = (int)UIGroupLayer.System; layer >= (int)UIGroupLayer.Background; layer--)
            {
                if (_groups.TryGetValue((UIGroupLayer)layer, out UIGroup group))
                    covered = group.ApplyCoverState(covered);
            }
        }

        private Transform GetGroupRoot(UIGroupLayer layer) => _groups[layer].Root;

        private UIPreloadInfo GetOrCreatePreloadInfo(string formId, UIFormConfig config, UIPreloadMode mode)
        {
            if (!_preloadInfos.TryGetValue(formId, out UIPreloadInfo info))
            {
                info = new UIPreloadInfo
                {
                    FormId = formId
                };
                _preloadInfos.Add(formId, info);
            }

            info.Mode = mode;
            info.PackageName = ResolvePackageName(config);
            info.AssetPath = config != null ? config.assetPath : null;
            return info;
        }

        internal string ResolvePackageName(UIFormConfig config)
        {
            if (config == null)
                return null;

            if (!string.IsNullOrEmpty(config.packageName))
                return config.packageName;

            string packageName = _configRegistry?.GetDefaultPackageName(config.configGroup);
            if (!string.IsNullOrEmpty(packageName))
                return packageName;

            if (!string.IsNullOrEmpty(config.configGroup) && _configGroupPackageFallbackWarnings.Add(config.configGroup))
                FRLog.Warning($"UI config uses configGroup as package fallback: {config.configGroup}. Please set packageName/defaultPackageName explicitly.");

            return string.IsNullOrEmpty(config.configGroup) ? null : config.configGroup;
        }

        private Type ResolveLogicType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return null;

            Type type = Type.GetType(fullTypeName);
            if (type != null)
                return type;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullTypeName);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
