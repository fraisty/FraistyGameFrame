using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    public static class ObjectExtension
    {
        private static readonly List<Transform> s_CachedTransform = new List<Transform>();

        public static T GetOrAddComponent<T>(this Component obj) where T : Component
        {
            return obj.gameObject.GetOrAddComponent<T>();
        }
        
        public static T GetOrAddComponent<T>(this GameObject gameObject) where T : Component
        {
            T obj = gameObject.GetComponent<T>();
            if (obj == null)
            {
                obj = gameObject.AddComponent<T>();
            }
            return obj;
        }

        public static Component GetOrAddComponent(this Component obj, Type componentType)
        {
            return obj.gameObject.GetOrAddComponent(componentType);
        }
        
        public static Component GetOrAddComponent(this GameObject gameObject, Type componentType)
        {
            if (gameObject == null) return null;
            Component component = gameObject.GetComponent(componentType);
            if (component == null)
            {
                component = gameObject.AddComponent(componentType);
            }
            return component;
        }

        public static void SetLayerRecursively(this GameObject gameObject, int layer)
        {
            gameObject.GetComponentsInChildren(true, s_CachedTransform);
            for (int i = 0; i < s_CachedTransform.Count; i++)
            {
                s_CachedTransform[i].gameObject.layer = layer;
            }
            s_CachedTransform.Clear();
        }
        
        public static void SetParentAndReset(this Transform transform, Transform parent)
        {
            transform.SetParent(parent,false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
        
        public static void AddOrUpdate<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value)
        {
            if (dict.ContainsKey(key))
                dict[key] = value;
            else
                dict.Add(key, value);
        }
    }
}
