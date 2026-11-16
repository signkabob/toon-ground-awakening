using UnityEngine;

namespace ToonGround
{
    /// <summary>
    /// Scripts-only setup: when a scene starts playing without a <see cref="RubberGround"/>,
    /// this swaps the sample ground block for a rubber sheet, turns the sample character into
    /// the Gear 5 player, points the camera at them and builds a small playground:
    /// soft walls around the edge and a wall-bounce shaft, orange launch ramps, pinball
    /// enemies and a few test props.
    /// Add a RubberGround to a scene yourself to set things up by hand instead.
    /// </summary>
    static class Gear5Bootstrap
    {
        const string SampleGroundName = "block-grass-overhang-low";
        const string SampleCharacterName = "character-male-d";
        const bool SpawnTestProps = true;
        const bool SpawnPlayground = true;
        const int EnemyCount = 6;

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

            if (SpawnPlayground)
                BuildPlayground(groundObject.GetComponent<RubberGround>(), groundCenter, player.transform.position);
            if (SpawnTestProps)
                SpawnProps(groundCenter, player.transform.position);

            Debug.Log("Rubber playground ready: G / RB claps Soften on and off. Space jump, C pound, click punch.");
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
            for (int i = 0; i < 12; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance = 2.5f + (float)random.NextDouble() * 3f;
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

        static void BuildPlayground(RubberGround ground, Vector3 groundCenter, Vector3 playerPosition)
        {
            var root = new GameObject("Rubber Playground").transform;
            float y = groundCenter.y;
            var wallColor = new Color(0.62f, 0.5f, 0.85f);
            var shaftColor = new Color(0.4f, 0.75f, 0.85f);

            // Soft walls around the edge, just inside the rubber sheet. Sunk a little so ripples
            // don't open gaps underneath.
            float half = (ground != null ? ground.size : 40f) * 0.5f - 2.5f;
            const float edgeHeight = 3f;
            const float sink = 0.4f;
            float edgeY = y + edgeHeight * 0.5f - sink;
            SoftWall.Create("Soft Wall North", new Vector3(groundCenter.x, edgeY, groundCenter.z + half), new Vector3(half * 2f, edgeHeight, 0.8f), 0f, wallColor, root);
            SoftWall.Create("Soft Wall South", new Vector3(groundCenter.x, edgeY, groundCenter.z - half), new Vector3(half * 2f, edgeHeight, 0.8f), 0f, wallColor, root);
            SoftWall.Create("Soft Wall East", new Vector3(groundCenter.x + half, edgeY, groundCenter.z), new Vector3(0.8f, edgeHeight, half * 2f), 0f, wallColor, root);
            SoftWall.Create("Soft Wall West", new Vector3(groundCenter.x - half, edgeY, groundCenter.z), new Vector3(0.8f, edgeHeight, half * 2f), 0f, wallColor, root);

            Vector3 c = new Vector3(playerPosition.x, y, playerPosition.z);

            // Wall-bounce shaft: two tall parallel walls to stretch-launch between.
            const float shaftHeight = 7f;
            float shaftY = y + shaftHeight * 0.5f - sink;
            SoftWall.Create("Soft Shaft A", new Vector3(c.x + 8f, shaftY, c.z - 4f), new Vector3(0.8f, shaftHeight, 6f), 0f, shaftColor, root);
            SoftWall.Create("Soft Shaft B", new Vector3(c.x + 11.5f, shaftY, c.z - 4f), new Vector3(0.8f, shaftHeight, 6f), 0f, shaftColor, root);

            // A lone stretchy pillar wall to ping off in the open.
            SoftWall.Create("Soft Slab", new Vector3(c.x - 9f, y + 2f - sink, c.z + 9f), new Vector3(5f, 4f, 0.8f), 45f, wallColor, root);

            // Orange chevron launch ramps.
            LaunchRamp.Create("Launch Ramp 1", new Vector3(c.x - 5f, y, c.z + 2f), 0f, 5f, 2.5f, 22f, root);
            LaunchRamp.Create("Launch Ramp 2", new Vector3(c.x - 3f, y, c.z - 8f), 90f, 5f, 2.5f, 22f, root);
            LaunchRamp.Create("Launch Ramp 3", new Vector3(c.x + 4f, y, c.z + 10f), 200f, 5f, 2.5f, 25f, root);

            // Pinball enemies scattered around.
            var enemyColors = new[] { new Color(0.9f, 0.3f, 0.35f), new Color(0.55f, 0.35f, 0.9f) };
            var random = new System.Random(55);
            for (int i = 0; i < EnemyCount; i++)
            {
                float angle = i * Mathf.PI * 2f / EnemyCount + (float)random.NextDouble() * 0.5f;
                float distance = 6f + (float)random.NextDouble() * 5f;
                var spot = c + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance + Vector3.up * 1f;
                spot.x = Mathf.Clamp(spot.x, groundCenter.x - half + 1.5f, groundCenter.x + half - 1.5f);
                spot.z = Mathf.Clamp(spot.z, groundCenter.z - half + 1.5f, groundCenter.z + half - 1.5f);
                PinballEnemy.Create("Pinball Enemy " + i, spot, enemyColors[i % enemyColors.Length], root);
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
