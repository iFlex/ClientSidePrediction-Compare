using FishNet.Object.Prediction;

namespace DefaultNamespace
{
    public struct PlayerReplicateData: IReplicateData
    {
        public bool boost;
        public float throttle;
        public float steer;
        public bool strafeLeft;
        public bool strafeRight;
        public bool spin;
        public bool flip;

        public PlayerReplicateData(bool boost, float throttle, float steer, bool strafeLeft, bool strafeRight, bool spin, bool flip) : this()
        {
            this.boost = boost;
            this.throttle = throttle;
            this.steer = steer;
            this.strafeLeft = strafeLeft;
            this.strafeRight = strafeRight;
            this.spin = spin;
            this.flip = flip;
        }

        public PlayerReplicateData(PlayerMovementInput input) : this(input.boost, input.throttle, input.steer, input.strafeLeft, input.strafeRight, input.spin, input.flip) { }

        public PlayerMovementInput ToMovementInput()
        {
            return new PlayerMovementInput(throttle, steer, boost, strafeLeft, strafeRight, spin, flip);
        }

        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
}
