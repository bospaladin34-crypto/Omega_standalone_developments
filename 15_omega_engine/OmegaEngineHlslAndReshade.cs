using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OmegaTriEngine
{
    public sealed class ArenaShaderPreset
    {
        public string Name;
        public int SymmetryMode;
        public float K1, K2, K3, K4;
        public float WeatherAurora;
        public float CustomMix;
        public string CustomOmshSnippet;

        public ArenaShaderPreset(string name, int sym, float k1, float k2, float k3, float k4, float aurora, float mix, string code)
        {
            Name = name;
            SymmetryMode = sym;
            K1 = k1; K2 = k2; K3 = k3; K4 = k4;
            WeatherAurora = aurora;
            CustomMix = mix;
            CustomOmshSnippet = code;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public static class OmegaEngineHlslAndReshade
    {
        public static readonly ArenaShaderPreset[] BuiltInPresets = new ArenaShaderPreset[]
        {
            new ArenaShaderPreset(
                "01. Sovngarde Penrose P3 Sanctum (5D de Bruijn + Crystal Monolith)",
                0, 1.00f, 1.00f, 0.85f, 1.54f, 0.82f, 0.25f,
                "// Live .omsh / HLSL Arena Surface Accent (wp = world XYZ, rd = view ray, col = linear HDR RGB)\r\n" +
                "float ring = 0.5 + 0.5 * sin(length(wp.xz) * 3.0 - U[0].z * 2.2 + U[3].x * 4.0);\r\n" +
                "col += float3(0.05, 0.14, 0.24) * ring * exp(-abs(wp.y) * 0.6);"
            ),
            new ArenaShaderPreset(
                "02. Chiral Spectre Obsidian Citadel (arXiv:2305.17743 Homochiral)",
                3, 1.12f, 1.35f, 1.05f, 1.62f, 0.68f, 0.32f,
                "// Chiral Spectre mystic gold HDR pulse synced to 48 kHz mid/bass\r\n" +
                "float swirl = sin(atan2(wp.z, wp.x + 1e-6) * 5.0 + length(wp.xz) * 1.618 - U[0].z * 1.8);\r\n" +
                "col += float3(0.18, 0.10, 0.03) * saturate(swirl) * (0.35 + U[3].y);"
            ),
            new ArenaShaderPreset(
                "03. Einstein Hat Bioluminescent Moat (arXiv:2303.10798 Polykite)",
                2, 0.96f, 0.90f, 1.20f, 1.52f, 0.90f, 0.36f,
                "// Polykite metatile emerald-cyan caustic boost\r\n" +
                "float g = abs(om_sdGyroid(wp * 1.8 + float3(0.0, U[0].z * 0.3, 0.0), 2.4));\r\n" +
                "col += float3(0.03, 0.20, 0.16) * exp(-g * 9.0) * (0.4 + U[3].x);"
            ),
            new ArenaShaderPreset(
                "04. 8D E8 Coxeter Singularity Vault (Exact F2-F1 Facets + Gyroid Core)",
                1, 1.12f, 1.40f, 1.28f, 1.66f, 0.96f, 0.38f,
                "// 8D E8 Voronoi spinor coset violet-cyan facet resonance\r\n" +
                "uint h8 = 0u; float f1 = 0.0; float sp8 = 0.0;\r\n" +
                "float edge8 = om_e8_voronoi_edge(float4(wp.xyz * 0.9, U[0].z * 0.25), float4(wp.zyx * 0.6, U[3].x), f1, sp8, h8);\r\n" +
                "col += float3(0.13, 0.08, 0.28) * exp(-edge8 * 14.0) * (0.5 + U[3].y);"
            ),
            new ArenaShaderPreset(
                "05. 4D Hopf Fibration Villarceau Sanctum (S^3 -> S^2 Clifford Tori)",
                4, 1.05f, 1.10f, 0.95f, 1.58f, 0.88f, 0.34f,
                "// Stereographic S^3 -> S^2 Hopf fiber azimuth shimmer driven by OSP4 quaternion U[6]\r\n" +
                "float3 bS2; float hSeam;\r\n" +
                "om_hopf_villarceau_field(wp * 0.26, U[6], U[0].z * 0.35, bS2, hSeam);\r\n" +
                "float hHue = frac(atan2(bS2.y, bS2.x + 1e-6) / 6.2831853 + U[0].z * 0.05);\r\n" +
                "col += om_hsv2rgb(float3(hHue, 0.78, 0.22)) * exp(-hSeam * 12.0) * (0.45 + U[3].x);"
            ),
            new ArenaShaderPreset(
                "06. 4D 24-Cell & 120-Cell Polychora Vault (Hurwitz + H4 Coxeter)",
                5, 1.08f, 1.15f, 1.00f, 1.64f, 0.84f, 0.35f,
                "// Double-isoclinic SO(4) H4 [5,3,3] 120-cell Coxeter chamber resonance\r\n" +
                "float4 fH4 = om_h4_fold16(float4(wp * 0.32, sin(U[0].z * 0.4) * 0.5));\r\n" +
                "float strut = abs(fH4.w - 0.85);\r\n" +
                "col += float3(0.22, 0.14, 0.04) * exp(-strut * 14.0) * (0.45 + U[3].y);"
            ),
            new ArenaShaderPreset(
                "07. Hyperbolic Poincare {5,3,4} & {4,3,5} Portal (arXiv:1511.02851)",
                6, 1.12f, 1.05f, 0.65f, 1.50f, 0.92f, 0.36f,
                "// Non-Euclidean Poincare disk horocycle & geodesic caustic aura (k3 < 0.9 = {5,3,4}, >= 0.9 = {4,3,5})\r\n" +
                "float3 fU; float invSteps;\r\n" +
                "float dHyp = om_poincare_honeycomb_sdf(clamp(wp * 0.12, -0.92, 0.92), 1.0, U[0].z * 0.22, U[15].z, fU, invSteps);\r\n" +
                "col += float3(0.04, 0.18, 0.25) * exp(-abs(dHyp) * 16.0) * (0.40 + 0.08 * invSteps);"
            ),
            new ArenaShaderPreset(
                "08. 24D Leech Lattice & Golay [24,12,8] Chiral Citadel",
                7, 1.10f, 1.25f, 1.10f, 1.68f, 0.94f, 0.38f,
                "// Self-dual [24,12,8] Extended Binary Golay octad/dodecad + 24D Leech Voronoi glow\r\n" +
                "float f1L, octTag; uint hL;\r\n" +
                "float e24 = om_leech24_golay_voronoi(wp * 0.85, U[0].z * 0.30, f1L, octTag, hL);\r\n" +
                "float3 lTint = lerp(float3(0.06, 0.22, 0.16), float3(0.24, 0.14, 0.05), octTag);\r\n" +
                "col += lTint * exp(-e24 * 15.0) * (0.45 + U[3].x);"
            )
        };

        // Shared 3D CSG Sunken Arena + Dual-Gimbal HSV Gyroscope SDF used identically by CSRayConePrepass & CSArenaRaymarch
        private static string BuildSharedArenaGeometrySdfHlsl()
        {
            return @"
static const float OM_PI  = 3.14159265;
static const float OM_TAU = 6.28318531;
static const float OM_PHI = 1.61803399;

float om_smin(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / max(1e-4, k));
    return lerp(b, a, h) - k * h * (1.0 - h);
}

float om_sdRoundCyl(float3 p, float r, float halfH, float bevel)
{
    float2 d = abs(float2(length(p.xz), p.y)) - float2(r - bevel, halfH - bevel);
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - bevel;
}

float om_sdRoundBox(float3 p, float3 b, float bevel)
{
    float3 q = abs(p) - (b - bevel);
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0) - bevel;
}

float om_sdGyroid(float3 p, float sc)
{
    float s = max(0.05, abs(sc));
    float3 q = p * s;
    return (sin(q.x) * cos(q.y) + sin(q.y) * cos(q.z) + sin(q.z) * cos(q.x)) / s;
}

float3 om_hsv2rgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * lerp(float3(1.0, 1.0, 1.0), saturate(p - 1.0), c.y);
}

void EvalGyroscopeOrbLocal(float3 p, out float3 opOut, out float orbROut, out float orbPhaseOut, out float dCoreOut, out float dGimbalsOut)
{
    bool ewAxis = (abs(p.x) > abs(p.z));
    orbPhaseOut = ewAxis ? ((p.x >= 0.0) ? 0.0 : 3.14159265) : ((p.z >= 0.0) ? 1.57079633 : 4.71238898);
    float2 q2 = ewAxis ? float2(p.z, abs(p.x) - 7.9) : float2(p.x, abs(p.z) - 7.9);
    float bob = 0.16 * sin(U[0].z * 2.4 + orbPhaseOut) + 0.22 * U[3].x;
    float3 op = float3(q2.x, p.y - (2.32 + bob), q2.y);
    float orbR = 0.29 + 0.06 * U[3].w + 0.05 * U[14].x;
    opOut = op;
    orbROut = orbR;

    // Outer precessing obsidian-iridium gimbal torus
    float a1 = U[0].z * 1.85 + orbPhaseOut + U[3].y * 0.8;
    float c1 = cos(a1), s1 = sin(a1);
    float3 g1 = float3(op.x * c1 - op.z * s1, op.y, op.x * s1 + op.z * c1);
    g1.yz = float2(g1.y * 0.8660254 - g1.z * 0.5, g1.y * 0.5 + g1.z * 0.8660254);
    float dOuter = length(float2(length(g1.xz) - orbR * 1.24, g1.y)) - 0.030;

    // Inner counter-precessing orthogonal gimbal torus
    float a2 = -U[0].z * 2.55 + orbPhaseOut * 0.5 - U[3].z * 0.9;
    float c2 = cos(a2), s2 = sin(a2);
    float3 g2 = float3(op.x * c2 + op.y * s2, -op.x * s2 + op.y * c2, op.z);
    g2.xz = float2(g2.x * 0.8090170 - g2.z * 0.5877853, g2.x * 0.5877853 + g2.z * 0.8090170);
    float dInner = length(float2(length(g2.xy) - orbR * 1.02, g2.z)) - 0.025;
    dGimbalsOut = min(dOuter, dInner);

    // Faceted octahedral-sphere spectral harmonic core
    float coreR = orbR * 0.76;
    float dOcta = (abs(op.x) + abs(op.y) * 0.86 + abs(op.z) - coreR * 1.38) * 0.57735027;
    dCoreOut = lerp(length(op) - coreR, dOcta, 0.58);
}

// Smooth Lipschitz-Bounded 3D CSG Sunken Arena SDF (Intact Arched Bridges + Side Curbs + Dual-Gimbal Gyroscopes)
float EvalArenaArchitectureSdf(float3 p, out int matId)
{
    matId = 1;
    float r = length(p.xz);

    // 1. Sunken Moat Basin + Smooth Beveled Amphitheater Terraces (r = 11.4 .. 17.5)
    float basinY = -0.62;
    if (r > 11.15)
    {
        float uStep = clamp((r - 11.4) / 1.22, 0.0, 5.999);
        float fl = floor(uStep);
        float fr = smoothstep(0.0, 0.14, uStep - fl);
        float amphY = 0.20 + (fl + fr) * 0.34;
        if (r > 17.5) amphY = 2.24 + clamp((r - 17.5) * 0.55, 0.0, 2.35);
        basinY = lerp(-0.62, amphY, smoothstep(11.15, 11.48, r));
    }
    float nearRiser = smoothstep(10.8, 11.15, r) * (1.0 - smoothstep(0.7, 1.6, abs(p.y - basinY)));
    float lipScale = lerp(0.80, 0.31, nearRiser);
    float dFloor = (p.y - basinY) * lipScale;

    // 2. Smooth Two-Tier Beveled Central Sanctum Dais
    float dDaisLow  = om_sdRoundCyl(p - float3(0.0, -0.20, 0.0), 4.62, 0.48, 0.045);
    float dDaisHigh = om_sdRoundCyl(p - float3(0.0,  0.14, 0.0), 2.24, 0.41, 0.045);
    float dDais = om_smin(dDaisLow, dDaisHigh, 0.06);

    // 3. 4 Intact Arched Obsidian Causeway Bridges (crown y = +0.52m, vault top = +0.22m -> 0.30m solid keystone)
    float3 qBr = float3(min(abs(p.x), abs(p.z)), p.y, max(abs(p.x), abs(p.z)));
    float spanU = clamp((qBr.z - 7.9) / 3.65, -1.0, 1.0);
    float archRise = 0.24 * (1.0 - spanU * spanU);
    float deckCenterY = 0.06 + archRise;
    float dBridgeDeck = om_sdRoundBox(float3(qBr.x, qBr.y - deckCenterY, qBr.z - 7.9), float3(1.38, 0.22, 3.68), 0.035);
    float dCurb = om_sdRoundBox(float3(abs(qBr.x) - 1.26, qBr.y - (deckCenterY + 0.26), qBr.z - 7.9), float3(0.11, 0.08, 3.55), 0.02);
    float dSpan = min(dBridgeDeck, dCurb);
    float dArchVault = length(float2((qBr.z - 7.9) * 0.58, qBr.y + 0.50)) - 0.72;
    float dBridge = max(dSpan, -dArchVault);

    float dScene = om_smin(dFloor, dDais, 0.07);
    dScene = om_smin(dScene, dBridge, 0.05);

    // 4. 10 Fluted Obsidian Rune Pillars + Capitals at r = 16.2m (continuity-preserving 1-Lipschitz bound guard)
    float dPilBound = max(abs(r - 16.2) - 0.64, p.y - 5.34);
    if (dPilBound < dScene)
    {
        float ang = atan2(p.z, p.x + 1e-6);
        float sector = OM_TAU / 10.0;
        float snap = floor(ang / sector + 0.5) * sector;
        float2 pc = float2(cos(snap), sin(snap)) * 16.2;
        float2 lp = p.xz - pc;
        float fluting = 0.018 * cos(atan2(lp.y, lp.x + 1e-6) * 8.0);
        float dShaft = om_sdRoundCyl(float3(lp.x, p.y - 2.65, lp.y), 0.46 + fluting, 2.55, 0.03);
        float dCap   = om_sdRoundCyl(float3(lp.x, p.y - 5.15, lp.y), 0.60, 0.16, 0.03);
        float dBase  = om_sdRoundCyl(float3(lp.x, p.y - 0.32, lp.y), 0.58, 0.18, 0.03);
        float dPil = min(dShaft, min(dCap, dBase));
        if (dPil < dScene)
        {
            dScene = dPil;
            matId = 2;
        }
    }

    // 5. 4 Counter-Precessing HSV Spectral Gyroscope Orbs above N/S/E/W bridge crowns (continuity-preserving bound guard)
    float dOrbBound = max(abs(r - 7.9) - 0.95, abs(p.y - 2.35) - 0.95);
    if (dOrbBound < dScene)
    {
        float3 op; float orbR, orbPhase, dCore, dGimbals;
        EvalGyroscopeOrbLocal(p, op, orbR, orbPhase, dCore, dGimbals);
        float dOrb = min(dCore, dGimbals);
        if (dOrb < dScene)
        {
            dScene = dOrb;
            matId = 3;
        }
    }

    return dScene;
}
";
        }

        public static string BuildReactionMoatHlsl()
        {
            return @"
cbuffer OmegaParams : register(b0)
{
    float4 U[20];
    float4 Post0;
    float4 Post1;
};
StructuredBuffer<float4> MoatPrev : register(t0);
RWStructuredBuffer<float4> MoatNext : register(u0);

[numthreads(16, 16, 1)]
void CSReactionMoat(uint3 dtid : SV_DispatchThreadID)
{
    const uint N = 256u;
    if (dtid.x >= N || dtid.y >= N) return;
    uint x = dtid.x;
    uint y = dtid.y;
    uint idx = y * N + x;

    float2 uv = (float2(x, y) / float(N)) * 2.0 - 1.0;
    float2 worldXZ = uv * 14.0;
    float r = length(worldXZ);

    if (U[2].w < -0.5)
    {
        float ang = atan2(worldXZ.y, worldXZ.x + 1e-6);
        float seedRing = exp(-abs(r - 8.0) * 1.25) * (0.5 + 0.5 * cos(10.0 * ang));
        float v0 = (seedRing > 0.42) ? 0.40 : 0.0;
        MoatNext[idx] = float4(1.0 - v0, v0, 0.0, 0.0);
        return;
    }

    uint xm = (x + N - 1u) & 255u;
    uint xp = (x + 1u) & 255u;
    uint ym = (y + N - 1u) & 255u;
    uint yp = (y + 1u) & 255u;

    float4 c  = MoatPrev[idx];
    float4 l  = MoatPrev[y * N + xm];
    float4 rg = MoatPrev[y * N + xp];
    float4 up = MoatPrev[ym * N + x];
    float4 dn = MoatPrev[yp * N + x];
    float4 ul = MoatPrev[ym * N + xm];
    float4 ur = MoatPrev[ym * N + xp];
    float4 dl = MoatPrev[yp * N + xm];
    float4 dr = MoatPrev[yp * N + xp];

    float4 lap = (l + rg + up + dn) * 0.20 + (ul + ur + dl + dr) * 0.05 - c;

    uint x3m = (x + N - 3u) & 255u;
    uint x3p = (x + 3u) & 255u;
    uint y3m = (y + N - 3u) & 255u;
    uint y3p = (y + 3u) & 255u;
    float ringAvg = 0.25 * (MoatPrev[y * N + x3m].y + MoatPrev[y * N + x3p].y + MoatPrev[y3m * N + x].y + MoatPrev[y3p * N + x].y);

    float mu = max(0.08, U[10].x);
    float sig = max(0.012, U[10].y);
    float leniaGrowth = 2.0 * exp(-((ringAvg - mu) * (ringAvg - mu)) / (2.0 * sig * sig)) - 1.0;

    float feed = 0.036 + 0.012 * saturate(U[3].x);
    float kill = 0.061 + 0.004 * saturate(U[3].z);
    float uvv = c.x * c.y * c.y;

    float nextU = saturate(c.x + 0.20 * lap.x - uvv + feed * (1.0 - c.x));
    float nextV = saturate(c.y + 0.10 * lap.y + uvv - (feed + kill) * c.y + U[10].z * 0.015 * leniaGrowth);

    float waveLap = (l.z + rg.z + up.z + dn.z) - 4.0 * c.z;
    float2 impXZ = U[13].xz;
    float impDist = length(worldXZ - impXZ);
    float impShell = exp(-abs(impDist - U[14].y) * 1.8) * U[14].x;

    float ang = atan2(worldXZ.y, worldXZ.x + 1e-6);
    float sanctumRing = exp(-abs(r - 4.75) * 3.2) * (U[3].x * 0.055 + U[4].x * 0.045) * sin(U[0].z * 8.5 + cos(5.0 * ang) * 2.0);
    float2 qPier = float2(min(abs(worldXZ.x), abs(worldXZ.y)), max(abs(worldXZ.x), abs(worldXZ.y)) - 7.9);
    float orbEmitter = exp(-length(qPier) * 1.85) * (U[3].y * 0.048 + U[4].z * 0.038) * sin(U[0].z * 11.0);
    float chladniDrive = 0.018 * (U[3].x + 0.5 * U[3].z) * cos(U[11].x * worldXZ.x * 0.52 + U[0].z * 2.2) * cos(U[11].y * worldXZ.y * 0.52 - U[0].z * 1.7);

    float vel = (c.w + waveLap * 0.235 + impShell * 0.17 + sanctumRing + orbEmitter + chladniDrive) * 0.972;
    float hgt = clamp(c.z + vel, -1.0, 1.0);
    if (impShell > 0.10) nextV = saturate(nextV + impShell * 0.18);

    MoatNext[idx] = float4(nextU, nextV, hgt, vel);
}
";
        }

        // Conservative 8x8 Tile Ray-Cone Prepass (Triangle-Inequality Exact Lipschitz Bound -> zero clipped geometry)
        public static string BuildRayConePrepassHlsl()
        {
            StringBuilder sb = new StringBuilder(8192);
            sb.Append(@"
cbuffer OmegaParams : register(b0)
{
    float4 U[20];
    float4 Post0;
    float4 Post1;
};
RWTexture2D<float> TileStartT : register(u0);
");
            sb.Append(BuildSharedArenaGeometrySdfHlsl());
            sb.Append(@"
// Strictly 1-Lipschitz Convex Lower-Bound Envelope SDF (|grad| <= 1.0 everywhere, 0 riser-gradient overshoot)
float EvalRayConeSafeEnvelopeSdf(float3 p)
{
    float r = length(p.xz);
    float dBasin = (p.y + 0.62) * 0.78 - 0.08;
    float yTerraceTop = 0.58 + max(0.0, r - 11.10) * 0.30 + max(0.0, r - 17.40) * 0.30;
    float dTerrace = max(11.10 - r, (p.y - yTerraceTop) * 0.72) - 0.08;
    float dDais = om_sdRoundCyl(p - float3(0.0, -0.02, 0.0), 4.70, 0.64, 0.04) - 0.08;

    float2 qBr = float2(min(abs(p.x), abs(p.z)), max(abs(p.x), abs(p.z)));
    float dBridge = om_sdRoundBox(float3(qBr.x, p.y - 0.18, qBr.y - 7.9), float3(1.44, 0.48, 3.74), 0.02) - 0.06;

    float ang = atan2(p.z, p.x + 1e-6);
    float sector = OM_TAU / 10.0;
    float snap = floor(ang / sector + 0.5) * sector;
    float2 lp = p.xz - float2(cos(snap), sin(snap)) * 16.2;
    float dPil = om_sdRoundCyl(float3(lp.x, p.y - 2.65, lp.y), 0.64, 2.70, 0.02);

    float orbMaxR = (0.29 + 0.06 * max(0.0, U[3].w) + 0.05 * max(0.0, U[14].x)) * 1.28 + 0.06;
    float bobMax  = 0.18 + 0.24 * max(0.0, U[3].x);
    float dOrb = om_sdRoundCyl(float3(qBr.x, p.y - 2.32, qBr.y - 7.9), orbMaxR, orbMaxR + bobMax, 0.02);

    return min(min(dBasin, dTerrace), min(min(dDais, dBridge), min(dPil, dOrb)));
}

float3 MakeTileRayDir(float2 px, float2 renderDim, float fovTan, float3 camFwd, float3 camRight, float3 camUp)
{
    float2 ndc = float2((px.x / renderDim.x) * 2.0 - 1.0, 1.0 - (px.y / renderDim.y) * 2.0);
    ndc.x *= renderDim.x / renderDim.y;
    return normalize(camFwd + ndc.x * fovTan * camRight + ndc.y * fovTan * camUp);
}

[numthreads(8, 8, 1)]
void CSRayConePrepass(uint3 dtid : SV_DispatchThreadID)
{
    uint renderW = (uint)max(1.0, U[0].x);
    uint renderH = (uint)max(1.0, U[0].y);
    uint tileW = (renderW + 7u) >> 3u;
    uint tileH = (renderH + 7u) >> 3u;
    if (dtid.x >= tileW || dtid.y >= tileH) return;

    float2 renderDim = float2(renderW, renderH);
    float fovTan = max(0.25, U[0].w);
    float3 ro = U[1].xyz;
    float3 camFwd = normalize(U[18].xyz);
    float3 camRight = normalize(U[19].xyz);
    float3 camUp = normalize(cross(camFwd, camRight));

    float x0 = float(dtid.x * 8u);
    float y0 = float(dtid.y * 8u);
    float2 centerPx = float2(min(renderDim.x - 0.5, x0 + 4.0), min(renderDim.y - 0.5, y0 + 4.0));
    float3 rdC  = MakeTileRayDir(centerPx, renderDim, fovTan, camFwd, camRight, camUp);
    float3 rd00 = MakeTileRayDir(float2(x0, y0), renderDim, fovTan, camFwd, camRight, camUp);
    float3 rd10 = MakeTileRayDir(float2(x0 + 8.0, y0), renderDim, fovTan, camFwd, camRight, camUp);
    float3 rd01 = MakeTileRayDir(float2(x0, y0 + 8.0), renderDim, fovTan, camFwd, camRight, camUp);
    float3 rd11 = MakeTileRayDir(float2(x0 + 8.0, y0 + 8.0), renderDim, fovTan, camFwd, camRight, camUp);

    float coneAperture = max(
        max(length(rd00 - rdC), length(rd10 - rdC)),
        max(length(rd01 - rdC), length(rd11 - rdC))) + 0.004;

    float minT = max(U[2].y, 0.06);
    float t = minT;

    [loop]
    for (int s = 0; s < 28; s++)
    {
        float dSafe = EvalRayConeSafeEnvelopeSdf(ro + rdC * t);
        float stepFree = dSafe - (t * coneAperture + 0.035);
        if (stepFree <= 0.020) break;
        t += (stepFree / (1.0 + coneAperture)) * 0.94;
        if (rdC.y > 0.0 && (ro.y + rdC.y * t) > 5.55)
        {
            t = 84.0;
            break;
        }
        if (t > 84.0)
        {
            t = 84.0;
            break;
        }
    }

    TileStartT[dtid.xy] = max(minT, t - 0.24);
}
");
            return sb.ToString();
        }

        // Dual-Kawase FP16 HDR Bloom Pyramid (Quarter Downsample w/ Soft-Knee + Eighth Downsample + 9-Tap Tent Upsample)
        public static string BuildDualKawaseBloomHlsl()
        {
            return @"
cbuffer OmegaParams : register(b0)
{
    float4 U[20];
    float4 Post0;
    float4 Post1;
};
Texture2D<float4> SrcTexA : register(t0);
Texture2D<float4> SrcTexB : register(t1);
RWTexture2D<float4> DstBloom : register(u0);

float3 SampleBilinearHdr(Texture2D<float4> tex, int2 dim, float2 uv)
{
    float2 p = saturate(uv) * float2(dim) - 0.5;
    int2 i0 = int2(floor(p));
    float2 f = p - float2(i0);
    int2 maxP = dim - int2(1, 1);
    int2 p00 = clamp(i0, int2(0, 0), maxP);
    int2 p10 = clamp(i0 + int2(1, 0), int2(0, 0), maxP);
    int2 p01 = clamp(i0 + int2(0, 1), int2(0, 0), maxP);
    int2 p11 = clamp(i0 + int2(1, 1), int2(0, 0), maxP);
    float3 a = lerp(tex.Load(int3(p00, 0)).rgb, tex.Load(int3(p10, 0)).rgb, f.x);
    float3 b = lerp(tex.Load(int3(p01, 0)).rgb, tex.Load(int3(p11, 0)).rgb, f.x);
    return lerp(a, b, f.y);
}

float3 SoftKneeHdrPrefilter(float3 c)
{
    c = clamp(c, 0.0, 24.0);
    float br = max(c.r, max(c.g, c.b));
    const float thresh = 0.88;
    const float knee = 0.44;
    float soft = clamp(br - thresh + knee, 0.0, 2.0 * knee);
    soft = (soft * soft) / (4.0 * knee + 1e-4);
    float contrib = max(soft, br - thresh) / max(br, 1e-4);
    return c * max(0.0, contrib);
}

[numthreads(16, 16, 1)]
void CSBloomDownQuarter(uint3 dtid : SV_DispatchThreadID)
{
    int2 dstDim = int2(max(1.0, Post0.z), max(1.0, Post0.w));
    if (int(dtid.x) >= dstDim.x || int(dtid.y) >= dstDim.y) return;

    int2 srcDim = int2(max(1.0, U[0].x), max(1.0, U[0].y));
    float2 invDst = 1.0 / float2(dstDim);
    float2 uv = (float2(dtid.xy) + 0.5) * invDst;
    float2 h = invDst * 0.5;

    float3 c  = SoftKneeHdrPrefilter(SampleBilinearHdr(SrcTexA, srcDim, uv));
    float3 ul = SoftKneeHdrPrefilter(SampleBilinearHdr(SrcTexA, srcDim, uv + float2(-h.x, -h.y)));
    float3 ur = SoftKneeHdrPrefilter(SampleBilinearHdr(SrcTexA, srcDim, uv + float2( h.x, -h.y)));
    float3 dl = SoftKneeHdrPrefilter(SampleBilinearHdr(SrcTexA, srcDim, uv + float2(-h.x,  h.y)));
    float3 dr = SoftKneeHdrPrefilter(SampleBilinearHdr(SrcTexA, srcDim, uv + float2( h.x,  h.y)));

    DstBloom[dtid.xy] = float4(c * 0.50 + (ul + ur + dl + dr) * 0.125, 1.0);
}

[numthreads(16, 16, 1)]
void CSBloomDownEighth(uint3 dtid : SV_DispatchThreadID)
{
    int2 dstDim = int2(max(1.0, Post1.x), max(1.0, Post1.y));
    if (int(dtid.x) >= dstDim.x || int(dtid.y) >= dstDim.y) return;

    int2 srcDim = int2(max(1.0, Post0.z), max(1.0, Post0.w));
    float2 invDst = 1.0 / float2(dstDim);
    float2 uv = (float2(dtid.xy) + 0.5) * invDst;
    float2 h = invDst * 0.5;

    float3 c  = SampleBilinearHdr(SrcTexA, srcDim, uv);
    float3 ul = SampleBilinearHdr(SrcTexA, srcDim, uv + float2(-h.x, -h.y));
    float3 ur = SampleBilinearHdr(SrcTexA, srcDim, uv + float2( h.x, -h.y));
    float3 dl = SampleBilinearHdr(SrcTexA, srcDim, uv + float2(-h.x,  h.y));
    float3 dr = SampleBilinearHdr(SrcTexA, srcDim, uv + float2( h.x,  h.y));

    DstBloom[dtid.xy] = float4(c * 0.50 + (ul + ur + dl + dr) * 0.125, 1.0);
}

[numthreads(16, 16, 1)]
void CSBloomUpsampleTent(uint3 dtid : SV_DispatchThreadID)
{
    int2 qDim = int2(max(1.0, Post0.z), max(1.0, Post0.w));
    if (int(dtid.x) >= qDim.x || int(dtid.y) >= qDim.y) return;

    int2 eDim = int2(max(1.0, Post1.x), max(1.0, Post1.y));
    float2 uv = (float2(dtid.xy) + 0.5) / float2(qDim);
    float2 d = 1.0 / float2(eDim);

    float3 s0 = SampleBilinearHdr(SrcTexB, eDim, uv + float2(-d.x, -d.y));
    float3 s1 = SampleBilinearHdr(SrcTexB, eDim, uv + float2( 0.0, -d.y)) * 2.0;
    float3 s2 = SampleBilinearHdr(SrcTexB, eDim, uv + float2( d.x, -d.y));
    float3 s3 = SampleBilinearHdr(SrcTexB, eDim, uv + float2(-d.x,  0.0)) * 2.0;
    float3 s4 = SampleBilinearHdr(SrcTexB, eDim, uv) * 4.0;
    float3 s5 = SampleBilinearHdr(SrcTexB, eDim, uv + float2( d.x,  0.0)) * 2.0;
    float3 s6 = SampleBilinearHdr(SrcTexB, eDim, uv + float2(-d.x,  d.y));
    float3 s7 = SampleBilinearHdr(SrcTexB, eDim, uv + float2( 0.0,  d.y)) * 2.0;
    float3 s8 = SampleBilinearHdr(SrcTexB, eDim, uv + float2( d.x,  d.y));

    float3 wide = (s0 + s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8) * (1.0 / 16.0);
    float3 tight = SrcTexA.Load(int3(dtid.xy, 0)).rgb;
    DstBloom[dtid.xy] = float4(tight * 0.72 + wide * 0.88, 1.0);
}
";
        }

        // Karis-Weighted Linear HDR SSAA Resolve + Dual-Kawase Bloom + Narkowicz ACES + sRGB + 18x18 LDS 5-Tap CAS
        // NOTE: Unconditional GroupMemoryBarrierWithGroupSync() with ZERO preceding return statements (0 X3663 risk!)
        public static string BuildCompositeTonemapCasHlsl()
        {
            return @"
cbuffer OmegaParams : register(b0)
{
    float4 U[20];
    float4 Post0;
    float4 Post1;
};
Texture2D<float4> HdrScene : register(t0);
Texture2D<float4> BloomTex : register(t1);
RWTexture2D<float4> OutTex : register(u0);
RWStructuredBuffer<uint> ReadbackBuf : register(u1);

groupshared float3 s_ldr[18][18];

float3 SampleBilinearComp(Texture2D<float4> tex, int2 dim, float2 uv)
{
    float2 p = saturate(uv) * float2(dim) - 0.5;
    int2 i0 = int2(floor(p));
    float2 f = p - float2(i0);
    int2 maxP = dim - int2(1, 1);
    int2 p00 = clamp(i0, int2(0, 0), maxP);
    int2 p10 = clamp(i0 + int2(1, 0), int2(0, 0), maxP);
    int2 p01 = clamp(i0 + int2(0, 1), int2(0, 0), maxP);
    int2 p11 = clamp(i0 + int2(1, 1), int2(0, 0), maxP);
    float3 a = lerp(tex.Load(int3(p00, 0)).rgb, tex.Load(int3(p10, 0)).rgb, f.x);
    float3 b = lerp(tex.Load(int3(p01, 0)).rgb, tex.Load(int3(p11, 0)).rgb, f.x);
    return lerp(a, b, f.y);
}

float KarisWeight(float3 c)
{
    float luma = dot(c, float3(0.2126, 0.7152, 0.0722));
    return 1.0 / (1.0 + luma * 0.30);
}

float3 EvalTonemappedSrgbAtOutPx(int2 rawOutPx, int2 outDim, int2 renderDim, int2 quarterDim)
{
    int2 p = clamp(rawOutPx, int2(0, 0), outDim - int2(1, 1));
    float2 invOut = 1.0 / float2(outDim);
    float2 uv = (float2(p) + 0.5) * invOut;

    float3 hdr = float3(0.0, 0.0, 0.0);
    if (renderDim.x == outDim.x && renderDim.y == outDim.y)
    {
        hdr = HdrScene.Load(int3(p, 0)).rgb;
    }
    else
    {
        float2 qOff = invOut * 0.25;
        float3 c0 = SampleBilinearComp(HdrScene, renderDim, uv + float2(-qOff.x, -qOff.y));
        float3 c1 = SampleBilinearComp(HdrScene, renderDim, uv + float2( qOff.x, -qOff.y));
        float3 c2 = SampleBilinearComp(HdrScene, renderDim, uv + float2(-qOff.x,  qOff.y));
        float3 c3 = SampleBilinearComp(HdrScene, renderDim, uv + float2( qOff.x,  qOff.y));
        float w0 = KarisWeight(c0);
        float w1 = KarisWeight(c1);
        float w2 = KarisWeight(c2);
        float w3 = KarisWeight(c3);
        hdr = (c0 * w0 + c1 * w1 + c2 * w2 + c3 * w3) / max(1e-4, w0 + w1 + w2 + w3);
    }

    float bloomStr = max(0.0, Post1.z);
    if (bloomStr > 0.001)
    {
        hdr += SampleBilinearComp(BloomTex, quarterDim, uv) * bloomStr;
    }

    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    ndc.x *= float(outDim.x) / max(1.0, float(outDim.y));
    hdr *= max(0.35, U[18].w);
    float vig = saturate(1.0 - 0.16 * dot(ndc * 0.65, ndc * 0.65));
    float3 x = max(0.0, hdr * vig);
    float3 aces = (x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14);
    return pow(max(1e-5, saturate(aces)), 1.0 / 2.2);
}

[numthreads(16, 16, 1)]
void CSCompositeTonemapCas(
    uint3 gid  : SV_GroupID,
    uint3 gtid : SV_GroupThreadID,
    uint3 dtid : SV_DispatchThreadID)
{
    int2 outDim     = int2(max(1.0, Post0.x), max(1.0, Post0.y));
    int2 renderDim  = int2(max(1.0, U[0].x),  max(1.0, U[0].y));
    int2 quarterDim = int2(max(1.0, Post0.z), max(1.0, Post0.w));
    int2 basePx     = int2(gid.xy) * 16 - int2(1, 1);

    uint flatIdx = gtid.y * 16u + gtid.x;
    uint ly0 = flatIdx / 18u;
    uint lx0 = flatIdx - ly0 * 18u;
    s_ldr[ly0][lx0] = EvalTonemappedSrgbAtOutPx(basePx + int2(int(lx0), int(ly0)), outDim, renderDim, quarterDim);

    if (flatIdx < 68u)
    {
        uint idx2 = flatIdx + 256u;
        uint ly1 = idx2 / 18u;
        uint lx1 = idx2 - ly1 * 18u;
        s_ldr[ly1][lx1] = EvalTonemappedSrgbAtOutPx(basePx + int2(int(lx1), int(ly1)), outDim, renderDim, quarterDim);
    }

    GroupMemoryBarrierWithGroupSync();

    uint outW = (uint)outDim.x;
    uint outH = (uint)outDim.y;
    if (dtid.x < outW && dtid.y < outH)
    {
        uint sx = gtid.x + 1u;
        uint sy = gtid.y + 1u;
        float3 c = s_ldr[sy][sx];

        float sharpAmt = saturate(U[12].y);
        if (sharpAmt > 0.01)
        {
            float3 n  = s_ldr[sy - 1u][sx];
            float3 s  = s_ldr[sy + 1u][sx];
            float3 e  = s_ldr[sy][sx + 1u];
            float3 wv = s_ldr[sy][sx - 1u];

            float3 mn = min(c, min(min(n, s), min(e, wv)));
            float3 mx = max(c, max(max(n, s), max(e, wv)));
            float3 amp = sqrt(saturate(min(mn, 1.0 - mx) / max(mx, 1e-4)));
            float3 wt = -amp * (0.115 + 0.085 * sharpAmt);
            c = saturate((c + (n + s + e + wv) * wt) / max(float3(0.15, 0.15, 0.15), 1.0 + 4.0 * wt));
        }

        int dxC = abs(int(dtid.x) - int(outW / 2u));
        int dyC = abs(int(dtid.y) - int(outH / 2u));
        if ((dxC == 0 && dyC >= 3 && dyC <= 7) || (dyC == 0 && dxC >= 3 && dxC <= 7))
        {
            c = lerp(c, float3(0.38, 0.94, 1.0), 0.65);
        }

        OutTex[dtid.xy] = float4(c, 1.0);

        if (U[2].z < 0.0)
        {
            uint r8 = (uint)round(c.r * 255.0);
            uint g8 = (uint)round(c.g * 255.0);
            uint b8 = (uint)round(c.b * 255.0);
            ReadbackBuf[dtid.y * outW + dtid.x] = 0xFF000000u | (r8 << 16) | (g8 << 8) | b8;
        }
    }
}
";
        }

        public static string BuildCasSharpenHlsl()
        {
            return BuildCompositeTonemapCasHlsl();
        }


        // Single-Pass 2048x2048 DirectCompute cs_5_0 4-Map Lossless PBR Baker
        // Simultaneously outputs Albedo (u0), Scharr Tangent-Space Normal (u1), Height/Parallax (u2), and Emissive Mortar (u3)
        // with C^1 / C^inf seamless toroidal UV wrapping across all 8 Symmetry Modes (0..7)
        public static string BuildPbrMaterialBakerHlsl()
        {
            return @"
cbuffer OmegaParams : register(b0)
{
    float4 U[20];
    float4 Post0; // x = texDim (e.g. 2048), y = symMode (0..7), z = normalYSign (+1 OpenGL, -1 DirectX), w = normalStrength
    float4 Post1; // x = tilePeriod, y = mortarWidthScale, z = emissiveStrength, w = phaseSeed
};

RWStructuredBuffer<uint> OutAlbedo   : register(u0);
RWStructuredBuffer<uint> OutNormal   : register(u1);
RWStructuredBuffer<uint> OutHeight   : register(u2);
RWStructuredBuffer<uint> OutEmissive : register(u3);

groupshared float g_pbrH[18][18];

static const float OM_PI  = 3.14159265;
static const float OM_TAU = 6.28318531;
static const float OM_PHI = 1.61803399;

static const float2 PENTA_DIR[5] = {
    float2( 1.0000000,  0.0000000),
    float2( 0.3090170,  0.9510565),
    float2(-0.8090170,  0.5877853),
    float2(-0.8090170, -0.5877853),
    float2( 0.3090170, -0.9510565)
};

static const uint OM_GOLAY_P12[12] = {
    0xFFEu, 0x477u, 0x8EDu, 0x1DBu, 0x3B5u, 0x769u,
    0xED1u, 0xDA3u, 0xB47u, 0x68Fu, 0xD1Du, 0xA3Bu
};

float3 pbr_hsv2rgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * lerp(float3(1.0, 1.0, 1.0), saturate(p - 1.0), c.y);
}

void pbr_d8_f1f2(float4 xA, float4 xB, float sh, out float d1Sq, out float d2Sq, out uint chash)
{
    float4 uA = xA - sh;
    float4 uB = xB - sh;
    int4 zA = int4(floor(uA + 0.5));
    int4 zB = int4(floor(uB + 0.5));
    float4 aA = abs(uA - float4(zA));
    float4 aB = abs(uB - float4(zB));
    float baseD2 = dot(aA, aA) + dot(aB, aB);
    int parity = (zA.x + zA.y + zA.z + zA.w + zB.x + zB.y + zB.z + zB.w) & 1;

    float m1 = aA.x; float m2 = 0.0; int topIdx = 0;
    if (aA.y > m1) { m2 = m1; m1 = aA.y; topIdx = 1; } else if (aA.y > m2) { m2 = aA.y; }
    if (aA.z > m1) { m2 = m1; m1 = aA.z; topIdx = 2; } else if (aA.z > m2) { m2 = aA.z; }
    if (aA.w > m1) { m2 = m1; m1 = aA.w; topIdx = 3; } else if (aA.w > m2) { m2 = aA.w; }
    if (aB.x > m1) { m2 = m1; m1 = aB.x; topIdx = 4; } else if (aB.x > m2) { m2 = aB.x; }
    if (aB.y > m1) { m2 = m1; m1 = aB.y; topIdx = 5; } else if (aB.y > m2) { m2 = aB.y; }
    if (aB.z > m1) { m2 = m1; m1 = aB.z; topIdx = 6; } else if (aB.z > m2) { m2 = aB.z; }
    if (aB.w > m1) { m2 = m1; m1 = aB.w; topIdx = 7; } else if (aB.w > m2) { m2 = aB.w; }

    if (parity == 0)
    {
        d1Sq = baseD2;
        d2Sq = baseD2 + 2.0 - 2.0 * (m1 + m2);
        chash = (uint(zA.x) * 73856093u) ^ (uint(zA.y) * 19349663u) ^ (uint(zA.z) * 83492791u) ^ (uint(zB.x) * 2654435761u);
    }
    else
    {
        d1Sq = baseD2 + 1.0 - 2.0 * m1;
        d2Sq = baseD2 + 1.0 - 2.0 * m2;
        chash = (uint(zA.x) * 73856093u) ^ (uint(zA.y) * 19349663u) ^ (uint(zB.y) * 83492791u) ^ (uint(topIdx + 1) * 2246822519u);
    }
}

float pbr_e8_edge(float4 xA, float4 xB, out float spinorCoset, out uint cellHash)
{
    float dA1, dA2, dB1, dB2;
    uint hA = 0u, hB = 0u;
    pbr_d8_f1f2(xA, xB, 0.0, dA1, dA2, hA);
    pbr_d8_f1f2(xA, xB, 0.5, dB1, dB2, hB);
    if (dB1 < dA1)
    {
        spinorCoset = 1.0;
        cellHash = hB ^ 0x9E3779B9u;
        return max(0.0, sqrt(max(0.0, min(dB2, dA1))) - sqrt(max(0.0, dB1)));
    }
    spinorCoset = 0.0;
    cellHash = hA;
    return max(0.0, sqrt(max(0.0, min(dA2, dB1))) - sqrt(max(0.0, dA1)));
}

float pbr_tri_seam(float2 p)
{
    const float2 n0 = float2(0.0, 1.0);
    const float2 n1 = float2(0.8660254, -0.5);
    const float2 n2 = float2(-0.8660254, -0.5);
    float d0 = abs(frac(dot(p, n0) + 0.5) - 0.5);
    float d1 = abs(frac(dot(p, n1) + 0.5) - 0.5);
    float d2 = abs(frac(dot(p, n2) + 0.5) - 0.5);
    return min(d0, min(d1, d2));
}

void EvalPlanarQuasiPatch(float2 p, int sym, out float seamDist, out float3 colTone)
{
    if (sym == 0)
    {
        float dMin = 10.0;
        float hTone = 0.0;
        [unroll]
        for (int k = 0; k < 5; k++)
        {
            float g = (k == 0) ? U[7].x : ((k == 1) ? U[7].y : ((k == 2) ? U[7].z : ((k == 3) ? U[7].w : U[8].x)));
            float u = dot(p, PENTA_DIR[k]) + g;
            float fl = floor(u);
            float dist = abs(u - fl - 0.5);
            dMin = min(dMin, dist);
            hTone += fl * (0.21 + 0.07 * float(k));
        }
        seamDist = dMin * 0.92;
        colTone = lerp(float3(0.068, 0.095, 0.145), float3(0.145, 0.125, 0.092), frac(hTone * 0.618034));
    }
    else if (sym == 2)
    {
        float dKite = pbr_tri_seam(p * 1.35);
        float dMeta = pbr_tri_seam(float2(p.x * 0.78 - p.y * 0.62, p.x * 0.62 + p.y * 0.78) * 0.52 + float2(0.31, -0.19));
        seamDist = min(dKite, dMeta * 1.45 + 0.015);
        float clus = smoothstep(0.22, 0.05, dMeta);
        colTone = lerp(float3(0.052, 0.112, 0.118), float3(0.118, 0.142, 0.085), clus);
    }
    else
    {
        float2 curl = float2(
            p.x + 0.15 * sin(p.y * 2.094395 + p.x * 0.618034),
            p.y - 0.15 * sin(p.x * 2.094395 - p.y * 0.618034));
        float dBase = pbr_tri_seam(curl * 1.25);
        float dSup = pbr_tri_seam(float2(curl.x * 0.875 - curl.y * 0.484, curl.x * 0.484 + curl.y * 0.875) * 0.44);
        seamDist = min(dBase, dSup * 1.55 + 0.018);
        float mys = smoothstep(0.24, 0.06, dSup);
        colTone = lerp(float3(0.068, 0.064, 0.128), float3(0.158, 0.106, 0.056), mys);
    }
}

void EvalSeamlessPbrTexel(int2 rawPx, int texDim, int sym, out float3 albedoSrgb, out float height01, out float3 emissiveSrgb)
{
    int wx = ((rawPx.x % texDim) + texDim) % texDim;
    int wy = ((rawPx.y % texDim) + texDim) % texDim;
    float2 uv = (float2(wx, wy) + 0.5) / float(texDim);

    float au = OM_TAU * uv.x;
    float av = OM_TAU * uv.y;
    float4 cTorus = float4(cos(au), sin(au), cos(av), sin(av));

    float edgeMetric = 0.25;
    float3 baseLin = float3(0.08, 0.10, 0.14);
    float3 glowHue = float3(0.10, 0.92, 1.18);

    if (sym == 1)
    {
        // Mode 1: Exact C^inf Seamless 8D E8 Clifford-Torus Voronoi Facets
        float sp = 0.0; uint ch = 0u;
        float4 xA = cTorus * 2.15 + 0.65 * float4(cos(2.0 * au), sin(2.0 * au), cos(2.0 * av), sin(2.0 * av));
        float4 xB = float4(cos(au + av), sin(au + av), cos(au - av), sin(au - av)) * 1.75;
        edgeMetric = pbr_e8_edge(xA, xB, sp, ch);
        float cellV = float(ch & 255u) / 255.0;
        baseLin = lerp(float3(0.058, 0.086, 0.142), float3(0.132, 0.082, 0.156), sp * 0.6 + cellV * 0.4);
        glowHue = lerp(float3(0.12, 0.95, 1.28), float3(0.92, 0.38, 1.32), sp);
    }
    else if (sym == 4)
    {
        // Mode 4: Seamless 4D Stereographic Hopf Villarceau Tori
        float4 z4 = normalize(float4(cTorus.xy * 1.15, cTorus.zw * 0.85) + 0.28 * float4(cos(2.0 * av), sin(2.0 * au), 0.0, 0.0));
        float3 bS2 = normalize(float3(
            2.0 * (z4.x * z4.z + z4.y * z4.w),
            2.0 * (z4.y * z4.z - z4.x * z4.w),
            z4.x * z4.x + z4.y * z4.y - z4.z * z4.z - z4.w * z4.w) + float3(0.0, 0.0, 1e-6));
        float az = atan2(bS2.y, bS2.x + 1e-6);
        float sec6 = floor(az / 1.04719755 + 0.5) * 1.04719755;
        float dotEq = dot(bS2, float3(cos(sec6), sin(sec6), 0.0));
        float ringSeam = sqrt(max(0.0, 0.5 * (1.0 - max(dotEq, abs(bS2.z) * 0.96))));
        float latSeam = abs(frac(bS2.z * 3.0 + 0.5) - 0.5) * 0.24;
        edgeMetric = min(ringSeam * 0.85, latSeam);
        float hue = frac(az / OM_TAU + 0.5);
        baseLin = lerp(float3(0.052, 0.088, 0.138), pbr_hsv2rgb(float3(hue, 0.48, 0.16)), 0.70);
        glowHue = pbr_hsv2rgb(float3(hue, 0.78, 1.28));
    }
    else if (sym == 5)
    {
        // Mode 5: Seamless 4D H4 [5,3,3] 120-Cell & Hurwitz 24-Cell Facets
        float4 f = normalize(cTorus + 0.34 * float4(cos(2.0 * au), sin(2.0 * av), cos(au + av), sin(au - av)));
        const float3 n1 = float3(-0.8090170, 0.3090170, 0.5000000);
        const float3 n3 = float3(-0.8090170, 0.5000000, 0.3090170);
        [loop]
        for (int it = 0; it < 12; it++)
        {
            f.x = abs(f.x);
            float d1 = dot(f.xyz, n1);
            if (d1 < 0.0) f.xyz -= 2.0 * d1 * n1;
            if (f.z > 0.0) f.z = -f.z;
            float d3 = dot(f.yzw, n3);
            if (d3 < 0.0) f.yzw -= 2.0 * d3 * n3;
        }
        edgeMetric = min(min(f.x, abs(f.z)), abs(dot(f.xyz, n1))) * 1.55;
        float ct = saturate((f.w - 0.85) * 8.0);
        baseLin = lerp(float3(0.062, 0.076, 0.124), float3(0.156, 0.116, 0.068), ct);
        glowHue = lerp(float3(0.18, 0.92, 1.25), float3(1.32, 0.92, 0.32), ct);
    }
    else if (sym == 6)
    {
        // Mode 6: Seamless Periodic Hyperbolic Poincare {5,3,4} Sphere-Inversion Mosaic
        float3 u = float3(sin(au) * 0.74, 0.10 * cos(au + av), sin(av) * 0.74);
        const float3 h3M1 = float3(-0.8090170, 0.3090170, 0.5000000);
        const float3 c534 = float3(0.0, 1.27201965, -0.78615138);
        float scaleJ = 1.0;
        float invSteps = 0.0;
        [loop]
        for (int it = 0; it < 10; it++)
        {
            u.x = abs(u.x);
            if (u.z > 0.0) u.z = -u.z;
            float d1 = dot(u, h3M1);
            if (d1 < 0.0) u -= 2.0 * d1 * h3M1;
            float3 diff = u - c534;
            float d2 = max(1e-6, dot(diff, diff));
            if (d2 < 1.2360680)
            {
                float k = 1.2360680 / d2;
                u = c534 + diff * k;
                scaleJ *= k;
                invSteps += 1.0;
            }
            else break;
        }
        edgeMetric = abs((length(u - c534) - 1.1117859) / scaleJ) * 2.1;
        float par = frac(invSteps * 0.5) * 2.0;
        baseLin = lerp(float3(0.048, 0.098, 0.132), float3(0.124, 0.078, 0.158), par);
        glowHue = lerp(float3(0.10, 1.05, 1.28), float3(0.85, 0.42, 1.32), par);
    }
    else if (sym == 7)
    {
        // Mode 7: Seamless 24D Leech Lattice + Extended Binary Golay [24,12,8] Voronoi
        float sp0, sp1, sp2; uint h0, h1, h2;
        float e0 = pbr_e8_edge(cTorus * 2.25, float4(cos(2.0 * au), sin(2.0 * au), cos(2.0 * av), sin(2.0 * av)) * 1.45, sp0, h0);
        float e1 = pbr_e8_edge(float4(cos(au + av), sin(au + av), cos(au - av), sin(au - av)) * 2.05, cTorus.zwxy * 1.55, sp1, h1);
        float e2 = pbr_e8_edge(float4(cos(2.0 * au + av), sin(2.0 * au + av), cos(au - 2.0 * av), sin(au - 2.0 * av)) * 1.65, cTorus * 1.35, sp2, h2);
        uint msg12 = (h0 ^ (h1 >> 4u) ^ (h2 * 2246822519u)) & 0xFFFu;
        uint parBits = 0u;
        [loop]
        for (int b = 0; b < 12; b++)
        {
            if (((msg12 >> uint(b)) & 1u) != 0u) parBits ^= OM_GOLAY_P12[b];
        }
        uint cw24 = msg12 | ((parBits & 0xFFFu) << 12u);
        uint x = cw24 - ((cw24 >> 1u) & 0x555555u);
        x = (x & 0x333333u) + ((x >> 2u) & 0x333333u);
        uint wt = (((x + (x >> 4u)) & 0x0F0F0Fu) * 0x010101u) >> 16u;
        float dod = (wt == 12u) ? 1.0 : 0.0;
        float eMin = min(e0, min(e1, e2));
        float eMid = max(min(e0, e1), min(max(e0, e1), e2));
        edgeMetric = (dod > 0.5) ? eMin : (0.5 * (eMin + eMid));
        baseLin = lerp(float3(0.048, 0.106, 0.112), float3(0.148, 0.108, 0.062), dod);
        glowHue = lerp(float3(0.10, 1.18, 0.92), float3(1.38, 0.96, 0.28), dod);
    }
    else
    {
        // Modes 0, 2, 3: C^1-Continuous 4-Corner Toroidal Cross-Blend over Planar Quasicrystals
        float period = max(4.0, Post1.x);
        float sU = sin(OM_PI * uv.x);
        float sV = sin(OM_PI * uv.y);
        float wu = sU * sU;
        float wv = sV * sV;
        float2 p0 = uv * period;
        float2 p1 = frac(uv + 0.5) * period;

        float d00, d10, d01, d11;
        float3 c00, c10, c01, c11;
        EvalPlanarQuasiPatch(p1, sym, d00, c00);
        EvalPlanarQuasiPatch(float2(p0.x + 3.17, p1.y - 1.83), sym, d10, c10);
        EvalPlanarQuasiPatch(float2(p1.x - 2.41, p0.y + 2.79), sym, d01, c01);
        EvalPlanarQuasiPatch(p0, sym, d11, c11);

        float w00 = (1.0 - wu) * (1.0 - wv);
        float w10 = wu * (1.0 - wv);
        float w01 = (1.0 - wu) * wv;
        float w11 = wu * wv;

        edgeMetric = w00 * d00 + w10 * d10 + w01 * d01 + w11 * d11;
        baseLin    = w00 * c00 + w10 * c10 + w01 * c01 + w11 * c11;
        glowHue    = (sym == 3) ? float3(1.28, 0.86, 0.28) : ((sym == 2) ? float3(0.10, 1.15, 0.88) : float3(0.14, 0.94, 1.28));
    }

    float mortarW = 0.036 * max(0.4, Post1.y);
    float mortarMask = smoothstep(mortarW * 1.45, mortarW * 0.35, edgeMetric);
    float bevelLip = smoothstep(mortarW * 3.1, mortarW * 1.25, edgeMetric) * (1.0 - mortarMask);

    // Exact integer-harmonic periodic micro-grain (zero border jump!)
    float grain = 0.045 * sin(OM_TAU * (uv.x * 13.0 + uv.y * 7.0)) * cos(OM_TAU * (uv.y * 17.0 - uv.x * 11.0))
                + 0.022 * sin(OM_TAU * (uv.x * 29.0 - uv.y * 23.0));

    height01 = saturate(lerp(0.80 + 0.14 * bevelLip + grain, 0.08 + 0.06 * saturate(edgeMetric / max(1e-4, mortarW)), mortarMask));

    float3 litBase = baseLin * (2.35 + 0.38 * bevelLip + 1.8 * grain);
    litBase = lerp(litBase, float3(0.022, 0.028, 0.042), mortarMask * 0.86);
    albedoSrgb = pow(max(1e-5, saturate(litBase)), 1.0 / 2.2);

    float emAmt = smoothstep(mortarW * 1.30, mortarW * 0.15, edgeMetric) * max(0.0, Post1.z);
    float3 emHdr = glowHue * emAmt * 1.25;
    float3 emAces = (emHdr * (2.51 * emHdr + 0.03)) / (emHdr * (2.43 * emHdr + 0.59) + 0.14);
    emissiveSrgb = pow(max(1e-5, saturate(emAces)), 1.0 / 2.2) * step(0.015, emAmt);
}

uint PackRgba8(float3 rgb01)
{
    uint3 c8 = uint3(round(saturate(rgb01) * 255.0));
    return 0xFF000000u | (c8.r << 16u) | (c8.g << 8u) | c8.b;
}

[numthreads(16, 16, 1)]
void CSPbrMaterialBaker(
    uint3 gid  : SV_GroupID,
    uint3 gtid : SV_GroupThreadID,
    uint3 dtid : SV_DispatchThreadID)
{
    int texDim = max(64, int(round(Post0.x)));
    int sym    = clamp(int(round(Post0.y)), 0, 7);
    int2 basePx = int2(gid.xy) * 16 - int2(1, 1);

    uint flatIdx = gtid.y * 16u + gtid.x;
    uint ly0 = flatIdx / 18u;
    uint lx0 = flatIdx - ly0 * 18u;

    float3 dAlb, dEm;
    float h0;
    EvalSeamlessPbrTexel(basePx + int2(int(lx0), int(ly0)), texDim, sym, dAlb, h0, dEm);
    g_pbrH[ly0][lx0] = h0;

    if (flatIdx < 68u)
    {
        uint idx2 = flatIdx + 256u;
        uint ly1 = idx2 / 18u;
        uint lx1 = idx2 - ly1 * 18u;
        float3 dAlb2, dEm2;
        float h1;
        EvalSeamlessPbrTexel(basePx + int2(int(lx1), int(ly1)), texDim, sym, dAlb2, h1, dEm2);
        g_pbrH[ly1][lx1] = h1;
    }

    GroupMemoryBarrierWithGroupSync();

    if (int(dtid.x) < texDim && int(dtid.y) < texDim)
    {
        float3 alb, emi;
        float hCenter;
        EvalSeamlessPbrTexel(int2(dtid.xy), texDim, sym, alb, hCenter, emi);

        uint sx = gtid.x + 1u;
        uint sy = gtid.y + 1u;

        // 3x3 rotationally-symmetric Scharr Sobel tangent-space gradient
        float dhdx = (3.0 * g_pbrH[sy - 1u][sx + 1u] + 10.0 * g_pbrH[sy][sx + 1u] + 3.0 * g_pbrH[sy + 1u][sx + 1u])
                   - (3.0 * g_pbrH[sy - 1u][sx - 1u] + 10.0 * g_pbrH[sy][sx - 1u] + 3.0 * g_pbrH[sy + 1u][sx - 1u]);
        float dhdy = (3.0 * g_pbrH[sy + 1u][sx - 1u] + 10.0 * g_pbrH[sy + 1u][sx] + 3.0 * g_pbrH[sy + 1u][sx + 1u])
                   - (3.0 * g_pbrH[sy - 1u][sx - 1u] + 10.0 * g_pbrH[sy - 1u][sx] + 3.0 * g_pbrH[sy - 1u][sx + 1u]);

        float ySign  = (Post0.z < 0.0) ? -1.0 : 1.0;
        float nScale = max(0.5, Post0.w) * (float(texDim) / 512.0) * (1.0 / 32.0);
        float3 nrmTS = normalize(float3(-dhdx * nScale, -dhdy * nScale * ySign, 1.0));

        uint outIdx = dtid.y * uint(texDim) + dtid.x;
        OutAlbedo[outIdx]   = PackRgba8(alb);
        OutNormal[outIdx]   = PackRgba8(nrmTS * 0.5 + 0.5);
        OutHeight[outIdx]   = PackRgba8(float3(hCenter, hCenter, hCenter));
        OutEmissive[outIdx] = PackRgba8(emi);
    }
}
";
        }

        public static string BuildArenaRaymarchHlsl(string customOmshSnippet)
        {
            string safeSnippet = string.IsNullOrEmpty(customOmshSnippet)
                ? "col = col;"
                : customOmshSnippet;

            StringBuilder sb = new StringBuilder(32768);
            sb.Append(@"
cbuffer OmegaParams : register(b0)
{
    float4 U[20];
    float4 Post0;
    float4 Post1;
};
StructuredBuffer<float4> MoatField : register(t0);
Texture2D<float> TileStartT : register(t1);
RWTexture2D<float4> OutTex : register(u0);

static const float2 PENTA_DIR[5] = {
    float2( 1.0000000,  0.0000000),
    float2( 0.3090170,  0.9510565),
    float2(-0.8090170,  0.5877853),
    float2(-0.8090170, -0.5877853),
    float2( 0.3090170, -0.9510565)
};
static const float2 PENTA_CONJ[5] = {
    float2( 1.0000000,  0.0000000),
    float2(-0.8090170,  0.5877853),
    float2( 0.3090170, -0.9510565),
    float2( 0.3090170,  0.9510565),
    float2(-0.8090170, -0.5877853)
};
");
            sb.Append(BuildSharedArenaGeometrySdfHlsl());
            sb.Append(@"
// Exact O(1) Branch-Minimal 8D D8 1st & 2nd Nearest Squared Lattice Distances (zero loops!)
void om_d8_f1f2(float4 xA, float4 xB, float sh, out float d1Sq, out float d2Sq, out uint chash)
{
    float4 uA = xA - sh;
    float4 uB = xB - sh;
    int4 zA = int4(floor(uA + 0.5));
    int4 zB = int4(floor(uB + 0.5));
    float4 aA = abs(uA - float4(zA));
    float4 aB = abs(uB - float4(zB));

    float baseD2 = dot(aA, aA) + dot(aB, aB);
    int parity = (zA.x + zA.y + zA.z + zA.w + zB.x + zB.y + zB.z + zB.w) & 1;

    float m1 = aA.x; float m2 = 0.0; int topIdx = 0;
    if (aA.y > m1) { m2 = m1; m1 = aA.y; topIdx = 1; } else if (aA.y > m2) { m2 = aA.y; }
    if (aA.z > m1) { m2 = m1; m1 = aA.z; topIdx = 2; } else if (aA.z > m2) { m2 = aA.z; }
    if (aA.w > m1) { m2 = m1; m1 = aA.w; topIdx = 3; } else if (aA.w > m2) { m2 = aA.w; }
    if (aB.x > m1) { m2 = m1; m1 = aB.x; topIdx = 4; } else if (aB.x > m2) { m2 = aB.x; }
    if (aB.y > m1) { m2 = m1; m1 = aB.y; topIdx = 5; } else if (aB.y > m2) { m2 = aB.y; }
    if (aB.z > m1) { m2 = m1; m1 = aB.z; topIdx = 6; } else if (aB.z > m2) { m2 = aB.z; }
    if (aB.w > m1) { m2 = m1; m1 = aB.w; topIdx = 7; } else if (aB.w > m2) { m2 = aB.w; }

    if (parity == 0)
    {
        d1Sq = baseD2;
        d2Sq = baseD2 + 2.0 - 2.0 * (m1 + m2);
        chash = (uint(zA.x) * 73856093u) ^ (uint(zA.y) * 19349663u) ^ (uint(zA.z) * 83492791u) ^ (uint(zB.x) * 2654435761u);
    }
    else
    {
        d1Sq = baseD2 + 1.0 - 2.0 * m1;
        d2Sq = baseD2 + 1.0 - 2.0 * m2;
        chash = (uint(zA.x) * 73856093u) ^ (uint(zA.y) * 19349663u) ^ (uint(zB.y) * 83492791u) ^ (uint(topIdx + 1) * 2246822519u);
    }
}

float om_d8_dist2(float4 xA, float4 xB, float sh, out uint chash)
{
    float d1, d2;
    om_d8_f1f2(xA, xB, sh, d1, d2, chash);
    return d1;
}

float om_e8_voronoi_edge(float4 xA, float4 xB, out float f1Out, out float spinorCoset, out uint cellHash)
{
    float dA1, dA2, dB1, dB2;
    uint hA = 0u, hB = 0u;
    om_d8_f1f2(xA, xB, 0.0, dA1, dA2, hA);
    om_d8_f1f2(xA, xB, 0.5, dB1, dB2, hB);

    if (dB1 < dA1)
    {
        spinorCoset = 1.0;
        cellHash = hB ^ 0x9E3779B9u;
        f1Out = sqrt(max(0.0, dB1));
        float f2 = sqrt(max(0.0, min(dB2, dA1)));
        return max(0.0, f2 - f1Out);
    }
    spinorCoset = 0.0;
    cellHash = hA;
    f1Out = sqrt(max(0.0, dA1));
    float f2A = sqrt(max(0.0, min(dA2, dB1)));
    return max(0.0, f2A - f1Out);
}

float om_e8_voronoi(float4 xA, float4 xB, out float spinorCoset, out uint cellHash)
{
    float f1;
    om_e8_voronoi_edge(xA, xB, f1, spinorCoset, cellHash);
    return f1;
}

// ============================================================================
// TASK #5 CLOSED-FORM 4D HOPF, 24/120-CELL (H4), POINCARE {5,3,4} & 24D LEECH/GOLAY
// ============================================================================
static const uint OM_GOLAY_P12[12] = {
    0xFFEu, 0x477u, 0x8EDu, 0x1DBu, 0x3B5u, 0x769u,
    0xED1u, 0xDA3u, 0xB47u, 0x68Fu, 0xD1Du, 0xA3Bu
};

float4 om_qmul(float4 a, float4 b)
{
    return float4(
        a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
        a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
        a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
        a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
}

// 1) Stereographic Hopf Fibration (S^3 -> S^2) Linked Villarceau Fibers on Clifford Tori
void om_hopf_villarceau_field(float3 q, float4 qBus, float spinPhase, out float3 baseS2, out float ringSeam)
{
    baseS2 = float3(0.0, 0.0, 1.0);
    ringSeam = 0.5;

    float r2 = dot(q, q);
    float invD = 1.0 / (1.0 + r2);
    float4 z4 = float4(2.0 * q.x * invD, 2.0 * q.y * invD, 2.0 * q.z * invD, (r2 - 1.0) * invD);

    float qLen = length(qBus);
    float4 qNorm = (qLen > 0.25) ? (qBus / qLen) : float4(0.0, 0.0, 0.0, 1.0);
    float cS = cos(spinPhase * 0.5), sS = sin(spinPhase * 0.5);
    float4 qSpin = float4(sS * 0.5773503, sS * 0.5773503, sS * 0.5773503, cS);
    float4 zr = om_qmul(om_qmul(qSpin, qNorm), z4);

    float3 b = float3(
        2.0 * (zr.x * zr.z + zr.y * zr.w),
        2.0 * (zr.y * zr.z - zr.x * zr.w),
        zr.x * zr.x + zr.y * zr.y - zr.z * zr.z - zr.w * zr.w);
    baseS2 = normalize(b + float3(0.0, 0.0, 1e-6));

    // 6 equatorial Villarceau fibers on the sqrt(2) Clifford torus + 2 linked polar core fibers
    float az = atan2(baseS2.y, baseS2.x + 1e-6);
    float sec6 = floor(az / 1.04719755 + 0.5) * 1.04719755;
    float3 nEq = float3(cos(sec6), sin(sec6), 0.0);
    float dotEq = dot(baseS2, nEq);
    float dotPol = abs(baseS2.z);
    float bestDot = max(dotEq, dotPol * 0.985);
    ringSeam = sqrt(max(0.0, 0.5 * (1.0 - bestDot)));
}

float om_hopf_villarceau_sdf(float3 q, float4 qBus, float spinPhase, float scaleR, out float3 baseS2Out)
{
    baseS2Out = float3(0.0, 0.0, 1.0);
    float rEucl = length(q);
    float maxShell = 1.92 * scaleR;
    if (rEucl > maxShell + 0.35)
    {
        return rEucl - maxShell;
    }
    float3 u = q / max(0.35, scaleR);
    float ringSeam;
    om_hopf_villarceau_field(u, qBus, spinPhase, baseS2Out, ringSeam);
    float r2 = dot(u, u);
    float conf = max(0.35, scaleR) * 0.5 * (1.0 + r2);
    float tubeR = 0.095 + 0.030 * saturate(U[15].z * 0.65);
    float dRings = (ringSeam - tubeR) * min(conf, 2.15) * 0.68;
    return max(dRings, rEucl - maxShell);
}

// 2) Exact 16-Fold H4 = [5,3,3] Coxeter Mirror Chamber (120-Cell & 600-Cell) + Hurwitz 24-Cell {3,4,3}
float4 om_h4_fold16(float4 v)
{
    const float4 n1 = float4(-0.8090170, 0.3090170, 0.5000000, 0.0);
    const float4 n3 = float4( 0.0,      -0.8090170, 0.5000000, 0.3090170);
    float4 f = v;
    [loop]
    for (int it = 0; it < 16; it++)
    {
        f.x = abs(f.x);
        float d1 = dot(f.xyz, n1.xyz);
        if (d1 < 0.0) f.xyz -= 2.0 * d1 * n1.xyz;
        if (f.z > 0.0) f.z = -f.z;
        float d3 = dot(f.yzw, n3.yzw);
        if (d3 < 0.0) f.yzw -= 2.0 * d3 * n3.yzw;
    }
    return f;
}

float om_polychora_24_120_sdf(float3 q, float4 qBus, float spinA, float rad, float k3Mode, out float4 foldedOut, out float strutGlowOut)
{
    foldedOut = float4(0.0, 0.0, 0.0, 1.0);
    strutGlowOut = 0.0;

    // Double-isoclinic SO(4) rotation: v4 = qL * p4 * qR
    float sliceW = 0.22 * rad * sin(spinA * 0.618034 + U[8].y);
    float4 p4 = float4(q, sliceW);
    float cL = cos(spinA * 0.5), sL = sin(spinA * 0.5);
    float cR = cos(spinA * 0.309017), sR = sin(spinA * 0.309017);
    float4 qL = float4(0.0, sL * 0.7071068, sL * 0.7071068, cL);
    float4 qR = float4(sR * 0.8090170, 0.0, sR * 0.5877853, cR);
    float qLen = length(qBus);
    if (qLen > 0.25) qL = om_qmul(qBus / qLen, qL);
    float4 v4 = om_qmul(om_qmul(qL, p4), qR);

    // Exact O(1) Hurwitz 24-cell {3,4,3} crystal core (8 axis + 16 half-integer units)
    float4 av = abs(v4);
    float maxAxis = max(max(av.x, av.y), max(av.z, av.w));
    float sumHalf = 0.5 * (av.x + av.y + av.z + av.w);
    float d24Core = max(maxAxis, sumHalf) - 0.76 * rad;

    // Exact 16-fold H4 Coxeter 120-cell {5,3,3} dodecaplex edge struts [u0, u1] + cell shell
    float4 f = om_h4_fold16(v4);
    foldedOut = f;
    const float4 u0 = float4(0.1350454, 0.3535534, 0.0, 0.9256148);
    const float4 seg = float4(-0.1350454, 0.0032687, 0.0, 0.0085576);
    const float segLen2 = 0.0183212;
    float4 a0 = u0 * rad;
    float4 sg = seg * rad;
    float tSeg = saturate(dot(f - a0, sg) / max(1e-5, segLen2 * rad * rad));
    float d120Strut = length(f - (a0 + tSeg * sg)) - 0.052 * rad;
    strutGlowOut = exp(-max(0.0, d120Strut) * 18.0);

    if (k3Mode < 0.25) return d24Core * 0.90;
    if (k3Mode > 1.45) return max(d120Strut, length(q) - 1.68 * rad) * 0.90;
    return min(d24Core, max(d120Strut, length(q) - 1.65 * rad)) * 0.90;
}

// 3) 3D Hyperbolic Poincare Ball {5,3,4} & {4,3,5} Mobius Sphere-Inversion Portal (arXiv:1511.02851)
float3 om_mobius_add3(float3 u, float3 b)
{
    float u2 = dot(u, u);
    float b2 = dot(b, b);
    float ub = dot(u, b);
    float denom = max(1e-5, 1.0 + 2.0 * ub + u2 * b2);
    return ((1.0 + 2.0 * ub + b2) * u + (1.0 - u2) * b) / denom;
}

float om_poincare_honeycomb_sdf(float3 q, float portalR, float boostPhase, float k3Mode, out float3 foldedU, out float invSteps)
{
    foldedU = float3(0.0, 0.0, 0.0);
    invSteps = 0.0;

    float r = length(q);
    float dOuter = r - portalR;
    if (r >= portalR * 0.985)
    {
        return dOuter;
    }

    float3 u = q / max(0.35, portalR);
    // Smooth Lorentz/Mobius hyperbolic glide strictly inside |b| <= 0.34
    float3 bBoost = float3(0.24 * sin(boostPhase * 0.55), 0.16 * cos(boostPhase * 0.41), 0.24 * cos(boostPhase * 0.63));
    u = om_mobius_add3(u, bBoost);
    float scaleJ = (1.0 / max(0.35, portalR)) * (1.0 / max(0.25, 1.0 - dot(bBoost, bBoost)));

    bool mode435 = (k3Mode >= 0.95);
    const float3 h3M1 = float3(-0.8090170, 0.3090170, 0.5000000);
    const float3 c534 = float3(0.0, 1.27201965, -0.78615138);
    const float rho2_534 = 1.23606798;
    const float rho_534  = 1.11178594;

    const float3 c435 = float3(2.05817103, 0.0, 0.0);
    const float rho2_435 = 3.23606798;
    const float rho_435  = 1.79890744;

    [loop]
    for (int it = 0; it < 12; it++)
    {
        if (!mode435)
        {
            u.x = abs(u.x);
            if (u.z > 0.0) u.z = -u.z;
            float d1 = dot(u, h3M1);
            if (d1 < 0.0) u -= 2.0 * d1 * h3M1;
            float3 diff = u - c534;
            float d2 = max(1e-6, dot(diff, diff));
            if (d2 < rho2_534)
            {
                float k = rho2_534 / d2;
                u = c534 + diff * k;
                scaleJ *= k;
                invSteps += 1.0;
            }
            else break;
        }
        else
        {
            u = abs(u);
            if (u.x < u.y) { float t = u.x; u.x = u.y; u.y = t; }
            if (u.y < u.z) { float t = u.y; u.y = u.z; u.z = t; }
            if (u.x < u.y) { float t = u.x; u.x = u.y; u.y = t; }
            float3 diff = u - c435;
            float d2 = max(1e-6, dot(diff, diff));
            if (d2 < rho2_435)
            {
                float k = rho2_435 / d2;
                u = c435 + diff * k;
                scaleJ *= k;
                invSteps += 1.0;
            }
            else break;
        }
    }

    foldedU = u;
    float dHoney = (!mode435)
        ? (((length(u - c534) - rho_534) - 0.058) / scaleJ * 0.86)
        : (((length(u - c435) - rho_435) - 0.088) / scaleJ * 0.76);
    return max(dOuter, dHoney);
}

// 4) 24D Leech Lattice (3 x E8 Turyn) + Self-Dual [24,12,8] Extended Binary Golay Voronoi
uint om_popcnt24(uint v)
{
    uint x = v & 0xFFFFFFu;
    x = x - ((x >> 1u) & 0x555555u);
    x = (x & 0x333333u) + ((x >> 2u) & 0x333333u);
    return (((x + (x >> 4u)) & 0x0F0F0Fu) * 0x010101u) >> 16u;
}

uint om_golay24_encode(uint msg12)
{
    uint m = msg12 & 0xFFFu;
    uint p = 0u;
    [loop]
    for (int b = 0; b < 12; b++)
    {
        if (((m >> uint(b)) & 1u) != 0u) p ^= OM_GOLAY_P12[b];
    }
    return m | ((p & 0xFFFu) << 12u);
}

float om_leech24_golay_voronoi(float3 p3, float phasonT, out float f1Out, out float dodecadTag, out uint cellHash)
{
    f1Out = 0.0;
    dodecadTag = 0.0;
    cellHash = 0u;

    float eMin = 9.0, eMid = 9.0, f1SqSum = 0.0;
    uint hMix = 0u;
    [loop]
    for (int oct = 0; oct < 3; oct++)
    {
        float4 aOct;
        float4 bOct;
        if (oct == 0)
        {
            aOct = float4(p3.x, p3.y, p3.z, (p3.x + p3.y + p3.z) * 0.5773503 + phasonT * 0.25);
            bOct = float4((p3.x - p3.z) * 0.7071068, p3.y * 0.8090170 - p3.x * 0.5877853, p3.z * 0.8090170 + p3.y * 0.5877853, U[8].y * 0.5);
        }
        else if (oct == 1)
        {
            aOct = float4(p3.z * 1.1180, -p3.x * 0.9511, p3.y * 1.0515, phasonT * 0.19 - p3.x * 0.3090);
            bOct = float4(p3.y * 0.6180 + p3.z * 0.7862, -p3.z * 0.6180 + p3.x * 0.7862, (p3.x - p3.y) * 0.7071, U[8].z * 0.5);
        }
        else
        {
            aOct = float4((p3.y + p3.z) * 0.7639, (p3.z - p3.x) * 0.7639, (p3.x + p3.y) * 0.7639, -phasonT * 0.15);
            bOct = float4(p3.x * 0.5257 + p3.y * 0.8507, p3.y * 0.5257 - p3.z * 0.8507, p3.z * 0.5257 + p3.x * 0.8507, (U[8].y - U[8].z) * 0.35);
        }
        float f1k, spk;
        uint hk;
        float ek = om_e8_voronoi_edge(aOct, bOct, f1k, spk, hk);
        f1SqSum += f1k * f1k;
        if (ek < eMin) { eMid = eMin; eMin = ek; }
        else if (ek < eMid) { eMid = ek; }
        hMix ^= (hk >> uint(oct * 4)) ^ (hk * (uint(oct) * 2246822519u + 1u));
    }

    uint msg12 = hMix & 0xFFFu;
    uint cw24 = om_golay24_encode(msg12);
    uint wt = om_popcnt24(cw24);
    dodecadTag = (wt == 12u) ? 1.0 : 0.0;
    cellHash = cw24 ^ hMix;
    f1Out = sqrt(f1SqSum * (1.0 / 3.0));
    return (dodecadTag > 0.5) ? eMin : (0.5 * (eMin + eMid));
}


float TriGridSeam(float2 p)
{
    float a = p.x;
    float b = -0.5 * p.x + 0.8660254 * p.y;
    float c = -0.5 * p.x - 0.8660254 * p.y;
    float da = abs(a - floor(a + 0.5));
    float db = abs(b - floor(b + 0.5));
    float dc = abs(c - floor(c + 0.5));
    return min(da, min(db, dc));
}

float4 SampleMoatSmooth(float2 worldXZ)
{
    float2 uv = saturate((worldXZ / 28.0) + 0.5) * 255.0;
    uint x0 = (uint)floor(uv.x);
    uint y0 = (uint)floor(uv.y);
    uint x1 = min(255u, x0 + 1u);
    uint y1 = min(255u, y0 + 1u);
    float fx = uv.x - float(x0);
    float fy = uv.y - float(y0);
    fx = fx * fx * (3.0 - 2.0 * fx);
    fy = fy * fy * (3.0 - 2.0 * fy);
    float4 a = lerp(MoatField[y0 * 256u + x0], MoatField[y0 * 256u + x1], fx);
    float4 b = lerp(MoatField[y1 * 256u + x0], MoatField[y1 * 256u + x1], fx);
    return lerp(a, b, fy);
}

void EvalCourtyardStone(float2 xz, float coneW, out float3 stoneCol, out float seamGlow, out float mortarJoint, out float2 bevelGrad)
{
    int sym = (int)round(U[9].x);
    float sc = max(0.25, U[9].y);
    float mortarW = max(0.014, U[9].z);
    float2 p = xz * sc;

    seamGlow = 0.0;
    mortarJoint = 0.0;
    bevelGrad = float2(0.0, 0.0);
    stoneCol = float3(0.085, 0.105, 0.145);
    float edgeMetric = 1.0;

    if (sym == 0)
    {
        float dMin = 9.0;
        float perpX = 0.0, perpY = 0.0;
        float2 bestDir = float2(1.0, 0.0);
        float bestSign = 1.0;
        [unroll]
        for (int r = 0; r < 5; r++)
        {
            float gShift = (r == 0) ? U[7].x : ((r == 1) ? U[7].y : ((r == 2) ? U[7].z : ((r == 3) ? U[7].w : U[8].x)));
            float pr = dot(p, PENTA_DIR[r]) + gShift;
            float idx = floor(pr + 0.5);
            float diff = pr - idx;
            float fr = abs(diff);
            if (fr < dMin) { dMin = fr; bestDir = PENTA_DIR[r]; bestSign = (diff >= 0.0) ? 1.0 : -1.0; }
            perpX += idx * PENTA_CONJ[r].x;
            perpY += idx * PENTA_CONJ[r].y;
        }
        edgeMetric = dMin;
        bevelGrad = bestDir * bestSign;
        float perpR = sqrt(perpX * perpX + perpY * perpY);
        float isThick = frac(perpR * 0.61803399);
        stoneCol = lerp(float3(0.052, 0.070, 0.110), float3(0.108, 0.086, 0.145), smoothstep(0.25, 0.75, isThick));
        mortarJoint = smoothstep(mortarW + coneW, max(0.0, mortarW - coneW), dMin);
        float phasonFlip = exp(-abs(perpR - (1.85 + 0.35 * sin(U[0].z * 0.4))) * 4.5);
        seamGlow = mortarJoint * (0.35 + 0.65 * phasonFlip);
    }
    else if (sym == 1)
    {
        float sp = 0.0, f1 = 0.0; uint ch = 0u;
        float2 pS = p * 1.45;
        float edgeD = om_e8_voronoi_edge(
            float4(pS.x, pS.y, (pS.x - pS.y) * 0.7071068, (pS.x + pS.y) * 0.7071068),
            float4(pS.x * 0.618034, -pS.y * 0.618034, U[8].y * 0.55, U[8].z * 0.55),
            f1, sp, ch);
        edgeMetric = edgeD;
        mortarJoint = smoothstep(mortarW * 1.35 + coneW, max(0.0, mortarW * 1.35 - coneW), edgeD);
        float tint = float(ch & 255u) / 255.0;
        float innerFacet = saturate(1.0 - f1 * 0.55);
        stoneCol = lerp(float3(0.048, 0.068, 0.108), float3(0.125, 0.092, 0.148), sp * 0.58 + tint * 0.42) * (0.85 + 0.24 * innerFacet);
        bevelGrad = normalize(xz + float2(1e-4, 1e-4)) * (0.5 - sp);
        seamGlow = mortarJoint * (0.45 + 0.55 * sp);
    }
    else if (sym == 2)
    {
        float invPhi2 = 0.38196601;
        float e0 = TriGridSeam(p * 1.35);
        float2 rp = float2(p.x * 0.8660254 - p.y * 0.5, p.x * 0.5 + p.y * 0.8660254) * (1.35 * invPhi2) + float2(0.371, -0.219) + U[8].yz * 0.15;
        float e1 = TriGridSeam(rp);
        float sel = sin(p.x * 1.1547 + rp.y * OM_PHI) + cos(p.y * 1.1547 - rp.x * OM_PHI);
        float dHat = (sel > 0.18) ? e0 : min(e0 + 0.055, e1 * 1.35);
        edgeMetric = dHat;
        mortarJoint = smoothstep(mortarW + coneW, max(0.0, mortarW - coneW), dHat);
        stoneCol = lerp(float3(0.046, 0.090, 0.104), float3(0.104, 0.116, 0.078), smoothstep(-0.6, 0.8, sel));
        bevelGrad = float2(cos(sel * 3.14), sin(sel * 3.14));
        seamGlow = mortarJoint * (0.38 + 0.55 * exp(-e1 * 8.0));
    }
    else if (sym == 3)
    {
        float bow = 0.16 * max(0.2, U[9].w);
        float2 q = p * 1.25;
        float2 curl = float2(
            q.x + bow * sin(q.y * 2.094395 + q.x * 0.618034 + U[8].y),
            q.y - bow * sin(q.x * 2.094395 - q.y * 0.618034 + U[8].z));
        float dBase = TriGridSeam(curl);
        float2 mys = float2(curl.x * 0.8750 - curl.y * 0.4841, curl.x * 0.4841 + curl.y * 0.8750) * 0.366025 + float2(0.513, -0.287);
        float dSup = TriGridSeam(mys);
        float dSpec = min(dBase, dSup * 1.65 + 0.022);
        edgeMetric = dSpec;
        float mystic = smoothstep(0.24, 0.06, dSup);
        mortarJoint = smoothstep(mortarW + coneW, max(0.0, mortarW - coneW), dSpec);
        stoneCol = lerp(float3(0.064, 0.060, 0.114), float3(0.148, 0.098, 0.050), mystic);
        bevelGrad = normalize(curl - q + float2(1e-4, 1e-4));
        seamGlow = mortarJoint * (0.40 + 0.60 * mystic);
    }
    else if (sym == 4)
    {
        // Mode 4: Stereographic Hopf Fibration (S^3 -> S^2) Villarceau Cyclide Floor Tiles
        float3 bS2; float hSeam;
        om_hopf_villarceau_field(float3(p.x * 0.26, 0.18 * sin(U[0].z * 0.35), p.y * 0.26), U[6], U[0].z * 0.28, bS2, hSeam);
        float latRings = abs(frac(bS2.z * 2.5 + 0.5) - 0.5) * 0.22;
        float dHopf = min(hSeam * 0.85, latRings);
        edgeMetric = dHopf;
        mortarJoint = smoothstep(mortarW + coneW, max(0.0, mortarW - coneW), dHopf);
        float azHue = frac(atan2(bS2.y, bS2.x + 1e-6) / OM_TAU + 0.5);
        stoneCol = lerp(float3(0.045, 0.078, 0.128), om_hsv2rgb(float3(azHue, 0.45, 0.14)), 0.65);
        bevelGrad = bS2.xy;
        seamGlow = mortarJoint * (0.48 + 0.52 * abs(bS2.z));
    }
    else if (sym == 5)
    {
        // Mode 5: 4D 24-Cell (Hurwitz) & 120-Cell (H4 Coxeter) Projected Polychoron Flagstones
        float4 v4 = normalize(float4(p.x * 0.32, p.y * 0.32, 0.55 + 0.15 * sin(U[0].z * 0.25), 0.55));
        float4 f4 = om_h4_fold16(v4);
        float dH4 = min(min(f4.x, abs(f4.z)), abs(f4.x * (-0.8090170) + f4.y * 0.3090170 + f4.z * 0.5)) * 1.45;
        edgeMetric = dH4;
        mortarJoint = smoothstep(mortarW + coneW, max(0.0, mortarW - coneW), dH4);
        float cellTone = saturate((f4.w - 0.86) * 8.0);
        stoneCol = lerp(float3(0.056, 0.068, 0.112), float3(0.142, 0.104, 0.062), cellTone);
        bevelGrad = normalize(xz + float2(1e-4, 1e-4)) * (cellTone - 0.5);
        seamGlow = mortarJoint * (0.45 + 0.55 * cellTone);
    }
    else if (sym == 6)
    {
        // Mode 6: Conformal Poincare Disk {5,3,4} & {4,3,5} Hyperbolic Sphere-Inversion Mosaic (arXiv:1511.02851)
        float2 diskUv = xz / 17.6;
        float rD = length(diskUv);
        if (rD > 0.94) diskUv *= (0.94 / rD);
        float3 fU; float invSteps;
        float dHyp = abs(om_poincare_honeycomb_sdf(float3(diskUv.x, 0.04, diskUv.y), 1.0, U[0].z * 0.18 * U[15].y, U[15].z, fU, invSteps)) * 2.2;
        edgeMetric = dHyp;
        mortarJoint = smoothstep(mortarW + coneW, max(0.0, mortarW - coneW), dHyp);
        float parity = frac(invSteps * 0.5) * 2.0;
        stoneCol = lerp(float3(0.038, 0.088, 0.118), float3(0.112, 0.072, 0.148), parity);
        bevelGrad = fU.xz;
        seamGlow = mortarJoint * (0.42 + 0.58 * saturate(invSteps * 0.16));
    }
    else
    {
        // Mode 7: 24D Leech Lattice + Extended Binary Golay [24,12,8] Chiral Voronoi Tiles
        float f1L = 0.0, dod = 0.0; uint hL = 0u;
        float e24 = om_leech24_golay_voronoi(float3(p.x * 1.15, 0.25 * U[8].z, p.y * 1.15), U[0].z * 0.22, f1L, dod, hL);
        edgeMetric = e24;
        mortarJoint = smoothstep(mortarW * 1.25 + coneW, max(0.0, mortarW * 1.25 - coneW), e24);
        float subTint = float(hL & 255u) / 255.0;
        stoneCol = lerp(float3(0.042, 0.092, 0.098), float3(0.132, 0.096, 0.054), dod * 0.65 + subTint * 0.35);
        bevelGrad = normalize(xz + float2(1e-4, 1e-4)) * (dod - 0.5);
        seamGlow = mortarJoint * (0.46 + 0.54 * dod);
    }

    float grain = sin(xz.x * 23.7 + sin(xz.y * 17.3)) * cos(xz.y * 25.1 - sin(xz.x * 14.9));
    float lip = smoothstep(mortarW * 2.6, mortarW * 1.05, edgeMetric) * (1.0 - mortarJoint);
    bevelGrad *= lip * 0.24;
    stoneCol *= (0.93 + 0.07 * grain + 0.14 * lip);
    stoneCol = lerp(stoneCol, float3(0.012, 0.016, 0.026), mortarJoint * 0.90);
}

float EvalFastOccluderSdf(float3 p)
{
    float r = length(p.xz);
    float dFloor = p.y - ((r > 11.35) ? (0.20 + clamp((r - 11.4) * 0.28, 0.0, 2.2)) : -0.62);
    float dDais = om_sdRoundCyl(p - float3(0.0, -0.05, 0.0), 4.60, 0.60, 0.04);
    float3 qBr = float3(min(abs(p.x), abs(p.z)), p.y - 0.22, max(abs(p.x), abs(p.z)) - 7.9);
    float dBr = max(abs(qBr.x) - 1.36, max(abs(qBr.y) - 0.24, abs(qBr.z) - 3.65));
    float dMono = length(p - float3(0.0, 2.78, 0.0)) - 1.38 * max(0.35, U[15].x);
    float dScene = min(min(dFloor, dDais), min(dBr, dMono));
    if (r > 14.0 && r < 18.4)
    {
        float sector = OM_TAU / 10.0;
        float snap = floor(atan2(p.z, p.x + 1e-6) / sector + 0.5) * sector;
        float2 lp = p.xz - float2(cos(snap), sin(snap)) * 16.2;
        dScene = min(dScene, max(length(lp) - 0.48, abs(p.y - 2.65) - 2.60));
    }
    return dScene;
}

float EvalCrystalMonolithSdf(float3 p)
{
    int sym = (int)round(U[9].x);
    float3 q = p - float3(0.0, 2.78 + 0.14 * sin(U[0].z * 1.35), 0.0);

    if (sym == 4)
    {
        float3 bS2;
        return om_hopf_villarceau_sdf(q, U[6], U[0].z * (0.45 + 0.35 * U[15].y), 1.02 * max(0.45, U[15].x), bS2);
    }
    if (sym == 5)
    {
        float4 fOut; float sGlow;
        return om_polychora_24_120_sdf(q, U[6], U[0].z * (0.42 + 0.30 * U[15].y), 1.34 * max(0.45, U[15].x), U[15].z, fOut, sGlow);
    }
    if (sym == 6)
    {
        float3 fU; float invSteps;
        return om_poincare_honeycomb_sdf(q, 1.56 * max(0.45, U[15].x), U[0].z * U[15].y, U[15].z, fU, invSteps);
    }

    float spin = U[0].z * U[16].w;
    float cs = cos(spin), sn = sin(spin);
    q.xz = float2(q.x * cs - q.z * sn, q.x * sn + q.z * cs);
    q.y *= 0.78;

    static const float3 icosa[6] = {
        float3(0.0, 0.5257311,  0.8506508),
        float3(0.0, 0.5257311, -0.8506508),
        float3(0.5257311,  0.8506508, 0.0),
        float3(0.5257311, -0.8506508, 0.0),
        float3( 0.8506508, 0.0, 0.5257311),
        float3(-0.8506508, 0.0, 0.5257311)
    };

    float rad = 1.28 * max(0.35, U[15].x);
    float dPoly = -1e6;
    [loop]
    for (int m = 0; m < 6; m++)
    {
        float ph = 0.13 * U[15].y * cos(U[0].z * 0.7 + float(m) * 1.0471976 + U[8].y);
        float dm = abs(dot(q, icosa[m])) - (rad + ph);
        dPoly = max(dPoly, dm);
    }

    if (sym == 7)
    {
        // Mode 7: 24D Leech Lattice + Golay [24,12,8] Faceted Dodecad-Octad Hypercrystal
        float f1L, dod; uint hL;
        float e24 = om_leech24_golay_voronoi(q * 1.45, U[0].z * 0.25, f1L, dod, hL);
        float leechGroove = 0.045 - e24;
        float carved24 = lerp(dPoly, max(dPoly, leechGroove * 0.55), saturate(U[15].z * 0.75));
        return carved24 * 0.85;
    }

    float gyr = abs(om_sdGyroid(q, U[16].x)) - max(0.02, U[16].y);
    float carved = lerp(dPoly, max(dPoly, -gyr * 0.42), saturate(U[15].z));
    return carved * 0.85;
}

float3 CalcArenaNormal(float3 p)
{
    const float e = 0.0035;
    float3 n = float3(0.0, 0.0, 0.0);
    [loop]
    for (int i = 0; i < 4; i++)
    {
        int mTmp;
        float3 kv = float3(
            ((((i + 3) >> 1) & 1) != 0) ? 1.0 : -1.0,
            (((i >> 1) & 1) != 0) ? 1.0 : -1.0,
            ((i & 1) != 0) ? 1.0 : -1.0);
        n += kv * EvalArenaArchitectureSdf(p + kv * e, mTmp);
    }
    return normalize(n + float3(0.0, 1e-5, 0.0));
}

float3 CalcMonolithNormal(float3 p)
{
    const float e = 0.003;
    float3 n = float3(0.0, 0.0, 0.0);
    [loop]
    for (int i = 0; i < 4; i++)
    {
        float3 kv = float3(
            ((((i + 3) >> 1) & 1) != 0) ? 1.0 : -1.0,
            (((i >> 1) & 1) != 0) ? 1.0 : -1.0,
            ((i & 1) != 0) ? 1.0 : -1.0);
        n += kv * EvalCrystalMonolithSdf(p + kv * e);
    }
    return normalize(n + float3(0.0, 1e-5, 0.0));
}

float CalcSoftShadow(float3 ro, float3 rd)
{
    float res = 1.0;
    float t = 0.08;
    [loop]
    for (int i = 0; i < 8; i++)
    {
        float h = EvalFastOccluderSdf(ro + rd * t);
        if (h < 0.003) return 0.14;
        res = min(res, 9.0 * h / t);
        t += clamp(h, 0.10, 0.85);
        if (t > 11.0) break;
    }
    return clamp(res, 0.14, 1.0);
}

float CalcArenaAO(float3 pos, float3 nor)
{
    float occ = 0.0;
    float sca = 1.0;
    [loop]
    for (int i = 0; i < 3; i++)
    {
        float h = 0.05 + 0.13 * float(i);
        float d = EvalFastOccluderSdf(pos + h * nor);
        occ += (h - d) * sca;
        sca *= 0.65;
    }
    return saturate(1.0 - 2.2 * occ);
}

float3 EvalSkyReflectFast(float3 rd)
{
    float up = saturate(rd.y);
    float3 sky = lerp(float3(0.042, 0.078, 0.132), float3(0.012, 0.022, 0.055), pow(max(1e-4, up), 0.65));
    if (rd.y > 0.01)
    {
        float2 suv = rd.xz / (rd.y + 0.25);
        float t = U[0].z * U[12].x;
        float w0 = cos(suv.x * 1.4 + t * 0.45 + U[7].x) + cos(suv.y * 1.3 - t * 0.38 + U[7].y);
        float curtain = exp(-abs(w0) * 1.15) * smoothstep(0.01, 0.30, rd.y) * exp(-length(suv) * 0.20);
        float3 aurCol = lerp(float3(0.08, 0.90, 0.70), float3(0.54, 0.24, 0.96), saturate(0.5 + 0.5 * sin(suv.x + U[0].z * 0.3)));
        sky += aurCol * curtain * U[14].w * (0.55 + 0.55 * U[3].x);
    }
    float3 sunDir = normalize(U[17].xyz);
    float sunDot = saturate(dot(rd, sunDir));
    sky += float3(1.45, 1.25, 0.88) * pow(max(1e-4, sunDot), 96.0) * 1.65;
    return sky;
}

float3 EvalSovngardeAuroraSky(float3 rd)
{
    float up = saturate(rd.y);
    float3 sky = lerp(float3(0.042, 0.078, 0.132), float3(0.012, 0.022, 0.055), pow(max(1e-4, up), 0.65));

    uint starHash = 0u; float sp = 0.0;
    float starD = om_e8_voronoi(float4(rd * 26.0, 1.0), float4(rd.zyx * 17.0, 0.5), sp, starHash);
    float star = smoothstep(0.095, 0.012, starD) * smoothstep(0.08, 0.45, up);
    sky += float3(0.95, 1.05, 1.22) * star * 0.90;

    if (rd.y > 0.02)
    {
        float2 skyUV = rd.xz / (rd.y + 0.22);
        float t = U[0].z * U[12].x;
        float qSum = 0.0, qAlt = 0.0;
        [unroll]
        for (int k = 0; k < 5; k++)
        {
            float gShift = (k == 0) ? U[7].x : ((k == 1) ? U[7].y : ((k == 2) ? U[7].z : ((k == 3) ? U[7].w : U[8].x)));
            float phase = dot(skyUV * 1.45, PENTA_DIR[k]) + t * (0.4 + 0.12 * float(k)) + gShift;
            qSum += cos(phase);
            qAlt += sin(phase * OM_PHI);
        }
        float curtain = exp(-abs(qSum) * 0.92) * saturate(0.55 + 0.45 * sin(qAlt));
        float fftBoost = 0.55 + 0.60 * (U[4].x + U[4].z + U[5].x + U[5].z);
        float3 aurCol = lerp(float3(0.08, 1.08, 0.82), float3(0.64, 0.28, 1.16), saturate(0.5 + 0.5 * sin(qAlt * 0.7 + U[0].z * 0.3)));
        aurCol = lerp(aurCol, float3(1.25, 0.88, 0.26), saturate(U[14].x * 0.65));
        float fade = smoothstep(0.02, 0.32, rd.y) * exp(-length(skyUV) * 0.18);
        sky += aurCol * curtain * fade * U[14].w * fftBoost * 1.05;
    }

    float3 sunDir = normalize(U[17].xyz);
    float sunDot = saturate(dot(rd, sunDir));
    sky += float3(1.85, 1.55, 1.05) * pow(max(1e-4, sunDot), 120.0) * 1.80 + float3(0.24, 0.40, 0.65) * pow(max(1e-4, sunDot), 8.0) * 0.28;
    return sky;
}

void EvalCustomOmsh(float3 wp, float3 rd, inout float3 col)
{
");
            sb.Append(safeSnippet).Append("\r\n}\r\n");
            sb.Append(@"
[numthreads(16, 16, 1)]
void CSArenaRaymarch(uint3 dtid : SV_DispatchThreadID)
{
    uint w = (uint)max(1.0, U[0].x);
    uint h = (uint)max(1.0, U[0].y);
    if (dtid.x >= w || dtid.y >= h) return;

    float2 ndc = float2(
        ((float(dtid.x) + 0.5) / float(w)) * 2.0 - 1.0,
        1.0 - ((float(dtid.y) + 0.5) / float(h)) * 2.0);
    ndc.x *= float(w) / float(h);

    float fovTan = max(0.25, U[0].w);
    float3 ro = U[1].xyz;
    float3 camFwd = normalize(U[18].xyz);
    float3 camRight = normalize(U[19].xyz);
    float3 camUp = normalize(cross(camFwd, camRight));
    float3 rd = normalize(camFwd + ndc.x * fovTan * camRight + ndc.y * fovTan * camUp);

    float3 impOrig = U[13].xyz;
    float impRad = U[14].y;
    float impStr = U[14].x;

    // 1. Mode-Tight Analytic Monolith Bounding Sphere + 1-Step Secant Root Refinement
    int activeSymTop = (int)round(U[9].x);
    float3 monoCenter = float3(0.0, 2.78 + 0.14 * sin(U[0].z * 1.35), 0.0);
    float3 oc = ro - monoCenter;
    float bSq = dot(oc, rd);
    float k1Safe = max(0.45, U[15].x);
    float boundR = (activeSymTop == 6) ? (1.64 * k1Safe) : ((activeSymTop == 5) ? (2.32 * k1Safe) : (2.06 * k1Safe));
    float cSq = dot(oc, oc) - boundR * boundR;
    float disc = bSq * bSq - cSq;
    float tMono = 1e6;
    bool hitMono = false;
    if (disc > 0.0)
    {
        float tStart = max(U[2].y, -bSq - sqrt(disc));
        float tEnd   = min(60.0, -bSq + sqrt(disc));
        if (tEnd > tStart)
        {
            float tm = tStart;
            float prevTm = tm;
            float prevDm = 1.0;
            int maxMonoSteps = (((int)round(U[9].x)) >= 4) ? 68 : 42;
            [loop]
            for (int i = 0; i < maxMonoSteps; i++)
            {
                float dm = EvalCrystalMonolithSdf(ro + rd * tm);
                if (dm < 0.0022)
                {
                    float fracHit = saturate((prevDm - 0.0022) / max(1e-5, prevDm - dm));
                    tMono = lerp(prevTm, tm, fracHit);
                    hitMono = true;
                    break;
                }
                prevTm = tm;
                prevDm = dm;
                tm += max(0.010, dm * 0.86);
                if (tm > tEnd) break;
            }
        }
    }

    // 2. Smooth CSG Arena Architecture (accelerated by conservative 8x8 Ray-Cone TileStartT!) + Planar Water Surface
    float waterLevel = U[10].w;
    float tWaterPlane = (rd.y < -1e-4 && ro.y > waterLevel) ? ((waterLevel - ro.y) / rd.y) : 1e6;
    if (tWaterPlane < U[2].y) tWaterPlane = 1e6;

    float tSolid = 1e6;
    bool hitSolid = false;
    int hitMatId = 1;
    {
        float maxDist = min(88.0, hitMono ? tMono : 88.0);
        float ta = max(U[2].y, 0.06);
        if (Post1.w > 0.5)
        {
            float coneStart = TileStartT.Load(int3(int(dtid.x >> 3u), int(dtid.y >> 3u), 0));
            ta = max(ta, min(coneStart, maxDist));
        }
        float prevTa = ta;
        float prevDa = 1.0;
        [loop]
        for (int s = 0; s < 84; s++)
        {
            float3 posA = ro + rd * ta;
            if (rd.y > 0.0 && posA.y > 5.55) break;
            int mId;
            float dScene = EvalArenaArchitectureSdf(posA, mId);
            if (dScene < 0.0028)
            {
                float fracA = (s > 0) ? saturate((prevDa - 0.0014) / max(1e-5, prevDa - dScene)) : 1.0;
                tSolid = lerp(prevTa, ta, fracA);
                hitMatId = mId;
                if (mId != 1) EvalArenaArchitectureSdf(ro + rd * tSolid, hitMatId);
                hitSolid = true;
                break;
            }
            prevTa = ta;
            prevDa = dScene;
            ta += max(0.012, dScene * 0.92);
            if (ta > maxDist) break;
        }
    }

    bool hitWater = false;
    if (tWaterPlane < tSolid && tWaterPlane < tMono && tWaterPlane < 85.0)
    {
        float3 wpWater = ro + rd * tWaterPlane;
        float rW = length(wpWater.xz);
        int wMat;
        float solidAtWater = EvalArenaArchitectureSdf(wpWater, wMat);
        if (rW > 4.45 && rW < 11.45 && solidAtWater > 0.008)
        {
            hitWater = true;
        }
    }

    float3 sunDir = normalize(U[17].xyz);
    float3 skyCol = (!hitMono && !hitWater && !hitSolid) ? EvalSovngardeAuroraSky(rd) : EvalSkyReflectFast(rd);
    float3 finalCol = skyCol;
    float hitT = max(1.0, abs(U[2].z));

    if (hitMono && tMono <= tSolid && !hitWater)
    {
        hitT = tMono;
        float3 hitPos = ro + rd * tMono;
        float3 nrm = CalcMonolithNormal(hitPos);
        float fres = pow(max(1e-4, 1.0 - saturate(dot(-rd, nrm))), 3.6);
        float ior = clamp(U[15].w, 1.25, 2.20);
        float3 reflDir = reflect(rd, nrm);
        float3 refrR = refract(rd, nrm, 1.0 / (ior - 0.05));
        float3 refrG = refract(rd, nrm, 1.0 / ior);
        float3 refrB = refract(rd, nrm, 1.0 / (ior + 0.05));
        if (dot(refrR, refrR) < 0.01) refrR = reflDir;
        if (dot(refrG, refrG) < 0.01) refrG = reflDir;
        if (dot(refrB, refrB) < 0.01) refrB = reflDir;

        float3 envRefl = EvalSkyReflectFast(reflDir);
        float3 envRefr = float3(
            EvalSkyReflectFast(refrR).r,
            EvalSkyReflectFast(refrG).g,
            EvalSkyReflectFast(refrB).b);

        int monoSym = (int)round(U[9].x);
        float3 qRel = hitPos - float3(0.0, 2.78 + 0.14 * sin(U[0].z * 1.35), 0.0);
        float innerCore = exp(-length(hitPos - monoCenter) * 1.45) * (0.65 + 1.05 * U[3].w);
        float3 crystalCore = float3(0.0, 0.0, 0.0);

        if (monoSym == 4)
        {
            float3 bS2;
            om_hopf_villarceau_sdf(qRel, U[6], U[0].z * (0.45 + 0.35 * U[15].y), 1.02 * max(0.45, U[15].x), bS2);
            float hHue = frac(atan2(bS2.y, bS2.x + 1e-6) / OM_TAU + 0.5 + U[0].z * 0.05);
            crystalCore = om_hsv2rgb(float3(hHue, 0.78, 1.45 + 1.15 * U[3].x)) * (0.75 + 0.45 * abs(bS2.z));
        }
        else if (monoSym == 5)
        {
            float rad5 = 1.34 * max(0.45, U[15].x);
            float shellBand = smoothstep(0.72 * rad5, 1.05 * rad5, length(qRel));
            float crest = saturate(1.0 - abs(abs(nrm.x) - abs(nrm.y)) * 3.5);
            crystalCore = lerp(float3(0.18, 0.88, 1.35), float3(1.55, 1.08, 0.32), saturate(shellBand * 0.7 + crest * 0.3)) * (0.85 + 0.95 * U[3].y)
                        + float3(0.85, 0.42, 1.35) * innerCore * 0.55;
        }
        else if (monoSym == 6)
        {
            float3 uBall = qRel / max(0.45, 1.56 * max(0.45, U[15].x));
            float hypMetric = 2.0 / max(0.12, 1.0 - dot(uBall, uBall));
            float depthHue = frac(0.50 + hypMetric * 0.065 + U[0].z * 0.04);
            crystalCore = om_hsv2rgb(float3(depthHue, 0.76, min(2.4, 0.95 + 0.14 * hypMetric))) * (0.80 + 0.75 * U[3].x);
        }
        else if (monoSym == 7)
        {
            float f1L, dod; uint hL;
            float e24 = om_leech24_golay_voronoi(qRel * 1.95, U[0].z * 0.30, f1L, dod, hL);
            float vGlow24 = exp(-e24 * 15.0) * (0.95 + 1.25 * U[3].x + 0.85 * impStr);
            crystalCore = lerp(float3(0.08, 1.25, 0.98), float3(1.45, 0.98, 0.28), dod) * vGlow24
                        + float3(0.65, 0.32, 1.25) * innerCore * 0.55;
        }
        else
        {
            uint hVein = 0u; float spVein = 0.0, f1Vein = 0.0;
            float veinEdge = om_e8_voronoi_edge(float4(hitPos * U[16].z, U[0].z * 0.35), float4(nrm, U[3].x), f1Vein, spVein, hVein);
            float veinGlow = exp(-veinEdge * 14.0) * (0.80 + 1.35 * U[3].x + 0.95 * impStr);
            crystalCore = lerp(float3(0.10, 1.05, 1.35), float3(1.05, 0.36, 1.35), spVein) * veinGlow
                        + float3(1.30, 0.95, 0.38) * innerCore * 0.65;
        }

        float rim = pow(max(1e-4, 1.0 - saturate(dot(-rd, nrm))), 2.4);
        float spec = pow(max(1e-4, saturate(dot(reflect(-sunDir, nrm), -rd))), 96.0);
        finalCol = lerp(envRefr * float3(0.52, 0.88, 1.16) + crystalCore, envRefl, saturate(0.20 + 0.76 * fres))
                 + float3(0.28, 1.02, 1.24) * rim * 0.36
                 + float3(1.85, 1.72, 1.48) * spec * 1.65;
    }
    else if (hitWater || hitSolid)
    {
        hitT = hitWater ? tWaterPlane : tSolid;
        float3 hitPos = ro + rd * hitT;
        float coneW = max(0.0014, hitT * fovTan * 1.55 / float(h));

        float2 stoneSampleXZ = hitPos.xz;
        float3 wNrm = float3(0.0, 1.0, 0.0);
        float4 mf = float4(0.0, 0.0, 0.0, 0.0);
        float waterDepth = 0.0;

        if (hitWater)
        {
            mf = SampleMoatSmooth(hitPos.xz);
            float4 mfX = SampleMoatSmooth(hitPos.xz + float2(0.11, 0.0));
            float4 mfZ = SampleMoatSmooth(hitPos.xz + float2(0.0, 0.11));

            float tW = U[0].z * 1.85;
            float audioRip = 0.022 * (0.35 + U[3].x + U[3].y) * sin(length(hitPos.xz) * 7.5 - tW * 3.2);
            float capX = 0.032 * cos(hitPos.x * 5.3 + tW) * sin(hitPos.z * 4.7 - tW * 0.8)
                       + 0.016 * cos(hitPos.x * 11.7 - tW * 1.6) + audioRip * (hitPos.x / max(0.5, length(hitPos.xz)));
            float capZ = 0.032 * sin(hitPos.z * 5.1 + tW * 1.1) * cos(hitPos.x * 4.9 + tW * 0.7)
                       + 0.016 * sin(hitPos.z * 11.3 + tW * 1.5) + audioRip * (hitPos.z / max(0.5, length(hitPos.xz)));
            wNrm = normalize(float3((mf.z - mfX.z) * 2.35 + capX, 1.0, (mf.z - mfZ.z) * 2.35 + capZ));

            waterDepth = max(0.02, hitSolid ? ((tSolid - tWaterPlane) * max(0.18, -rd.y)) : (waterLevel - (-0.62)));
            float3 refrDir = refract(rd, wNrm, 0.75);
            stoneSampleXZ = hitPos.xz + refrDir.xz * min(1.8, waterDepth * 1.15);
        }

        float3 stoneCol; float seamGlow; float mortarJoint; float2 bevelGrad;
        EvalCourtyardStone(stoneSampleXZ, coneW * (hitWater ? 1.18 : 1.0), stoneCol, seamGlow, mortarJoint, bevelGrad);

        if (hitWater)
        {
            uint cHash = 0u; float cSp = 0.0, cF1 = 0.0;
            float causEdge = om_e8_voronoi_edge(
                float4(stoneSampleXZ * 1.60 + wNrm.xz * 1.35, U[0].z * 0.58, 0.0),
                float4(stoneSampleXZ.yx * 1.15, -U[0].z * 0.44, U[3].x), cF1, cSp, cHash);
            float caustic = exp(-causEdge * 16.0) * (0.95 + 1.10 * U[3].y);
            float coralVein = smoothstep(0.12, 0.48, mf.y) * exp(-waterDepth * 0.35);

            float3 seabed = stoneCol * 1.15
                          + float3(0.08, 0.85, 1.08) * seamGlow * 0.38
                          + float3(0.24, 1.08, 1.24) * caustic * 0.65
                          + float3(0.08, 1.05, 0.78) * coralVein * (0.36 + 0.38 * U[3].x);

            float3 absorb = exp(-waterDepth * float3(1.18, 0.35, 0.16));
            float3 scatter = float3(0.014, 0.082, 0.132) * (1.0 - absorb.x);
            float3 refractedCol = seabed * absorb + scatter;

            float shoreDist = EvalFastOccluderSdf(hitPos);
            float shoreFoam = smoothstep(0.12, 0.008, shoreDist) * (0.55 + 0.45 * sin(hitPos.x * 9.5 + hitPos.z * 9.5 - U[0].z * 3.8));

            float3 reflVec = reflect(rd, wNrm);
            float3 reflCol = EvalSkyReflectFast(reflVec);
            float monoAlign = saturate(dot(normalize(monoCenter - hitPos), reflVec));
            reflCol += float3(0.22, 0.82, 1.18) * pow(max(1e-4, monoAlign), 28.0) * 0.68;

            float wFres = 0.03 + 0.92 * pow(max(1e-4, 1.0 - saturate(dot(-rd, wNrm))), 4.5);
            float wSpec = pow(max(1e-4, saturate(dot(reflect(-sunDir, wNrm), -rd))), 160.0);
            finalCol = lerp(refractedCol, reflCol, wFres)
                     + float3(0.75, 1.05, 1.12) * shoreFoam * 0.40
                     + float3(1.75, 1.65, 1.45) * wSpec * 1.45;
        }
        else
        {
            float3 baseNrm = CalcArenaNormal(hitPos);

            if (hitMatId == 3)
            {
                // Counter-Precessing Dual-Gimbal HSV Spectral Gyroscope Orb (linear HDR core + obsidian-iridium gimbals)
                float3 opLoc; float orbRLoc, orbPhase, dCoreLoc, dGimbalLoc;
                EvalGyroscopeOrbLocal(hitPos, opLoc, orbRLoc, orbPhase, dCoreLoc, dGimbalLoc);

                float oFres = pow(max(1e-4, 1.0 - saturate(dot(-rd, baseNrm))), 3.0);
                float oSpec = pow(max(1e-4, saturate(dot(reflect(-sunDir, baseNrm), -rd))), 96.0);
                float hueBase = frac(
                    orbPhase / OM_TAU +
                    U[12].w / 240.0 +
                    atan2(baseNrm.z, baseNrm.x + 1e-6) / OM_TAU * 0.28 +
                    U[0].z * 0.09 +
                    U[3].x * 0.18 - U[3].z * 0.12);

                if (dGimbalLoc < dCoreLoc + 0.008)
                {
                    // Brushed obsidian-iridium containment gimbal ring + spectral Vernier azimuth ticks
                    float azTick = exp(-abs(frac(atan2(opLoc.z, opLoc.x + 1e-6) * 3.8197186) - 0.5) * 18.0);
                    float3 tickRgb = om_hsv2rgb(float3(hueBase, 0.80, 1.35 + 1.10 * U[3].y));
                    float3 metalBase = float3(0.032, 0.042, 0.072) + tickRgb * azTick * 0.48;
                    finalCol = metalBase
                             + EvalSkyReflectFast(reflect(rd, baseNrm)) * (0.22 + 0.72 * oFres)
                             + float3(1.65, 1.52, 1.28) * oSpec * 1.15;
                }
                else
                {
                    // Faceted HSV Spectral Harmonic Core (linear HDR radiance -> feeds Dual-Kawase Bloom!)
                    float facetEdge = smoothstep(0.18, 0.02, abs(abs(baseNrm.x) - abs(baseNrm.z)));
                    float hdrVal = 1.25 + 1.55 * (U[3].x * 0.55 + U[3].y * 0.45 + U[3].w * 0.35 + impStr * 0.50);
                    float3 coreRgb = om_hsv2rgb(float3(hueBase, 0.78 - 0.18 * facetEdge, hdrVal));
                    float3 rimRgb  = om_hsv2rgb(float3(frac(hueBase + 0.14), 0.65, 1.85));
                    finalCol = coreRgb * (0.72 + 0.42 * facetEdge)
                             + rimRgb * oFres * 0.85
                             + EvalSkyReflectFast(reflect(rd, baseNrm)) * oFres * 0.45
                             + float3(1.80, 1.72, 1.55) * oSpec * 1.20;
                }
            }
            else
            {
                float3 gNrm = normalize(baseNrm + float3(bevelGrad.x, 0.0, bevelGrad.y) * saturate(baseNrm.y));
                float ao = CalcArenaAO(hitPos, baseNrm);
                float sh = CalcSoftShadow(hitPos + baseNrm * 0.02, sunDir);

                float ndl = max(0.0, dot(gNrm, sunDir));
                float distCenter = length(hitPos.xz);
                float altarBounce = exp(-distCenter * 0.22) * (0.35 + 0.45 * U[3].x) * saturate(1.0 - abs(hitPos.y - 0.55) * 0.4);
                float3 glowTint = lerp(float3(0.08, 0.88, 1.12), float3(1.18, 0.80, 0.26), saturate(impStr));

                float3 halfV = normalize(sunDir - rd);
                float stoneSpec = pow(max(1e-4, saturate(dot(gNrm, halfV))), 56.0) * (1.0 - mortarJoint) * 0.48;

                float pillarRune = 0.0;
                if (hitMatId == 2)
                {
                    stoneCol = float3(0.042, 0.052, 0.080);
                    seamGlow *= 0.25;
                    pillarRune = exp(-abs(frac(hitPos.y * 0.85 - U[0].z * 0.35) - 0.5) * 12.0) * (0.70 + 0.95 * U[3].y);
                }

                float3 skyAmb = float3(0.09, 0.14, 0.22) * (0.55 + 0.45 * gNrm.y);
                finalCol = stoneCol * (ndl * sh * float3(1.05, 1.00, 0.94) + skyAmb) * ao
                         + float3(0.95, 1.02, 1.10) * stoneSpec * sh
                         + glowTint * seamGlow * (0.34 + 0.62 * U[3].x) * ao
                         + float3(0.14, 0.76, 1.08) * altarBounce * 0.44
                         + float3(0.22, 1.12, 1.28) * pillarRune * 0.85;
            }
        }

        if (impStr > 0.01)
        {
            float dImp = length(hitPos - impOrig);
            float ringWave = exp(-abs(dImp - impRad) * 1.65) * impStr;
            float cymatic = 0.5 + 0.5 * cos((dImp - impRad) * U[14].z);
            finalCol += float3(0.34, 1.15, 1.38) * ringWave * (0.55 + 0.45 * cymatic) * 1.35;
        }

        if (U[19].w > 0.001)
        {
            float3 customCol = finalCol;
            EvalCustomOmsh(hitPos, rd, customCol);
            finalCol = lerp(finalCol, customCol, saturate(U[19].w));
        }

        float fogAmt = 1.0 - exp(-hitT * max(0.002, U[17].w));
        finalCol = lerp(finalCol, skyCol, saturate(fogAmt));
    }

    // Subtle volumetric HDR halo around central 6D crystal monolith & active shockwave shell
    {
        float tNear = clamp(dot(monoCenter - ro, rd), 0.0, hitT);
        float dAxis = length((ro + rd * tNear) - monoCenter);
        float halo = exp(-max(0.0, dAxis - 1.25) * 1.85) * (0.065 + 0.085 * U[3].x);
        finalCol += float3(0.16, 0.72, 1.08) * halo;
    }
    if (impStr > 0.01)
    {
        float tClosest = clamp(dot(impOrig - ro, rd), 0.0, hitT);
        float3 closestPt = ro + rd * tClosest;
        float shellErr = abs(length(closestPt - impOrig) - impRad);
        finalCol += float3(0.24, 0.86, 1.18) * exp(-shellErr * 2.4) * impStr * 0.34;
    }

    // Output raw unbounded linear FP16 HDR radiance + hitT (ACES + Bloom + sRGB + 5-Tap CAS run in Pass 4-6!)
    OutTex[dtid.xy] = float4(clamp(finalCol, 0.0, 32.0), hitT);
}
");
            return sb.ToString();
        }

        public static string GetUpstreamCc0ReShadeFxh()
        {
            return @"/*
 * SPDX-License-Identifier: CC0-1.0
 * Standard ReShade.fxh helper header (crosire/reshade-shaders)
 */

#pragma once

#if !defined(__RESHADE__) || __RESHADE__ < 30000
    #error ""ReShade 3.0+ is required to use this header file""
#endif

#ifndef RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN
    #define RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN 0
#endif
#ifndef RESHADE_DEPTH_INPUT_IS_REVERSED
    #define RESHADE_DEPTH_INPUT_IS_REVERSED 0
#endif
#ifndef RESHADE_DEPTH_INPUT_IS_MIRRORED
    #define RESHADE_DEPTH_INPUT_IS_MIRRORED 0
#endif
#ifndef RESHADE_DEPTH_INPUT_IS_LOGARITHMIC
    #define RESHADE_DEPTH_INPUT_IS_LOGARITHMIC 0
#endif

#ifndef RESHADE_DEPTH_MULTIPLIER
    #define RESHADE_DEPTH_MULTIPLIER 1
#endif
#ifndef RESHADE_DEPTH_LINEARIZATION_FAR_PLANE
    #define RESHADE_DEPTH_LINEARIZATION_FAR_PLANE 1000.0
#endif

#ifndef RESHADE_DEPTH_INPUT_Y_SCALE
    #define RESHADE_DEPTH_INPUT_Y_SCALE 1
#endif
#ifndef RESHADE_DEPTH_INPUT_X_SCALE
    #define RESHADE_DEPTH_INPUT_X_SCALE 1
#endif
#ifndef RESHADE_DEPTH_INPUT_Y_OFFSET
    #define RESHADE_DEPTH_INPUT_Y_OFFSET 0
#endif
#ifndef RESHADE_DEPTH_INPUT_Y_PIXEL_OFFSET
    #define RESHADE_DEPTH_INPUT_Y_PIXEL_OFFSET 0
#endif
#ifndef RESHADE_DEPTH_INPUT_X_OFFSET
    #define RESHADE_DEPTH_INPUT_X_OFFSET 0
#endif
#ifndef RESHADE_DEPTH_INPUT_X_PIXEL_OFFSET
    #define RESHADE_DEPTH_INPUT_X_PIXEL_OFFSET 0
#endif

#define BUFFER_PIXEL_SIZE float2(BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT)
#define BUFFER_SCREEN_SIZE float2(BUFFER_WIDTH, BUFFER_HEIGHT)
#define BUFFER_ASPECT_RATIO (BUFFER_WIDTH * BUFFER_RCP_HEIGHT)

namespace ReShade
{
#if defined(__RESHADE_FXC__)
    float GetAspectRatio() { return BUFFER_WIDTH * BUFFER_RCP_HEIGHT; }
    float2 GetPixelSize() { return float2(BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT); }
    float2 GetScreenSize() { return float2(BUFFER_WIDTH, BUFFER_HEIGHT); }
    #define AspectRatio GetAspectRatio()
    #define PixelSize GetPixelSize()
    #define ScreenSize GetScreenSize()
#else
    static const float AspectRatio = BUFFER_WIDTH * BUFFER_RCP_HEIGHT;
    static const float2 PixelSize = float2(BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT);
    static const float2 ScreenSize = float2(BUFFER_WIDTH, BUFFER_HEIGHT);
#endif

    texture BackBufferTex : COLOR;
    texture DepthBufferTex : DEPTH;

    sampler BackBuffer { Texture = BackBufferTex; };
    sampler DepthBuffer { Texture = DepthBufferTex; };

    float GetLinearizedDepth(float2 texcoord)
    {
#if RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN
        texcoord.y = 1.0 - texcoord.y;
#endif
#if RESHADE_DEPTH_INPUT_IS_MIRRORED
        texcoord.x = 1.0 - texcoord.x;
#endif
        texcoord.x /= RESHADE_DEPTH_INPUT_X_SCALE;
        texcoord.y /= RESHADE_DEPTH_INPUT_Y_SCALE;
#if RESHADE_DEPTH_INPUT_X_PIXEL_OFFSET
        texcoord.x -= RESHADE_DEPTH_INPUT_X_PIXEL_OFFSET * BUFFER_RCP_WIDTH;
#else
        texcoord.x -= RESHADE_DEPTH_INPUT_X_OFFSET / 2.000000001;
#endif
#if RESHADE_DEPTH_INPUT_Y_PIXEL_OFFSET
        texcoord.y += RESHADE_DEPTH_INPUT_Y_PIXEL_OFFSET * BUFFER_RCP_HEIGHT;
#else
        texcoord.y += RESHADE_DEPTH_INPUT_Y_OFFSET / 2.000000001;
#endif
        float depth = tex2Dlod(DepthBuffer, float4(texcoord, 0, 0)).x * RESHADE_DEPTH_MULTIPLIER;

#if RESHADE_DEPTH_INPUT_IS_LOGARITHMIC
        const float C = 0.01;
        depth = (exp(depth * log(C + 1.0)) - 1.0) / C;
#endif
#if RESHADE_DEPTH_INPUT_IS_REVERSED
        depth = 1.0 - depth;
#endif
        const float N = 1.0;
        depth /= RESHADE_DEPTH_LINEARIZATION_FAR_PLANE - depth * (RESHADE_DEPTH_LINEARIZATION_FAR_PLANE - N);

        return depth;
    }
}

void PostProcessVS(in uint id : SV_VertexID, out float4 position : SV_Position, out float2 texcoord : TEXCOORD)
{
    texcoord.x = (id == 2) ? 2.0 : 0.0;
    texcoord.y = (id == 1) ? 2.0 : 0.0;
    position = float4(texcoord * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}";
        }

        public static string GetCanonicalSkyrimAeWorldShaderFx()
        {
            return @"// =============================================================================
// OmegaTriEngine_SkyrimAE.fx (Suite v3.9.0 / Station 15 OmegaEngine v1.9.0)
// SPDX-License-Identifier: BSD-3-Clause OR MIT
// Phase 2: 512B OTRI v1.9 Bus (U0..U27) + 28px Top-Screen Live Diagnostics Strip
// + 4 Aesthetic Personas (Classic Sovngarde / Obsidian Dark Fantasy / Liminal
//   Dreamcore / Stark Nihilist Monolith) + Multi-Pass Half-Res RGBA16F HDR
//   Dual-Kawase Bloom & Depth-Bilateral Godrays + Water-Plane & Spell Coupling
// Zero ReShadeFX reserved words, zero bitwise int ops, zero vector ternaries.
// =============================================================================

#include ""ReShade.fxh""

texture OmegaHdrHalfTex { Width = BUFFER_WIDTH / 2; Height = BUFFER_HEIGHT / 2; Format = RGBA16F; };
sampler OmegaHdrHalfSamp { Texture = OmegaHdrHalfTex; };

uniform float4 U0  < source = ""omega_tri_engine""; index = 0;  >;
uniform float4 U1  < source = ""omega_tri_engine""; index = 1;  >;
uniform float4 U2  < source = ""omega_tri_engine""; index = 2;  >;
uniform float4 U3  < source = ""omega_tri_engine""; index = 3;  >;
uniform float4 U4  < source = ""omega_tri_engine""; index = 4;  >;
uniform float4 U5  < source = ""omega_tri_engine""; index = 5;  >;
uniform float4 U6  < source = ""omega_tri_engine""; index = 6;  >;
uniform float4 U7  < source = ""omega_tri_engine""; index = 7;  >;
uniform float4 U8  < source = ""omega_tri_engine""; index = 8;  >;
uniform float4 U9  < source = ""omega_tri_engine""; index = 9;  >;
uniform float4 U10 < source = ""omega_tri_engine""; index = 10; >;
uniform float4 U11 < source = ""omega_tri_engine""; index = 11; >;
uniform float4 U12 < source = ""omega_tri_engine""; index = 12; >;
uniform float4 U13 < source = ""omega_tri_engine""; index = 13; >;
uniform float4 U14 < source = ""omega_tri_engine""; index = 14; >;
uniform float4 U15 < source = ""omega_tri_engine""; index = 15; >;
uniform float4 U16 < source = ""omega_tri_engine""; index = 16; >;
uniform float4 U17 < source = ""omega_tri_engine""; index = 17; >;
uniform float4 U18 < source = ""omega_tri_engine""; index = 18; >;
uniform float4 U19 < source = ""omega_tri_engine""; index = 19; >;
uniform float4 U20 < source = ""omega_tri_engine""; index = 20; >;
uniform float4 U21 < source = ""omega_tri_engine""; index = 21; >;
uniform float4 U22 < source = ""omega_tri_engine""; index = 22; >;
uniform float4 U23 < source = ""omega_tri_engine""; index = 23; >;
uniform float4 U24 < source = ""omega_tri_engine""; index = 24; >;
uniform float4 U25 < source = ""omega_tri_engine""; index = 25; >;
uniform float4 U26 < source = ""omega_tri_engine""; index = 26; >;
uniform float4 U27 < source = ""omega_tri_engine""; index = 27; >;

uniform float Timer < source = ""timer""; >;
uniform float OmegaRsFrameMs < source = ""frametime""; >;

uniform bool UiShowTopDiagBar <
    ui_label = ""Show 28px Top-Screen Omega Diagnostics HUD Strip (Alt+F2)"";
> = true;

uniform bool UiAutoMenuFade <
    ui_label = ""Auto-Fade Shader in Skyrim Menus over 250ms (Alt+F6)"";
> = true;

uniform bool UiFollowLivePersona <
    ui_label = ""Follow Live Top-Bar / Bus Aesthetic Persona (U20.x / Alt+F8)"";
> = true;

uniform int UiAestheticPersona <
    ui_type = ""combo"";
    ui_label = ""Aesthetic Shader Persona (0..3)"";
    ui_items = ""0: Classic Sovngarde\01: Obsidian Dark Fantasy\02: Liminal Dreamcore\03: Stark Nihilist Monolith\0"";
> = 0;

uniform bool UiUseLiveBusSymmetry <
    ui_label = ""Follow Live Station 15 / SKSE64 Symmetry Mode (U9.x / U26.x)"";
> = true;

uniform int UiSymmetryMode <
    ui_type = ""combo"";
    ui_label = ""Fallback Symmetry Mode (0..7)"";
    ui_items = ""0: 5D Penrose P3\01: 8D E8 Coxeter\02: Einstein Hat (1,sqrt3)\03: Chiral Spectre (1,1)\04: 4D Hopf Villarceau\05: 4D 24/120-Cell H4\06: Hyperbolic Poincare {5,3,4}\07: 24D Leech Golay\0"";
> = 0;

uniform float UiAuroraBlend <
    ui_type = ""slider"";
    ui_min = 0.0;
    ui_max = 1.5;
    ui_step = 0.01;
    ui_label = ""Sovngarde Sky Aurora Intensity"";
> = 0.78;

uniform float UiCausticStrength <
    ui_type = ""slider"";
    ui_min = 0.0;
    ui_max = 1.0;
    ui_step = 0.01;
    ui_label = ""World-Space Ground/Water Caustics"";
> = 0.36;

uniform float UiHdrBloomStrength <
    ui_type = ""slider"";
    ui_min = 0.0;
    ui_max = 1.5;
    ui_step = 0.01;
    ui_label = ""Half-Res RGBA16F Dual-Kawase HDR Bloom"";
> = 0.34;

uniform float UiGodrayStrength <
    ui_type = ""slider"";
    ui_min = 0.0;
    ui_max = 1.2;
    ui_step = 0.01;
    ui_label = ""Depth-Bilateral Crepuscular Godrays"";
> = 0.28;

uniform float UiSpectralHarmony <
    ui_type = ""slider"";
    ui_min = 0.0;
    ui_max = 1.0;
    ui_step = 0.01;
    ui_label = ""E8 Audio & Spell Chromatic Coupling"";
> = 0.28;

uniform float UiTileScale <
    ui_type = ""slider"";
    ui_min = 0.25;
    ui_max = 4.0;
    ui_step = 0.01;
    ui_label = ""Quasicrystal Spatial Scale"";
> = 0.85;

uniform float UiNormalPlanarWeight <
    ui_type = ""slider"";
    ui_min = 0.0;
    ui_max = 1.0;
    ui_step = 0.01;
    ui_label = ""Horizontal Floor/Water Normal Mask"";
> = 0.72;

uniform bool UiWorldSpaceLock <
    ui_label = ""World-Space Camera & Water Plane Lock (SKSE64 U1/U18/U19/U21)"";
> = true;

uniform bool UiSheafQuestGuard <
    ui_label = ""Cellular Sheaf H1=0 Spectre Dungeon Guard (U16.w)"";
> = true;

float3 OmegaHsv2Rgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - float3(3.0, 3.0, 3.0));
    return c.z * lerp(float3(1.0, 1.0, 1.0), saturate(p - float3(1.0, 1.0, 1.0)), c.y);
}

float OmegaTriSeam(float2 p)
{
    float a = p.x;
    float b = -0.5 * p.x + 0.8660254 * p.y;
    float c = -0.5 * p.x - 0.8660254 * p.y;
    return min(abs(a - floor(a + 0.5)), min(abs(b - floor(b + 0.5)), abs(c - floor(c + 0.5))));
}

float2 OmegaD8NearestAndSecond(float4 xA, float4 xB, float sh)
{
    float4 sh4 = float4(sh, sh, sh, sh);
    float4 kHalf4 = float4(0.5, 0.5, 0.5, 0.5);
    float4 uA = xA - sh4;
    float4 uB = xB - sh4;
    float4 flA = floor(uA + kHalf4);
    float4 flB = floor(uB + kHalf4);
    float4 fA = uA - flA;
    float4 fB = uB - flB;
    float4 aA = abs(fA);
    float4 aB = abs(fB);

    float sum8 = (flA.x + flA.y + flA.z + flA.w) + (flB.x + flB.y + flB.z + flB.w);
    float halfTurn = abs(sum8 * 0.5 - floor(sum8 * 0.5 + 0.1));

    float d2Base = dot(fA, fA) + dot(fB, fB);
    float maxComp = max(max(max(aA.x, aA.y), max(aA.z, aA.w)), max(max(aB.x, aB.y), max(aB.z, aB.w)));
    float minComp = min(min(min(aA.x, aA.y), min(aA.z, aA.w)), min(min(aB.x, aB.y), min(aB.z, aB.w)));
    float minDelta = 1.0 - 2.0 * maxComp;
    float maxDelta = -(1.0 - 2.0 * minComp);
    float d1 = d2Base;
    float d2 = d2Base + max(0.02, minDelta);
    if (halfTurn > 0.25)
    {
        d1 = d2Base + minDelta;
        d2 = d2Base + max(minDelta + 0.04, 1.0 + maxDelta);
    }
    return float2(d1, d2);
}

float OmegaE8FacetSeam(float2 wp, float phasonT)
{
    float4 xA = float4(wp.x * 0.72, wp.y * 0.72, (wp.x + wp.y) * 0.51, phasonT * 0.25);
    float4 xB = float4((wp.x - wp.y) * 0.51, wp.x * 0.37 - wp.y * 0.61, wp.y * 0.37 + wp.x * 0.61, U8.y);
    float2 da = OmegaD8NearestAndSecond(xA, xB, 0.0);
    float2 db = OmegaD8NearestAndSecond(xA, xB, 0.5);
    float f1 = min(da.x, db.x);
    float f2 = min(max(da.x, db.x), min(da.y, db.y));
    return saturate((f2 - f1) * 0.85);
}

float4 OmegaQMul(float4 a, float4 b)
{
    return float4(
        a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
        a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
        a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
        a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
}

float OmegaHopfVillarceauSeam(float2 wp, float tSec)
{
    float3 q = float3(wp.x * 0.24, 0.20 * sin(tSec * 0.35), wp.y * 0.24);
    float r2 = dot(q, q);
    float invD = 1.0 / (1.0 + r2);
    float4 z4 = float4(2.0 * q.x * invD, 2.0 * q.y * invD, 2.0 * q.z * invD, (r2 - 1.0) * invD);
    float qLen = length(U6);
    float4 qRot = float4(0.0, 0.0, sin(tSec * 0.2), cos(tSec * 0.2));
    if (qLen > 0.25)
    {
        qRot = U6 / qLen;
    }
    float4 zr = OmegaQMul(qRot, z4);
    float3 bS2 = normalize(float3(
        2.0 * (zr.x * zr.z + zr.y * zr.w),
        2.0 * (zr.y * zr.z - zr.x * zr.w),
        zr.x * zr.x + zr.y * zr.y - zr.z * zr.z - zr.w * zr.w) + float3(0.0, 0.0, 0.00001));
    float az = atan2(bS2.y, bS2.x + 0.000001);
    float sec6 = floor(az / 1.04719755 + 0.5) * 1.04719755;
    float dotEq = dot(bS2, float3(cos(sec6), sin(sec6), 0.0));
    return sqrt(max(0.0, 0.5 * (1.0 - max(dotEq, abs(bS2.z) * 0.98))));
}

float4 OmegaH4FoldStep(float4 f, float3 n1, float3 n3)
{
    f.x = abs(f.x);
    float d1 = dot(f.xyz, n1);
    if (d1 < 0.0)
    {
        float3 r1 = f.xyz - 2.0 * d1 * n1;
        f = float4(r1.x, r1.y, r1.z, f.w);
    }
    if (f.z > 0.0)
    {
        f.z = -f.z;
    }
    float d3 = dot(f.yzw, n3);
    if (d3 < 0.0)
    {
        float3 r3 = f.yzw - 2.0 * d3 * n3;
        f = float4(f.x, r3.x, r3.y, r3.z);
    }
    return f;
}

float OmegaPolychoraH4Seam(float2 wp, float tSec)
{
    float4 f = normalize(float4(wp.x * 0.30, wp.y * 0.30, 0.55 + 0.16 * sin(tSec * 0.3), 0.55));
    float3 n1 = float3(-0.8090170, 0.3090170, 0.5000000);
    float3 n3 = float3(-0.8090170, 0.5000000, 0.3090170);
    f = OmegaH4FoldStep(f, n1, n3);
    f = OmegaH4FoldStep(f, n1, n3);
    f = OmegaH4FoldStep(f, n1, n3);
    f = OmegaH4FoldStep(f, n1, n3);
    f = OmegaH4FoldStep(f, n1, n3);
    f = OmegaH4FoldStep(f, n1, n3);
    f = OmegaH4FoldStep(f, n1, n3);
    f = OmegaH4FoldStep(f, n1, n3);
    return min(min(f.x, abs(f.z)), abs(dot(f.xyz, n1))) * 1.5;
}

float4 OmegaPoincareFoldStep(float4 us, float3 h3M1, float3 c534)
{
    float3 u = us.xyz;
    float scaleJ = us.w;
    u.x = abs(u.x);
    if (u.z > 0.0)
    {
        u.z = -u.z;
    }
    float d1 = dot(u, h3M1);
    if (d1 < 0.0)
    {
        u = u - 2.0 * d1 * h3M1;
    }
    float3 diff = u - c534;
    float d2 = max(0.00001, dot(diff, diff));
    if (d2 < 1.2360680)
    {
        float k = 1.2360680 / d2;
        u = c534 + diff * k;
        scaleJ = scaleJ * k;
    }
    return float4(u.x, u.y, u.z, scaleJ);
}

float OmegaPoincare534Seam(float2 wp, float tSec)
{
    float2 dUv = wp * 0.085;
    float r = length(dUv);
    if (r > 0.92)
    {
        dUv = dUv * (0.92 / r);
    }
    float4 us = float4(dUv.x + 0.14 * sin(tSec * 0.25), 0.04, dUv.y + 0.14 * cos(tSec * 0.21), 1.0);
    float3 h3M1 = float3(-0.8090170, 0.3090170, 0.5000000);
    float3 c534 = float3(0.0, 1.27201965, -0.78615138);
    us = OmegaPoincareFoldStep(us, h3M1, c534);
    us = OmegaPoincareFoldStep(us, h3M1, c534);
    us = OmegaPoincareFoldStep(us, h3M1, c534);
    us = OmegaPoincareFoldStep(us, h3M1, c534);
    us = OmegaPoincareFoldStep(us, h3M1, c534);
    us = OmegaPoincareFoldStep(us, h3M1, c534);
    return abs((length(us.xyz - c534) - 1.1117859) / us.w) * 1.8;
}

float OmegaLeech24GolaySeam(float2 wp, float tSec)
{
    float s0 = OmegaE8FacetSeam(wp, tSec);
    float s1 = OmegaE8FacetSeam(float2(wp.x * 0.618034 - wp.y * 0.786151, wp.x * 0.786151 + wp.y * 0.618034), -tSec * 0.77);
    float s2 = OmegaE8FacetSeam(float2((wp.x + wp.y) * 0.707107, (wp.x - wp.y) * 0.707107) * 1.12, tSec * 0.53);
    return min(s0, min(s1, s2));
}

float3 OmegaEstimateViewNormal(float2 uv, float centerDepth)
{
    float2 px = float2(BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT);
    float2 kHalf2 = float2(0.5, 0.5);
    float dR = ReShade::GetLinearizedDepth(uv + float2(px.x, 0.0));
    float dU = ReShade::GetLinearizedDepth(uv - float2(0.0, px.y));
    float3 pC = float3((uv - kHalf2) * (centerDepth * 2.0), centerDepth);
    float3 pR = float3((uv + float2(px.x, 0.0) - kHalf2) * (dR * 2.0), dR);
    float3 pU = float3((uv - float2(0.0, px.y) - kHalf2) * (dU * 2.0), dU);
    return normalize(cross(pR - pC, pU - pC) + float3(0.0, 0.0, 0.00001));
}

float3 OmegaReconstructRayDir(float2 centered)
{
    float3 camFwd = float3(0.0, 0.0, 1.0);
    if (dot(U18.xyz, U18.xyz) > 0.25)
    {
        camFwd = normalize(U18.xyz);
    }
    float3 camRight = float3(1.0, 0.0, 0.0);
    if (dot(U19.xyz, U19.xyz) > 0.25)
    {
        camRight = normalize(U19.xyz);
    }
    float3 camUp = normalize(cross(camFwd, camRight) + float3(0.0, 0.00001, 0.0));
    float fovTan = 0.6249;
    if (U0.w > 0.15)
    {
        fovTan = U0.w;
    }
    return normalize(camFwd + camRight * (centered.x * fovTan * 2.0) - camUp * (centered.y * fovTan * 2.0));
}

float OmegaPenroseAxisSeam(float2 wp, float ang, float shift)
{
    float pr = dot(wp, float2(cos(ang), sin(ang))) + shift;
    return abs(pr - floor(pr + 0.5));
}

float3 OmegaSpellSchoolPalette(int schoolId)
{
    float3 c = float3(0.24, 0.86, 1.0);
    if (schoolId == 1)      { c = float3(1.00, 0.36, 0.08); } // Destruction Fire
    else if (schoolId == 2) { c = float3(0.32, 0.88, 1.00); } // Destruction Frost
    else if (schoolId == 3) { c = float3(0.68, 0.38, 1.00); } // Destruction Shock
    else if (schoolId == 4) { c = float3(1.00, 0.86, 0.38); } // Restoration Sun
    else if (schoolId == 5) { c = float3(0.84, 0.20, 0.72); } // Conjuration Void
    return c;
}

float3 OmegaApplyPersonaGrade(
    float3 col,
    int persona,
    float2 centeredUv,
    float depth,
    float dPattern,
    float edgeRelief,
    float vigBase,
    float masterGain)
{
    float rVig = dot(centeredUv, centeredUv);
    float luma = dot(col, float3(0.2126, 0.7152, 0.0722));
    float3 graded = col;

    if (persona == 1)
    {
        // 1: Obsidian Dark Fantasy - crushed obsidian toe, cold iron midtones, ember-gold & crimson silhouette rims
        float3 crushed = pow(max(col, float3(0.0001, 0.0001, 0.0001)), float3(1.16, 1.18, 1.12));
        float3 coldSlate = lerp(float3(luma, luma, luma) * float3(0.84, 0.90, 1.04), crushed, 0.76);
        float emberRim = saturate(edgeRelief * 3.4) * (1.0 - depth * 0.55);
        float ironSeam = exp(-dPattern * 18.0) * (1.0 - depth * 0.75) * 0.10;
        graded = coldSlate + float3(0.92, 0.38, 0.12) * (emberRim * 0.22) + float3(0.82, 0.62, 0.26) * ironSeam;
        graded = graded * saturate(1.0 - rVig * (0.42 + vigBase));
    }
    else if (persona == 2)
    {
        // 2: Liminal Dreamcore - pastel lavender/seafoam halation, lifted twilight blacks, valley mist veil
        float3 softLift = pow(saturate(col * 0.94 + float3(0.028, 0.022, 0.042)), float3(0.92, 0.94, 0.90));
        float mistFactor = smoothstep(0.18, 0.92, depth) * 0.24;
        float3 dreamMist = lerp(float3(0.58, 0.52, 0.86), float3(0.38, 0.84, 0.78), saturate(centeredUv.y + 0.5));
        graded = lerp(softLift, dreamMist, mistFactor);
        graded = graded * saturate(1.0 - rVig * (0.24 + vigBase * 0.65));
    }
    else if (persona == 3)
    {
        // 3: Stark Nihilist Monolith - austere silver-bone chiaroscuro + sharp brutalist depth ink & monolith seams
        float3 mono = float3(luma, luma, luma) * float3(0.95, 0.98, 1.03);
        float3 stark = lerp(col, mono, 0.78);
        stark = saturate((stark - float3(0.5, 0.5, 0.5)) * 1.24 + float3(0.5, 0.5, 0.5));
        float inkEdge = saturate(edgeRelief * 4.5);
        float boneSeam = exp(-dPattern * 22.0) * (1.0 - depth * 0.80) * 0.14;
        graded = stark * (1.0 - inkEdge * 0.42) + float3(0.88, 0.92, 0.96) * boneSeam;
        graded = graded * saturate(1.0 - rVig * (0.46 + vigBase));
    }
    else
    {
        // 0: Classic Sovngarde - clean natural Skyrim palette with subtle ACES shoulder & gentle rim
        graded = col * saturate(1.0 - rVig * (0.16 + vigBase * 0.5));
    }

    return lerp(col, saturate(graded), saturate(masterGain));
}

// Pass 0 (Half-Resolution RGBA16F): 5-tap Dual-Kawase HDR highlight extraction + sky shaft mask in alpha
float4 PS_OmegaHdrHalfPrepass(float4 vpos : SV_Position, float2 texcoord : TEXCOORD) : SV_Target
{
    float2 hpx = float2(BUFFER_RCP_WIDTH * 2.0, BUFFER_RCP_HEIGHT * 2.0);
    float3 c0 = tex2D(ReShade::BackBuffer, texcoord).rgb;
    float3 c1 = tex2D(ReShade::BackBuffer, saturate(texcoord + float2( hpx.x,  hpx.y))).rgb;
    float3 c2 = tex2D(ReShade::BackBuffer, saturate(texcoord + float2(-hpx.x,  hpx.y))).rgb;
    float3 c3 = tex2D(ReShade::BackBuffer, saturate(texcoord + float2( hpx.x, -hpx.y))).rgb;
    float3 c4 = tex2D(ReShade::BackBuffer, saturate(texcoord + float2(-hpx.x, -hpx.y))).rgb;

    float3 avg = c0 * 0.50 + (c1 + c2 + c3 + c4) * 0.125;
    float lum = dot(avg, float3(0.2126, 0.7152, 0.0722));
    float knee = saturate((lum - 0.58) * 2.15);
    float3 hdrBright = avg * (knee * knee * 1.45);

    float d = ReShade::GetLinearizedDepth(texcoord);
    float skyShaft = step(0.985, d) * saturate(lum * 1.25 + 0.12);
    return float4(hdrBright, skyShaft);
}

float OmegaRectFill(float2 p, float x0, float y0, float x1, float y1)
{
    float ix = step(x0, p.x) * step(p.x, x1);
    float iy = step(y0, p.y) * step(p.y, y1);
    return ix * iy;
}

float OmegaDigit7Seg(float2 lp, int dg)
{
    if (lp.x < 0.0 || lp.x > 8.0 || lp.y < 0.0 || lp.y > 12.0)
    {
        return 0.0;
    }
    float sTop = OmegaRectFill(lp, 1.0, 0.0, 7.0, 1.7);
    float sMid = OmegaRectFill(lp, 1.0, 5.2, 7.0, 6.8);
    float sBot = OmegaRectFill(lp, 1.0, 10.3, 7.0, 12.0);
    float sTL  = OmegaRectFill(lp, 0.0, 1.0, 1.8, 5.8);
    float sTR  = OmegaRectFill(lp, 6.2, 1.0, 8.0, 5.8);
    float sBL  = OmegaRectFill(lp, 0.0, 6.2, 1.8, 11.0);
    float sBR  = OmegaRectFill(lp, 6.2, 6.2, 8.0, 11.0);

    float m = 0.0;
    if (dg == 0)      { m = max(max(sTop, sBot), max(max(sTL, sTR), max(sBL, sBR))); }
    else if (dg == 1) { m = max(sTR, sBR); }
    else if (dg == 2) { m = max(max(sTop, sMid), max(sBot, max(sTR, sBL))); }
    else if (dg == 3) { m = max(max(sTop, sMid), max(sBot, max(sTR, sBR))); }
    else if (dg == 4) { m = max(sMid, max(sTL, max(sTR, sBR))); }
    else if (dg == 5) { m = max(max(sTop, sMid), max(sBot, max(sTL, sBR))); }
    else if (dg == 6) { m = max(max(sTop, sMid), max(sBot, max(sTL, max(sBL, sBR)))); }
    else if (dg == 7) { m = max(sTop, max(sTR, sBR)); }
    else if (dg == 8) { m = max(max(sTop, sMid), max(sBot, max(max(sTL, sTR), max(sBL, sBR)))); }
    else              { m = max(max(sTop, sMid), max(sBot, max(sTL, max(sTR, sBR)))); }
    return saturate(m);
}

float3 OmegaRenderTopDiagStrip(
    float2 px,
    float3 sceneCol,
    bool masterOn,
    bool skLive,
    bool rsLive,
    int activeSym,
    int activePersona,
    float aurLevel,
    float cauLevel,
    float menuFadeAlpha,
    float thuumLevel,
    float h1Val,
    float fpsVal)
{
    float3 bg = lerp(sceneCol * 0.20, float3(0.043, 0.055, 0.078), 0.88);
    if (px.y >= 26.5)
    {
        float gx = saturate(px.x * BUFFER_RCP_WIDTH);
        return lerp(float3(0.96, 0.72, 0.32), float3(0.36, 0.88, 0.90), gx);
    }

    float3 hud = bg;
    float3 cGold  = float3(0.96, 0.72, 0.32);
    float3 cCyan  = float3(0.36, 0.88, 0.90);
    float3 cGreen = float3(0.20, 0.84, 0.60);
    float3 cSlate = float3(0.20, 0.26, 0.34);
    float3 cCoral = float3(0.96, 0.38, 0.38);

    float2 dEmb = abs(px - float2(18.0, 13.5));
    float octD = max(max(dEmb.x, dEmb.y), (dEmb.x + dEmb.y) * 0.7071);
    if (octD >= 4.2 && octD <= 6.8)
    {
        hud = cGold;
    }
    float d1 = OmegaDigit7Seg(px - float2(30.0, 7.5), 1);
    float dotV = OmegaRectFill(px, 40.0, 17.5, 42.0, 19.5);
    float d9 = OmegaDigit7Seg(px - float2(44.5, 7.5), 9);
    hud = lerp(hud, cGold, saturate(max(d1, max(dotV, d9))));

    float fxPill = OmegaRectFill(px, 62.0, 6.5, 94.0, 20.5);
    if (fxPill > 0.5)
    {
        float3 fxCol = cCoral;
        if (masterOn) { fxCol = cGreen; }
        hud = lerp(hud, fxCol * 0.32, 0.85);
        float fxDot = step(length(px - float2(71.0, 13.5)), 3.2);
        float fxBar = OmegaRectFill(px, 78.0, 11.5, 89.0, 15.5);
        hud = lerp(hud, fxCol, saturate(max(fxDot, fxBar)));
    }

    float skPill = OmegaRectFill(px, 102.0, 6.5, 133.0, 20.5);
    if (skPill > 0.5)
    {
        float3 skCol = cSlate;
        if (skLive) { skCol = cGreen; }
        hud = lerp(hud, skCol * 0.28, 0.85);
        float skLed = step(length(px - float2(111.0, 13.5)), 3.4);
        float skTick = OmegaRectFill(px, 118.0, 12.0, 128.0, 15.0);
        hud = lerp(hud, skCol, saturate(max(skLed, skTick)));
    }

    float rsPill = OmegaRectFill(px, 139.0, 6.5, 170.0, 20.5);
    if (rsPill > 0.5)
    {
        float3 rsCol = cSlate;
        if (rsLive) { rsCol = cCyan; }
        hud = lerp(hud, rsCol * 0.28, 0.85);
        float rsLed = step(length(px - float2(148.0, 13.5)), 3.4);
        float rsTick = OmegaRectFill(px, 155.0, 12.0, 165.0, 15.0);
        hud = lerp(hud, rsCol, saturate(max(rsLed, rsTick)));
    }

    if (px.x >= 182.0 && px.x < 310.0 && px.y >= 7.5 && px.y <= 19.5)
    {
        float relX = px.x - 182.0;
        int cellIdx = (int)floor(relX / 16.0);
        float cellLocalX = relX - floor(relX / 16.0) * 16.0;
        if (cellLocalX <= 13.0)
        {
            hud = cSlate * 0.65;
            if (cellIdx == activeSym)
            {
                hud = cGold;
            }
        }
    }
    float symDg = OmegaDigit7Seg(px - float2(314.0, 7.5), activeSym);
    hud = lerp(hud, cCyan, symDg);

    if (OmegaRectFill(px, 334.0, 9.0, 404.0, 18.0) > 0.5)
    {
        hud = cSlate * 0.55;
        if ((px.x - 334.0) <= saturate(aurLevel) * 70.0) { hud = float3(0.18, 0.84, 0.75); }
    }
    if (OmegaRectFill(px, 414.0, 9.0, 484.0, 18.0) > 0.5)
    {
        hud = cSlate * 0.55;
        if ((px.x - 414.0) <= saturate(cauLevel) * 70.0) { hud = float3(0.38, 0.65, 0.98); }
    }
    if (OmegaRectFill(px, 494.0, 9.0, 564.0, 18.0) > 0.5)
    {
        hud = cSlate * 0.55;
        if ((px.x - 494.0) <= saturate(menuFadeAlpha) * 70.0) { hud = cGold; }
    }
    if (OmegaRectFill(px, 574.0, 9.0, 644.0, 18.0) > 0.5)
    {
        hud = cSlate * 0.55;
        if ((px.x - 574.0) <= saturate(thuumLevel) * 70.0) { hud = float3(0.75, 0.52, 0.98); }
    }

    if (OmegaRectFill(px, 656.0, 6.5, 686.0, 20.5) > 0.5)
    {
        float3 hCol = cGreen;
        if (h1Val >= 1.5) { hCol = cGold; }
        else if (h1Val < -0.5) { hCol = cCoral; }
        hud = lerp(hud, hCol * 0.30, 0.85);
        float hDot = step(length(px - float2(671.0, 13.5)), 4.0);
        hud = lerp(hud, hCol, hDot);
    }

    // Persona 0..3 4-pip indicator + 7-seg digit (694..754 px)
    if (px.x >= 694.0 && px.x < 742.0 && px.y >= 7.5 && px.y <= 19.5)
    {
        float pRel = px.x - 694.0;
        int pIdx = (int)floor(pRel / 12.0);
        float pLoc = pRel - floor(pRel / 12.0) * 12.0;
        if (pLoc <= 9.5)
        {
            hud = cSlate * 0.65;
            if (pIdx == activePersona)
            {
                float3 perCol = cGold;
                if (activePersona == 1)      { perCol = float3(0.92, 0.22, 0.34); }
                else if (activePersona == 2) { perCol = float3(0.76, 0.54, 0.98); }
                else if (activePersona == 3) { perCol = float3(0.84, 0.88, 0.94); }
                hud = perCol;
            }
        }
    }
    float perDg = OmegaDigit7Seg(px - float2(745.0, 7.5), activePersona);
    hud = lerp(hud, cGold, perDg);

    float fpsClamped = clamp(floor(fpsVal + 0.5), 0.0, 999.0);
    int dHund = (int)floor(fpsClamped / 100.0);
    float rem100 = fpsClamped - (float)(dHund * 100);
    int dTens = (int)floor(rem100 / 10.0);
    int dOnes = (int)floor(rem100 - (float)(dTens * 10) + 0.1);

    float xRight = BUFFER_WIDTH - 46.0;
    float mFps = 0.0;
    if (dHund > 0)
    {
        mFps = max(mFps, OmegaDigit7Seg(px - float2(xRight, 7.5), dHund));
    }
    if (dHund > 0 || dTens > 0)
    {
        mFps = max(mFps, OmegaDigit7Seg(px - float2(xRight + 11.0, 7.5), dTens));
    }
    mFps = max(mFps, OmegaDigit7Seg(px - float2(xRight + 22.0, 7.5), dOnes));

    float3 fpsCol = cCyan;
    if (fpsVal < 30.0) { fpsCol = cCoral; }
    else if (fpsVal < 54.0) { fpsCol = cGold; }
    hud = lerp(hud, fpsCol, saturate(mFps));

    return saturate(hud);
}

float4 PS_OmegaTriEngine(float4 vpos : SV_Position, float2 texcoord : TEXCOORD) : SV_Target
{
    float liveMaskF = floor(max(0.0, U11.w) + 0.5);
    bool busLive = (liveMaskF > 0.5) || (U0.z > 0.001);
    float bit5Check = frac(floor(liveMaskF / 32.0) * 0.5);
    bool skseWorldLive = (bit5Check > 0.25);

    bool v2Active = (abs(U27.x - 180.0) < 0.5);
    if (v2Active && U27.w > 0.5)
    {
        skseWorldLive = true;
    }
    bool rsAddonLive = busLive;
    if (v2Active && U27.z > 0.5)
    {
        rsAddonLive = true;
    }

    bool topStripAllowed = UiShowTopDiagBar;
    if (v2Active && U24.x < 0.5)
    {
        topStripAllowed = false;
    }

    bool shaderMasterOn = true;
    if (v2Active && U24.y < 0.5)
    {
        shaderMasterOn = false;
    }

    float menuFadeAlpha = 1.0;
    if (v2Active && UiAutoMenuFade && U24.z > 0.5)
    {
        menuFadeAlpha = clamp(U25.z, 0.06, 1.0);
    }

    float aurMult = 1.0;
    float cauMult = 1.0;
    float hdrBusMult = 1.0;
    float personaVig = 0.18;
    int activePersona = UiAestheticPersona;
    float waterPlaneY = -0.14;
    float waterRippleGain = 1.0;
    int spellSchoolId = 0;
    float spellIntensity = 0.0;

    if (v2Active)
    {
        if (U26.y > 0.05) { aurMult = clamp(U26.y, 0.1, 3.0); }
        if (U26.z > 0.05) { cauMult = clamp(U26.z, 0.1, 3.0); }
        if (UiFollowLivePersona)
        {
            activePersona = (int)clamp(floor(U20.x + 0.5), 0.0, 3.0);
        }
        if (U20.z >= 0.01) { personaVig = clamp(U20.z, 0.0, 0.65); }
        if (U20.w > 0.05)  { hdrBusMult = clamp(U20.w, 0.1, 3.0); }
        waterPlaneY = clamp(U21.x, -500.0, 500.0);
        if (U21.y > 0.05)  { waterRippleGain = clamp(U21.y, 0.1, 3.0); }
        spellSchoolId = (int)clamp(floor(U21.z + 0.5), 0.0, 5.0);
        spellIntensity = clamp(U21.w, 0.0, 2.5);
    }

    float masterGain = menuFadeAlpha;
    if (!shaderMasterOn)
    {
        masterGain = 0.0;
    }

    float tSec = Timer * 0.001;
    if (busLive)
    {
        tSec = U0.z;
    }

    int activeSym = UiSymmetryMode;
    if (busLive && UiUseLiveBusSymmetry)
    {
        activeSym = (int)clamp(floor(U9.x + 0.5), 0.0, 7.0);
    }
    if (v2Active && UiUseLiveBusSymmetry && U26.x >= -0.25)
    {
        activeSym = (int)clamp(floor(U26.x + 0.5), 0.0, 7.0);
    }
    if (UiSheafQuestGuard && skseWorldLive)
    {
        if (U16.w >= 1.5)
        {
            activeSym = 3;
        }
        else if (U16.w < -0.5 && activeSym == 3)
        {
            activeSym = 0;
        }
    }

    float depth = ReShade::GetLinearizedDepth(texcoord);
    float dRight = ReShade::GetLinearizedDepth(texcoord + float2(BUFFER_RCP_WIDTH * 1.5, 0.0));
    float dDown  = ReShade::GetLinearizedDepth(texcoord + float2(0.0, BUFFER_RCP_HEIGHT * 1.5));
    float edgeRelief = abs(depth - dRight) + abs(depth - dDown);
    float3 vNorm = OmegaEstimateViewNormal(texcoord, depth);

    float tileScale = UiTileScale;
    if (busLive && U9.y > 0.05)
    {
        tileScale = U9.y * UiTileScale;
    }

    float2 centered = texcoord - float2(0.5, 0.5);
    centered.x = centered.x * (BUFFER_WIDTH * BUFFER_RCP_HEIGHT);
    float3 rayWorldDir = OmegaReconstructRayDir(centered);
    float3 worldXYZ = U1.xyz + rayWorldDir * (depth * 142.8);

    float rScreen = length(centered);
    float rShout = rScreen * 18.0;
    float shoutFalloff = 1.8;
    if (skseWorldLive && UiWorldSpaceLock)
    {
        rShout = length(worldXYZ - U13.xyz);
        shoutFalloff = 0.55;
    }
    float impWave = exp(-abs(rShout - U14.y) * shoutFalloff) * U14.x * masterGain;
    float2 dirUv = normalize(centered + float2(0.00001, 0.00001));
    float2 warpedUv = saturate(texcoord + dirUv * (impWave * 0.013 * cos(rShout * 3.2 - tSec * max(6.0, U14.z))));

    float3 col = tex2D(ReShade::BackBuffer, warpedUv).rgb;
    float weatherAuroraGain = 1.0;
    if (busLive && U14.w > 0.02)
    {
        weatherAuroraGain = U14.w;
    }

    float dPatternForGrade = 0.5;
    float3 spellCol = OmegaSpellSchoolPalette(spellSchoolId);

    if (masterGain > 0.001)
    {
        if (depth >= 0.992 && UiAuroraBlend > 0.001)
        {
            float2 skyP = centered * (2.7 * tileScale);
            if (skseWorldLive && UiWorldSpaceLock)
            {
                skyP = (rayWorldDir.xz / max(0.18, abs(rayWorldDir.y) + 0.22)) * (1.85 * tileScale);
            }
            float a0 = tSec * 0.04;
            float a1 = 1.25663706 + tSec * 0.04;
            float a2 = 2.51327412 + tSec * 0.04;
            float a3 = 3.76991118 + tSec * 0.04;
            float a4 = 5.02654824 + tSec * 0.04;
            float qSum = cos(dot(skyP, float2(cos(a0), sin(a0))) + U7.x + tSec * 0.42)
                       + cos(dot(skyP, float2(cos(a1), sin(a1))) + U7.y + tSec * 0.42)
                       + cos(dot(skyP, float2(cos(a2), sin(a2))) + U7.z + tSec * 0.42)
                       + cos(dot(skyP, float2(cos(a3), sin(a3))) + U7.w + tSec * 0.42)
                       + cos(dot(skyP, float2(cos(a4), sin(a4))) + U8.x + tSec * 0.42);

            float curtain = exp(-abs(qSum) * 0.92) * saturate(1.0 - texcoord.y * 1.32);
            float audioBoost = 0.65 + 0.85 * U3.x + 0.35 * U12.x;
            float sovngardeGold = saturate((weatherAuroraGain - 1.10) * 2.2);
            float3 aurBase = lerp(float3(0.06, 0.90, 0.70), float3(0.58, 0.24, 0.98), saturate(0.5 + 0.5 * sin(qSum + tSec * 0.38)));
            if (activePersona == 1)
            {
                aurBase = lerp(float3(0.72, 0.16, 0.22), float3(0.92, 0.64, 0.24), saturate(0.5 + 0.5 * sin(qSum * 0.8)));
            }
            else if (activePersona == 2)
            {
                aurBase = lerp(float3(0.52, 0.86, 0.92), float3(0.86, 0.58, 0.98), saturate(0.5 + 0.5 * cos(qSum * 0.7 + tSec * 0.25)));
            }
            else if (activePersona == 3)
            {
                aurBase = float3(0.78, 0.84, 0.92);
            }
            float3 aur = lerp(aurBase, float3(0.98, 0.84, 0.32), sovngardeGold * 0.45);
            if (spellSchoolId > 0 && spellIntensity > 0.02)
            {
                aur = lerp(aur, spellCol, saturate(spellIntensity * 0.38));
            }
            col = col + aur * (curtain * UiAuroraBlend * aurMult * weatherAuroraGain * audioBoost * 0.56 * masterGain);
        }
        else if (UiCausticStrength > 0.001)
        {
            float planarMask = lerp(1.0, saturate(abs(vNorm.y) * 1.35 + 0.15), UiNormalPlanarWeight);
            float2 wp = centered * (depth * 24.0 * tileScale) + float2(U8.y, U8.z) * 1.5;
            float waterBandBoost = 1.0;
            if (skseWorldLive && UiWorldSpaceLock)
            {
                wp = worldXYZ.xz * (0.42 * tileScale) + float2(U8.y, U8.z) * 1.5;
                float distWater = abs(worldXYZ.y - waterPlaneY);
                float shorelineMask = exp(-distWater * 1.35) * saturate(abs(vNorm.y) * 1.4);
                waterBandBoost = 1.0 + shorelineMask * (0.85 * waterRippleGain);
                wp = wp + float2(sin(tSec * 1.35 + worldXYZ.z * 1.8), cos(tSec * 1.15 + worldXYZ.x * 1.8)) * (shorelineMask * 0.09);
            }

            float dPattern = 0.5;
            if (activeSym == 7)
            {
                dPattern = OmegaLeech24GolaySeam(wp, tSec);
            }
            else if (activeSym == 6)
            {
                dPattern = OmegaPoincare534Seam(wp, tSec);
            }
            else if (activeSym == 5)
            {
                dPattern = OmegaPolychoraH4Seam(wp, tSec);
            }
            else if (activeSym == 4)
            {
                dPattern = OmegaHopfVillarceauSeam(wp, tSec);
            }
            else if (activeSym == 3)
            {
                float2 curl = wp + 0.16 * float2(sin(wp.y * 2.094 + tSec * 0.55), -sin(wp.x * 2.094 - tSec * 0.55));
                dPattern = OmegaTriSeam(curl);
            }
            else if (activeSym == 2)
            {
                dPattern = min(OmegaTriSeam(wp), OmegaTriSeam(wp * 0.381966 + float2(0.37, -0.22)) * 1.35);
            }
            else if (activeSym == 1)
            {
                dPattern = OmegaE8FacetSeam(wp, tSec);
            }
            else
            {
                float sh0 = tSec * 0.16;
                float sh1 = tSec * 0.16;
                float sh2 = tSec * 0.16;
                float sh3 = tSec * 0.16;
                float sh4 = tSec * 0.16;
                if ((abs(U7.x) + abs(U7.y) + abs(U7.z)) > 0.0005)
                {
                    sh0 = U7.x;
                    sh1 = U7.y;
                    sh2 = U7.z;
                    sh3 = U7.w;
                    sh4 = U8.x;
                }
                float m5 = OmegaPenroseAxisSeam(wp, 0.0, sh0);
                m5 = min(m5, OmegaPenroseAxisSeam(wp, 1.25663706, sh1));
                m5 = min(m5, OmegaPenroseAxisSeam(wp, 2.51327412, sh2));
                m5 = min(m5, OmegaPenroseAxisSeam(wp, 3.76991118, sh3));
                m5 = min(m5, OmegaPenroseAxisSeam(wp, 5.02654824, sh4));
                dPattern = m5;
            }
            dPatternForGrade = dPattern;

            float audioMid = 0.55 + 0.75 * U3.y + 0.30 * U3.w;
            float caus = exp(-dPattern * 14.5) * (1.0 - depth * 0.88) * planarMask * UiCausticStrength * cauMult * waterBandBoost * audioMid * masterGain;
            float3 baseTint = float3(0.10, 0.80, 0.96);
            if (activeSym == 1)
            {
                baseTint = float3(0.45, 0.62, 1.0);
            }
            else if (activeSym == 3)
            {
                baseTint = float3(0.96, 0.76, 0.24);
            }
            float specHue = frac(U12.w / 240.0 + tSec * 0.05 + U3.x * 0.18);
            float3 tint = lerp(baseTint, OmegaHsv2Rgb(float3(specHue, 0.75, 1.0)), saturate(UiSpectralHarmony));
            if (spellSchoolId > 0 && spellIntensity > 0.02)
            {
                tint = lerp(tint, spellCol, saturate(spellIntensity * 0.65));
            }
            float thuumCausticBoost = 1.0 + impWave * 1.45 + spellIntensity * 0.35;
            col = col + tint * (caus * thuumCausticBoost * 0.44);
            if (UiSheafQuestGuard && skseWorldLive && U16.w < -0.5)
            {
                col = col + float3(0.96, 0.52, 0.12) * (exp(-dPattern * 20.0) * 0.22 * masterGain);
            }
        }

        // Half-Res RGBA16F Dual-Kawase HDR BloomUpsample + Depth-Bilateral Crepuscular Godrays
        if (UiHdrBloomStrength > 0.001 || UiGodrayStrength > 0.001)
        {
            float2 upPx = float2(BUFFER_RCP_WIDTH * 1.5, BUFFER_RCP_HEIGHT * 1.5);
            float4 b0 = tex2D(OmegaHdrHalfSamp, saturate(warpedUv + float2( upPx.x,  upPx.y)));
            float4 b1 = tex2D(OmegaHdrHalfSamp, saturate(warpedUv + float2(-upPx.x,  upPx.y)));
            float4 b2 = tex2D(OmegaHdrHalfSamp, saturate(warpedUv + float2( upPx.x, -upPx.y)));
            float4 b3 = tex2D(OmegaHdrHalfSamp, saturate(warpedUv + float2(-upPx.x, -upPx.y)));
            float4 bloomTap = (b0 + b1 + b2 + b3) * 0.25;

            float personaBloomScale = 1.0;
            if (activePersona == 2)      { personaBloomScale = 1.38; }
            else if (activePersona == 3) { personaBloomScale = 0.55; }
            col = col + bloomTap.rgb * (UiHdrBloomStrength * hdrBusMult * personaBloomScale * 0.52 * masterGain);

            if (UiGodrayStrength > 0.001)
            {
                float2 sunAnchor = float2(0.5 + clamp(U17.x * 0.28, -0.35, 0.35), 0.16);
                float2 rayStep = (sunAnchor - texcoord) * 0.055;
                float shaftAcc = bloomTap.a * 0.30;
                float4 r1 = tex2D(OmegaHdrHalfSamp, saturate(texcoord + rayStep * 1.0));
                float4 r2 = tex2D(OmegaHdrHalfSamp, saturate(texcoord + rayStep * 2.0));
                float4 r3 = tex2D(OmegaHdrHalfSamp, saturate(texcoord + rayStep * 3.0));
                float4 r4 = tex2D(OmegaHdrHalfSamp, saturate(texcoord + rayStep * 4.0));
                shaftAcc = shaftAcc + (r1.a * 0.26 + r2.a * 0.20 + r3.a * 0.14 + r4.a * 0.10);
                float bilateralGate = 1.0 - saturate(edgeRelief * 6.0) * 0.45;
                float3 shaftCol = float3(0.96, 0.86, 0.62);
                if (activePersona == 1)      { shaftCol = float3(0.92, 0.52, 0.24); }
                else if (activePersona == 2) { shaftCol = float3(0.74, 0.68, 0.98); }
                else if (activePersona == 3) { shaftCol = float3(0.82, 0.86, 0.92); }
                if (spellSchoolId > 0 && spellIntensity > 0.05)
                {
                    shaftCol = lerp(shaftCol, spellCol, saturate(spellIntensity * 0.45));
                }
                col = col + shaftCol * (shaftAcc * bilateralGate * UiGodrayStrength * hdrBusMult * 0.26 * masterGain);
            }
        }

        float3 pulseTint = float3(0.24, 0.86, 1.0);
        if (spellSchoolId > 0 && spellIntensity > 0.02)
        {
            pulseTint = lerp(pulseTint, spellCol, saturate(spellIntensity * 0.80));
        }
        col = col + pulseTint * (impWave * 0.65);

        col = OmegaApplyPersonaGrade(col, activePersona, centered, depth, dPatternForGrade, edgeRelief, personaVig, masterGain);
    }

    if (topStripAllowed && vpos.y < 28.0)
    {
        float liveFps = 60.0;
        if (OmegaRsFrameMs > 0.20)
        {
            liveFps = 1000.0 / OmegaRsFrameMs;
        }
        if (v2Active && U25.x > 1.0)
        {
            liveFps = U25.x;
        }
        float aurLvl = saturate(UiAuroraBlend * aurMult * weatherAuroraGain * 0.55);
        float cauLvl = saturate(UiCausticStrength * cauMult * 1.6);
        float thmLvl = saturate(U14.x * 0.75 + U3.x * 0.45 + spellIntensity * 0.40);
        col = OmegaRenderTopDiagStrip(
            vpos.xy,
            col,
            shaderMasterOn,
            skseWorldLive,
            rsAddonLive,
            activeSym,
            activePersona,
            aurLvl,
            cauLvl,
            menuFadeAlpha,
            thmLvl,
            U16.w,
            liveFps);
    }

    return float4(saturate(col), 1.0);
}

technique OmegaTriEngine <
    ui_tooltip = ""Omega Tri-Engine v1.9.0 (Phase 2: 28px Top Bar + 4 Dark-Fantasy/Dreamcore/Nihilist Personas + Half-Res RGBA16F HDR Bloom/Godrays + Water/Spell Coupling)"";
>
{
    pass P0_HalfHdr
    {
        VertexShader = PostProcessVS;
        PixelShader  = PS_OmegaHdrHalfPrepass;
        RenderTarget = OmegaHdrHalfTex;
    }
    pass P1_Composite
    {
        VertexShader = PostProcessVS;
        PixelShader  = PS_OmegaTriEngine;
    }
}";
        }

        // Exports standards-compliant CC0 ReShade.fxh + BSD-3-Clause/MIT ReShadeFX 6.8 .fx + .addon64 C++ reference stub
        public static string ExportReshadeFxAndAddonStub(
            string exportsDir,
            float[] currentU80,
            string customOmsh,
            out string addonStubPath,
            out string fxSha256)
        {
            Directory.CreateDirectory(exportsDir);
            UTF8Encoding utf8NoBom = new UTF8Encoding(false);

            string fxhPath = Path.Combine(exportsDir, "ReShade.fxh");
            string fxPath = Path.Combine(exportsDir, "OmegaTriEngine_SkyrimAE.fx");
            string presetIniPath = Path.Combine(exportsDir, "OmegaTriEngine_Preset.ini");
            addonStubPath = Path.Combine(exportsDir, "OmegaTriEngine_Addon.cpp");

            int symMode = (currentU80 != null && currentU80.Length >= 40) ? Math.Max(0, Math.Min(7, (int)Math.Round(currentU80[36]))) : 0;
            float defAurora = (currentU80 != null && currentU80.Length >= 60) ? currentU80[59] : 0.78f;
            float defScale  = (currentU80 != null && currentU80.Length >= 40) ? currentU80[37] : 0.85f;

            File.WriteAllText(fxhPath, GetUpstreamCc0ReShadeFxh(), utf8NoBom);
            string fxContent = GetCanonicalSkyrimAeWorldShaderFx();
            File.WriteAllText(fxPath, fxContent, utf8NoBom);

            string skyrimDepthDefs = "RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000.0,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=0,RESHADE_DEPTH_INPUT_IS_REVERSED=0,RESHADE_DEPTH_INPUT_IS_LOGARITHMIC=0";
            string presetIni =
                "Techniques=OmegaTriEngine@OmegaTriEngine_SkyrimAE.fx\r\n" +
                "TechniqueSorting=OmegaTriEngine@OmegaTriEngine_SkyrimAE.fx\r\n" +
                "PreprocessorDefinitions=" + skyrimDepthDefs + "\r\n\r\n" +
                "[OmegaTriEngine_SkyrimAE.fx]\r\n" +
                "UiShowTopDiagBar=1\r\n" +
                "UiAutoMenuFade=1\r\n" +
                "UiFollowLivePersona=1\r\n" +
                "UiAestheticPersona=0\r\n" +
                "UiHdrBloomStrength=0.340000\r\n" +
                "UiGodrayStrength=0.280000\r\n" +
                "UiUseLiveBusSymmetry=1\r\n" +
                string.Format(CultureInfo.InvariantCulture, "UiSymmetryMode={0}\r\n", symMode) +
                string.Format(CultureInfo.InvariantCulture, "UiAuroraBlend={0:0.000000}\r\n", defAurora) +
                "UiCausticStrength=0.360000\r\n" +
                "UiSpectralHarmony=0.280000\r\n" +
                string.Format(CultureInfo.InvariantCulture, "UiTileScale={0:0.000000}\r\n", defScale) +
                "UiNormalPlanarWeight=0.720000\r\n" +
                "UiWorldSpaceLock=1\r\n" +
                "UiSheafQuestGuard=1\r\n";
            File.WriteAllText(presetIniPath, presetIni, utf8NoBom);

            byte[] fxBytes = utf8NoBom.GetBytes(fxContent);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hb = sha.ComputeHash(fxBytes);
                StringBuilder hs = new StringBuilder(64);
                for (int i = 0; i < hb.Length; i++) hs.Append(hb[i].ToString("x2", CultureInfo.InvariantCulture));
                fxSha256 = hs.ToString();
            }

            string addonCppRef = "// Companion C++17 reference; compiled native binary is OmegaTriEngine.addon64 (vtable[19/22/25/27/33])\r\n" +
                                 "// SPDX-License-Identifier: BSD-3-Clause OR MIT\r\n" +
                                 "#include <reshade.hpp>\r\n";
            string repoCpp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reshade_addon", "OmegaTriEngine_Addon.cpp");
            if (File.Exists(repoCpp))
            {
                try { addonCppRef = File.ReadAllText(repoCpp, Encoding.UTF8); } catch { }
            }
            File.WriteAllText(addonStubPath, addonCppRef, utf8NoBom);

            string builtAddon = FindCompiledAddon64Path();
            if (!string.IsNullOrEmpty(builtAddon) && File.Exists(builtAddon))
            {
                string dstAddon = Path.Combine(exportsDir, "OmegaTriEngine.addon64");
                if (!string.Equals(Path.GetFullPath(builtAddon), Path.GetFullPath(dstAddon), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Copy(builtAddon, dstAddon, true); } catch { }
                }
            }
            return fxPath;
        }

        public static string FindCompiledAddon64Path()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd((char)92, '/');
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "exports", "OmegaTriEngine.addon64"),
                Path.Combine(baseDir, "reshade_addon", "target", "release", "omega_tri_engine_addon.dll"),
                Path.Combine(baseDir, "..", "downloads", "omega_engine_exports", "OmegaTriEngine.addon64")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    string full = Path.GetFullPath(candidates[i]);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return "";
        }

        public static string FindCompiledMockHost64Path()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd((char)92, '/');
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "exports", "OmegaReShadeMockHost64.dll"),
                Path.Combine(baseDir, "reshade_mock_host", "target", "release", "omega_reshade_mock_host.dll"),
                Path.Combine(baseDir, "..", "downloads", "omega_engine_exports", "OmegaReShadeMockHost64.dll")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    string full = Path.GetFullPath(candidates[i]);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return "";
        }

        public static string FindCompiledSkseBridgeDllPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd((char)92, '/');
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "exports", "OmegaSkyrimWorldBridge.dll"),
                Path.Combine(baseDir, "skse64_bridge", "target", "release", "omega_skyrim_world_bridge.dll"),
                Path.Combine(baseDir, "..", "downloads", "omega_engine_exports", "OmegaSkyrimWorldBridge.dll")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    string full = Path.GetFullPath(candidates[i]);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return "";
        }

        public static string FindCompiledSkseMockHostDllPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd((char)92, '/');
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "exports", "OmegaSkseMockHost64.dll"),
                Path.Combine(baseDir, "skse64_mock_host", "target", "release", "omega_skse_mock_host.dll"),
                Path.Combine(baseDir, "..", "downloads", "omega_engine_exports", "OmegaSkseMockHost64.dll")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    string full = Path.GetFullPath(candidates[i]);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return "";
        }

        private static int MapPeRvaToOffset(byte[] pe, int secOff, int numSec, uint rva)
        {
            if (rva == 0u) return -1;
            for (int s = 0; s < numSec; s++)
            {
                int sh = secOff + s * 40;
                if (sh + 40 > pe.Length) break;
                uint vSize = BitConverter.ToUInt32(pe, sh + 8);
                uint vAddr = BitConverter.ToUInt32(pe, sh + 12);
                uint rSize = BitConverter.ToUInt32(pe, sh + 16);
                uint rPtr  = BitConverter.ToUInt32(pe, sh + 20);
                uint span  = (vSize > rSize) ? vSize : rSize;
                if (rva >= vAddr && rva < vAddr + span)
                {
                    long off = (long)rPtr + (long)(rva - vAddr);
                    return (off >= 0 && off < pe.Length) ? (int)off : -1;
                }
            }
            return -1;
        }

        private static string ReadAsciiZ(byte[] pe, int off)
        {
            if (off < 0 || off >= pe.Length) return "";
            int end = off;
            while (end < pe.Length && pe[end] != 0 && (end - off) < 260) end++;
            return Encoding.ASCII.GetString(pe, off, end - off);
        }

        public static bool InspectPe32PlusZeroCrt(
            string dllPath,
            out List<string> importedDlls,
            out List<string> exportedNames,
            out long sizeBytes,
            out string errorMsg)
        {
            importedDlls = new List<string>();
            exportedNames = new List<string>();
            sizeBytes = 0L;
            errorMsg = "";
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                errorMsg = "File missing";
                return false;
            }
            byte[] pe = File.ReadAllBytes(dllPath);
            sizeBytes = pe.Length;
            if (pe.Length < 256 || pe[0] != 0x4D || pe[1] != 0x5A)
            {
                errorMsg = "Missing MZ signature";
                return false;
            }
            int peOff = BitConverter.ToInt32(pe, 0x3C);
            if (peOff < 64 || peOff + 264 > pe.Length || BitConverter.ToUInt32(pe, peOff) != 0x00004550u)
            {
                errorMsg = "Missing PE00 signature";
                return false;
            }
            int coffOff = peOff + 4;
            ushort machine = BitConverter.ToUInt16(pe, coffOff);
            ushort numSec = BitConverter.ToUInt16(pe, coffOff + 2);
            ushort optSize = BitConverter.ToUInt16(pe, coffOff + 16);
            ushort chars = BitConverter.ToUInt16(pe, coffOff + 18);
            if (machine != 0x8664 || (chars & 0x2000) == 0)
            {
                errorMsg = "Not x86_64 DLL (machine=0x" + machine.ToString("X4", CultureInfo.InvariantCulture) + ")";
                return false;
            }
            int optOff = coffOff + 20;
            ushort optMagic = BitConverter.ToUInt16(pe, optOff);
            if (optMagic != 0x020B)
            {
                errorMsg = "Not PE32+ (magic=0x" + optMagic.ToString("X4", CultureInfo.InvariantCulture) + ")";
                return false;
            }
            int secOff = optOff + optSize;
            uint expRva = BitConverter.ToUInt32(pe, optOff + 112);
            uint impRva = BitConverter.ToUInt32(pe, optOff + 120);

            int impOff = MapPeRvaToOffset(pe, secOff, numSec, impRva);
            if (impOff >= 0)
            {
                for (int d = impOff; d + 20 <= pe.Length; d += 20)
                {
                    uint nameRva = BitConverter.ToUInt32(pe, d + 12);
                    uint ftRva   = BitConverter.ToUInt32(pe, d + 16);
                    if (nameRva == 0u && ftRva == 0u) break;
                    int nOff = MapPeRvaToOffset(pe, secOff, numSec, nameRva);
                    string dllName = ReadAsciiZ(pe, nOff);
                    if (!string.IsNullOrEmpty(dllName)) importedDlls.Add(dllName);
                }
            }

            int expOff = MapPeRvaToOffset(pe, secOff, numSec, expRva);
            if (expOff >= 0 && expOff + 40 <= pe.Length)
            {
                uint numNames = BitConverter.ToUInt32(pe, expOff + 24);
                uint namesRva = BitConverter.ToUInt32(pe, expOff + 32);
                int namesOff  = MapPeRvaToOffset(pe, secOff, numSec, namesRva);
                if (namesOff >= 0 && numNames <= 4096u)
                {
                    for (uint i = 0; i < numNames; i++)
                    {
                        int p = namesOff + (int)(i * 4u);
                        if (p + 4 > pe.Length) break;
                        uint symRva = BitConverter.ToUInt32(pe, p);
                        int symOff  = MapPeRvaToOffset(pe, secOff, numSec, symRva);
                        string sym  = ReadAsciiZ(pe, symOff);
                        if (!string.IsNullOrEmpty(sym)) exportedNames.Add(sym);
                    }
                }
            }
            return true;
        }

        public static bool DeployBridgeToGameDirectory(
            string gameDir,
            float[] currentU80,
            string customOmsh,
            out string summaryOut)
        {
            summaryOut = "";
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                summaryOut = "Target game directory does not exist.";
                return false;
            }

            string shadersDir = Path.Combine(Path.Combine(gameDir, "reshade-shaders"), "Shaders");
            Directory.CreateDirectory(shadersDir);

            string stubTmp, sha;
            string tempExp = Path.Combine(Path.GetTempPath(), "omega_reshade_stage_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(tempExp);
            string stagedFx = ExportReshadeFxAndAddonStub(tempExp, currentU80, customOmsh, out stubTmp, out sha);
            string stagedFxh = Path.Combine(tempExp, "ReShade.fxh");

            string dstFxh = Path.Combine(shadersDir, "ReShade.fxh");
            string dstFx = Path.Combine(shadersDir, "OmegaTriEngine_SkyrimAE.fx");
            if (File.Exists(stagedFxh)) File.Copy(stagedFxh, dstFxh, true);
            File.Copy(stagedFx, dstFx, true);

            string stagedIni = Path.Combine(tempExp, "OmegaTriEngine_Preset.ini");
            string dstPreset = Path.Combine(gameDir, "OmegaTriEngine_Preset.ini");
            if (File.Exists(stagedIni)) File.Copy(stagedIni, dstPreset, true);

            string srcAddon = FindCompiledAddon64Path();
            bool copiedAddon = false;
            string dstAddon = Path.Combine(gameDir, "OmegaTriEngine.addon64");
            if (!string.IsNullOrEmpty(srcAddon) && File.Exists(srcAddon))
            {
                File.Copy(srcAddon, dstAddon, true);
                copiedAddon = true;
            }

            string reshadeIni = Path.Combine(gameDir, "ReShade.ini");
            if (!File.Exists(reshadeIni))
            {
                UTF8Encoding utf8NoBom = new UTF8Encoding(false);
                string minIni =
                    "[ADDON]\r\n" +
                    "AddonPath=.\\\r\n" +
                    "DisabledAddons=\r\n\r\n" +
                    "[GENERAL]\r\n" +
                    "EffectSearchPaths=.\\reshade-shaders\\Shaders\r\n" +
                    "TextureSearchPaths=.\\reshade-shaders\\Textures\r\n" +
                    "PresetPath=.\\OmegaTriEngine_Preset.ini\r\n" +
                    "PerformanceMode=0\r\n" +
                    "SkipLoadingDisabledEffects=0\r\n" +
                    "PreprocessorDefinitions=RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000.0,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=0,RESHADE_DEPTH_INPUT_IS_REVERSED=0,RESHADE_DEPTH_INPUT_IS_LOGARITHMIC=0\r\n\r\n" +
                    "[INPUT]\r\n" +
                    "KeyOverlay=113,0,1,0\r\n" +
                    "KeyEffects=145,0,0,0\r\n";
                File.WriteAllText(reshadeIni, minIni, utf8NoBom);
            }

            try
            {
                string tmpRs = Path.Combine(Path.GetTempPath(), "ReShade");
                if (Directory.Exists(tmpRs))
                {
                    string[] blobs = Directory.GetFiles(tmpRs, "reshade-*");
                    for (int bi = 0; bi < blobs.Length; bi++)
                    {
                        try { File.Delete(blobs[bi]); } catch { }
                    }
                }
            }
            catch { }

            try { Directory.Delete(tempExp, true); } catch { }

            string srcSkse = FindCompiledSkseBridgeDllPath();
            bool copiedSkse = false;
            string dataDir = Path.Combine(gameDir, "Data");
            if (Directory.Exists(dataDir) && !string.IsNullOrEmpty(srcSkse) && File.Exists(srcSkse))
            {
                try
                {
                    string sksePlugDir = Path.Combine(Path.Combine(dataDir, "SKSE"), "Plugins");
                    Directory.CreateDirectory(sksePlugDir);
                    File.Copy(srcSkse, Path.Combine(sksePlugDir, "OmegaSkyrimWorldBridge.dll"), true);
                    OmegaEngineSkseModBuilder.EmitEslLightPluginEsp(Path.Combine(dataDir, "OmegaTriEngine_WorldBridge.esp"));
                    OmegaEngineSkseModBuilder.EnsureCreationKitCustomIni(gameDir);
                    copiedSkse = true;
                }
                catch { }
            }

            summaryOut = string.Format(CultureInfo.InvariantCulture,
                "Deployed ReShade.fxh + .fx -> {0} | Addon64={1} | SKSE64={2}",
                dstFx, copiedAddon ? dstAddon : "NOT_FOUND", copiedSkse ? "Data\\SKSE\\Plugins\\OmegaSkyrimWorldBridge.dll" : "RootOnly");
            return copiedAddon && File.Exists(dstFx) && File.Exists(dstFxh);
        }
    }
}