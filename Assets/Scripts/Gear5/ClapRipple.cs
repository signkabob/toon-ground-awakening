using UnityEngine;

namespace ToonGround
{
    /// <summary>
    /// The Soften clap: a gold jelly ring that races out from the player to the Soften radius,
    /// hugging the rubber ground, then fades and removes itself.
    /// </summary>
    public class ClapRipple : MonoBehaviour
    {
        const int Segments = 72;

        Vector3 center;
        float radius;
        float duration;
        float age;
        Color color;
        LineRenderer line;

        public static void Spawn(Vector3 center, float radius, float duration, Color color)
        {
            var go = new GameObject("Clap Ripple");
            var ripple = go.AddComponent<ClapRipple>();
            ripple.center = center;
            ripple.radius = radius;
            ripple.duration = Mathf.Max(0.05f, duration);
            ripple.color = color;
        }

        void Start()
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = Segments;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            line.material = new Material(shader) { color = color };
            Draw();
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= duration * 1.6f)
            {
                Destroy(gameObject);
                return;
            }
            Draw();
        }

        void Draw()
        {
            // Ease out to the full radius, then fade while the jelly wobbles.
            float t = Mathf.Clamp01(age / duration);
            float eased = 1f - (1f - t) * (1f - t);
            float wobble = 1f + Mathf.Sin(age * 40f) * 0.04f * (1f - Mathf.Clamp01(age / (duration * 1.6f)));
            float r = Mathf.Max(0.1f, radius * eased * wobble);
            float fade = 1f - Mathf.Clamp01((age - duration) / (duration * 0.6f));

            var c = color;
            c.a *= fade;
            line.startColor = line.endColor = c;
            line.material.color = c;
            line.widthMultiplier = Mathf.Lerp(0.45f, 0.12f, t);

            var ground = RubberGround.Instance;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                var point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
                point.y = (ground != null ? ground.SampleHeight(point) : center.y) + 0.08f;
                line.SetPosition(i, point);
            }
        }
    }
}
