using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marinade.InstancedRendering
{
    [GenerateHLSL, StructLayout(LayoutKind.Sequential)]
    public struct SpatialCell
    {
        public int Pointer;
        public int Count;
    }
    
    [GenerateHLSL, StructLayout(LayoutKind.Sequential)]
    public struct Instance
    {
        public float4x4 matrix;
        public float4 probeColor;

        public static Instance CreateWithSampledProbe(float4x4 matrix)
        {
            var instance = new Instance
            {
                matrix = matrix
            };
            instance.UpdateLightProbe();
            return instance;
        }

        public float3 GetPosition()
        {
            return matrix.c3.xyz;
        }

        public quaternion GetRotation()
        {
            return quaternion.LookRotation(matrix.c2.xyz, matrix.c1.xyz);
        }

        public float3 GetScale()
        {
            return new float3(
                math.length(matrix.c0.xyz),
                math.length(matrix.c1.xyz),
                math.length(matrix.c2.xyz));
        }

        public void UpdateLightProbe()
        {
            LIGHT_PROBE_SAMPLE_VECTOR[0] = GetPosition();
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(LIGHT_PROBE_SAMPLE_VECTOR, LIGHT_PROBE_SAMPLE_RESULT, LIGHT_PROBE_SAMPLE_OCCLUSION_RESULT);
            LIGHT_PROBE_SAMPLE_VECTOR[0] = math.mul(GetRotation(), new float3(0,1,0));
            LIGHT_PROBE_SAMPLE_RESULT[0].Evaluate(LIGHT_PROBE_SAMPLE_VECTOR, LIGHT_PROBE_EVALUATION_RESULT);
            probeColor = LIGHT_PROBE_SAMPLE_OCCLUSION_RESULT[0];
        }


        public static Vector3[] LIGHT_PROBE_SAMPLE_VECTOR = new Vector3[1];
        private static SphericalHarmonicsL2[] LIGHT_PROBE_SAMPLE_RESULT = new SphericalHarmonicsL2[1];
        private static Vector4[] LIGHT_PROBE_SAMPLE_OCCLUSION_RESULT = new Vector4[1];
        public static Color[] LIGHT_PROBE_EVALUATION_RESULT = new Color[1];
    }
}