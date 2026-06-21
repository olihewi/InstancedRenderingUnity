using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marinade.InstancedRendering
{
    public class InstancingData
    {
        public const ushort SPATIAL_CELL_ARRAY_SIZE = 1024;
        public const int CURRENT_SERIALIZED_VERSION = 1;
        private static readonly int _PerInstanceData = Shader.PropertyToID("_PerInstanceData");
        
        public float SpatialHashingFactor;
        public bool IsCPUDirty;
        public int InstanceCount;
        public float3 boundsMin, boundsMax;
        
        public NativeArray<SpatialCell> SpatialCells;
        public NativeList<Instance> Instances;
        public NativeList<ushort> ActiveHashIndices;

        public ComputeBuffer InstancesBuffer;

        public InstancingData(float spatialHashingFactor)
        {
            this.SpatialHashingFactor = spatialHashingFactor;
        }

        ~InstancingData()
        {
            UnloadCPU();
            UnloadGPU();
        }

    #region Util
        
        public static ushort GetSpatialIndex(int3 cell)
        {
            int hashCode = (cell.x * 92837111) ^ (cell.y * 689287499) ^ (cell.z * 283923481);
            return (ushort)(math.abs(hashCode) % SPATIAL_CELL_ARRAY_SIZE);
        }
        public static int3 GetSpatialCell(float3 position, float spatialHashingFactor)
        {
            return (int3)(position * spatialHashingFactor);
        }
        public static ushort GetSpatialIndex(float3 position, float spatialHashingFactor)
        {
            return GetSpatialIndex(GetSpatialCell(position, spatialHashingFactor));
        }
        
        public int3 GetSpatialCell(float3 position) => GetSpatialCell(position, SpatialHashingFactor);
        public ushort GetSpatialIndex(float3 position) => GetSpatialIndex(position, SpatialHashingFactor);

    #endregion

    #region GPU
        
        public bool UploadGPU(MaterialPropertyBlock propertyBlock)
        {
            IsCPUDirty = false;
            if (!Instances.IsCreated || Instances.IsEmpty) return false;
            if (InstancesBuffer == null || InstancesBuffer.count != Instances.Capacity)
            {
                InstancesBuffer?.Release();
                InstancesBuffer = new ComputeBuffer(Instances.Capacity, sizeof(float) * 20, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            }
            var arr = InstancesBuffer.BeginWrite<Instance>(0, InstanceCount);
            for (int i = InstanceCount - 1; i >= 0; --i)
            {
                arr[i] = Instances[i];
            }
            InstancesBuffer.EndWrite<Instance>(InstanceCount);
            propertyBlock.SetBuffer(_PerInstanceData, InstancesBuffer);
            return true;
        }

        /*public bool DownloadGPUAlloc()
        {
            if (InstancesBuffer == null || !InstancesBuffer.IsValid()) return false;
            if (!Instances.IsCreated)
            {
                Instances = new NativeList<Instance>(InstanceCount, Allocator.Persistent);
            }
            else if (Instances.Capacity < InstanceCount)
            {
                Instances.SetCapacity(InstanceCount);
            }
            var tmp = new Instance[InstanceCount];
            InstancesBuffer.GetData(tmp);
            for (int i = InstanceCount - 1; i >= 0; --i)
            {
                Instances[i] = tmp[i];
            }
        }*/

        public void UnloadGPU()
        {
            InstancesBuffer?.Dispose();
        }

    #endregion

    #region CPU Modification
        
        public void AddInstance(float4x4 matrix)
        {
            if (!Instances.IsCreated) InitializeCPU();
            var instance = Instance.CreateWithSampledProbe(matrix);
            float3 position = instance.GetPosition();
            ushort spatialIndex = GetSpatialIndex(position);
            if (InstanceCount == 0) boundsMin = boundsMax = position;
            else
            {
                boundsMin = math.min(boundsMin, position);
                boundsMax = math.max(boundsMax, position);
            }
            Instances.Add(instance);
            InstanceCount++;
            IsCPUDirty = true;
            
            var spatialCell = SpatialCells[spatialIndex];
            // If there are no other elements in this spatial hash, add it to the end of the instance list.
            if (spatialCell.Count == 0)
            {
                spatialCell.Pointer = InstanceCount - 1;
                spatialCell.Count = 1;
                SpatialCells[spatialIndex] = spatialCell;
                ActiveHashIndices.Add(spatialIndex);
                return;
            }
            
            // Else move subsequent hash cells along and update their pointers
            int activeIndex = ActiveHashIndices.IndexOf(spatialIndex);
            int swapIndex = InstanceCount - 1;
            for (int i = ActiveHashIndices.Length - 1; i > activeIndex; --i)
            {
                var swapCell = SpatialCells[ActiveHashIndices[i]];
                Instances[swapIndex] = Instances[swapIndex = swapCell.Pointer++];
                SpatialCells[ActiveHashIndices[i]] = swapCell;
            }
            Instances[swapIndex] = instance;
            spatialCell.Count++;
            SpatialCells[spatialIndex] = spatialCell;
            IsCPUDirty = true;
        }
        
        public void RemoveInstance(int index)
        {
            // Assume instances is created and index is valid
            var instance = Instances[index];
            ushort spatialIndex = GetSpatialIndex(instance.GetPosition());
            int activeIndex = ActiveHashIndices.IndexOf(spatialIndex);
            // Swap to back of spatial cell
            var modifiedCell = SpatialCells[spatialIndex];
            int swapIndex = modifiedCell.Pointer + modifiedCell.Count - 1;
            Instances[index] = Instances[swapIndex];
            // Move subsequent hash cells back and update their pointers
            int activeHashIndicesLength = ActiveHashIndices.Length;
            for (int i = activeIndex + 1; i < activeHashIndicesLength; i++)
            {
                var nextCell = SpatialCells[ActiveHashIndices[i]];
                int lastIndexInNextCell = --nextCell.Pointer + nextCell.Count;
                SpatialCells[ActiveHashIndices[i]] = nextCell;
                Instances[swapIndex] = Instances[lastIndexInNextCell];
                swapIndex = lastIndexInNextCell;
            }
            // Remove from active hash indices if cell is empty
            if (--modifiedCell.Count <= 0)
            {
                ActiveHashIndices.RemoveAt(activeIndex);
            }

            SpatialCells[spatialIndex] = modifiedCell;
            Instances.RemoveAt(index);
            InstanceCount--;
            IsCPUDirty = true;
        }

        public void ReplaceInstance(int index, float4x4 matrix)
        {
            // Assume instances is created and index is valid
            // Could optimise if in same spatial cell
            RemoveInstance(index);
            AddInstance(matrix);
            IsCPUDirty = true;
        }


        public void UpdateLightProbes() => UpdateLightProbes(0, InstanceCount);
        public void UpdateLightProbes(int startIndex, int count)
        {
            int max = startIndex + count;
            // Assume instances is created and indices are valid
            var positions = new Vector3[count];
            for (int i = max-1; i >= startIndex; --i)
            {
                positions[i-startIndex] = Instances[i].GetPosition();
            }
            var lightProbes = new SphericalHarmonicsL2[InstanceCount];
            var occlusionProbes = new Vector4[InstanceCount];
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(positions, lightProbes, occlusionProbes);
            for (int i = max-1; i >= startIndex; --i)
            {
                var instance = Instances[i];
                Instance.LIGHT_PROBE_SAMPLE_VECTOR[0] = math.mul(instance.GetRotation(), new float3(0, 1, 0));
                lightProbes[i-startIndex].Evaluate(Instance.LIGHT_PROBE_SAMPLE_VECTOR, Instance.LIGHT_PROBE_EVALUATION_RESULT);
                instance.probeColor = new float4(Instance.LIGHT_PROBE_EVALUATION_RESULT[0].r,
                    Instance.LIGHT_PROBE_EVALUATION_RESULT[0].g, Instance.LIGHT_PROBE_EVALUATION_RESULT[0].b,
                    Instance.LIGHT_PROBE_EVALUATION_RESULT[0].a);
            }
            IsCPUDirty = true;
        }

    #endregion

    #region CPU Loading
        
        public void InitializeCPU(int instanceCapacity = 64, int activeHashesCapacity = 64)
        {
            UnloadCPU();
            Instances = new NativeList<Instance>(instanceCapacity, Allocator.Persistent);
            BuildSpatialCellsFromOrderedData(activeHashesCapacity);
        }
        public void BuildSpatialCellsFromOrderedData(int activeHashIndicesCapacity = 64)
        {
            if (SpatialCells.IsCreated) SpatialCells.Dispose();
            if (ActiveHashIndices.IsCreated) ActiveHashIndices.Dispose();
            
            SpatialCells = new NativeArray<SpatialCell>(SPATIAL_CELL_ARRAY_SIZE, Allocator.Persistent);
            ActiveHashIndices = new NativeList<ushort>(activeHashIndicesCapacity, Allocator.Persistent);
            if (!Instances.IsCreated) return;
            int prevSpatialIndex = -1;
            for (int i = 0; i < Instances.Length; i++)
            {
                int spatialIndex = GetSpatialIndex(Instances[i].GetPosition());
                if (prevSpatialIndex != spatialIndex)
                {
                    var cell = SpatialCells[spatialIndex];
                    cell.Pointer = i;
                    cell.Count++;
                    SpatialCells[spatialIndex] = cell;
                    ActiveHashIndices.Add((ushort)spatialIndex);
                    prevSpatialIndex = spatialIndex;
                }
                else
                {
                    var cell = SpatialCells[spatialIndex];
                    cell.Count++;
                    SpatialCells[spatialIndex] = cell;
                }
            }
        }
        public void UnloadCPU()
        {
            if (Instances.IsCreated) Instances.Dispose();
            if (SpatialCells.IsCreated) SpatialCells.Dispose();
            if (ActiveHashIndices.IsCreated) ActiveHashIndices.Dispose();
            InstanceCount = 0;
        }

        public void RebuildInstanceData()
        {
            if (!Instances.IsCreated) return;
            var newInstances = new NativeList<Instance>(Instances.Capacity, Allocator.Persistent);
            for (int i = 0; i < InstanceCount; i++)
            {
                newInstances.Add(Instances[i]);
            }
            UnloadCPU();
            BuildSpatialCellsFromOrderedData();
            for (int i = 0; i < InstanceCount; i++)
            {
                AddInstance(newInstances[i].matrix);
            }
        }

        private BinaryReader GetBinaryReader(Stream stream, out int serializedVersion)
        {
            using var versionReader = new BinaryReader(stream);
            serializedVersion = versionReader.ReadInt32();
            if (serializedVersion < 0)
                return new BinaryReader(new GZipStream(stream, CompressionMode.Decompress, false));
            return new BinaryReader(stream);
        }
        public bool LoadFromBytes(Stream stream)
        {
            try
            {
                using var br = GetBinaryReader(stream, out int serializedVersion);
                int activeHashIndicesCount = br.ReadInt32();
                var bounds = new Bounds(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                boundsMin = bounds.min;
                boundsMax = bounds.max;
                InstanceCount = br.ReadInt32();
                UnloadCPU();
                Instances = new NativeList<Instance>(InstanceCount, Allocator.Persistent);
                for (int i = 0; i < InstanceCount; ++i)
                {
                    Instances.Add(new Instance{matrix = new float4x4(
                        new float4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        new float4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        new float4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                        new float4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()))});
                }
                BuildSpatialCellsFromOrderedData(activeHashIndicesCount);
                UpdateLightProbes();
                IsCPUDirty = true;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        public int GetByteSize() => sizeof(int) * 3 + sizeof(float) * 6 + math.min(0,InstanceCount) * sizeof(float) * 16;

        private BinaryWriter GetBinaryWriterAndWriteVersion(Stream stream, bool compressed)
        {
            if (compressed)
            {
                using var versionWriter = new BinaryWriter(stream);
                versionWriter.Write(-CURRENT_SERIALIZED_VERSION);
                return new BinaryWriter(new GZipStream(stream, CompressionMode.Compress, false));
            }
            var writer = new BinaryWriter(stream);
            writer.Write(CURRENT_SERIALIZED_VERSION);
            return writer;
        }

        public bool WriteBytes(Stream stream, bool compressed)
        {
            try
            {
                using var bw = GetBinaryWriterAndWriteVersion(stream, compressed);
                bw.Write(ActiveHashIndices.IsCreated ? ActiveHashIndices.Length : 0);
                var bounds = new Bounds();
                bounds.SetMinMax(boundsMin, boundsMax);
                bw.Write(bounds.center.x);
                bw.Write(bounds.center.y);
                bw.Write(bounds.center.z);
                bw.Write(bounds.size.x);
                bw.Write(bounds.size.y);
                bw.Write(bounds.size.z);
                bw.Write(InstanceCount);
                if (!Instances.IsCreated || InstanceCount <= 0) return true;
                for (int i = 0; i < InstanceCount; ++i)
                {
                    var m = Instances[i].matrix;
                    bw.Write(m.c0.x);
                    bw.Write(m.c0.y);
                    bw.Write(m.c0.z);
                    bw.Write(m.c0.w);
                    bw.Write(m.c1.x);
                    bw.Write(m.c1.y);
                    bw.Write(m.c1.z);
                    bw.Write(m.c1.w);
                    bw.Write(m.c2.x);
                    bw.Write(m.c2.y);
                    bw.Write(m.c2.z);
                    bw.Write(m.c2.w);
                    bw.Write(m.c3.x);
                    bw.Write(m.c3.y);
                    bw.Write(m.c3.z);
                    bw.Write(m.c3.w);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

    #endregion
    }
    
}