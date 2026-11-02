using UnityEngine;

namespace FRFrame
{
    public abstract class Singleton<T> where T : Singleton<T>, new()
    {
        private static T instance;
        private static readonly object lockObj = new object();

        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (lockObj)
                    {
                        if (instance == null)
                        {
                            instance = new T();
                            instance.OnInitialize();
                        }
                    }
                }
                return instance;
            }
        }

        protected virtual void OnInitialize()
        {
            
        }

        public virtual void Initialize()
        {
            
        }
    }

    public abstract class SingletonMono<T> : MonoBehaviour where T : SingletonMono<T>
    {
        public static T Instance;

        public virtual void Awake()
        {
            if (Instance == null)
            {
                Instance = this as T;
            }
        }
    }
}



