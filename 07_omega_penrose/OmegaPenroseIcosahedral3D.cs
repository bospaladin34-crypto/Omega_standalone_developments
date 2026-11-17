using System;
using System.Collections.Generic;

namespace OmegaPenroseStudio
{
    public struct CrystalQuad3D
    {
        public float X0, Y0, Z0;
        public float X1, Y1, Z1;
        public float X2, Y2, Z2;
        public float X3, Y3, Z3;
        public float Nx, Ny, Nz;
        public int MaterialKind; // 0=Prolate Sapphire, 1=Oblate Amethyst, 2=Indigo Terrace, 3=Emerald-Gold Sanctum
        public float GlowEnergy;
    }

    public struct ProjectedQuad2D
    {
        public float SX0, SY0, SX1, SY1, SX2, SY2, SX3, SY3;
        public float DepthZ;
        public float R, G, B, EdgeBoost;
    }

    public static class OmegaPenroseIcosahedral3D
    {
        public const int MaxGpuQuads = 3072;
        private static readonly double[][] ParAxes = BuildIcosaAxes(false);
        private static readonly double[][] PerpAxes = BuildIcosaAxes(true);

        private static double[][] BuildIcosaAxes(bool conjugate)
        {
            double p = conjugate ? (-1.0 / 1.6180339887498949) : 1.6180339887498949;
            double norm = 1.0 / Math.Sqrt(1.0 + p * p);
            return new double[][]
            {
                new double[] {  0.0 * norm,  1.0 * norm,  p * norm },
                new double[] {  0.0 * norm,  1.0 * norm, -p * norm },
                new double[] {  1.0 * norm,  p * norm,  0.0 * norm },
                new double[] {  1.0 * norm, -p * norm,  0.0 * norm },
                new double[] {  p * norm,  0.0 * norm,  1.0 * norm },
                new double[] { -p * norm,  0.0 * norm,  1.0 * norm }
            };
        }

        public static List<CrystalQuad3D> BuildIcosahedralRhombohedra6D(
            double phasonPhase, float radiusLimit, out int prolateCount, out int oblateCount)
        {
            prolateCount = 0; oblateCount = 0;
            List<CrystalQuad3D> quads = new List<CrystalQuad3D>(2400);

            double[] shift6 = new double[6];
            for (int k = 0; k < 6; k++)
                shift6[k] = 0.113 * (k + 1) + 0.22 * Math.Cos(phasonPhase + k * 1.04719755);

            int[] n = new int[6];
            double acceptR = 1.18;
            double maxPhysR = Math.Max(1.6, Math.Min(4.2, radiusLimit));

            for (int a0 = -1; a0 <= 1; a0++)
            for (int a1 = -1; a1 <= 1; a1++)
            for (int a2 = -1; a2 <= 1; a2++)
            for (int a3 = -1; a3 <= 1; a3++)
            for (int a4 = -1; a4 <= 1; a4++)
            for (int a5 = -1; a5 <= 1; a5++)
            {
                n[0] = a0; n[1] = a1; n[2] = a2; n[3] = a3; n[4] = a4; n[5] = a5;
                double px = 0, py = 0, pz = 0, wx = 0, wy = 0, wz = 0;
                for (int m = 0; m < 6; m++)
                {
                    px += n[m] * ParAxes[m][0]; py += n[m] * ParAxes[m][1]; pz += n[m] * ParAxes[m][2];
                    double nm = n[m] + shift6[m];
                    wx += nm * PerpAxes[m][0]; wy += nm * PerpAxes[m][1]; wz += nm * PerpAxes[m][2];
                }
                if (Math.Sqrt(wx * wx + wy * wy + wz * wz) > acceptR) continue;
                if (Math.Sqrt(px * px + py * py + pz * pz) > maxPhysR) continue;

                for (int i = 0; i < 6 && quads.Count + 6 <= MaxGpuQuads; i++)
                for (int j = i + 1; j < 6 && quads.Count + 6 <= MaxGpuQuads; j++)
                for (int k = j + 1; k < 6 && quads.Count + 6 <= MaxGpuQuads; k++)
                {
                    double cwx = wx + 0.5 * (PerpAxes[i][0] + PerpAxes[j][0] + PerpAxes[k][0]);
                    double cwy = wy + 0.5 * (PerpAxes[i][1] + PerpAxes[j][1] + PerpAxes[k][1]);
                    double cwz = wz + 0.5 * (PerpAxes[i][2] + PerpAxes[j][2] + PerpAxes[k][2]);
                    double cPerp = Math.Sqrt(cwx * cwx + cwy * cwy + cwz * cwz);
                    if (cPerp > acceptR * 0.76) continue;

                    double[] ei = ParAxes[i], ej = ParAxes[j], ek = ParAxes[k];
                    double cx = ej[1] * ek[2] - ej[2] * ek[1];
                    double cy = ej[2] * ek[0] - ej[0] * ek[2];
                    double cz = ej[0] * ek[1] - ej[1] * ek[0];
                    double vol = Math.Abs(ei[0] * cx + ei[1] * cy + ei[2] * cz);
                    if (vol < 0.15) continue;

                    bool isProlate = (vol > 0.60);
                    if (isProlate) prolateCount++; else oblateCount++;
                    float flipGlow = (float)Math.Exp(-Math.Abs(acceptR * 0.76 - cPerp) * 12.0);
                    EmitRhombohedronFaces(quads, px, py, pz, ei, ej, ek, isProlate ? 0 : 1, flipGlow);
                }
            }
            return quads;
        }

