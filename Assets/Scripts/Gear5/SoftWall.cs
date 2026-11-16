using UnityEngine;

namespace ToonGround
{
    /// <summary>
    /// A wall that can be Softened. While softened the player clings to it, stretches it by
    /// hanging on (the longer, the bigger the charge) and is flung off it on jump.
    /// The collider stays put; only the visual child dents in and wobbles back.
    /// Unsoftened, it is a normal wall you can still wall-jump off.
    /// </summary>
    [DisallowMultipleComponent]
    public class SoftWall : MonoBehaviour
    {
        [Tooltip("How far the wall surface dents in at full charge (m).")]
        public float maxDent = 0.6f;
        public float wobbleSpring = 160f;
        public float wobbleDamping = 9f;

        Transform visual;
        Vector3 visualBasePosition;
        Vector3 visualBaseScale;
        Vector3 dentDirection = Vector3.forward; // local, pointing into the wall
        float dent;
        float dentVelocity;
        float heldDent;
        float lastHoldTime = float.NegativeInfinity;

        public bool IsSoft => Rubberized.IsSoftened(gameObject);

        /// <summary>Builds a soft wall: a static box collider plus a separate visual cube.</summary>
        public static SoftWall Create(string name, Vector3 center, Vector3 size, float yaw, Color color, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
            var box = root.AddComponent<BoxCollider>();
            box.size = size;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = size;
            visual.GetComponent<Renderer>().material.color = color;

            return root.AddComponent<SoftWall>();
        }

        void Awake()
        {
            visual = transform.childCount > 0 ? transform.GetChild(0) : null;
            if (visual != null)
            {
                visualBasePosition = visual.localPosition;
                visualBaseScale = visual.localScale;
            }
        }

        /// <summary>
        /// Called every physics step while someone hangs on the wall.
        /// <paramref name="charge"/> is 0..1, <paramref name="outwardNormal"/> points from the wall to the player.
        /// </summary>
        public void Hold(float charge, Vector3 outwardNormal)
        {
            dentDirection = transform.InverseTransformDirection(-outwardNormal).normalized;
            heldDent = Mathf.Clamp01(charge) * maxDent;
            lastHoldTime = Time.fixedTime;
        }

        /// <summary>Let go: the wall snaps back and wobbles.</summary>
        public void Release()
        {
            lastHoldTime = float.NegativeInfinity;
            ToonFollowCamera.Shake(0.15f);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool held = Time.fixedTime - lastHoldTime <= dt * 1.5f;
            if (held)
            {
                dent = Mathf.MoveTowards(dent, heldDent, dt * 4f);
                dentVelocity = 0f;
            }
            else
            {
                dentVelocity += -dent * wobbleSpring * dt;
                dentVelocity /= 1f + wobbleDamping * dt;
                dent += dentVelocity * dt;
            }
        }

        void LateUpdate()
        {
            if (visual == null)
                return;

            // Dent in along the push direction and bulge out sideways a little (volume-ish).
            Vector3 dir = dentDirection;
            Vector3 absDir = new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));
            float thin = Mathf.Clamp(dent, -maxDent, maxDent);
            Vector3 scale = visualBaseScale;
            Vector3 squash = Vector3.one - absDir * (thin / Mathf.Max(0.01f, Vector3.Scale(visualBaseScale, absDir).magnitude));
            Vector3 bulge = Vector3.one + (Vector3.one - absDir) * thin * 0.08f;
            visual.localScale = Vector3.Scale(scale, Vector3.Scale(squash, bulge));
            visual.localPosition = visualBasePosition + dir * thin * 0.5f;
        }
    }
}
