using UnityEngine;
using UnityEngine.InputSystem;

namespace ToonGround
{
    /// <summary>
    /// Rigidbody controller (Move / Jump / Crouch / Attack from the project input actions) with
    /// the Peppy World rubber moves. With Soften (Gear 5) on:
    /// - rubber floors auto-hop you, jumps are 1.6x, and a ground pound catapults you 2.2x;
    /// - you cling to softened walls, stretch them by hanging on, and jump to be flung off;
    /// - punches and pounds turn softened enemies into pinballs.
    /// With it off you still get normal jumps, a ground pound, wall jumps off any wall and punches.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class Gear5Player : MonoBehaviour
    {
        [Header("Input")]
        public string moveActionPath = "Player/Move";
        public string jumpActionPath = "Player/Jump";
        public string poundActionPath = "Player/Crouch";
        public string attackActionPath = "Player/Attack";

        [Header("Movement")]
        public float moveSpeed = 4f;
        public float acceleration = 40f;
        [Tooltip("Steering in the air. Low, so launches keep their momentum.")]
        public float airAcceleration = 10f;
        public float jumpSpeed = 5.5f;
        public float turnSpeed = 12f;
        public float groundCheckDistance = 0.2f;
        [Tooltip("Jump still works this long after leaving the ground (s).")]
        public float coyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing still happens (s).")]
        public float jumpBuffer = 0.15f;
        [Tooltip("Jump buffer while Soften is on, long enough to catch the next auto-hop (s).")]
        public float softJumpBuffer = 0.45f;

        [Header("Soften: floors")]
        [Tooltip("Jump multiplier while Soften is on (Peppy World: 1.6x).")]
        public float gear5JumpMultiplier = 1.6f;
        [Tooltip("Rubber floors hop you automatically, at this fraction of a normal jump.")]
        public float autoHopFraction = 0.5f;
        [Tooltip("How much of the ground's upward speed is passed on to the player.")]
        public float bounceGain = 1.4f;
        [Tooltip("Falling faster than this (m/s) punches a dent into the ground on landing.")]
        public float stompSpeed = 5f;
        public float stompRadius = 1f;

        [Header("Ground pound")]
        public float poundSpeed = 18f;
        [Tooltip("Catapult after a softened ground pound, as a multiple of the normal jump (Peppy World: 2.2x).")]
        public float poundCatapultMultiplier = 2.2f;
        public float poundHitRadius = 3f;

        [Header("Walls")]
        [Tooltip("Seconds of hanging on a soft wall to reach full stretch charge.")]
        public float wallChargeTime = 1f;
        [Tooltip("Longest you can hang on a soft wall (s).")]
        public float maxClingTime = 2.5f;
        public float clingSlideSpeed = 0.6f;
        [Tooltip("Push away from a soft wall at zero / full charge (m/s).")]
        public Vector2 wallLaunchOut = new Vector2(6f, 14f);
        [Tooltip("Upward speed off a soft wall at zero / full charge (m/s).")]
        public Vector2 wallLaunchUp = new Vector2(7f, 11f);
        [Tooltip("Plain wall jump off any wall: out, up (m/s).")]
        public Vector2 wallJump = new Vector2(4.5f, 6f);

        [Header("Punch")]
        public float punchRange = 0.8f;
        public float punchRadius = 0.8f;
        public float punchCooldown = 0.35f;
        public float propPunchSpeed = 7f;

        [Header("Look")]
        [Tooltip("Squash and stretch the character model while Soften is on.")]
        public bool squashVisuals = true;

        Rigidbody body;
        Collider bodyCollider;
        Gear5Aura aura;
        InputAction move, jump, pound, attack;
        bool ownsMove, ownsJump, ownsPound, ownsAttack;

        bool grounded;
        float lastGroundedTime = float.NegativeInfinity;
        float jumpPressedTime = float.NegativeInfinity;
        float airborneFallSpeed;
        bool pounding;
        float launchLock;
        float lastPunch = float.NegativeInfinity;
        float punchStretch;

        // Wall contact (filled from OnCollisionStay).
        float wallContactTime = float.NegativeInfinity;
        Vector3 wallNormal;
        Collider wallCollider;
        float wallIgnoreUntil;
        SoftWall clingWall;
        float clingTime;

        Transform[] visuals;
        Vector3[] visualScales;
        float stretch;
        float stretchVelocity;

        public bool Grounded => grounded;
        public bool IsClinging => clingWall != null;

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
            pound = Gear5Input.Find(poundActionPath, Gear5Input.Pound, out ownsPound);
            attack = Gear5Input.Find(attackActionPath, Gear5Input.Attack, out ownsAttack);
        }

