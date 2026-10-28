// ps_3_0 — fond animé « Aurore » (derrière les icônes du bureau, rendu par le GPU seulement).
// Même formule que WallpaperArt.Aurora (l'image fixe du pack) : garder les deux identiques.
// Dessine tout lui-même (l'entrée est ignorée). Compiler avec tools\compile-shaders.ps1.

sampler2D input : register(s0);
float4 info : register(c0);       // largeur / hauteur, temps (s) × vitesse
float4 deep : register(c1);
float4 baseColor : register(c2);
float4 accent1 : register(c3);
float4 accent2 : register(c4);
float4 glow : register(c5);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float t = info.y;
    float px = (uv.x - 0.5) * info.x;
    float py = uv.y - 0.5;

    float3 color = lerp(deep.rgb, baseColor.rgb * 0.8, smoothstep(-0.6, 0.7, py));
    [unroll] for (int i = 0; i < 3; i++)
    {
        float fi = i;
        float y = -0.22 + 0.07 * fi + 0.06 * sin(px * 1.9 + fi * 1.7 + t * 0.11) + 0.03 * sin(px * 4.7 + fi * 0.9 - t * 0.17);
        float w = 0.045 + 0.025 * sin(px * 1.3 + fi + t * 0.07);
        float d = py - y;
        float core = exp(-(d * d) / (w * w));
        float rays = 0.55 + 0.25 * sin(px * 37 + sin(px * 5 + t * 0.3) * 2.5 + t * 0.4) + 0.2 * sin(px * 91 - t * 0.6 + fi);
        float curtain = d > 0 ? exp(-d * 7) * rays : 0;
        float3 tint = lerp(accent1.rgb, accent2.rgb, 0.5 + 0.5 * sin(px * 1.7 + fi * 2 + t * 0.05));
        color += tint * ((core * 0.8 + curtain * 0.3) * (0.6 + 0.2 * fi));
    }
    color += baseColor.rgb * (0.25 * smoothstep(0.1, 0.5, py));
    float2 g = float2(px - 0.3, py + 0.35);
    color += glow.rgb * (0.05 * exp(-dot(g, g) / 0.1));
    color *= 1 - 0.35 * (px * px + py * py);
    color = 1 - exp(-color * 1.1);
    return float4(color, 1);
}
