// Verre liquide (ps_2_0, pour que le rendu logiciel de WPF et --snapshot le dessinent aussi).
// Sur le bord : le contenu est légèrement réfracté (loupe) avec une pointe d'aberration chromatique ;
// liseré lumineux et reflet tournés vers la « lumière » (le curseur) ; léger dégradé d'épaisseur en haut.
// Longueurs en DIP. L'entrée est le rendu de la forme (couleurs pré-multipliées par l'alpha).
// ps_2_0 = 64 instructions au plus : tout ce qui peut l'être est précalculé (Controls/LiquidGlassEffect).
// Compiler avec tools\compile-shaders.ps1 après chaque modification (le .ps est embarqué dans l'app).

sampler2D input : register(s0);
float4 shape : register(c0);   // largeur, hauteur (de l'élément), rayon des coins, 1 / largeur de la bande du bord
float4 light : register(c1);   // position de la lumière (x, y), intensité du liseré, intensité du reflet
float4 optics : register(c2);  // réfraction (DIP), aberration (0 à 1), -log2(e) / rayon du reflet², lumière du haut
float4 inner : register(c3);   // demi-largeur - rayon, demi-hauteur - rayon (de la forme), 1 / largeur, 1 / hauteur (de l'élément)
float4 spot : register(c4);    // centre du reflet (la lumière ramenée dans la forme), centre de la forme
// La forme peut déborder de l'élément (barre du dock : seul le bord du haut est visible, donc éclairé).

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p = uv * shape.xy;
    float2 rel = p - spot.zw;

    // Distance signée au bord d'un rectangle arrondi (< 0 dedans) et normale vers l'extérieur.
    float2 q = abs(rel) - inner.xy;
    float2 qp = max(q, 0.0001);              // jamais nul : normalize resterait défini
    float d = length(qp) + min(max(q.x, q.y), 0) - shape.z;
    float2 n = normalize(qp);
    n = rel < 0 ? -n : n;

    float band = saturate(1 + d * shape.w);  // 1 au bord, 0 à « largeur de bande » à l'intérieur
    float band2 = band * band;
    float2 offset = n * (band2 * optics.x * saturate(-d)) * inner.zw; // saturate(-d) : contour antialiasé intact

    float4 color = tex2D(input, uv - offset);
    color.r = tex2D(input, uv - offset * (1 + optics.y)).r;
    color.b = tex2D(input, uv - offset * (1 - optics.y)).b;

    // Le bord brille s'il est tourné vers la lumière OU proche du point du bord le plus près d'elle (halo).
    float facing = saturate(dot(n, normalize(light.xy - p)));
    float2 toSpot = p - spot.xy;
    float glow = exp2(dot(toSpot, toSpot) * optics.z);
    float edge = band2 * band2;
    edge *= edge;                            // trait fin et net tout au bord (bande^8)
    float rim = (band2 * band * 0.5 + edge) * (0.25 + 0.75 * max(facing, glow)) * light.z;
    float sheen = light.w * glow;
    float top = saturate(1 - 2 * uv.y);
    color.rgb += (rim + sheen + optics.w * top) * color.a;
    color.rgb = min(color.rgb, color.a);     // reste une couleur pré-multipliée valide
    return color;
}