        void OnDisable()
        {
            Gear5Input.Release(move, ownsMove);
            Gear5Input.Release(jump, ownsJump);
            Gear5Input.Release(pound, ownsPound);
            Gear5Input.Release(attack, ownsAttack);
            move = jump = pound = attack = null;
            ReleaseWall();
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

        /// <summary>
        /// Fling the player (ramps, walls, enemy shoves). Air steering is weak for
        /// <paramref name="lockTime"/> seconds so the launch keeps its momentum.
        /// </summary>
        public void Launch(Vector3 velocity, float lockTime)
        {
            body.linearVelocity = velocity;
            launchLock = Mathf.Max(launchLock, lockTime);
            grounded = false;
            pounding = false;
            lastGroundedTime = float.NegativeInfinity;
            stretchVelocity += 6f;
            ReleaseWall();
        }

        void Update()
        {
            if (jump != null && jump.WasPressedThisFrame())
                jumpPressedTime = Time.time;

            if (pound != null && pound.WasPressedThisFrame() && !grounded && !pounding && clingWall == null)
                StartPound();

            if (attack != null && attack.WasPressedThisFrame() && Time.time - lastPunch >= punchCooldown)
                Punch();
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool wasGrounded = grounded;
            grounded = CheckGrounded();
            var ground = RubberGround.Instance;
            launchLock = Mathf.Max(0f, launchLock - dt);

            if (grounded)
                lastGroundedTime = Time.time;

            if (!grounded)
                airborneFallSpeed = Mathf.Max(0f, -body.linearVelocity.y);
            else if (!wasGrounded)
                Land(ground);

            // Auto-hops keep you in the air most of the time, so hold a jump press for the next bounce.
            bool jumpWanted = Time.time - jumpPressedTime <= (Gear5On ? softJumpBuffer : jumpBuffer);

            if (UpdateWall(dt, ref jumpWanted))
            {
                UpdateStretch(dt);
                return;
            }

            // Camera-relative movement on the XZ plane.
            Vector2 input = move != null ? move.ReadValue<Vector2>() : Vector2.zero;
            Vector3 direction = CameraRelative(input);
            Vector3 velocity = body.linearVelocity;

            if (pounding)
            {
                velocity = new Vector3(0f, -poundSpeed, 0f);
            }
            else
            {
                Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
                if (launchLock > 0f)
                    flat += direction * airAcceleration * 0.5f * dt;
                else if (grounded)
                    flat = Vector3.MoveTowards(flat, direction * moveSpeed, acceleration * dt);
                else if (direction.sqrMagnitude > 0.001f || flat.magnitude <= moveSpeed)
                    flat = Vector3.MoveTowards(flat, direction * Mathf.Max(moveSpeed, flat.magnitude * 0.98f), airAcceleration * dt);
                velocity.x = flat.x;
                velocity.z = flat.z;
            }

            bool canJump = grounded || Time.time - lastGroundedTime <= coyoteTime;
            if (jumpWanted && canJump && !pounding)
            {
                velocity.y = Mathf.Max(velocity.y, jumpSpeed * (Gear5On ? gear5JumpMultiplier : 1f));
                grounded = false;
                lastGroundedTime = float.NegativeInfinity;
                jumpPressedTime = float.NegativeInfinity;
                if (Gear5On && ground != null)
                    ground.AddImpulse(BottomPoint(), stompRadius, -jumpSpeed * 0.6f);
            }
            else if (grounded && Gear5On && !pounding && velocity.y < 0.5f)
            {
                // Rubber floor: it keeps bouncing you.
                velocity.y = jumpSpeed * autoHopFraction;
                stretchVelocity -= 2f;
                if (ground != null)
                    ground.AddImpulse(BottomPoint(), stompRadius * 0.7f, -jumpSpeed * 0.3f);
            }
            body.linearVelocity = velocity;

            if (direction.sqrMagnitude > 0.001f && !pounding)
            {
                var facing = Quaternion.LookRotation(direction, Vector3.up);
                body.MoveRotation(Quaternion.Slerp(body.rotation, facing, turnSpeed * dt));
            }

            if (Gear5On && !pounding && ground != null && ground.TryLaunch(body, BottomPoint().y, bounceGain))
                stretchVelocity += 5f;

            UpdateStretch(dt);
        }

        void StartPound()
        {
            pounding = true;
            launchLock = 0f;
            body.linearVelocity = new Vector3(0f, -poundSpeed, 0f);
            stretchVelocity += 4f;
        }

        void Land(RubberGround ground)
        {
            if (pounding)
            {
                pounding = false;
                bool soft = Gear5On;
                if (ground != null)
                    ground.AddImpulse(BottomPoint(), stompRadius * (soft ? 2f : 1f), -poundSpeed * (soft ? 1.2f : 0.5f));
                ToonFollowCamera.Shake(soft ? 0.45f : 0.2f);
                HitEnemiesAround(BottomPoint(), poundHitRadius, soft ? 1f : 0.7f);

                if (soft)
                {
                    // Soft ground pound: the rubber catapults you.
                    var v = body.linearVelocity;
                    v.y = jumpSpeed * poundCatapultMultiplier;
                    body.linearVelocity = v;
                    grounded = false;
                    lastGroundedTime = float.NegativeInfinity;
                    stretchVelocity += 8f;
                }
                else
                {
                    stretchVelocity -= 6f;
                }
                airborneFallSpeed = 0f;
                return;
            }

            if (Gear5On && ground != null && airborneFallSpeed > stompSpeed)
            {
                ground.AddImpulse(BottomPoint(), stompRadius, -airborneFallSpeed * 0.9f);
                ToonFollowCamera.Shake(Mathf.Clamp(airborneFallSpeed * 0.02f, 0.05f, 0.4f));
                stretchVelocity -= airborneFallSpeed * 0.5f;
            }
            airborneFallSpeed = 0f;
        }

        // ---- Walls -------------------------------------------------------------

        void OnCollisionStay(Collision collision)
        {
            if (collision.collider.GetComponentInParent<RubberGround>() != null)
                return;
            if (collision.rigidbody != null && !collision.rigidbody.isKinematic)
                return;
            if (Time.time < wallIgnoreUntil)
                return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector3 normal = collision.GetContact(i).normal;
                if (Mathf.Abs(normal.y) < 0.35f)
                {
                    wallNormal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
                    wallCollider = collision.collider;
                    wallContactTime = Time.fixedTime;
                    return;
                }
            }
        }

