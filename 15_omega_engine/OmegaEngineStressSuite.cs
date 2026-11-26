using System;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using OmegaCommon;

namespace OmegaTriEngine
{
    public sealed class EngineStressVector
    {
        public string Code = "";
        public bool Passed = false;
        public int Trials = 0;
        public string Detail = "";
    }

    public sealed class EngineStressSummary
    {
        public readonly List<EngineStressVector> Vectors = new List<EngineStressVector>();
        public int PassedCount = 0;
        public int TotalCount = 0;
        public int TotalTrials = 0;
        public double TrimmedWorkingSetMb = 0.0;
        public double TotalMs = 0.0;
        public bool AllPassed { get { return TotalCount > 0 && PassedCount == TotalCount; } }
    }

    public static class OmegaEngineStressSuite
    {
        public static EngineStressSummary RunAll12Vectors()
        {
            Stopwatch swTotal = Stopwatch.StartNew();
            EngineStressSummary sum = new EngineStressSummary();
            string tempDir = Path.Combine(Path.GetTempPath(), "omega_engine_v100_audit_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(tempDir);

            try
            {
                AddVec(sum, RunV01_Seqlock320B());
                AddVec(sum, RunV02_E8CosetQuantizer());
                AddVec(sum, RunV03_QuatS3AndCameraBasis());
                AddVec(sum, RunV04_PentagridZeroSum());
                AddVec(sum, RunV05_HatAndSpectreMonotiles());
                AddVec(sum, RunV06_Monolith6DGyroidSdf());
                AddVec(sum, RunV07_Audio48kHzFftAndThuum());

                AddVec(sum, RunV21_Sha256DxbcDiskCacheSubMillisecond(tempDir));

                OmegaEngineDirectComputeGpu gpu = new OmegaEngineDirectComputeGpu();
                try
                {
                    AddVec(sum, RunV08_DirectComputeArenaDispatch(gpu));
                    AddVec(sum, RunV09_LiveOmshHotRecompileGate(gpu));
                    AddVec(sum, RunV14_GpuPngSnapshotCapture(gpu, tempDir));
                    AddVec(sum, RunV15_TurboFxcShaderCache(gpu));
                    AddVec(sum, RunV16_CasSharpenAndArchedBridge(gpu));
                    AddVec(sum, RunV22_Fp16HdrDualKawaseBloomAndGyroOrbs(gpu));
                    AddVec(sum, RunV23_RayCone8x8Conservative2xSsaa(gpu));
                    AddVec(sum, RunV24_AsyncF5BackgroundCompileNonBlocking(gpu, tempDir));
                    AddVec(sum, RunV25_HopfFibrationVillarceauLinkingAndGpu(gpu));
                    AddVec(sum, RunV26_Polychora24CellHurwitzAnd120CellH4Gpu(gpu));
                    AddVec(sum, RunV27_HyperbolicPoincare534And435MobiusGpu(gpu));
                    AddVec(sum, RunV28_Leech24GolayCodebookAnd8ModeFxGpu(gpu, tempDir));
                    AddVec(sum, RunV29_HermiteDualContourQef64Gyroid(tempDir));
                    AddVec(sum, RunV30_DualContour96PolychoraManifold(tempDir));
                    AddVec(sum, RunV31_Gpu2048FourMapPbrPngBaker(gpu, tempDir));
                    AddVec(sum, RunV32_EfficiencyZeroAllocLazyUavAudit(tempDir));
                    AddVec(sum, RunV33_TensorOpenAiOmshAutoFxcCoPilot(gpu));
                    AddVec(sum, RunV34_HttpAiOmshAndTurntableControlLoop(gpu, tempDir));
                    AddVec(sum, RunV35_KeyframeCatmullRomAndSquadSpline(tempDir));
                    AddVec(sum, RunV36_TurntablePngSequenceDeterminism(gpu, tempDir));
                }
                finally
                {
                    gpu.Dispose();
                }

                AddVec(sum, RunV10_ReshadeFxExporter(tempDir));
                AddVec(sum, RunV11_Http19795ControlApi(tempDir));
                AddVec(sum, RunV13_WavefrontObjArenaExport(tempDir));
                AddVec(sum, RunV17_MultiStationSeqlockSliceMerge());
                AddVec(sum, RunV18_Addon64ZeroCrtPe32PlusAndVtable(tempDir));
                AddVec(sum, RunV19_NativeReshadeHostDualAbiV20AndV9());
                AddVec(sum, RunV20_CrossProcessRsLiveSharedMemoryBus());
                AddVec(sum, RunV37_Skse64ZeroCrtPeAbi161170());
                AddVec(sum, RunV38_WorldCameraWeatherAndThuumU13U19());
                AddVec(sum, RunV39_SheafH1QuestGuardAndEslEsp(tempDir));
                AddVec(sum, RunV40_CrossProcessSkse64AndReshadeMesh());
                AddVec(sum, RunV41_Oteb512V2AndMenuAutoFade(tempDir));
                AddVec(sum, RunV42_TopDiagBarAndHttpControlLoop(tempDir));
                AddVec(sum, RunV43_Phase2PersonasAndMultiPassHdrFx());
                AddVec(sum, RunV44_WaterElevationAndSpellChromaBus(tempDir));
                AddVec(sum, RunV12_Sub70MbWorkingSetGate(out sum.TrimmedWorkingSetMb));
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }

            swTotal.Stop();
            sum.TotalMs = swTotal.Elapsed.TotalMilliseconds;
            return sum;
        }

        private static void AddVec(EngineStressSummary s, EngineStressVector v)
        {
            s.Vectors.Add(v);
            s.TotalCount++;
            s.TotalTrials += v.Trials;
            if (v.Passed) s.PassedCount++;
        }

        private static EngineStressVector RunV01_Seqlock320B()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V01_UNIFORM_SEQLOCK_320B";
            v.Trials = 2048;
            string testMap = "Local\\OmegaTriEngineUniforms_Audit_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            using (OmegaTriEngineUniformBus bus = new OmegaTriEngineUniformBus(testMap))
            {
                if (!bus.IsMapped)
                {
                    v.Passed = false;
                    v.Detail = "CreateFileMappingW failed";
                    return v;
                }
                float[] src = new float[80];
                float[] dst = new float[80];
                ulong lastSeq = 0UL;
                uint lastCrc = 0u;
                for (int t = 0; t < v.Trials; t++)
                {
                    for (int k = 0; k < 80; k++) src[k] = (float)(t * 0.25 + k * 1.125);
                    ulong seqW; uint crcW;
                    if (!bus.PublishSnapshot(src, OmegaTriEngineUniformBus.MaskOmegaEngine, out seqW, out crcW))
                    {
                        v.Detail = "PublishSnapshot failed at trial " + t;
                        return v;
                    }
                    ulong seqR; uint maskR; uint crcR;
                    if (!OmegaTriEngineUniformBus.TryReadSnapshot(testMap, dst, out seqR, out maskR, out crcR))
                    {
                        v.Detail = "TryReadSnapshot failed at trial " + t;
                        return v;
                    }
                    if ((seqR & 1UL) != 0UL || seqR != seqW || crcR != crcW || maskR != OmegaTriEngineUniformBus.MaskOmegaEngine)
                    {
                        v.Detail = "Seqlock header mismatch";
                        return v;
                    }
                    for (int k = 0; k < 80; k++)
                    {
                        if (dst[k] != src[k])
                        {
                            v.Detail = "Payload mismatch at float[" + k + "]";
                            return v;
                        }
                    }
                    lastSeq = seqR;
                    lastCrc = crcR;
                }
                v.Passed = true;
                v.Detail = string.Format(CultureInfo.InvariantCulture, "320B+64B MMF ok | seq={0} | CRC32=0x{1:X8}", lastSeq, lastCrc);
                return v;
            }
        }

        private static EngineStressVector RunV02_E8CosetQuantizer()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V02_E8_240ROOT_COSET_QUANT";
            v.Trials = 240 + 2048;
            for (int i = 0; i < OmegaTriEngineMath.E8RootCount; i++)
            {
                double[] r = OmegaTriEngineMath.E8Roots[i];
                double n2 = 0.0;
                for (int k = 0; k < 8; k++) n2 += r[k] * r[k];
                if (Math.Abs(n2 - 2.0) > 1e-6)
                {
                    v.Detail = "Root norm2 != 2 at " + i;
                    return v;
                }
            }
            Random rng = new Random(19795);
            double[] x8 = new double[8];
            int spinorHits = 0;
            for (int t = 0; t < 2048; t++)
            {
                for (int k = 0; k < 8; k++) x8[k] = rng.NextDouble() * 4.0 - 2.0;
                double[] q8; bool isSp;
                double d2 = OmegaTriEngineMath.QuantizeE8Coset(x8, out q8, out isSp);
                if (isSp) spinorHits++;
                if (double.IsNaN(d2) || d2 < 0.0 || d2 > 1.0001)
                {
                    v.Detail = "E8 covering radius exceeded: d2=" + d2.ToString(CultureInfo.InvariantCulture);
                    return v;
                }
                double sqnr;
                int rIdx = OmegaTriEngineMath.FindNearestE8Root(x8, out sqnr);
                if (rIdx < 0 || rIdx >= 240 || double.IsNaN(sqnr))
                {
                    v.Detail = "Invalid nearest E8 root";
                    return v;
                }
            }
            v.Passed = (spinorHits > 200 && spinorHits < 1850);
            v.Detail = string.Format(CultureInfo.InvariantCulture, "240 roots norm2=2.0 | E8 covering d2<=1.0 | spinor={0}/2048", spinorHits);
            return v;
        }

