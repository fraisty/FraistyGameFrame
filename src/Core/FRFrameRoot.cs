using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame
{
    public class FRFrameRoot : MonoBehaviour
    {
        public static readonly string RootName = "FRFrameObject";
        private static FRFrameRoot _instance;
        private bool _frameworkShutdownStarted;
        private bool _frameworkStarted;

        public static bool IsCreated => _instance != null;
        public static bool IsShuttingDown => _instance != null && _instance._frameworkShutdownStarted;
        
        /// <summary>
        /// 提供一个公共接口，获取不到就创建一个， TODO:使用Mono单例替代
        /// </summary>
        public static FRFrameRoot Get()
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<FRFrameRoot>();
            }
            
            if (_instance == null)
            {
                GameObject prefabObj = Resources.Load<GameObject>("Prefabs/" + RootName);
                bool loadedFromPrefab = false;
                
                if (prefabObj != null)
                {
                    GameObject instanceObj = Instantiate(prefabObj);
                    _instance = instanceObj.GetComponent<FRFrameRoot>();

                    if (_instance != null)
                    {
                        loadedFromPrefab = true;
                        Debug.Log("FRFrameRoot Resources Load And Instantiate.");
                    }
                    else
                    {
                        Debug.Log("FRFrameRoot Resources Load Failed, may script lose.");
                        Object.Destroy(instanceObj);
                    }
                }
                
                if (!loadedFromPrefab)
                {
                    GameObject go = new GameObject(RootName);
                    _instance = go.AddComponent<FRFrameRoot>();
                    Debug.Log("FrameRoot Create GameObject.");
                }
                
                _instance.name = RootName;
                
                // 注册所有的模块
                FRFM.RegisterAllManagers();
            }
            
            return _instance;
        }
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            GameObject.DontDestroyOnLoad(this);
        }

        private async void Start()
        {
            _frameworkStarted = true;
            // 启动框架的初始化
            await FRFM.InitializeAsync();

            Debug.Log("FRFrameRoot Start Finish.");
        }

        private void Update()
        {
            FRFM.Update(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                FRLogger.Flush();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                FRLogger.Flush();
            }
        }

        private void OnApplicationQuit()
        {
            ShutdownFramework();
        }

        private void OnDestroy()
        {
            if (_instance != this)
                return;

            ShutdownFramework();
            _instance = null;
        }

        private void ShutdownFramework()
        {
            if (_frameworkShutdownStarted)
                return;

            _frameworkShutdownStarted = true;
            if (Application.isPlaying || _frameworkStarted)
                FRFM.Shutdown();
        }


        public Transform GetOrCreateCompoent(string childName)
        {
            Transform child = transform.Find(childName);
            if (child == null)
            {
                Debug.Log("FRFrameRoot not find child " + childName);
                child = new GameObject(childName).transform;
                child.SetParent(transform, false);
            }

            return child;
        }
    }
}
