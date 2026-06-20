using UnityEngine;

namespace Marinade.InstancedRendering
{
    [CreateAssetMenu(menuName = "Rendering/Instance Scattering Brush", order = 90000)]
    [Icon("Packages/com.marinade.instancedrendering/Editor/Icons/icon_ScatteringBrush.png")]
    public class InstanceScatteringBrush : ScriptableObject
    {
        // Brush
        public float noiseFrequency;
        [Range(0.01F, 25F)] public float outerRadius = 1F;
        [Range(0.01F, 15F)] public float innerRadius = 0.8F;
        
        // Scattering
        public float scatterDistance = 0.1F;
        [Range(1F, 8F)] public float falloffScatteringMultiplier = 1F;
        public float noiseScatteringVariation;
        
        // Placement Limits
        public LayerMask layerMask = Physics.DefaultRaycastLayers;
        public bool requireSameCollider;
        [Range(-1F, 1F)] public float normalLimit = -1F;

        // Position
        public Vector3 pivot = Vector3.zero;
        
        // Scale
        public Vector3 baseScale = Vector3.one;
        public Vector3 falloffScaleMultiplier = Vector3.one;
        public Vector3 scaleRandomization = Vector3.zero;
        public Vector3 noiseScaleVariance = Vector3.zero;
        
        // Rotation
        public Vector3 baseRotation = Vector3.zero;
        [Range(0F, 1F)] public float normalAlignment = 1F;
        public Vector3 rotationRandomization = new(0F, 360F, 0F);
        
        public Vector3 GetInstanceScale(Vector3 position, float noise = 0F, float falloff = 1F)
        {
            var scale = baseScale;
            var positionHash = Mathf.Abs(position.GetHashCode() * 0.001F);
            if (!float.IsNormal(positionHash)) positionHash = 0;
            if (scaleRandomization.x != 0.0F) scale.x += positionHash % scaleRandomization.x;
            if (scaleRandomization.y != 0.0F) scale.y += positionHash % scaleRandomization.y;
            if (scaleRandomization.z != 0.0F) scale.z += positionHash % scaleRandomization.z;
            scale += noiseScaleVariance * noise;
            scale.Scale(Vector3.LerpUnclamped(Vector3.one, falloffScaleMultiplier, falloff));
            return scale;
        }

        public Quaternion GetInstanceRotation(Ray ray, RaycastHit hit)
        {
            var rotation = normalAlignment <= 0F
                ? Quaternion.identity
                : normalAlignment >= 1F
                    ? Quaternion.LookRotation(Vector3.Cross(Quaternion.Euler(0,90,0) * ray.direction, hit.normal),
                        hit.normal)
                    : Quaternion.Lerp(Quaternion.identity, 
                        Quaternion.LookRotation(Vector3.Cross(Random.onUnitSphere, hit.normal),
                            hit.normal), normalAlignment);
            
            rotation *= Quaternion.Euler(
                            Random.Range(-rotationRandomization.x * 0.5F, rotationRandomization.x * 0.5F),
                            Random.Range(-rotationRandomization.y * 0.5F, rotationRandomization.y * 0.5F),
                            Random.Range(-rotationRandomization.z * 0.5F, rotationRandomization.z * 0.5F)) 
                        * Quaternion.Euler(baseRotation);
            return rotation;
        }
    }
}