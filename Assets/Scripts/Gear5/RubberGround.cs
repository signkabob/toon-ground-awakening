using System.Collections.Generic;
using UnityEngine;

namespace ToonGround
{
    /// <summary>
    /// A runtime-generated height-field ground that behaves like a rubber sheet.
    /// Each vertex is a damped spring coupled to its neighbours (a 2D wave equation),
    /// so a push sends ripples outward. Waves only stay lively inside "rubber zones"
    /// (Gear 5 auras); everywhere else the sheet is heavily damped and settles flat.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class RubberGround : MonoBehaviour
    {
        public static RubberGround Instance { get; private set; }

        [Header("Shape (applied on Awake)")]
        public float size = 40f;
        [Range(16, 160)] public int resolution = 100;
        public float checkerSize = 1f;
        public Color colorA = new Color(0.45f, 0.78f, 0.35f);
        public Color colorB = new Color(0.38f, 0.70f, 0.30f);

        [Header("Rubber")]
        [Tooltip("How fast ripples travel, in metres per second.")]
        public float waveSpeed = 7f;
        [Tooltip("Pull of every point back toward flat.")]
        public float stiffness = 6f;
        [Tooltip("Damping inside a rubber zone. Low = long-lasting wobble.")]
        public float rubberDamping = 1.2f;
        [Tooltip("Damping outside every rubber zone. High = normal, solid ground.")]
        public float calmDamping = 14f;
        public float maxDisplacement = 4f;

        [Header("Physics")]
        [Tooltip("Rebuild the mesh collider every N physics steps while the ground is moving.")]
        [Min(1)] public int colliderRefreshSteps = 2;

        struct Zone
        {
            public Vector3 localCenter;
            public float radius;
        }

        readonly List<Zone> zones = new List<Zone>();

        Mesh mesh;
        MeshCollider meshCollider;
        Vector3[] vertices;
        float[] heights;
        float[] velocities;
        float[] damping;
        int n; // vertices per side
        float cell;
        bool awake;
        bool normalsDirty;
        int stepsSinceCollider;

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("More than one RubberGround in the scene; the newest one wins.", this);
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Build()
        {
            n = resolution + 1;
            cell = size / resolution;
            heights = new float[n * n];
            velocities = new float[n * n];
            damping = new float[n * n];
            vertices = new Vector3[n * n];

            var uvs = new Vector2[n * n];
            float half = size * 0.5f;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                float lx = -half + x * cell;
                float lz = -half + z * cell;
                vertices[i] = new Vector3(lx, 0f, lz);
                uvs[i] = new Vector2(lx, lz) / (checkerSize * 2f);
            }

            var triangles = new int[resolution * resolution * 6];
            int t = 0;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * n + x;
                triangles[t++] = i;
                triangles[t++] = i + n;
                triangles[t++] = i + 1;
                triangles[t++] = i + 1;
                triangles[t++] = i + n;
                triangles[t++] = i + n + 1;
            }

