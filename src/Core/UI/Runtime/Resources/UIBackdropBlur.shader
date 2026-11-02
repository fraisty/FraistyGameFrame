Shader "Hidden/MobileBlur"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Bloom ("Bloom (RGB)", 2D) = "black" {}
        _Parameter ("Blur Radius", Vector) = (1, 0, 0, 0)
        _Direction ("Blur Direction", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _Parameter;
            float2 _Direction;

            half4 Frag(v2f_img input) : SV_Target
            {
                // The captured MobileBlur fragment samples seven texels at -3..3.
                // Keep the weights and ordering identical to the device shader.
                float2 step = abs(_MainTex_TexelSize.xy) * _Direction * _Parameter.x;
                float2 uv = input.uv - step * 3.0;
                half4 color = tex2D(_MainTex, uv) * 0.0205h;
                color += tex2D(_MainTex, uv + step) * 0.0855h;
                color += tex2D(_MainTex, uv + step * 2.0) * 0.2320h;
                color += tex2D(_MainTex, uv + step * 3.0) * 0.3240h;
                color += tex2D(_MainTex, uv + step * 4.0) * 0.2320h;
                color += tex2D(_MainTex, uv + step * 5.0) * 0.0855h;
                color += tex2D(_MainTex, uv + step * 6.0) * 0.0205h;
                return color;
            }
            ENDHLSL
        }

    }
}
