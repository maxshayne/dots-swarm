using Unity.Entities;
using UnityEngine;

namespace DotsSwarm.Spawning
{
    [DisallowMultipleComponent]
    public sealed class SpawnConfigAuthoring : MonoBehaviour
    {
        [SerializeField]
        private GameObject enemyPrefab;

        [SerializeField]
        [Min(0f)]
        private float spawnRadius = SpawnConfig.DefaultSpawnRadius;

        [SerializeField, Min(0f), Tooltip("Enemies per second at session start.")]
        private float initialSpawnRate = SpawnConfig.DefaultInitialSpawnRate;

        [SerializeField, Min(0f), Tooltip("Enemies per second once the ramp completes.")]
        private float peakSpawnRate = SpawnConfig.DefaultPeakSpawnRate;

        [SerializeField, Min(0f), Tooltip("Session seconds from the initial to the peak rate; 0 keeps the initial rate.")]
        private float rampDuration = SpawnConfig.DefaultRampDuration;

        [SerializeField, Min(0f), Tooltip("Ramp shape: 1 is linear, higher values back-load the swarm.")]
        private float rampExponent = SpawnConfig.DefaultRampExponent;

        [SerializeField]
        [Min(0)]
        private int maxEnemies = SpawnConfig.DefaultMaxEnemies;

        [SerializeField]
        private uint randomSeed = SpawnConfig.DefaultRandomSeed;

        private void OnValidate()
        {
            spawnRadius = Mathf.Max(0f, spawnRadius);
            initialSpawnRate = Mathf.Max(0f, initialSpawnRate);
            peakSpawnRate = Mathf.Max(0f, peakSpawnRate);
            rampDuration = Mathf.Max(0f, rampDuration);
            rampExponent = Mathf.Max(0f, rampExponent);
            maxEnemies = Mathf.Max(0, maxEnemies);
        }

        private sealed class SpawnConfigBaker : Baker<SpawnConfigAuthoring>
        {
            public override void Bake(SpawnConfigAuthoring authoring)
            {
                if (authoring.enemyPrefab == null)
                {
                    return;
                }

                var entity = GetEntity(TransformUsageFlags.None);
                var prefabEntity = GetEntity(
                    authoring.enemyPrefab,
                    TransformUsageFlags.Dynamic);

                AddComponent(entity, SpawnConfig.Create(
                    prefabEntity,
                    authoring.spawnRadius,
                    authoring.initialSpawnRate,
                    authoring.maxEnemies,
                    authoring.randomSeed).WithRamp(
                    authoring.peakSpawnRate,
                    authoring.rampDuration,
                    authoring.rampExponent));
                AddComponent(entity, new SpawnState());
            }
        }
    }
}
