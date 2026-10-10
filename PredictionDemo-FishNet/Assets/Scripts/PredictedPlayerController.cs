using DefaultNamespace;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using GameKit.Dependencies.Utilities;
using UnityEngine;

public class PredictedPlayerController : TickNetworkBehaviour
{
    private readonly SyncVar<Color> color = new SyncVar<Color>();
    
    [Tooltip("Shared movement tuning from the common package, the same asset every demo uses.")]
    [SerializeField] private PlayerMovementConfig movementConfig;
    [Tooltip("Used instead of movementConfig when this object is a bot.")]
    [SerializeField] private PlayerMovementConfig botMovementConfig;
    
    [SerializeField] private Renderer _renderer;
    [SerializeField] private GameObject _playerPrefab;
    
    // PredictionRigidbody is set within OnStart/StopNetwork to use our
    // caching system. You could simply initialize a new instance in the field
    // but for increased performance using the cache is demonstrated.
    public PredictionRigidbody PredictionRigidbody;
    // Reconciled with the rigidbody (PlayerReconcileData), so replays restart from the flip state of the reconciled tick.
    private PlayerFlipState flipState;
    
    [SerializeField] private Camera localCamera;
    private LocalCameraSwitch cameraSwitch;

    // Bots are the player prefab spawned without an owner. Owner is replicated, so clients replaying a bot pick the same config.
    private RandomBotBrain botBrain;
    bool IsBot => !Owner.IsValid;
    PlayerMovementConfig Config => IsBot && botMovementConfig ? botMovementConfig : movementConfig;
    
    private void Awake()
    {
        PredictionRigidbody = ResettableObjectCaches<PredictionRigidbody>.Retrieve();
        PredictionRigidbody.Initialize(GetComponent<Rigidbody>());
        color.OnChange += OnColorChanged;
        cameraSwitch = new LocalCameraSwitch(localCamera);
    }
    
    private void OnDestroy()
    {
        ResettableObjectCaches<PredictionRigidbody>.StoreAndDefault(ref PredictionRigidbody);
        color.OnChange -= OnColorChanged;
    }

    void Start()
    {
        if (IsServerInitialized)
        {
            color.Value = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f));
            OnColorChanged(Color.white, color.Value, true);
        }
    }
    
    private void Update()
    {
        if (!base.IsOwner)
            return;
        
        cameraSwitch.Update();
    }
    
    private PlayerReplicateData CreateReplicateData()
    {
        // The server is the controller of bots and reads their RandomBotBrain.
        if (IsServerInitialized && IsBot)
            return CreateBotReplicateData();

        if (!base.IsOwner)
            return default;

        // Build the replicate data with all inputs which affect the prediction.
        return new PlayerReplicateData(DemoInput.ReadMovement());
    }
    
    private PlayerReplicateData CreateBotReplicateData()
    {
        if (!botBrain)
        {
            botBrain = GetComponent<RandomBotBrain>();
            if (!botBrain)
            {
                botBrain = gameObject.AddComponent<RandomBotBrain>();
            }
        }
        return new PlayerReplicateData(botBrain.GetInput());
    }

    protected override void TimeManager_OnTick()
    {
        RunInputs(CreateReplicateData());
    }
    
    protected override void TimeManager_OnPostTick()
    {
        CreateReconcile();
    }

    [Replicate]
    private void RunInputs(PlayerReplicateData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        /* ReplicateState is set based on if the data is new, being replayed, ect.
         * Visit the ReplicationState enum for more information on what each value
         * indicates. At the end of this guide a more advanced use of state will
         * be demonstrated. */
    
        // Be sure to always apply and set velocities using PredictionRigidbody
        // and never on the rigidbody itself; this includes if also accessing from
        // another script.

        PlayerMovementInput input = data.ToMovementInput();
        // Read the body before adding this tick's forces: the flip decides from the state the tick starts in.
        Config.ComputeFlip(input, PredictionRigidbody.Rigidbody.rotation, PredictionRigidbody.Rigidbody.angularVelocity, ref flipState,
            out Vector3 jumpVelocity, out Vector3 flipTorque);
        if (jumpVelocity != Vector3.zero)
            PredictionRigidbody.AddForce(jumpVelocity, ForceMode.VelocityChange);
        PredictionRigidbody.AddTorque(flipTorque, ForceMode.Acceleration);

        PredictionRigidbody.AddRelativeTorque(Config.ComputeTorque(input));
        if (Config.CanAccelerate(PredictionRigidbody.Rigidbody.linearVelocity.magnitude, input.boost))
        {
            PredictionRigidbody.AddRelativeForce(Config.ComputeForce(input));
        }
        
        // Simulate the added forces.
        // Typically you call this at the end of your replicate. Calling
        // Simulate is ultimately telling the PredictionRigidbody to iterate
        // the forces we added above.
        PredictionRigidbody.Simulate();
    }
    
    // Create the reconcile data here and call your reconcile method.
    public override void CreateReconcile()
    {
        // We must send back the state of the rigidbody. Using your
        // PredictionRigidbody field in the reconcile data is an easy
        // way to accomplish this. More advanced states may require other
        // values to be sent; this will be covered later on.
        PlayerReconcileData rd = new PlayerReconcileData(PredictionRigidbody, flipState);
        // Like with the replicate you could specify a channel here, though
        // it's unlikely you ever would with a reconcile.
        ReconcileState(rd);
    }
    
    [Reconcile]
    private void ReconcileState(PlayerReconcileData data, Channel channel = Channel.Unreliable)
    {
        // Call reconcile on your PredictionRigidbody field passing in
        // values from data.
        PredictionRigidbody.Reconcile(data.PredictionRigidbody);
        flipState = data.FlipState;
    }

    void OnColorChanged(Color prev, Color next, bool asServer)
    {
        _renderer.material.color = next;
    }
}
