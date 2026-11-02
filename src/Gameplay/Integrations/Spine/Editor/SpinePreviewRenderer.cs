using System;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Spine.Editor
{
    /// <summary>Disposable EditMode-only Spine viewport. It never advances a Gameplay clock.</summary>
    public sealed class SpinePreviewRenderer : IDisposable
    {
        private SkeletonAnimation _skeleton;
        private Renderer _renderer;
        private GameObject _cameraObject;
        private Camera _camera;
        private RenderTexture _texture;

        public void Load(SkeletonDataAsset asset, string[] skinNames = null, Material material = null)
        {
            Dispose();
            if (asset == null) return;
            try
            {
                _skeleton = SkeletonAnimation.NewSkeletonAnimationGameObject(asset, true);
                if (_skeleton == null || !_skeleton.valid)
                    throw new InvalidOperationException("Spine skeleton did not initialize.");
                _skeleton.gameObject.hideFlags = HideFlags.HideAndDontSave;
                _skeleton.gameObject.layer = 30;
                _renderer = _skeleton.GetComponent<Renderer>();
                if (material != null && _renderer.sharedMaterial != null)
                    _skeleton.CustomMaterialOverride[_renderer.sharedMaterial] = material;
                if (skinNames != null && skinNames.Length > 0)
                {
                    var skin = new global::Spine.Skin("Editor preview");
                    foreach (string name in skinNames)
                    { var part = _skeleton.Skeleton.Data.FindSkin(name); if (part != null) skin.AddSkin(part); }
                    _skeleton.Skeleton.SetSkin(skin); _skeleton.Skeleton.SetSlotsToSetupPose();
                }
                _renderer.enabled = true;
                _skeleton.LateUpdate(); // Build the first mesh before using its bounds for camera framing.
                _cameraObject = new GameObject("Skill Spine Preview Camera")
                    { hideFlags = HideFlags.HideAndDontSave, layer = 30 };
                _camera = _cameraObject.AddComponent<Camera>();
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = FRFrame.Gameplay.Editor.GameplayEditorStyles.PreviewBackground;
                _camera.cullingMask = 1 << 30;
                _camera.orthographic = true;
                _camera.enabled = false;
                _camera.transform.position = new Vector3(0f, 0f, -10f);
                _texture = new RenderTexture(512, 320, 16, RenderTextureFormat.ARGB32)
                    { hideFlags = HideFlags.HideAndDontSave };
                _texture.Create();
                _camera.targetTexture = _texture;
                var bounds = _renderer.bounds;
                _camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
                _camera.orthographicSize = Mathf.Max(.5f, bounds.extents.y * 1.3f,
                    bounds.extents.x * 1.3f / _camera.aspect);
                _renderer.enabled = false;
            }
            catch { Dispose(); throw; }
        }

        public void Seek(global::Spine.Animation animation, float seconds)
        {
            if (_skeleton == null || animation == null) return;
            _skeleton.Skeleton.SetToSetupPose();
            _skeleton.AnimationState.ClearTracks();
            var entry = _skeleton.AnimationState.SetAnimation(0, animation, false);
            entry.TrackTime = Mathf.Clamp(seconds, 0f, animation.Duration);
            entry.TimeScale = 0f;
            _skeleton.Update(0f);
            _renderer.enabled = true;
            try { _skeleton.LateUpdate(); }
            finally { _renderer.enabled = false; }
        }

        public void Render(Rect viewport)
        {
            if (_camera == null || _texture == null) return;
            if (Event.current.type != EventType.Repaint) return;
            _renderer.enabled = true;
            try { _camera.Render(); }
            finally { _renderer.enabled = false; }
            EditorGUI.DrawPreviewTexture(viewport, _texture, null, ScaleMode.ScaleToFit);
        }

        public void Dispose()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (_skeleton != null) UnityEngine.Object.DestroyImmediate(_skeleton.gameObject);
            if (_cameraObject != null) UnityEngine.Object.DestroyImmediate(_cameraObject);
            if (_texture != null) { _texture.Release(); UnityEngine.Object.DestroyImmediate(_texture); }
            _skeleton = null;
            _renderer = null;
            _camera = null;
            _cameraObject = null;
            _texture = null;
        }
    }
}
