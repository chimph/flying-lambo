Shader "AstroQuest/Vehicle Paint"
{
    Properties
    {
        _Color ("Paint", Color) = (0.95,0.95,0.95,1)
        _MainTex ("Donor albedo", 2D) = "white" {}
        _UseDonorMap ("Use donor paint texture", Range(0,1)) = 1
        _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpStrength ("Paint normal strength", Range(0,1)) = 0
        _Metallic ("Metallic", Range(0,1)) = 0.38
        _Glossiness ("Smoothness", Range(0,1)) = 0.65
        _EmissionMap ("Emission", 2D) = "black" {}
        _EmissionColor ("Emission color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing
        sampler2D _MainTex, _BumpMap, _EmissionMap;
        fixed4 _Color, _EmissionColor;
        half _Metallic, _Glossiness, _BumpStrength, _UseDonorMap;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; float3 worldNormal; INTERNAL_DATA };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 donor = tex2D(_MainTex, IN.uv_MainTex);
            // Repaint the yellow areas while retaining the donor's black details.
            half paint = saturate((min(donor.r, donor.g) - donor.b - 0.05h) / 0.13h);
            o.Albedo = lerp(_Color.rgb, lerp(donor.rgb, _Color.rgb, paint), _UseDonorMap);
            o.Normal = normalize(lerp(half3(0,0,1), UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap)), _BumpStrength));
            o.Metallic = _Metallic;
            half3 n = normalize(WorldNormalVector(IN, o.Normal));
            half variance = dot(ddx(n),ddx(n)) + dot(ddy(n),ddy(n));
            o.Smoothness = min(_Glossiness, 1 - sqrt(saturate(variance)));
            o.Emission = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
