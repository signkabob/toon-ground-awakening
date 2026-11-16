using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ToonGround
{
    /// <summary>
    /// Gear 5 / Soften on/off (G on keyboard, right bumper on gamepad). Turning it on is a hand
    /// clap: a gold jelly ripple races out to the radius. While on, the ground around the player
    /// turns to rubber and pulses ripples outward, and everything with a collider inside the
    /// radius (floors, walls, ramps, props, enemies) becomes rubbery (see <see cref="Rubberized"/>).
    /// A power meter drains while it is on and refills while it is off.
    /// </summary>
    [DisallowMultipleComponent]
    public class Gear5Aura : MonoBehaviour
    {
        [Header("Input")]
        public string toggleActionPath = "Player/Gear5";

        [Header("Aura")]
        public float radius = 8f;
        [Tooltip("Seconds between ripples sent out from the player.")]
        public float waveInterval = 0.8f;
        [Tooltip("Radius of the ring each ripple starts from, around the player.")]
        public float waveRingRadius = 1.5f;
        public float waveRingWidth = 0.5f;
        [Tooltip("Upward kick of each ripple (m/s).")]
        public float waveStrength = 4f;
        [Tooltip("Downward punch into the ground when Gear 5 turns on (m/s).")]
        public float awakeningPunch = 10f;
        [Tooltip("Seconds between scans for things inside the radius.")]
        public float scanInterval = 0.15f;

        [Header("Power meter")]
        [Tooltip("Seconds of Soften from a full meter.")]
        public float powerDuration = 8f;
        [Tooltip("Seconds to refill an empty meter while Soften is off.")]
        public float rechargeDuration = 5f;
        [Tooltip("Meter needed (0..1) to clap Soften on.")]
        [Range(0f, 1f)] public float minimumPowerToClap = 0.2f;

        [Header("Clap")]
        [Tooltip("Seconds for the clap ripple to reach the radius.")]
        public float clapRippleTime = 0.35f;
        [Tooltip("Upward kick the clap gives the ground just inside the radius (m/s).")]
        public float clapWaveStrength = 6f;

        [Header("Look")]
        public Color ringColor = new Color(1f, 0.78f, 0.25f, 0.9f);
        public float ringWidth = 0.08f;

        public bool IsActive { get; private set; }

        /// <summary>Soften power meter, 0..1.</summary>
        public float Power { get; private set; } = 1f;

        InputAction toggle;
        bool ownsToggle;
        float waveTimer;
        float scanTimer;
        LineRenderer ring;
        readonly HashSet<GameObject> scanned = new HashSet<GameObject>();

        void OnEnable()
        {
            toggle = Gear5Input.Find(toggleActionPath, Gear5Input.Gear5Toggle, out ownsToggle);
        }

        void OnDisable()
        {
            Gear5Input.Release(toggle, ownsToggle);
            toggle = null;
            SetActive(false);
        }

        void Update()
        {
            if (toggle != null && toggle.WasPressedThisFrame())
            {
                if (IsActive || Power >= minimumPowerToClap)
                    SetActive(!IsActive);
            }

            if (IsActive)
            {
                Power = Mathf.Max(0f, Power - Time.deltaTime / Mathf.Max(0.01f, powerDuration));
                if (Power <= 0f)
                    SetActive(false);
            }
            else
            {
                Power = Mathf.Min(1f, Power + Time.deltaTime / Mathf.Max(0.01f, rechargeDuration));
            }
        }

        public void SetActive(bool active)
        {
            if (IsActive == active)
                return;
            IsActive = active;

            if (active)
            {
                waveTimer = 0f;
                scanTimer = 0f;
                var ground = RubberGround.Instance;
                if (ground != null)
                {
                    ground.KeepRubbery(transform.position, radius);
                    ground.AddImpulse(transform.position, 1.5f, -awakeningPunch);
                    ground.AddRingImpulse(transform.position, radius * 0.6f, 0.8f, clapWaveStrength);
                }
                ClapRipple.Spawn(transform.position, radius, clapRippleTime, ringColor);
                RubberizeNearby();
                ToonFollowCamera.Shake(0.35f);
            }

            if (ring != null)
                ring.enabled = active;
        }

        void FixedUpdate()
        {
            if (!IsActive)
                return;

            var ground = RubberGround.Instance;
            if (ground != null)
            {
                ground.KeepRubbery(transform.position, radius);

                waveTimer -= Time.fixedDeltaTime;
                if (waveTimer <= 0f)
                {
                    waveTimer = waveInterval;
                    ground.AddRingImpulse(transform.position, waveRingRadius, waveRingWidth, waveStrength);
                }
            }

            scanTimer -= Time.fixedDeltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = scanInterval;
                RubberizeNearby();
            }
        }

        void RubberizeNearby()
        {
            scanned.Clear();
            var hits = Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform) || hit.GetComponentInParent<RubberGround>() != null)
                    continue;

                var target = hit.attachedRigidbody != null ? hit.attachedRigidbody.gameObject : hit.gameObject;
                if (target == gameObject || !scanned.Add(target))
                    continue;

                if (!target.TryGetComponent(out Rubberized rubber))
                    rubber = target.AddComponent<Rubberized>();
                rubber.Touch();
            }
        }

        void LateUpdate()
        {
            if (!IsActive)
                return;
            if (ring == null)
                ring = CreateRing();
            DrawRing();
        }

        LineRenderer CreateRing()
        {
            var ringObject = new GameObject("Gear 5 Ring");
            ringObject.transform.SetParent(transform, false);
            var line = ringObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 72;
            line.widthMultiplier = ringWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            line.material = new Material(shader) { color = ringColor };
            line.startColor = line.endColor = ringColor;
            return line;
        }

        void DrawRing()
        {
            var ground = RubberGround.Instance;
            Vector3 center = transform.position;
            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / ring.positionCount;
                var point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                point.y = (ground != null ? ground.SampleHeight(point) : center.y) + 0.05f;
                ring.SetPosition(i, point);
            }
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            style.normal.textColor = IsActive ? ringColor : new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(16, 12, 520, 30), IsActive ? "SOFTEN: ON  (G / RB to turn off)" : "Soften: off  (G / RB to clap)", style);

            // Power meter.
            var back = new Rect(16, 42, 220, 12);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            bool canClap = IsActive || Power >= minimumPowerToClap;
            GUI.color = canClap ? ringColor : new Color(0.6f, 0.6f, 0.6f, 0.9f);
            GUI.DrawTexture(new Rect(back.x + 2, back.y + 2, (back.width - 4) * Power, back.height - 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
