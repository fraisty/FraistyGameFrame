using UnityEngine;

namespace FRFrame
{
    public sealed class GameObjectPoolOptions
    {
        public int MaxInactiveCount = 0;
        public bool UseGlobalDefaultMaxInactiveCount = true;
        public bool DestroyOverflow = true;
        public bool ResetTransformOnRelease = true;
        public bool ResetLocalPosition = true;
        public bool ResetLocalRotation = true;
        public bool ResetLocalScale = false;
        public Vector3 DefaultLocalPosition = Vector3.zero;
        public Quaternion DefaultLocalRotation = Quaternion.identity;
        public Vector3 DefaultLocalScale = Vector3.one;
    }
}
