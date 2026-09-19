using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Spawning
{
    public struct SpawnConfig : IComponentData
    {
        public const float DefaultSpawnRadius = 40f;
        public const float DefaultInitialSpawnRate = 3f;
        public const float DefaultPeakSpawnRate = 425f;
        public const float DefaultRampDuration = 160f;
        public const float DefaultRampExponent = 2.5f;
        public const int DefaultMaxEnemies = 20000;
        public const uint DefaultRandomSeed = 1u;

        public Entity EnemyPrefab;
        public float SpawnRadius;
        // Enemies per second at session start; the whole session without a ramp.
        public float InitialSpawnRate;
        public float PeakSpawnRate;
        public float RampDuration;
        public float RampExponent;
        public int MaxEnemies;
        public uint RandomSeed;

        public static SpawnConfig Create(
            Entity enemyPrefab,
            float spawnRadius,
            float spawnRate,
            int maxEnemies,
            uint randomSeed)
        {
            return new SpawnConfig
            {
                EnemyPrefab = enemyPrefab,
                SpawnRadius = math.max(0f, spawnRadius),
                InitialSpawnRate = math.max(0f, spawnRate),
                PeakSpawnRate = math.max(0f, spawnRate),
                MaxEnemies = math.max(0, maxEnemies),
                RandomSeed = randomSeed
            };
        }

        // Rate eases from the initial to the peak value over the ramp, then holds.
        public SpawnConfig WithRamp(float peakSpawnRate, float rampDuration, float rampExponent)
        {
            var config = this;
            config.PeakSpawnRate = math.max(0f, peakSpawnRate);
            config.RampDuration = math.max(0f, rampDuration);
            config.RampExponent = math.max(0f, rampExponent);
            return config;
        }

        public float CalculateSpawnRate(float elapsed)
        {
            if (RampDuration <= 0f)
                return InitialSpawnRate;
            var progress = math.saturate(elapsed / RampDuration);
            return math.lerp(InitialSpawnRate, PeakSpawnRate, math.pow(progress, RampExponent));
        }

        // Closed-form integral of the rate: the spawn schedule does not depend on frame rate.
        public float CalculateScheduledSpawns(float elapsed)
        {
            elapsed = math.max(0f, elapsed);
            if (RampDuration <= 0f)
                return InitialSpawnRate * elapsed;
            var rampTime = math.min(elapsed, RampDuration);
            var progress = rampTime / RampDuration;
            return InitialSpawnRate * rampTime
                   + (PeakSpawnRate - InitialSpawnRate) * RampDuration / (RampExponent + 1f)
                   * math.pow(progress, RampExponent + 1f)
                   + PeakSpawnRate * (elapsed - rampTime);
        }
    }

    public struct SpawnState : IComponentData
    {
        public float SpawnBudget;
        public float Elapsed;
        public uint SpawnSequence;
    }
}
