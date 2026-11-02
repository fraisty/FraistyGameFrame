using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// 资源事件参数基类。
    /// 如果你后续想做 ReferencePool，也可以让这些参数实现 IReference。
    /// 当前为了简单直接使用普通 class。
    /// </summary>
    public class ResourceEventArgs
    {
        public ResourceKey Key;
        public string Location;
        public string PackageName;
        public string Error;
        public Object AssetObject;
        public GameObject Instance;
    }
}
