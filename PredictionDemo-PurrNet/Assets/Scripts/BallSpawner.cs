using PurrNet.Prediction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DefaultNamespace
{
    // Equivalent of FishnetGameController: the server presses B to spawn a ball.
    // Spawning goes through hierarchy.Create inside Simulate so the ball is part of the predicted world.
    // Has no owner, so the server is its controller and the only side that reads the key.
    public class BallSpawner : PredictedIdentity<BallSpawner.SpawnInput, BallSpawner.SpawnState>
    {
        [SerializeField] private GameObject ballPrefab;
        [SerializeField] private Transform spawnPoint;

        // Update runs every frame on the controller; accumulate the key press until the next tick consumes it.
        protected override void UpdateInput(ref SpawnInput input)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
                input.spawn |= keyboard.bKey.wasPressedThisFrame;
        }

        // A key press is a one-off, don't repeat it when inputs are extrapolated.
        protected override void ModifyExtrapolatedInput(ref SpawnInput input)
        {
            input.spawn = false;
        }

        protected override void Simulate(SpawnInput input, ref SpawnState state, float delta)
        {
            if (!input.spawn)
                return;

            Transform at = spawnPoint ? spawnPoint : transform;
            hierarchy.Create(ballPrefab, at.position, at.rotation);
        }

        public struct SpawnInput : IPredictedData
        {
            public bool spawn;

            public void Dispose() { }
        }

        public struct SpawnState : IPredictedData<SpawnState>
        {
            public void Dispose() { }
        }
    }
}
