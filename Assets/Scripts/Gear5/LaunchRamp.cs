using System.Collections.Generic;
using UnityEngine;

namespace ToonGround
{
    /// <summary>
    /// Orange chevron launch ramp. Anything with a rigidbody that runs up it gets flung off
    /// the top along the ramp. Softened, the ramp turns to rubber and flings much harder.
    /// </summary>
    [DisallowMultipleComponent]
    public class LaunchRamp : MonoBehaviour
    {
        public static readonly Color Orange = new Color(1f, 0.55f, 0.12f);

        [Tooltip("Launch speed when the ramp is normal (m/s).")]
        public float launchSpeed = 9f;
        [Tooltip("Launch speed multiplier while the ramp is softened.")]
        public float softMultiplier = 1.6f;
        [Tooltip("Extra upward tilt added to the ramp direction.")]
        public float extraLift = 0.5f;
        [Tooltip("Must be moving up the ramp at least this fast (m/s) to be launched.")]
        public float minimumApproachSpeed = 0.5f;
        public float cooldown = 0.6f;

        readonly Dictionary<Rigidbody, float> lastLaunch = new Dictionary<Rigidbody, float>();
        Transform visual;
        float boing;
        float boingVelocity;

        public bool IsSoft => Rubberized.IsSoftened(gameObject);

        /// <summary>Up-slope launch direction in world space.</summary>
        public Vector3 LaunchDirection => (transform.forward + Vector3.up * extraLift).normalized;

        /// <summary>
        /// Builds a ramp whose low edge sits at <paramref name="foot"/> and rises along <paramref name="yaw"/>.
        /// </summary>
        public static LaunchRamp Create(string name, Vector3 foot, float yaw, float length, float width, float angle, Transform parent)
        {
            const float thickness = 0.4f;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var rotation = Quaternion.Euler(-angle, yaw, 0f);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 up = rotation * Vector3.up;
            root.transform.SetPositionAndRotation(foot + forward * (length * 0.5f) - up * (thickness * 0.5f), rotation);

            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(width, thickness, length);

            // Unscaled "Visual" pivot (it bounces), holding the scaled slab and the chevrons.
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            Object.DestroyImmediate(slab.GetComponent<Collider>());
            slab.transform.SetParent(visual, false);
            slab.transform.localScale = box.size;
            slab.GetComponent<Renderer>().material.color = Orange;

            // Dark chevrons (two angled bars each) pointing up the ramp.
            var chevronColor = new Color(0.45f, 0.18f, 0.05f);
            float barLength = width * 0.42f;
            for (int i = 0; i < 3; i++)
            {
                float z = -length * 0.3f + i * length * 0.3f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bar.name = "Chevron";
                    Object.DestroyImmediate(bar.GetComponent<Collider>());
                    bar.transform.SetParent(visual, false);
                    bar.transform.localPosition = new Vector3(side * barLength * 0.4f, thickness * 0.5f + 0.01f, z);
                    bar.transform.localRotation = Quaternion.Euler(0f, side * 35f, 0f);
                    bar.transform.localScale = new Vector3(barLength, 0.02f, 0.18f);
                    bar.GetComponent<Renderer>().material.color = chevronColor;
                }
            }

            return root.AddComponent<LaunchRamp>();
        }

        void Awake()
        {
            visual = transform.childCount > 0 ? transform.GetChild(0) : null;
        }

        void OnCollisionEnter(Collision collision) => TryLaunch(collision);
        void OnCollisionStay(Collision collision) => TryLaunch(collision);

        void TryLaunch(Collision collision)
        {
            var body = collision.rigidbody;
            if (body == null || body.isKinematic)
                return;

            // Only from the top face, while moving up-slope.
            Vector3 normal = collision.GetContact(0).normal; // sign depends on which side reports it
            if (Vector3.Dot(normal, transform.up) < 0f)
                normal = -normal;
            if (Vector3.Dot(normal, transform.up) < 0.7f)
                return;

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 velocity = body.linearVelocity;
            if (Vector3.Dot(velocity, flatForward) < minimumApproachSpeed)
                return;

            // Wait until the body is near the top lip so it uses the whole ramp.
            Vector3 local = transform.InverseTransformPoint(body.position);
            var box = GetComponent<BoxCollider>();
            if (box != null && local.z < box.size.z * 0.15f)
                return;

            if (lastLaunch.TryGetValue(body, out float last) && Time.time - last < cooldown)
                return;
            lastLaunch[body] = Time.time;

            bool soft = IsSoft;
            float speed = launchSpeed * (soft ? softMultiplier : 1f);
            Vector3 launch = LaunchDirection * speed;

            if (body.TryGetComponent(out Gear5Player player))
                player.Launch(launch, 0.6f);
            else if (body.TryGetComponent(out PinballEnemy enemy))
                enemy.Launch(launch, soft);
            else
                body.linearVelocity = launch;

            boingVelocity += soft ? 9f : 4f;
            if (soft)
                ToonFollowCamera.Shake(0.12f);
        }

        void Update()
        {
            // Springy "boing" on the visual when something launches off it.
            float dt = Time.deltaTime;
            boingVelocity += -boing * 220f * dt;
            boingVelocity /= 1f + 10f * dt;
            boing += boingVelocity * dt;
            if (visual != null)
                visual.localPosition = Vector3.up * Mathf.Clamp(boing * 0.05f, -0.15f, 0.25f);
        }
    }
}
