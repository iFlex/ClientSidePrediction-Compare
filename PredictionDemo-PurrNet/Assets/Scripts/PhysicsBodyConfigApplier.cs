using UnityEngine;

namespace DefaultNamespace
{
    //NOTE: runs before the prediction and networking components on the same object, so they only ever see the configured values
    //(Mirror's PredictedRigidbody, for example, copies the Rigidbody and collider settings onto its ghost objects).
    [DefaultExecutionOrder(-1000)]
    public class PhysicsBodyConfigApplier : MonoBehaviour
    {
        [SerializeField] private PhysicsBodyConfig config;
        [SerializeField] private Rigidbody rigidbody;
        [Tooltip("Left empty, every collider on this object and its children gets the config's physics material.")]
        [SerializeField] private Collider[] colliders;

        void Awake()
        {
            if (!config)
            {
                Debug.LogWarning($"[PhysicsBodyConfigApplier] {name} has no PhysicsBodyConfig assigned, keeping the prefab's physics settings.");
                return;
            }

            if (!rigidbody)
                rigidbody = GetComponent<Rigidbody>();
            if (colliders == null || colliders.Length == 0)
                colliders = GetComponentsInChildren<Collider>(true);

            config.Apply(rigidbody, colliders);
        }
    }
}
