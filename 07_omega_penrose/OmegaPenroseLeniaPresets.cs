using System;

namespace OmegaPenroseStudio
{
    public sealed class LeniaSpeciesSpec
    {
        public string Name;
        public float Mu, Sigma;
        public int KernelRadius;
        public float Beta0, Beta1, Beta2;
        public int RingCount;
        public float Dt, SeedRadius, FlowCrossGain;
        public override string ToString() { return Name; }
    }

    public static class OmegaPenroseLeniaPresets
    {
        public static readonly LeniaSpeciesSpec[] Species = new LeniaSpeciesSpec[]
        {
            Make("01. Tri-Species Coral Symbiosis (RGB Flow-Lenia)",     0.210f, 0.0480f, 13, 1.00f, 0.62f, 0.00f, 2, 0.16f, 14.0f, 0.42f),
            Make("02. Darwinian Niche Speciators (Mutating Mu/Sigma)",   0.200f, 0.0460f, 13, 1.00f, 0.50f, 0.00f, 2, 0.17f, 13.5f, 0.55f),
            Make("03. Predator-Prey Chemotactic Chasers (RGB Flow)",     0.185f, 0.0420f, 14, 1.00f, 0.35f, 0.00f, 2, 0.18f, 13.0f, 0.68f),
            Make("04. Conway-Worm Phason Surfers (RGB Waveguide)",       0.195f, 0.0450f, 13, 0.80f, 1.00f, 0.30f, 3, 0.17f, 13.5f, 0.52f),
            Make("05. Kohmoto-Sutherland 5-Fold Quantum Ring Packet",    0.180f, 0.0350f, 13, 1.00f, 0.00f, 0.00f, 1, 0.12f, 18.0f, 0.30f),
            Make("06. Orbium unicaudatus (Classic Glider Soliton)",      0.150f, 0.0155f, 13, 1.00f, 0.00f, 0.00f, 1, 0.10f, 11.5f, 0.35f),
            Make("07. Hydrogeminium natans (3-Ring Spinner)",            0.170f, 0.0185f, 15, 0.50f, 1.00f, 0.35f, 3, 0.10f, 14.0f, 0.40f),
            Make("08. Pentapora 10-Fold Starfish Symbiont",              0.205f, 0.0265f, 15, 0.85f, 1.00f, 0.45f, 3, 0.09f, 15.0f, 0.45f)
        };

        private static LeniaSpeciesSpec Make(
            string name, float mu, float sig, int rad,
            float b0, float b1, float b2, int rings, float dt, float seedR, float cross)
        {
            LeniaSpeciesSpec s = new LeniaSpeciesSpec();
            s.Name = name; s.Mu = mu; s.Sigma = sig; s.KernelRadius = rad;
            s.Beta0 = b0; s.Beta1 = b1; s.Beta2 = b2; s.RingCount = rings;
            s.Dt = dt; s.SeedRadius = seedR; s.FlowCrossGain = cross;
            return s;
        }

        public static float[] BuildNormalizedKernel33(LeniaSpeciesSpec spec)
        {
            const int S = 33, C = 16;
            float[] k = new float[S * S];
            float rad = Math.Max(4.0f, Math.Min(16.0f, spec.KernelRadius));
            float[] betas = new float[] { spec.Beta0, spec.Beta1, spec.Beta2 };
            int B = Math.Max(1, Math.Min(3, spec.RingCount));

            double sum = 0.0;
            for (int dy = -C; dy <= C; dy++)
            {
                for (int dx = -C; dx <= C; dx++)
                {
                    double r = Math.Sqrt(dx * dx + dy * dy) / rad;
                    double w = 0.0;
                    if (r < 1.0)
                    {
                        double br = B * r;
                        int ringIdx = Math.Min(B - 1, (int)Math.Floor(br));
                        double localR = br - ringIdx;
                        w = betas[ringIdx] * Math.Exp(4.0 - 1.0 / Math.Max(1e-4, localR * (1.0 - localR)));
                    }
                    k[(dy + C) * S + (dx + C)] = (float)w;
                    sum += w;
                }
            }
            if (sum > 1e-9)
            {
                float inv = (float)(1.0 / sum);
                for (int i = 0; i < k.Length; i++) k[i] *= inv;
            }
            return k;
        }

