using System;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    /// <summary>Event-driven binding of an existing Graphic to a notification tree.
    /// Closing/disabling releases its subscription; reopening reads current state, with no polling.</summary>
    public sealed class UIRedDotBinder:MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private Graphic indicator;
        private UIRedDotTree _tree;
        private IDisposable _binding;
        public void Bind(UIRedDotTree tree,string nodeKey,Graphic graphic)
        {
            if(tree==null)throw new ArgumentNullException(nameof(tree));
            if(string.IsNullOrWhiteSpace(nodeKey))throw new ArgumentException("Notification key required.",nameof(nodeKey));
            if(graphic==null)throw new ArgumentNullException(nameof(graphic));
            _binding?.Dispose();_binding=null;_tree=tree;key=nodeKey;indicator=graphic;
            if(isActiveAndEnabled)Subscribe();
        }
        private void Subscribe(){_binding?.Dispose();_binding=_tree.Subscribe(key,visible=>{if(indicator!=null)indicator.enabled=visible;});}
        private void OnEnable(){if(_tree!=null&&indicator!=null)Subscribe();}
        private void OnDisable(){_binding?.Dispose();_binding=null;}
        private void OnDestroy(){_binding?.Dispose();_binding=null;_tree=null;}
    }
}
