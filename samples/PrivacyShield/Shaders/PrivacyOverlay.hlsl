Texture2D<float4> SceneTexture : register(t0);
Texture2D<float4> MaskTexture : register(t1);
SamplerState LinearSampler : register(s0);
SamplerState PointSampler : register(s1);

cbuffer PrivacySettings : register(b0)
{
    float2 TextureSize;
    float2 BlurStep;
    float2 MosaicBlockSize;
    uint EffectMode;
    float Padding;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float2 TextureCoordinate : TEXCOORD0;
};

VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    float2 textureCoordinate = float2(
        (vertexId << 1) & 2,
        vertexId & 2);

    VertexOutput output;
    output.Position = float4(
        textureCoordinate.x * 2.0f - 1.0f,
        1.0f - textureCoordinate.y * 2.0f,
        0.0f,
        1.0f);
    output.TextureCoordinate = textureCoordinate;
    return output;
}

float3 SampleMosaic(float2 textureCoordinate)
{
    float2 pixel = textureCoordinate * TextureSize;
    float2 block = max(MosaicBlockSize, float2(1.0f, 1.0f));
    float2 blockCenter = (floor(pixel / block) + 0.5f) * block;
    return SceneTexture.SampleLevel(
        PointSampler,
        saturate(blockCenter / TextureSize),
        0).rgb;
}

float3 SampleBlur(float2 textureCoordinate)
{
    static const float weights[5] =
    {
        1.0f / 16.0f,
        4.0f / 16.0f,
        6.0f / 16.0f,
        4.0f / 16.0f,
        1.0f / 16.0f
    };

    float3 color = float3(0.0f, 0.0f, 0.0f);
    [unroll]
    for (int y = -2; y <= 2; y++)
    {
        [unroll]
        for (int x = -2; x <= 2; x++)
        {
            float2 offset = float2(x, y) * BlurStep;
            color += SceneTexture.SampleLevel(
                LinearSampler,
                saturate(textureCoordinate + offset),
                0).rgb * weights[x + 2] * weights[y + 2];
        }
    }
    return color;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float alpha = MaskTexture.SampleLevel(
        PointSampler,
        saturate(input.TextureCoordinate),
        0).a;
    if (alpha <= 0.0f)
    {
        discard;
    }

    float3 color = EffectMode == 1
        ? SampleMosaic(input.TextureCoordinate)
        : SampleBlur(input.TextureCoordinate);
    return float4(color, alpha);
}
