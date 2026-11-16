using UnityEngine;

namespace ToonGround
{
    /// <summary>
    /// A simple blob enemy that waddles toward the player and shoves them on contact.
    /// Hit it while it is softened and it turns into a pinball: it shoots off, ricochets off
    /// walls without losing speed and knocks other enemies along the way.
    /// Hit it while it is normal and it only gets knocked back a little.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class PinballEnemy : MonoBehaviour
    {
        [Header("Walk")]
        public float chaseRange = 10f;
        public float walkSpeed = 1.6f;
        public float shoveSpeed = 5f;

        [Header("Hits")]
        [Tooltip("Knockback speed when hit while normal (m/s).")]
        public float knockbackSpeed = 5f;
        public float stunTime = 0.8f;

        [Header("Pinball (softened)")]
        [Tooltip("Speed a softened enemy shoots off at when hit (m/s).")]
        public float pinballSpeed = 16f;
        [Tooltip("How long it keeps ricocheting before it settles down.")]
        public float pinballTime = 3f;
        [Tooltip("Speed kept on each wall ricochet.")]
        [Range(0.5f, 1.2f)] public float ricochetKeep = 0.97f;

        Rigidbody body;
        Transform player;
        float stunTimer;
        float pinballTimer;
        float pinballStartSpeed;
        Vector3 lastVelocity;
        float lastShove;
        float spin;

        public bool IsPinball => pinballTimer > 0f;
        public bool IsSoft => Rubberized.IsSoftened(gameObject);

        public static PinballEnemy Create(string name, Vector3 position, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.8f;
            go.GetComponent<Renderer>().material.color = color;

            for (int side = -1; side <= 1; side += 2)
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye";
                Object.DestroyImmediate(eye.GetComponent<Collider>());
                eye.transform.SetParent(go.transform, false);
                eye.transform.localPosition = new Vector3(side * 0.18f, 0.15f, 0.42f);
                eye.transform.localScale = Vector3.one * 0.18f;
                eye.GetComponent<Renderer>().material.color = Color.black;
            }

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.5f;
            return go.AddComponent<PinballEnemy>();
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void Start()
        {
            var p = FindAnyObjectByType<Gear5Player>();
            if (p != null)
                player = p.transform;
        }

        /// <summary>Punch, stomp or dash hit. <paramref name="direction"/> is where it should fly.</summary>
        public void Hit(Vector3 direction, float strength = 1f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : transform.forward;

            if (IsSoft)
            {
                StartPinball(flat * pinballSpeed * strength + Vector3.up * 2f);
            }
            else
            {
                body.linearVelocity = flat * knockbackSpeed * strength + Vector3.up * 3f;
                stunTimer = stunTime;
            }
        }

        /// <summary>Thrown by something else (a launch ramp).</summary>
        public void Launch(Vector3 velocity, bool asPinball)
        {
            if (asPinball)
                StartPinball(velocity);
            else
            {
                body.linearVelocity = velocity;
                stunTimer = stunTime;
            }
        }

        void StartPinball(Vector3 velocity)
        {
            body.linearVelocity = velocity;
            lastVelocity = velocity;
            pinballTimer = pinballTime;
            pinballStartSpeed = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
            stunTimer = 0f;
            ToonFollowCamera.Shake(0.1f);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (pinballTimer > 0f)
            {
                pinballTimer -= dt;
                // Keep it zipping: hold horizontal speed, easing down only near the end.
                Vector3 v = body.linearVelocity;
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                float want = pinballStartSpeed * Mathf.Clamp01(pinballTimer / (pinballTime * 0.4f));
                if (flat.sqrMagnitude > 0.01f && flat.magnitude < want)
                {
                    flat = flat.normalized * want;
                    body.linearVelocity = new Vector3(flat.x, v.y, flat.z);
                }
                spin += dt * 1080f;
                body.MoveRotation(Quaternion.Euler(0f, spin, 0f));
            }
            else if (stunTimer > 0f)
            {
                stunTimer -= dt;
            }
            else if (player != null)
            {
                Vector3 toPlayer = player.position - body.position;
                toPlayer.y = 0f;
                Vector3 v = body.linearVelocity;
                Vector3 target = Vector3.zero;
                if (toPlayer.magnitude < chaseRange && toPlayer.magnitude > 0.6f)
                {
                    target = toPlayer.normalized * walkSpeed;
                    body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(toPlayer.normalized, Vector3.up), dt * 6f));
                }
                Vector3 flat = Vector3.MoveTowards(new Vector3(v.x, 0f, v.z), target, 10f * dt);
                body.linearVelocity = new Vector3(flat.x, v.y, flat.z);
            }

            lastVelocity = body.linearVelocity;
        }

        void OnCollisionEnter(Collision collision)
        {
            Vector3 normal = collision.GetContact(0).normal; // points toward this enemy

            if (IsPinball)
            {
                if (collision.rigidbody != null && collision.rigidbody.TryGetComponent(out PinballEnemy other))
                {
                    // Pass the hit on, then carry on with a glancing bounce.
                    other.Hit(-normal, 1f);
                }

                if (Mathf.Abs(normal.y) < 0.6f)
                {
                    Vector3 flatNormal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
                    Vector3 flat = Vector3.ProjectOnPlane(lastVelocity, Vector3.up);
                    Vector3 reflected = Vector3.Reflect(flat, flatNormal) * ricochetKeep;
                    body.linearVelocity = new Vector3(reflected.x, Mathf.Max(lastVelocity.y, 1.5f), reflected.z);
                    pinballStartSpeed *= ricochetKeep;
                    ToonFollowCamera.Shake(0.06f);
                }
                return;
            }

            ShovePlayer(collision);
        }

        void OnCollisionStay(Collision collision)
        {
            if (!IsPinball)
                ShovePlayer(collision);
        }

        void ShovePlayer(Collision collision)
        {
            if (stunTimer > 0f || Time.time - lastShove < 0.6f || collision.rigidbody == null)
                return;
            if (!collision.rigidbody.TryGetComponent(out Gear5Player hero))
                return;

            lastShove = Time.time;
            Vector3 away = Vector3.ProjectOnPlane(hero.transform.position - transform.position, Vector3.up).normalized;
            hero.Launch(away * shoveSpeed + Vector3.up * 3.5f, 0.3f);
            ToonFollowCamera.Shake(0.12f);
        }
    }
}
