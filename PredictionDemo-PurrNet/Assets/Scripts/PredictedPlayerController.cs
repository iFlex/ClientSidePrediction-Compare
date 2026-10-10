using PurrNet;
using PurrNet.Prediction;
using UnityEngine;

namespace DefaultNamespace
{
    // Same box controller as the Ursitoare and FishNet demos: arrows throttle/steer, space boosts.
    [RequireComponent(typeof(PredictedRigidbody))]
    public class PredictedPlayerController : PredictedIdentity<PredictedPlayerController.PlayerInput, PredictedPlayerController.PlayerState>
    {
        public static bool CLIENTS_PICK_COLOR = true;
        
        [Header("Visuals")]
        [Tooltip("The renderer that should change color.")]
        [SerializeField] private Renderer _renderer;
    
        // 1. The SyncVar goes inside your ONE allowed network identity
        public SyncVar<Color> playerColor = new();
        
        [Tooltip("Shared movement tuning from the common package, the same asset every demo uses.")]
        [SerializeField] private PlayerMovementConfig movementConfig;
        [Tooltip("Used instead of movementConfig when this object is a bot.")]
        [SerializeField] private PlayerMovementConfig botMovementConfig;

        private PredictedRigidbody _predictedRigidbody;
        
        [SerializeField] private Camera localCamera;
        private LocalCameraSwitch cameraSwitch;

        // Bots are the player prefab created without an owner. Ownership is part of the predicted world, so every side agrees.
        private RandomBotBrain botBrain;
        bool IsBot => !owner.HasValue;
        PlayerMovementConfig Config => IsBot && botMovementConfig ? botMovementConfig : movementConfig;
        
        private void OnColorChanged(Color newColor)
        {
            ApplyColor(newColor);
        }

        private void ApplyColor(Color color)
        {
            _renderer.material.color = color;
        }
        
        private void Awake()
        {
            _predictedRigidbody = GetComponent<PredictedRigidbody>();
            cameraSwitch = new LocalCameraSwitch(localCamera);
        }

        //NOTE: THIS DOESN'T WORK...
        //PurrNet doesn't enable SyncVars on PredictedIdentity from the looks of it...
        private void Start()
        {
            // Subscribe to changes so clients update when the server changes the value
            playerColor.onChanged += OnColorChanged;
            // Only the Server is allowed to assign the authoritative color
            if (CLIENTS_PICK_COLOR || isServer)
            {
                playerColor.value =
                    new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f));
                // Apply the color immediately upon spawning (handles the host and late joiners)
            }
            ApplyColor(playerColor.value);
        }

        private void OnDisable()
        {
            playerColor.onChanged -= OnColorChanged;
        }

        protected override void Update()
        {
            base.Update();
            if (isController && !IsBot)
            {
                cameraSwitch.Update();
            }
        }

        // Called once per tick on the controlling side; inputs here are held keys so sampling at the tick is enough.
        protected override void GetFinalInput(ref PlayerInput input)
        {
            // The server is the controller of bots and reads their RandomBotBrain.
            if (IsBot)
            {
                if (!botBrain)
                {
                    botBrain = GetComponent<RandomBotBrain>();
                    if (!botBrain)
                        botBrain = gameObject.AddComponent<RandomBotBrain>();
                }
                input = new PlayerInput(botBrain.GetInput());
                return;
            }

            input = new PlayerInput(DemoInput.ReadMovement());
        }

        protected override void Simulate(PlayerInput input, ref PlayerState state, float delta)
        {
            // Forces must go through PredictedRigidbody (not the Rigidbody) so they are part of the rolled back state.
            PlayerMovementInput movement = input.ToMovementInput();
            // Read the body before adding this tick's forces: the flip decides from the state the tick starts in.
            var flip = new PlayerFlipState(state.flipActiveTicks, state.flipCooldownTicks);
            Config.ComputeFlip(movement, _predictedRigidbody.rotation, _predictedRigidbody.angularVelocity, ref flip,
                out Vector3 jumpVelocity, out Vector3 flipTorque);
            state.flipActiveTicks = flip.activeTicks;
            state.flipCooldownTicks = flip.cooldownTicks;

            _predictedRigidbody.AddRelativeTorque(Config.ComputeTorque(movement));
            if (Config.CanAccelerate(_predictedRigidbody.linearVelocity.magnitude, movement.boost))
            {
                _predictedRigidbody.AddRelativeForce(Config.ComputeForce(movement));
            }

            // PredictedRigidbody changes its velocities as soon as a force is added (the other libraries wait for the
            // physics step), so the flip goes last: the jump must not count towards this tick's speed cap.
            if (jumpVelocity != Vector3.zero)
                _predictedRigidbody.AddForce(jumpVelocity, ForceMode.VelocityChange);
            _predictedRigidbody.AddTorque(flipTorque, ForceMode.Acceleration);
        }

        public struct PlayerInput : IPredictedData
        {
            public float throttle;
            public float steer;
            public bool boost;
            public bool strafeLeft;
            public bool strafeRight;
            public bool spin;
            public bool flip;

            public PlayerInput(PlayerMovementInput input)
            {
                throttle = input.throttle;
                steer = input.steer;
                boost = input.boost;
                strafeLeft = input.strafeLeft;
                strafeRight = input.strafeRight;
                spin = input.spin;
                flip = input.flip;
            }

            public PlayerMovementInput ToMovementInput()
            {
                return new PlayerMovementInput(throttle, steer, boost, strafeLeft, strafeRight, spin, flip);
            }

            public void Dispose() { }
        }

        // Physical state lives in PredictedRigidbody; the controller only predicts the flip's progress, which rolls back with it.
        public struct PlayerState : IPredictedData<PlayerState>
        {
            public int flipActiveTicks;
            public int flipCooldownTicks;

            public void Dispose() { }
        }
    }
}
