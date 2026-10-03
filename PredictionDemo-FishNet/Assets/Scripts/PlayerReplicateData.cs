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

        public PlayerReplicateData(bool boost, float throttle, float steer, bool strafeLeft, bool strafeRight, bool spin) : this()
        {
            this.boost = boost;
            this.throttle = throttle;
            this.steer = steer;
            this.strafeLeft = strafeLeft;
            this.strafeRight = strafeRight;
            this.spin = spin;
        }

        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
}