        // Mode 2 in 3D: 8D->3D E8 Elser-Sloane Gosset 240-Root Faceted Crystal Star!
        public static List<CrystalQuad3D> BuildE8Gosset3DStar(double phasonPhase, out int rootFacets)
        {
            List<CrystalQuad3D> quads = new List<CrystalQuad3D>(2400);
            double cp = Math.Cos(phasonPhase * 0.5), sp = Math.Sin(phasonPhase * 0.5);
            double[][] r3 = new double[240][];
            for (int i = 0; i < 240; i++)
            {
                double[] v = OmegaPenroseDeBruijnMath.E8Roots240[i];
                double x = 0, y = 0, z = 0, w = 0;
                for (int d = 0; d < 8; d++)
                {
                    x += v[d] * OmegaPenroseDeBruijnMath.E8_U1[d];
                    y += v[d] * OmegaPenroseDeBruijnMath.E8_U2[d];
                    z += v[d] * OmegaPenroseDeBruijnMath.E8_U3[d];
                    w += v[d] * OmegaPenroseDeBruijnMath.E8_U4[d];
                }
                r3[i] = new double[]
                {
                    (x * cp + w * sp * 0.35) * 2.35,
                    (y * cp - z * sp * 0.25) * 2.35,
                    (z * cp + y * sp * 0.25) * 2.35,
                    Math.Abs(w)
                };
            }

            // Build golden diamond facets spanning origin -> r_i -> (r_i+r_j)*0.72 -> r_j for outer E8 root pairs!
            for (int i = 0; i < 240 && quads.Count < MaxGpuQuads; i++)
            {
                double li = r3[i][0] * r3[i][0] + r3[i][1] * r3[i][1] + r3[i][2] * r3[i][2];
                if (li < 1.35) continue;
                for (int j = i + 1; j < 240 && quads.Count < MaxGpuQuads; j++)
                {
                    double dot8 = 0;
                    for (int d = 0; d < 8; d++)
                        dot8 += OmegaPenroseDeBruijnMath.E8Roots240[i][d] * OmegaPenroseDeBruijnMath.E8Roots240[j][d];
                    if (Math.Abs(dot8 - 1.0) > 0.02) continue;
                    double lj = r3[j][0] * r3[j][0] + r3[j][1] * r3[j][1] + r3[j][2] * r3[j][2];
                    if (lj < 1.35) continue;

                    double mx = (r3[i][0] + r3[j][0]) * 0.68;
                    double my = (r3[i][1] + r3[j][1]) * 0.68;
                    double mz = (r3[i][2] + r3[j][2]) * 0.68;
                    double nx = r3[i][1] * r3[j][2] - r3[i][2] * r3[j][1];
                    double ny = r3[i][2] * r3[j][0] - r3[i][0] * r3[j][2];
                    double nz = r3[i][0] * r3[j][1] - r3[i][1] * r3[j][0];
                    double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (nl < 1e-4) continue;
                    nx /= nl; ny /= nl; nz /= nl;
                    if (nx * mx + ny * my + nz * mz < 0) { nx = -nx; ny = -ny; nz = -nz; }

                    CrystalQuad3D q = new CrystalQuad3D();
                    q.X0 = 0; q.Y0 = 0; q.Z0 = 0;
                    q.X1 = (float)r3[i][0]; q.Y1 = (float)r3[i][1]; q.Z1 = (float)r3[i][2];
                    q.X2 = (float)mx;       q.Y2 = (float)my;       q.Z2 = (float)mz;
                    q.X3 = (float)r3[j][0]; q.Y3 = (float)r3[j][1]; q.Z3 = (float)r3[j][2];
                    q.Nx = (float)nx; q.Ny = (float)ny; q.Nz = (float)nz;
                    q.MaterialKind = ((i + j) % 3 == 0) ? 3 : ((i & 1) == 0 ? 0 : 1);
                    q.GlowEnergy = (float)Math.Min(1.0, r3[i][3] * 0.8);
                    quads.Add(q);
                }
            }
            rootFacets = quads.Count;
            return quads;
        }

