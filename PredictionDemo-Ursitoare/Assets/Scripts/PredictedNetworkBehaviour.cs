using Mirror;
using Prediction;
using Prediction.Components.Controllers;
using UnityEngine;

namespace DefaultNamespace
{
    public class PredictedNetworkBehaviour : NetworkBehaviour, PredictedEntity
    {
        [SyncVar(hook = nameof(OnColorUpdated))]
        private Color color;

        [SerializeField] private Renderer renderer;
        [SerializeField] private MonoBehaviour[] predictionComponents;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private int bufferSize = 50;
        public PredictedEntityVisuals visuals;
        public ClientPredictedEntity clientPredictedEntity { get; private set; }
        public ServerPredictedEntity serverPredictedEntity { get; private set; }
        
        void Awake()
        {
            if (!_rigidbody)
            {
                _rigidbody = GetComponent<Rigidbody>();
            }
        }
        
        public override void OnStartServer()
        {
            ConfigureAsServer();
            int connId = (connectionToClient == null) ? 0 : connectionToClient.connectionId;
            ServerPredictionManager.Instance.SetEntityOwner(serverPredictedEntity, connId);
            
            color = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f));
            OnColorUpdated(Color.white, color);
        }
        
        public override void OnStartClient()
        {
            if (!isServer)
            {
                ConfigureAsClient();
            }
        }
        
        void ConfigureAsServer()
        {
            Debug.Log($"[PredictedNetworkBehaviour][ConfigureAsServer]({netId}) bfrSize:{bufferSize} rb:{_rigidbody} visuals:{visuals}");
            serverPredictedEntity = new ServerPredictedEntity(netId, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(predictionComponents), WrapperHelpers.GetComponents(predictionComponents));
            ((PredictedEntity)this).Register();
            visuals.SetServerPredictedEntity(transform);
        }

        void ConfigureAsClient()
        {
            Debug.Log($"[PredictedNetworkBehaviour][ConfigureAsClient]({netId}) bfrSize:{bufferSize} rb:{_rigidbody} visuals:{visuals}");
            clientPredictedEntity = new ClientPredictedEntity(netId, false, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(predictionComponents), WrapperHelpers.GetComponents(predictionComponents));
            visuals.SetClientPredictedEntity(clientPredictedEntity, PredictionManager.INTERPOLATION_PROVIDER());
            ((PredictedEntity)this).Register();
        }
        
        public uint GetId()
        {
            return netId;
        }

        public int GetOwnerId()
        {
            return (netIdentity.connectionToClient == null) ? 0 : netIdentity.connectionToClient.connectionId;
        }

        public ClientPredictedEntity GetClientEntity()
        {
            return clientPredictedEntity;
        }

        public ServerPredictedEntity GetServerEntity()
        {
            return serverPredictedEntity;
        }

        public PredictedEntityVisuals GetVisualsControlled()
        {
            return visuals;
        }

        public bool IsServer()
        {
            return isServer;
        }

        public bool IsClient()
        {
            return isClient;
        }

        public Rigidbody GetRigidbody()
        {
            return _rigidbody;
        }

        void OnColorUpdated(Color old, Color newC)
        {
            renderer.material.color = newC;
        }
    }
}