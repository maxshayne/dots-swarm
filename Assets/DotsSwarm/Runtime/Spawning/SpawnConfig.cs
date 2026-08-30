using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Spawning
{
    public struct SpawnConfig : IComponentData
    {
        public const float DefaultSpawnRadius = 40f;
        public const float DefaultSpawnRate = 200f;
        public const int DefaultMaxEnemies = 20000;
        public const uint DefaultRandomSeed = 1u;

        public Entity EnemyPrefab;
        public float SpawnRadius;
        public float SpawnRate;
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
                SpawnRate = math.max(0f, spawnRate),
                MaxEnemies = math.max(0, maxEnemies),
                RandomSeed = randomSeed
            };
        }
    }

    public struct SpawnState : IComponentData
    {
        public float SpawnBudget;
        public uint SpawnSequence;
    }
}
