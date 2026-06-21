using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Marinade.InstancedRendering
{
    [BurstCompile]
    public struct GetFirstOverlappingInstanceSpheresJob : IJob
    {
        [ReadOnly] public float in_SpatialHashingFactor;
        [ReadOnly] public NativeArray<SpatialCell> in_SpatialCells;
        [ReadOnly] public NativeList<Instance> in_Instances;
        
        [ReadOnly] public NativeArray<float4> in_SampleSpheres; // w = radius
        
        [WriteOnly] public NativeArray<int> out_Indices;

        public void Execute()
        {
            int sampleCount = in_SampleSpheres.Length;
            
            for (int sampleIndex = sampleCount - 1; sampleCount >= 0; --sampleCount)
            {
                out_Indices[sampleIndex] = GetFirstOverlappingInstanceForSample(sampleIndex);
            }
        }

        private int GetFirstOverlappingInstanceForSample(int sampleIndex)
        {
            float3 position = in_SampleSpheres[sampleIndex].xyz;
            float radius = in_SampleSpheres[sampleIndex].w;
            float radiusSqr = radius * radius;
            int3 cell = InstancingData.GetSpatialCell(position, in_SpatialHashingFactor);
            int maxCellRadius = (int)math.ceil(radius * in_SpatialHashingFactor) + 1;
            float invMaxCellRadius = 1 / (float)maxCellRadius;
            int result = -1;
            
            for (int x = 0; x <= maxCellRadius; x++)
            {
                float sphereRelative = x * invMaxCellRadius;
                int sphereSlice = (int)math.ceil(math.sqrt(1F - sphereRelative * sphereRelative));
                for (int y = 0; y <= sphereSlice; y++)
                {
                    for (int z = 0; z <= sphereSlice; z++)
                    {
                        result = GetFirstOverlappingInstanceInCell(cell + new int3(x, y, z), position, radiusSqr);
                        if (result >= 0) return result;
                        if (x != 0)
                        {
                            result = GetFirstOverlappingInstanceInCell(cell + new int3(-x, y, z), position, radiusSqr);
                            if (result >= 0) return result;
                            if (y != 0)
                            {
                                result = GetFirstOverlappingInstanceInCell(cell + new int3(-x, -y, z), position, radiusSqr);
                                if (result >= 0) return result;
                                if (z == 0) continue;
                                result = GetFirstOverlappingInstanceInCell(cell + new int3(-x, -y, -z), position, radiusSqr);
                                if (result >= 0) return result;
                            }
                            else if (z != 0)
                            {
                                result = GetFirstOverlappingInstanceInCell(cell + new int3(-x, y, -z), position, radiusSqr);
                                if (result >= 0) return result;
                            }
                        }
                        else if (y != 0)
                        {
                            result = GetFirstOverlappingInstanceInCell(cell + new int3(x, -y, z), position, radiusSqr);
                            if (result >= 0) return result;
                            if (z == 0) continue;
                            result = GetFirstOverlappingInstanceInCell(cell + new int3(x, -y, -z), position, radiusSqr);
                            if (result >= 0) return result;
                        }
                        else if (z != 0)
                        {
                            result = GetFirstOverlappingInstanceInCell(cell + new int3(x, y, -z), position, radiusSqr);
                            if (result >= 0) return result;
                        }
                    }
                }
            }
            return result;
        }

        private int GetFirstOverlappingInstanceInCell(int3 cell, float3 position, float radiusSqr)
        {
            ushort spatialIndex = InstancingData.GetSpatialIndex(cell);
            int ptrMax = in_SpatialCells[spatialIndex].Pointer + in_SpatialCells[spatialIndex].Count;
            for (int i = in_SpatialCells[spatialIndex].Pointer; i < ptrMax; i++)
            {
                if (math.lengthsq(in_Instances[i].GetPosition() - position) < radiusSqr) return i;
            }
            return -1;
        }
    }
}