        public static void StampRgbOrganismIntoField(
            float[] fieldRgba512, float[] genome2, int gridN,
            float normX, float normY, LeniaSpeciesSpec spec, float headingRad, int channelMask)
        {
            int cx = (int)Math.Round(normX * (gridN - 1));
            int cy = (int)Math.Round(normY * (gridN - 1));
            float sr = Math.Max(6.0f, spec.SeedRadius * 1.15f);
            int ir = (int)Math.Ceiling(sr * 1.35f);
            float dirX = (float)Math.Cos(headingRad), dirY = (float)Math.Sin(headingRad);

            for (int dy = -ir; dy <= ir; dy++)
            {
                int py = (cy + dy + gridN) & (gridN - 1);
                for (int dx = -ir; dx <= ir; dx++)
                {
                    int px = (cx + dx + gridN) & (gridN - 1);
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / sr;
                    if (d > 1.15f) continue;

                    float asym = 1.0f + 0.24f * ((dx * dirX + dy * dirY) / Math.Max(1.0f, sr));
                    float ring = (float)Math.Exp(-Math.Pow((d - 0.36f) / 0.34f, 2.0));
                    float core = (float)Math.Exp(-Math.Pow(d / 0.42f, 2.0)) * 0.60f;
                    float v = Math.Min(1.0f, Math.Max(0.0f, (ring * 0.78f + core * 0.48f) * asym));

                    int cell = py * gridN + px;
                    int baseIdx = cell * 4;
                    if (channelMask == 0 || channelMask == 1)
                        fieldRgba512[baseIdx + 0] = Math.Max(fieldRgba512[baseIdx + 0], v);
                    if (channelMask == 0 || channelMask == 2)
                        fieldRgba512[baseIdx + 1] = Math.Max(fieldRgba512[baseIdx + 1], v * (channelMask == 0 ? 0.85f : 1.0f));
                    if (channelMask == 0 || channelMask == 3)
                        fieldRgba512[baseIdx + 2] = Math.Max(fieldRgba512[baseIdx + 2], v * (channelMask == 0 ? 0.70f : 1.0f));
                    fieldRgba512[baseIdx + 3] = Math.Max(fieldRgba512[baseIdx + 3], v * 0.6f);

                    if (genome2 != null)
                    {
                        float nicheOffset = (channelMask == 1) ? -0.024f : ((channelMask == 3) ? 0.024f : 0.0f);
                        genome2[cell * 2 + 0] = spec.Mu + nicheOffset;
                        genome2[cell * 2 + 1] = spec.Sigma;
                    }
                }
            }
        }

        // Seed a 5-fold resonant Kohmoto-Sutherland Quantum Wavepacket (x=psiRe, y=psiIm, z=|psi|^2, w=envelope)!
        public static void StampQuantumWavepacketIntoField(
            float[] fieldRgba512, int gridN, float normX, float normY, float sigmaCells, float kx, float ky, int angMomM)
        {
            int cx = (int)Math.Round(normX * (gridN - 1));
            int cy = (int)Math.Round(normY * (gridN - 1));
            int ir = (int)Math.Ceiling(sigmaCells * 3.2f);
            for (int dy = -ir; dy <= ir; dy++)
            {
                int py = (cy + dy + gridN) & (gridN - 1);
                for (int dx = -ir; dx <= ir; dx++)
                {
                    int px = (cx + dx + gridN) & (gridN - 1);
                    float r2 = dx * dx + dy * dy;
                    float env = (float)Math.Exp(-r2 / (2.0f * sigmaCells * sigmaCells));
                    if (env < 1e-4f) continue;

                    double vortex = (angMomM != 0) ? (angMomM * Math.Atan2(dy, dx + 1e-6)) : 0.0;
                    double phase = kx * dx + ky * dy + vortex;
                    float re = env * (float)Math.Cos(phase);
                    float im = env * (float)Math.Sin(phase);

                    int o = (py * gridN + px) * 4;
                    fieldRgba512[o + 0] += re;
                    fieldRgba512[o + 1] += im;
                    float p2 = fieldRgba512[o + 0] * fieldRgba512[o + 0] + fieldRgba512[o + 1] * fieldRgba512[o + 1];
                    fieldRgba512[o + 2] = p2;
                    fieldRgba512[o + 3] = Math.Max(fieldRgba512[o + 3], (float)Math.Sqrt(p2));
                }
            }
        }
    }
}
