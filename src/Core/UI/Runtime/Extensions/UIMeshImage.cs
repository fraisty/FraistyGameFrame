using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    public enum UIMeshRaycastMode
    {
        None,
        Rect,
        Triangles
    }

    public enum UIMeshFitMode
    {
        None,
        Stretch,
        Fit,
        Fill
    }

    public sealed class UIMeshImage : MaskableGraphic
    {
        [SerializeField] private Texture _texture;
        [SerializeField] private Mesh _mesh;
        [SerializeField] private UIMeshRaycastMode _raycastMode = UIMeshRaycastMode.Rect;
        [SerializeField] private UIMeshFitMode _fitMode = UIMeshFitMode.Stretch;

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();
        private readonly List<int> _submeshTriangles = new List<int>();
        private readonly List<Vector2> _mappedVertices = new List<Vector2>();
        private readonly List<Vector2> _mappedUvs = new List<Vector2>();
        private readonly List<int> _validTriangles = new List<int>();
        private bool _geometryDirty = true;
        private Rect _cachedRect;

        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

        public Mesh Mesh
        {
            get => _mesh;
            set
            {
                if (_mesh == value)
                    return;

                _mesh = value;
                InvalidateGeometry();
            }
        }

        public UIMeshRaycastMode RaycastMode
        {
            get => _raycastMode;
            set
            {
                if (_raycastMode == value)
                    return;

                _raycastMode = value;
                SetVerticesDirty();
            }
        }

        public UIMeshFitMode FitMode
        {
            get => _fitMode;
            set
            {
                if (_fitMode == value)
                    return;

                _fitMode = value;
                InvalidateGeometry();
            }
        }

        public void SetTexture(Texture texture)
        {
            if (_texture == texture)
                return;

            _texture = texture;
            SetMaterialDirty();
        }

        public void SetMesh(Mesh mesh)
        {
            Mesh = mesh;
        }

        public void RefreshMesh()
        {
            InvalidateGeometry();
        }

        public void SetVertices(IList<Vector3> vertices, IList<Vector2> uvs, IList<int> triangles)
        {
            SetGeometry(vertices, uvs, triangles);
        }

        public void SetGeometry(IList<Vector3> vertices, IList<Vector2> uvs, IList<int> triangles)
        {
            _mesh = null;
            _vertices.Clear();
            _uvs.Clear();
            _triangles.Clear();
            if (vertices != null)
                _vertices.AddRange(vertices);
            if (uvs != null)
                _uvs.AddRange(uvs);
            if (triangles != null)
                _triangles.AddRange(triangles);
            InvalidateGeometry();
        }

        public void ClearMesh()
        {
            _mesh = null;
            _vertices.Clear();
            _uvs.Clear();
            _triangles.Clear();
            InvalidateGeometry();
        }

        public override bool Raycast(Vector2 sp, Camera eventCamera)
        {
            if (_raycastMode == UIMeshRaycastMode.None)
                return false;
            if (_raycastMode == UIMeshRaycastMode.Rect)
                return base.Raycast(sp, eventCamera);
            if (!base.Raycast(sp, eventCamera))
                return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, eventCamera, out Vector2 local))
                return false;

            EnsureGeometry();
            for (int i = 0; i < _validTriangles.Count; i += 3)
            {
                Vector2 a = _mappedVertices[_validTriangles[i]];
                Vector2 b = _mappedVertices[_validTriangles[i + 1]];
                Vector2 c = _mappedVertices[_validTriangles[i + 2]];
                if (ContainsPoint(local, a, b, c))
                    return true;
            }
            return false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            EnsureGeometry();
            if (_mappedVertices.Count == 0 || _validTriangles.Count == 0)
            {
                AddDefaultQuad(vh);
                return;
            }

            for (int i = 0; i < _mappedVertices.Count; i++)
            {
                UIVertex vertex = UIVertex.simpleVert;
                vertex.position = _mappedVertices[i];
                vertex.color = color;
                vertex.uv0 = _mappedUvs[i];
                vh.AddVert(vertex);
            }

            for (int i = 0; i < _validTriangles.Count; i += 3)
                vh.AddTriangle(_validTriangles[i], _validTriangles[i + 1], _validTriangles[i + 2]);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            InvalidateGeometry();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            InvalidateGeometry();
            SetMaterialDirty();
        }
