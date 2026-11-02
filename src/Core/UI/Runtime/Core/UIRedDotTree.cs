using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    /// <summary>Instance-owned notification tree. A node is visible when its own flag or any
    /// child is visible. Reparenting refreshes both ancestors and rejects cycles. It owns no timer,
    /// global singleton or gameplay state; presenters supply actual business eligibility.</summary>
    public sealed class UIRedDotTree
    {
        private sealed class Node
        {
            public bool Own,Visible;
            public Node Parent;
            public readonly List<Node> Children=new List<Node>();
            public Action<bool> Changed;
        }
        private readonly Dictionary<string,Node> _nodes=new Dictionary<string,Node>(StringComparer.Ordinal);
        private Node Get(string key)
        {
            if(string.IsNullOrWhiteSpace(key))throw new ArgumentException("Notification key required.",nameof(key));
            if(!_nodes.TryGetValue(key,out var node)){node=new Node();_nodes.Add(key,node);}return node;
        }
        public bool IsVisible(string key)=>Get(key).Visible;
        public void Set(string key,bool visible){var node=Get(key);if(node.Own==visible)return;node.Own=visible;Refresh(node);}
        public void SetParent(string key,string parentKey)
        {
            var node=Get(key);var parent=parentKey==null?null:Get(parentKey);
            for(var ancestor=parent;ancestor!=null;ancestor=ancestor.Parent)
                if(ReferenceEquals(ancestor,node))throw new ArgumentException("Notification hierarchy cannot contain a cycle.");
            if(ReferenceEquals(node.Parent,parent))return;
            var old=node.Parent;old?.Children.Remove(node);node.Parent=parent;parent?.Children.Add(node);
            Refresh(old);Refresh(parent);
        }
        /// <summary>Immediate current-state callback. Dispose removes only this binding;
        /// observers cannot interrupt tree state propagation.</summary>
        public IDisposable Subscribe(string key,Action<bool> callback)
        {
            if(callback==null)throw new ArgumentNullException(nameof(callback));
            var node=Get(key);node.Changed+=callback;Notify(callback,node.Visible);return new Binding(node,callback);
        }
        private static void Refresh(Node node)
        {
            var changed=new List<Node>();
            for(var current=node;current!=null;current=current.Parent){
                bool visible=current.Own||current.Children.Exists(child=>child.Visible);
                if(current.Visible==visible)break;current.Visible=visible;changed.Add(current);
            }
            foreach(var current in changed)if(current.Changed!=null)
                foreach(Action<bool> callback in current.Changed.GetInvocationList())Notify(callback,current.Visible);
        }
        private static void Notify(Action<bool> callback,bool value)
        {try{callback(value);}catch(Exception error){Debug.LogException(error);}}
        private sealed class Binding:IDisposable
        {
            private Node _node;private Action<bool> _callback;
            public Binding(Node node,Action<bool> callback){_node=node;_callback=callback;}
            public void Dispose(){if(_node==null)return;_node.Changed-=_callback;_node=null;_callback=null;}
        }
    }
}
