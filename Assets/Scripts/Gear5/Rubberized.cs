using UnityEngine;

namespace ToonGround
{
    /// <summary>
    /// Added at runtime by <see cref="Gear5Aura"/> to anything inside its radius.
    /// Rigidbodies get a bouncy physics material and ride the ground like a trampoline;
    /// static objects bob on the ripples. Everything squashes and stretches.
    /// Softened things turn marshmallow cream with a gold jelly ripple running through them.
    /// Once nothing has touched it for a moment it eases back and removes itself.
    /// </summary>
    [DisallowMultipleComponent]
    public class Rubberized : MonoBehaviour
    {
        const float Linger = 0.35f;
        const float LaunchGain = 1.3f;
        const float StretchSpring = 90f;
        const float StretchDamping = 7f;

        static readonly Color Cream = new Color(1f, 0.95f, 0.82f);
        static readonly Color Gold = new Color(1f, 0.76f, 0.2f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static PhysicsMaterial bouncyMaterial;

        /// <summary>True while the object is inside a Soften radius (or just left it).</summary>
        public bool IsSoft => initialized && Active;

        /// <summary>How softened the object looks and feels right now, 0..1 (eases in and out).</summary>
        public float Blend => blend;

        /// <summary>Is this object (or the rigidbody/object it belongs to) softened right now?</summary>
        public static bool IsSoftened(GameObject target)
        {
            if (target == null)
                return false;
            var rubber = target.GetComponentInParent<Rubberized>();
            return rubber != null && rubber.IsSoft;
        }

        float lastTouch = float.NegativeInfinity;
        bool initialized;
        float blend;
        float stretch;
        float stretchVelocity;
        Vector3 baseScale;
        float baseY;
        Rigidbody body;
        Collider[] colliders;
        PhysicsMaterial[] originalMaterials;
        bool customVisuals;
        Renderer[] renderers;
        Color[] baseColors;
        MaterialPropertyBlock block;

        /// <summary>Keeps this object rubbery for a little longer.</summary>
        public void Touch()
        {
            lastTouch = Time.time;
            if (!initialized)
                Initialize();
        }

        void Initialize()
        {
            initialized = true;
            baseScale = transform.localScale;
            baseY = transform.position.y;
            body = GetComponent<Rigidbody>();
            // Soft walls and launch ramps animate themselves: no bobbing or whole-object squash.
            customVisuals = GetComponent<SoftWall>() != null || GetComponent<LaunchRamp>() != null;

            renderers = GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;
                baseColors[i] = material != null && (material.HasProperty(BaseColorId) || material.HasProperty(ColorId)) ? material.color : Color.white;
            }
            block = new MaterialPropertyBlock();

            colliders = GetComponentsInChildren<Collider>();
            originalMaterials = new PhysicsMaterial[colliders.Length];
            if (body != null)
            {
                if (bouncyMaterial == null)
                {
                    bouncyMaterial = new PhysicsMaterial("Gear 5 Rubber")
                    {
                        bounciness = 0.85f,
                        bounceCombine = PhysicsMaterialCombine.Maximum,
                        dynamicFriction = 0.4f,
                        staticFriction = 0.4f
                    };
                }
                for (int i = 0; i < colliders.Length; i++)
                {
                    originalMaterials[i] = colliders[i].sharedMaterial;
                    colliders[i].sharedMaterial = bouncyMaterial;
                }
            }
        }

        bool Active => Time.time - lastTouch < Linger;

        void FixedUpdate()
        {
            if (!initialized)
                return;

            float dt = Time.fixedDeltaTime;
            blend = Mathf.MoveTowards(blend, Active ? 1f : 0f, dt * 3f);

            var ground = RubberGround.Instance;
            float surfaceSpeed = ground != null ? ground.SampleVelocity(transform.position) : 0f;

            if (Active && ground != null && body != null && !body.isKinematic)
            {
                if (ground.TryLaunch(body, BottomY(), LaunchGain))
                    stretchVelocity += 6f;
            }

            // Squash/stretch spring, pushed around by the ground and by falling speed.
            float target = Mathf.Clamp(surfaceSpeed * 0.06f, -0.35f, 0.45f);
            if (body != null)
                target += Mathf.Clamp(Mathf.Abs(body.linearVelocity.y) * 0.02f, 0f, 0.25f);
            target *= blend;

            stretchVelocity += (target - stretch) * StretchSpring * dt;
            stretchVelocity /= 1f + StretchDamping * dt;
            stretch = Mathf.Clamp(stretch + stretchVelocity * dt, -0.6f, 0.8f);

            if (!Active && blend <= 0f && Mathf.Abs(stretch) < 0.002f && Mathf.Abs(stretchVelocity) < 0.01f)
                Restore();
        }

        void LateUpdate()
        {
            if (!initialized)
                return;

            Tint();
            if (customVisuals)
                return;

            float squash = 1f / Mathf.Sqrt(1f + stretch);
            transform.localScale = Vector3.Scale(baseScale, new Vector3(squash, 1f + stretch, squash));

            // Static things bob with the ripples; rigidbodies are moved by physics instead.
            var ground = RubberGround.Instance;
            if (body == null && ground != null)
            {
                var position = transform.position;
                position.y = baseY + ground.SampleDisplacement(position) * blend;
                transform.position = position;
            }
        }

        void Tint()
        {
            // Marshmallow cream, with a gold jelly band sweeping up through the object.
            float ripple = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(Time.time * 7f - transform.position.y * 2.5f), 3f);
            Color soft = Color.Lerp(Cream, Gold, ripple * 0.65f);
            for (int i = 0; i < renderers.Length; i++)
            {
                // Leave very dark details (eyes, chevrons) alone so they still read.
                if (renderers[i] == null || baseColors[i].maxColorComponent < 0.5f)
                    continue;
                Color tinted = Color.Lerp(baseColors[i], soft, blend * 0.85f);
                renderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, tinted);
                block.SetColor(ColorId, tinted);
                renderers[i].SetPropertyBlock(block);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (blend > 0f)
                stretchVelocity -= Mathf.Min(collision.relativeVelocity.magnitude, 12f) * 0.6f * blend;
        }

        float BottomY()
        {
            float bottom = float.PositiveInfinity;
            foreach (var c in colliders)
                if (c != null && c.enabled)
                    bottom = Mathf.Min(bottom, c.bounds.min.y);
            return float.IsPositiveInfinity(bottom) ? transform.position.y : bottom;
        }

        void Restore()
        {
            foreach (var r in renderers)
                if (r != null)
                    r.SetPropertyBlock(null);
            if (!customVisuals)
                transform.localScale = baseScale;
            if (body == null && !customVisuals)
            {
                var position = transform.position;
                position.y = baseY;
                transform.position = position;
            }
            else
            {
                for (int i = 0; i < colliders.Length; i++)
                    if (colliders[i] != null)
                        colliders[i].sharedMaterial = originalMaterials[i];
            }
            Destroy(this);
        }
    }
}