        private static void EmitRhombohedronFaces(
            List<CrystalQuad3D> quads, double ox, double oy, double oz,
            double[] a, double[] b, double[] c, int mat, float glow)
        {
            double cx = ox + 0.5 * (a[0] + b[0] + c[0]);
            double cy = oy + 0.5 * (a[1] + b[1] + c[1]);
            double cz = oz + 0.5 * (a[2] + b[2] + c[2]);
            AddRhombFace(quads, ox, oy, oz, a, b, cx, cy, cz, mat, glow);
            AddRhombFace(quads, ox + c[0], oy + c[1], oz + c[2], a, b, cx, cy, cz, mat, glow);
            AddRhombFace(quads, ox, oy, oz, b, c, cx, cy, cz, mat, glow);
            AddRhombFace(quads, ox + a[0], oy + a[1], oz + a[2], b, c, cx, cy, cz, mat, glow);
            AddRhombFace(quads, ox, oy, oz, c, a, cx, cy, cz, mat, glow);
            AddRhombFace(quads, ox + b[0], oy + b[1], oz + b[2], c, a, cx, cy, cz, mat, glow);
        }

        private static void AddRhombFace(
            List<CrystalQuad3D> quads, double x0, double y0, double z0,
            double[] u, double[] v, double cellCx, double cellCy, double cellCz, int mat, float glow)
        {
            if (quads.Count >= MaxGpuQuads) return;
            double nx = u[1] * v[2] - u[2] * v[1];
            double ny = u[2] * v[0] - u[0] * v[2];
            double nz = u[0] * v[1] - u[1] * v[0];
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-6) return;
            nx /= nl; ny /= nl; nz /= nl;

            double fcx = x0 + 0.5 * (u[0] + v[0]) - cellCx;
            double fcy = y0 + 0.5 * (u[1] + v[1]) - cellCy;
            double fcz = z0 + 0.5 * (u[2] + v[2]) - cellCz;
            bool flip = (nx * fcx + ny * fcy + nz * fcz) < 0.0;
            if (flip) { nx = -nx; ny = -ny; nz = -nz; }

