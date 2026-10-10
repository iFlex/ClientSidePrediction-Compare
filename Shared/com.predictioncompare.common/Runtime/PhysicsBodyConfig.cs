using UnityEngine;

namespace DefaultNamespace
{
    //NOTE: shared verbatim by all four demos so they simulate players and balls with identical physics. One asset per kind of body (PlayerPhysicsConfig, BallPhysicsConfig).
    //Values are applied once, when the body spawns (see PhysicsBodyConfigApplier). Server and clients must use the same values or prediction will keep correcting.
    [CreateAssetMenu(menuName = "Prediction Demo/Physics Body Config", fileName = "PhysicsBodyConfig")]
    public class PhysicsBodyConfig : ScriptableObject
    {
        [Header("Rigidbody")]
        [Min(0.0001f)] public float mass = 1f;
        [Min(0f)] public float linearDamping = 0f;
        [Min(0f)] public float angularDamping = 0.05f;
        public bool useGravity = true;

        [Header("Physics material")]
        [Min(0f)] public float dynamicFriction = 0.01f;
        [Min(0f)] public float staticFriction = 0.02f;
        [Range(0f, 1f)] public float bounciness = 0f;
        public PhysicsMaterialCombine frictionCombine = PhysicsMaterialCombine.Average;
        public PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Average;

        //Built from the values above and shared by every body using this config.
        PhysicsMaterial _material;

        public void Apply(Rigidbody body, Collider[] colliders)
        {
            if (body)
            {
                body.mass = mass;
                body.linearDamping = linearDamping;
                body.angularDamping = angularDamping;
                body.useGravity = useGravity;
            }

            PhysicsMaterial material = GetMaterial();
            foreach (Collider collider in colliders)
            {
                if (collider && !collider.isTrigger)
                    collider.sharedMaterial = material;
            }
        }

        PhysicsMaterial GetMaterial()
        {
            if (!_material)
                _material = new PhysicsMaterial($"{name} (runtime)");

            _material.dynamicFriction = dynamicFriction;
            _material.staticFriction = staticFriction;
            _material.bounciness = bounciness;
            _material.frictionCombine = frictionCombine;
            _material.bounceCombine = bounceCombine;
            return _material;
        }
    }
}
