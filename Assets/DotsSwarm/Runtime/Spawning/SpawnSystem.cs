using DotsSwarm.Core;
using DotsSwarm.Gameplay;
using Unity.Burst;
using Unity.Collections;
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
            enemyQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Enemy>()
                .WithNone<Prefab>()
                .Build(ref state);

            state.RequireForUpdate<SpawnConfig>();
            state.RequireForUpdate<SpawnState>();
            state.RequireForUpdate<GameConfig>();
            state.RequireForUpdate<Player>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<GameSession>(out var session) && !session.CanSimulate)
                return;

            var config = SystemAPI.GetSingleton<SpawnConfig>();
            var spawnState = SystemAPI.GetSingletonRW<SpawnState>();
            var budget = spawnState.ValueRO.SpawnBudget;
            var isStress = SystemAPI.TryGetSingleton<BenchmarkState>(out var benchmark) && benchmark.IsStress;
            var enemyCount = enemyQuery.CalculateEntityCount();
            int spawnCount;
            if (isStress)
            {
                spawnCount = math.max(0, benchmark.TargetCount - enemyCount);
                budget = 0f;
            }
            else
            {
                var elapsed = spawnState.ValueRO.Elapsed;
                var next = elapsed + math.max(0f, SystemAPI.Time.DeltaTime);
                spawnCount = CalculateSpawnCount(
                    ref budget,
                    config.CalculateScheduledSpawns(next) - config.CalculateScheduledSpawns(elapsed),
                    enemyCount,
                    config.MaxEnemies);
                spawnState.ValueRW.Elapsed = next;
            }

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
                    isStress ? BenchmarkState.RandomSeed : config.RandomSeed,
                    sequence++);
                commandBuffer.SetComponent(enemy, transform);
                // Playback happens after TransformSystemGroup: without this the first
                // rendered frame uses the prefab's baked matrix and the enemy pops in at origin.
                commandBuffer.SetComponent(enemy, new LocalToWorld { Value = transform.ToMatrix() });
            }

            spawnState.ValueRW.SpawnSequence = sequence;
        }

        public static int CalculateSpawnCount(
            ref float spawnBudget,
            float scheduledSpawns,
            int currentEnemyCount,
            int maxEnemies)
        {
            var availableCapacity = math.max(0, maxEnemies - math.max(0, currentEnemyCount));
            if (availableCapacity == 0)
            {
                spawnBudget = 0f;
                return 0;
            }

            var accumulatedBudget = math.max(0f, spawnBudget) + math.max(0f, scheduledSpawns);
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
            // Mirror an axis that leaves the arena: clamping alone would place wall
            // spawns next to (or on) a player standing at the wall.
            offset = math.select(offset, -offset, math.abs(center.xz + offset) > safeHalfExtents);
            // The clamp remains for arenas narrower than the spawn radius.
            var position = math.clamp(
                center.xz + offset,
                -safeHalfExtents,
                safeHalfExtents);
            return new float3(position.x, height, position.y);
        }
    }
}