        /// <summary>Handles clinging and wall jumps. Returns true when it fully drove this step.</summary>
        bool UpdateWall(float dt, ref bool jumpWanted)
        {
            bool touchingWall = !grounded && !pounding && wallCollider != null
                && Time.fixedTime - wallContactTime <= dt * 2.5f;

            if (!touchingWall)
            {
                ReleaseWall();
                return false;
            }

            var softWall = wallCollider.GetComponentInParent<SoftWall>();
            bool soft = softWall != null && softWall.IsSoft;

            if (!soft)
            {
                ReleaseWall();
                if (jumpWanted)
                {
                    // Plain wall jump off any wall.
                    jumpPressedTime = float.NegativeInfinity;
                    jumpWanted = false;
                    wallIgnoreUntil = Time.time + 0.2f;
                    Launch(wallNormal * wallJump.x + Vector3.up * wallJump.y, 0.3f);
                }
                return false;
            }

            // Soft wall: cling and stretch.
            if (clingWall != softWall)
            {
                ReleaseWall();
                clingWall = softWall;
                clingTime = 0f;
                launchLock = 0f;
            }
            clingTime += dt;
            float charge = Mathf.Clamp01(clingTime / Mathf.Max(0.01f, wallChargeTime));
            clingWall.Hold(charge, wallNormal);

            if (jumpWanted)
            {
                jumpPressedTime = float.NegativeInfinity;
                wallIgnoreUntil = Time.time + 0.25f;
                Vector3 launch = wallNormal * Mathf.Lerp(wallLaunchOut.x, wallLaunchOut.y, charge)
                               + Vector3.up * Mathf.Lerp(wallLaunchUp.x, wallLaunchUp.y, charge);
                ToonFollowCamera.Shake(0.1f + 0.2f * charge);
                Launch(launch, 0.5f);
                stretchVelocity += 6f * charge;
                return true;
            }

            if (clingTime > maxClingTime)
            {
                // Lost grip: drop off.
                wallIgnoreUntil = Time.time + 0.4f;
                ReleaseWall();
                body.linearVelocity = wallNormal * 1.5f + Vector3.down * 1f;
                return true;
            }

            // Hang on, sinking slowly, pressed into the stretching wall.
            var v = body.linearVelocity;
            v.y = Mathf.MoveTowards(v.y, -clingSlideSpeed, 40f * dt);
            Vector3 flat = -wallNormal * 0.5f;
            body.linearVelocity = new Vector3(flat.x, v.y, flat.z);
            body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(-wallNormal, Vector3.up), turnSpeed * dt));
            stretch = Mathf.MoveTowards(stretch, -0.25f * charge, dt);
            return true;
        }

        void ReleaseWall()
        {
            if (clingWall != null)
                clingWall.Release();
            clingWall = null;
            clingTime = 0f;
        }

        // ---- Hits --------------------------------------------------------------

        void Punch()
        {
            lastPunch = Time.time;
            punchStretch = 0.6f;
            Vector3 forward = transform.forward;
            Vector3 center = body.position + Vector3.up * 0.4f + forward * punchRange;

            foreach (var hit in Physics.OverlapSphere(center, punchRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var rb = hit.attachedRigidbody;
                if (rb == null || rb == body)
                    continue;
                if (rb.TryGetComponent(out PinballEnemy enemy))
                    enemy.Hit(forward);
                else if (!rb.isKinematic)
                    rb.linearVelocity = forward * propPunchSpeed * (Rubberized.IsSoftened(rb.gameObject) ? 1.6f : 1f) + Vector3.up * 2f;
            }
        }

        void HitEnemiesAround(Vector3 point, float radius, float strength)
        {
            foreach (var hit in Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var rb = hit.attachedRigidbody;
                if (rb == null || rb == body || !rb.TryGetComponent(out PinballEnemy enemy))
                    continue;
                Vector3 away = rb.position - point;
                enemy.Hit(away, strength);
            }
        }

        // ---- Helpers -------------------------------------------------------------

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
            punchStretch = Mathf.MoveTowards(punchStretch, 0f, dt * 4f);
        }

        void LateUpdate()
        {
            if (!squashVisuals)
                return;
            float squash = 1f / Mathf.Sqrt(1f + stretch);
            // Stretchy punch: the model shoots forward along its facing.
            var factor = new Vector3(squash, 1f + stretch, squash * (1f + punchStretch));
            for (int i = 0; i < visuals.Length; i++)
                if (visuals[i] != null)
                    visuals[i].localScale = Vector3.Scale(visualScales[i], factor);
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(16, 60, 900, 24),
                "Space jump / wall jump (hang on a soft wall to charge)   C pound   Click or Enter punch   G clap Soften",
                style);
        }
    }
}
