using UnityEngine;
using UnityEngine.InputSystem;

namespace ToonGround
{
    /// <summary>
    /// Minimal rigidbody controller (Move / Jump from the project input actions).
    /// With Gear 5 on, the player rides the rubber ground like a trampoline and
    /// landing hard punches a dent that ripples outward.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class Gear5Player : MonoBehaviour
    {
        [Header("Input")]
        public string moveActionPath = "Player/Move";
        public string jumpActionPath = "Player/Jump";

        [Header("Movement")]
        public float moveSpeed = 4f;
        public float acceleration = 40f;
        public float jumpSpeed = 5.5f;
        public float turnSpeed = 12f;
        public float groundCheckDistance = 0.2f;

        [Header("Gear 5")]
        [Tooltip("Jump height multiplier while Gear 5 is on.")]
        public float gear5JumpMultiplier = 1.5f;
        [Tooltip("How much of the ground's upward speed is passed on to the player.")]
        public float bounceGain = 1.4f;
        [Tooltip("Falling faster than this (m/s) punches a dent into the ground on landing.")]
        public float stompSpeed = 5f;
        public float stompRadius = 1f;
        [Tooltip("Squash and stretch the character model while Gear 5 is on.")]
        public bool squashVisuals = true;

        Rigidbody body;
        Collider bodyCollider;
        Gear5Aura aura;
        InputAction move;
        InputAction jump;
        bool ownsMove;
        bool ownsJump;
        bool jumpQueued;
        bool grounded;
        float airborneFallSpeed;
        Transform[] visuals;
        Vector3[] visualScales;
        float stretch;
        float stretchVelocity;

        public bool Grounded => grounded;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (!c.isTrigger)
                {
                    bodyCollider = c;
                    break;
                }
            }

            aura = GetComponent<Gear5Aura>();

            // Squash the model, not the collider: scale the direct children.
            visuals = new Transform[transform.childCount];
            visualScales = new Vector3[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
            {
                visuals[i] = transform.GetChild(i);
                visualScales[i] = visuals[i].localScale;
            }
        }

        void OnEnable()
        {
            move = Gear5Input.Find(moveActionPath, Gear5Input.Move, out ownsMove);
            jump = Gear5Input.Find(jumpActionPath, Gear5Input.Jump, out ownsJump);
        }

        void OnDisable()
        {
            Gear5Input.Release(move, ownsMove);
            Gear5Input.Release(jump, ownsJump);
            move = jump = null;
        }

        bool Gear5On
        {
            get
            {
                if (aura == null)
                    aura = GetComponent<Gear5Aura>();
                return aura != null && aura.IsActive;
            }
        }

        void Update()
        {
            if (jump != null && jump.WasPressedThisFrame())
                jumpQueued = true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool wasGrounded = grounded;
            grounded = CheckGrounded();
            var ground = RubberGround.Instance;

            if (!grounded)
                airborneFallSpeed = Mathf.Max(0f, -body.linearVelocity.y);
            else if (!wasGrounded)
                Land(ground);

            // Camera-relative movement on the XZ plane.
            Vector2 input = move != null ? move.ReadValue<Vector2>() : Vector2.zero;
            Vector3 direction = CameraRelative(input);
            Vector3 velocity = body.linearVelocity;
            Vector3 horizontal = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), direction * moveSpeed, acceleration * dt);
            velocity.x = horizontal.x;
            velocity.z = horizontal.z;

            if (jumpQueued)
            {
                if (grounded)
                {
                    velocity.y = jumpSpeed * (Gear5On ? gear5JumpMultiplier : 1f);
                    grounded = false;
                    if (Gear5On && ground != null)
                        ground.AddImpulse(BottomPoint(), stompRadius, -jumpSpeed * 0.6f);
                }
                jumpQueued = false;
            }
            body.linearVelocity = velocity;

            if (direction.sqrMagnitude > 0.001f)
            {
                var facing = Quaternion.LookRotation(direction, Vector3.up);
                body.MoveRotation(Quaternion.Slerp(body.rotation, facing, turnSpeed * dt));
            }

            if (Gear5On && ground != null && ground.TryLaunch(body, BottomPoint().y, bounceGain))
                stretchVelocity += 5f;

            UpdateStretch(dt);
        }

        void Land(RubberGround ground)
        {
            if (Gear5On && ground != null && airborneFallSpeed > stompSpeed)
            {
                ground.AddImpulse(BottomPoint(), stompRadius, -airborneFallSpeed * 0.9f);
                ToonFollowCamera.Shake(Mathf.Clamp(airborneFallSpeed * 0.02f, 0.05f, 0.4f));
                stretchVelocity -= airborneFallSpeed * 0.5f;
            }
            airborneFallSpeed = 0f;
        }

        bool CheckGrounded()
        {
            Vector3 bottom = BottomPoint();
            // The ray starts inside our own collider, so it never hits it.
            Vector3 origin = bottom + Vector3.up * 0.1f;
            return Physics.Raycast(origin, Vector3.down, 0.1f + groundCheckDistance, ~0, QueryTriggerInteraction.Ignore);
        }

        Vector3 BottomPoint()
        {
            if (bodyCollider == null)
                return body.position;
            Bounds bounds = bodyCollider.bounds;
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        Vector3 CameraRelative(Vector2 input)
        {
            var cam = Camera.main;
            Vector3 forward = cam != null ? cam.transform.forward : Vector3.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
        }

        void UpdateStretch(float dt)
        {
            float target = Gear5On ? Mathf.Clamp(body.linearVelocity.y * 0.03f, -0.2f, 0.35f) : 0f;
            stretchVelocity += (target - stretch) * 80f * dt;
            stretchVelocity /= 1f + 8f * dt;
            stretch = Mathf.Clamp(stretch + stretchVelocity * dt, -0.45f, 0.6f);
        }

        void LateUpdate()
        {
            if (!squashVisuals)
                return;
            float squash = 1f / Mathf.Sqrt(1f + stretch);
            var factor = new Vector3(squash, 1f + stretch, squash);
            for (int i = 0; i < visuals.Length; i++)
                if (visuals[i] != null)
                    visuals[i].localScale = Vector3.Scale(visualScales[i], factor);
        }
    }
}
