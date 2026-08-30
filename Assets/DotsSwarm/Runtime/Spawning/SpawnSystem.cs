using DotsSwarm.Core;
using DotsSwarm.Gameplay;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Spawning
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
    public partial struct SpawnSystem : ISystem
    {
        private EntityQuery enemyQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            enemyQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<Enemy>(),
                ComponentType.Exclude<Prefab>());

            state.RequireForUpdate<SpawnConfig>();
            state.RequireForUpdate<SpawnState>();
            state.RequireForUpdate<GameConfig>();
            state.RequireForUpdate<Player>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<SpawnConfig>();
            var spawnState = SystemAPI.GetSingletonRW<SpawnState>();
            var budget = spawnState.ValueRO.SpawnBudget;
            var spawnCount = CalculateSpawnCount(
                ref budget,
                config.SpawnRate,
                SystemAPI.Time.DeltaTime,
                enemyQuery.CalculateEntityCount(),
                config.MaxEnemies);

            spawnState.ValueRW.SpawnBudget = budget;
            if (spawnCount == 0)
            {
                return;
            }

            var playerEntity = SystemAPI.GetSingletonEntity<Player>();
            var spawnCenter = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
            var arenaHalfExtents = SystemAPI.GetSingleton<GameConfig>().ArenaHalfExtents;
            var prefabTransform = SystemAPI.GetComponent<LocalTransform>(config.EnemyPrefab);
            var sequence = spawnState.ValueRO.SpawnSequence;
            var commandBuffer = SystemAPI
                .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            for (var index = 0; index < spawnCount; index++)
            {
                var enemy = commandBuffer.Instantiate(config.EnemyPrefab);
                var transform = prefabTransform;
                transform.Position = CalculateSpawnPosition(
                    spawnCenter,
                    prefabTransform.Position.y,
                    config.SpawnRadius,
                    arenaHalfExtents,
                    config.RandomSeed,
                    sequence++);
                commandBuffer.SetComponent(enemy, transform);
            }

            spawnState.ValueRW.SpawnSequence = sequence;
        }

        public static int CalculateSpawnCount(
            ref float spawnBudget,
            float spawnRate,
            float deltaTime,
            int currentEnemyCount,
            int maxEnemies)
        {
            var availableCapacity = math.max(0, maxEnemies - math.max(0, currentEnemyCount));
            if (availableCapacity == 0)
            {
                spawnBudget = 0f;
                return 0;
            }

            var accumulatedBudget = math.max(0f, spawnBudget)
                                    + math.max(0f, spawnRate) * math.max(0f, deltaTime);
            var wholeBudget = (int)math.min(math.floor(accumulatedBudget), int.MaxValue);
            var spawnCount = math.min(wholeBudget, availableCapacity);
            spawnBudget = spawnCount == availableCapacity
                ? 0f
                : accumulatedBudget - spawnCount;
            return spawnCount;
        }

        public static float3 CalculateSpawnPosition(
            float3 center,
            float height,
            float spawnRadius,
            float2 arenaHalfExtents,
            uint randomSeed,
            uint spawnSequence)
        {
            var randomIndex = math.hash(new uint2(randomSeed, spawnSequence));
            randomIndex = randomIndex == uint.MaxValue ? 0u : randomIndex;
            var random = Random.CreateFromIndex(randomIndex);
            var angle = random.NextFloat(0f, 2f * math.PI);
            var offset = new float2(math.cos(angle), math.sin(angle))
                         * math.max(0f, spawnRadius);
            var safeHalfExtents = math.max(float2.zero, arenaHalfExtents);
            var position = math.clamp(
                center.xz + offset,
                -safeHalfExtents,
                safeHalfExtents);
            return new float3(position.x, height, position.y);
        }
    }
}
