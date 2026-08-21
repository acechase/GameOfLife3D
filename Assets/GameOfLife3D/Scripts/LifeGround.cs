using UnityEngine;
using UnityEngine.Rendering;

namespace GameOfLife3D
{
    /// <summary>
    /// A dim reference grid on the floor beneath the volume, with zero wiring.
    ///
    /// This is a navigation aid, not decoration. Orbiting keeps the subject
    /// centred by definition, so against a featureless background nothing in
    /// frame changes except the subject's own silhouette — and the move reads
    /// as the world spinning rather than the camera travelling. Grid lines near
    /// the camera sweep past faster than distant ones, and that parallax is
    /// what resolves the ambiguity. It also gives the volume a stated size:
    /// squares are <see cref="minorSpacing"/> metres.
    ///
    /// Drop it on the same GameObject as <see cref="LifeVolume"/>. Like
    /// <see cref="LifeGlow"/> it draws itself with no scene objects at all —
    /// here via Graphics.RenderMesh, so there is nothing in the Hierarchy and
    /// the scene is never dirtied.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("GameOfLife3D/Life Ground")]
    [DisallowMultipleComponent]
    public class LifeGround : MonoBehaviour
    {
        [Header("Visibility")]
        [Tooltip("Turn OFF for passthrough AR: the real room already supplies parallax and " +
                 "scale, and a grid plane would just paint over the actual floor.")]
        public bool showGrid = true;
        [Range(0f, 1f)] public float opacity = 0.5f;
        [Tooltip("Deliberately dim and below 1.0 — the grid must stay under the bloom " +
                 "threshold so it never competes with the cells.")]
        public Color lineColor = new Color(0.30f, 0.55f, 0.62f);

        [Header("Layout")]
        [Tooltip("Metres between fine lines. This is the grid's statement about scale.")]
        public float minorSpacing = 0.1f;
        [Tooltip("A heavier line every N fine ones.")]
        public int majorEvery = 10;
        [Tooltip("Line half-width in pixels — constant on screen at any distance.")]
        public float lineWidth = 1.1f;
        [Tooltip("Metres from the centre at which the grid fades out entirely.")]
        public float fadeRadius = 3f;
        [Tooltip("Extra drop below the volume's underside, in metres. Only used when " +
                 "Use World Floor is off.")]
        public float clearance = 0.15f;
        [Tooltip("Put the grid at world y = 0 — an actual floor. Off tucks it just under " +
                 "the volume, which is a desktop framing choice: in a headset that lands " +
                 "around thigh height and reads as a sheet of glass rather than the ground.")]
        public bool useWorldFloor = true;

        [Header("Physics")]
        [Tooltip("Invisible floor collider matching the grid, so an XR rig with gravity has " +
                 "something to stand on. Without one the rig falls forever and the volume " +
                 "appears to fly away from you.")]
        public bool physicalFloor = true;
        [Tooltip("Side length of that collider, in metres. Generous on purpose — walking off " +
                 "the edge means falling out of the world.")]
        public float floorSize = 60f;

        Material _material;
        Mesh _quad;
        MaterialPropertyBlock _mpb;
        LifeVolume _volume;
        GameObject _floor;

        void OnEnable()
        {
            _volume = GetComponent<LifeVolume>();
            if (_volume == null) _volume = FindFirstObjectByType<LifeVolume>();

            Shader shader = Resources.Load<Shader>("LifeGround");
            if (shader == null)
            {
                Debug.LogError("LifeGround: LifeGround.shader not found in a Resources folder.", this);
                enabled = false;
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _quad = BuildQuad();
            _mpb = new MaterialPropertyBlock();
            BuildFloor();
        }

        void OnDisable()
        {
            if (_material != null) DestroySafely(_material);
            if (_quad != null) DestroySafely(_quad);
            if (_floor != null) DestroySafely(_floor);
            _material = null; _quad = null; _mpb = null; _floor = null;
        }

        static void DestroySafely(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void LateUpdate()
        {
            Vector3 centre = GroundCentre();
            UpdateFloor(centre);

            if (!showGrid || _material == null || _quad == null) return;

            float span = fadeRadius * 2.2f;   // past the fade, so the rim is never reached

            _mpb.SetColor("_LineColor", lineColor);
            _mpb.SetVector("_GridCenter", centre);
            _mpb.SetFloat("_MinorSpacing", Mathf.Max(minorSpacing, 1e-3f));
            _mpb.SetFloat("_MajorEvery", Mathf.Max(majorEvery, 1));
            _mpb.SetFloat("_LineWidth", Mathf.Max(lineWidth, 0.1f));
            _mpb.SetFloat("_FadeRadius", Mathf.Max(fadeRadius, 1e-3f));
            _mpb.SetFloat("_Opacity", opacity);

            var rp = new RenderParams(_material)
            {
                matProps = _mpb,
                worldBounds = new Bounds(centre, new Vector3(span, 0.01f, span)),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };

            Graphics.RenderMesh(rp, _quad, 0,
                Matrix4x4.TRS(centre, Quaternion.identity, new Vector3(span, 1f, span)));
        }

        /// <summary>
        /// Where the ground sits: at the world floor, or tucked under the
        /// volume's lowest cell.
        /// </summary>
        Vector3 GroundCentre()
        {
            Vector3 p = _volume != null ? _volume.transform.position : transform.position;

            if (useWorldFloor)
            {
                // Centred under the volume, but at the height the player's feet
                // actually are. An XR rig stands at y = 0; a plane tucked under
                // a volume floating at 1.2 m would be above the rig's head-start
                // position, so it could never be stood on.
                p.y = 0f;
                return p;
            }

            if (_volume != null)
            {
                float halfHeight = _volume.gridSize.y * 0.5f * _volume.CellSizeLocal
                                 * Mathf.Abs(_volume.transform.lossyScale.y);
                p.y -= halfHeight;
            }
            p.y -= clearance;
            return p;
        }

        void BuildFloor()
        {
            if (!physicalFloor || _floor != null) return;

            _floor = new GameObject("Life Ground Floor") { layer = 0 };
            _floor.hideFlags = HideFlags.HideAndDontSave;
            var box = _floor.AddComponent<BoxCollider>();
            box.size = new Vector3(floorSize, 0.2f, floorSize);
        }

        /// <summary>Keep the collider's top surface level with the drawn grid.</summary>
        void UpdateFloor(Vector3 centre)
        {
            if (!physicalFloor)
            {
                if (_floor != null) { DestroySafely(_floor); _floor = null; }
                return;
            }

            if (_floor == null) BuildFloor();
            if (_floor == null) return;

            var box = _floor.GetComponent<BoxCollider>();
            if (box != null) box.size = new Vector3(floorSize, 0.2f, floorSize);
            _floor.transform.position = centre - new Vector3(0f, 0.1f, 0f);
        }

        /// <summary>Unit quad in the XZ plane, facing up.</summary>
        static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "LifeGround Quad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
