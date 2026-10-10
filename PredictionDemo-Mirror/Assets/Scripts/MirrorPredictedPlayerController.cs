using DefaultNamespace;
using Mirror;
using UnityEngine;

public class MirrorPredictedPlayerController : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnColorUpdated))]
    private Color color;

    [Tooltip("Shared movement tuning from the common package, the same asset every demo uses.")]
    [SerializeField] private PlayerMovementConfig movementConfig;
    [Tooltip("Used instead of movementConfig when this object is a bot.")]
    [SerializeField] private PlayerMovementConfig botMovementConfig;

    [SerializeField] private Renderer renderer;
    [SerializeField] private PredictedRigidbody predictedRigidbody;
    [SerializeField] private Rigidbody rigidbody;
    
    [SerializeField] private PlayerMovementInput input;
    [SerializeField] private PlayerFlipState flipState;
    [SerializeField] private bool groundTest = false;
    [SerializeField] private GameObject ground;
    
    [SerializeField] private Camera localCamera;
    private LocalCameraSwitch cameraSwitch;

    // Bots are the player prefab spawned without an owner; the server drives them from a RandomBotBrain.
    private RandomBotBrain botBrain;
    bool IsBot => isServer && connectionToClient == null;
    PlayerMovementConfig Config => IsBot && botMovementConfig ? botMovementConfig : movementConfig;
    
    private void Awake()
    {
        cameraSwitch = new LocalCameraSwitch(localCamera);
    }
    
    private void Start()
    {
        if (isServer)
        {
            color = new Color(Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f));
            OnColorUpdated(Color.white, color);
        }

        if (IsBot)
        {
            botBrain = GetComponent<RandomBotBrain>();
            if (!botBrain)
            {
                botBrain = gameObject.AddComponent<RandomBotBrain>();
            }
        }
    }
    
    void Update()
    {
        if (!isLocalPlayer)
            return;
            
        cameraSwitch.Update();
    }

    //TODO: pick colors for playables :D
    
    private void FixedUpdate()
    {
        if (!isServer && isLocalPlayer)
        {
            input = DemoInput.ReadMovement();
            ApplyFlip(predictedRigidbody.predictedRigidbody);
            
            predictedRigidbody.predictedRigidbody.AddRelativeTorque(Config.ComputeTorque(input));
            if (Config.CanAccelerate(rigidbody.linearVelocity.magnitude, input.boost))
            {
                predictedRigidbody.predictedRigidbody.AddRelativeForce(Config.ComputeForce(input));
            }
            CmdApplyServerForce(input.throttle, input.steer, input.boost, input.strafeLeft, input.strafeRight, input.spin, input.flip);
        }
        
        //TODO: a better host mode check?
        if (isServer && isClient && isLocalPlayer)
        {
            LocalApplyForces(DemoInput.ReadMovement());
        }

        if (IsBot && botBrain)
        {
            LocalApplyForces(botBrain.GetInput());
        }
    }
    
    [Command]
    void CmdApplyServerForce(float cthrottle, float csteer, bool cboost, bool cstrafeLeft, bool cstrafeRight, bool cspin, bool cflip)
    {
        LocalApplyForces(new PlayerMovementInput(cthrottle, csteer, cboost, cstrafeLeft, cstrafeRight, cspin, cflip));
    }

    void LocalApplyForces(PlayerMovementInput applied)
    {
        input = applied;
        ApplyFlip(rigidbody);
        
        rigidbody.AddRelativeTorque(Config.ComputeTorque(input));
        if (Config.CanAccelerate(rigidbody.linearVelocity.magnitude, input.boost))
        {
            rigidbody.AddRelativeForce(Config.ComputeForce(input));
        }
    }
    
    // Mirror has no rollback: the owning client and the server each run the flip on their own body with their own flip state,
    // and the server's PredictedRigidbody state corrects the client. The server steps it once per received command.
    void ApplyFlip(Rigidbody body)
    {
        Config.ComputeFlip(input, body.rotation, body.angularVelocity, ref flipState, out Vector3 jumpVelocity, out Vector3 flipTorque);
        if (jumpVelocity != Vector3.zero)
            body.AddForce(jumpVelocity, ForceMode.VelocityChange);
        body.AddTorque(flipTorque, ForceMode.Acceleration);
    }
    
    void OnColorUpdated(Color oldC, Color newC)
    {
        renderer.material.color = newC;
    }
    
    //TODO: ground test
    //TODO: other finesse aspects?
}
