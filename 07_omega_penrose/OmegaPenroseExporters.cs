using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OmegaPenroseStudio
{
    public static class OmegaPenroseExporters
    {
        // 1. Export IEEE-754 Binary .STL 3D-Printer Mesh (2 Triangles per Non-Degenerate Quad, Millimeter Scale!)
        public static string ExportBinaryStl3D(string exportsDir, List<CrystalQuad3D> quads, string modelTag)
        {
            Directory.CreateDirectory(exportsDir);
            string path = Path.Combine(exportsDir, "omega_penrose_print3d_" + modelTag + "_" + DateTime.Now.ToString("HHmmss") + ".stl");
            const float mmScale = 18.0f; // ~100 mm desktop sculpture scale for PrusaSlicer / Bambu / Cura!

            List<float[]> tris = new List<float[]>(quads.Count * 2);
            for (int i = 0; i < quads.Count; i++)
            {
                CrystalQuad3D q = quads[i];
                AddStlTri(tris, q.Nx, q.Ny, q.Nz, q.X0, q.Y0, q.Z0, q.X1, q.Y1, q.Z1, q.X2, q.Y2, q.Z2, mmScale);
                float d23 = (q.X3 - q.X2) * (q.X3 - q.X2) + (q.Y3 - q.Y2) * (q.Y3 - q.Y2) + (q.Z3 - q.Z2) * (q.Z3 - q.Z2);
                if (d23 > 1e-8f)
                    AddStlTri(tris, q.Nx, q.Ny, q.Nz, q.X0, q.Y0, q.Z0, q.X2, q.Y2, q.Z2, q.X3, q.Y3, q.Z3, mmScale);
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                byte[] hdr = new byte[80];
                byte[] title = Encoding.ASCII.GetBytes("OmegaPenrose v1.0.2 GM 3D Quasicrystal Mesh (" + modelTag + ")");
                Array.Copy(title, 0, hdr, 0, Math.Min(79, title.Length));
                bw.Write(hdr);
                bw.Write((uint)tris.Count);
                for (int i = 0; i < tris.Count; i++)
                {
                    float[] t = tris[i];
                    for (int k = 0; k < 12; k++) bw.Write(t[k]);
                    bw.Write((ushort)0);
                }
            }
            return path;
        }

        private static void AddStlTri(
            List<float[]> dst, float nx, float ny, float nz,
            float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, float s)
        {
            // Map Y-up engine coords -> Z-up 3D printer bed coords (X, -Z, Y)
            dst.Add(new float[]
            {
                nx, -nz, ny,
                ax * s, -az * s, ay * s,
                bx * s, -bz * s, by * s,
                cx * s, -cz * s, cy * s
            });
        }

        // 2. Export Wavefront 3D .OBJ + .MTL
        public static string ExportWavefrontObj3D(string exportsDir, List<CrystalQuad3D> quads, string modelTag)
        {
            Directory.CreateDirectory(exportsDir);
            string stamp = DateTime.Now.ToString("HHmmss");
            string baseName = "omega_penrose_3d_" + modelTag + "_" + stamp;
            string objPath = Path.Combine(exportsDir, baseName + ".obj");
            string mtlName = baseName + ".mtl";
            string mtlPath = Path.Combine(exportsDir, mtlName);
            UTF8Encoding utf8NoBom = new UTF8Encoding(false);

            StringBuilder mtl = new StringBuilder(1024);
            mtl.AppendLine("# OmegaPenrose v1.0.2 GM Material Library");
            mtl.AppendLine("newmtl Prolate_Sapphire\r\nKd 0.14 0.76 0.98\r\nKs 0.85 0.92 1.00\r\nNs 96.0\r\nd 1.00\r\n");
            mtl.AppendLine("newmtl Oblate_Amethyst\r\nKd 0.94 0.36 0.82\r\nKs 0.95 0.78 0.98\r\nNs 96.0\r\nd 1.00\r\n");
            mtl.AppendLine("newmtl Terrace_Indigo\r\nKd 0.36 0.48 0.88\r\nKs 0.60 0.68 0.90\r\nNs 64.0\r\nd 1.00\r\n");
            mtl.AppendLine("newmtl Sanctum_Waveguide_Gold\r\nKd 0.25 1.00 0.58\r\nKe 0.15 0.65 0.35\r\nKs 1.00 0.95 0.60\r\nNs 128.0\r\nd 1.00\r\n");
            File.WriteAllText(mtlPath, mtl.ToString(), utf8NoBom);

            StringBuilder obj = new StringBuilder(Math.Max(4096, quads.Count * 180));
            obj.AppendLine("# Exported by OmegaPenrose v1.0.2 GM (07_omega_penrose)");
            obj.AppendLine("mtllib " + mtlName);
            obj.AppendLine("o OmegaPenrose_" + modelTag);

            for (int i = 0; i < quads.Count; i++)
            {
                CrystalQuad3D q = quads[i];
                obj.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.00000} {1:0.00000} {2:0.00000}\r\n", q.X0, q.Y0, q.Z0);
                obj.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.00000} {1:0.00000} {2:0.00000}\r\n", q.X1, q.Y1, q.Z1);
                obj.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.00000} {1:0.00000} {2:0.00000}\r\n", q.X2, q.Y2, q.Z2);
                obj.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.00000} {1:0.00000} {2:0.00000}\r\n", q.X3, q.Y3, q.Z3);
                obj.AppendFormat(CultureInfo.InvariantCulture, "vn {0:0.00000} {1:0.00000} {2:0.00000}\r\n", q.Nx, q.Ny, q.Nz);
            }

            int lastMat = -1;
            for (int i = 0; i < quads.Count; i++)
            {
                int mat = quads[i].MaterialKind;
                if (mat != lastMat)
                {
                    lastMat = mat;
                    string mName = (mat == 0) ? "Prolate_Sapphire" : (mat == 1) ? "Oblate_Amethyst" : (mat == 3) ? "Sanctum_Waveguide_Gold" : "Terrace_Indigo";
                    obj.AppendLine("usemtl " + mName);
                }
                int vBase = i * 4 + 1, nIdx = i + 1;
                obj.AppendFormat(CultureInfo.InvariantCulture, "f {0}//{4} {1}//{4} {2}//{4} {3}//{4}\r\n", vBase, vBase + 1, vBase + 2, vBase + 3, nIdx);
            }
            File.WriteAllText(objPath, obj.ToString(), utf8NoBom);
            return objPath;
        }

        // 3. Export Multi-Tier Stepped Creation Kit Citadel Pack (Skyrim SE + Fallout 4 + Rust L0)
        public static string ExportCreationKitCitadelPack(
            string exportsDir, List<PenroseRhombTile> tiles, float panX, float panY,
            float cellSpacingUnits, float zStepUnits, float maxRadiusUnits)
        {
            string packDir = Path.Combine(exportsDir, "CreationKit_SteppedCitadel_" + DateTime.Now.ToString("HHmmss"));
            Directory.CreateDirectory(packDir);

            List<PenroseRhombTile> sel = new List<PenroseRhombTile>();
            for (int i = 0; i < tiles.Count && sel.Count < 320; i++)
            {
                if (tiles[i].EdgeMask == 1) continue;
                float rx = (tiles[i].CenterX - panX) * cellSpacingUnits;
                float ry = (tiles[i].CenterY - panY) * cellSpacingUnits;
                if (Math.Sqrt(rx * rx + ry * ry) <= maxRadiusUnits) sel.Add(tiles[i]);
            }

            UTF8Encoding utf8NoBom = new UTF8Encoding(false);
            StringBuilder csv = new StringBuilder(16384);
            csv.AppendLine("index,tile_class,sheet_k,is_corridor,x_units,y_units,z_units,yaw_deg,phason_flip_energy");
            for (int i = 0; i < sel.Count; i++)
            {
                PenroseRhombTile t = sel[i];
                float xu = (t.CenterX - panX) * cellSpacingUnits;
                float yu = (t.CenterY - panY) * cellSpacingUnits;
                float zu = t.ZTierHeight * zStepUnits;
                float yawDeg = (float)(t.OrientationRad * 180.0 / Math.PI);
                csv.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4:0.00},{5:0.00},{6:0.00},{7:0.00},{8:0.000}\r\n",
                    i, t.IsThick == 1 ? "PrimaryTile" : "DualChiralTile", t.IndexSumK, t.IsCorridorPath, xu, yu, zu, yawDeg, t.PhasonFlipEnergy);
            }
            File.WriteAllText(Path.Combine(packDir, "penrose_citadel_transforms_3d.csv"), csv.ToString(), utf8NoBom);

            int pscCount = Math.Min(128, sel.Count);
            StringBuilder psc = new StringBuilder(20480);
            psc.AppendLine("Scriptname SovereignPenroseCitadelSpawner extends ObjectReference");
            psc.AppendLine("; Generated by OmegaPenrose v1.0.2 Final GM — Zero-BOM Sovereign Creation Kit Citadel");
            psc.AppendLine("Form Property ThickTerracePillar Auto");
            psc.AppendLine("Form Property ThinSpireCrystal Auto");
            psc.AppendLine("Form Property SanctumCorridorBeacon Auto");
            psc.AppendLine("ObjectReference[] _spawnedRefs");
            psc.AppendLine("Int _braidStateWord = 0");
            psc.AppendLine("");
            psc.AppendLine("Event OnActivate(ObjectReference akActionRef)");
            psc.AppendLine("    If _braidStateWord == 0");
            psc.AppendLine("        _braidStateWord = 1");
            psc.AppendLine("        SpawnSteppedPenroseCitadel()");
            psc.AppendLine("    Else");
            psc.AppendLine("        RollbackCitadel()");
            psc.AppendLine("        _braidStateWord = 0");
            psc.AppendLine("    EndIf");
            psc.AppendLine("EndEvent");
            psc.AppendLine("");
            psc.AppendLine("Function SpawnSteppedPenroseCitadel()");
            psc.AppendLine("    If !Self.Is3DLoaded()");
            psc.AppendLine("        Return");
            psc.AppendLine("    EndIf");
            psc.AppendLine("    _spawnedRefs = new ObjectReference[" + pscCount + "]");
            psc.AppendLine("    Float bx = Self.GetPositionX()");
            psc.AppendLine("    Float by = Self.GetPositionY()");
            psc.AppendLine("    Float bz = Self.GetPositionZ()");
            for (int i = 0; i < pscCount; i++)
            {
                PenroseRhombTile t = sel[i];
                float xu = (t.CenterX - panX) * cellSpacingUnits;
                float yu = (t.CenterY - panY) * cellSpacingUnits;
                float zu = t.ZTierHeight * zStepUnits;
                float yawDeg = (float)(t.OrientationRad * 180.0 / Math.PI);
                string formSym = (t.IsCorridorPath == 1) ? "SanctumCorridorBeacon" : ((t.IsThick == 1) ? "ThickTerracePillar" : "ThinSpireCrystal");
                psc.AppendFormat(CultureInfo.InvariantCulture, "    SpawnNode({0}, {1}, bx + ({2:0.00}), by + ({3:0.00}), bz + ({4:0.00}), {5:0.00})\r\n", i, formSym, xu, yu, zu, yawDeg);
            }
            psc.AppendLine("EndFunction");
            psc.AppendLine("");
            psc.AppendLine("Function SpawnNode(Int idx, Form akBase, Float tx, Float ty, Float tz, Float yawDeg)");
            psc.AppendLine("    If akBase");
            psc.AppendLine("        ObjectReference r = Self.PlaceAtMe(akBase, 1, false, true)");
            psc.AppendLine("        If r");
            psc.AppendLine("            r.SetPosition(tx, ty, tz)");
            psc.AppendLine("            r.SetAngle(0.0, 0.0, yawDeg)");
            psc.AppendLine("            r.EnableNoWait()");
            psc.AppendLine("            _spawnedRefs[idx] = r");
            psc.AppendLine("        EndIf");
            psc.AppendLine("    EndIf");
            psc.AppendLine("EndFunction");
            psc.AppendLine("");
            psc.AppendLine("Function RollbackCitadel()");
            psc.AppendLine("    If !_spawnedRefs");
            psc.AppendLine("        Return");
            psc.AppendLine("    EndIf");
            psc.AppendLine("    Int i = 0");
            psc.AppendLine("    While i < _spawnedRefs.Length");
            psc.AppendLine("        ObjectReference r = _spawnedRefs[i]");
            psc.AppendLine("        If r");
            psc.AppendLine("            r.DisableNoWait()");
            psc.AppendLine("            r.Delete()");
            psc.AppendLine("        EndIf");
            psc.AppendLine("        i += 1");
            psc.AppendLine("    EndWhile");
            psc.AppendLine("EndFunction");
            File.WriteAllText(Path.Combine(packDir, "SovereignPenroseCitadelSpawner.psc"), psc.ToString(), utf8NoBom);

            StringBuilder rs = new StringBuilder(8192);
            rs.AppendLine("//! Generated by OmegaPenrose v1.0.2 Final GM — Rust L0 SKSE64 / F4SE Citadel Spatial Table");
            rs.AppendLine("#![allow(dead_code)]");
            rs.AppendLine("#[repr(C)] #[derive(Copy, Clone)]");
            rs.AppendLine("pub struct CitadelNodeI32 { pub x_mm: i32, pub y_mm: i32, pub z_mm: i32, pub yaw_mdeg: i32, pub sheet_k: u8, pub is_corridor: u8 }");
            rs.AppendLine("pub static CITADEL_NODES: [CitadelNodeI32; " + pscCount + "] = [");
            for (int i = 0; i < pscCount; i++)
            {
                PenroseRhombTile t = sel[i];
                int xmm = (int)Math.Round((t.CenterX - panX) * cellSpacingUnits * 1000.0f);
                int ymm = (int)Math.Round((t.CenterY - panY) * cellSpacingUnits * 1000.0f);
                int zmm = (int)Math.Round(t.ZTierHeight * zStepUnits * 1000.0f);
                int yawMdeg = (int)Math.Round(t.OrientationRad * 180000.0 / Math.PI);
                rs.AppendFormat(CultureInfo.InvariantCulture, "    CitadelNodeI32 {{ x_mm: {0}, y_mm: {1}, z_mm: {2}, yaw_mdeg: {3}, sheet_k: {4}, is_corridor: {5} }},\r\n", xmm, ymm, zmm, yawMdeg, t.IndexSumK, t.IsCorridorPath);
            }
            rs.AppendLine("];");
            File.WriteAllText(Path.Combine(packDir, "sovereign_citadel_l0.rs"), rs.ToString(), utf8NoBom);

            StringBuilder bat = new StringBuilder(8192);
            bat.AppendLine("; Fallout 4 / Skyrim SE Console Stepped Citadel Spawner (bat citadel_console_batch)");
            for (int i = 0; i < Math.Min(64, sel.Count); i++)
            {
                PenroseRhombTile t = sel[i];
                bat.AppendFormat(CultureInfo.InvariantCulture,
                    "; Tile #{0} ({1}, K={2}) dX={3:0.0} dY={4:0.0} dZ={5:0.0} Yaw={6:0.0}\r\n",
                    i, t.IsThick == 1 ? "Primary" : "Chiral", t.IndexSumK,
                    (t.CenterX - panX) * cellSpacingUnits, (t.CenterY - panY) * cellSpacingUnits,
                    t.ZTierHeight * zStepUnits, t.OrientationRad * 180.0 / Math.PI);
            }
            File.WriteAllText(Path.Combine(packDir, "citadel_console_batch.txt"), bat.ToString(), utf8NoBom);
            return packDir;
        }

        public static string ExportOmshShader(string exportsDir, string avDir, int symMode, double[] gamma5, float zoom, bool hotLoadOmegaAv)
        {
            Directory.CreateDirectory(exportsDir);
            string fileName = "penrose_gm_quasicrystal_" + DateTime.Now.ToString("HHmmss") + ".omsh";
            string expPath = Path.Combine(exportsDir, fileName);

            int nFold = (symMode == 4) ? 4 : ((symMode == 5 || symMode >= 7) ? 6 : (symMode == 6 ? 15 : 5));
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("// Exported by OmegaPenrose v1.0.2 Final GM (07_omega_penrose)");
            sb.AppendFormat(CultureInfo.InvariantCulture, "qx = x * {0:0.00}; qy = y * {0:0.00};\r\n", Math.Max(3.0f, zoom * 0.5f));
            sb.AppendLine("ws = 0.0; ridge = 0.0;");
            for (int k = 0; k < nFold; k++)
            {
                double ang = (nFold == 5) ? (2.0 * Math.PI * k / 5.0) : (Math.PI * k / nFold);
                double gk = (k < gamma5.Length) ? gamma5[k] : 0.2;
                sb.AppendFormat(CultureInfo.InvariantCulture, "ph{0} = qx * {1:0.00000} + qy * {2:0.00000} + {3:0.0000} + t * 0.08 * bass;\r\n", k, Math.Cos(ang), Math.Sin(ang), gk);
                sb.AppendFormat("ws = ws + cos(6.283185 * ph{0});\r\n", k);
                sb.AppendFormat("ridge = ridge + exp(-abs(frac(ph{0}) - 0.5) * 24.0);\r\n", k);
            }
            sb.AppendFormat(CultureInfo.InvariantCulture, "qn = ws / {0:0.0} * 0.5 + 0.5;\r\n", (double)nFold);
            sb.AppendLine("r = clamp(qn * 0.85 + ridge * 0.25 * mid, 0.0, 1.0);");
            sb.AppendLine("g = clamp(sin(qn * 3.14159) * 0.90 + bass * 0.30, 0.0, 1.0);");
            sb.AppendLine("b = clamp((1.0 - qn) * 0.95 + ridge * 0.35 * high, 0.0, 1.0);");

            UTF8Encoding utf8NoBom = new UTF8Encoding(false);
            File.WriteAllText(expPath, sb.ToString(), utf8NoBom);
            if (hotLoadOmegaAv && !string.IsNullOrEmpty(avDir) && Directory.Exists(avDir))
            {
                string avShaders = Path.Combine(avDir, "shaders");
                Directory.CreateDirectory(avShaders);
                File.WriteAllText(Path.Combine(avShaders, fileName), sb.ToString(), utf8NoBom);
                File.WriteAllText(Path.Combine(avShaders, "00_omega_penrose_live.omsh"), sb.ToString(), utf8NoBom);
            }
            return expPath;
        }

        public static string ExportPenroseSvg(string exportsDir, List<PenroseRhombTile> tiles, float panX, float panY, float zoomUnits, bool showWorms)
        {
            Directory.CreateDirectory(exportsDir);
            string path = Path.Combine(exportsDir, "penrose_gm_blueprint_" + DateTime.Now.ToString("HHmmss") + ".svg");
            const int S = 1200;
            float scale = S / Math.Max(2.5f, zoomUnits), half = S * 0.5f;

            StringBuilder sb = new StringBuilder(Math.Max(8192, tiles.Count * 160));
            sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1200 1200\" width=\"1200\" height=\"1200\">");
            sb.AppendLine("<rect width=\"1200\" height=\"1200\" fill=\"#070b14\"/>");

            for (int i = 0; i < tiles.Count; i++)
            {
                PenroseRhombTile t = tiles[i];
                float x0 = half + (t.X0 - panX) * scale, y0 = half + (t.Y0 - panY) * scale;
                float x1 = half + (t.X1 - panX) * scale, y1 = half + (t.Y1 - panY) * scale;
                float x2 = half + (t.X2 - panX) * scale, y2 = half + (t.Y2 - panY) * scale;
                float x3 = half + (t.X3 - panX) * scale, y3 = half + (t.Y3 - panY) * scale;
                if (x0 < -120 || x0 > S + 120 || y0 < -120 || y0 > S + 120) continue;

                string fill = (t.IsCorridorPath == 1) ? "#065f46" : (t.PhasonFlipEnergy > 0.68f) ? "#78350f"
                            : (t.IndexSumK == 1) ? "#0f294a" : (t.IndexSumK == 2) ? "#0d3b3e" : (t.IndexSumK == 3) ? "#3b1747" : "#422c10";
                string stroke = (t.IsCorridorPath == 1) ? "#34d399" : (t.PhasonFlipEnergy > 0.68f) ? "#fbbf24" : (t.IsThick == 1 ? "#38bdf8" : "#c084fc");

                if (t.EdgeMask == 15)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "<polygon points=\"{0:0.0},{1:0.0} {2:0.0},{3:0.0} {4:0.0},{5:0.0} {6:0.0},{7:0.0}\" fill=\"{8}\" stroke=\"{9}\" stroke-width=\"1.0\"/>\r\n",
                        x0, y0, x1, y1, x2, y2, x3, y3, fill, stroke);
                }
                else
                {
                    if (!(t.EdgeMask == 1 && Math.Abs(x2 - x1) < 0.01f && Math.Abs(x3 - x0) < 0.01f))
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture,
                            "<polygon points=\"{0:0.0},{1:0.0} {2:0.0},{3:0.0} {4:0.0},{5:0.0}\" fill=\"{6}\" stroke=\"none\"/>\r\n",
                            x0, y0, x1, y1, x2, y2, fill);
                    }
                    if ((t.EdgeMask & 1) != 0) sb.AppendFormat(CultureInfo.InvariantCulture, "<line x1=\"{0:0.0}\" y1=\"{1:0.0}\" x2=\"{2:0.0}\" y2=\"{3:0.0}\" stroke=\"{4}\" stroke-width=\"1.2\"/>\r\n", x0, y0, x1, y1, stroke);
                    if ((t.EdgeMask & 2) != 0) sb.AppendFormat(CultureInfo.InvariantCulture, "<line x1=\"{0:0.0}\" y1=\"{1:0.0}\" x2=\"{2:0.0}\" y2=\"{3:0.0}\" stroke=\"{4}\" stroke-width=\"1.2\"/>\r\n", x1, y1, x2, y2, stroke);
                    if ((t.EdgeMask & 4) != 0) sb.AppendFormat(CultureInfo.InvariantCulture, "<line x1=\"{0:0.0}\" y1=\"{1:0.0}\" x2=\"{2:0.0}\" y2=\"{3:0.0}\" stroke=\"{4}\" stroke-width=\"1.2\"/>\r\n", x2, y2, x3, y3, stroke);
                    if ((t.EdgeMask & 8) != 0) sb.AppendFormat(CultureInfo.InvariantCulture, "<line x1=\"{0:0.0}\" y1=\"{1:0.0}\" x2=\"{2:0.0}\" y2=\"{3:0.0}\" stroke=\"{4}\" stroke-width=\"1.2\"/>\r\n", x3, y3, x0, y0, stroke);
                }
            }
            sb.AppendLine("</svg>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static string ExportStagePng(string exportsDir, Control stageControl)
        {
            Directory.CreateDirectory(exportsDir);
            string path = Path.Combine(exportsDir, "penrose_gm_stage_" + DateTime.Now.ToString("HHmmss") + ".png");
            int w = Math.Max(320, stageControl.Width), h = Math.Max(240, stageControl.Height);
            Point screenOrigin = stageControl.PointToScreen(Point.Empty);
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(screenOrigin, Point.Empty, new Size(w, h));
                }
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }
    }
}
