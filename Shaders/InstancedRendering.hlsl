#ifndef MARINADE_INSTANCED_RENDERING
#define MARINADE_INSTANCED_RENDERING

struct MarinadeInstance
{
    float4x4 m;
    float4 probeColor;
};
StructuredBuffer<MarinadeInstance> _PerInstanceData;
float4x4 _ObjectMatrix;

void MatrixInstancing_float(float3 Position_OS, float3 Normal_OS, float InstanceID, out float3 OutPosition_OS, out float3 OutNormal_OS, out float3 AmbientColor)
{
    MarinadeInstance data = _PerInstanceData[InstanceID];
    //OutPosition_OS = mul(data.m, float4(Position_OS, 1));
    OutPosition_OS = mul(_ObjectMatrix, mul(data.m, float4(Position_OS, 1))).xyz;
    OutNormal_OS = mul(data.m, float4(Normal_OS,0)).xyz;
    AmbientColor = data.probeColor.rgb;
}

#endif