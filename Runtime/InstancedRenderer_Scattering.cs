using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marinade.InstancedRendering
{
    public partial class InstancedRenderer
    {
        public int Scatter(Ray ray, RaycastHit hit, InstanceScatteringBrush brush, Action<Matrix4x4> perInstanceAddedOrModified = null)
        {
            int prevInstanceCount = _instanceData?.InstanceCount ?? 0;
            var scatterDistance = Mathf.Max(m_Settings.minScatterDistance, brush.scatterDistance);
            var xAxis = Vector3.Cross(hit.normal, Mathf.Abs(hit.normal.y) < 0.99F ? Vector3.up : Vector3.right).normalized;
            var yAxis = Vector3.Cross(hit.normal, xAxis);
            var raycastCommands = new NativeList<RaycastCommand>(256, Allocator.TempJob);
            for (float x = -brush.outerRadius; x < brush.outerRadius; x += scatterDistance)
            {
                float circleRelative = Mathf.Abs(x / brush.outerRadius);
                float circleSlice = Mathf.Sqrt(1F - circleRelative * circleRelative) * brush.outerRadius;
                for (float y = -circleSlice; y < circleSlice; y += scatterDistance)
                {
                    var targetPoint = hit.point + xAxis * x + yAxis * y;
                    raycastCommands.Add(new RaycastCommand(ray.origin, targetPoint - ray.origin, new QueryParameters(brush.layerMask, false, QueryTriggerInteraction.Ignore)));
                    // TODO replace with RaycastCommand
                    if (!Physics.Raycast(ray.origin, targetPoint - ray.origin, out var scatterHit, hit.distance + brush.outerRadius, brush.layerMask,
                            QueryTriggerInteraction.Ignore) || (brush.requireSameCollider && scatterHit.collider != hit.collider)
                        || scatterHit.normal.y < brush.normalLimit) continue;
                            
                    var existingInstance = GetFirstOverlappingInstance(scatterHit.point, scatterRadius);
                    if (existingInstance >= 0)
                    {
                    }

                }
            }
            var results = new NativeArray<RaycastHit>(raycastCommands.Length, Allocator.TempJob);
            var raycastHandle = RaycastCommand.ScheduleBatch(raycastCommands.AsDeferredJobArray(), results, 1, 1);
            raycastHandle.Complete();
            var overlapSamples = new NativeList<float4>(results.Length, Allocator.TempJob);
            for (int result = 0; result < results.Length; ++result)
            {
                var scatterHit = results[result];
                if ((brush.requireSameCollider && scatterHit.colliderEntityId != hit.colliderEntityId) ||
                    scatterHit.normal.y < brush.normalLimit) continue;
                float falloffFactor = brush.innerRadius >= brush.outerRadius ? 1F : 1F - Mathf.Clamp01(Vector3.Distance(scatterHit.point, hit.point) - brush.innerRadius) / (brush.outerRadius - brush.innerRadius);
                var scatterRadius = scatterDistance *
                                    Mathf.LerpUnclamped(brush.falloffScatteringMultiplier, 1F, falloffFactor);
                var noise = brush.noiseFrequency > 0F
                    ? Mathf.PerlinNoise(hit.point.x * brush.noiseFrequency,
                        hit.point.z * brush.noiseFrequency)
                    : 0F;
                if (brush.noiseScatteringVariation > 0F)
                    scatterRadius += noise * brush.noiseScatteringVariation;
                results[overlapSamples.Length] = scatterHit;
                overlapSamples.Add(new float4(hit.point, scatterRadius));
            }

            // Overlap Sphere
            var overlapIndices = new NativeArray<int>(overlapSamples.Length, Allocator.TempJob);
            if (_instanceData == null || _instanceData.InstanceCount == 0)
            {
                overlapIndices.FillArray(-1);
            }
            else
            {
                var overlapJob = new GetFirstOverlappingInstanceSpheresJob()
                {
                    in_SpatialHashingFactor = _instanceData.SpatialHashingFactor,
                    in_SpatialCells = _instanceData.SpatialCells,
                    in_Instances = _instanceData.Instances,
                    in_SampleSpheres = overlapSamples.AsDeferredJobArray(),
                    out_Indices = overlapIndices,
                };
                var overlapHandle = overlapJob.Schedule();
                overlapHandle.Complete();
            }

            for (int i = overlapIndices.Length - 1; i >= 0; i--)
            {
                var scatterHit = results[i];
                float falloffFactor = brush.innerRadius >= brush.outerRadius ? 1F : 1F - Mathf.Clamp01(Vector3.Distance(scatterHit.point, hit.point) - brush.innerRadius) / (brush.outerRadius - brush.innerRadius);
                var noise = brush.noiseFrequency > 0F
                    ? Mathf.PerlinNoise(hit.point.x * brush.noiseFrequency,
                        hit.point.z * brush.noiseFrequency)
                    : 0F;
                
                if (overlapIndices[i] >= 0)
                {
                    var m = _instanceData.Instances[overlapIndices[i]];
                    var position = m.GetPosition();
                    var desiredScale = brush.GetInstanceScale(position, noise, falloffFactor);
                    if (math.lengthsq(m.GetScale()) >= desiredScale.sqrMagnitude) continue;
                    m.matrix = float4x4.TRS(position, m.GetRotation(), 
                        desiredScale);
                    ReplaceInstance(overlapIndices[i], m.matrix);
                    perInstanceAddedOrModified?.Invoke(m.matrix);
                    continue;
                }
                var rot = brush.GetInstanceRotation(ray, scatterHit);
                var instance = Matrix4x4.TRS(scatterHit.point + rot * brush.pivot,
                    rot,
                    brush.GetInstanceScale(scatterHit.point, noise, falloffFactor));
                AddInstance(instance);
                perInstanceAddedOrModified?.Invoke(instance);
                
            }

            return _instanceData.InstanceCount - prevInstanceCount;
        }
        
        
        public int RemoveSphere(Vector3 position, float radius, Space transformSpace = Space.World, Action<Matrix4x4> perInstanceRemoved = null)
        {
            if (transformSpace == Space.World && m_Settings.transformSpace == Space.Self)
                position = transform.InverseTransformPoint(position);
            else if (transformSpace == Space.Self && m_Settings.transformSpace == Space.World)
                position = transform.TransformPoint(position);
            
            int prevInstanceCount = _instanceData.Count;
            float radiusSqr = radius * radius;

            var hashCell = GetHashCell(position);
            var radiusInt = Mathf.CeilToInt(radius * _spatialHashingFactor);
            for (int x = -radiusInt; x <= radiusInt; x++)
            {
                //float sphereRelative = Mathf.Abs(x / radius);
                //float sphereSlice = Mathf.Sqrt(1F - sphereRelative * sphereRelative) * radius;
                for (int y = -radiusInt; y <= radiusInt; y++)
                {
                    for (int z = -radiusInt; z <= radiusInt; z++)
                    {
                        var spatialIndex = GetSpatialIndex(hashCell + new Vector3Int(x, y, z));
                        ref var spatialCell = ref _spatialHashTable[spatialIndex];
                        for (int i = spatialCell.Pointer; i < spatialCell.Pointer + spatialCell.Count; i++)
                        {
                            var m = _instanceData[i];
                            if ((m.matrix.GetPosition() - position).sqrMagnitude > radiusSqr) continue;
                            RemoveInstance(i);
                            perInstanceRemoved?.Invoke(m.matrix);
                        }
                    }
                }
            }
            return prevInstanceCount - _instanceData.Count;
        }
    }
}