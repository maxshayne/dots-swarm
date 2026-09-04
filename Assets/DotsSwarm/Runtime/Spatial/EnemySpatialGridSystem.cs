using DotsSwarm.Gameplay;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Spatial
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EnemyMovementSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct EnemySpatialGridSystem : ISystem
    {
        // A fixed cell size keeps every grid consumer on the same spatial partition.
        public const float CellSize = 2f;
        public const int NeighborCellCount = 9;

        private const int InitialCapacity = 64;

        private EntityQuery enemyQuery;
        private NativeParallelMultiHashMap<int2, Entity> grid;
        private JobHandle gridDependency;
        private int capacity;

        public NativeParallelMultiHashMap<int2, Entity>.ReadOnly Grid => grid.AsReadOnly();
        public int Capacity => capacity;

        // Consumers must combine this handle into their work and assign the resulting
        // handle back so the next clear waits for every grid reader.
        public JobHandle GridDependency
        {
            get => gridDependency;
            set => gridDependency = value;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            enemyQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Enemy, LocalTransform>()
                .WithNone<Prefab>()
                .Build(ref state);

            capacity = InitialCapacity;
            grid = new NativeParallelMultiHashMap<int2, Entity>(
                capacity,
                Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (!grid.IsCreated)
            {
                return;
            }

            var disposeDependency = JobHandle.CombineDependencies(
                state.Dependency,
                gridDependency);
            state.Dependency = grid.Dispose(disposeDependency);
            gridDependency = state.Dependency;
            capacity = 0;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var inputDependency = JobHandle.CombineDependencies(
                state.Dependency,
                gridDependency);
            var enemyCount = enemyQuery.CalculateEntityCount();
            var resizeDependency = inputDependency;

            if (enemyCount > capacity)
            {
                capacity = CalculateGrowthCapacity(capacity, enemyCount);
                resizeDependency = new ResizeGridJob
                {
                    Grid = grid,
                    Capacity = capacity
                }.Schedule(inputDependency);
            }

            var clearDependency = new ClearGridJob
            {
                Grid = grid
            }.Schedule(resizeDependency);

            var buildDependency = new BuildGridJob
            {
                Grid = grid.AsParallelWriter(),
                CellSize = CellSize
            }.ScheduleParallel(enemyQuery, clearDependency);

            state.Dependency = buildDependency;
            gridDependency = buildDependency;
        }

        public static int2 PositionToCell(float3 position, float cellSize)
        {
            return (int2)math.floor(position.xz / cellSize);
        }

        public static int2 GetNeighborCell(int2 center, int index)
        {
            return center + new int2(index % 3 - 1, index / 3 - 1);
        }

        private static int CalculateGrowthCapacity(int currentCapacity, int requiredCapacity)
        {
            var grownCapacity = math.max(1, currentCapacity);
            while (grownCapacity < requiredCapacity)
            {
                if (grownCapacity > NativeParallelMultiHashMap<int2, Entity>.MaxCapacity / 2)
                {
                    return NativeParallelMultiHashMap<int2, Entity>.MaxCapacity;
                }

                grownCapacity *= 2;
            }

            return grownCapacity;
        }

        [BurstCompile]
        private struct ResizeGridJob : IJob
        {
            public NativeParallelMultiHashMap<int2, Entity> Grid;
            public int Capacity;

            public void Execute()
            {
                Grid.Capacity = Capacity;
            }
        }

        [BurstCompile]
        private struct ClearGridJob : IJob
        {
            public NativeParallelMultiHashMap<int2, Entity> Grid;

            public void Execute()
            {
                Grid.Clear();
            }
        }

        [BurstCompile]
        private partial struct BuildGridJob : IJobEntity
        {
            public NativeParallelMultiHashMap<int2, Entity>.ParallelWriter Grid;
            public float CellSize;

            private void Execute(Entity entity, in LocalTransform transform)
            {
                Grid.Add(PositionToCell(transform.Position, CellSize), entity);
            }
        }
    }
}

