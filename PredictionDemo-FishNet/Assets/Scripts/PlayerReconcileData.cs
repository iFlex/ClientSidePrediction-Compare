using FishNet.Object.Prediction;

namespace DefaultNamespace
{
    public struct PlayerReconcileData : IReconcileData
    {
        // PredictionRigidbody is used to synchronize rigidbody states
        // and forces. This could be done manually but the PredictionRigidbody
        // type makes this process considerably easier. Velocities, kinematic state,
        // transform properties, pending velocities and more are automatically
        // handled with PredictionRigidbody.
        public PredictionRigidbody PredictionRigidbody;
    
        public PlayerReconcileData(PredictionRigidbody pr) : this()
        {
            PredictionRigidbody = pr;
        }

        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
}