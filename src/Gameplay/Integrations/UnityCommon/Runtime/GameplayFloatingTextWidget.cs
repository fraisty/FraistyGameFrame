using System;
using TMPro;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Reusable world-space UIWidget. Existing Core UI owns initialization/visibility and
    /// event cleanup; Core Pool owns reuse. Motion/fade is sampled by the actor's presentation driver.</summary>
    public sealed class GameplayFloatingTextWidget:UIWidget,IPoolRecycleHandler
    {
        private TMP_Text _text;
        private Color _color;
        private TMP_FontAsset _font;
        private Material _material;
        public string DisplayText=>_text==null?string.Empty:_text.text;
        protected override void OnInit()
        {
            _text=GetComponentInChildren<TMP_Text>(true);if(_text==null)throw new InvalidOperationException("Floating widget requires TMP text.");
            // Preview and learning scenes must not add glyphs or atlas pixels to a shared asset.
            // A pooled widget keeps its own dynamic font/atlas until the widget is destroyed.
            if(_text.font!=null){
                var source=_text.font;
                if(source.sourceFontFile!=null){
                    _font=TMP_FontAsset.CreateFontAsset(source.sourceFontFile,(int)source.faceInfo.pointSize,source.atlasPadding,source.atlasRenderMode,source.atlasWidth,source.atlasHeight,AtlasPopulationMode.Dynamic);
                }
                // TMP_FontAsset.OnDestroy destroys its atlas and material even in Static mode.
                // Borrow the immutable static font itself instead of cloning an atlas-owning object.
                var atlasTextures=_font!=null?_font.atlasTextures:source.atlasTextures;
                if(_font!=null){_font.hideFlags=HideFlags.HideAndDontSave;foreach(var atlas in atlasTextures)if(atlas!=null)atlas.hideFlags=HideFlags.HideAndDontSave;}
                _material=Instantiate(source.material);_material.hideFlags=HideFlags.HideAndDontSave;
                if(atlasTextures.Length>0)_material.mainTexture=atlasTextures[0];
                if(_font!=null){Release(_font.material);_font.material=_material;_text.font=_font;}
                _text.fontSharedMaterial=_material;
            }
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            // Dynamic font owns its private atlas/material; static font is never destroyed here.
            if(_font!=null)Release(_font);else Release(_material);
        }
        private static void Release(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
        protected override void OnOpen(object userData)=>OnRefresh(userData);
        protected override void OnRefresh(object userData)
        {
            if(!(userData is GameplayFloatingTextContent content))throw new ArgumentException("Floating content is required.",nameof(userData));
            _text.text=content.Text;_color=content.Color;_text.color=_color;_text.raycastTarget=false;_text.textWrappingMode=TextWrappingModes.NoWrap;
        }
        public void SetOpacity(float opacity)
        {if(_text==null)return;var color=_color;color.a*=Mathf.Clamp01(opacity);_text.color=color;}
        public void OnRecycleToPool(){SetVisible(false);if(_text!=null)_text.text=string.Empty;}
    }
    public readonly struct GameplayFloatingTextContent
    {
        public string Text {get;}
        public Color Color {get;}
        public GameplayFloatingTextContent(string text,Color color){Text=text;Color=color;}
    }
}
