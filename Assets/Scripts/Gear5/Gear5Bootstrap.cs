using UnityEngine;

namespace ToonGround
{
    /// <summary>
    /// Scripts-only setup: when a scene starts playing without a <see cref="RubberGround"/>,
    /// this swaps the sample ground block for a rubber sheet, turns the sample character into
    /// the Gear 5 player, points the camera at them and scatters a few test props.
    /// Add a RubberGround to a scene yourself to set things up by hand instead.
    /// </summary>
    static class Gear5Bootstrap
    {
        const string SampleGroundName = "block-grass-overhang-low";
        const string SampleCharacterName = "character-male-d";
        const bool SpawnTestProps = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Setup()
        {
            if (Object.FindAnyObjectByType<RubberGround>() != null)
                return;

            Vector3 groundCenter = Vector3.zero;
            var oldGround = GameObject.Find(SampleGroundName);
            if (oldGround != null)
            {
                var bounds = CombinedBounds(oldGround);
                groundCenter = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                oldGround.SetActive(false);
            }

            var groundObject = new GameObject("Rubber Ground");
            groundObject.transform.position = groundCenter;
            groundObject.AddComponent<RubberGround>();

            var player = FindOrCreatePlayer(groundCenter);
            if (!player.TryGetComponent(out Gear5Player _))
                player.AddComponent<Gear5Player>();
            if (!player.TryGetComponent(out Gear5Aura _))
                player.AddComponent<Gear5Aura>();

            var cam = Camera.main;
            if (cam != null)
            {
                if (!cam.TryGetComponent(out ToonFollowCamera follow))
                    follow = cam.gameObject.AddComponent<ToonFollowCamera>();
                follow.target = player.transform;
            }

            if (SpawnTestProps)
                SpawnProps(groundCenter, player.transform.position);

            Debug.Log("Gear 5 prototype ready: press G (or RB on a gamepad) to toggle Gear 5.");
        }

        static GameObject FindOrCreatePlayer(Vector3 groundCenter)
        {
            var existing = Object.FindAnyObjectByType<Gear5Player>();
            if (existing != null)
                return existing.gameObject;

            var character = GameObject.Find(SampleCharacterName);
            if (character != null && character.GetComponent<Rigidbody>() != null)
            {
                if (character.transform.position.y < groundCenter.y)
                    character.transform.position = new Vector3(character.transform.position.x, groundCenter.y + 1f, character.transform.position.z);
                return character;
            }

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Gear 5 Player";
            capsule.transform.position = groundCenter + Vector3.up * 2f;
            capsule.GetComponent<Renderer>().material.color = Color.white;
            capsule.AddComponent<Rigidbody>();
            return capsule;
        }

        static void SpawnProps(Vector3 groundCenter, Vector3 playerPosition)
        {
            var root = new GameObject("Gear 5 Test Props").transform;
            var random = new System.Random(5);
            Color[] palette =
            {
                new Color(0.95f, 0.55f, 0.25f),
                new Color(0.35f, 0.65f, 0.95f),
                new Color(0.95f, 0.85f, 0.3f),
                new Color(0.85f, 0.4f, 0.75f),
            };

            Vector3 center = new Vector3(playerPosition.x, groundCenter.y, playerPosition.z);
            for (int i = 0; i < 24; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance = 2.5f + (float)random.NextDouble() * 7f;
                Vector3 spot = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                Color color = palette[i % palette.Length];

                GameObject prop;
                switch (i % 3)
                {
                    case 0: // crate
                        prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        prop.transform.localScale = Vector3.one * 0.6f;
                        prop.transform.position = spot + Vector3.up * 0.5f;
                        prop.AddComponent<Rigidbody>().mass = 1f;
                        break;
                    case 1: // ball
                        prop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        prop.transform.localScale = Vector3.one * 0.5f;
                        prop.transform.position = spot + Vector3.up * 0.5f;
                        prop.AddComponent<Rigidbody>().mass = 0.5f;
                        break;
                    default: // fixed post
                        prop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        prop.transform.localScale = new Vector3(0.35f, 0.9f, 0.35f);
                        prop.transform.position = spot + Vector3.up * 0.9f;
                        break;
                }

                prop.name = prop.name + " " + i;
                prop.transform.SetParent(root, true);
                prop.GetComponent<Renderer>().material.color = color;
            }
        }

        static Bounds CombinedBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
