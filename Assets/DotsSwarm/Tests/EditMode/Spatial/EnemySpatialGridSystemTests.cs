using System.Collections.Generic;
using DotsSwarm.Gameplay;
using DotsSwarm.Spatial;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Tests.Spatial
{
    public sealed class EnemySpatialGridSystemTests
    {
        [Test]
        public void PositionToCell_FloorsNegativeCoordinates()
        {
            var cellSize = EnemySpatialGridSystem.CellSize;

            var cell = EnemySpatialGridSystem.PositionToCell(
                new float3(-0.01f, 100f, -cellSize - 0.01f),
                cellSize);

            Assert.That(cellSize, Is.GreaterThan(0f));
            Assert.That(cell, Is.EqualTo(new int2(-1, -2)));
        }

        [Test]
        public void GetNeighborCell_ReturnsEveryCellInThreeByThreeArea()
        {
            var center = new int2(5, -3);
            var actual = new List<int2>(EnemySpatialGridSystem.NeighborCellCount);
            var expected = new List<int2>(9);

            for (var zOffset = -1; zOffset <= 1; zOffset++)
            {
                for (var xOffset = -1; xOffset <= 1; xOffset++)
                {
                    expected.Add(center + new int2(xOffset, zOffset));
                }
            }

            for (var index = 0; index < EnemySpatialGridSystem.NeighborCellCount; index++)
            {
                actual.Add(EnemySpatialGridSystem.GetNeighborCell(center, index));
            }

            Assert.That(EnemySpatialGridSystem.NeighborCellCount, Is.EqualTo(9));
            Assert.That(actual, Is.EquivalentTo(expected));
        }

        [Test]
        public void System_GroupsMultipleEnemiesInTheSameCell()
        {
            using var world = new World("Enemy spatial grid grouping test");
            var entityManager = world.EntityManager;
            var cellSize = EnemySpatialGridSystem.CellSize;
            var sharedPosition = new float3(cellSize * 0.25f, 0.4f, cellSize * 0.25f);
            var firstEnemy = CreateEnemy(entityManager, sharedPosition);
            var secondEnemy = CreateEnemy(entityManager, sharedPosition + new float3(0.1f, 0f, 0.1f));
            var otherEnemy = CreateEnemy(
                entityManager,
                new float3(cellSize * 1.25f, 0.4f, cellSize * 0.25f));
            var systemHandle = world.GetOrCreateSystem<EnemySpatialGridSystem>();

            UpdateSystem(world, systemHandle);

            ref var system = ref world.Unmanaged.GetUnsafeSystemRef<EnemySpatialGridSystem>(
                systemHandle);
            var grid = system.Grid;
            var sharedCell = EnemySpatialGridSystem.PositionToCell(sharedPosition, cellSize);
            var otherCell = EnemySpatialGridSystem.PositionToCell(
                entityManager.GetComponentData<LocalTransform>(otherEnemy).Position,
                cellSize);

            Assert.That(GetEntities(grid, sharedCell), Is.EquivalentTo(new[]
            {
                firstEnemy,
                secondEnemy
            }));
            Assert.That(GetEntities(grid, otherCell), Is.EquivalentTo(new[] { otherEnemy }));
            Assert.That(grid.Count(), Is.EqualTo(3));
        }

        [Test]
        public void System_RebuildsGridAfterEnemyMoves()
        {
            using var world = new World("Enemy spatial grid rebuild test");
            var entityManager = world.EntityManager;
            var cellSize = EnemySpatialGridSystem.CellSize;
            var oldPosition = new float3(cellSize * 0.25f, 0.4f, cellSize * 0.25f);
            var newPosition = new float3(cellSize * 2.25f, 0.4f, -cellSize * 1.25f);
            var enemy = CreateEnemy(entityManager, oldPosition);
            var systemHandle = world.GetOrCreateSystem<EnemySpatialGridSystem>();
            var oldCell = EnemySpatialGridSystem.PositionToCell(oldPosition, cellSize);
            var newCell = EnemySpatialGridSystem.PositionToCell(newPosition, cellSize);

            UpdateSystem(world, systemHandle);
            ref var initialSystem = ref world.Unmanaged.GetUnsafeSystemRef<EnemySpatialGridSystem>(
                systemHandle);
            Assert.That(GetEntities(initialSystem.Grid, oldCell), Is.EquivalentTo(new[] { enemy }));

            entityManager.SetComponentData(enemy, LocalTransform.FromPosition(newPosition));
            UpdateSystem(world, systemHandle);

            ref var rebuiltSystem = ref world.Unmanaged.GetUnsafeSystemRef<EnemySpatialGridSystem>(
                systemHandle);
            Assert.That(rebuiltSystem.Grid.ContainsKey(oldCell), Is.False);
            Assert.That(GetEntities(rebuiltSystem.Grid, newCell), Is.EquivalentTo(new[] { enemy }));
            Assert.That(rebuiltSystem.Grid.Count(), Is.EqualTo(1));
        }

        [Test]
        public void System_GrowsCapacityToFitEnemyCount()
        {
            using var world = new World("Enemy spatial grid capacity test");
            var entityManager = world.EntityManager;
            var firstEnemy = CreateEnemy(entityManager, float3.zero);
            var systemHandle = world.GetOrCreateSystem<EnemySpatialGridSystem>();

            UpdateSystem(world, systemHandle);
            ref var initialSystem = ref world.Unmanaged.GetUnsafeSystemRef<EnemySpatialGridSystem>(
                systemHandle);
            var initialCapacity = initialSystem.Capacity;
            var targetEnemyCount = initialCapacity + 17;
            var additionalEnemyCount = targetEnemyCount - 1;
            var enemyArchetype = entityManager.CreateArchetype(
                typeof(Enemy),
                typeof(LocalTransform));

            using var additionalEnemies = entityManager.CreateEntity(
                enemyArchetype,
                additionalEnemyCount,
                Allocator.Temp);

            for (var index = 0; index < additionalEnemies.Length; index++)
            {
                entityManager.SetComponentData(additionalEnemies[index], Enemy.Create(0f));
                entityManager.SetComponentData(
                    additionalEnemies[index],
                    LocalTransform.FromPosition(new float3(index + 1, 0f, 0f)));
            }

            UpdateSystem(world, systemHandle);

            ref var grownSystem = ref world.Unmanaged.GetUnsafeSystemRef<EnemySpatialGridSystem>(
                systemHandle);
            Assert.That(entityManager.Exists(firstEnemy), Is.True);
            Assert.That(grownSystem.Capacity, Is.GreaterThan(initialCapacity));
            Assert.That(grownSystem.Capacity, Is.GreaterThanOrEqualTo(targetEnemyCount));
            Assert.That(grownSystem.Grid.Count(), Is.EqualTo(targetEnemyCount));
        }

        private static Entity CreateEnemy(EntityManager entityManager, float3 position)
        {
            var enemy = entityManager.CreateEntity(typeof(Enemy), typeof(LocalTransform));
            entityManager.SetComponentData(enemy, Enemy.Create(0f));
            entityManager.SetComponentData(enemy, LocalTransform.FromPosition(position));
            return enemy;
        }

        private static void UpdateSystem(World world, SystemHandle systemHandle)
        {
            systemHandle.Update(world.Unmanaged);
            world.EntityManager.CompleteAllTrackedJobs();
        }

        private static List<Entity> GetEntities(
            NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
            int2 cell)
        {
            var entities = new List<Entity>();

            if (!grid.TryGetFirstValue(cell, out var entity, out var iterator))
            {
                return entities;
            }

            do
            {
                entities.Add(entity);
            }
            while (grid.TryGetNextValue(out entity, ref iterator));

            return entities;
        }
    }
}
