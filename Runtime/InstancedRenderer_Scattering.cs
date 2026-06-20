using System;
using UnityEngine;

namespace Marinade.InstancedRendering
{
    public partial class InstancedRenderer
    {
        public int Scatter(Ray ray, RaycastHit hit, ScatteringBrush brush, Action<Matrix4x4> perInstanceAddedOrModified = null)
        {
            int prevInstanceCount = _instances?.Count ?? 0;
            var scatterDistance = Mathf.Max(m_Settings.minScatterDistance, brush.scatterDistance);
            var xAxis = Vector3.Cross(hit.normal, Mathf.Abs(hit.normal.y) < 0.99F ? Vector3.up : Vector3.right).normalized;
            var yAxis = Vector3.Cross(hit.normal, xAxis);
            for (float x = -brush.outerRadius; x < brush.outerRadius; x += scatterDistance)
            {
                float circleRelative = Mathf.Abs(x / brush.outerRadius);
                float circleSlice = Mathf.Sqrt(1F - circleRelative * circleRelative) * brush.outerRadius;
                for (float y = -circleSlice; y < circleSlice; y += scatterDistance)
                {
                    var targetPoint = hit.point + xAxis * x + yAxis * y;
                    // TODO replace with RaycastCommand
                    if (!Physics.Raycast(ray.origin, targetPoint - ray.origin, out var scatterHit, hit.distance + brush.outerRadius, brush.layerMask,
                            QueryTriggerInteraction.Ignore) || (brush.requireSameCollider && scatterHit.collider != hit.collider)
                        || scatterHit.normal.y < brush.normalLimit) continue;
                            
                    float falloffFactor = brush.innerRadius >= brush.outerRadius ? 1F : 1F - Mathf.Clamp01(Vector3.Distance(scatterHit.point, hit.point) - brush.innerRadius) / (brush.outerRadius - brush.innerRadius);
                    var scatterRadius = scatterDistance *
                                        Mathf.LerpUnclamped(brush.falloffScatteringMultiplier, 1F, falloffFactor);
                    var noise = brush.noiseFrequency > 0F
                        ? Mathf.PerlinNoise(targetPoint.x * brush.noiseFrequency,
                            targetPoint.y * brush.noiseFrequency)
                        : 0F;
                    if (brush.noiseScatteringVariation > 0F)
                        scatterRadius += noise * brush.noiseScatteringVariation;
                    var existingInstance = GetFirstOverlappingInstance(scatterHit.point, scatterRadius);
                    if (existingInstance >= 0)
                    {
                        var m = _instances[existingInstance].matrix;
                        var position = m.GetPosition();
                        var desiredScale = brush.GetInstanceScale(position, noise, falloffFactor);
                        if (m.lossyScale.sqrMagnitude >= desiredScale.sqrMagnitude) continue;
                        m = Matrix4x4.TRS(position, m.rotation, 
                            desiredScale);
                        ReplaceInstance(existingInstance, m);
                        perInstanceAddedOrModified?.Invoke(m);
                        continue;
                    }

                    var instance = Matrix4x4.TRS(scatterHit.point,
                        brush.GetInstanceRotation(ray, scatterHit),
                        brush.GetInstanceScale(scatterHit.point, noise, falloffFactor));
                    AddInstance(instance);
                    perInstanceAddedOrModified?.Invoke(instance);
                }
            }
            return _instances.Count - prevInstanceCount;
        }
        
        
        public int RemoveSphere(Vector3 position, float radius, Space transformSpace = Space.World, Action<Matrix4x4> perInstanceRemoved = null)
        {
            if (transformSpace == Space.World && m_Settings.transformSpace == Space.Self)
                position = transform.InverseTransformPoint(position);
            else if (transformSpace == Space.Self && m_Settings.transformSpace == Space.World)
                position = transform.TransformPoint(position);
            
            int prevInstanceCount = _instances.Count;
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
                            var m = _instances[i];
                            if ((m.matrix.GetPosition() - position).sqrMagnitude > radiusSqr) continue;
                            RemoveInstance(i);
                            perInstanceRemoved?.Invoke(m.matrix);
                        }
                    }
                }
            }
            return prevInstanceCount - _instances.Count;
        }
    }
}