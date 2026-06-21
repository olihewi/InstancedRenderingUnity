using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Marinade.InstancedRendering
{
    [BurstCompile]
    public struct RemoveSpheresJob : IJob
    {
        [ReadOnly] public float in_SpatialHashingFactor;
        [ReadOnly] public NativeArray<>
        public NativeArray<SpatialCell> inout_SpatialCells;
        public NativeList<Instance> inout_Instances;
        public NativeList<ushort> inout_ActiveHashIndices;
        public void Execute()
        {
            throw new System.NotImplementedException();
        }
    }
}