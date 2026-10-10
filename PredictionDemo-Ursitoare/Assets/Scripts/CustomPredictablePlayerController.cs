using Mirror;
using Sector0.Ursitoare.Data;
using Sector0.UrsitoareMirror;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DefaultNamespace
{
    public class CustomPredictablePlayerController : AbstractPredictedNetworkBehaviour
    {
        
        [SyncVar(hook = nameof(OnColorUpdated))]
        private Color color;

        [SerializeField] private Renderer renderer;
        private LocalCameraSwitch cameraSwitch;

        [SerializeField] private Camera localCamera;
        
        protected void Awake()
        {
            base.Awake();
            cameraSwitch = new LocalCameraSwitch(localCamera);
        }

        protected virtual void Start()
        {
            if (isServer)
            {
                color = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f));
            }
            OnColorUpdated(Color.white, color);
        }

        protected void Update()
        {
            if (!isLocalPlayer)
                return;
            
            cameraSwitch.Update();
        }
        
        void OnColorUpdated(Color old, Color newC)
        {
            if (renderer)
            {
                renderer.material.color = newC;
            }
        }
        
        [Tooltip("Shared movement tuning from the common package, the same asset every demo uses.")]
        [SerializeField] private PlayerMovementConfig movementConfig;
        [SerializeField] private Rigidbody rigidbody;

        [SerializeField] private PlayerMovementInput input;
        // Travels as component state, so a resimulation or snap restores it together with the body.
        [SerializeField] private PlayerFlipState flipState;

        // Where this controller's input comes from; the bot controller reads its brain instead of the keyboard.
        protected virtual PlayerMovementInput ReadInput()
        {
            return DemoInput.ReadMovement();
        }

        public override int GetFloatInputCount()
        {
            return 2;
        }

        public override int GetBinaryInputCount()
        {
            return 5;
        }

        public override void SampleInput(PredictionInputRecord record)
        {
            PlayerMovementInput sampled = ReadInput();
            record.WriteNextScalar(sampled.throttle);
            record.WriteNextScalar(sampled.steer);
            record.WriteNextBinary(sampled.boost);
            record.WriteNextBinary(sampled.strafeLeft);
            record.WriteNextBinary(sampled.strafeRight);
            record.WriteNextBinary(sampled.spin);
            record.WriteNextBinary(sampled.flip);
        }

        public override bool ValidateInput(float deltaTime, PredictionInputRecord record)
        {
            return true;
        }

        public override void LoadInput(PredictionInputRecord record)
        {
            input.throttle = record.ReadNextScalar();
            input.steer = record.ReadNextScalar();
            input.boost = record.ReadNextBool();
            input.strafeLeft = record.ReadNextBool();
            input.strafeRight = record.ReadNextBool();
            input.spin = record.ReadNextBool();
            input.flip = record.ReadNextBool();
        }

        public override void ClearInput()
        {
            input = default;
        }

        public override void ApplyForces()
        {
            // Read the body before adding this tick's forces: the flip decides from the state the tick starts in.
            movementConfig.ComputeFlip(input, rigidbody.rotation, rigidbody.angularVelocity, ref flipState,
                out Vector3 jumpVelocity, out Vector3 flipTorque);
            if (jumpVelocity != Vector3.zero)
            {
                rigidbody.AddForce(jumpVelocity, ForceMode.VelocityChange);
            }
            rigidbody.AddTorque(flipTorque, ForceMode.Acceleration);

            rigidbody.AddRelativeTorque(movementConfig.ComputeTorque(input));
            if (movementConfig.CanAccelerate(rigidbody.linearVelocity.magnitude, input.boost))
            {
                rigidbody.AddRelativeForce(movementConfig.ComputeForce(input));
            }
        }

        public override bool HasState()
        {
            return true;
        }

        public override void SampleComponentState(PhysicsStateRecord physicsStateRecord)
        {
            physicsStateRecord.componentState.WriteNextScalar(flipState.activeTicks);
            physicsStateRecord.componentState.WriteNextScalar(flipState.cooldownTicks);
        }

        public override void LoadComponentState(PhysicsStateRecord physicsStateRecord)
        {
            flipState.activeTicks = Mathf.RoundToInt(physicsStateRecord.componentState.ReadNextScalar());
            flipState.cooldownTicks = Mathf.RoundToInt(physicsStateRecord.componentState.ReadNextScalar());
        }

        public override int GetStateFloatCount()
        {
            return 2;
        }

        public override int GetStateBoolCount()
        {
            return 0;
        }
    }
}