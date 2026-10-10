namespace DefaultNamespace
{
    // Everything a player or bot can ask the box to do in one tick. Each library carries it in its own input format.
    [System.Serializable]
    public struct PlayerMovementInput
    {
        public float throttle;
        public float steer;
        public bool boost;
        public bool strafeLeft;
        public bool strafeRight;
        public bool spin;
        // Held, not pressed: a held flag survives lost or repeated input packets, the flip itself is gated by PlayerFlipState.
        public bool flip;

        public PlayerMovementInput(float throttle, float steer, bool boost, bool strafeLeft, bool strafeRight, bool spin, bool flip = false)
        {
            this.throttle = throttle;
            this.steer = steer;
            this.boost = boost;
            this.strafeLeft = strafeLeft;
            this.strafeRight = strafeRight;
            this.spin = spin;
            this.flip = flip;
        }
    }

    // The flip's progress. It must be part of each library's predicted state so a rollback restores it with the body;
    // otherwise a replay would see the cooldown as already spent and never jump.
    [System.Serializable]
    public struct PlayerFlipState
    {
        // 0 while not flipping, otherwise the number of ticks the corrective torque has been running.
        public int activeTicks;
        // Ticks left before another flip can start.
        public int cooldownTicks;

        public PlayerFlipState(int activeTicks, int cooldownTicks)
        {
            this.activeTicks = activeTicks;
            this.cooldownTicks = cooldownTicks;
        }
    }
}
