Shader "Custom/RedSilhouette"
{
    Properties
    {
        _Color ("Ana Renk", Color) = (1, 0.15, 0.15, 0.85)
        _LineColor ("Cizgi Rengi", Color) = (1, 0.3, 0.3, 1)
        _LineDensity ("Cizgi Sikligi", Float) = 35.0
        _RimPower ("Kenar Parlakligi", Float) = 2.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+600" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always // Sisin, zemin ve engellerin arkasinda her zaman gorunsun
            Cull Off     // Hangi acidan bakilirsa bakilsin asla kaybolmasin!

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            fixed4 _Color;
            fixed4 _LineColor;
            float _LineDensity;
            float _RimPower;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(UnityWorldSpaceViewDir(o.worldPos));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(i.viewDir);
                
                // Kenar parlakligi (Rim)
                float NdotV = 1.0 - abs(dot(normal, viewDir));
                float rim = pow(saturate(NdotV), _RimPower);

                // Cizgili desen
                float linePattern = sin(i.worldPos.y * _LineDensity);
                float lines = step(0.0, linePattern);

                // Asla tamamen gorunmez olmamasi icin taban parlaklik garantisi (0.4)
                fixed4 col = _Color * (0.35 + lines * 0.65);
                col += _LineColor * rim * 1.5;
                col.a = saturate(0.65 + rim);

                return col;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Color"
}
