using Prediction.Simulation;

namespace DefaultNamespace
{
    public class RewindablePhysicsController2 : RewindablePhysicsController
    {
        public RewindablePhysicsController2() : base()
        {
            Setup(false);
        }

        public RewindablePhysicsController2(int bufferSize) : base(bufferSize)
        {
            Setup(false);
        }
    }
}