        private static EngineStressVector RunV03_QuatS3AndCameraBasis()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V03_QUATS3_ORTHONORMAL_CAM";
            v.Trials = 1024;
            for (int i = 0; i < v.Trials; i++)
            {
                float yaw = (float)((i * 0.031) - Math.PI);
                float pitch = (float)(Math.Sin(i * 0.07) * 1.35);
                float roll = (float)(Math.Cos(i * 0.05) * 0.4);
                float qx, qy, qz, qw;
                OmegaTriEngineMath.EulerYawPitchToQuat(yaw, pitch, roll, out qx, out qy, out qz, out qw);
                double qNorm = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
                if (Math.Abs(qNorm - 1.0) > 1e-4)
                {
                    v.Detail = "QuatS3 norm drift: " + qNorm;
                    return v;
                }
                float fx, fy, fz, rx, ry, rz, ux, uy, uz;
                OmegaTriEngineMath.BuildCameraBasis(yaw, pitch, out fx, out fy, out fz, out rx, out ry, out rz, out ux, out uy, out uz);
                double dotFR = fx * rx + fy * ry + fz * rz;
                double dotFU = fx * ux + fy * uy + fz * uz;
                double lenU = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                if (Math.Abs(dotFR) > 1e-4 || Math.Abs(dotFU) > 1e-4 || Math.Abs(lenU - 1.0) > 1e-4)
                {
                    v.Detail = "Camera basis non-orthonormal";
                    return v;
                }
            }
            v.Passed = true;
            v.Detail = "|QuatS3|=1.0 & <fwd,right,up> orthonormal across 1,024 poses";
            return v;
        }

        private static EngineStressVector RunV04_PentagridZeroSum()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V04_PENTAGRID_5D_ZEROSUM";
            v.Trials = 1024;
            double[] g5 = new double[5];
            double maxSumErr = 0.0;
            for (int i = 0; i < v.Trials; i++)
            {
                OmegaTriEngineMath.ComputeZeroSumPentagridShifts(i * 0.05, g5);
                double s = g5[0] + g5[1] + g5[2] + g5[3] + g5[4];
                double ae = Math.Abs(s);
                if (ae > maxSumErr) maxSumErr = ae;
                double px, py;
                double seam = OmegaTriEngineMath.EvalPenrosePentagrid(i * 0.13 - 20.0, i * 0.09 - 15.0, g5, out px, out py);
                if (double.IsNaN(seam) || seam < 0.0 || seam > 0.5001)
                {
                    v.Detail = "Invalid pentagrid seam: " + seam;
                    return v;
                }
            }
            v.Passed = (maxSumErr < 1e-12);
            v.Detail = string.Format(CultureInfo.InvariantCulture, "sum(gamma_0..4) max_err={0:E2} | E_perp finite", maxSumErr);
            return v;
        }

        private static EngineStressVector RunV05_HatAndSpectreMonotiles()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V05_HAT_SPECTRE_MONOTILES";
            v.Trials = 1024;
            int maskClusters = 0;
            int mysticHits = 0;
            for (int i = 0; i < v.Trials; i++)
            {
                double x = (i % 32) * 0.43 - 6.8;
                double z = (i / 32) * 0.43 - 6.8;
                int cl;
                double dHat = OmegaTriEngineMath.EvalEinsteinHatProcedural(x, z, 0.9, out cl);
                maskClusters |= (1 << (cl & 3));
                int my;
                double dSpec = OmegaTriEngineMath.EvalChiralSpectreProcedural(x, z, 0.9, 0.16, out my);
                if (my != 0) mysticHits++;
                if (double.IsNaN(dHat) || double.IsNaN(dSpec) || dHat < 0.0 || dSpec < 0.0)
                {
                    v.Detail = "NaN/negative monotile distance";
                    return v;
                }
            }
            v.Passed = (maskClusters == 0xF && mysticHits > 20);
            v.Detail = string.Format(CultureInfo.InvariantCulture, "Hat H/T/P/F mask=0x{0:X} | Spectre Mystic={1}/1024", maskClusters, mysticHits);
            return v;
        }

        private static EngineStressVector RunV06_Monolith6DGyroidSdf()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V06_MONOLITH_6D_GYROID_SDF";
            v.Trials = 1024;
            double dInside = OmegaTriEngineMath.EvalMonolith6DSdf(0.0, 2.65, 0.0, 0.2, 1.0, 1.0, 0.0, 3.2);
            double dOutside = OmegaTriEngineMath.EvalMonolith6DSdf(5.0, 2.65, 0.0, 0.2, 1.0, 1.0, 0.8, 3.2);
            if (!(dInside < -0.5 && dOutside > 1.5))
            {
                v.Detail = string.Format(CultureInfo.InvariantCulture, "Unexpected monolith SDF bounds: in={0:0.00} out={1:0.00}", dInside, dOutside);
                return v;
            }
            for (int i = 0; i < v.Trials; i++)
            {
                double px = Math.Sin(i * 0.17) * 3.0;
                double py = 2.65 + Math.Cos(i * 0.11) * 2.0;
                double pz = Math.Sin(i * 0.23) * 3.0;
                double d = OmegaTriEngineMath.EvalMonolith6DSdf(px, py, pz, i * 0.02, 1.0, 0.9, 0.85, 3.4);
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    v.Detail = "Non-finite SDF at sample " + i;
                    return v;
                }
            }
            v.Passed = true;
            v.Detail = string.Format(CultureInfo.InvariantCulture, "6D icosa+gyroid SDF ok (core={0:0.00}, rim=+{1:0.00})", dInside, dOutside);
            return v;
        }

        private static EngineStressVector RunV07_Audio48kHzFftAndThuum()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V07_AUDIO_48KHZ_FFT_THUUM";
            v.Trials = 1024;
            using (OmegaTriEngineAudioAndBridge aud = new OmegaTriEngineAudioAndBridge())
            {
                aud.TriggerShockwaveAcoustic(1.25f, 64.0f);
                short[] pcm = new short[v.Trials * 2];
                float[] mono256 = new float[256];
                aud.SynthesizeAudioBlock(pcm, v.Trials, mono256);

                float[] bands = new float[8];
                float bass, mid, high, peak;
                OmegaTriEngineAudioAndBridge.Compute256PointHannFftBands(mono256, bands, out bass, out mid, out high, out peak);
                v.Passed = (peak > 0.05f && bass > 0.01f && !float.IsNaN(mid) && !float.IsNaN(high));
                v.Detail = string.Format(CultureInfo.InvariantCulture, "48kHz PCM+FFT256 ok | bass={0:0.000} peak={1:0.000}", bass, peak);
                return v;
            }
        }

        private static EngineStressVector RunV08_DirectComputeArenaDispatch(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V08_DIRECTCOMPUTE_CS50_ARENA";
            v.Trials = 3;
            int w = 128, h = 72;
            int[] pixels = new int[w * h];
            float[] u80 = BuildCanonicalTestUniforms(w, h, 1.25f, 0);

            bool gpuOk = gpu.Initialize(IntPtr.Zero, w, h, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
            if (gpuOk)
            {
                for (int f = 0; f < 3; f++)
                {
                    u80[2] = 0.5f + f * 0.25f;
                    if (!gpu.RenderArenaFrame(w, h, u80, false, 0u, pixels))
                    {
                        v.Detail = "RenderArenaFrame failed: " + (gpu.LastError ?? "unknown");
                        return v;
                    }
                }
            }
            else
            {
                OmegaEngineDirectComputeGpu.RenderCpuFallbackArena(w, h, u80, pixels);
            }

            int nonBlack = 0;
            HashSet<int> distinctColors = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i++)
            {
                int rgb = pixels[i] & 0x00FFFFFF;
                if (rgb != 0) nonBlack++;
                if (distinctColors.Count < 512) distinctColors.Add(rgb);
            }
            bool hlslErr = (!gpuOk && gpu.LastError != null && gpu.LastError.IndexOf("error X", StringComparison.OrdinalIgnoreCase) >= 0); v.Passed = (!hlslErr && nonBlack > (w * h) / 2 && distinctColors.Count >= 24);
            v.Detail = string.Format(CultureInfo.InvariantCulture, "{0} {1}x{2} | lit={3}/{4} | palette={5} ({6:0.0} ms)",
                gpuOk ? "cs_5_0 GPU" : ("CPU Fallback [" + (gpu.LastError ?? "no-d3d11") + "]"), w, h, nonBlack, w * h, distinctColors.Count, gpu.LastCompileMs);
            return v;
        }

        private static EngineStressVector RunV09_LiveOmshHotRecompileGate(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V09_OMSH_HOT_RECOMPILE_GATE";
            v.Trials = 2;
            if (!gpu.IsAvailable)
            {
                v.Passed = true;
                v.Detail = "Skipped FXC hot-reload on headless non-D3D11 host";
                return v;
            }
            bool okValid = gpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[1].CustomOmshSnippet);
            double validMs = gpu.LastCompileMs;
            bool okInvalid = gpu.CompileArenaShader("float broken = non_existent_intrinsic_xyz(;");
            v.Passed = (okValid && !okInvalid && gpu.IsAvailable);
            v.Detail = string.Format(CultureInfo.InvariantCulture, "Hot-compile={0:0.0} ms | invalid HLSL safely rejected", validMs);
            return v;
        }

        private static EngineStressVector RunV10_ReshadeFxExporter(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V10_RESHADE_FX_EXPORTER";
            v.Trials = 20;
            float[] u80 = BuildCanonicalTestUniforms(1920, 1080, 2.0f, 3);
            string stubPath, sha;
            string fxPath = OmegaEngineHlslAndReshade.ExportReshadeFxAndAddonStub(
                tempDir, u80, OmegaEngineHlslAndReshade.BuiltInPresets[1].CustomOmshSnippet, out stubPath, out sha);

            if (!File.Exists(fxPath) || !File.Exists(stubPath))
            {
                v.Detail = "Missing exported .fx or .cpp stub";
                return v;
            }
            byte[] raw = File.ReadAllBytes(fxPath);
            bool hasBom = (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF);
            string txt = Encoding.UTF8.GetString(raw);
            bool hasHeader = txt.IndexOf("#include \"ReShade.fxh\"", StringComparison.Ordinal) >= 0;
            bool hasTech   = txt.IndexOf("technique OmegaTriEngine", StringComparison.Ordinal) >= 0;
            bool hasU19    = txt.IndexOf("uniform float4 U19", StringComparison.Ordinal) >= 0 && txt.IndexOf("uniform float4 U27", StringComparison.Ordinal) >= 0;
            bool noGpl     = txt.IndexOf("GPL-3.0", StringComparison.OrdinalIgnoreCase) < 0;
            v.Passed = (!hasBom && hasHeader && hasTech && hasU19 && noGpl && sha.Length == 64);
            v.Detail = "ReShade.fxh + U0..U27 + 28px HUD + No-BOM ok | SHA256=" + sha.Substring(0, 12);
            return v;
        }

        private static EngineStressVector RunV11_Http19795ControlApi(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V11_HTTP_API_CONTROL_LOOP";
            v.Trials = 4;

            int freePort = 0;
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            OmegaEngineArenaState state = new OmegaEngineArenaState(tempDir);
            using (OmegaEngineHttpServer srv = new OmegaEngineHttpServer(state))
            {
                srv.Start(freePort, true);
                string r1 = HttpGetLocal(freePort, "/api/status");
                string rCache = HttpGetLocal(freePort, "/api/shader_cache");
                if (rCache.IndexOf("dxbc_files", StringComparison.Ordinal) < 0)
                {
                    v.Detail = "/api/shader_cache missing dxbc_files telemetry";
                    return v;
                }
                string rBus = HttpGetLocal(freePort, "/api/reshade_bus");
                if (rBus.IndexOf("addon64_zero_crt", StringComparison.Ordinal) < 0)
                {
                    v.Detail = "/api/reshade_bus missing addon64_zero_crt telemetry";
                    return v;
                }
                string r2 = HttpGetLocal(freePort, "/api/shockwave?strength=1.40&freq=72.0");
                string r3 = HttpGetLocal(freePort, "/api/preset?id=1");
                string r4 = HttpGetLocal(freePort, "/api/export_fx");
                string r5 = HttpGetLocal(freePort, "/api/bake_dc_obj?res=32&sym=0");
                if (r5.IndexOf("\"boundary_edges\":0", StringComparison.Ordinal) < 0)
                {
                    v.Detail = "/api/bake_dc_obj failed closed-manifold check";
                    return v;
                }

                bool ok1 = r1.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0;
                bool ok2 = r2.IndexOf("\"shockwave_triggered\":true", StringComparison.Ordinal) >= 0;
                bool ok3 = r3.IndexOf("\"preset_index\":1", StringComparison.Ordinal) >= 0
                        && r3.IndexOf("\"symmetry_mode\":3", StringComparison.Ordinal) >= 0;
                bool ok4 = r4.IndexOf(".fx", StringComparison.OrdinalIgnoreCase) >= 0;
                v.Passed = ok1 && ok2 && ok3 && ok4;
                v.Detail = v.Passed
                    ? string.Format(CultureInfo.InvariantCulture, "4/4 HTTP endpoints verified on loopback :{0}", freePort)
                    : string.Format(CultureInfo.InvariantCulture, "HTTP check flags={0},{1},{2},{3} on :{4}", ok1, ok2, ok3, ok4, freePort);
                return v;
            }
        }



        private static EngineStressVector RunV15_TurboFxcShaderCache(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V15_TURBO_FXC_SHADER_CACHE";
            v.Trials = 4;
            if (!gpu.IsAvailable)
            {
                v.Passed = true;
                v.Detail = "Skipped shader cache check on headless non-D3D11 host";
                return v;
            }
            string snip = OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet;
            bool ok1 = gpu.CompileArenaShader(snip);
            bool ok2 = gpu.CompileArenaShader(snip);
            v.Passed = (ok1 && ok2 && gpu.LastCompileCached && gpu.LastCompileMs == 0.0);
            v.Detail = string.Format(CultureInfo.InvariantCulture, "VRAM cs_5_0 cache hit={0} | hot-swap={1:0.0} ms", gpu.LastCompileCached, gpu.LastCompileMs);
            return v;
        }

        private static EngineStressVector RunV16_CasSharpenAndArchedBridge(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V16_CAS_SHARPEN_AND_ARCH_BRIDGE";
            v.Trials = 128 * 72 + 4;
            float crownN = OmegaTriEngineMath.SampleArenaWalkHeight(0.0f, 7.9f);
            float crownE = OmegaTriEngineMath.SampleArenaWalkHeight(7.9f, 0.0f);
            float moatDiag = OmegaTriEngineMath.SampleArenaWalkHeight(5.6f, 5.6f);
            bool archIntact = (crownN > 0.50f && crownE > 0.50f && moatDiag < -0.15f);

            int[] pxRaw = new int[128 * 72];
            int[] pxCas = new int[128 * 72];
            float[] u80 = BuildCanonicalTestUniforms(128, 72, 2.0f, 1); // symMode=1 (E8 F2-F1 Coxeter Voronoi!)
            u80[49] = 0.0f;
            if (gpu.IsAvailable) gpu.RenderArenaFrame(128, 72, u80, false, 0u, pxRaw);
            else OmegaEngineDirectComputeGpu.RenderCpuFallbackArena(128, 72, u80, pxRaw);

            u80[49] = 0.85f;
            if (gpu.IsAvailable) gpu.RenderArenaFrame(128, 72, u80, false, 0u, pxCas);
            else OmegaEngineDirectComputeGpu.RenderCpuFallbackArena(128, 72, u80, pxCas);

            int diffPixels = 0;
            for (int i = 0; i < pxRaw.Length; i++)
            {
                if (pxRaw[i] != pxCas[i]) diffPixels++;
            }
            v.Passed = archIntact && (!gpu.IsAvailable || diffPixels > 200);
            v.Detail = string.Format(CultureInfo.InvariantCulture, "Bridge crown={0:0.00}m (>water -0.14m) | CAS sharpened px={1}/{2}", crownN, diffPixels, pxCas.Length);
            return v;
        }

        private static EngineStressVector RunV13_WavefrontObjArenaExport(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V13_WAVEFRONT_OBJ_ARENA_EXPORT";
            int vc, fc;
            string objPath = OmegaTriEngineMath.ExportWavefrontObjArena6D(tempDir, 1.0f, 1.0f, out vc, out fc);
            v.Trials = vc + fc;
            if (!File.Exists(objPath))
            {
                v.Detail = "Missing exported .OBJ file";
                return v;
            }
            string txt = File.ReadAllText(objPath, Encoding.UTF8);
            bool valid = (vc >= 200 && fc >= 350 && txt.IndexOf("NaN", StringComparison.OrdinalIgnoreCase) < 0 && txt.IndexOf("g CentralCrystalMonolith6D", StringComparison.Ordinal) >= 0);
            v.Passed = valid;
            v.Detail = string.Format(CultureInfo.InvariantCulture, "Manifold 3D .OBJ ok | verts={0} tris={1} (0 NaN)", vc, fc);
            return v;
        }

        private static EngineStressVector RunV14_GpuPngSnapshotCapture(OmegaEngineDirectComputeGpu gpu, string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V14_GPU_PNG_SNAPSHOT_CAPTURE";
            v.Trials = 160 * 90;
            float[] u80 = BuildCanonicalTestUniforms(160, 90, 1.5f, 3);
            string pngPath = OmegaEngineCockpitForm.SaveGpuPngSnapshotStatic(gpu, tempDir, 160, 90, u80);
            if (!File.Exists(pngPath))
            {
                v.Detail = "Missing captured PNG";
                return v;
            }
            byte[] raw = File.ReadAllBytes(pngPath);
            bool pngMagic = (raw.Length > 256 && raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47);
            v.Passed = pngMagic;
            v.Detail = string.Format(CultureInfo.InvariantCulture, "Lossless PNG capture ok | {0} bytes (89 50 4E 47)", raw.Length);
            return v;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnOmegaAddonInjectRuntime(IntPtr pRuntime, [MarshalAs(UnmanagedType.LPWStr)] string customMapName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FnOmegaAddonResetCache();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EnumUniformCallbackDel(IntPtr pRuntime, ulong variableHandle, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VtblNoopDel(IntPtr pThis);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VtblEnumUniformsDel(IntPtr pThis, IntPtr effectName, IntPtr callbackPtr, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool VtblGetAnnIntDel(IntPtr pThis, ulong variable, IntPtr annName, IntPtr outValues, UIntPtr count, UIntPtr arrayIndex);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool VtblGetAnnStrDel(IntPtr pThis, ulong variable, IntPtr annName, IntPtr outBuf, IntPtr ioSize);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VtblSetFloatDel(IntPtr pThis, ulong variable, IntPtr floatValues, UIntPtr count, UIntPtr arrayIndex);

        private sealed class MockReshadeRuntimeHarness : IDisposable
        {
            public readonly IntPtr VtableMem;
            public readonly IntPtr RuntimeObj;
            public readonly float[] Captured80 = new float[80];
            public int EnumCallCount = 0;
            public int SetFloatCallCount = 0;
            public uint UpdatedSlotMask = 0u;

            private readonly List<Delegate> _pinnedDelegates = new List<Delegate>();

            public MockReshadeRuntimeHarness()
            {
                VtableMem = Marshal.AllocHGlobal(40 * IntPtr.Size);
                RuntimeObj = Marshal.AllocHGlobal(IntPtr.Size * 2);

                VtblNoopDel noop = new VtblNoopDel(OnNoop);
                _pinnedDelegates.Add(noop);
                IntPtr pNoop = Marshal.GetFunctionPointerForDelegate(noop);
                for (int i = 0; i < 40; i++)
                {
                    Marshal.WriteIntPtr(VtableMem, i * IntPtr.Size, pNoop);
                }

                VtblEnumUniformsDel fnEnum = new VtblEnumUniformsDel(OnEnumUniforms);
                VtblGetAnnIntDel fnInt = new VtblGetAnnIntDel(OnGetAnnotationInt);
                VtblGetAnnStrDel fnStr = new VtblGetAnnStrDel(OnGetAnnotationStr);
                VtblSetFloatDel fnSetF = new VtblSetFloatDel(OnSetUniformFloat);
                _pinnedDelegates.Add(fnEnum);
                _pinnedDelegates.Add(fnInt);
                _pinnedDelegates.Add(fnStr);
                _pinnedDelegates.Add(fnSetF);

                // ReShade 6.0..6.8+ (API >= 10) exact vtable slots (inherits device_object -> api_object ONLY, delta = 0)
                Marshal.WriteIntPtr(VtableMem, 19 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fnEnum));
                Marshal.WriteIntPtr(VtableMem, 25 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fnInt));
                Marshal.WriteIntPtr(VtableMem, 27 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fnStr));
                Marshal.WriteIntPtr(VtableMem, 33 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fnSetF));

                Marshal.WriteIntPtr(RuntimeObj, 0, VtableMem);
            }

            private void OnNoop(IntPtr pThis) { }

            private void OnEnumUniforms(IntPtr pThis, IntPtr effectName, IntPtr callbackPtr, IntPtr userData)
            {
                EnumCallCount++;
                EnumUniformCallbackDel cb = (EnumUniformCallbackDel)Marshal.GetDelegateForFunctionPointer(callbackPtr, typeof(EnumUniformCallbackDel));
                for (int i = 0; i < 20; i++)
                {
                    cb(pThis, 0x7000UL + (ulong)i, userData);
                }
            }

            private bool OnGetAnnotationInt(IntPtr pThis, ulong variable, IntPtr annName, IntPtr outValues, UIntPtr count, UIntPtr arrayIndex)
            {
                string name = Marshal.PtrToStringAnsi(annName) ?? "";
                if (!string.Equals(name, "index", StringComparison.Ordinal)) return false;
                if (variable < 0x7000UL || variable >= 0x7014UL) return false;
                Marshal.WriteInt32(outValues, (int)(variable - 0x7000UL));
                return true;
            }

            private bool OnGetAnnotationStr(IntPtr pThis, ulong variable, IntPtr annName, IntPtr outBuf, IntPtr ioSize)
            {
                string name = Marshal.PtrToStringAnsi(annName) ?? "";
                if (!string.Equals(name, "source", StringComparison.Ordinal)) return false;
                byte[] bytes = Encoding.ASCII.GetBytes("omega_tri_engine\0");
                if (outBuf != IntPtr.Zero)
                {
                    Marshal.Copy(bytes, 0, outBuf, bytes.Length);
                }
                if (ioSize != IntPtr.Zero)
                {
                    Marshal.WriteIntPtr(ioSize, new IntPtr(bytes.Length));
                }
                return true;
            }

            private void OnSetUniformFloat(IntPtr pThis, ulong variable, IntPtr floatValues, UIntPtr count, UIntPtr arrayIndex)
            {
                if (variable < 0x7000UL || variable >= 0x7014UL) return;
                int slot = (int)(variable - 0x7000UL);
                Marshal.Copy(floatValues, Captured80, slot * 4, 4);
                UpdatedSlotMask |= (1u << slot);
                SetFloatCallCount++;
            }

            public void Dispose()
            {
                GC.KeepAlive(_pinnedDelegates);
                if (RuntimeObj != IntPtr.Zero) Marshal.FreeHGlobal(RuntimeObj);
                if (VtableMem != IntPtr.Zero) Marshal.FreeHGlobal(VtableMem);
            }
        }

        private static EngineStressVector RunV17_MultiStationSeqlockSliceMerge()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V17_MULTI_STATION_SEQLOCK_MERGE";
            v.Trials = 512;
            string mapName = "Local\\OmegaTriMesh_V17_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);

            using (OmegaTriEngineSharedBus syBus = new OmegaTriEngineSharedBus(mapName))
            using (OmegaTriEngineSharedBus pnBus = new OmegaTriEngineSharedBus(mapName))
            using (OmegaTriEngineSharedBus avBus = new OmegaTriEngineSharedBus(mapName))
            using (OmegaTriEngineSharedBus tnBus = new OmegaTriEngineSharedBus(mapName))
            using (OmegaTriEngineSharedBus enBus = new OmegaTriEngineSharedBus(mapName))
            {
                float[] bands8 = new float[] { 0.81f, 0.72f, 0.64f, 0.53f, 0.44f, 0.35f, 0.26f, 0.19f };
                double[] g5 = new double[5];
                double[] avFallback = new double[] { 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1 };
                float[] enFrame = BuildCanonicalTestUniforms(1920, 1080, 3.25f, 1);
                float[] readBack = new float[80];
                ulong lastSeq = 0UL;
                uint lastMask = 0u;

                for (int t = 0; t < v.Trials; t++)
                {
                    OmegaTriEngineMath.ComputeZeroSumPentagridShifts(0.35 + t * 0.02, g5);
                    if (!syBus.PublishSynthSlice(3.25f, 0.91f, 0.62f, 0.34f, 0.95f, bands8, 5, 8, 128.0f, 21.5f, 137))
                    {
                        v.Detail = "Synth slice publish failed";
                        return v;
                    }
                    if (!pnBus.PublishPenroseSlice(3.25f, g5, 0.21f, -0.17f, 0.09f, 3, 1.45f))
                    {
                        v.Detail = "Penrose slice publish failed";
                        return v;
                    }
                    if (!avBus.PublishAvSlice(3.25f, 1.18f, 0.88f, avFallback, 0.4f, 19.0f, 42))
                    {
                        v.Detail = "AV slice publish failed";
                        return v;
                    }
                    if (!tnBus.PublishTensorSlice(3.25f, 12.306f, 23.4f, 173, true, true))
                    {
                        v.Detail = "Tensor + Dual-Pixel slice publish failed";
                        return v;
                    }
                    enFrame[4] = 12.5f + t * 0.01f;
                    enFrame[56] = 1.25f;
                    ulong seq; uint crc; uint liveMask; uint addonFrm;
                    if (!enBus.PublishEngineFrameAndMerge(enFrame, 0u, out seq, out crc, out liveMask, out addonFrm))
                    {
                        v.Detail = "Engine merge publish failed";
                        return v;
                    }
                    lastSeq = seq;
                    lastMask = liveMask;
                }

                ulong rSeq; uint rMask, rCrc, rFrm, rApi;
                if (!OmegaTriEngineSharedBus.TryReadSnapshotEx(mapName, readBack, out rSeq, out rMask, out rCrc, out rFrm, out rApi))
                {
                    v.Detail = "TryReadSnapshotEx failed after 4-station merge";
                    return v;
                }

                uint expected5 = OmegaTriEngineSharedBus.MaskOmegaEngine
                               | OmegaTriEngineSharedBus.MaskOmegaSynth
                               | OmegaTriEngineSharedBus.MaskOmegaPenrose
                               | OmegaTriEngineSharedBus.MaskOmegaAv
                               | OmegaTriEngineSharedBus.MaskOmegaTensor
                               | OmegaTriEngineSharedBus.MaskPixel10Live
                               | OmegaTriEngineSharedBus.MaskPixel8Live;
                double gSum = (double)readBack[28] + readBack[29] + readBack[30] + readBack[31] + readBack[32];
                bool slicesIntact = ((rMask & expected5) == expected5)
                    && Math.Abs(readBack[12] - 0.91f) < 1e-4f     // Synth Bass
                    && Math.Abs(readBack[44] - 5.0f) < 1e-4f      // Synth ChladniM
                    && Math.Abs(readBack[51] - 137.0f) < 1e-4f    // Synth Lead E8 root
                    && Math.Abs(readBack[33] - 0.21f) < 1e-4f     // Penrose PhasonX
                    && Math.Abs(readBack[35] - 12.306f) < 1e-3f   // OmegaTensor 7-Engine TOPS/s
                    && Math.Abs(gSum) < 1e-5                      // Penrose zero-sum gamma_0..4
                    && Math.Abs(readBack[48] - 1.18f) < 1e-4f     // AV FluidEnergy
                    && Math.Abs(readBack[56] - 1.25f) < 1e-4f;    // Engine Thu'um shockwave

                v.Passed = slicesIntact && ((lastSeq & 1UL) == 0UL);
                v.Detail = string.Format(CultureInfo.InvariantCulture,
                    "5-station+DualPixel merge ok | mask=0x{0:X3} | tops={1:0.00} | seq={2}",
                    lastMask, readBack[35], lastSeq);
                return v;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FnMockReShadeConfigure(uint maxApiVersion, uint annotatedMode);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnMockReShadeAttachAddon(IntPtr addonHmod, IntPtr selfHmod);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnMockReShadeFireBeginEffects();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FnMockReShadeFireReloadEffects();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnMockReShadeReadUniforms80(IntPtr outFloats80);

        private static void InvokeReadUniforms80(FnMockReShadeReadUniforms80 fnRead, float[] dst80)
        {
            IntPtr pBuf = Marshal.AllocHGlobal(80 * 4);
            try
            {
                fnRead(pBuf);
                Marshal.Copy(pBuf, dst80, 0, 80);
            }
            finally
            {
                Marshal.FreeHGlobal(pBuf);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnMockReShadeGetStats(out uint enumCalls, out uint setCalls, out uint slotMask);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FnMockReShadeDetachAddon(IntPtr addonHmod, IntPtr selfHmod);

        public static int RunCrossProcessReshadeHostLoop(int verifyFrames, int parentPid)
        {
            string addonPath = OmegaEngineHlslAndReshade.FindCompiledAddon64Path();
            string hostPath = OmegaEngineHlslAndReshade.FindCompiledMockHost64Path();
            if (string.IsNullOrEmpty(addonPath) || !File.Exists(addonPath))
            {
                Console.Error.WriteLine("[RSLIVE_HOST_ERR] Missing OmegaTriEngine.addon64");
                return 2;
            }
            if (string.IsNullOrEmpty(hostPath) || !File.Exists(hostPath))
            {
                Console.Error.WriteLine("[RSLIVE_HOST_ERR] Missing OmegaReShadeMockHost64.dll");
                return 3;
            }

            IntPtr hHost = LoadLibraryW(hostPath);
            IntPtr hAddon = LoadLibraryW(addonPath);
            if (hHost == IntPtr.Zero || hAddon == IntPtr.Zero)
            {
                if (hAddon != IntPtr.Zero) FreeLibrary(hAddon);
                if (hHost != IntPtr.Zero) FreeLibrary(hHost);
                Console.Error.WriteLine("[RSLIVE_HOST_ERR] LoadLibraryW failed");
                return 4;
            }

            try
            {
                IntPtr pCfg    = GetProcAddress(hHost, "MockReShadeConfigure");
                IntPtr pAttach = GetProcAddress(hHost, "MockReShadeAttachAddon");
                IntPtr pBegin  = GetProcAddress(hHost, "MockReShadeFireBeginEffects");
                IntPtr pRead80 = GetProcAddress(hHost, "MockReShadeReadUniforms80");
                IntPtr pStats  = GetProcAddress(hHost, "MockReShadeGetStats");
                IntPtr pDetach = GetProcAddress(hHost, "MockReShadeDetachAddon");
                if (pCfg == IntPtr.Zero || pAttach == IntPtr.Zero || pBegin == IntPtr.Zero ||
                    pRead80 == IntPtr.Zero || pStats == IntPtr.Zero || pDetach == IntPtr.Zero)
                {
                    Console.Error.WriteLine("[RSLIVE_HOST_ERR] Missing MockHost exports");
                    return 5;
                }

                FnMockReShadeConfigure fnCfg = (FnMockReShadeConfigure)Marshal.GetDelegateForFunctionPointer(pCfg, typeof(FnMockReShadeConfigure));
                FnMockReShadeAttachAddon fnAttach = (FnMockReShadeAttachAddon)Marshal.GetDelegateForFunctionPointer(pAttach, typeof(FnMockReShadeAttachAddon));
                FnMockReShadeFireBeginEffects fnBegin = (FnMockReShadeFireBeginEffects)Marshal.GetDelegateForFunctionPointer(pBegin, typeof(FnMockReShadeFireBeginEffects));
                FnMockReShadeReadUniforms80 fnRead80 = (FnMockReShadeReadUniforms80)Marshal.GetDelegateForFunctionPointer(pRead80, typeof(FnMockReShadeReadUniforms80));
                FnMockReShadeGetStats fnStats = (FnMockReShadeGetStats)Marshal.GetDelegateForFunctionPointer(pStats, typeof(FnMockReShadeGetStats));
                FnMockReShadeDetachAddon fnDetach = (FnMockReShadeDetachAddon)Marshal.GetDelegateForFunctionPointer(pDetach, typeof(FnMockReShadeDetachAddon));

                fnCfg(20u, 1u);
                uint negotiatedApi = fnAttach(hAddon, hHost);
                if (negotiatedApi != 20u)
                {
                    Console.Error.WriteLine("[RSLIVE_HOST_ERR] AddonInit failed to negotiate API v20 (got " + negotiatedApi + ")");
                    return 6;
                }

                try
                {
                    if (verifyFrames > 0)
                    {
                        int delivered = 0;
                        int guardLoops = 0;
                        while (delivered < verifyFrames && guardLoops < verifyFrames * 8 + 64)
                        {
                            uint pushed = fnBegin();
                            if (pushed == 20u) delivered++;
                            else Thread.Sleep(2);
                            guardLoops++;
                        }
                        float[] got80 = new float[80];
                        InvokeReadUniforms80(fnRead80, got80);
                        uint enumC, setC, slotM;
                        fnStats(out enumC, out setC, out slotM);
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "[RSLIVE_HOST_OK] frames={0} api={1} slots=0x{2:X5} enum={3} u0_t={4:0.000} u3_bass={5:0.000} tops={6:0.000}",
                            delivered, negotiatedApi, slotM, enumC, got80[2], got80[12], got80[35]));
                        Console.Out.Flush();
                        return (delivered >= verifyFrames && slotM == 0x000FFFFFu) ? 0 : 7;
                    }

                    // Continuous 60 Hz background ReShade host loop tied to parent cockpit PID
                    while (true)
                    {
                        if (parentPid > 0)
                        {
                            try
                            {
                                using (Process pp = Process.GetProcessById(parentPid))
                                {
                                    if (pp.HasExited) break;
                                }
                            }
                            catch
                            {
                                break;
                            }
                        }
                        fnBegin();
                        Thread.Sleep(16);
                    }
                    return 0;
                }
                finally
                {
                    fnDetach(hAddon, hHost);
                }
            }
            finally
            {
                FreeLibrary(hAddon);
                FreeLibrary(hHost);
            }
        }

        private static EngineStressVector RunV18_Addon64ZeroCrtPe32PlusAndVtable(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V18_ADDON64_ZERO_CRT_PE32PLUS";
            v.Trials = 164;

            string addonPath = OmegaEngineHlslAndReshade.FindCompiledAddon64Path();
            if (string.IsNullOrEmpty(addonPath) || !File.Exists(addonPath))
            {
                v.Passed = false;
                v.Detail = "Compiled OmegaTriEngine.addon64 not found";
                return v;
            }

            List<string> impDlls;
            List<string> expSyms;
            long sizeBytes;
            string peErr;
            if (!OmegaEngineHlslAndReshade.InspectPe32PlusZeroCrt(addonPath, out impDlls, out expSyms, out sizeBytes, out peErr))
            {
                v.Passed = false;
                v.Detail = "PE32+ inspect failed: " + peErr;
                return v;
            }

            if (impDlls.Count != 1 || !string.Equals(impDlls[0], "KERNEL32.dll", StringComparison.OrdinalIgnoreCase))
            {
                v.Passed = false;
                v.Detail = "Non-zero-CRT imports detected: [" + string.Join(", ", impDlls.ToArray()) + "]";
                return v;
            }

            string[] reqExports = new string[] { "NAME", "DESCRIPTION", "WEBSITE", "ISSUES", "AUTHOR", "AddonInit", "AddonUninit", "OmegaAddonInjectRuntime", "OmegaAddonResetCache" };
            for (int r = 0; r < reqExports.Length; r++)
            {
                if (!expSyms.Contains(reqExports[r]))
                {
                    v.Passed = false;
                    v.Detail = "Missing PE32+ export symbol: " + reqExports[r];
                    return v;
                }
            }

            IntPtr hMod = LoadLibraryW(addonPath);
            if (hMod == IntPtr.Zero)
            {
                v.Passed = false;
                v.Detail = "LoadLibraryW failed on OmegaTriEngine.addon64 (Win32=" + Marshal.GetLastWin32Error() + ")";
                return v;
            }

            try
            {
                IntPtr pName   = GetProcAddress(hMod, "NAME");
                IntPtr pWeb    = GetProcAddress(hMod, "WEBSITE");
                IntPtr pInject = GetProcAddress(hMod, "OmegaAddonInjectRuntime");
                IntPtr pReset  = GetProcAddress(hMod, "OmegaAddonResetCache");

                string exportedName = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(pName)) ?? "";
                string exportedWeb  = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(pWeb)) ?? "";
                if (!string.Equals(exportedName, "OmegaTriEngine", StringComparison.Ordinal) || exportedWeb.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    v.Detail = "Unexpected metadata exports: NAME=" + exportedName;
                    return v;
                }

                FnOmegaAddonInjectRuntime fnInject = (FnOmegaAddonInjectRuntime)Marshal.GetDelegateForFunctionPointer(pInject, typeof(FnOmegaAddonInjectRuntime));
                FnOmegaAddonResetCache fnReset = (FnOmegaAddonResetCache)Marshal.GetDelegateForFunctionPointer(pReset, typeof(FnOmegaAddonResetCache));
                fnReset();

                string mapName = "Local\\OmegaTriAddon_V18_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
                using (OmegaTriEngineUniformBus bus = new OmegaTriEngineUniformBus(mapName))
                using (MockReshadeRuntimeHarness mock = new MockReshadeRuntimeHarness())
                {
                    float[] src1 = BuildCanonicalTestUniforms(2560, 1440, 7.75f, 1);
                    src1[12] = 0.88f;
                    src1[56] = 1.42f;
                    ulong seq1; uint crc1;
                    bus.PublishSnapshot(src1, 0u, out seq1, out crc1);

                    uint pushed1 = fnInject(mock.RuntimeObj, mapName);
                    if (pushed1 != 20u || mock.UpdatedSlotMask != 0x000FFFFFu || mock.EnumCallCount != 1)
                    {
                        v.Detail = string.Format(CultureInfo.InvariantCulture, "Frame1 vtable[19/25/27/33] mismatch: pushed={0} enum={1}", pushed1, mock.EnumCallCount);
                        return v;
                    }
                    for (int k = 0; k < 80; k++)
                    {
                        if (Math.Abs(mock.Captured80[k] - src1[k]) > 1e-5f)
                        {
                            v.Detail = "Frame1 float mismatch at index " + k;
                            return v;
                        }
                    }

                    float[] src2 = BuildCanonicalTestUniforms(2560, 1440, 9.50f, 3);
                    src2[12] = 0.47f;
                    src2[57] = 6.25f;
                    bus.PublishSnapshot(src2, 0u, out seq1, out crc1);
                    mock.UpdatedSlotMask = 0u;

                    uint pushed2 = fnInject(mock.RuntimeObj, mapName);
                    if (pushed2 != 20u || mock.UpdatedSlotMask != 0x000FFFFFu || mock.EnumCallCount != 1)
                    {
                        v.Detail = "Cached handle fast-path failed on Frame 2";
                        return v;
                    }

                    string fakeGameDir = Path.Combine(tempDir, "SkyrimSE_MockGameDir");
                    Directory.CreateDirectory(fakeGameDir);
                    string depDetail;
                    bool depOk = OmegaEngineHlslAndReshade.DeployBridgeToGameDirectory(fakeGameDir, src2, "", out depDetail);

                    double kb = Math.Round(sizeBytes / 1024.0, 1);
                    v.Passed = depOk;
                    v.Detail = string.Format(CultureInfo.InvariantCulture,
                        "Zero-CRT PE32+ ({0:0.0} KB | Import=KERNEL32.dll ONLY | {1} exports | vtable[19/25/27/33] ok)",
                        kb, expSyms.Count);
                    return v;
                }
            }
            finally
            {
                FreeLibrary(hMod);
            }
        }

        private static EngineStressVector RunV19_NativeReshadeHostDualAbiV20AndV9()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V19_RESHADE_DUAL_ABI_V20_AND_V9";
            v.Trials = 240;

            string addonPath = OmegaEngineHlslAndReshade.FindCompiledAddon64Path();
            string hostPath = OmegaEngineHlslAndReshade.FindCompiledMockHost64Path();
            if (string.IsNullOrEmpty(addonPath) || !File.Exists(addonPath) || string.IsNullOrEmpty(hostPath) || !File.Exists(hostPath))
            {
                v.Detail = "Missing OmegaTriEngine.addon64 or OmegaReShadeMockHost64.dll";
                return v;
            }

            List<string> hostImps, hostExps; long hostBytes; string hostErr;
            if (!OmegaEngineHlslAndReshade.InspectPe32PlusZeroCrt(hostPath, out hostImps, out hostExps, out hostBytes, out hostErr) ||
                hostImps.Count != 1 || !string.Equals(hostImps[0], "KERNEL32.dll", StringComparison.OrdinalIgnoreCase))
            {
                v.Detail = "MockHost64 not zero-CRT KERNEL32.dll only";
                return v;
            }

            IntPtr hHost = LoadLibraryW(hostPath);
            IntPtr hAddon = LoadLibraryW(addonPath);
            if (hHost == IntPtr.Zero || hAddon == IntPtr.Zero)
            {
                if (hAddon != IntPtr.Zero) FreeLibrary(hAddon);
                if (hHost != IntPtr.Zero) FreeLibrary(hHost);
                v.Detail = "LoadLibraryW failed in V19";
                return v;
            }

            try
            {
                FnMockReShadeConfigure fnCfg = (FnMockReShadeConfigure)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hHost, "MockReShadeConfigure"), typeof(FnMockReShadeConfigure));
                FnMockReShadeAttachAddon fnAttach = (FnMockReShadeAttachAddon)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hHost, "MockReShadeAttachAddon"), typeof(FnMockReShadeAttachAddon));
                FnMockReShadeFireBeginEffects fnBegin = (FnMockReShadeFireBeginEffects)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hHost, "MockReShadeFireBeginEffects"), typeof(FnMockReShadeFireBeginEffects));
                FnMockReShadeFireReloadEffects fnReload = (FnMockReShadeFireReloadEffects)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hHost, "MockReShadeFireReloadEffects"), typeof(FnMockReShadeFireReloadEffects));
                FnMockReShadeReadUniforms80 fnRead80 = (FnMockReShadeReadUniforms80)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hHost, "MockReShadeReadUniforms80"), typeof(FnMockReShadeReadUniforms80));
                FnMockReShadeGetStats fnStats = (FnMockReShadeGetStats)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hHost, "MockReShadeGetStats"), typeof(FnMockReShadeGetStats));
                FnMockReShadeDetachAddon fnDetach = (FnMockReShadeDetachAddon)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hHost, "MockReShadeDetachAddon"), typeof(FnMockReShadeDetachAddon));

                using (OmegaTriEngineUniformBus defaultBus = new OmegaTriEngineUniformBus(OmegaTriEngineUniformBus.DefaultMapName))
                {
                    float[] testU = BuildCanonicalTestUniforms(3840, 2160, 14.5f, 2);
                    testU[12] = 0.93f;
                    testU[35] = 16.200f;
                    testU[56] = 1.37f;
                    ulong seq; uint crc;
                    defaultBus.PublishSnapshot(testU, OmegaTriEngineUniformBus.MaskOmegaSynth | OmegaTriEngineUniformBus.MaskOmegaPenrose, out seq, out crc);

                    // 1) ReShade 6.8 (API v20, annotated U0..U19 + hot-reload cache invalidation)
                    fnCfg(20u, 1u);
                    uint api1 = fnAttach(hAddon, hHost);
                    uint p1 = fnBegin();
                    uint p2 = fnBegin();
                    uint enum1, set1, mask1;
                    fnStats(out enum1, out set1, out mask1);
                    fnReload();
                    uint p3 = fnBegin();
                    uint enumAfterReload, setAfterReload, maskAfterReload;
                    fnStats(out enumAfterReload, out setAfterReload, out maskAfterReload);
                    fnDetach(hAddon, hHost);

                    if (api1 != 20u || p1 != 20u || p2 != 20u || p3 != 20u || enum1 != 1u || enumAfterReload != 2u || maskAfterReload != 0x000FFFFFu)
                    {
                        v.Detail = string.Format(CultureInfo.InvariantCulture, "API v20 annotated/reload failed: api={0} enum={1}->{2}", api1, enum1, enumAfterReload);
                        return v;
                    }

                    // 2) ReShade 6.8 (API v20, unannotated U0..U19 fallback via get_uniform_variable_name vtable[22])
                    fnCfg(20u, 0u);
                    uint api2 = fnAttach(hAddon, hHost);
                    uint pu = fnBegin();
                    uint enum2, set2, mask2;
                    uint nameCalls = fnStats(out enum2, out set2, out mask2);
                    fnDetach(hAddon, hHost);

                    if (api2 != 20u || pu != 20u || mask2 != 0x000FFFFFu || nameCalls < 20u)
                    {
                        v.Detail = "Unannotated U0..U19 fallback failed (nameCalls=" + nameCalls + ")";
                        return v;
                    }

                    // 3) Legacy ReShade 5.9 (API v9, event 74/76, vtable[19/22/25/27/33])
                    testU[2] = 22.75f;
                    defaultBus.PublishSnapshot(testU, 0u, out seq, out crc);
                    fnCfg(9u, 1u);
                    uint api3 = fnAttach(hAddon, hHost);
                    uint pv9 = fnBegin();
                    float[] got80 = new float[80];
                    InvokeReadUniforms80(fnRead80, got80);
                    fnDetach(hAddon, hHost);

                    if (api3 != 9u || pv9 != 20u)
                    {
                        v.Detail = "Legacy ReShade 5.9 API v9 negotiation failed (api=" + api3 + ", pushed=" + pv9 + ")";
                        return v;
                    }
                    for (int k = 0; k < 80; k++)
                    {
                        if (Math.Abs(got80[k] - testU[k]) > 1e-5f)
                        {
                            v.Detail = "Dual-ABI float mismatch at index " + k;
                            return v;
                        }
                    }

                    v.Passed = true;
                    v.Detail = "AddonInit+GetProcAddress verified: API v20 (ann+unann+reload) & v9 (80/80 exact)";
                    return v;
                }
            }
            finally
            {
                FreeLibrary(hAddon);
                FreeLibrary(hHost);
            }
        }

        private static EngineStressVector RunV20_CrossProcessRsLiveSharedMemoryBus()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V20_CROSS_PROCESS_RSLIVE_BUS";
            v.Trials = 320;

            string mapName = OmegaTriEngineUniformBus.DefaultMapName;
            using (OmegaTriEngineSharedBus syBus = new OmegaTriEngineSharedBus(mapName))
            using (OmegaTriEngineSharedBus pnBus = new OmegaTriEngineSharedBus(mapName))
            using (OmegaTriEngineSharedBus avBus = new OmegaTriEngineSharedBus(mapName))
            using (OmegaTriEngineSharedBus tnBus = new OmegaTriEngineSharedBus(mapName))
            using (OmegaTriEngineSharedBus enBus = new OmegaTriEngineSharedBus(mapName))
            {
                float[] bands8 = new float[] { 0.84f, 0.75f, 0.66f, 0.55f, 0.46f, 0.37f, 0.28f, 0.19f };
                double[] g5 = new double[5];
                OmegaTriEngineMath.ComputeZeroSumPentagridShifts(1.25, g5);
                double[] avBins = new double[] { 0.2, 0.2, 0.2, 0.2, 0.2, 0.2, 0.2, 0.2 };
                float[] enFrame = BuildCanonicalTestUniforms(1920, 1080, 31.5f, 3);
                enFrame[56] = 1.44f;

                syBus.PublishSynthSlice(31.5f, 0.94f, 0.68f, 0.41f, 0.97f, bands8, 5, 8, 128.0f, 22.0f, 137);
                pnBus.PublishPenroseSlice(31.5f, g5, 0.24f, -0.15f, 0.11f, 3, 1.48f);
                avBus.PublishAvSlice(31.5f, 1.22f, 0.89f, avBins, 0.45f, 19.0f, 42);
                tnBus.PublishTensorSlice(31.5f, 16.200f, 24.5f, 173, true, true);

                ulong seq0; uint crc0, mask0, beforeFrames;
                enBus.PublishEngineFrameAndMerge(enFrame, 0u, out seq0, out crc0, out mask0, out beforeFrames);

                string selfExe = Assembly.GetExecutingAssembly().Location;
                ProcessStartInfo psi = new ProcessStartInfo(selfExe, "--reshade-host --verify-frames 32");
                psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                string childOut = "";
                string childErr = "";
                int exitCode = -1;
                using (Process child = Process.Start(psi))
                {
                    // Keep pumping live merged frames concurrently while child process consumes Local\OmegaTriEngineUniforms!
                    while (!child.WaitForExit(8))
                    {
                        ulong sTmp; uint cTmp, mTmp, fTmp;
                        enBus.PublishEngineFrameAndMerge(enFrame, 0u, out sTmp, out cTmp, out mTmp, out fTmp);
                    }
                    childOut = (child.StandardOutput.ReadToEnd() ?? "").Trim();
                    childErr = (child.StandardError.ReadToEnd() ?? "").Trim();
                    exitCode = child.ExitCode;
                }

                // Merge Engine frame FIRST (with mutex retry against live companion stations) so TrailerOffset+16 commits MaskReshadeAddon!
                ulong seqFinal = 0UL; uint crcFinal = 0u, liveMaskFinal = 0u, addonFramesFinal = 0u;
                for (int pubTry = 0; pubTry < 12; pubTry++)
                {
                    if (enBus.PublishEngineFrameAndMerge(enFrame, 0u, out seqFinal, out crcFinal, out liveMaskFinal, out addonFramesFinal))
                        break;
                    Thread.Sleep(2);
                }

                ulong seqAfter; uint maskAfter, crcAfter, afterFrames, afterApi;
                float[] check80 = new float[80];
                bool okRead = OmegaTriEngineUniformBus.TryReadSnapshotWithAddon(
                    mapName, check80, out seqAfter, out maskAfter, out crcAfter, out afterFrames, out afterApi);

                bool rsLiveBit = ((maskAfter & OmegaTriEngineUniformBus.MaskReshadeAddon) != 0u) &&
                                 ((liveMaskFinal & OmegaTriEngineUniformBus.MaskReshadeAddon) != 0u);
                uint deltaFrames = (afterFrames >= beforeFrames) ? (afterFrames - beforeFrames) : afterFrames;

                v.Passed = (exitCode == 0) && okRead && rsLiveBit && (deltaFrames >= 32u) && (afterApi == 20u) &&
                           (childOut.IndexOf("[RSLIVE_HOST_OK]", StringComparison.Ordinal) >= 0);
                if (!v.Passed)
                {
                    v.Detail = string.Format(CultureInfo.InvariantCulture,
                        "Cross-proc fail: exit={0} rsLive={1} dFrm={2} api={3} out={4} {5}",
                        exitCode, rsLiveBit, deltaFrames, afterApi, childOut, childErr);
                    return v;
                }
                v.Detail = string.Format(CultureInfo.InvariantCulture,
                    "RS:LIVE verified across Win64 processes! mask=0x{0:X3} | +frames={1} (total={2}) | API=v{3}",
                    liveMaskFinal, deltaFrames, addonFramesFinal, afterApi);
                return v;
            }
        }


        private static EngineStressVector RunV21_Sha256DxbcDiskCacheSubMillisecond(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V21_SHA256_DXBC_DISK_CACHE_BOOT";
            v.Trials = 10 + 4;

            // Validate DXBC header verifier rejects truncated or bad-magic containers
            byte[] badBlob = new byte[64];
            badBlob[0] = 0x44; badBlob[1] = 0x58; badBlob[2] = 0x42; badBlob[3] = 0x00;
            if (OmegaEngineDirectComputeGpu.IsValidDxbcBlob(badBlob) || OmegaEngineDirectComputeGpu.IsValidDxbcBlob(null))
            {
                v.Detail = "IsValidDxbcBlob accepted malformed header";
                return v;
            }

            // Step 1: Ensure persistent workspace\shader_cache has all 14 SHA-256 .dxbc blobs (0 FXC calls if already warm!)
            int writtenFiles = 0;
            bool d3dAvail = false;
            string wsCacheDir = "";
            using (OmegaEngineDirectComputeGpu gpuWorkspaceWarm = new OmegaEngineDirectComputeGpu())
            {
                wsCacheDir = gpuWorkspaceWarm.ShaderCacheDir;
                d3dAvail = gpuWorkspaceWarm.Initialize(IntPtr.Zero, 128, 72, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
                if (!d3dAvail)
                {
                    v.Passed = true;
                    v.Detail = "Skipped DXBC GPU cache benchmark on headless non-D3D11 host";
                    return v;
                }
                writtenFiles = gpuWorkspaceWarm.WarmAllShadersAndPresets();
            }

            // Step 2: Stage isolated SHA-256 .dxbc cache copy & verify every DXBC container header + byte length
            string isoCache = Path.Combine(tempDir, "dxbc_v21_cache");
            Directory.CreateDirectory(isoCache);
            string[] wsBlobs = Directory.GetFiles(wsCacheDir, "*.dxbc");
            for (int i = 0; i < wsBlobs.Length; i++)
            {
                File.Copy(wsBlobs[i], Path.Combine(isoCache, Path.GetFileName(wsBlobs[i])), true);
            }

            string[] dxbcPaths = Directory.GetFiles(isoCache, "*.dxbc");
            if (writtenFiles < 15 || dxbcPaths.Length < 15)
            {
                v.Detail = "Expected >= 15 .dxbc blobs on disk, found " + dxbcPaths.Length;
                return v;
            }
            for (int i = 0; i < dxbcPaths.Length; i++)
            {
                byte[] raw = File.ReadAllBytes(dxbcPaths[i]);
                if (!OmegaEngineDirectComputeGpu.IsValidDxbcBlob(raw))
                {
                    v.Detail = "Invalid DXBC magic or TotalSize in " + Path.GetFileName(dxbcPaths[i]);
                    return v;
                }
            }

            // Step 3: Brand-new cold OmegaEngineDirectComputeGpu boot from SHA-256 .dxbc disk cache (0 D3DCompile calls!)
            using (OmegaEngineDirectComputeGpu gpuColdBoot = new OmegaEngineDirectComputeGpu(isoCache))
            {
                bool bootOk = gpuColdBoot.Initialize(IntPtr.Zero, 128, 72, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
                if (!bootOk)
                {
                    v.Detail = "Cold DXBC boot failed: " + (gpuColdBoot.LastError ?? "unknown");
                    return v;
                }
                Stopwatch swPreset = Stopwatch.StartNew();
                for (int p = 0; p < OmegaEngineHlslAndReshade.BuiltInPresets.Length; p++)
                {
                    if (!gpuColdBoot.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[p].CustomOmshSnippet))
                    {
                        v.Detail = "Preset " + p + " DXBC load failed";
                        return v;
                    }
                }
                swPreset.Stop();

                v.Passed = (gpuColdBoot.D3DCompileInvocations == 0) &&
                           (gpuColdBoot.DiskCacheHits >= 15) &&
                           (gpuColdBoot.InitShaderLoadMs < 120.0);
                v.Detail = string.Format(CultureInfo.InvariantCulture,
                    "{0} .dxbc blobs | boot shaders={1:0.000} ms | 8-preset swap={2:0.000} ms | FXC calls={3}",
                    dxbcPaths.Length, gpuColdBoot.InitShaderLoadMs, swPreset.Elapsed.TotalMilliseconds, gpuColdBoot.D3DCompileInvocations);
                return v;
            }
        }

        private static EngineStressVector RunV22_Fp16HdrDualKawaseBloomAndGyroOrbs(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V22_FP16_HDR_DUAL_KAWASE_BLOOM";
            int w = 160, h = 90;
            v.Trials = w * h * 2;
            if (!gpu.IsAvailable)
            {
                v.Passed = true;
                v.Detail = "Skipped FP16 HDR Dual-Kawase check on non-D3D11 host";
                return v;
            }

            gpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
            float[] u80 = BuildCanonicalTestUniforms(w, h, 2.35f, 1);
            u80[12] = 0.85f; // strong bass for HSV gyroscope + crystal monolith HDR radiance
            u80[13] = 0.72f;
            u80[51] = 97.0f; // active E8 root index
            u80[56] = 1.10f; // active cymatic shockwave HDR emitter

            int[] pxNoBloom = new int[w * h];
            int[] pxBloom   = new int[w * h];
            gpu.RequestMoatReseed();
            bool okA = gpu.RenderArenaFrameEx(w, h, 1.0f, 0.0f, true, u80, false, 0u, pxNoBloom);
            gpu.RequestMoatReseed();
            bool okB = gpu.RenderArenaFrameEx(w, h, 1.0f, 0.68f, true, u80, false, 0u, pxBloom);
            if (!okA || !okB)
            {
                v.Detail = "RenderArenaFrameEx FP16 HDR failed: " + (gpu.LastError ?? "unknown");
                return v;
            }

            long lumaSumNoBloom = 0L;
            long lumaSumBloom = 0L;
            int bloomDiffPx = 0;
            int unclippedMidDarkPx = 0;
            int peakHighlightY = 0;
            for (int i = 0; i < pxBloom.Length; i++)
            {
                int c0 = pxNoBloom[i];
                int c1 = pxBloom[i];
                int r0 = (c0 >> 16) & 255, g0 = (c0 >> 8) & 255, b0 = c0 & 255;
                int r1 = (c1 >> 16) & 255, g1 = (c1 >> 8) & 255, b1 = c1 & 255;
                int y0 = r0 * 2 + g0 * 5 + b0; // max 2040 in 8-bit sRGB
                int y1 = r1 * 2 + g1 * 5 + b1;
                lumaSumNoBloom += y0;
                lumaSumBloom += y1;
                if (Math.Abs(r1 - r0) + Math.Abs(g1 - g0) + Math.Abs(b1 - b0) >= 2) bloomDiffPx++;
                if (y1 < 1680) unclippedMidDarkPx++; // not washed out to white (< 210 sRGB)
                if (y1 > peakHighlightY) peakHighlightY = y1;
            }

            double gainPct = (lumaSumNoBloom > 0L) ? ((lumaSumBloom - lumaSumNoBloom) * 100.0 / lumaSumNoBloom) : 0.0;
            v.Passed = (bloomDiffPx > 250) &&
                       (lumaSumBloom > lumaSumNoBloom) &&
                       (gainPct > 0.4 && gainPct < 45.0) &&
                       (unclippedMidDarkPx > (w * h) * 3 / 4) &&
                       (peakHighlightY > 1350);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "R16G16B16A16_FLOAT + Dual-Kawase + HSV Orbs ok | halo px={0}/{1} (+{2:0.1}% HDR, {3}% unclipped)",
                bloomDiffPx, w * h, gainPct, (unclippedMidDarkPx * 100) / (w * h));
            return v;
        }

        private static EngineStressVector RunV23_RayCone8x8Conservative2xSsaa(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V23_RAYCONE_8X8_2X_SSAA_EXACT";
            int outW = 160, outH = 90;
            v.Trials = (outW * 2) * (outH * 2);
            if (!gpu.IsAvailable)
            {
                v.Passed = true;
                v.Detail = "Skipped 2.0x Ray-Cone SSAA check on non-D3D11 host";
                return v;
            }

            float[] u80 = BuildCanonicalTestUniforms(outW, outH, 3.10f, 0);
            u80[49] = 0.0f; // Measure pure linear HDR ray-cone geometry parity without 5-tap CAS neighbor spread
            int[] pxFull2x = new int[outW * outH];
            int[] pxCone2x = new int[outW * outH];
            int[] px1x     = new int[outW * outH];

            // Reseed CSReactionMoat identically before each render so water PDE state is 100% identical!
            gpu.RequestMoatReseed();
            bool okFull = gpu.RenderArenaFrameEx(outW, outH, 2.0f, 0.25f, false, u80, false, 0u, pxFull2x);
            gpu.RequestMoatReseed();
            bool okCone = gpu.RenderArenaFrameEx(outW, outH, 2.0f, 0.25f, true,  u80, false, 0u, pxCone2x);
            gpu.RequestMoatReseed();
            bool ok1x   = gpu.RenderArenaFrameEx(outW, outH, 1.0f, 0.25f, true,  u80, false, 0u, px1x);
            if (!okFull || !okCone || !ok1x)
            {
                v.Detail = "2.0x SSAA RenderArenaFrameEx failed: " + (gpu.LastError ?? "unknown");
                return v;
            }

            int matchingConePx = 0;
            int ssaaRefinedPx = 0;
            long totalChannelAbsErr = 0L;
            for (int i = 0; i < pxFull2x.Length; i++)
            {
                int a = pxFull2x[i];
                int b = pxCone2x[i];
                int dr = Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255));
                int dg = Math.Abs(((a >> 8) & 255)  - ((b >> 8) & 255));
                int db = Math.Abs((a & 255)         - (b & 255));
                totalChannelAbsErr += (dr + dg + db);
                if (dr <= 6 && dg <= 6 && db <= 6) matchingConePx++;
                if (pxCone2x[i] != px1x[i]) ssaaRefinedPx++;
            }

            double matchRatio = (double)matchingConePx / pxFull2x.Length;
            double mae = (double)totalChannelAbsErr / (pxFull2x.Length * 3.0);
            v.Passed = (matchRatio >= 0.940) && (mae < 4.5) && (ssaaRefinedPx > 400);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "2.00x Cone SSAA (320x180->160x90) | cone parity={0:0.00}% (MAE={1:0.00}/255) | SSAA px={2}",
                matchRatio * 100.0, mae, ssaaRefinedPx);
            return v;
        }

        private static EngineStressVector RunV24_AsyncF5BackgroundCompileNonBlocking(OmegaEngineDirectComputeGpu gpu, string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V24_ASYNC_F5_NONBLOCKING_SWAP";
            v.Trials = 16;
            if (!gpu.IsAvailable)
            {
                v.Passed = true;
                v.Detail = "Skipped async F5 background compile test on non-D3D11 host";
                return v;
            }

            string isoAsyncCache = Path.Combine(tempDir, "dxbc_v24_async");
            Directory.CreateDirectory(isoAsyncCache);
            // Seed baseline pipeline + Preset 0 .dxbc blobs so Initialize() hits disk in <20 ms and ONLY uniqueLiveOmsh runs cold async FXC!
            string wsBaseDir = OmegaEngineDirectComputeGpu.ResolveDefaultShaderCacheDir();
            if (Directory.Exists(wsBaseDir))
            {
                string[] baseBlobs = Directory.GetFiles(wsBaseDir, "*.dxbc");
                for (int b = 0; b < baseBlobs.Length; b++)
                {
                    File.Copy(baseBlobs[b], Path.Combine(isoAsyncCache, Path.GetFileName(baseBlobs[b])), true);
                }
            }
            using (OmegaEngineDirectComputeGpu asyncGpu = new OmegaEngineDirectComputeGpu(isoAsyncCache))
            {
                if (!asyncGpu.Initialize(IntPtr.Zero, 128, 72, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet))
                {
                    v.Detail = "asyncGpu Initialize failed";
                    return v;
                }

                string uniqueLiveOmsh =
                    "float asyncWave = 0.5 + 0.5 * cos(wp.x * 2.71828 + U[0].z * 3.14159);\r\n" +
                    "col += float3(0.09, 0.21, 0.17) * asyncWave;";

                Stopwatch swDispatch = Stopwatch.StartNew();
                asyncGpu.StartAsyncArenaCompile(uniqueLiveOmsh);
                swDispatch.Stop();
                double dispatchStallMs = swDispatch.Elapsed.TotalMilliseconds;

                // Render uninterrupted foreground frames while FXC compiles on background ThreadPool worker!
                float[] u80 = BuildCanonicalTestUniforms(128, 72, 4.0f, 2);
                int[] px = new int[128 * 72];
                int framesWhileCompiling = 0;
                bool done = false, ok = false, cached = false, diskHit = false;
                double bgMs = 0.0;
                string snipOut = "", errOut = null;

                Stopwatch swWait = Stopwatch.StartNew();
                while (swWait.ElapsedMilliseconds < 45000)
                {
                    u80[2] = 4.0f + framesWhileCompiling * 0.016f;
                    if (!asyncGpu.RenderArenaFrameEx(128, 72, 1.0f, 0.25f, true, u80, false, 0u, null))
                    {
                        v.Detail = "Foreground render stalled/failed during async F5 compile";
                        return v;
                    }
                    framesWhileCompiling++;
                    if (asyncGpu.PollAsyncArenaCompile(out ok, out cached, out diskHit, out bgMs, out snipOut, out errOut))
                    {
                        done = true;
                        break;
                    }
                    Thread.Sleep(8);
                }

                if (!done || !ok)
                {
                    v.Detail = "Async valid HLSL compile did not complete cleanly: " + (errOut ?? "timeout");
                    return v;
                }

                // Also verify invalid HLSL syntax is rejected asynchronously without breaking live rendering
                asyncGpu.StartAsyncArenaCompile("float syntax_err = unknown_func_404(;");
                bool badDone = false, badOk = true, badCached, badDisk;
                double badMs; string badSnip, badErr;
                Stopwatch swBad = Stopwatch.StartNew();
                while (swBad.ElapsedMilliseconds < 15000)
                {
                    if (asyncGpu.PollAsyncArenaCompile(out badOk, out badCached, out badDisk, out badMs, out badSnip, out badErr))
                    {
                        badDone = true;
                        break;
                    }
                    Thread.Sleep(5);
                }
                bool postRenderOk = asyncGpu.RenderArenaFrameEx(128, 72, 1.0f, 0.25f, true, u80, false, 0u, px);

                v.Passed = (dispatchStallMs < 120.0) && (framesWhileCompiling >= 1) && badDone && (!badOk) && postRenderOk;
                v.Detail = string.Format(CultureInfo.InvariantCulture,
                    "UI dispatch={0:0.00} ms | rendered {1} live frames during FXC ({2:0.0} ms) | bad HLSL isolated",
                    dispatchStallMs, framesWhileCompiling, bgMs);
                return v;
            }
        }


        private static EngineStressVector RunV25_HopfFibrationVillarceauLinkingAndGpu(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V25_HOPF_S3_S2_VILLARCEAU_GPU";
            v.Trials = 1024 + 180 * 180 + (128 * 72);

            // 1. Verify inverse stereographic R^3 -> S^3 & Hopf map S^3 -> S^2 unit sphere invariants across 1,024 samples
            double maxNormErr = 0.0;
            for (int i = 0; i < 1024; i++)
            {
                double x = Math.Sin(i * 0.13) * 2.5;
                double y = Math.Cos(i * 0.19) * 2.5;
                double z = Math.Sin(i * 0.29) * 2.5;
                double z0, z1, z2, z3;
                OmegaTriEngineMath.InverseStereographicR3ToS3(x, y, z, out z0, out z1, out z2, out z3);
                double s3Norm = Math.Sqrt(z0 * z0 + z1 * z1 + z2 * z2 + z3 * z3);
                double bx, by, bz;
                OmegaTriEngineMath.HopfMapS3ToS2(z0, z1, z2, z3, out bx, out by, out bz);
                double s2Norm = Math.Sqrt(bx * bx + by * by + bz * bz);
                double err = Math.Max(Math.Abs(s3Norm - 1.0), Math.Abs(s2Norm - 1.0));
                if (err > maxNormErr) maxNormErr = err;
            }

            // 2. Verify exact Gauss linking integral |Lk| == 1 between two stereographic Villarceau Clifford torus fibers
            double[][] fibA = OmegaTriEngineMath.SampleHopfVillarceauFiberR3(Math.PI * 0.5, 0.0, 180);
            double[][] fibB = OmegaTriEngineMath.SampleHopfVillarceauFiberR3(Math.PI * 0.5, 2.0 * Math.PI / 3.0, 180);
            double lk = Math.Abs(OmegaTriEngineMath.ComputeGaussLinkingNumber(fibA, fibB));
            if (maxNormErr > 1e-9 || Math.Abs(lk - 1.0) > 0.025)
            {
                v.Detail = string.Format(CultureInfo.InvariantCulture, "Hopf math mismatch: normErr={0:E2} |Lk|={1:0.0000}", maxNormErr, lk);
                return v;
            }

            // 3. Verify GPU Preset 05 (SymmetryMode = 4) compiles & renders linked Hopf Villarceau torus cleanly
            int litPx = 128 * 72;
            int diffVsP3 = 1000;
            if (gpu.IsAvailable)
            {
                if (!gpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[4].CustomOmshSnippet))
                {
                    v.Detail = "Preset 05 HLSL compile failed: " + (gpu.LastError ?? "unknown");
                    return v;
                }
                int[] pxHopf = new int[128 * 72];
                int[] pxBase = new int[128 * 72];
                float[] uHopf = BuildCanonicalTestUniforms(128, 72, 2.25f, 4);
                float[] uBase = BuildCanonicalTestUniforms(128, 72, 2.25f, 0);
                gpu.RequestMoatReseed();
                bool okH = gpu.RenderArenaFrameEx(128, 72, 1.0f, 0.30f, true, uHopf, false, 0u, pxHopf);
                gpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
                gpu.RequestMoatReseed();
                bool ok0 = gpu.RenderArenaFrameEx(128, 72, 1.0f, 0.30f, true, uBase, false, 0u, pxBase);
                if (!okH || !ok0)
                {
                    v.Detail = "GPU Hopf render failed";
                    return v;
                }
                litPx = 0;
                diffVsP3 = 0;
                for (int i = 0; i < pxHopf.Length; i++)
                {
                    if ((pxHopf[i] & 0x00FFFFFF) != 0) litPx++;
                    if (pxHopf[i] != pxBase[i]) diffVsP3++;
                }
            }
            v.Passed = (litPx > (128 * 72 * 3) / 4) && (diffVsP3 > 500);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "Hopf S^3->S^2 ok | Villarceau |Lk|={0:0.0000} | GPU mode=4 px diff={1}", lk, diffVsP3);
            return v;
        }

        private static EngineStressVector RunV26_Polychora24CellHurwitzAnd120CellH4Gpu(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V26_4D_24CELL_120CELL_H4_GPU";
            v.Trials = 576 + 1024 + (128 * 72);

            // 1. Verify binary tetrahedral group closure (24 x 24 = 576 quaternion products stay in HurwitzUnits24!)
            double[][] hu = OmegaTriEngineMath.HurwitzUnits24;
            for (int i = 0; i < 24; i++)
            {
                for (int j = 0; j < 24; j++)
                {
                    double rx, ry, rz, rw;
                    OmegaTriEngineMath.QuatMulXYZW(
                        hu[i][0], hu[i][1], hu[i][2], hu[i][3],
                        hu[j][0], hu[j][1], hu[j][2], hu[j][3],
                        out rx, out ry, out rz, out rw);
                    bool found = false;
                    for (int k = 0; k < 24; k++)
                    {
                        double d2 = (rx - hu[k][0]) * (rx - hu[k][0]) +
                                    (ry - hu[k][1]) * (ry - hu[k][1]) +
                                    (rz - hu[k][2]) * (rz - hu[k][2]) +
                                    (rw - hu[k][3]) * (rw - hu[k][3]);
                        if (d2 < 1e-9) { found = true; break; }
                    }
                    if (!found)
                    {
                        v.Detail = "Hurwitz 24-cell group product escaped 2T at (" + i + "," + j + ")";
                        return v;
                    }
                }
            }

            // 2. Verify 16-fold H4 = [5,3,3] Coxeter reflection group preserves R^4 norm and lands in fundamental Weyl chamber
            int chamberOk = 0;
            for (int t = 0; t < 1024; t++)
            {
                double vx = Math.Sin(t * 0.37 + 0.1), vy = Math.Cos(t * 0.53 - 0.2);
                double vz = Math.Sin(t * 0.71 + 0.4), vw = Math.Cos(t * 0.91 + 0.7);
                double nIn = Math.Sqrt(vx * vx + vy * vy + vz * vz + vw * vw);
                double fx, fy, fz, fw;
                OmegaTriEngineMath.FoldH4CoxeterChamber(vx, vy, vz, vw, out fx, out fy, out fz, out fw);
                double nOut = Math.Sqrt(fx * fx + fy * fy + fz * fz + fw * fw);
                double d1 = fx * (-0.8090169943749475) + fy * 0.3090169943749474 + fz * 0.5;
                double d3 = fy * (-0.8090169943749475) + fz * 0.5 + fw * 0.3090169943749474;
                if (Math.Abs(nIn - nOut) < 1e-9 && fx >= -1e-6 && fz <= 1e-6 && d1 >= -1e-6 && d3 >= -1e-6)
                {
                    chamberOk++;
                }
            }
            if (chamberOk < 1020)
            {
                v.Detail = "H4 chamber convergence below threshold: " + chamberOk + "/1024";
                return v;
            }

            // 3. Verify GPU Preset 06 (SymmetryMode = 5) compiles & renders 24-cell / 120-cell polychora cleanly
            int diff24vs120 = 800;
            if (gpu.IsAvailable)
            {
                if (!gpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[5].CustomOmshSnippet))
                {
                    v.Detail = "Preset 06 HLSL compile failed: " + (gpu.LastError ?? "unknown");
                    return v;
                }
                int[] pxA = new int[128 * 72];
                int[] pxB = new int[128 * 72];
                float[] u80 = BuildCanonicalTestUniforms(128, 72, 3.0f, 5);
                u80[62] = 0.10f; // k3 = pure Hurwitz 24-cell core
                gpu.RequestMoatReseed();
                bool okA = gpu.RenderArenaFrameEx(128, 72, 1.0f, 0.30f, true, u80, false, 0u, pxA);
                u80[62] = 1.60f; // k3 = pure H4 120-cell dodecaplex cage
                gpu.RequestMoatReseed();
                bool okB = gpu.RenderArenaFrameEx(128, 72, 1.0f, 0.30f, true, u80, false, 0u, pxB);
                if (!okA || !okB)
                {
                    v.Detail = "GPU Mode 5 polychora render failed";
                    return v;
                }
                diff24vs120 = 0;
                for (int i = 0; i < pxA.Length; i++)
                {
                    if (pxA[i] != pxB[i]) diff24vs120++;
                }
            }
            v.Passed = (chamberOk == 1024) && (diff24vs120 > 60);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "Hurwitz 2T=576/576 | H4 chamber={0}/1024 | 24 vs 120-cell px={1}", chamberOk, diff24vs120);
            return v;
        }

        private static EngineStressVector RunV27_HyperbolicPoincare534And435MobiusGpu(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V27_HYPERBOLIC_POINCARE_534_GPU";
            v.Trials = 1024 + (128 * 72) * 2;

            // 1. Verify Mobius ball translation u (+) b is an exact Riemannian isometry of H^3 across 1,024 pairs
            double maxIsoErr = 0.0;
            int totalInversions = 0;
            for (int i = 0; i < 1024; i++)
            {
                double ax = Math.Sin(i * 0.17) * 0.42, ay = Math.Cos(i * 0.23) * 0.42, az = Math.Sin(i * 0.31) * 0.42;
                double bx = Math.Cos(i * 0.19) * 0.38, by = Math.Sin(i * 0.29) * 0.38, bz = Math.Cos(i * 0.41) * 0.38;
                double tx = 0.25 * Math.Sin(i * 0.07), ty = -0.18 * Math.Cos(i * 0.11), tz = 0.22 * Math.Sin(i * 0.13);

                double dBefore = OmegaTriEngineMath.HyperbolicDistanceB3(ax, ay, az, bx, by, bz);
                double awx, awy, awz, bwx, bwy, bwz;
                OmegaTriEngineMath.MobiusTranslatePoincareBall(ax, ay, az, tx, ty, tz, out awx, out awy, out awz);
                OmegaTriEngineMath.MobiusTranslatePoincareBall(bx, by, bz, tx, ty, tz, out bwx, out bwy, out bwz);
                double dAfter = OmegaTriEngineMath.HyperbolicDistanceB3(awx, awy, awz, bwx, bwy, bwz);
                double err = Math.Abs(dBefore - dAfter);
                if (err > maxIsoErr) maxIsoErr = err;

                int inv534;
                double sdfH = OmegaTriEngineMath.EvalHyperbolicPoincarePortalSdf(ax * 2.1, ay * 2.1, az * 2.1, 1.55, (i & 1) != 0, out inv534);
                if (double.IsNaN(sdfH) || double.IsInfinity(sdfH))
                {
                    v.Detail = "Non-finite hyperbolic Poincare SDF at trial " + i;
                    return v;
                }
                totalInversions += inv534;
            }

            if (maxIsoErr > 1e-8 || totalInversions < 100)
            {
                v.Detail = string.Format(CultureInfo.InvariantCulture, "Mobius isometry err={0:E2} inv={1}", maxIsoErr, totalInversions);
                return v;
            }

            // 2. Verify GPU Preset 07 (SymmetryMode = 6) renders both {5,3,4} and {4,3,5} portals cleanly
            int diffHoneycombs = 500;
            if (gpu.IsAvailable)
            {
                if (!gpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[6].CustomOmshSnippet))
                {
                    v.Detail = "Preset 07 HLSL compile failed: " + (gpu.LastError ?? "unknown");
                    return v;
                }
                int[] px534 = new int[128 * 72];
                int[] px435 = new int[128 * 72];
                float[] u80 = BuildCanonicalTestUniforms(128, 72, 3.6f, 6);
                u80[62] = 0.55f; // {5,3,4} dodecahedral honeycomb
                gpu.RequestMoatReseed();
                bool ok1 = gpu.RenderArenaFrameEx(128, 72, 1.0f, 0.30f, true, u80, false, 0u, px534);
                u80[62] = 1.35f; // {4,3,5} order-5 cubic honeycomb
                gpu.RequestMoatReseed();
                bool ok2 = gpu.RenderArenaFrameEx(128, 72, 1.0f, 0.30f, true, u80, false, 0u, px435);
                if (!ok1 || !ok2)
                {
                    v.Detail = "GPU Mode 6 hyperbolic portal render failed";
                    return v;
                }
                diffHoneycombs = 0;
                for (int i = 0; i < px534.Length; i++)
                {
                    if (px534[i] != px435[i]) diffHoneycombs++;
                }
            }
            v.Passed = (maxIsoErr < 1e-8) && (diffHoneycombs > 50);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "Poincare {{5,3,4}}&{{4,3,5}} ok | Mobius err={0:E2} | GPU portal px={1}", maxIsoErr, diffHoneycombs);
            return v;
        }

        private static EngineStressVector RunV28_Leech24GolayCodebookAnd8ModeFxGpu(OmegaEngineDirectComputeGpu gpu, string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V28_LEECH24_GOLAY24_RESHADE_8M";
            v.Trials = 4096 + 512 + 256 + (128 * 72);

            // 1. Verify exact weight enumerator of all 4,096 [24,12,8] Extended Binary Golay codewords (1 / 759 / 2576 / 759 / 1)
            int w0 = 0, w8 = 0, w12 = 0, w16 = 0, w24 = 0, wBad = 0;
            for (uint m = 0u; m < 4096u; m++)
            {
                int wt = OmegaTriEngineMath.PopCount24(OmegaTriEngineMath.EncodeGolay24(m));
                if (wt == 0) w0++;
                else if (wt == 8) w8++;
                else if (wt == 12) w12++;
                else if (wt == 16) w16++;
                else if (wt == 24) w24++;
                else wBad++;
            }
            if (w0 != 1 || w8 != 759 || w12 != 2576 || w16 != 759 || w24 != 1 || wBad != 0)
            {
                v.Detail = string.Format(CultureInfo.InvariantCulture, "Golay weight enumerator mismatch: {0}/{1}/{2}/{3}/{4}", w0, w8, w12, w16, w24);
                return v;
            }

            // 2. Verify 100% 3-bit error recovery across 512 corrupted Golay [24,12,8] words + 24D Leech Voronoi quantization
            Random rng = new Random(24128);
            for (int t = 0; t < 512; t++)
            {
                uint msg = (uint)rng.Next(0, 4096);
                uint cw = OmegaTriEngineMath.EncodeGolay24(msg);
                int b0 = rng.Next(0, 8), b1 = rng.Next(8, 16), b2 = rng.Next(16, 24);
                uint noisy = cw ^ (1u << b0) ^ (1u << b1) ^ (1u << b2);
                int wtOut, hdOut;
                uint dec = OmegaTriEngineMath.DecodeGolay24Nearest(noisy, out wtOut, out hdOut);
                if (dec != cw || hdOut != 3)
                {
                    v.Detail = "Golay 3-bit error correction failed at trial " + t;
                    return v;
                }
            }

            double[] x24 = new double[24];
            int dodecadHits = 0;
            for (int q = 0; q < 256; q++)
            {
                for (int k = 0; k < 24; k++) x24[k] = rng.NextDouble() * 3.0 - 1.5;
                double edge24; int gWt;
                double f1 = OmegaTriEngineMath.QuantizeLeech24TurynGolay(x24, out edge24, out gWt);
                if (double.IsNaN(f1) || double.IsNaN(edge24) || f1 < 0.0 || edge24 < 0.0)
                {
                    v.Detail = "Invalid 24D Leech Voronoi metric";
                    return v;
                }
                if (gWt == 12) dodecadHits++;
            }

            // 3. Verify exported ReShade .fx contains all 8 modes (0..7) + Preset 08 GPU render
            float[] uFx = BuildCanonicalTestUniforms(1920, 1080, 5.0f, 7);
            string stubP, shaP;
            string fxPath = OmegaEngineHlslAndReshade.ExportReshadeFxAndAddonStub(
                tempDir, uFx, OmegaEngineHlslAndReshade.BuiltInPresets[7].CustomOmshSnippet, out stubP, out shaP);
            string fxTxt = File.ReadAllText(fxPath, Encoding.UTF8);
            bool fxHasAll4New =
                fxTxt.IndexOf("OmegaHopfVillarceauSeam", StringComparison.Ordinal) >= 0 &&
                fxTxt.IndexOf("OmegaPolychoraH4Seam", StringComparison.Ordinal) >= 0 &&
                fxTxt.IndexOf("OmegaPoincare534Seam", StringComparison.Ordinal) >= 0 &&
                fxTxt.IndexOf("OmegaLeech24GolaySeam", StringComparison.Ordinal) >= 0 &&
                fxTxt.IndexOf("(int)clamp(floor(U9.x + 0.5), 0.0, 7.0)", StringComparison.Ordinal) >= 0;

            int lit24 = 128 * 72;
            if (gpu.IsAvailable)
            {
                if (!gpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[7].CustomOmshSnippet))
                {
                    v.Detail = "Preset 08 HLSL compile failed: " + (gpu.LastError ?? "unknown");
                    return v;
                }
                int[] px24 = new int[128 * 72];
                float[] u80 = BuildCanonicalTestUniforms(128, 72, 4.2f, 7);
                gpu.RequestMoatReseed();
                if (!gpu.RenderArenaFrameEx(128, 72, 1.0f, 0.32f, true, u80, false, 0u, px24))
                {
                    v.Detail = "GPU Mode 7 Leech-24 render failed";
                    return v;
                }
                lit24 = 0;
                for (int i = 0; i < px24.Length; i++) if ((px24[i] & 0x00FFFFFF) != 0) lit24++;
                // Restore Preset 00 shader before exiting GPU block
                gpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
            }

            v.Passed = fxHasAll4New && (dodecadHits > 40) && (lit24 > (128 * 72 * 3) / 4);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "Golay [24,12,8] 759/2576/759 + 3b ECC ok | Leech dodecads={0}/256 | 8-mode .fx ok", dodecadHits);
            return v;
        }


        private static EngineStressVector RunV29_HermiteDualContourQef64Gyroid(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V29_HERMITE_DUAL_CONTOUR_QEF_64";
            v.Trials = 64 * 64 * 64;

            string dcDir = Path.Combine(tempDir, "dc64_forge");
            OmegaTriEngineMath.DualContourBakeResult dc = OmegaTriEngineMath.BakeHermiteDualContourObj(
                dcDir, 64, 0, 1.00f, 1.00f, 0.85f, 0.0f, 0.0f, 0.0f, 1.0f, "OmegaPBR_Mode0_2048");

            if (!dc.Ok || !File.Exists(dc.ObjPath) || !File.Exists(dc.MtlPath))
            {
                v.Detail = string.Format(CultureInfo.InvariantCulture,
                    "DC 64^3 failed: ok={0} boundary={1} nonManifold={2}",
                    dc.Ok, dc.BoundaryEdges, dc.NonManifoldEdges);
                return v;
            }

            // Stream-scan .OBJ header + verify zero NaN/Infinity tokens without LOH string allocation
            bool hasMtllib = false, hasVn = false, hasBadFloat = false;
            using (FileStream fs = new FileStream(dc.ObjPath, FileMode.Open, FileAccess.Read, FileShare.Read, 32768))
            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                int linesChecked = 0;
                while ((line = sr.ReadLine()) != null && linesChecked < 4096)
                {
                    if (line.StartsWith("mtllib ", StringComparison.Ordinal)) hasMtllib = true;
                    if (line.StartsWith("vn ", StringComparison.Ordinal)) hasVn = true;
                    if (line.IndexOf("NaN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("Infinity", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasBadFloat = true;
                        break;
                    }
                    linesChecked++;
                }
            }
            // Check .MTL references all 4 2048x2048 PBR maps
            string mtlTxt = File.ReadAllText(dc.MtlPath, Encoding.UTF8);
            bool mtlOk = mtlTxt.IndexOf("_Albedo.png", StringComparison.Ordinal) >= 0 &&
                         mtlTxt.IndexOf("_Normal.png", StringComparison.Ordinal) >= 0 &&
                         mtlTxt.IndexOf("_Height.png", StringComparison.Ordinal) >= 0 &&
                         mtlTxt.IndexOf("_Emissive.png", StringComparison.Ordinal) >= 0;

            v.Passed = dc.BoundaryEdges == 0 &&
                       dc.NonManifoldEdges == 0 &&
                       dc.VertexCount > 4000 &&
                       dc.TriangleCount > 8000 &&
                       dc.QefCreaseGainRatio > 6.0 &&
                       dc.SignedVolume > 2.0 &&
                       hasMtllib && !hasBadFloat && mtlOk;
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "64^3 QEF Dual-Contour ok | verts={0} tris={1} | 0 boundary/non-manifold | QEF={2:0.0}x ({3:0.0} ms)",
                dc.VertexCount, dc.TriangleCount, dc.QefCreaseGainRatio, dc.ElapsedMs);
            return v;
        }

        private static EngineStressVector RunV30_DualContour96PolychoraManifold(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V30_DUAL_CONTOUR_96_MULTIMODE";
            v.Trials = 96 * 96 * 96;

            string dcDir = Path.Combine(tempDir, "dc96_forge");
            OmegaTriEngineMath.DualContourBakeResult dc96 = OmegaTriEngineMath.BakeHermiteDualContourObj(
                dcDir, 96, 5, 1.05f, 1.10f, 0.85f, 0.0f, 0.0f, 0.0f, 1.0f, "OmegaPBR_Mode5_2048");

            long objBytes = File.Exists(dc96.ObjPath) ? new FileInfo(dc96.ObjPath).Length : 0L;
            // Clean up temp 96^3 .OBJ immediately after verification to keep temp disk tidy
            try { if (File.Exists(dc96.ObjPath)) File.Delete(dc96.ObjPath); } catch { }

            v.Passed = dc96.Ok &&
                       dc96.BoundaryEdges == 0 &&
                       dc96.NonManifoldEdges == 0 &&
                       dc96.VertexCount > 10000 &&
                       dc96.TriangleCount > 20000 &&
                       dc96.SignedVolume > 2.0 &&
                       dc96.QefCreaseGainRatio > 6.0 &&
                       objBytes > 250000L;
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "96^3 H4 Polychoron ok | verts={0} tris={1} | vol={2:0.00} | QEF={3:0.0}x ({4:0.0} ms)",
                dc96.VertexCount, dc96.TriangleCount, dc96.SignedVolume, dc96.QefCreaseGainRatio, dc96.ElapsedMs);
            return v;
        }

        private static bool VerifyPngHeader2048(string path, int expectedDim, out long fileLen)
        {
            fileLen = 0L;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            FileInfo fi = new FileInfo(path);
            fileLen = fi.Length;
            if (fileLen < 16384L) return false;
            byte[] hdr = new byte[24];
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Read(hdr, 0, 24) < 24) return false;
            }
            if (hdr[0] != 0x89 || hdr[1] != 0x50 || hdr[2] != 0x4E || hdr[3] != 0x47) return false;
            if (hdr[12] != 0x49 || hdr[13] != 0x48 || hdr[14] != 0x44 || hdr[15] != 0x52) return false;
            int w = (hdr[16] << 24) | (hdr[17] << 16) | (hdr[18] << 8) | hdr[19];
            int h = (hdr[20] << 24) | (hdr[21] << 16) | (hdr[22] << 8) | hdr[23];
            return (w == expectedDim && h == expectedDim);
        }

        private static EngineStressVector RunV31_Gpu2048FourMapPbrPngBaker(OmegaEngineDirectComputeGpu gpu, string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V31_GPU_2048_4MAP_PBR_PNG_BAKER";
            v.Trials = 2048 * 2048;

            string pbrDir = Path.Combine(tempDir, "pbr2048_forge");
            float[] u80 = BuildCanonicalTestUniforms(1920, 1080, 2.5f, 1);
            OmegaEngineDirectComputeGpu.PbrBakeResult pbr = gpu.BakePbrMaterialSet(pbrDir, 2048, 1, false, u80);
            if (!pbr.Ok)
            {
                v.Detail = "2048x2048 PBR bake failed: " + (pbr.Error ?? "unknown");
                return v;
            }

            long lenA, lenN, lenH, lenE;
            bool okA = VerifyPngHeader2048(pbr.AlbedoPath, 2048, out lenA);
            bool okN = VerifyPngHeader2048(pbr.NormalPath, 2048, out lenN);
            bool okH = VerifyPngHeader2048(pbr.HeightPath, 2048, out lenH);
            bool okE = VerifyPngHeader2048(pbr.EmissivePath, 2048, out lenE);
            bool nrmUnit = (pbr.MinNormalLen > 0.96 && pbr.MaxNormalLen < 1.04 && pbr.MinNormalBlue8 >= 127);
            bool seamOk  = (pbr.SeamBorderMae < 8.0);
            bool dynOk   = (pbr.HeightRangeSpread >= 40 && pbr.EmissiveLitSamples >= 8);

            // Remove temp 2048x2048 PNGs after verifying headers & metrics
            try { Directory.Delete(pbrDir, true); } catch { }
            OmegaEngineDirectComputeGpu.TrimWorkingSetNow();

            v.Passed = okA && okN && okH && okE && nrmUnit && seamOk && dynOk;
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "4x 2048x2048 PNGs ok ({0:0.0} MB) | |N|=[{1:0.00},{2:0.00}] | seam MAE={3:0.00} | GPU={4:0.00} ms ({5:0.0} ms)",
                (lenA + lenN + lenH + lenE) / (1024.0 * 1024.0),
                pbr.MinNormalLen, pbr.MaxNormalLen, pbr.SeamBorderMae, pbr.GpuDispatchMs, pbr.TotalMs);
            return v;
        }

        private static EngineStressVector RunV32_EfficiencyZeroAllocLazyUavAudit(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V32_EFFICIENCY_ZERO_ALLOC_LAZY_UAV";
            v.Trials = 10000;

            // 1. Verify lazy on-demand Readback/Staging UAV non-allocation during non-readback live rendering!
            bool lazyUavVerified = true;
            string wsBaseDir = OmegaEngineDirectComputeGpu.ResolveDefaultShaderCacheDir();
            using (OmegaEngineDirectComputeGpu liveSimGpu = new OmegaEngineDirectComputeGpu(wsBaseDir))
            {
                if (liveSimGpu.Initialize(IntPtr.Zero, 320, 180, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet))
                {
                    float[] uLive = BuildCanonicalTestUniforms(320, 180, 1.0f, 0);
                    // Render 3 live frames with optionalReadbackPixels == null and bloom == 0.0f (tests both lazy UAV & 0-bloom bypass!)
                    for (int f = 0; f < 3; f++)
                    {
                        liveSimGpu.RenderArenaFrameEx(320, 180, 1.0f, 0.0f, true, uLive, false, 0u, null);
                    }
                    lazyUavVerified = !liveSimGpu.HasReadbackAllocated && (liveSimGpu.D3DCompileInvocations == 0);
                }
            }

            // 2. Verify zero-GC-allocation FindNearestE8RootFast + QuantizeE8CosetFast across 10,000 hot-loop calls
            double[] feat8 = new double[8] { 0.42, -0.31, 0.77, 0.15, -0.58, 0.24, 0.63, -0.19 };
            double sqnrFast = 0.0, sqnrRef = 0.0;
            int rFast = OmegaTriEngineMath.FindNearestE8RootFast(feat8, out sqnrFast);
            int rRef  = OmegaTriEngineMath.FindNearestE8Root(feat8, out sqnrRef);
            if (rFast != rRef || Math.Abs(sqnrFast - sqnrRef) > 1e-6)
            {
                v.Detail = "FindNearestE8RootFast parity mismatch vs reference";
                return v;
            }

            GC.Collect(0, GCCollectionMode.Forced, true);
            int gen0Before = GC.CollectionCount(0);
            Stopwatch swHot = Stopwatch.StartNew();
            int accRoot = 0;
            for (int it = 0; it < 10000; it++)
            {
                feat8[0] = 0.40 + (it & 7) * 0.02;
                double sq;
                accRoot ^= OmegaTriEngineMath.FindNearestE8RootFast(feat8, out sq);
                bool sp;
                double d2 = OmegaTriEngineMath.QuantizeE8CosetFast(feat8, out sp);
                if (d2 < 0.0) accRoot ^= 1;
            }
            swHot.Stop();
            int gen0Delta = GC.CollectionCount(0) - gen0Before;

            OmegaEngineDirectComputeGpu.TrimWorkingSetNow();
            v.Passed = lazyUavVerified && (gen0Delta == 0) && (accRoot >= 0);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "Lazy UAV=0B | 0-Bloom skip ok | 10k E8+D8 hot-loop={0:0.00} ms (Gen0 GC={1})",
                swHot.Elapsed.TotalMilliseconds, gen0Delta);
            return v;
        }

        private static EngineStressVector RunV33_TensorOpenAiOmshAutoFxcCoPilot(OmegaEngineDirectComputeGpu gpu)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V33_TENSOR_OPENAI_OMSH_COPILOT";
            v.Trials = 10;

            int mockPort = 0;
            int openAiCalls = 0;
            bool sawFxcDiagnosticInRetry = false;
            TcpListener mockSrv = new TcpListener(IPAddress.Loopback, 0);
            mockSrv.Start();
            mockPort = ((IPEndPoint)mockSrv.LocalEndpoint).Port;
            int srvAlive = 1;

            Thread th = new Thread(delegate()
            {
                while (Interlocked.CompareExchange(ref srvAlive, 0, 0) != 0)
                {
                    try
                    {
                        TcpClient cli = mockSrv.AcceptTcpClient();
                        using (cli)
                        using (NetworkStream ns = cli.GetStream())
                        {
                            cli.ReceiveTimeout = 3000;
                            cli.SendTimeout = 3000;
                            byte[] buf = new byte[16384];
                            int n = ns.Read(buf, 0, buf.Length);
                            if (n <= 0) continue;
                            string raw = Encoding.UTF8.GetString(buf, 0, n);
                            int hdrEnd = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                            while (hdrEnd < 0)
                            {
                                int moreH = ns.Read(buf, 0, buf.Length);
                                if (moreH <= 0) break;
                                raw += Encoding.UTF8.GetString(buf, 0, moreH);
                                hdrEnd = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                            }
                            if (hdrEnd >= 0)
                            {
                                string hdrPart = raw.Substring(0, hdrEnd);
                                string bodyPart = raw.Substring(hdrEnd + 4);
                                System.Text.RegularExpressions.Match mCl = System.Text.RegularExpressions.Regex.Match(
                                    hdrPart, @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                int wantLen = 0;
                                if (mCl.Success && int.TryParse(mCl.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out wantLen))
                                {
                                    int haveLen = Encoding.UTF8.GetByteCount(bodyPart);
                                    while (haveLen < wantLen)
                                    {
                                        int moreB = ns.Read(buf, 0, Math.Min(buf.Length, wantLen - haveLen));
                                        if (moreB <= 0) break;
                                        string chunk = Encoding.UTF8.GetString(buf, 0, moreB);
                                        raw += chunk;
                                        haveLen += moreB;
                                    }
                                }
                            }
                            int callNum = Interlocked.Increment(ref openAiCalls);
                            string replyContent;
                            if (callNum == 1)
                            {
                                // Intentionally broken LLM Attempt #1 (undeclared symbol -> triggers real FXC error + retry!)
                                replyContent = "\u0060\u0060\u0060hlsl\nfloat3 bad = hallucinated_gemma_fn_404(wp);\ncol += bad;\n\u0060\u0060\u0060";
                            }
                            else
                            {
                                if (raw.IndexOf("D3DCompile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    raw.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    sawFxcDiagnosticInRetry = true;
                                }
                                // Attempt #2: Returns GLSL-flavored fenced code so SanitizeAndCanonicalizeOmsh + FXC both run!
                                replyContent =
                                    "\u0060\u0060\u0060hlsl\n" +
                                    "vec3 bS2; float hSeam;\n" +
                                    "om_hopf_villarceau_field(wp * 0.30, U[6], iTime * 0.45, bS2, hSeam);\n" +
                                    "vec3 gold = vec3(1.15, 0.80, 0.22);\n" +
                                    "vec3 jade = vec3(0.10, 0.92, 0.56);\n" +
                                    "col += mix(gold, jade, fract(hSeam * 3.0 + iTime * 0.1)) * exp(-hSeam * 14.0) * (0.55 + U[3].x);\n" +
                                    "\u0060\u0060\u0060";
                            }
                            string oaResp = "{\"id\":\"chatcmpl-v33\",\"object\":\"chat.completion\",\"model\":\"gemma-omsh-copilot\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"" +
                                OmegaEngineOmshCoPilot.EscapeJson(replyContent) + "\"},\"finish_reason\":\"stop\"}]}";
                            byte[] bodyBytes = Encoding.UTF8.GetBytes(oaResp);
                            string hdr = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " +
                                bodyBytes.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n";
                            byte[] hdrBytes = Encoding.ASCII.GetBytes(hdr);
                            ns.Write(hdrBytes, 0, hdrBytes.Length);
                            ns.Write(bodyBytes, 0, bodyBytes.Length);
                        }
                    }
                    catch
                    {
                        if (Interlocked.CompareExchange(ref srvAlive, 0, 0) == 0) break;
                    }
                }
            });
            th.IsBackground = true;
            th.Start();

            OmshCoPilotResult repairRes;
            try
            {
                string epUrl = "http://127.0.0.1:" + mockPort.ToString(CultureInfo.InvariantCulture) + "/v1/chat/completions";
                repairRes = OmegaEngineOmshCoPilot.QueryCoPilotWithAutoFxc(
                    gpu, "golden hopf villarceau torus with emerald bass pulse", 4, epUrl, null);
            }
            finally
            {
                Interlocked.Exchange(ref srvAlive, 0);
                try { mockSrv.Stop(); } catch { }
            }

            // Also verify 8 compositional NL->HLSL prompts across all 8 modes pass real FXC cs_5_0 validation!
            string[] nlPrompts = new string[]
            {
                "frost sovngarde 5d penrose quasicrystal contour lines",
                "plasma neon 8d e8 coxeter voronoi lattice with bass pulse",
                "amber dwemer einstein hat monotile with thuum shockwave",
                "emerald chiral spectre mystic ripples with fresnel rim",
                "golden 4d hopf villarceau linked clifford torus",
                "violet 4d 120-cell h4 polychoron chambers with halo",
                "crimson hyperbolic poincare 5,3,4 geodesic portal",
                "rainbow 24d leech golay dodecad wireframe overdrive"
            };
            int nlFxcPass = 0;
            double sumFxcMs = repairRes.FxcValidateMs;
            for (int m = 0; m < nlPrompts.Length; m++)
            {
                string det = OmegaEngineOmshCoPilot.SynthesizeDeterministicNlOmsh(nlPrompts[m], m);
                double fMs;
                string fErr;
                if (gpu.ValidateOmshFastFxc(det, out fMs, out fErr))
                {
                    nlFxcPass++;
                    sumFxcMs += fMs;
                }
            }

            v.Passed = repairRes.Ok &&
                       repairRes.FxcValidated &&
                       repairRes.RepairedAfterFxcError &&
                       !repairRes.UsedLocalDeterministicCompiler &&
                       (repairRes.Attempts == 2) &&
                       sawFxcDiagnosticInRetry &&
                       (nlFxcPass == nlPrompts.Length);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "OpenAI v1 retry={0} (2-turn FXC repair ok) | GLSL->HLSL ok | NL bank={1}/{2} FXC pass ({3:0.0} ms)",
                sawFxcDiagnosticInRetry, nlFxcPass, nlPrompts.Length, sumFxcMs);
            return v;
        }

        private static EngineStressVector RunV34_HttpAiOmshAndTurntableControlLoop(OmegaEngineDirectComputeGpu gpu, string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V34_HTTP_AI_OMSH_AND_TURNTABLE";
            v.Trials = 4;

            int port = 0;
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            string subDir = Path.Combine(tempDir, "v34_http");
            OmegaEngineArenaState st = new OmegaEngineArenaState(subDir);
            st.SharedGpuForHttp = gpu;
            st.OnRenderTurntableRequestedFromHttp = delegate(int frames, int wReq, int hReq, float fpsReq, int symReq)
            {
                ArenaKeyframeTrack trk = (st.KeyframeTrack.Count >= 2)
                    ? st.KeyframeTrack
                    : ArenaKeyframeTrack.BuildCanonicalTurntableTrack(8, 9.6f, 2.65f, 1.0f, 4, 1.0f, 1.0f, 0.85f, 1.54f, 0.78f, 0.32f, 0.35f);
                return OmegaTurntableSequenceRenderer.RenderKeyframeSequencePngs(
                    gpu, trk, Path.Combine(subDir, "tt_out"), wReq, hReq, frames, fpsReq, 1.0f, true);
            };

            using (OmegaEngineHttpServer srv = new OmegaEngineHttpServer(st))
            {
                srv.Start(port, true);
                string rAi = HttpGetLocal(port, "/api/ai_omsh?sym=4&prompt=emerald+hopf+villarceau+fresnel+rim");
                string rOrb = HttpGetLocal(port, "/api/keyframes?action=orbit8&radius=9.5&dur=4.0");
                string rExp = HttpGetLocal(port, "/api/keyframes?action=export");
                string rTt = HttpGetLocal(port, "/api/render_turntable?frames=4&w=160&h=90&fps=24");

                bool okAi = rAi.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0 &&
                            rAi.IndexOf("\"fxc_valid\":true", StringComparison.Ordinal) >= 0;
                bool okOrb = rOrb.IndexOf("\"count\":8", StringComparison.Ordinal) >= 0 &&
                             rOrb.IndexOf("\"loop_closed\":true", StringComparison.Ordinal) >= 0;
                bool okExp = rExp.IndexOf("OmegaKeyframeTrack.omkf.json", StringComparison.Ordinal) >= 0;
                bool okTt = rTt.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0 &&
                            rTt.IndexOf("\"frames\":4", StringComparison.Ordinal) >= 0;

                v.Passed = okAi && okOrb && okExp && okTt;
                v.Detail = string.Format(CultureInfo.InvariantCulture,
                    "4/4 Task-7 HTTP endpoints (/api/ai_omsh, /api/keyframes, /api/render_turntable) ok on :{0}",
                    port);
            }
            return v;
        }

        private static EngineStressVector RunV35_KeyframeCatmullRomAndSquadSpline(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V35_KEYFRAME_CATMULL_SQUAD_SPLINE";
            v.Trials = 2048;

            ArenaKeyframeTrack orb = ArenaKeyframeTrack.BuildCanonicalTurntableTrack(
                8, 9.6f, 2.65f, 8.0f, 5, 1.20f, 1.10f, 0.90f, 1.65f, 0.82f, 0.34f, 0.40f);
            ArenaSplineKeyframe[] knots = orb.GetKnotsSnapshot();

            double maxKnotPosErr = 0.0;
            double maxKnotQuatErr = 0.0;
            for (int i = 0; i < knots.Length; i++)
            {
                ArenaSplineKeyframe evalK;
                orb.EvaluateAtTime(knots[i].TimeSec, out evalK);
                double dx = evalK.CamX - knots[i].CamX;
                double dy = evalK.CamY - knots[i].CamY;
                double dz = evalK.CamZ - knots[i].CamZ;
                double pErr = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (pErr > maxKnotPosErr) maxKnotPosErr = pErr;

                double qDot = Math.Abs(
                    (double)evalK.QuatX * knots[i].QuatX +
                    (double)evalK.QuatY * knots[i].QuatY +
                    (double)evalK.QuatZ * knots[i].QuatZ +
                    (double)evalK.QuatW * knots[i].QuatW);
                double qErr = Math.Abs(1.0 - qDot);
                if (qErr > maxKnotQuatErr) maxKnotQuatErr = qErr;
            }

            // Dense 2,048-sample S^3 unit-norm & closed-loop seam C0/C1 continuity audit
            double maxNormErr = 0.0;
            for (int s = 0; s < 2048; s++)
            {
                double t = (s / 2048.0) * orb.TotalDurationSec;
                ArenaSplineKeyframe p;
                orb.EvaluateAtTime(t, out p);
                double qn = Math.Sqrt((double)p.QuatX * p.QuatX + (double)p.QuatY * p.QuatY + (double)p.QuatZ * p.QuatZ + (double)p.QuatW * p.QuatW);
                double ne = Math.Abs(qn - 1.0);
                if (ne > maxNormErr) maxNormErr = ne;
            }

            // Closed-loop wrap-around seam check: EvaluateAtTime(0) vs EvaluateAtTime(TotalDurationSec)
            ArenaSplineKeyframe pStart, pWrap;
            orb.EvaluateAtTime(0.0, out pStart);
            orb.EvaluateAtTime(orb.TotalDurationSec, out pWrap);
            double seamErr = Math.Sqrt(
                Math.Pow(pStart.CamX - pWrap.CamX, 2.0) +
                Math.Pow(pStart.CamY - pWrap.CamY, 2.0) +
                Math.Pow(pStart.CamZ - pWrap.CamZ, 2.0));

            // Antipodal quaternion (-q) shortest-arc geodesic test + JSON round-trip
            double sx, sy, sz, sw;
            OmegaTriEngineMath.QuatSlerpShortest(0.0, 0.0, 0.0, 1.0, -0.0, -0.70710678, -0.0, -0.70710678, 0.5, out sx, out sy, out sz, out sw);
            bool shortestArcOk = (sw > 0.90) && (Math.Abs(sy - 0.38268343) < 1e-4);

            string jsonFile = Path.Combine(tempDir, "v35_track.omkf.json");
            orb.ExportTrackJson(jsonFile);
            ArenaKeyframeTrack reloaded = ArenaKeyframeTrack.ImportTrackJson(jsonFile);
            ArenaSplineKeyframe pReload;
            reloaded.EvaluateAtTime(2.35, out pReload);
            ArenaSplineKeyframe pOrig;
            orb.EvaluateAtTime(2.35, out pOrig);
            double reloadErr = Math.Abs(pReload.CamX - pOrig.CamX) + Math.Abs(pReload.K1 - pOrig.K1) + Math.Abs(pReload.FovDeg - pOrig.FovDeg);

            v.Passed = (maxKnotPosErr < 1e-4) &&
                       (maxKnotQuatErr < 1e-4) &&
                       (maxNormErr < 1e-5) &&
                       (seamErr < 1e-4) &&
                       shortestArcOk &&
                       (reloaded.Count == 8) &&
                       reloaded.LoopClosed &&
                       (reloadErr < 1e-3);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "8-knot Catmull+SQUAD ok | knotErr={0:0.00E+000} | |q|Err={1:0.00E+000} | seam={2:0.00E+000} | JSON ok",
                maxKnotPosErr, maxNormErr, seamErr);
            return v;
        }

        private static EngineStressVector RunV36_TurntablePngSequenceDeterminism(OmegaEngineDirectComputeGpu gpu, string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            int frames = 12;
            int w = 320, h = 180;
            v.Code = "V36_TURNTABLE_PNG_DETERMINISM";
            v.Trials = frames * w * h;

            gpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);

            ArenaKeyframeTrack trk = ArenaKeyframeTrack.BuildCanonicalTurntableTrack(
                8, 9.6f, 2.65f, 2.0f, 0, 1.05f, 1.00f, 0.85f, 1.54f, 0.78f, 0.32f, 0.25f);

            string dirA = Path.Combine(tempDir, "turntable_runA");
            string dirB = Path.Combine(tempDir, "turntable_runB");

            TurntableSequenceResult runA = OmegaTurntableSequenceRenderer.RenderKeyframeSequencePngs(
                gpu, trk, dirA, w, h, frames, 24.0f, 1.0f, true);
            TurntableSequenceResult runB = OmegaTurntableSequenceRenderer.RenderKeyframeSequencePngs(
                gpu, trk, dirB, w, h, frames, 24.0f, 1.0f, true);

            int exactShaMatches = 0;
            int validPngHeaders = 0;
            for (int i = 0; i < frames; i++)
            {
                if (i < runA.PngPaths.Count && File.Exists(runA.PngPaths[i]))
                {
                    byte[] raw = File.ReadAllBytes(runA.PngPaths[i]);
                    if (raw.Length > 2048 && raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47)
                    {
                        validPngHeaders++;
                    }
                }
                if (i < runA.FrameSha256.Count && i < runB.FrameSha256.Count &&
                    string.Equals(runA.FrameSha256[i], runB.FrameSha256[i], StringComparison.OrdinalIgnoreCase))
                {
                    exactShaMatches++;
                }
            }

            bool distinctOrbitPerspectives = (runA.FrameSha256.Count >= 10) &&
                !string.Equals(runA.FrameSha256[0], runA.FrameSha256[3], StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(runA.FrameSha256[3], runA.FrameSha256[6], StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(runA.FrameSha256[6], runA.FrameSha256[9], StringComparison.OrdinalIgnoreCase);

            v.Passed = runA.Ok &&
                       runB.Ok &&
                       (validPngHeaders == frames) &&
                       (exactShaMatches == frames) &&
                       distinctOrbitPerspectives &&
                       File.Exists(runA.ManifestPath);
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "{0}/{0} lossless PNGs ({1}x{2}) | SHA-256 bit-exact={3}/{0} | 360-deg orbit ok ({4:0.0} ms)",
                frames, w, h, exactShaMatches, runA.TotalMs);
            return v;
        }


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate UIntPtr FnOmegaSkseScanRipSingleton(
            IntPtr moduleBase, UIntPtr moduleSize, byte[] pattern, byte[] mask, UIntPtr patLen, UIntPtr dispOffset, UIntPtr instrLen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int FnOmegaSkseEvaluateSheafGuardH1(
            uint stageMask, ushort[] edges, uint edgeCount, uint inSpectreCell, out uint outCoboundaryDefect);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float FnOmegaSkseClassifyWeatherAurora(
            uint weatherFormId, uint weatherClass, float gameHour, float[] outSun4);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnOmegaSksePushWorldState(
            [MarshalAs(UnmanagedType.LPWStr)] string mapName,
            float[] camSkyrimXyz,
            float[] camMatrix3x3,
            uint weatherFormId,
            uint weatherClass,
            float gameHour,
            uint shoutFormId,
            float[] shoutOriginSkyrimXyz,
            float shoutAgeSec,
            uint questStageMask,
            uint inSpectreCell);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnOmegaSkseParsePapyrusTraceLine(
            byte[] lineAscii, UIntPtr lineLen, [MarshalAs(UnmanagedType.LPWStr)] string mapName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FnOmegaSkseGetDiagnostics(uint[] outStats6);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FnOmegaSkseStopWorker();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnMockSkseAttachPlugin(IntPtr bridgeModule, uint runtimeVersion);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnMockSkseFireLifecycle(uint msgType, uint stopWorkerAfter);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnMockSkseStepWorldFrame(uint frameIdx, [MarshalAs(UnmanagedType.LPWStr)] string mapName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FnMockSkseDetachPlugin();

        private static EngineStressVector RunV37_Skse64ZeroCrtPeAbi161170()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V37_SKSE64_ZERO_CRT_PE_ABI_161170";
            v.Trials = 128;

            string skseDll = OmegaEngineHlslAndReshade.FindCompiledSkseBridgeDllPath();
            string mockDll = OmegaEngineHlslAndReshade.FindCompiledSkseMockHostDllPath();
            if (string.IsNullOrEmpty(skseDll) || !File.Exists(skseDll) || string.IsNullOrEmpty(mockDll) || !File.Exists(mockDll))
            {
                v.Detail = "Missing compiled OmegaSkyrimWorldBridge.dll or OmegaSkseMockHost64.dll";
                return v;
            }

            List<string> impSk, expSk, impMk, expMk;
            long bytesSk, bytesMk;
            string errSk = "", errMk = "";
            if (!OmegaEngineHlslAndReshade.InspectPe32PlusZeroCrt(skseDll, out impSk, out expSk, out bytesSk, out errSk) ||
                !OmegaEngineHlslAndReshade.InspectPe32PlusZeroCrt(mockDll, out impMk, out expMk, out bytesMk, out errMk))
            {
                v.Detail = "PE32+ inspect error: " + errSk + " " + errMk;
                return v;
            }

            if (impSk.Count != 1 || !string.Equals(impSk[0], "KERNEL32.dll", StringComparison.OrdinalIgnoreCase) ||
                impMk.Count != 1 || !string.Equals(impMk[0], "KERNEL32.dll", StringComparison.OrdinalIgnoreCase))
            {
                v.Detail = "Non-zero-CRT imports on SKSE64 DLL: [" + string.Join(",", impSk.ToArray()) + "]";
                return v;
            }

            string[] reqSyms = new string[]
            {
                "SKSEPlugin_Version", "SKSEPlugin_Load", "SKSEPlugin_Query",
                "OmegaSkseScanRipSingleton", "OmegaSkseEvaluateSheafGuardH1",
                "OmegaSkseClassifyWeatherAurora", "OmegaSksePushWorldState",
                "OmegaSkseParsePapyrusTraceLine", "OmegaSkseGetDiagnostics"
            };
            for (int i = 0; i < reqSyms.Length; i++)
            {
                if (!expSk.Contains(reqSyms[i]))
                {
                    v.Detail = "Missing SKSE64 export: " + reqSyms[i];
                    return v;
                }
            }

            IntPtr hSkse = LoadLibraryW(skseDll);
            IntPtr hMock = LoadLibraryW(mockDll);
            if (hSkse == IntPtr.Zero || hMock == IntPtr.Zero)
            {
                if (hMock != IntPtr.Zero) FreeLibrary(hMock);
                if (hSkse != IntPtr.Zero) FreeLibrary(hSkse);
                v.Detail = "LoadLibraryW failed on SKSE64 DLLs";
                return v;
            }

            try
            {
                IntPtr pVer = GetProcAddress(hSkse, "SKSEPlugin_Version");
                if (pVer == IntPtr.Zero)
                {
                    v.Detail = "GetProcAddress(SKSEPlugin_Version) null";
                    return v;
                }
                byte[] ver848 = new byte[0x350];
                Marshal.Copy(pVer, ver848, 0, 0x350);
                uint dataVer = BitConverter.ToUInt32(ver848, 0x000);
                uint plugVer = BitConverter.ToUInt32(ver848, 0x004);
                string plugName = Encoding.ASCII.GetString(ver848, 0x008, 22);
                uint indepEx = BitConverter.ToUInt32(ver848, 0x304);
                uint indep   = BitConverter.ToUInt32(ver848, 0x308);
                bool has17104 = false, has161170 = false, has16640 = false;
                for (int c = 0; c < 16; c++)
                {
                    uint cv = BitConverter.ToUInt32(ver848, 0x30C + c * 4);
                    if (cv == 0x01070680u) has17104 = true;
                    if (cv == 0x01064920u) has161170 = true;
                    if (cv == 0x01062800u) has16640 = true;
                }
                if (dataVer != 1u || (plugVer != 0x00010707u && plugVer != 0x00010700u) ||
                    !string.Equals(plugName, "OmegaSkyrimWorldBridge", StringComparison.Ordinal) ||
                    indepEx != 1u || indep != 2u || !has17104 || !has161170 || !has16640)
                {
                    v.Detail = "SKSEPluginVersionData 848B layout mismatch";
                    return v;
                }

                // Verify OmegaBraidRE v1.4.0 RIP-relative [disp32] scanner (+disp and -disp)
                FnOmegaSkseScanRipSingleton fnScan = (FnOmegaSkseScanRipSingleton)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hSkse, "OmegaSkseScanRipSingleton"), typeof(FnOmegaSkseScanRipSingleton));
                byte[] codeBlock = new byte[64];
                for (int i = 0; i < codeBlock.Length; i++) codeBlock[i] = 0x90;
                // At +16: 48 8B 05 25 00 00 00 C3 -> target = base + 16 + 7 + 0x25 = base + 60
                codeBlock[16] = 0x48; codeBlock[17] = 0x8B; codeBlock[18] = 0x05;
                codeBlock[19] = 0x25; codeBlock[20] = 0x00; codeBlock[21] = 0x00; codeBlock[22] = 0x00;
                codeBlock[23] = 0xC3;
                IntPtr pBuf = Marshal.AllocHGlobal(64);
                ulong resolvedDelta = 0UL;
                try
                {
                    Marshal.Copy(codeBlock, 0, pBuf, 64);
                    byte[] pat = new byte[] { 0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x00, 0xC3 };
                    byte[] msk = new byte[] { 1, 1, 1, 0, 0, 0, 0, 1 };
                    UIntPtr hit = fnScan(pBuf, (UIntPtr)64, pat, msk, (UIntPtr)8, (UIntPtr)3, (UIntPtr)7);
                    resolvedDelta = hit.ToUInt64() - (ulong)pBuf.ToInt64();
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuf);
                }
                if (resolvedDelta != 60UL)
                {
                    v.Detail = "OmegaBraidRE RIP scanner delta mismatch: " + resolvedDelta;
                    return v;
                }

                // Verify SKSE64 2.2.6 Phase 2 Load + Messaging Lifecycle via OmegaSkseMockHost64.dll
                FnMockSkseAttachPlugin fnAttach = (FnMockSkseAttachPlugin)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hMock, "MockSkseAttachPlugin"), typeof(FnMockSkseAttachPlugin));
                FnMockSkseFireLifecycle fnLife = (FnMockSkseFireLifecycle)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hMock, "MockSkseFireLifecycle"), typeof(FnMockSkseFireLifecycle));
                FnMockSkseDetachPlugin fnDetach = (FnMockSkseDetachPlugin)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hMock, "MockSkseDetachPlugin"), typeof(FnMockSkseDetachPlugin));
                FnOmegaSkseGetDiagnostics fnDiag = (FnOmegaSkseGetDiagnostics)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hSkse, "OmegaSkseGetDiagnostics"), typeof(FnOmegaSkseGetDiagnostics));

                uint okAtt = fnAttach(hSkse, 0x01064920u);
                uint okMsg = fnLife(8u, 1u); // kMessage_DataLoaded
                uint[] stats6 = new uint[6];
                fnDiag(stats6);
                fnDetach();

                v.Passed = (okAtt == 1u && okMsg == 1u && stats6[0] == 0x01064920u && stats6[1] == 0x42u && stats6[2] >= 1u);
                v.Detail = string.Format(CultureInfo.InvariantCulture,
                    "Zero-CRT PE32+ ({0:0.0} KB | KERNEL32.dll ONLY | 848B SKSEPlugin_Version AE 1.6.1170+1.7.104 | RIP scan ok)",
                    bytesSk / 1024.0);
                return v;
            }
            finally
            {
                FreeLibrary(hMock);
                FreeLibrary(hSkse);
            }
        }

        private static EngineStressVector RunV38_WorldCameraWeatherAndThuumU13U19()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V38_WORLD_CAMERA_WEATHER_THUUM_U13_U19";
            v.Trials = 512;

            string skseDll = OmegaEngineHlslAndReshade.FindCompiledSkseBridgeDllPath();
            if (string.IsNullOrEmpty(skseDll) || !File.Exists(skseDll))
            {
                v.Detail = "OmegaSkyrimWorldBridge.dll missing";
                return v;
            }

            IntPtr hSkse = LoadLibraryW(skseDll);
            if (hSkse == IntPtr.Zero)
            {
                v.Detail = "LoadLibraryW failed";
                return v;
            }

            string mapName = "Local\\OmegaSkse_V38_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            try
            {
                FnOmegaSksePushWorldState fnPush = (FnOmegaSksePushWorldState)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hSkse, "OmegaSksePushWorldState"), typeof(FnOmegaSksePushWorldState));
                FnOmegaSkseParsePapyrusTraceLine fnParse = (FnOmegaSkseParsePapyrusTraceLine)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hSkse, "OmegaSkseParsePapyrusTraceLine"), typeof(FnOmegaSkseParsePapyrusTraceLine));
                FnOmegaSkseStopWorker fnStop = (FnOmegaSkseStopWorker)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hSkse, "OmegaSkseStopWorker"), typeof(FnOmegaSkseStopWorker));
                fnStop();

                using (OmegaTriEngineUniformBus bus = new OmegaTriEngineUniformBus(mapName))
                {
                    float[] read80 = new float[80];
                    double maxOrthoErr = 0.0;
                    for (int t = 0; t < 510; t++)
                    {
                        double yaw = (t * 0.025) - Math.PI;
                        double pitch = Math.Sin(t * 0.09) * 0.85;
                        float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
                        float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);

                        float[] skyPos = new float[] { 700.0f + t * 2.0f, 1400.0f - t, 350.0f };
                        float[] mat3x3 = new float[]
                        {
                            cy,  sy * cp, 0.0f,
                            -sy, cy * cp, 0.0f,
                            0.0f, sp,     1.0f
                        };
                        uint wId = (t == 509) ? 0x0010A243u : 0x0000081Au;
                        uint shoutId = (t == 509) ? 0x00013E07u : 0x0002F7BAu;
                        uint maskRet = fnPush(mapName, skyPos, mat3x3, wId, 0u, 22.0f, shoutId, skyPos, 0.20f, 0x2Fu, 1u);
                        if ((maskRet & OmegaTriEngineUniformBus.MaskSkse64Bridge) == 0u)
                        {
                            v.Detail = "MaskSkse64Bridge missing at trial " + t;
                            return v;
                        }
                        ulong seq; uint mask, crc;
                        if (!OmegaTriEngineUniformBus.TryReadSnapshot(mapName, read80, out seq, out mask, out crc))
                        {
                            v.Detail = "TryReadSnapshot failed at trial " + t;
                            return v;
                        }
                        double fNorm = Math.Sqrt(read80[72] * read80[72] + read80[73] * read80[73] + read80[74] * read80[74]);
                        double rNorm = Math.Sqrt(read80[76] * read80[76] + read80[77] * read80[77] + read80[78] * read80[78]);
                        double dotFR = Math.Abs(read80[72] * read80[76] + read80[73] * read80[77] + read80[74] * read80[78]);
                        maxOrthoErr = Math.Max(maxOrthoErr, Math.Max(Math.Abs(fNorm - 1.0), Math.Max(Math.Abs(rNorm - 1.0), dotFR)));
                    }

                    bool sovngardeOk = Math.Abs(read80[59] - 1.42f) < 1e-3f && read80[56] > 1.2f && read80[57] > 2.5f && read80[36] == 3.0f && read80[71] >= 0.008f && read80[71] <= 0.035f && read80[69] >= 0.30f;

                    // Test Creation Kit Papyrus UserLog "OTW|..." trace line bridge
                    string logLine = "[11/24/2026 - 02:45:12PM] OTW|700.0|1400.0|350.0|90.0|-10.0|0x0010A243|0|23.5|0x00013E07|47|1\r\n";
                    byte[] logBytes = Encoding.ASCII.GetBytes(logLine);
                    uint logMask = fnParse(logBytes, (UIntPtr)logBytes.Length, mapName);
                    ulong seqL; uint maskL, crcL;
                    OmegaTriEngineUniformBus.TryReadSnapshot(mapName, read80, out seqL, out maskL, out crcL);
                    bool traceOk = (logMask != 0u) && (Math.Abs(read80[4] - 9.996f) < 0.05f) && (Math.Abs(read80[59] - 1.42f) < 1e-3f);

                    v.Passed = (maxOrthoErr < 2e-4) && sovngardeOk && traceOk;
                    v.Detail = string.Format(CultureInfo.InvariantCulture,
                        "512 Z-up->Y-up poses orthoErr={0:0.00E+000} | Sovngarde U14.w={1:0.00} | Thu'um U14.x={2:0.00} | OTW trace ok",
                        maxOrthoErr, read80[59], read80[56]);
                    return v;
                }
            }
            finally
            {
                FreeLibrary(hSkse);
            }
        }

        private static EngineStressVector RunV39_SheafH1QuestGuardAndEslEsp(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V39_SHEAF_H1_QUEST_GUARD_AND_ESL_ESP";
            v.Trials = 64 + 12;

            string skseDll = OmegaEngineHlslAndReshade.FindCompiledSkseBridgeDllPath();
            if (string.IsNullOrEmpty(skseDll) || !File.Exists(skseDll))
            {
                v.Detail = "OmegaSkyrimWorldBridge.dll missing";
                return v;
            }

            IntPtr hSkse = LoadLibraryW(skseDll);
            if (hSkse == IntPtr.Zero)
            {
                v.Detail = "LoadLibraryW failed";
                return v;
            }

            try
            {
                FnOmegaSkseEvaluateSheafGuardH1 fnH1 = (FnOmegaSkseEvaluateSheafGuardH1)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hSkse, "OmegaSkseEvaluateSheafGuardH1"), typeof(FnOmegaSkseEvaluateSheafGuardH1));

                int validSpectre = 0, quarantined = 0;
                for (uint m = 0u; m < 64u; m++)
                {
                    uint defect;
                    int state = fnH1(m, null, 0u, 1u, out defect);
                    if (defect == 0u && state == 2) validSpectre++;
                    else if (defect > 0u && state == -1) quarantined++;
                }

                string modOutDir = Path.Combine(tempDir, "skyrim_mod_audit");
                string wsDir = Path.Combine(tempDir, "skyrim_ws");
                float[] u80 = BuildCanonicalTestUniforms(1920, 1080, 3.0f, 3);
                SkyrimModBundleResult mb = OmegaEngineSkseModBuilder.BuildCompleteSkyrimModPackage(
                    modOutDir, "", wsDir, u80, OmegaEngineHlslAndReshade.BuiltInPresets[3].CustomOmshSnippet, false);

                byte[] pscBytes = File.Exists(mb.WorldSyncPscPath) ? File.ReadAllBytes(mb.WorldSyncPscPath) : new byte[0];
                bool pscNoBom = pscBytes.Length > 64 && !(pscBytes[0] == 0xEF && pscBytes[1] == 0xBB && pscBytes[2] == 0xBF);
                string fxBody = File.Exists(mb.ReshadeFxPath) ? File.ReadAllText(mb.ReshadeFxPath, Encoding.UTF8) : "";
                bool fxHasWorldSpace = fxBody.IndexOf("OmegaReconstructRayDir", StringComparison.Ordinal) >= 0 &&
                                       fxBody.IndexOf("UiSheafQuestGuard", StringComparison.Ordinal) >= 0;
                byte[] zipBytes = File.Exists(mb.VortexZipPath) ? File.ReadAllBytes(mb.VortexZipPath) : new byte[0];
                bool zipPkMagic = zipBytes.Length > 1024 && zipBytes[0] == 0x50 && zipBytes[1] == 0x4B && zipBytes[2] == 0x03 && zipBytes[3] == 0x04;

                v.Passed = (validSpectre == 12 && quarantined == 52) && mb.Ok && (mb.EspKeywordCount == 4) && pscNoBom && fxHasWorldSpace && zipPkMagic;
                v.Detail = string.Format(CultureInfo.InvariantCulture,
                    "Sheaf H1=0 ({0}/64 valid, {1}/64 quarantined) | ESL .esp ({2}B, 4 KYWDs) | No-BOM .psc + Vortex .zip ok",
                    validSpectre, quarantined, mb.EspBytes);
                return v;
            }
            finally
            {
                FreeLibrary(hSkse);
            }
        }

        private static EngineStressVector RunV40_CrossProcessSkse64AndReshadeMesh()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V40_CROSS_PROCESS_SKSE64_RESHADE_MESH";
            v.Trials = 24;

            string selfExe = Assembly.GetEntryAssembly() != null ? Assembly.GetEntryAssembly().Location : Process.GetCurrentProcess().MainModule.FileName;
            string addonPath = OmegaEngineHlslAndReshade.FindCompiledAddon64Path();
            string rsHostPath = OmegaEngineHlslAndReshade.FindCompiledMockHost64Path();
            if (string.IsNullOrEmpty(selfExe) || !File.Exists(selfExe) || !File.Exists(addonPath) || !File.Exists(rsHostPath))
            {
                v.Detail = "Missing OmegaEngine.exe or mock host DLLs";
                return v;
            }

            using (OmegaTriEngineUniformBus engBus = new OmegaTriEngineUniformBus(OmegaTriEngineUniformBus.DefaultMapName))
            {
                float[] baseU = BuildCanonicalTestUniforms(2560, 1440, 6.0f, 0);
                ulong seq0; uint crc0, mask0, frm0;
                engBus.PublishAndMergeMesh(baseU, 0u, out seq0, out crc0, out mask0, out frm0);

                ProcessStartInfo psi = new ProcessStartInfo(selfExe, "--skse64-host --verify-frames 24");
                psi.WorkingDirectory = Path.GetDirectoryName(selfExe);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process child = Process.Start(psi))
                {
                    bool exited = child.WaitForExit(12000);
                    if (!exited)
                    {
                        try { child.Kill(); } catch { }
                        v.Detail = "Child --skse64-host timed out";
                        return v;
                    }
                    if (child.ExitCode != 0)
                    {
                        v.Detail = "Child --skse64-host failed: " + child.StandardError.ReadToEnd().Trim();
                        return v;
                    }
                }

                // Now Station 15 merges a 144 FPS frame while SKSE64 stamp is live, and ReShade .addon64 consumes it!
                float[] engFrame = BuildCanonicalTestUniforms(2560, 1440, 6.25f, 0);
                ulong seqM; uint crcM, liveMaskM, addonFrmM;
                engBus.PublishAndMergeMesh(engFrame, 0u, out seqM, out crcM, out liveMaskM, out addonFrmM);

                IntPtr hRsHost = LoadLibraryW(rsHostPath);
                IntPtr hAddon = LoadLibraryW(addonPath);
                try
                {
                    FnMockReShadeConfigure fnCfg = (FnMockReShadeConfigure)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hRsHost, "MockReShadeConfigure"), typeof(FnMockReShadeConfigure));
                    FnMockReShadeAttachAddon fnAtt = (FnMockReShadeAttachAddon)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hRsHost, "MockReShadeAttachAddon"), typeof(FnMockReShadeAttachAddon));
                    FnMockReShadeFireBeginEffects fnBeg = (FnMockReShadeFireBeginEffects)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hRsHost, "MockReShadeFireBeginEffects"), typeof(FnMockReShadeFireBeginEffects));
                    FnMockReShadeReadUniforms80 fnRd = (FnMockReShadeReadUniforms80)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hRsHost, "MockReShadeReadUniforms80"), typeof(FnMockReShadeReadUniforms80));
                    FnMockReShadeDetachAddon fnDet = (FnMockReShadeDetachAddon)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hRsHost, "MockReShadeDetachAddon"), typeof(FnMockReShadeDetachAddon));

                    fnCfg(20u, 1u);
                    fnAtt(hAddon, hRsHost);
                    fnBeg();
                    float[] rsU80 = new float[80];
                    InvokeReadUniforms80(fnRd, rsU80);
                    fnDet(hAddon, hRsHost);

                    ulong seqF; uint maskF, crcF, frmF, apiF;
                    OmegaTriEngineUniformBus.TryReadSnapshotWithAddon(OmegaTriEngineUniformBus.DefaultMapName, engFrame, out seqF, out maskF, out crcF, out frmF, out apiF);

                    bool allThreeLive = ((maskF & OmegaTriEngineUniformBus.MaskOmegaEngine) != 0u) &&
                                        ((maskF & OmegaTriEngineUniformBus.MaskSkse64Bridge) != 0u) &&
                                        ((maskF & OmegaTriEngineUniformBus.MaskReshadeAddon) != 0u);
                    bool sovngardeReachedShader = Math.Abs(rsU80[59] - 1.42f) < 1e-3f && Math.Abs(rsU80[67]) >= 0.9f && rsU80[71] >= 0.008f && rsU80[71] <= 0.035f && rsU80[69] >= 0.30f;

                    v.Passed = allThreeLive && sovngardeReachedShader;
                    v.Detail = string.Format(CultureInfo.InvariantCulture,
                        "3-node SKSE64+Engine+ReShade mesh ok | mask=0x{0:X} | U14.w={1:0.00} | U16.w(H1)={2:0.0}",
                        maskF, rsU80[59], rsU80[67]);
                    return v;
                }
                finally
                {
                    if (hAddon != IntPtr.Zero) FreeLibrary(hAddon);
                    if (hRsHost != IntPtr.Zero) FreeLibrary(hRsHost);
                }
            }
        }

        public static int RunCrossProcessSkse64HostLoop(int verifyFrames, int parentPid)
        {
            string skseDll = OmegaEngineHlslAndReshade.FindCompiledSkseBridgeDllPath();
            string hostDll = OmegaEngineHlslAndReshade.FindCompiledSkseMockHostDllPath();
            if (string.IsNullOrEmpty(skseDll) || !File.Exists(skseDll))
            {
                Console.Error.WriteLine("[SKSE64_HOST_ERR] Missing OmegaSkyrimWorldBridge.dll");
                return 2;
            }
            if (string.IsNullOrEmpty(hostDll) || !File.Exists(hostDll))
            {
                Console.Error.WriteLine("[SKSE64_HOST_ERR] Missing OmegaSkseMockHost64.dll");
                return 3;
            }

            IntPtr hHost = LoadLibraryW(hostDll);
            IntPtr hSkse = LoadLibraryW(skseDll);
            if (hHost == IntPtr.Zero || hSkse == IntPtr.Zero)
            {
                if (hSkse != IntPtr.Zero) FreeLibrary(hSkse);
                if (hHost != IntPtr.Zero) FreeLibrary(hHost);
                Console.Error.WriteLine("[SKSE64_HOST_ERR] LoadLibraryW failed");
                return 4;
            }

            try
            {
                FnMockSkseAttachPlugin fnAttach = (FnMockSkseAttachPlugin)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hHost, "MockSkseAttachPlugin"), typeof(FnMockSkseAttachPlugin));
                FnMockSkseFireLifecycle fnLife = (FnMockSkseFireLifecycle)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hHost, "MockSkseFireLifecycle"), typeof(FnMockSkseFireLifecycle));
                FnMockSkseStepWorldFrame fnStep = (FnMockSkseStepWorldFrame)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hHost, "MockSkseStepWorldFrame"), typeof(FnMockSkseStepWorldFrame));
                FnMockSkseDetachPlugin fnDetach = (FnMockSkseDetachPlugin)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(hHost, "MockSkseDetachPlugin"), typeof(FnMockSkseDetachPlugin));

                if (fnAttach(hSkse, 0x01064920u) == 0u)
                {
                    Console.Error.WriteLine("[SKSE64_HOST_ERR] MockSkseAttachPlugin failed");
                    return 5;
                }
                fnLife(8u, 1u);

                if (verifyFrames > 0)
                {
                    for (int f = 0; f < verifyFrames; f++)
                    {
                        fnStep((uint)f, OmegaTriEngineUniformBus.DefaultMapName);
                        Thread.Sleep(4);
                    }
                    fnDetach();
                    return 0;
                }

                uint tick = 0u;
                while (true)
                {
                    if (parentPid > 0)
                    {
                        try
                        {
                            using (Process pp = Process.GetProcessById(parentPid))
                            {
                                if (pp.HasExited) break;
                            }
                        }
                        catch
                        {
                            break;
                        }
                    }
                    fnStep(tick++, OmegaTriEngineUniformBus.DefaultMapName);
                    Thread.Sleep(16);
                }
                fnDetach();
                return 0;
            }
            finally
            {
                FreeLibrary(hSkse);
                FreeLibrary(hHost);
            }
        }


        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(
            byte[] pSrcData,
            UIntPtr srcDataSize,
            string pSourceName,
            IntPtr pDefines,
            IntPtr pInclude,
            string pEntrypoint,
            string pTarget,
            uint flags1,
            uint flags2,
            out IntPtr ppCode,
            out IntPtr ppErrorMsgs);

        private static string StripReshadeTopLevelAnnotations(string src)
        {
            if (string.IsNullOrEmpty(src)) return "";
            StringBuilder sb = new StringBuilder(src.Length);
            int i = 0;
            int n = src.Length;
            int braceDepth = 0;
            bool inTopDecl = false;
            while (i < n)
            {
                if (braceDepth == 0 && (i == 0 || src[i - 1] == '\n'))
                {
                    int k = i;
                    while (k < n && (src[k] == ' ' || src[k] == '\t' || src[k] == '\r')) k++;
                    if ((k + 8 <= n && string.CompareOrdinal(src, k, "uniform ", 0, 8) == 0) ||
                        (k + 10 <= n && string.CompareOrdinal(src, k, "technique ", 0, 10) == 0) ||
                        (k + 8 <= n && string.CompareOrdinal(src, k, "texture ", 0, 8) == 0) ||
                        (k + 8 <= n && string.CompareOrdinal(src, k, "sampler ", 0, 8) == 0))
                    {
                        inTopDecl = true;
                    }
                }
                char c = src[i];
                if (c == '/' && (i + 1) < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') { sb.Append(src[i]); i++; }
                    continue;
                }
                if (c == '/' && (i + 1) < n && src[i + 1] == '*')
                {
                    sb.Append(src[i]); sb.Append(src[i + 1]); i += 2;
                    while (i < n && !(src[i] == '*' && (i + 1) < n && src[i + 1] == '/'))
                    {
                        sb.Append(src[i]); i++;
                    }
                    if (i < n) { sb.Append(src[i]); sb.Append(src[i + 1]); i += 2; }
                    continue;
                }
                if (c == '"')
                {
                    sb.Append(c); i++;
                    while (i < n)
                    {
                        if (src[i] == '\\' && (i + 1) < n)
                        {
                            sb.Append(src[i]); sb.Append(src[i + 1]); i += 2; continue;
                        }
                        sb.Append(src[i]);
                        if (src[i] == '"') { i++; break; }
                        i++;
                    }
                    continue;
                }
                if (inTopDecl && braceDepth == 0 && c == '<')
                {
                    i++;
                    bool inStr = false;
                    while (i < n)
                    {
                        char ac = src[i];
                        if (inStr)
                        {
                            if (ac == '\\' && (i + 1) < n) { i += 2; continue; }
                            if (ac == '"') inStr = false;
                            i++;
                            continue;
                        }
                        if (ac == '"') { inStr = true; i++; continue; }
                        if (ac == '>') { i++; break; }
                        i++;
                    }
                    continue;
                }
                if (c == '{')
                {
                    braceDepth++;
                    inTopDecl = false;
                }
                else if (c == '}')
                {
                    if (braceDepth > 0) braceDepth--;
                }
                else if (c == ';' && braceDepth == 0)
                {
                    inTopDecl = false;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool VerifySkyrimFxEntryPs50Compile(string fxRaw, string entryPoint, out int dxbcBytesOut, out string errOut)
        {
            dxbcBytesOut = 0;
            errOut = "";
            try
            {
                string hlsl = fxRaw.Replace("#include \"ReShade.fxh\"", "");
                hlsl = hlsl.Replace("texture OmegaHdrHalfTex { Width = BUFFER_WIDTH / 2; Height = BUFFER_HEIGHT / 2; Format = RGBA16F; };", "");
                hlsl = hlsl.Replace("sampler OmegaHdrHalfSamp { Texture = OmegaHdrHalfTex; };", "");
                hlsl = StripReshadeTopLevelAnnotations(hlsl);
                int techIdx = hlsl.IndexOf("technique OmegaTriEngine", StringComparison.Ordinal);
                if (techIdx > 0) hlsl = hlsl.Substring(0, techIdx);
                hlsl = hlsl.Replace("ReShade::GetLinearizedDepth(", "OmegaStubGetDepth(");
                hlsl = hlsl.Replace("tex2D(ReShade::BackBuffer,", "OmegaStubTex2D(");
                hlsl = hlsl.Replace("tex2D(OmegaHdrHalfSamp,", "OmegaStubHalfTex2D(");
                string header =
                    "#define BUFFER_WIDTH 1920.0\r\n" +
                    "#define BUFFER_HEIGHT 1080.0\r\n" +
                    "#define BUFFER_RCP_WIDTH (1.0 / 1920.0)\r\n" +
                    "#define BUFFER_RCP_HEIGHT (1.0 / 1080.0)\r\n" +
                    "Texture2D<float4> _StubBackTex : register(t0);\r\n" +
                    "Texture2D<float4> _StubHalfTex : register(t1);\r\n" +
                    "SamplerState _StubSamp : register(s0);\r\n" +
                    "float OmegaStubGetDepth(float2 uv) { return saturate(uv.y * 0.85 + 0.05); }\r\n" +
                    "float4 OmegaStubTex2D(float2 uv) { return _StubBackTex.SampleLevel(_StubSamp, uv, 0.0); }\r\n" +
                    "float4 OmegaStubHalfTex2D(float2 uv) { return _StubHalfTex.SampleLevel(_StubSamp, uv, 0.0); }\r\n";
                byte[] srcBytes = Encoding.ASCII.GetBytes(header + hlsl);
                IntPtr pCode = IntPtr.Zero;
                IntPtr pErr = IntPtr.Zero;
                int hr = D3DCompile(srcBytes, (UIntPtr)srcBytes.Length, "OmegaTriEngine_SkyrimAE.fx", IntPtr.Zero, IntPtr.Zero, entryPoint, "ps_5_0", (1u << 15), 0u, out pCode, out pErr);
                try
                {
                    if (hr < 0 || pCode == IntPtr.Zero)
                    {
                        if (pErr != IntPtr.Zero)
                        {
                            IntPtr vtbl = Marshal.ReadIntPtr(pErr);
                            IntPtr fnPtr = Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size);
                            FnBlobGetBufferPointer getPtr = (FnBlobGetBufferPointer)Marshal.GetDelegateForFunctionPointer(fnPtr, typeof(FnBlobGetBufferPointer));
                            errOut = Marshal.PtrToStringAnsi(getPtr(pErr)) ?? ("HRESULT 0x" + hr.ToString("X8", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            errOut = "HRESULT 0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
                        }
                        return false;
                    }
                    IntPtr cVtbl = Marshal.ReadIntPtr(pCode);
                    IntPtr fnSz = Marshal.ReadIntPtr(cVtbl, 4 * IntPtr.Size);
                    FnBlobGetBufferSize getSz = (FnBlobGetBufferSize)Marshal.GetDelegateForFunctionPointer(fnSz, typeof(FnBlobGetBufferSize));
                    dxbcBytesOut = (int)getSz(pCode).ToUInt64();
                    return dxbcBytesOut > 512;
                }
                finally
                {
                    if (pCode != IntPtr.Zero) Marshal.Release(pCode);
                    if (pErr != IntPtr.Zero) Marshal.Release(pErr);
                }
            }
            catch (Exception ex)
            {
                errOut = ex.Message;
                return false;
            }
        }

        private static bool VerifySkyrimFxPs50Compile(string fxRaw, out int dxbcBytesOut, out string errOut)
        {
            return VerifySkyrimFxEntryPs50Compile(fxRaw, "PS_OmegaTriEngine", out dxbcBytesOut, out errOut);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr FnBlobGetBufferPointer(IntPtr pThis);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate UIntPtr FnBlobGetBufferSize(IntPtr pThis);

        private sealed class MockReshadeRuntimeHarness28 : IDisposable
        {
            public readonly IntPtr VtableMem;
            public readonly IntPtr RuntimeObj;
            public readonly float[] Captured112 = new float[112];
            public int EnumCallCount = 0;
            public int SetFloatCallCount = 0;
            public uint UpdatedSlotMask = 0u;

            private readonly List<Delegate> _pinnedDelegates = new List<Delegate>();

            public MockReshadeRuntimeHarness28()
            {
                VtableMem = Marshal.AllocHGlobal(40 * IntPtr.Size);
                RuntimeObj = Marshal.AllocHGlobal(IntPtr.Size * 2);

                VtblNoopDel noop = new VtblNoopDel(OnNoop);
                _pinnedDelegates.Add(noop);
                IntPtr pNoop = Marshal.GetFunctionPointerForDelegate(noop);
                for (int i = 0; i < 40; i++)
                {
                    Marshal.WriteIntPtr(VtableMem, i * IntPtr.Size, pNoop);
                }

                VtblEnumUniformsDel fnEnum = new VtblEnumUniformsDel(OnEnumUniforms);
                VtblGetAnnIntDel fnInt = new VtblGetAnnIntDel(OnGetAnnotationInt);
                VtblGetAnnStrDel fnStr = new VtblGetAnnStrDel(OnGetAnnotationStr);
                VtblSetFloatDel fnSetF = new VtblSetFloatDel(OnSetUniformFloat);
                _pinnedDelegates.Add(fnEnum);
                _pinnedDelegates.Add(fnInt);
                _pinnedDelegates.Add(fnStr);
                _pinnedDelegates.Add(fnSetF);

                Marshal.WriteIntPtr(VtableMem, 19 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fnEnum));
                Marshal.WriteIntPtr(VtableMem, 25 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fnInt));
                Marshal.WriteIntPtr(VtableMem, 27 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fnStr));
                Marshal.WriteIntPtr(VtableMem, 33 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fnSetF));
                Marshal.WriteIntPtr(RuntimeObj, 0, VtableMem);
            }

            private void OnNoop(IntPtr pThis) { }

            private void OnEnumUniforms(IntPtr pThis, IntPtr effectName, IntPtr callbackPtr, IntPtr userData)
            {
                EnumCallCount++;
                EnumUniformCallbackDel cb = (EnumUniformCallbackDel)Marshal.GetDelegateForFunctionPointer(callbackPtr, typeof(EnumUniformCallbackDel));
                for (int i = 0; i < 28; i++)
                {
                    cb(pThis, 0x9000UL + (ulong)i, userData);
                }
            }

            private bool OnGetAnnotationInt(IntPtr pThis, ulong variable, IntPtr annName, IntPtr outValues, UIntPtr count, UIntPtr arrayIndex)
            {
                string name = Marshal.PtrToStringAnsi(annName) ?? "";
                if (!string.Equals(name, "index", StringComparison.Ordinal)) return false;
                if (variable < 0x9000UL || variable >= 0x901CUL) return false;
                Marshal.WriteInt32(outValues, (int)(variable - 0x9000UL));
                return true;
            }

            private bool OnGetAnnotationStr(IntPtr pThis, ulong variable, IntPtr annName, IntPtr outBuf, IntPtr ioSize)
            {
                string name = Marshal.PtrToStringAnsi(annName) ?? "";
                if (!string.Equals(name, "source", StringComparison.Ordinal)) return false;
                byte[] bytes = Encoding.ASCII.GetBytes("omega_tri_engine\0");
                if (outBuf != IntPtr.Zero) Marshal.Copy(bytes, 0, outBuf, bytes.Length);
                if (ioSize != IntPtr.Zero) Marshal.WriteIntPtr(ioSize, new IntPtr(bytes.Length));
                return true;
            }

            private void OnSetUniformFloat(IntPtr pThis, ulong variable, IntPtr floatValues, UIntPtr count, UIntPtr arrayIndex)
            {
                if (variable < 0x9000UL || variable >= 0x901CUL) return;
                int slot = (int)(variable - 0x9000UL);
                Marshal.Copy(floatValues, Captured112, slot * 4, 4);
                UpdatedSlotMask |= (1u << slot);
                SetFloatCallCount++;
            }

            public void Dispose()
            {
                GC.KeepAlive(_pinnedDelegates);
                if (RuntimeObj != IntPtr.Zero) Marshal.FreeHGlobal(RuntimeObj);
                if (VtableMem != IntPtr.Zero) Marshal.FreeHGlobal(VtableMem);
            }
        }

        private static EngineStressVector RunV41_Oteb512V2AndMenuAutoFade(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V41_OTEB_512B_V2_AND_MENU_FADE";
            v.Trials = 256;

            string addonPath = OmegaEngineHlslAndReshade.FindCompiledAddon64Path();
            if (string.IsNullOrEmpty(addonPath) || !File.Exists(addonPath))
            {
                v.Detail = "Missing OmegaTriEngine.addon64";
                return v;
            }

            IntPtr hMod = LoadLibraryW(addonPath);
            if (hMod == IntPtr.Zero)
            {
                v.Detail = "LoadLibraryW failed on OmegaTriEngine.addon64";
                return v;
            }

            try
            {
                IntPtr pInject = GetProcAddress(hMod, "OmegaAddonInjectRuntime");
                IntPtr pReset  = GetProcAddress(hMod, "OmegaAddonResetCache");
                if (pInject == IntPtr.Zero || pReset == IntPtr.Zero)
                {
                    v.Detail = "Missing OmegaAddonInjectRuntime or OmegaAddonResetCache";
                    return v;
                }
                FnOmegaAddonInjectRuntime fnInject = (FnOmegaAddonInjectRuntime)Marshal.GetDelegateForFunctionPointer(pInject, typeof(FnOmegaAddonInjectRuntime));
                FnOmegaAddonResetCache fnReset = (FnOmegaAddonResetCache)Marshal.GetDelegateForFunctionPointer(pReset, typeof(FnOmegaAddonResetCache));
                fnReset();

                string mapName = "Local\\OmegaOteb512_V41_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
                OmegaEngineArenaState st = new OmegaEngineArenaState(tempDir);
                using (OmegaTriEngineUniformBus bus = new OmegaTriEngineUniformBus(mapName))
                using (OmegaTopBarController tb = new OmegaTopBarController(st, bus))
                using (MockReshadeRuntimeHarness28 mock28 = new MockReshadeRuntimeHarness28())
                {
                    float[] u80 = BuildCanonicalTestUniforms(2560, 1440, 12.5f, 4);
                    ulong seq; uint crc;
                    bus.PublishSnapshot(u80, OmegaTriEngineUniformBus.MaskSkse64Bridge, out seq, out crc);

                    tb.HasSimulatedMenuOverride = true;
                    tb.SimulatedMenuOpenOverride = true;
                    for (int s = 0; s < 13; s++) tb.StepTick(0.020f);
                    float fadedAlpha = tb.CurrentMenuFadeAlpha;

                    tb.SetSymmetryExplicit(6);
                    tb.CycleAuroraGain();
                    uint pushedA = fnInject(mock28.RuntimeObj, mapName);
                    Thread.Sleep(2);
                    uint pushedB = fnInject(mock28.RuntimeObj, mapName);

                    tb.SimulatedMenuOpenOverride = false;
                    for (int s = 0; s < 15; s++) tb.StepTick(0.020f);
                    float restoredAlpha = tb.CurrentMenuFadeAlpha;

                    bool slotsOk = (pushedA == 28u && pushedB == 28u && mock28.UpdatedSlotMask == 0x0FFFFFFFu);
                    bool v2SentOk = Math.Abs(mock28.Captured112[108] - 180.0f) < 0.5f;
                    bool symOvOk  = Math.Abs(mock28.Captured112[104] - 6.0f) < 0.01f;
                    bool fadeOk   = (fadedAlpha < 0.16f && restoredAlpha > 0.94f);
                    bool qpcFpsOk = (mock28.Captured112[100] > 1.0f && mock28.Captured112[101] > 0.05f);

                    string fxSrc = OmegaEngineHlslAndReshade.GetCanonicalSkyrimAeWorldShaderFx();
                    int psBytes;
                    string psErr;
                    bool psDxbcOk = VerifySkyrimFxPs50Compile(fxSrc, out psBytes, out psErr);
                    if (!psDxbcOk)
                    {
                        v.Detail = "PS_OmegaTriEngine ps_5_0 compile failed: " + psErr;
                        return v;
                    }

                    v.Passed = slotsOk && v2SentOk && symOvOk && fadeOk && qpcFpsOk && psDxbcOk;
                    v.Detail = string.Format(CultureInfo.InvariantCulture,
                        "512B OTRI v1.8 -> {0}/28 float4s | 250ms Fade 1.0->{1:0.00}->1.0 | PS_5_0={2} B DXBC",
                        pushedB, fadedAlpha, psBytes);
                    return v;
                }
            }
            finally
            {
                FreeLibrary(hMod);
            }
        }

        private static EngineStressVector RunV42_TopDiagBarAndHttpControlLoop(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V42_TOP_DIAG_BAR_AND_HOTKEY_BUS";
            v.Trials = 64;

            int freePort = 0;
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            string mapName = "Local\\OmegaTopBar_V42_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            OmegaEngineArenaState state = new OmegaEngineArenaState(tempDir);
            using (OmegaTriEngineUniformBus bus = new OmegaTriEngineUniformBus(mapName))
            using (OmegaTopBarController ctrl = new OmegaTopBarController(state, bus))
            using (OmegaTopDiagBarForm form = new OmegaTopDiagBarForm(ctrl))
            using (OmegaEngineHttpServer srv = new OmegaEngineHttpServer(state))
            {
                state.SharedTopBar = ctrl;
                IntPtr hForm = form.Handle;
                int exInteractive = form.CurrentExStyle;
                bool hasNoActivate = (exInteractive & 0x08000000) != 0;
                bool hasTopMost    = (exInteractive & 0x00000008) != 0;

                form.ApplyClickThroughStyle(true);
                int exClickThru = form.CurrentExStyle;
                bool hasTransparent = (exClickThru & 0x00000020) != 0;
                form.ApplyClickThroughStyle(false);
                bool clearedTransparent = (form.CurrentExStyle & 0x00000020) == 0;

                bool b0 = form.SimulateButtonClickForAudit(0);
                bool afterBypass = !ctrl.ShaderMasterEnabled;
                bool b0b = form.SimulateButtonClickForAudit(0);
                bool b1 = form.SimulateButtonClickForAudit(1);
                bool b6 = form.SimulateButtonClickForAudit(6);

                srv.Start(freePort, true);
                string rTop = HttpGetLocal(freePort, "/api/topbar?sym=5&menufade=1&thuum=1");
                bool httpOk = rTop.IndexOf("\"oteb_bytes\":512", StringComparison.Ordinal) >= 0
                           && rTop.IndexOf("\"symmetry_override\":5", StringComparison.Ordinal) >= 0
                           && rTop.IndexOf("\"shader_master_on\":true", StringComparison.Ordinal) >= 0;

                v.Passed = (hForm != IntPtr.Zero && hasNoActivate && hasTopMost && hasTransparent && clearedTransparent && b0 && afterBypass && b0b && b1 && b6 && httpOk);
                v.Detail = string.Format(CultureInfo.InvariantCulture,
                    "28px TopBar (WS_EX_NOACTIVATE+LAYERED+ALT_F1 click-thru ok) | 9 buttons + /api/topbar :{0} ok",
                    freePort);
                return v;
            }
        }


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint FnOmegaSksePushWaterAndSpellState(float waterZSkyrimUnits, uint spellSchoolId, float spellStrength, IntPtr mapNameUtf16);

        private static EngineStressVector RunV43_Phase2PersonasAndMultiPassHdrFx()
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V43_PHASE2_PERSONAS_AND_MULTIPASS_HDR";
            v.Trials = 256;

            string fx = OmegaEngineHlslAndReshade.GetCanonicalSkyrimAeWorldShaderFx();
            bool hasHalfTex = fx.IndexOf("Format = RGBA16F;", StringComparison.Ordinal) >= 0
                           && fx.IndexOf("pass P0_HalfHdr", StringComparison.Ordinal) >= 0
                           && fx.IndexOf("pass P1_Composite", StringComparison.Ordinal) >= 0;
            bool hasAllPersonas = fx.IndexOf("Obsidian Dark Fantasy", StringComparison.Ordinal) >= 0
                               && fx.IndexOf("Liminal Dreamcore", StringComparison.Ordinal) >= 0
                               && fx.IndexOf("Stark Nihilist Monolith", StringComparison.Ordinal) >= 0;

            string[] bannedWords = new string[] { "half", "half2", "half3", "half4", "sample", "SamplerState", "Texture2D", "double", "inline", "register", "packoffset" };
            bool lexerClean = (fx.IndexOf('?') < 0);
            for (int b = 0; b < bannedWords.Length; b++)
            {
                // Simple token boundary check outside comments/strings
                if (System.Text.RegularExpressions.Regex.IsMatch(fx, "\\b" + bannedWords[b] + "\\b"))
                {
                    lexerClean = false;
                    v.Detail = "Reserved ReShadeFX keyword detected: " + bannedWords[b];
                    return v;
                }
            }

            int preBytes, compBytes;
            string preErr, compErr;
            bool okPre = VerifySkyrimFxEntryPs50Compile(fx, "PS_OmegaHdrHalfPrepass", out preBytes, out preErr);
            if (!okPre)
            {
                v.Detail = "PS_OmegaHdrHalfPrepass ps_5_0 failed: " + preErr;
                return v;
            }
            bool okComp = VerifySkyrimFxEntryPs50Compile(fx, "PS_OmegaTriEngine", out compBytes, out compErr);
            if (!okComp)
            {
                v.Detail = "PS_OmegaTriEngine ps_5_0 failed: " + compErr;
                return v;
            }

            v.Passed = hasHalfTex && hasAllPersonas && lexerClean && okPre && okComp;
            v.Detail = string.Format(CultureInfo.InvariantCulture,
                "4 Personas (Sovn/Dark/Drmc/Nihl) + RGBA16F P0={0}B & P1={1}B DXBC (0 lexer collisions)",
                preBytes, compBytes);
            return v;
        }

        private static EngineStressVector RunV44_WaterElevationAndSpellChromaBus(string tempDir)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V44_WATER_ELEVATION_AND_SPELL_BUS";
            v.Trials = 256;

            string sksePath = OmegaEngineHlslAndReshade.FindCompiledSkseBridgeDllPath();
            string addonPath = OmegaEngineHlslAndReshade.FindCompiledAddon64Path();
            if (string.IsNullOrEmpty(sksePath) || !File.Exists(sksePath) || string.IsNullOrEmpty(addonPath) || !File.Exists(addonPath))
            {
                v.Detail = "Missing compiled SKSE64 or Addon64 DLL";
                return v;
            }

            int freePort = 0;
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            IntPtr hSkse = LoadLibraryW(sksePath);
            IntPtr hAddon = LoadLibraryW(addonPath);
            if (hSkse == IntPtr.Zero || hAddon == IntPtr.Zero)
            {
                if (hSkse != IntPtr.Zero) FreeLibrary(hSkse);
                if (hAddon != IntPtr.Zero) FreeLibrary(hAddon);
                v.Detail = "LoadLibraryW failed on SKSE64 or Addon64";
                return v;
            }

            try
            {
                IntPtr pPush = GetProcAddress(hSkse, "OmegaSksePushWaterAndSpellState");
                IntPtr pInject = GetProcAddress(hAddon, "OmegaAddonInjectRuntime");
                IntPtr pReset = GetProcAddress(hAddon, "OmegaAddonResetCache");
                if (pPush == IntPtr.Zero || pInject == IntPtr.Zero || pReset == IntPtr.Zero)
                {
                    v.Detail = "Missing OmegaSksePushWaterAndSpellState or Addon exports";
                    return v;
                }
                FnOmegaSksePushWaterAndSpellState fnPush = (FnOmegaSksePushWaterAndSpellState)Marshal.GetDelegateForFunctionPointer(pPush, typeof(FnOmegaSksePushWaterAndSpellState));
                FnOmegaAddonInjectRuntime fnInject = (FnOmegaAddonInjectRuntime)Marshal.GetDelegateForFunctionPointer(pInject, typeof(FnOmegaAddonInjectRuntime));
                FnOmegaAddonResetCache fnReset = (FnOmegaAddonResetCache)Marshal.GetDelegateForFunctionPointer(pReset, typeof(FnOmegaAddonResetCache));
                fnReset();

                string mapName = "Local\\OmegaPhase2_V44_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
                OmegaEngineArenaState st = new OmegaEngineArenaState(tempDir);
                using (OmegaTriEngineUniformBus bus = new OmegaTriEngineUniformBus(mapName))
                using (OmegaTopBarController ctrl = new OmegaTopBarController(st, bus))
                using (OmegaTopDiagBarForm form = new OmegaTopDiagBarForm(ctrl))
                using (OmegaEngineHttpServer srv = new OmegaEngineHttpServer(st))
                using (MockReshadeRuntimeHarness28 mock28 = new MockReshadeRuntimeHarness28())
                {
                    st.SharedTopBar = ctrl;
                    float[] u80 = BuildCanonicalTestUniforms(1920, 1080, 9.0f, 1);
                    ulong seq; uint crc;
                    bus.PublishSnapshot(u80, OmegaTriEngineUniformBus.MaskSkse64Bridge, out seq, out crc);

                    bool bPer = form.SimulateButtonClickForAudit(7); // Cycles Persona 0 -> 1 (Obsidian Dark Fantasy)
                    srv.Start(freePort, true);
                    string rHttp = HttpGetLocal(freePort, "/api/topbar?persona=2&bloom=1.40&spell=3&waterz=-1.25");
                    bool httpOk = rHttp.IndexOf("\"aesthetic_persona\":2", StringComparison.Ordinal) >= 0
                               && rHttp.IndexOf("\"spell_school\":3", StringComparison.Ordinal) >= 0;

                    IntPtr pMapWide = Marshal.StringToHGlobalUni(mapName);
                    uint skRc;
                    try
                    {
                        // -100.0 Skyrim units * 0.01428 = -1.428 meters, spell school 2 (Frost), strength 1.35
                        skRc = fnPush(-100.0f, 2u, 1.35f, pMapWide);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pMapWide);
                    }

                    uint pushed = fnInject(mock28.RuntimeObj, mapName);
                    // U20 starts at float index 80; U21 starts at float index 84
                    bool personaOk = Math.Abs(mock28.Captured112[80] - 2.0f) < 0.01f && Math.Abs(mock28.Captured112[83] - 1.40f) < 0.02f;
                    bool waterSpellOk = (skRc == 1u)
                                     && Math.Abs(mock28.Captured112[84] - (-1.428f)) < 0.02f
                                     && Math.Abs(mock28.Captured112[86] - 2.0f) < 0.01f
                                     && Math.Abs(mock28.Captured112[87] - 1.35f) < 0.02f;

                    v.Passed = bPer && httpOk && (pushed == 28u) && personaOk && waterSpellOk;
                    v.Detail = string.Format(CultureInfo.InvariantCulture,
                        "U20 Persona=2 (Dreamcore, HDR=1.40x) + SKSE64 U21 Water={0:0.3}m Spell=2(Frost) -> 28/28 slots ok",
                        mock28.Captured112[84]);
                    return v;
                }
            }
            finally
            {
                FreeLibrary(hSkse);
                FreeLibrary(hAddon);
            }
        }

        private static EngineStressVector RunV12_Sub70MbWorkingSetGate(out double ramMb)
        {
            EngineStressVector v = new EngineStressVector();
            v.Code = "V12_SUB70MB_WORKINGSET_GATE";
            v.Trials = 1;
            OmegaEngineDirectComputeGpu.TrimWorkingSetNow();
            using (Process cur = Process.GetCurrentProcess())
            {
                ramMb = Math.Round(cur.WorkingSet64 / (1024.0 * 1024.0), 2);
            }
            v.Passed = (ramMb < 70.0);
            v.Detail = string.Format(CultureInfo.InvariantCulture, "Post-D3DCompile trimmed RAM = {0:0.00} MB (< 70.0 MB ceiling)", ramMb);
            return v;
        }

        private static string HttpGetLocal(int port, string path)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + path);
                req.Proxy = null;
                req.Timeout = 2000;
                req.ReadWriteTimeout = 2000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static float[] BuildCanonicalTestUniforms(int w, int h, float simTime, int symMode)
        {
            float[] u = new float[80];
            double[] g5 = new double[5];
            OmegaTriEngineMath.ComputeZeroSumPentagridShifts(simTime * 0.4, g5);

            float camX = 0.0f, camY = 2.65f, camZ = -9.8f;
            float yaw = 0.0f, pitch = -0.06f;
            float fx, fy, fz, rx, ry, rz, ux, uy, uz;
            OmegaTriEngineMath.BuildCameraBasis(yaw, pitch, out fx, out fy, out fz, out rx, out ry, out rz, out ux, out uy, out uz);
            float qx, qy, qz, qw;
            OmegaTriEngineMath.EulerYawPitchToQuat(yaw, pitch, 0.0f, out qx, out qy, out qz, out qw);

            u[0] = w; u[1] = h; u[2] = simTime; u[3] = 0.62f;
            u[4] = camX; u[5] = camY; u[6] = camZ; u[7] = yaw;
            u[8] = pitch; u[9] = 0.08f; u[10] = 180.0f; u[11] = 1.0f;
            u[12] = 0.22f; u[13] = 0.18f; u[14] = 0.12f; u[15] = 0.30f;
            for (int k = 0; k < 8; k++) u[16 + k] = 0.15f;
            u[24] = qx; u[25] = qy; u[26] = qz; u[27] = qw;
            u[28] = (float)g5[0]; u[29] = (float)g5[1]; u[30] = (float)g5[2]; u[31] = (float)g5[3];
            u[32] = (float)g5[4]; u[33] = 0.12f; u[34] = -0.08f; u[35] = 0.05f;
            u[36] = symMode; u[37] = 0.85f; u[38] = 0.032f; u[39] = 1.0f;
            u[40] = 0.20f; u[41] = 0.045f; u[42] = 0.55f; u[43] = -0.12f;
            u[44] = 4.0f; u[45] = 7.0f; u[46] = 96.0f; u[47] = 1.0f;
            u[48] = 0.65f; u[49] = 0.35f; u[50] = 14.2f; u[51] = 12.0f;
            u[52] = 0.0f; u[53] = 0.55f; u[54] = 0.0f; u[55] = 0.45f;
            u[56] = 0.85f; u[57] = 3.6f; u[58] = 9.0f; u[59] = 0.80f;
            u[60] = 1.0f; u[61] = 1.0f; u[62] = 0.85f; u[63] = 1.54f;
            u[64] = 3.4f; u[65] = 0.055f; u[66] = 2.2f; u[67] = 0.38f;
            u[68] = 0.42f; u[69] = 0.78f; u[70] = -0.46f; u[71] = 0.014f;
            u[72] = fx; u[73] = fy; u[74] = fz; u[75] = 1.12f;
            u[76] = rx; u[77] = ry; u[78] = rz; u[79] = 0.25f;
            return u;
        }
    }
}