#endif

        private void InvalidateGeometry()
        {
            _geometryDirty = true;
            SetVerticesDirty();
        }

        private void EnsureGeometry()
        {
            Rect rect = GetPixelAdjustedRect();
            if (!_geometryDirty && rect == _cachedRect)
                return;

            _geometryDirty = false;
            _cachedRect = rect;
            _mappedVertices.Clear();
            _mappedUvs.Clear();
            _validTriangles.Clear();

            if (_mesh != null)
            {
                _vertices.Clear();
                _uvs.Clear();
                _triangles.Clear();
                _mesh.GetVertices(_vertices);
                _mesh.GetUVs(0, _uvs);
                for (int submesh = 0; submesh < _mesh.subMeshCount; submesh++)
                {
                    _submeshTriangles.Clear();
                    _mesh.GetTriangles(_submeshTriangles, submesh);
                    _triangles.AddRange(_submeshTriangles);
                }
            }

            if (_vertices.Count == 0 || _triangles.Count < 3)
                return;

            Bounds bounds = new Bounds(_vertices[0], Vector3.zero);
            for (int i = 1; i < _vertices.Count; i++)
                bounds.Encapsulate(_vertices[i]);

            for (int i = 0; i < _vertices.Count; i++)
            {
                _mappedVertices.Add(MapVertex(_vertices[i], bounds, rect));
                _mappedUvs.Add(i < _uvs.Count ? _uvs[i] : Vector2.zero);
            }

            for (int i = 0; i + 2 < _triangles.Count; i += 3)
            {
                int a = _triangles[i];
                int b = _triangles[i + 1];
                int c = _triangles[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= _mappedVertices.Count || b >= _mappedVertices.Count || c >= _mappedVertices.Count)
                    continue;
                if (Mathf.Abs(Cross(_mappedVertices[b] - _mappedVertices[a], _mappedVertices[c] - _mappedVertices[a])) <= Mathf.Epsilon)
                    continue;
                _validTriangles.Add(a);
                _validTriangles.Add(b);
                _validTriangles.Add(c);
            }
        }

        private Vector2 MapVertex(Vector3 vertex, Bounds bounds, Rect rect)
        {
            if (_fitMode == UIMeshFitMode.None)
                return new Vector2(vertex.x, vertex.y);

            float width = bounds.size.x;
            float height = bounds.size.y;
            float normalizedX = width > Mathf.Epsilon ? (vertex.x - bounds.min.x) / width - 0.5f : 0f;
            float normalizedY = height > Mathf.Epsilon ? (vertex.y - bounds.min.y) / height - 0.5f : 0f;
            float scaleX = rect.width;
            float scaleY = rect.height;

            if (_fitMode != UIMeshFitMode.Stretch)
            {
                float sourceWidth = width > Mathf.Epsilon ? width : 1f;
                float sourceHeight = height > Mathf.Epsilon ? height : 1f;
                float scale = _fitMode == UIMeshFitMode.Fit
                    ? Mathf.Min(rect.width / sourceWidth, rect.height / sourceHeight)
                    : Mathf.Max(rect.width / sourceWidth, rect.height / sourceHeight);
                scaleX = width * scale;
                scaleY = height * scale;
            }

            return new Vector2(rect.center.x + normalizedX * scaleX, rect.center.y + normalizedY * scaleY);
        }

        private static bool ContainsPoint(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross(b - a, point - a);
            float bc = Cross(c - b, point - b);
            float ca = Cross(a - c, point - c);
            return ab >= 0f && bc >= 0f && ca >= 0f || ab <= 0f && bc <= 0f && ca <= 0f;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private void AddDefaultQuad(VertexHelper vh)
        {
            Rect rect = GetPixelAdjustedRect();
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = new Vector3(rect.xMin, rect.yMin);
            vertex.uv0 = new Vector2(0f, 0f);
            vh.AddVert(vertex);
            vertex.position = new Vector3(rect.xMin, rect.yMax);
            vertex.uv0 = new Vector2(0f, 1f);
            vh.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMax);
            vertex.uv0 = new Vector2(1f, 1f);
            vh.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMin);
            vertex.uv0 = new Vector2(1f, 0f);
            vh.AddVert(vertex);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
