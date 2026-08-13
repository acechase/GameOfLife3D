using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameOfLife3D
{
    /// <summary>
    /// The whole simulation in one component. Drop this on an empty GameObject
    /// and press Play: it loads the compute shader + cell shader from Resources,
    /// builds its own cube mesh and material, seeds the grid, and renders live
    /// cells with a single indirect instanced draw. No prefab wiring required.
    ///
    /// The volume is centered on this GameObject's transform and scaled so its
    /// longest axis spans <see cref="volumeSize"/> meters in local space —
    /// move/rotate/scale the transform freely (including via XR grab).
    /// </summary>
    public class LifeVolume : MonoBehaviour
    {
        [Header("Grid")]
        [Tooltip("Cells per axis. Set z = 1 for a flat, classic-2D slab.")]
        public Vector3Int gridSize = new Vector3Int(48, 48, 48);
        [Tooltip("Wrap edges (torus) instead of treating outside as dead.")]
        public bool wrapEdges = false;
        [Tooltip("Size in local meters of the volume's longest axis.")]
        public float volumeSize = 0.6f;

        [Header("Rules")]
        public RulePreset rule = RulePreset.Pyroclastic;
        [Tooltip("Cell states. 2 = plain binary. Higher gives dead cells a refractory " +
                 "shell: they linger and fade for N-2 generations, can't be reborn while " +
                 "they linger, and don't count as neighbors. This is what makes a 3D rule " +
                 "sustain instead of dying out. <= 0 uses the rule's own default.")]
        public int states = 0;
        [Tooltip("Custom birth counts, e.g. \"5\" or \"13-14\" (Custom preset only).")]
        public string customBirth = "5";
        [Tooltip("Custom survive counts, e.g. \"4,5\" or \"13-26\" (Custom preset only).")]
        public string customSurvive = "4,5";

        [Header("Simulation")]
        [Range(0.5f, 60f)] public float stepsPerSecond = 6f;
        public bool startPaused = false;
        [Tooltip("Random fill probability at seed time. <= 0 uses the rule's recommended density.")]
        public float seedDensity = -1f;
        [Range(0.1f, 1f)] public float seedFillFraction = 0.6f;
        public bool reseedOnRuleChange = true;

        [Header("Look")]
        [Range(0.2f, 1f)] public float cubeScale = 0.72f;
        [ColorUsage(false, true)] public Color colorYoung = new Color(0.4f, 4.0f, 3.2f);  // bright cyan (HDR)
        [ColorUsage(false, true)] public Color colorMid   = new Color(0.1f, 1.2f, 2.2f);  // deep teal-blue
        [ColorUsage(false, true)] public Color colorOld   = new Color(1.6f, 0.3f, 2.4f);  // magenta-violet
        [Tooltip("Age (in generations) at which a cell reaches the mid color.")]
        public float ageMidpoint = 6f;
        [Tooltip("Idle rotation, degrees/second around Y. 0 = off.")]
        public float idleSpin = 0f;
        [Tooltip("How brightly a just-dead cell still glows (multi-state rules only). " +
                 "Below ~0.5 the trails drop under the bloom threshold and read as " +
                 "dim ghosts behind the living front.")]
        [Range(0f, 1f)] public float trailBrightness = 0.35f;
        [Tooltip("How far a fully-faded corpse shrinks, so trails taper away.")]
        [Range(0.1f, 1f)] public float trailScale = 0.45f;

        [Header("Debug")]
        public bool showStats = true;

        public bool Paused { get; set; }
        /// <summary>
        /// Cells currently drawn. Under a multi-state rule this counts fading
        /// corpses as well as living cells, because that is exactly the set
        /// the compact kernel appends.
        /// </summary>
        public int Population { get; private set; }
        public string RuleName => rule == RulePreset.Custom ? $"B{customBirth}/S{customSurvive}" : rule.ToString();

        const int kThreads = 4;      // must match THREADS in LifeCompute.compute
        const uint kMaxSeed = 1000000;

        ComputeShader _cs;
        int _kStep, _kSeed, _kClear, _kPaint, _kCompact;
        GraphicsBuffer _gridA, _gridB, _liveCells, _args;
        bool _aIsCurrent = true;
        Material _material;
        Mesh _cubeMesh;
        MaterialPropertyBlock _mpb;
        int _birthMask, _surviveMask;
        uint _seed = 1;
        float _accum;
        int _cellCount;
        int _framesSinceStat;

        GraphicsBuffer Current => _aIsCurrent ? _gridA : _gridB;
        GraphicsBuffer Next    => _aIsCurrent ? _gridB : _gridA;

        // ------------------------------------------------------------------ setup

        void OnEnable()
        {
            gridSize = Vector3Int.Max(gridSize, Vector3Int.one);
            _cellCount = gridSize.x * gridSize.y * gridSize.z;

            _cs = Resources.Load<ComputeShader>("LifeCompute");
            var shader = Resources.Load<Shader>("LifeCell");
            if (_cs == null || shader == null)
            {
                Debug.LogError("GameOfLife3D: LifeCompute.compute / LifeCell.shader not found in a Resources folder.");
                enabled = false;
                return;
            }

            _kStep = _cs.FindKernel("CSStep");
            _kSeed = _cs.FindKernel("CSSeed");
            _kClear = _cs.FindKernel("CSClear");
            _kPaint = _cs.FindKernel("CSPaint");
            _kCompact = _cs.FindKernel("CSCompact");

            _gridA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _cellCount, sizeof(uint));
            _gridB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _cellCount, sizeof(uint));
            _liveCells = new GraphicsBuffer(GraphicsBuffer.Target.Append, _cellCount, sizeof(uint) * 2);

            _cubeMesh = BuildCubeMesh();
            _args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var args = new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = _cubeMesh.GetIndexCount(0),
                instanceCount = 0,
            };
            _args.SetData(new[] { args });

            _material = new Material(shader);
            _mpb = new MaterialPropertyBlock();

            Paused = startPaused;
            ApplyRule();
            _seed = (uint)UnityEngine.Random.Range(1, (int)kMaxSeed);
            Reseed(_seed);
        }

        void OnDisable()
        {
            _gridA?.Release(); _gridA = null;
            _gridB?.Release(); _gridB = null;
            _liveCells?.Release(); _liveCells = null;
            _args?.Release(); _args = null;
            if (_material != null) Destroy(_material);
            if (_cubeMesh != null) Destroy(_cubeMesh);
        }

        void SetGridUniforms()
        {
            _cs.SetInt("_SizeX", gridSize.x);
            _cs.SetInt("_SizeY", gridSize.y);
            _cs.SetInt("_SizeZ", gridSize.z);
            _cs.SetInt("_Wrap", wrapEdges ? 1 : 0);
            _cs.SetInt("_BirthMask", _birthMask);
            _cs.SetInt("_SurviveMask", _surviveMask);
            _cs.SetInt("_States", EffectiveStates);
        }

        void DispatchFull(int kernel)
        {
            _cs.Dispatch(kernel,
                (gridSize.x + kThreads - 1) / kThreads,
                (gridSize.y + kThreads - 1) / kThreads,
                (gridSize.z + kThreads - 1) / kThreads);
        }

        // ------------------------------------------------------------------ public API

        public void ApplyRule()
        {
            if (rule == RulePreset.Custom)
            {
                _birthMask = LifeRules.ParseMask(customBirth);
                _surviveMask = LifeRules.ParseMask(customSurvive);
            }
            else
            {
                LifeRules.GetMasks(rule, out _birthMask, out _surviveMask);
            }
        }

        public void CycleRule(int direction)
        {
            int count = Enum.GetValues(typeof(RulePreset)).Length - 1; // skip Custom when cycling
            rule = (RulePreset)(((int)rule + direction + count) % count);
            ApplyRule();
            if (reseedOnRuleChange) Reseed();
        }

        /// <summary>Reseed with a fresh random seed.</summary>
        public void Reseed() => Reseed((uint)UnityEngine.Random.Range(1, (int)kMaxSeed));

        public void Reseed(uint seed)
        {
            if (_cs == null) return;
            _seed = seed;
            SetGridUniforms();
            float density = seedDensity > 0f ? seedDensity : LifeRules.DefaultDensity(rule);
            Vector3 center = (Vector3)gridSize * 0.5f;
            Vector3 half = (Vector3)gridSize * (0.5f * seedFillFraction);
            Vector3Int lo = Vector3Int.FloorToInt(center - half);
            Vector3Int hi = Vector3Int.CeilToInt(center + half);
            _cs.SetInt("_Seed", (int)_seed);
            _cs.SetFloat("_Density", density);
            _cs.SetInt("_SeedMinX", lo.x); _cs.SetInt("_SeedMinY", lo.y); _cs.SetInt("_SeedMinZ", lo.z);
            _cs.SetInt("_SeedMaxX", hi.x); _cs.SetInt("_SeedMaxY", hi.y); _cs.SetInt("_SeedMaxZ", hi.z);
            _cs.SetBuffer(_kSeed, "_GridOut", Current);
            DispatchFull(_kSeed);
            _accum = 0f;
            Recompact();
        }

        /// <summary>
        /// Clear the grid and stamp a known pattern into the middle of it,
        /// switching to the rule the pattern is defined under and flattening
        /// the grid to a single layer if the pattern needs one.
        ///
        /// Random soup is the wrong tool for seeing structure travel: the rules
        /// that sustain a soup (Pyroclastic, Coral) have no spaceships, and the
        /// rules that have spaceships die from soup. Placing a pattern by hand
        /// is how you get a traveler.
        /// </summary>
        public void StampPattern(int index) => StampPattern(LifePatterns.Get(index));

        public void StampPattern(in LifePattern pattern)
        {
            if (_cs == null) return;

            rule = pattern.rule;
            wrapEdges = pattern.wrap;

            // A 2D pattern only behaves as designed in a single-layer grid: in
            // a 3D grid the 26-neighbor count is a completely different rule.
            if (pattern.flat && gridSize.z != 1)
                Reshape(new Vector3Int(gridSize.x, gridSize.y, 1));
            else if (!pattern.flat && gridSize.z == 1)
                Reshape(new Vector3Int(gridSize.x, gridSize.y, gridSize.x));

            ApplyRule();

            Vector3Int extent = LifePatterns.Extent(pattern);
            Vector3Int origin = (gridSize - extent) / 2;

            var data = new uint[_cellCount];
            uint alive = (uint)(EffectiveStates - 1) << 8 | 1u;   // (state << 8) | age
            foreach (Vector3Int c in pattern.cells)
            {
                Vector3Int p = origin + c;
                if (p.x < 0 || p.y < 0 || p.z < 0 ||
                    p.x >= gridSize.x || p.y >= gridSize.y || p.z >= gridSize.z) continue;
                data[p.x + p.y * gridSize.x + p.z * gridSize.x * gridSize.y] = alive;
            }

            Current.SetData(data);
            _accum = 0f;
            Recompact();

            if (showStats)
                Debug.Log($"GameOfLife3D: stamped \"{pattern.name}\" — {pattern.note}");
        }

        /// <summary>
        /// Resize the grid at runtime. The GPU buffers are sized from gridSize
        /// at OnEnable, so changing it needs a full reallocation rather than
        /// just assigning the field.
        /// </summary>
        public void Reshape(Vector3Int newSize)
        {
            newSize = Vector3Int.Max(newSize, Vector3Int.one);
            if (newSize == gridSize && _gridA != null) return;

            gridSize = newSize;
            _cellCount = gridSize.x * gridSize.y * gridSize.z;

            _gridA?.Release();
            _gridB?.Release();
            _liveCells?.Release();
            _gridA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _cellCount, sizeof(uint));
            _gridB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _cellCount, sizeof(uint));
            _liveCells = new GraphicsBuffer(GraphicsBuffer.Target.Append, _cellCount, sizeof(uint) * 2);
            _aIsCurrent = true;
        }

        public void ClearAll()
        {
            if (_cs == null) return;
            SetGridUniforms();
            _cs.SetBuffer(_kClear, "_GridOut", Current);
            DispatchFull(_kClear);
            Recompact();
        }

        public void StepOnce()
        {
            if (_cs == null) return;
            DoStep();
        }

        /// <summary>Spawn (or erase) a noisy sphere of cells at a world position.</summary>
        public void PaintSphere(Vector3 worldPos, float worldRadius, bool erase = false)
        {
            if (_cs == null) return;
            float cellLocal = CellSizeLocal;
            Vector3 local = transform.InverseTransformPoint(worldPos);
            Vector3 cell = local / cellLocal + (Vector3)gridSize * 0.5f;
            float meanScale = (transform.lossyScale.x + transform.lossyScale.y + transform.lossyScale.z) / 3f;
            float cellRadius = worldRadius / Mathf.Max(cellLocal * meanScale, 1e-6f);

            SetGridUniforms();
            _cs.SetInt("_Seed", (int)(_seed + (uint)Time.frameCount)); // vary paint noise
            _cs.SetVector("_PaintCenter", cell);
            _cs.SetFloat("_PaintRadius", Mathf.Max(cellRadius, 1f));
            _cs.SetInt("_PaintValue", erase ? 0 : 1);
            _cs.SetBuffer(_kPaint, "_GridOut", Current);
            DispatchFull(_kPaint);
            Recompact();
        }

        /// <summary>True if a world-space point lies inside the volume's bounds.</summary>
        public bool ContainsWorldPoint(Vector3 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            Vector3 half = (Vector3)gridSize * (0.5f * CellSizeLocal);
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        public float CellSizeLocal => volumeSize / Mathf.Max(gridSize.x, Mathf.Max(gridSize.y, gridSize.z));

        /// <summary>
        /// State count actually in force: the inspector override when set,
        /// otherwise the rule's measured default. Clamped to 2 (binary) at the
        /// low end and 64 at the high end — the packed cell encoding is
        /// (state &lt;&lt; 8) | age, so the state has plenty of headroom but the
        /// decay shell stops being visually useful long before that.
        /// </summary>
        public int EffectiveStates =>
            Mathf.Clamp(states > 0 ? states : LifeRules.DefaultStates(rule), 2, 64);

        // ------------------------------------------------------------------ loop

        void Update()
        {
            if (_cs == null) return;

            if (idleSpin != 0f)
                transform.Rotate(0f, idleSpin * Time.deltaTime, 0f, Space.World);

            if (!Paused && stepsPerSecond > 0f)
            {
                _accum += Time.deltaTime;
                float interval = 1f / stepsPerSecond;
                int guard = 0;
                while (_accum >= interval && guard++ < 4)
                {
                    _accum -= interval;
                    DoStep();
                }
                if (_accum >= interval) _accum = 0f; // dropped behind; don't spiral
            }

            Render();
            UpdateStats();
        }

        void DoStep()
        {
            SetGridUniforms();
            _cs.SetBuffer(_kStep, "_GridIn", Current);
            _cs.SetBuffer(_kStep, "_GridOut", Next);
            DispatchFull(_kStep);
            _aIsCurrent = !_aIsCurrent;
            Recompact();
        }

        void Recompact()
        {
            _liveCells.SetCounterValue(0);
            SetGridUniforms();
            _cs.SetBuffer(_kCompact, "_GridIn", Current);
            _cs.SetBuffer(_kCompact, "_LiveCells", _liveCells);
            DispatchFull(_kCompact);
            GraphicsBuffer.CopyCount(_liveCells, _args, sizeof(uint)); // instanceCount lives at byte offset 4
        }

        void Render()
        {
            float cellLocal = CellSizeLocal;
            _mpb.SetBuffer("_LiveCells", _liveCells);
            _mpb.SetMatrix("_ObjectToWorld", transform.localToWorldMatrix);
            _mpb.SetVector("_GridDims", new Vector4(gridSize.x, gridSize.y, gridSize.z, 0));
            _mpb.SetFloat("_CellSizeLocal", cellLocal);
            _mpb.SetFloat("_CubeScale", cubeScale);
            _mpb.SetColor("_ColorYoung", colorYoung);
            _mpb.SetColor("_ColorMid", colorMid);
            _mpb.SetColor("_ColorOld", colorOld);
            _mpb.SetFloat("_AgeMidpoint", Mathf.Max(ageMidpoint, 1f));
            _mpb.SetFloat("_States", EffectiveStates);
            _mpb.SetFloat("_TrailBrightness", trailBrightness);
            _mpb.SetFloat("_TrailScale", trailScale);
            float phase = (!Paused && stepsPerSecond > 0f) ? Mathf.Clamp01(_accum * stepsPerSecond) : 1f;
            _mpb.SetFloat("_StepPhase", phase);

            float worldExtent = volumeSize * Mathf.Max(transform.lossyScale.x,
                Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
            var bounds = new Bounds(transform.position, Vector3.one * (worldExtent * 2f + 1f));

            var rp = new RenderParams(_material)
            {
                worldBounds = bounds,
                matProps = _mpb,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
            };
            Graphics.RenderMeshIndirect(rp, _cubeMesh, _args);
        }

        void UpdateStats()
        {
            if (!showStats) return;
            if (++_framesSinceStat < 30) return;
            _framesSinceStat = 0;
            AsyncGPUReadback.Request(_args, request =>
            {
                if (!request.hasError && _args != null)
                    Population = (int)request.GetData<uint>()[1];
            });
        }

        /// <summary>
        /// Adds/fits a trigger BoxCollider matching the volume, for use with
        /// XRGrabInteractable (grab, rotate and rescale the colony in XR).
        /// </summary>
        [ContextMenu("Fit Box Collider For XR Grab")]
        public void FitBoxCollider()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            var size = Vector3Int.Max(gridSize, Vector3Int.one);
            box.center = Vector3.zero;
            box.size = (Vector3)size * CellSizeLocal;
            box.isTrigger = true;
        }

        void OnValidate()
        {
            if (Application.isPlaying && _cs != null) ApplyRule();
        }

        void OnGUI()
        {
            if (!showStats) return;
            string status = Paused ? "PAUSED" : $"{stepsPerSecond:0.#} steps/s";
            GUI.Label(new Rect(12, 12, 640, 24),
                $"GameOfLife3D  |  {RuleName}  |  {gridSize.x}x{gridSize.y}x{gridSize.z}  |  " +
                $"{(EffectiveStates > 2 ? $"{EffectiveStates} states  |  " : "")}" +
                $"drawn {Population:n0}  |  {status}");
        }

        // ------------------------------------------------------------------ mesh

        static Mesh BuildCubeMesh()
        {
            var mesh = new Mesh { name = "LifeCell Cube" };
            Vector3[] n = { Vector3.back, Vector3.forward, Vector3.left, Vector3.right, Vector3.down, Vector3.up };
            var verts = new Vector3[24];
            var norms = new Vector3[24];
            var tris = new int[36];
            for (int f = 0; f < 6; f++)
            {
                Vector3 normal = n[f];
                Vector3 u = new Vector3(normal.y, normal.z, normal.x); // any perpendicular
                Vector3 v = Vector3.Cross(normal, u);
                int b = f * 4;
                verts[b + 0] = (normal - u - v) * 0.5f;
                verts[b + 1] = (normal + u - v) * 0.5f;
                verts[b + 2] = (normal + u + v) * 0.5f;
                verts[b + 3] = (normal - u + v) * 0.5f;
                for (int i = 0; i < 4; i++) norms[b + i] = normal;
                int t = f * 6;
                tris[t + 0] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
            }
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            return mesh;
        }
    }
}
