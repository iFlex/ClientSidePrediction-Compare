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
    [SerializeField] private bool LimitSpeed = true;
    [SerializeField] private float MaxTravelSpeed = 15f;
    [SerializeField] private float MaxBoostTravelSpeed = 30f;
    
    [SerializeField] private Renderer _renderer;
    [SerializeField] private GameObject _playerPrefab;
    
    [SerializeField] private float RotationPower = 10;
    [SerializeField] private float ThrottlePower = 10;
    [SerializeField] private float BoostPower = 50;
    
    
    // PredictionRigidbody is set within OnStart/StopNetwork to use our
    // caching system. You could simply initialize a new instance in the field
    // but for increased performance using the cache is demonstrated.
    public PredictionRigidbody PredictionRigidbody;
    
    private void Awake()
    {
        PredictionRigidbody = ResettableObjectCaches<PredictionRigidbody>.Retrieve();
        PredictionRigidbody.Initialize(GetComponent<Rigidbody>());
        color.OnChange += OnColorChanged;
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
    
    
    float ReadKeyboardThrottle()
    {
        float up = UnityEngine.InputSystem.Keyboard.current.upArrowKey.isPressed ? 1 : 0;
        float down = UnityEngine.InputSystem.Keyboard.current.downArrowKey.isPressed ? 1 : 0;
        return -down + up;
    }

    float ReadKeyboardRotate()
    {
        float left = UnityEngine.InputSystem.Keyboard.current.leftArrowKey.isPressed ? 1 : 0;
        float right = UnityEngine.InputSystem.Keyboard.current.rightArrowKey.isPressed ? 1 : 0;
        return -left + right;
    }

    bool ReadKeyboardBoost()
    {
        return UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed;
    }
    
    private void Update()
    {
        if (!base.IsOwner)
            return;

        //REMARK: why do we even need to do this? can't we just read them directly in CreateReplicateData?
        //throttle = ReadKeyboardThrottle();
        //steer = ReadKeyboardRotate();
        //boost = ReadKeyboardBoost();
    }
    
    private PlayerReplicateData CreateReplicateData()
    {
        if (!base.IsOwner)
            return default;

        // Build the replicate data with all inputs which affect the prediction.
        // REMARK: why aer you reading the input data again here?
        //float horizontal = Input.GetAxisRaw("Horizontal");
        //float vertical = Input.GetAxisRaw("Vertical");
        PlayerReplicateData md = new PlayerReplicateData(ReadKeyboardBoost(), ReadKeyboardThrottle(), ReadKeyboardRotate());
        // REMARK: why are you forcing jump to false now?
        //_jump = false;

        return md;
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
        
        Vector3 torqueVector = RotationPower * data.steer * Vector3.up;
        Vector3 throttleVector = Vector3.forward * ( data.boost ? Mathf.Sign(data.throttle) * BoostPower : ThrottlePower * data.throttle);
        
        PredictionRigidbody.AddRelativeTorque(torqueVector);
        if (!LimitSpeed || PredictionRigidbody.Rigidbody.linearVelocity.magnitude < GetMaxSpeed(data.boost))
        {
            PredictionRigidbody.AddRelativeForce(throttleVector);
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
        PlayerReconcileData rd = new PlayerReconcileData(PredictionRigidbody);
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
    }

    void OnColorChanged(Color prev, Color next, bool asServer)
    {
        _renderer.material.color = next;
    }
    
    float GetMaxSpeed(bool boosting)
    {
        return boosting ? MaxBoostTravelSpeed : MaxTravelSpeed;
    }
}
