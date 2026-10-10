using UnityEngine;
namespace DefaultNamespace
{
    public class RandomBotBrain : MonoBehaviour
    {
        [Header("Live Bot Inputs (Read these from your movement script)")]
        [Tooltip("-1 is backward, 1 is forward")]
        public float throttleInput;
        [Tooltip("-1 is left, 1 is right")]
        public float steerInput;
        public bool isBoosting;
        [Tooltip("Hold the flip input all the time: a flip only starts when the bot is tipped over and off cooldown, so this rights stuck bots.")]
        public bool autoFlip = true;

        [Header("Direction Settings")]
        [Tooltip("Min and Max time (in seconds) the bot will drive forward before re-evaluating")]
        public Vector2 forwardDuration = new Vector2(3f, 8f);
        [Tooltip("Min and Max time the bot will drive backward")]
        public Vector2 backwardDuration = new Vector2(1f, 3f);
        [Tooltip("Chance (0 to 1) to switch to reverse when re-evaluating direction")]
        [Range(0f, 1f)] public float reverseProbability = 0.2f;

        [Header("Steering Settings")]
        [Tooltip("Min and Max time the bot will spend turning")]
        public Vector2 turnDuration = new Vector2(0.5f, 2f);
        [Tooltip("Min and Max time the bot will force itself to drive straight (prevents circles)")]
        public Vector2 straightDuration = new Vector2(1f, 4f);
        public float steeringSmoothSpeed = 2f;

        [Header("Boost Settings")]
        public Vector2 boostDuration = new Vector2(0.5f, 2f);
        public Vector2 boostCooldown = new Vector2(3f, 10f);

        // Internal timers and targets
        private float directionTimer;
        private float steerTimer;
        private float boostTimer;

        private float targetThrottle;
        private float targetSteer;
        private bool isCurrentlyTurning;

        // The brain only drives and rights itself, it never strafes or spins.
        public PlayerMovementInput GetInput()
        {
            return new PlayerMovementInput(throttleInput, steerInput, isBoosting, false, false, false, autoFlip);
        }

        void Start()
        {
            // Initialize with random starting states
            targetThrottle = 1f;
            directionTimer = Random.Range(forwardDuration.x, forwardDuration.y);
            
            isCurrentlyTurning = false;
            targetSteer = 0f;
            steerTimer = Random.Range(straightDuration.x, straightDuration.y);

            isBoosting = false;
            boostTimer = Random.Range(boostCooldown.x, boostCooldown.y);
        }

        void Update()
        {
            HandleDirection();
            HandleSteering();
            HandleBoost();

            // Smoothly transition the actual inputs to make movement look natural
            throttleInput = Mathf.MoveTowards(throttleInput, targetThrottle, Time.deltaTime * steeringSmoothSpeed);
            steerInput = Mathf.MoveTowards(steerInput, targetSteer, Time.deltaTime * steeringSmoothSpeed);
        }

        private void HandleDirection()
        {
            directionTimer -= Time.deltaTime;
            
            if (directionTimer <= 0)
            {
                // Decide whether to go forward or backward
                bool goReverse = Random.value < reverseProbability;
                
                if (goReverse)
                {
                    targetThrottle = -1f;
                    directionTimer = Random.Range(backwardDuration.x, backwardDuration.y);
                }
                else
                {
                    targetThrottle = 1f;
                    directionTimer = Random.Range(forwardDuration.x, forwardDuration.y);
                }
            }
        }

        private void HandleSteering()
        {
            steerTimer -= Time.deltaTime;

            if (steerTimer <= 0)
            {
                // Toggle between a "Turning" state and a "Straight" state
                isCurrentlyTurning = !isCurrentlyTurning;

                if (isCurrentlyTurning)
                {
                    // Pick a random turn direction (left or right) and intensity
                    float turnDirection = Random.value > 0.5f ? 1f : -1f;
                    float turnIntensity = Random.Range(0.4f, 1f); // Avoid useless micro-turns
                    
                    targetSteer = turnDirection * turnIntensity;
                    steerTimer = Random.Range(turnDuration.x, turnDuration.y);
                }
                else
                {
                    // Force the bot to drive straight to break up the turns
                    targetSteer = 0f;
                    steerTimer = Random.Range(straightDuration.x, straightDuration.y);
                }
            }
        }

        private void HandleBoost()
        {
            boostTimer -= Time.deltaTime;

            if (boostTimer <= 0)
            {
                // Toggle boost state
                isBoosting = !isBoosting;

                if (isBoosting)
                {
                    // Set how long the boost will last
                    boostTimer = Random.Range(boostDuration.x, boostDuration.y);
                }
                else
                {
                    // Set how long the bot must wait before boosting again
                    boostTimer = Random.Range(boostCooldown.x, boostCooldown.y);
                }
            }
        }
    }
}