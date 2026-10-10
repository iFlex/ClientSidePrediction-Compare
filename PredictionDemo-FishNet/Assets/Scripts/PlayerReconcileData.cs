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
        // Flip progress is gameplay state outside the rigidbody, so it must be reconciled alongside it.
        public int FlipActiveTicks;
        public int FlipCooldownTicks;

        public PlayerReconcileData(PredictionRigidbody pr, PlayerFlipState flipState) : this()
        {
            PredictionRigidbody = pr;
            FlipActiveTicks = flipState.activeTicks;
            FlipCooldownTicks = flipState.cooldownTicks;
        }

        public PlayerFlipState FlipState => new PlayerFlipState(FlipActiveTicks, FlipCooldownTicks);

        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
}