            CrystalQuad3D q = new CrystalQuad3D();
            q.X0 = (float)x0; q.Y0 = (float)y0; q.Z0 = (float)z0;
            if (!flip)
            {
                q.X1 = (float)(x0 + u[0]); q.Y1 = (float)(y0 + u[1]); q.Z1 = (float)(z0 + u[2]);
                q.X2 = (float)(x0 + u[0] + v[0]); q.Y2 = (float)(y0 + u[1] + v[1]); q.Z2 = (float)(z0 + u[2] + v[2]);
                q.X3 = (float)(x0 + v[0]); q.Y3 = (float)(y0 + v[1]); q.Z3 = (float)(z0 + v[2]);
            }
            else
            {
                q.X1 = (float)(x0 + v[0]); q.Y1 = (float)(y0 + v[1]); q.Z1 = (float)(z0 + v[2]);
                q.X2 = (float)(x0 + u[0] + v[0]); q.Y2 = (float)(y0 + u[1] + v[1]); q.Z2 = (float)(z0 + u[2] + v[2]);
                q.X3 = (float)(x0 + u[0]); q.Y3 = (float)(y0 + u[1]); q.Z3 = (float)(z0 + u[2]);
            }
            q.Nx = (float)nx; q.Ny = (float)ny; q.Nz = (float)nz;
            q.MaterialKind = mat; q.GlowEnergy = glow;
            quads.Add(q);
        }

        // Watertight Stepped 3D Citadel (with floor cap + all exterior walls for valid 3D printing!)
        public static List<CrystalQuad3D> BuildSteppedCitadelFrom2D(List<PenroseRhombTile> tiles, float panX, float panY)
        {
            List<CrystalQuad3D> quads = new List<CrystalQuad3D>(Math.Min(MaxGpuQuads, tiles.Count * 4));
            float scaleXY = 0.34f;
            for (int i = 0; i < tiles.Count && quads.Count + 6 <= MaxGpuQuads; i++)
            {
                PenroseRhombTile t = tiles[i];
                if (t.EdgeMask == 1) continue; // Skip pure line chords
                float dx = (t.CenterX - panX) * scaleXY, dy = (t.CenterY - panY) * scaleXY;
                if (dx * dx + dy * dy > 11.5f) continue;

                float x0 = (t.X0 - panX) * scaleXY, z0 = (t.Y0 - panY) * scaleXY;
                float x1 = (t.X1 - panX) * scaleXY, z1 = (t.Y1 - panY) * scaleXY;
                float x2 = (t.X2 - panX) * scaleXY, z2 = (t.Y2 - panY) * scaleXY;
                float x3 = (t.X3 - panX) * scaleXY, z3 = (t.Y3 - panY) * scaleXY;
                float h = (t.IsCorridorPath == 1) ? (t.ZTierHeight * 0.55f + 0.22f) : (t.ZTierHeight * 0.72f + 0.16f);
                int mat = (t.IsCorridorPath == 1) ? 3 : (t.IsThick == 1 ? 0 : 2);

                // Top roof terrace
                CrystalQuad3D top = new CrystalQuad3D();
                top.X0 = x0; top.Y0 = h; top.Z0 = z0;
                top.X1 = x1; top.Y1 = h; top.Z1 = z1;
                top.X2 = x2; top.Y2 = h; top.Z2 = z2;
                top.X3 = x3; top.Y3 = h; top.Z3 = z3;
                top.Nx = 0.0f; top.Ny = 1.0f; top.Nz = 0.0f;
                top.MaterialKind = mat; top.GlowEnergy = t.PhasonFlipEnergy;
                quads.Add(top);

                // Bottom base plate (for 3D-printer .STL watertight manifold!)
                CrystalQuad3D bot = new CrystalQuad3D();
                bot.X0 = x3; bot.Y0 = 0.0f; bot.Z0 = z3;
                bot.X1 = x2; bot.Y1 = 0.0f; bot.Z1 = z2;
                bot.X2 = x1; bot.Y2 = 0.0f; bot.Z2 = z1;
                bot.X3 = x0; bot.Y3 = 0.0f; bot.Z3 = z0;
                bot.Nx = 0.0f; bot.Ny = -1.0f; bot.Nz = 0.0f;
                bot.MaterialKind = 2; bot.GlowEnergy = 0.0f;
                quads.Add(bot);

                if ((t.EdgeMask & 1) != 0) AddWall(quads, x0, z0, x1, z1, h, mat, t.PhasonFlipEnergy * 0.6f);
                if ((t.EdgeMask & 2) != 0) AddWall(quads, x1, z1, x2, z2, h, mat, t.PhasonFlipEnergy * 0.6f);
                if ((t.EdgeMask & 4) != 0) AddWall(quads, x2, z2, x3, z3, h, mat, t.PhasonFlipEnergy * 0.6f);
                if ((t.EdgeMask & 8) != 0) AddWall(quads, x3, z3, x0, z0, h, mat, t.PhasonFlipEnergy * 0.6f);
            }
            return quads;
        }

