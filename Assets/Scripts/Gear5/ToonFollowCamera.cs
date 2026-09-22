using UnityEngine;

namespace ToonGround
{
    /// <summary>Simple smoothed follow camera with a cartoon screen shake.</summary>
    public class ToonFollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 4f, -6.5f);
        public float lookHeight = 0.5f;
        public float followSharpness = 5f;

        static float shake;
        Vector3 smoothed;
        bool hasSmoothed;

        /// <summary>Shake every follow camera by this many metres (fades out).</summary>
        public static void Shake(float amount)
        {
            shake = Mathf.Max(shake, amount);
        }

        void LateUpdate()
        {
            if (target == null)
                return;

            Vector3 desired = target.position + offset;
            if (!hasSmoothed)
            {
                smoothed = desired;
                hasSmoothed = true;
            }
            smoothed = Vector3.Lerp(smoothed, desired, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));

            transform.position = smoothed + Random.insideUnitSphere * shake;
            transform.LookAt(target.position + Vector3.up * lookHeight);
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.5f);
        }
    }
}
