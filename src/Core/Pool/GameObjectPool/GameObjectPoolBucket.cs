using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    public sealed class GameObjectPoolBucket
    {
        public ResourceKey ResourceKey;
        public string Location;
        public Transform Root;
        public readonly Stack<GameObject> InactiveStack = new Stack<GameObject>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public readonly HashSet<GameObject> InactiveSet = new HashSet<GameObject>();
#endif

        public int CountInactive => InactiveStack.Count;
    }
}