        private static void AddWall(List<CrystalQuad3D> quads, float ax, float az, float bx, float bz, float h, int mat, float glow)
        {
            if (quads.Count >= MaxGpuQuads) return;
            float ux = bx - ax, uz = bz - az;
            float len = (float)Math.Sqrt(ux * ux + uz * uz);
            if (len < 1e-4f) return;
            CrystalQuad3D w = new CrystalQuad3D();
            w.X0 = ax; w.Y0 = 0.0f; w.Z0 = az;
            w.X1 = bx; w.Y1 = 0.0f; w.Z1 = bz;
            w.X2 = bx; w.Y2 = h;    w.Z2 = bz;
            w.X3 = ax; w.Y3 = h;    w.Z3 = az;
            w.Nx = -uz / len; w.Ny = 0.0f; w.Nz = ux / len;
            w.MaterialKind = mat; w.GlowEnergy = glow;
            quads.Add(w);
        }

        // Pack 12 floats per quad: (SX0,SY0, SX1,SY1, SX2,SY2, SX3,SY3, R, G, B, PackedDepthAndBoost)
        public static int ProjectAndSortQuads(
            List<CrystalQuad3D> src, float yawRad, float pitchRad, float camDist,
            float rectX, float rectY, float rectSide, bool solidZBufferMode,
            float[] gpuPack12PerQuad, int maxQuads)
        {
            if (src == null || src.Count == 0) return 0;
            float cy = (float)Math.Cos(yawRad), sy = (float)Math.Sin(yawRad);
            float cp = (float)Math.Cos(pitchRad), sp = (float)Math.Sin(pitchRad);
            float dist = Math.Max(2.8f, camDist);
            float fov = rectSide * 1.18f;
            float cx = rectX + rectSide * 0.5f;
            float cyPx = rectY + rectSide * 0.52f;

            List<ProjectedQuad2D> proj = new List<ProjectedQuad2D>(Math.Min(maxQuads, src.Count));
            for (int i = 0; i < src.Count; i++)
            {
                CrystalQuad3D q = src[i];
                float rnx = q.Nx * cy + q.Nz * sy;
                float rnz0 = -q.Nx * sy + q.Nz * cy;
                float rny = q.Ny * cp - rnz0 * sp;
                float rnz = q.Ny * sp + rnz0 * cp;
                if (solidZBufferMode && rnz > 0.08f) continue;
                if (!solidZBufferMode && rnz > 0.35f) continue;

                float sx0, sy0, z0, sx1, sy1, z1, sx2, sy2, z2, sx3, sy3, z3;
                if (!ProjPt(q.X0, q.Y0, q.Z0, cy, sy, cp, sp, dist, fov, cx, cyPx, out sx0, out sy0, out z0)) continue;
                if (!ProjPt(q.X1, q.Y1, q.Z1, cy, sy, cp, sp, dist, fov, cx, cyPx, out sx1, out sy1, out z1)) continue;
                if (!ProjPt(q.X2, q.Y2, q.Z2, cy, sy, cp, sp, dist, fov, cx, cyPx, out sx2, out sy2, out z2)) continue;
                if (!ProjPt(q.X3, q.Y3, q.Z3, cy, sy, cp, sp, dist, fov, cx, cyPx, out sx3, out sy3, out z3)) continue;

                // Blinn-Phong Specular + Rim Crystal Lighting!
                float ndotl = Math.Max(0.14f, -0.42f * rnx + 0.68f * rny - 0.58f * rnz);
                float spec = (float)Math.Pow(Math.Max(0.0f, -0.22f * rnx + 0.45f * rny - 0.86f * rnz), 18.0) * 0.55f;
                float fresnel = (float)Math.Pow(1.0 - Math.Min(1.0, Math.Abs(rnz)), 2.2) * 0.25f;

                float br, bg, bb;
                if (q.MaterialKind == 0)      { br = 0.12f; bg = 0.68f; bb = 0.96f; }
                else if (q.MaterialKind == 1) { br = 0.92f; bg = 0.30f; bb = 0.78f; }
                else if (q.MaterialKind == 3) { br = 0.22f; bg = 0.98f; bb = 0.56f; }
                else                          { br = 0.34f; bg = 0.44f; bb = 0.86f; }

                br += q.GlowEnergy * 0.45f;
                bg += q.GlowEnergy * 0.35f;

                ProjectedQuad2D pq = new ProjectedQuad2D();
                pq.SX0 = sx0; pq.SY0 = sy0; pq.SX1 = sx1; pq.SY1 = sy1;
                pq.SX2 = sx2; pq.SY2 = sy2; pq.SX3 = sx3; pq.SY3 = sy3;
                pq.DepthZ = 0.25f * (z0 + z1 + z2 + z3);
                pq.R = Math.Min(0.99f, br * ndotl + spec + fresnel);
                pq.G = Math.Min(0.99f, bg * ndotl + spec + fresnel);
                pq.B = Math.Min(0.99f, bb * ndotl + spec + fresnel);
                pq.EdgeBoost = pq.DepthZ; // Pass exact view-space DepthZ to GPU Z-Buffer!
                proj.Add(pq);
                if (proj.Count >= maxQuads) break;
            }

            proj.Sort(delegate(ProjectedQuad2D a, ProjectedQuad2D b) { return b.DepthZ.CompareTo(a.DepthZ); });

            int count = Math.Min(maxQuads, proj.Count);
            for (int i = 0; i < count; i++)
            {
                ProjectedQuad2D p = proj[i];
                int o = i * 12;
                gpuPack12PerQuad[o + 0] = p.SX0; gpuPack12PerQuad[o + 1] = p.SY0;
                gpuPack12PerQuad[o + 2] = p.SX1; gpuPack12PerQuad[o + 3] = p.SY1;
                gpuPack12PerQuad[o + 4] = p.SX2; gpuPack12PerQuad[o + 5] = p.SY2;
                gpuPack12PerQuad[o + 6] = p.SX3; gpuPack12PerQuad[o + 7] = p.SY3;
                gpuPack12PerQuad[o + 8] = p.R;   gpuPack12PerQuad[o + 9] = p.G;
                gpuPack12PerQuad[o + 10] = p.B;  gpuPack12PerQuad[o + 11] = p.EdgeBoost;
            }
            return count;
        }

        private static bool ProjPt(
            float x, float y, float z, float cy, float sy, float cp, float sp,
            float dist, float fov, float cx, float cyPx, out float sx, out float syPx, out float vz)
        {
            float rx = x * cy + z * sy;
            float rz0 = -x * sy + z * cy;
            float ry = y * cp - rz0 * sp;
            vz = y * sp + rz0 * cp + dist;
            if (vz < 0.45f) { sx = 0; syPx = 0; return false; }
            float inv = fov / vz;
            sx = cx + rx * inv;
            syPx = cyPx - ry * inv;
            return true;
        }
    }
}
