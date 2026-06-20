using UnityEngine;
using UnityEngine.Rendering;

namespace Marinade.InstancedRendering
{
    public struct Instance
    {
        public Instance(Matrix4x4 matrix)
        {
            this.matrix = matrix;
            LIGHT_PROBE_SAMPLE_VECTOR[0] = matrix.GetPosition();
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(LIGHT_PROBE_SAMPLE_VECTOR, LIGHT_PROBE_SAMPLE_RESULT, LIGHT_PROBE_SAMPLE_OCCLUSION_RESULT);
            LIGHT_PROBE_SAMPLE_VECTOR[0] = matrix.rotation * Vector3.up;
            LIGHT_PROBE_SAMPLE_RESULT[0].Evaluate(LIGHT_PROBE_SAMPLE_VECTOR, LIGHT_PROBE_EVALUATION_RESULT);
            this.probeColor = LIGHT_PROBE_SAMPLE_OCCLUSION_RESULT[0];
        }
        
        public Matrix4x4 matrix;
        public Color probeColor;
        
        public static Vector3[] LIGHT_PROBE_SAMPLE_VECTOR = new Vector3[1];
        private static SphericalHarmonicsL2[] LIGHT_PROBE_SAMPLE_RESULT = new SphericalHarmonicsL2[1];
        private static Vector4[] LIGHT_PROBE_SAMPLE_OCCLUSION_RESULT = new Vector4[1];
        public static Color[] LIGHT_PROBE_EVALUATION_RESULT = new Color[1];
    }
}