            mesh = new Mesh { name = "Rubber Ground" };
            mesh.indexFormat = n * n > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer.sharedMaterial == null)
                meshRenderer.sharedMaterial = CreateCheckerMaterial();

            meshCollider = GetComponent<MeshCollider>();
            meshCollider.convex = false;
            meshCollider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
            meshCollider.sharedMesh = mesh;
        }

        Material CreateCheckerMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "Rubber Checker"
            };
            texture.SetPixels(new[] { colorA, colorB, colorB, colorA });
            texture.Apply();

            var material = new Material(shader) { name = "Rubber Ground" };
            material.mainTexture = texture;
            material.color = Color.white;
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.25f);
            return material;
        }

        /// <summary>Keep the ground rubbery (lightly damped) inside this sphere for the next physics step.</summary>
        public void KeepRubbery(Vector3 worldCenter, float radius)
        {
            zones.Add(new Zone { localCenter = transform.InverseTransformPoint(worldCenter), radius = radius });
        }

        /// <summary>Kick the surface around a point. Positive strength pushes up, negative pushes down (m/s).</summary>
        public void AddImpulse(Vector3 worldPoint, float radius, float strength)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            float reach = radius * 2f;
            ForEachVertexNear(local, reach, (i, distance) =>
            {
                float w = Mathf.Exp(-(distance * distance) / (radius * radius));
                velocities[i] += strength * w;
            });
            awake = true;
        }

        /// <summary>Kick a ring around a point, which sends a circular ripple outward.</summary>
        public void AddRingImpulse(Vector3 worldCenter, float ringRadius, float width, float strength)
        {
            Vector3 local = transform.InverseTransformPoint(worldCenter);
            ForEachVertexNear(local, ringRadius + width * 2f, (i, distance) =>
            {
                float d = (distance - ringRadius) / width;
                velocities[i] += strength * Mathf.Exp(-d * d);
            });
            awake = true;
        }

        /// <summary>World-space height of the surface under a point (flat height outside the sheet).</summary>
        public float SampleHeight(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            local.y = Bilinear(heights, local);
            return transform.TransformPoint(local).y;
        }

        /// <summary>How far the surface is pushed up (+) or down (-) from rest under a point, in world units.</summary>
        public float SampleDisplacement(Vector3 worldPoint)
        {
            return SampleHeight(worldPoint) - transform.position.y;
        }

        /// <summary>Vertical speed of the surface under a point, in world units per second.</summary>
        public float SampleVelocity(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Bilinear(velocities, local) * transform.lossyScale.y;
        }

        /// <summary>
        /// Trampoline: if the body is touching the surface while it moves up, fling the body upward.
        /// Returns true when the body was launched.
        /// </summary>
        public bool TryLaunch(Rigidbody body, float bodyBottomY, float gain, float contactTolerance = 0.35f)
        {
            Vector3 position = body.position;
            if (bodyBottomY - SampleHeight(position) > contactTolerance)
                return false;

            float surfaceSpeed = SampleVelocity(position);
            if (surfaceSpeed < 0.5f)
                return false;

            Vector3 velocity = body.linearVelocity;
            float launch = surfaceSpeed * gain;
            if (velocity.y >= launch)
                return false;

            velocity.y = launch;
            body.linearVelocity = velocity;
            return true;
        }

        void FixedUpdate()
        {
            if (awake)
                Simulate(Time.fixedDeltaTime);
            zones.Clear();
        }

        void LateUpdate()
        {
            if (!normalsDirty)
                return;
            mesh.RecalculateNormals();
            normalsDirty = false;
        }

        void Simulate(float dt)
        {
            // Per-vertex damping: rubbery inside zones, calm outside.
            float half = size * 0.5f;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float lx = -half + x * cell;
                float lz = -half + z * cell;
                float rubber = 0f;
                for (int k = 0; k < zones.Count; k++)
                {
                    Zone zone = zones[k];
                    float dx = lx - zone.localCenter.x;
                    float dz = lz - zone.localCenter.z;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    rubber = Mathf.Max(rubber, 1f - Mathf.SmoothStep(0f, 1f, (distance - zone.radius * 0.8f) / (zone.radius * 0.2f)));
                }
                damping[z * n + x] = Mathf.Lerp(calmDamping, rubberDamping, rubber);
            }

            // Sub-step so the explicit integration stays stable at any wave speed.
            int substeps = Mathf.Max(1, Mathf.CeilToInt(waveSpeed * dt / cell / 0.5f));
            float h = dt / substeps;
            float c2 = waveSpeed * waveSpeed / (cell * cell);

            for (int s = 0; s < substeps; s++)
            {
                for (int z = 1; z < n - 1; z++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = z * n + x;
                    float laplacian = heights[i - 1] + heights[i + 1] + heights[i - n] + heights[i + n] - 4f * heights[i];
                    float acceleration = c2 * laplacian - stiffness * heights[i];
                    velocities[i] = (velocities[i] + acceleration * h) / (1f + damping[i] * h);
                }

                for (int z = 1; z < n - 1; z++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = z * n + x;
                    heights[i] = Mathf.Clamp(heights[i] + velocities[i] * h, -maxDisplacement, maxDisplacement);
                }
            }

            float energy = 0f;
            for (int i = 0; i < heights.Length; i++)
            {
                energy = Mathf.Max(energy, Mathf.Abs(heights[i]) + Mathf.Abs(velocities[i]));
                vertices[i].y = heights[i];
            }

            if (energy < 0.0005f)
            {
                // Settled: snap flat and stop simulating until the next impulse.
                System.Array.Clear(heights, 0, heights.Length);
                System.Array.Clear(velocities, 0, velocities.Length);
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i].y = 0f;
                awake = false;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            normalsDirty = true;

            stepsSinceCollider++;
            if (stepsSinceCollider >= colliderRefreshSteps || !awake)
            {
                stepsSinceCollider = 0;
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
            }
        }

        float Bilinear(float[] field, Vector3 local)
        {
            float half = size * 0.5f;
            float gx = (local.x + half) / cell;
            float gz = (local.z + half) / cell;
            if (gx < 0f || gz < 0f || gx >= n - 1 || gz >= n - 1)
                return 0f;

            int x0 = (int)gx;
            int z0 = (int)gz;
            float tx = gx - x0;
            float tz = gz - z0;
            int i = z0 * n + x0;
            float a = Mathf.Lerp(field[i], field[i + 1], tx);
            float b = Mathf.Lerp(field[i + n], field[i + n + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        void ForEachVertexNear(Vector3 local, float reach, System.Action<int, float> action)
        {
            float half = size * 0.5f;
            int minX = Mathf.Clamp(Mathf.FloorToInt((local.x - reach + half) / cell), 1, n - 2);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((local.x + reach + half) / cell), 1, n - 2);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((local.z - reach + half) / cell), 1, n - 2);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt((local.z + reach + half) / cell), 1, n - 2);

            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                float dx = -half + x * cell - local.x;
                float dz = -half + z * cell - local.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance <= reach)
                    action(z * n + x, distance);
            }
        }
    }
}
