using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using OmegaCommon;
using OmegaPortableConfig;

namespace OmegaTriEngine
{
    public sealed class OmegaEngineArenaState
    {
        public readonly object Sync = new object();
        public readonly string ExportsDir;
        public readonly float[] U = new float[80];
        public readonly double[] Gamma5 = new double[5];

        public float CamX = 0.0f;
        public float CamY = 2.35f;
        public float CamZ = -10.2f;
        public float CamYaw = 0.0f;
        public float CamPitch = -0.07f;
        public float FovDeg = 64.0f;
        public bool FlyMode = false;

        public int PresetIndex = 0;
        public int SymmetryMode = 0;
        public float TileScale = 0.85f;
        public float K1 = 1.00f;
        public float K2 = 1.00f;
        public float K3 = 0.85f;
        public float K4 = 1.54f;
        public float WeatherAurora = 0.78f;
        public float HdrBloomStrength = 0.32f;
        public float RenderScale = 1.25f;
        public bool RayConePrepassEnabled = true;
        public float CustomMix = 0.25f;
        public string ActiveOmshSnippet = OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet;

        public float ImpulseOrigX = 0.0f;
        public float ImpulseOrigY = 0.85f;
        public float ImpulseOrigZ = 0.0f;
        public float ImpulseAgeSec = 99.0f;
        public float ImpulsePeakStrength = 0.0f;
        public float ImpulseLiveStrength = 0.0f;
        public float ImpulseRadius = 0.0f;
        public float ImpulseFreq = 9.0f;

        public double LiveFps = 60.0;
        public double LiveFrameMs = 16.6;
        public string GpuAdapterName = "DirectCompute cs_5_0";
        public bool GpuCs50Active = false;
        public double InitShaderLoadMs = 0.0;
        public int DxbcCacheHits = 0;
        public int DxbcCacheMisses = 0;
        public int D3DCompileCalls = 0;
        public int DxbcFilesOnDisk = 0;

        public ulong LastSeqlockSeq = 0UL;
        public uint LastPayloadCrc32 = 0u;
        public uint LiveMeshMask = 1u;
        public uint ReshadeAddonFrames = 0u;
        public uint ReshadeAddonApiVersion = 0u;
        public OmegaTopBarController SharedTopBar;
        public Func<string, bool> OnReshadeProbeRequestedFromHttp;
        public Func<string, bool> OnSkseProbeRequestedFromHttp;
        public Func<string, bool, SkyrimModBundleResult> OnBuildSkyrimModRequestedFromHttp;
        public string LastVortexModZipPath = "";
        public float LastSheafH1State = 0.0f;
        public int NearestE8Root = 0;
        public double E8SqnrDb = 0.0;
        public string LastExportedFxPath = "";

        public Action<int> OnPresetRequestedFromHttp;
        public Action<float, float> OnShockwaveRequestedFromHttp;
        public Func<int, int, bool, OmegaEngineDirectComputeGpu.PbrBakeResult> OnBakePbrRequestedFromHttp;
        public OmegaEngineDirectComputeGpu SharedGpuForHttp;
        public Action<string> OnOmshAppliedFromHttp;
        public Func<int, int, int, float, int, TurntableSequenceResult> OnRenderTurntableRequestedFromHttp;
        public string LastExportedObjPath = "";
        public string LastBakedPbrPrefix = "";
        public readonly ArenaKeyframeTrack KeyframeTrack = new ArenaKeyframeTrack();
        public bool SplinePlaybackActive = false;
        public double SplinePlaybackCursorSec = 0.0;
        public string LastAiPrompt = "";
        public string LastAiModelSource = "";
        public bool LastAiFxcValid = true;
        public int LastAiAttempts = 0;
        public string LastTurntableDir = "";
        public int LastTurntableFrames = 0;

        public OmegaEngineArenaState(string exportsDir)
        {
            ExportsDir = exportsDir;
            Directory.CreateDirectory(ExportsDir);
        }

        public void TriggerShockwave(float strength, float freqHz)
        {
            lock (Sync)
            {
                float fx, fy, fz, rx, ry, rz, ux, uy, uz;
                OmegaTriEngineMath.BuildCameraBasis(CamYaw, CamPitch, out fx, out fy, out fz, out rx, out ry, out rz, out ux, out uy, out uz);
                ImpulseOrigX = CamX + fx * 1.6f;
                ImpulseOrigY = Math.Max(0.2f, CamY - 0.25f);
                ImpulseOrigZ = CamZ + fz * 1.6f;
                ImpulseAgeSec = 0.0f;
                ImpulsePeakStrength = Math.Max(0.25f, Math.Min(2.0f, strength));
                ImpulseLiveStrength = ImpulsePeakStrength;
                ImpulseRadius = 0.35f;
                ImpulseFreq = Math.Max(3.0f, Math.Min(24.0f, freqHz));
            }
        }

        public void ApplyPreset(int idx)
        {
            ArenaShaderPreset[] list = OmegaEngineHlslAndReshade.BuiltInPresets;
            int clamped = Math.Max(0, Math.Min(list.Length - 1, idx));
            ArenaShaderPreset p = list[clamped];
            lock (Sync)
            {
                PresetIndex = clamped;
                SymmetryMode = p.SymmetryMode;
                K1 = p.K1; K2 = p.K2; K3 = p.K3; K4 = p.K4;
                WeatherAurora = p.WeatherAurora;
                CustomMix = p.CustomMix;
                ActiveOmshSnippet = p.CustomOmshSnippet;
            }
        }

        public string BuildStatusJson(bool extraShockFlag)
        {
            lock (Sync)
            {
                double ramMb = 0.0;
                try { using (Process p = Process.GetCurrentProcess()) ramMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 2); } catch { }
                StringBuilder sb = new StringBuilder(1280);
                sb.Append("{");
                sb.Append("\"ok\":true,");
                sb.Append("\"station\":15,");
                sb.Append("\"service\":\"OmegaEngine v1.9.0\",");
                sb.Append("\"suite_version\":\"3.7.0\",");
                sb.Append("\"port\":19795,");
                if (extraShockFlag) sb.Append("\"shockwave_triggered\":true,");
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"fps\":{0:0.0},\"frame_ms\":{1:0.00},\"ram_mb\":{2:0.00},", LiveFps, LiveFrameMs, ramMb);
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"cs_5_0_live\":{0},\"gpu\":\"{1}\",", GpuCs50Active ? "true" : "false", EscJson(GpuAdapterName));
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"hdr_fp16\":true,\"dual_kawase_bloom\":{0:0.00},\"render_scale\":{1:0.00},\"ray_cone_prepass\":{2},",
                    HdrBloomStrength, RenderScale, RayConePrepassEnabled ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"init_shader_ms\":{0:0.000},\"dxbc_cache_hits\":{1},\"dxbc_cache_misses\":{2},\"d3dcompile_calls\":{3},\"dxbc_files\":{4},",
                    InitShaderLoadMs, DxbcCacheHits, DxbcCacheMisses, D3DCompileCalls, DxbcFilesOnDisk);
                bool rsLiveNow = (LiveMeshMask & OmegaTriEngineUniformBus.MaskReshadeAddon) != 0u;
                bool skLiveNow = (LiveMeshMask & OmegaTriEngineUniformBus.MaskSkse64Bridge) != 0u;
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"mmf\":\"Local\\\\OmegaTriEngineUniforms\",\"seqlock_seq\":{0},\"payload_crc32\":\"0x{1:X8}\",\"mesh_mask\":{2},\"reshade_live\":{3},\"skse64_live\":{4},\"sheaf_h1_state\":{5:0.0},\"reshade_frames\":{6},\"reshade_api_version\":{7},", LastSeqlockSeq, LastPayloadCrc32, LiveMeshMask, rsLiveNow ? "true" : "false", skLiveNow ? "true" : "false", LastSheafH1State, ReshadeAddonFrames, ReshadeAddonApiVersion);
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"symmetry_mode\":{0},\"preset_index\":{1},\"fly_mode\":{2},", SymmetryMode, PresetIndex, FlyMode ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"camera_xyz\":[{0:0.000},{1:0.000},{2:0.000}],\"yaw_pitch\":[{3:0.000},{4:0.000}],", CamX, CamY, CamZ, CamYaw, CamPitch);
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"audio_bmhp\":[{0:0.000},{1:0.000},{2:0.000},{3:0.000}],\"nearest_e8_root\":{4},", U[12], U[13], U[14], U[15], NearestE8Root);
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"shockwave_radius\":{0:0.00},\"shockwave_strength\":{1:0.000},", ImpulseRadius, ImpulseLiveStrength);
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"keyframes\":{0},\"spline_playing\":{1},\"last_ai_source\":\"{2}\",\"last_turntable_dir\":\"{3}\",",
                    KeyframeTrack.Count, SplinePlaybackActive ? "true" : "false", EscJson(LastAiModelSource), EscJson(LastTurntableDir));
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"last_exported_fx\":\"{0}\"", EscJson(LastExportedFxPath));
                sb.Append("}");
                return sb.ToString();
            }
        }

        public static string EscJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
    }

    public sealed class OmegaEngineHttpServer : IDisposable
    {
        private readonly OmegaEngineArenaState _state;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        public OmegaEngineHttpServer(OmegaEngineArenaState state)
        {
            _state = state;
        }

        public void Start(int port, bool loopbackOnly)
        {
            if (_running) return;
            _running = true;
            IPAddress bindIp = loopbackOnly ? IPAddress.Loopback : IPAddress.Any;
            _listener = new TcpListener(bindIp, port);
            _listener.Start();
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient cli = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, cli);
                }
                catch
                {
                    if (!_running) break;
                    Thread.Sleep(25);
                }
            }
        }

        private void HandleClient(object obj)
        {
            TcpClient cli = (TcpClient)obj;
            try
            {
                cli.ReceiveTimeout = 2000;
                cli.SendTimeout = 2000;
                using (NetworkStream ns = cli.GetStream())
                {
                    byte[] buf = new byte[16384];
                    int r = ns.Read(buf, 0, buf.Length);
                    if (r <= 0) return;
                    string req = Encoding.UTF8.GetString(buf, 0, r);
                    int hdrEnd = req.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    string reqBody = (hdrEnd >= 0 && hdrEnd + 4 <= req.Length) ? req.Substring(hdrEnd + 4) : "";
                    Match mCl = Regex.Match(hdrEnd >= 0 ? req.Substring(0, hdrEnd) : req, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
                    int expectedBytes = 0;
                    if (mCl.Success && int.TryParse(mCl.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedBytes))
                    {
                        int haveBytes = Encoding.UTF8.GetByteCount(reqBody);
                        while (haveBytes < expectedBytes && haveBytes < 65536)
                        {
                            int more = ns.Read(buf, 0, Math.Min(buf.Length, expectedBytes - haveBytes));
                            if (more <= 0) break;
                            reqBody += Encoding.UTF8.GetString(buf, 0, more);
                            haveBytes += more;
                        }
                    }
                    int s1 = req.IndexOf(' ');
                    int s2 = (s1 >= 0) ? req.IndexOf(' ', s1 + 1) : -1;
                    string rawUrl = (s1 >= 0 && s2 > s1) ? req.Substring(s1 + 1, s2 - s1 - 1) : "/";
                    string path = rawUrl;
                    string query = "";
                    int qIdx = rawUrl.IndexOf('?');
                    if (qIdx >= 0)
                    {
                        path = rawUrl.Substring(0, qIdx);
                        query = rawUrl.Substring(qIdx + 1);
                    }

                    if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteHttp(ns, "application/json; charset=utf-8", "{\"ok\":true,\"station\":15,\"service\":\"OmegaEngine v1.9.0\",\"suite_version\":\"3.7.0\",\"port\":19795}");
                        return;
                    }
                    if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteHttp(ns, "application/json; charset=utf-8", _state.BuildStatusJson(false));
                        return;
                    }
                    if (path.Equals("/api/topbar", StringComparison.OrdinalIgnoreCase))
                    {
                        OmegaTopBarController tb = _state.SharedTopBar;
                        bool ownTb = false;
                        if (tb == null)
                        {
                            tb = new OmegaTopBarController(_state, new OmegaTriEngineUniformBus(OmegaTriEngineUniformBus.DefaultMapName));
                            ownTb = true;
                        }
                        try
                        {
                            float qShow = ParseQueryFloat(query, "show", -1.0f);
                            if (qShow >= 0.0f && !ownTb)
                            {
                                tb.ShowDesktopBar(qShow > 0.5f);
                            }
                            float qMaster = ParseQueryFloat(query, "master", -1.0f);
                            if (qMaster >= 0.0f)
                            {
                                tb.ShaderMasterEnabled = (qMaster > 0.5f);
                            }
                            float qHud = ParseQueryFloat(query, "hud", -1.0f);
                            if (qHud >= 0.0f)
                            {
                                tb.InShaderHudEnabled = (qHud > 0.5f);
                            }
                            float qFade = ParseQueryFloat(query, "menufade", -1.0f);
                            if (qFade >= 0.0f)
                            {
                                tb.AutoMenuFadeEnabled = (qFade > 0.5f);
                            }
                            float qSym = ParseQueryFloat(query, "sym", -99.0f);
                            if (qSym >= -0.5f && qSym <= 7.5f)
                            {
                                tb.SetSymmetryExplicit((int)Math.Round(qSym));
                            }
                            float qPer = ParseQueryFloat(query, "persona", -99.0f);
                            if (qPer >= -0.25f && qPer <= 3.5f)
                            {
                                tb.SetPersonaExplicit((int)Math.Round(qPer));
                            }
                            float qBloom = ParseQueryFloat(query, "bloom", -1.0f);
                            if (qBloom >= 0.05f && qBloom <= 3.0f)
                            {
                                tb.HdrBloomMultiplier = qBloom;
                            }
                            float qWaterZ = ParseQueryFloat(query, "waterz", -9999.0f);
                            if (qWaterZ > -1000.0f && qWaterZ < 1000.0f)
                            {
                                tb.WaterElevationMeters = qWaterZ;
                            }
                            float qSpell = ParseQueryFloat(query, "spell", -99.0f);
                            if (qSpell >= -0.25f && qSpell <= 5.5f)
                            {
                                int spId = (int)Math.Round(qSpell);
                                tb.TriggerSpellPulseExplicit(spId, (spId > 0) ? 1.25f : 0.0f);
                            }
                            float qThuum = ParseQueryFloat(query, "thuum", -1.0f);
                            bool fireTh = (qThuum > 0.5f);
                            tb.PushControlsToBus(fireTh);
                            WriteHttp(ns, "application/json; charset=utf-8", tb.BuildTopBarJson());
                            return;
                        }
                        finally
                        {
                            if (ownTb) tb.Dispose();
                        }
                    }
                    if (path.Equals("/api/shader_cache", StringComparison.OrdinalIgnoreCase))
                    {
                        string cacheDir = OmegaEngineDirectComputeGpu.ResolveDefaultShaderCacheDir();
                        int files = 0;
                        try { if (Directory.Exists(cacheDir)) files = Directory.GetFiles(cacheDir, "*.dxbc").Length; } catch { }
                        double initMs; int hits, misses, fxcCalls;
                        lock (_state.Sync)
                        {
                            initMs = _state.InitShaderLoadMs;
                            hits = _state.DxbcCacheHits;
                            misses = _state.DxbcCacheMisses;
                            fxcCalls = _state.D3DCompileCalls;
                        }
                        string cj = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":true,\"cache_dir\":\"{0}\",\"dxbc_files\":{1},\"init_shader_ms\":{2:0.000},\"dxbc_cache_hits\":{3},\"dxbc_cache_misses\":{4},\"d3dcompile_calls\":{5}}}",
                            OmegaEngineArenaState.EscJson(cacheDir), files, initMs, hits, misses, fxcCalls);
                        WriteHttp(ns, "application/json; charset=utf-8", cj);
                        return;
                    }
                    if (path.Equals("/api/shockwave", StringComparison.OrdinalIgnoreCase))
                    {
                        float str = ParseQueryFloat(query, "strength", 1.30f);
                        float frq = ParseQueryFloat(query, "freq", 9.5f);
                        _state.TriggerShockwave(str, frq);
                        if (_state.OnShockwaveRequestedFromHttp != null) _state.OnShockwaveRequestedFromHttp(str, frq);
                        WriteHttp(ns, "application/json; charset=utf-8", _state.BuildStatusJson(true));
                        return;
                    }
                    if (path.Equals("/api/preset", StringComparison.OrdinalIgnoreCase))
                    {
                        int symOnly = (int)ParseQueryFloat(query, "sym", -1.0f);
                        int presetId = (int)ParseQueryFloat(query, "id", -1.0f);
                        if (presetId >= 0)
                        {
                            _state.ApplyPreset(presetId);
                            if (_state.OnPresetRequestedFromHttp != null) _state.OnPresetRequestedFromHttp(presetId);
                        }
                        if (symOnly >= 0 && symOnly <= 7)
                        {
                            lock (_state.Sync) { _state.SymmetryMode = symOnly; }
                        }
                        WriteHttp(ns, "application/json; charset=utf-8", _state.BuildStatusJson(false));
                        return;
                    }
                    if (path.Equals("/api/export_obj", StringComparison.OrdinalIgnoreCase))
                    {
                        float k1, k2; string outDir;
                        lock (_state.Sync) { k1 = _state.K1; k2 = _state.K2; outDir = _state.ExportsDir; }
                        int vc, fc;
                        string objPath = OmegaTriEngineMath.ExportWavefrontObjArena6D(outDir, k1, k2, out vc, out fc);
                        string jsonObj = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":true,\"obj_path\":\"{0}\",\"vertices\":{1},\"faces\":{2}}}",
                            OmegaEngineArenaState.EscJson(objPath), vc, fc);
                        WriteHttp(ns, "application/json; charset=utf-8", jsonObj);
                        return;
                    }
                    if (path.Equals("/api/bake_dc_obj", StringComparison.OrdinalIgnoreCase))
                    {
                        int resGrid = (int)ParseQueryFloat(query, "res", 64.0f);
                        int symReq = (int)ParseQueryFloat(query, "sym", -1.0f);
                        float k1, k2, k3, qx, qy, qz, qw;
                        int symCur;
                        string outDir;
                        lock (_state.Sync)
                        {
                            k1 = _state.K1; k2 = _state.K2; k3 = _state.K3;
                            qx = _state.U[24]; qy = _state.U[25]; qz = _state.U[26]; qw = (Math.Abs(_state.U[27]) > 0.01f) ? _state.U[27] : 1.0f;
                            symCur = (symReq >= 0 && symReq <= 7) ? symReq : _state.SymmetryMode;
                            outDir = _state.ExportsDir;
                        }
                        OmegaTriEngineMath.DualContourBakeResult dc = OmegaTriEngineMath.BakeHermiteDualContourObj(
                            outDir, resGrid, symCur, k1, k2, k3, qx, qy, qz, qw, null);
                        lock (_state.Sync) { _state.LastExportedObjPath = dc.ObjPath; }
                        string dcJson = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":{0},\"obj_path\":\"{1}\",\"mtl_path\":\"{2}\",\"grid_res\":{3},\"symmetry_mode\":{4},\"vertices\":{5},\"triangles\":{6},\"sharp_creases\":{7},\"boundary_edges\":{8},\"non_manifold_edges\":{9},\"qef_gain_ratio\":{10:0.00},\"elapsed_ms\":{11:0.0}}}",
                            dc.Ok ? "true" : "false",
                            OmegaEngineArenaState.EscJson(dc.ObjPath),
                            OmegaEngineArenaState.EscJson(dc.MtlPath),
                            dc.GridRes, dc.SymmetryMode, dc.VertexCount, dc.TriangleCount,
                            dc.SharpCreaseCount, dc.BoundaryEdges, dc.NonManifoldEdges,
                            dc.QefCreaseGainRatio, dc.ElapsedMs);
                        WriteHttp(ns, "application/json; charset=utf-8", dcJson);
                        return;
                    }
                    if (path.Equals("/api/bake_pbr", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/forge_bundle", StringComparison.OrdinalIgnoreCase))
                    {
                        bool alsoDc = path.Equals("/api/forge_bundle", StringComparison.OrdinalIgnoreCase);
                        int texRes = (int)ParseQueryFloat(query, "res", ParseQueryFloat(query, "tex", 2048.0f));
                        int gridRes = (int)ParseQueryFloat(query, "grid", 64.0f);
                        int symReq = (int)ParseQueryFloat(query, "sym", -1.0f);
                        bool dxFlip = ((int)ParseQueryFloat(query, "dx", 0.0f)) != 0;
                        float k1, k2, k3, qx, qy, qz, qw;
                        int symCur;
                        string outDir;
                        lock (_state.Sync)
                        {
                            k1 = _state.K1; k2 = _state.K2; k3 = _state.K3;
                            qx = _state.U[24]; qy = _state.U[25]; qz = _state.U[26]; qw = (Math.Abs(_state.U[27]) > 0.01f) ? _state.U[27] : 1.0f;
                            symCur = (symReq >= 0 && symReq <= 7) ? symReq : _state.SymmetryMode;
                            outDir = _state.ExportsDir;
                        }
                        OmegaEngineDirectComputeGpu.PbrBakeResult pbr;
                        if (_state.OnBakePbrRequestedFromHttp != null)
                        {
                            pbr = _state.OnBakePbrRequestedFromHttp(texRes, symCur, dxFlip);
                        }
                        else
                        {
                            using (OmegaEngineDirectComputeGpu tmpGpu = new OmegaEngineDirectComputeGpu())
                            {
                                tmpGpu.Initialize(IntPtr.Zero, 128, 72, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
                                pbr = tmpGpu.BakePbrMaterialSet(outDir, texRes, symCur, dxFlip, null);
                            }
                        }
                        string objOut = "";
                        int dcVerts = 0, dcTris = 0;
                        if (alsoDc)
                        {
                            OmegaTriEngineMath.DualContourBakeResult dc = OmegaTriEngineMath.BakeHermiteDualContourObj(
                                outDir, gridRes, symCur, k1, k2, k3, qx, qy, qz, qw, pbr.Prefix);
                            objOut = dc.ObjPath;
                            dcVerts = dc.VertexCount;
                            dcTris = dc.TriangleCount;
                        }
                        string pbrJson = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":{0},\"tex_size\":{1},\"symmetry_mode\":{2},\"albedo_png\":\"{3}\",\"normal_png\":\"{4}\",\"height_png\":\"{5}\",\"emissive_png\":\"{6}\",\"mtl_path\":\"{7}\",\"obj_path\":\"{8}\",\"dc_vertices\":{9},\"dc_triangles\":{10},\"gpu_dispatch_ms\":{11:0.00},\"total_ms\":{12:0.0}}}",
                            pbr.Ok ? "true" : "false", pbr.TexSize, pbr.SymmetryMode,
                            OmegaEngineArenaState.EscJson(pbr.AlbedoPath),
                            OmegaEngineArenaState.EscJson(pbr.NormalPath),
                            OmegaEngineArenaState.EscJson(pbr.HeightPath),
                            OmegaEngineArenaState.EscJson(pbr.EmissivePath),
                            OmegaEngineArenaState.EscJson(pbr.MtlPath),
                            OmegaEngineArenaState.EscJson(objOut),
                            dcVerts, dcTris, pbr.GpuDispatchMs, pbr.TotalMs);
                        WriteHttp(ns, "application/json; charset=utf-8", pbrJson);
                        return;
                    }
                    if (path.Equals("/api/mesh", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/reshade_bus", StringComparison.OrdinalIgnoreCase))
                    {
                        float[] busSnap = new float[80];
                        ulong bSeq; uint bMask, bCrc, bFrm, bApi;
                        bool okBus = OmegaTriEngineUniformBus.TryReadSnapshotWithAddon(
                            OmegaTriEngineUniformBus.DefaultMapName, busSnap, out bSeq, out bMask, out bCrc, out bFrm, out bApi);
                        string addonFile = OmegaEngineHlslAndReshade.FindCompiledAddon64Path();
                        List<string> impList, expList; long aBytes; string peErr;
                        bool peOk = OmegaEngineHlslAndReshade.InspectPe32PlusZeroCrt(addonFile, out impList, out expList, out aBytes, out peErr);
                        bool zeroCrt = peOk && impList.Count == 1 && string.Equals(impList[0], "KERNEL32.dll", StringComparison.OrdinalIgnoreCase);
                        string impJoined = (impList != null && impList.Count > 0) ? string.Join(",", impList.ToArray()) : "";
                        string meshJson = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":{0},\"seq\":{1},\"active_mask\":{2},\"crc32\":\"0x{3:X8}\",\"reshade_addon_frames\":{4},\"reshade_api_version\":{5},\"engine\":true,\"synth\":{6},\"penrose\":{7},\"av\":{8},\"tensor\":{9},\"pixel10_live\":{10},\"pixel8_live\":{11},\"cluster_tops\":{12:0.000},\"reshade_live\":{13},\"addon64_zero_crt\":{14},\"addon64_imports\":\"{15}\",\"addon64_bytes\":{16},\"u0_time\":{17:0.000},\"u3_bass\":{18:0.000},\"u9_sym\":{19:0.0}}}",
                            okBus ? "true" : "false", bSeq, bMask, bCrc, bFrm, bApi,
                            ((bMask & OmegaTriEngineUniformBus.MaskOmegaSynth) != 0u) ? "true" : "false",
                            ((bMask & OmegaTriEngineUniformBus.MaskOmegaPenrose) != 0u) ? "true" : "false",
                            ((bMask & OmegaTriEngineUniformBus.MaskOmegaAv) != 0u) ? "true" : "false",
                            ((bMask & OmegaTriEngineUniformBus.MaskOmegaTensor) != 0u) ? "true" : "false",
                            ((bMask & OmegaTriEngineUniformBus.MaskPixel10Live) != 0u) ? "true" : "false",
                            ((bMask & OmegaTriEngineUniformBus.MaskPixel8Live) != 0u) ? "true" : "false",
                            busSnap[35],
                            ((bMask & OmegaTriEngineUniformBus.MaskReshadeAddon) != 0u) ? "true" : "false",
                            zeroCrt ? "true" : "false",
                            OmegaEngineArenaState.EscJson(impJoined),
                            aBytes,
                            busSnap[2], busSnap[12], busSnap[36]);
                        WriteHttp(ns, "application/json; charset=utf-8", meshJson);
                        return;
                    }
                    if (path.Equals("/api/skse64", StringComparison.OrdinalIgnoreCase))
                    {
                        float[] busSnap = new float[80];
                        ulong bSeq; uint bMask, bCrc, bFrm, bApi;
                        bool okBus = OmegaTriEngineUniformBus.TryReadSnapshotWithAddon(
                            OmegaTriEngineUniformBus.DefaultMapName, busSnap, out bSeq, out bMask, out bCrc, out bFrm, out bApi);
                        string skDll = OmegaEngineHlslAndReshade.FindCompiledSkseBridgeDllPath();
                        List<string> impList, expList; long skBytes; string peErr;
                        bool peOk = OmegaEngineHlslAndReshade.InspectPe32PlusZeroCrt(skDll, out impList, out expList, out skBytes, out peErr);
                        bool zeroCrt = peOk && impList.Count == 1 && string.Equals(impList[0], "KERNEL32.dll", StringComparison.OrdinalIgnoreCase);
                        SkyrimInstallInfo sky = OmegaEngineSkseModBuilder.DiscoverSkyrimAeInstallation("");
                        string skJson = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":{0},\"skse64_live\":{1},\"active_mask\":{2},\"skse_dll_zero_crt\":{3},\"skse_dll_bytes\":{4},\"skyrim_found\":{5},\"skyrim_dir\":\"{6}\",\"skyrim_version\":\"{7}\",\"packed_runtime\":\"0x{8:X8}\",\"has_skse_loader\":{9},\"has_creation_kit\":{10},\"weather_aurora\":{11:0.000},\"thuum_strength\":{12:0.000},\"thuum_radius\":{13:0.00},\"sheaf_h1_state\":{14:0.0}}}",
                            okBus ? "true" : "false",
                            ((bMask & OmegaTriEngineUniformBus.MaskSkse64Bridge) != 0u) ? "true" : "false",
                            bMask,
                            zeroCrt ? "true" : "false",
                            skBytes,
                            sky.Found ? "true" : "false",
                            OmegaEngineArenaState.EscJson(sky.GameDir),
                            OmegaEngineArenaState.EscJson(sky.SkyrimVersionString),
                            sky.PackedSkseRuntimeVersion,
                            sky.HasSkseLoader ? "true" : "false",
                            sky.HasCreationKit ? "true" : "false",
                            busSnap[59], busSnap[56], busSnap[57], busSnap[67]);
                        WriteHttp(ns, "application/json; charset=utf-8", skJson);
                        return;
                    }
                    if (path.Equals("/api/skse_probe", StringComparison.OrdinalIgnoreCase))
                    {
                        string mode = ParseQueryString(query, "enable");
                        if (string.IsNullOrEmpty(mode)) mode = "toggle";
                        bool activeNow = (_state.OnSkseProbeRequestedFromHttp != null) && _state.OnSkseProbeRequestedFromHttp(mode);
                        Thread.Sleep(60);
                        float[] busSnap = new float[80];
                        ulong bSeq; uint bMask, bCrc, bFrm, bApi;
                        OmegaTriEngineUniformBus.TryReadSnapshotWithAddon(
                            OmegaTriEngineUniformBus.DefaultMapName, busSnap, out bSeq, out bMask, out bCrc, out bFrm, out bApi);
                        string prJson = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":true,\"probe_process_running\":{0},\"skse64_live\":{1},\"active_mask\":{2},\"weather_aurora\":{3:0.000},\"sheaf_h1_state\":{4:0.0}}}",
                            activeNow ? "true" : "false",
                            ((bMask & OmegaTriEngineUniformBus.MaskSkse64Bridge) != 0u) ? "true" : "false",
                            bMask, busSnap[59], busSnap[67]);
                        WriteHttp(ns, "application/json; charset=utf-8", prJson);
                        return;
                    }
                    if (path.Equals("/api/skyrim_mod", StringComparison.OrdinalIgnoreCase))
                    {
                        string gDir = ParseQueryString(query, "game_dir");
                        bool syncG = string.Equals(ParseQueryString(query, "sync"), "1", StringComparison.OrdinalIgnoreCase);
                        SkyrimModBundleResult mb = (_state.OnBuildSkyrimModRequestedFromHttp != null)
                            ? _state.OnBuildSkyrimModRequestedFromHttp(gDir, syncG)
                            : new SkyrimModBundleResult();
                        string modJson = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":{0},\"vortex_zip\":\"{1}\",\"esp_path\":\"{2}\",\"esp_bytes\":{3},\"kywd_count\":{4},\"pex_compiled_ck\":{5},\"detail\":\"{6}\"}}",
                            mb.Ok ? "true" : "false",
                            OmegaEngineArenaState.EscJson(mb.VortexZipPath),
                            OmegaEngineArenaState.EscJson(mb.EspPath),
                            mb.EspBytes, mb.EspKeywordCount,
                            mb.PexCompiledViaCk ? "true" : "false",
                            OmegaEngineArenaState.EscJson(mb.Detail));
                        WriteHttp(ns, "application/json; charset=utf-8", modJson);
                        return;
                    }
                    if (path.Equals("/api/reshade_probe", StringComparison.OrdinalIgnoreCase))
                    {
                        string mode = ParseQueryString(query, "enable");
                        if (string.IsNullOrEmpty(mode)) mode = "toggle";
                        bool activeNow = false;
                        if (_state.OnReshadeProbeRequestedFromHttp != null)
                        {
                            activeNow = _state.OnReshadeProbeRequestedFromHttp(mode);
                        }
                        Thread.Sleep(60);
                        float[] busSnap = new float[80];
                        ulong bSeq; uint bMask, bCrc, bFrm, bApi;
                        OmegaTriEngineUniformBus.TryReadSnapshotWithAddon(
                            OmegaTriEngineUniformBus.DefaultMapName, busSnap, out bSeq, out bMask, out bCrc, out bFrm, out bApi);
                        string pj = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":true,\"probe_process_running\":{0},\"reshade_live\":{1},\"reshade_addon_frames\":{2},\"reshade_api_version\":{3}}}",
                            activeNow ? "true" : "false",
                            ((bMask & OmegaTriEngineUniformBus.MaskReshadeAddon) != 0u) ? "true" : "false",
                            bFrm, bApi);
                        WriteHttp(ns, "application/json; charset=utf-8", pj);
                        return;
                    }
                    if (path.Equals("/api/deploy_game", StringComparison.OrdinalIgnoreCase))
                    {
                        string gameDir = ParseQueryString(query, "dir");
                        float[] snapD = new float[80];
                        string snipD;
                        lock (_state.Sync) { Array.Copy(_state.U, snapD, 80); snipD = _state.ActiveOmshSnippet; }
                        string msgD;
                        bool okD = OmegaEngineHlslAndReshade.DeployBridgeToGameDirectory(gameDir, snapD, snipD, out msgD);
                        string dj = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":{0},\"detail\":\"{1}\"}}",
                            okD ? "true" : "false", OmegaEngineArenaState.EscJson(msgD));
                        WriteHttp(ns, "application/json; charset=utf-8", dj);
                        return;
                    }
                    if (path.Equals("/api/ai_omsh", StringComparison.OrdinalIgnoreCase))
                    {
                        string prompt = ParseQueryString(query, "prompt");
                        if (string.IsNullOrEmpty(prompt) && !string.IsNullOrEmpty(reqBody))
                        {
                            Match mp = Regex.Match(reqBody, "\"prompt\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                            prompt = mp.Success ? OmegaEngineOmshCoPilot.UnescapeJsonString(mp.Groups[1].Value) : reqBody.Trim();
                        }
                        string endpoint = ParseQueryString(query, "endpoint");
                        int symReq = (int)ParseQueryFloat(query, "sym", -1.0f);
                        int activeSym;
                        lock (_state.Sync)
                        {
                            activeSym = (symReq >= 0 && symReq <= 7) ? symReq : _state.SymmetryMode;
                        }
                        OmshCoPilotResult cp = OmegaEngineOmshCoPilot.QueryCoPilotWithAutoFxc(_state.SharedGpuForHttp, prompt, activeSym, endpoint, null);
                        if (cp.Ok && !string.IsNullOrEmpty(cp.OmshCode))
                        {
                            lock (_state.Sync)
                            {
                                _state.ActiveOmshSnippet = cp.OmshCode;
                                if (_state.CustomMix < 0.48f) _state.CustomMix = 0.48f;
                                _state.LastAiPrompt = cp.Prompt;
                                _state.LastAiModelSource = cp.ModelSource;
                                _state.LastAiFxcValid = cp.FxcValidated;
                                _state.LastAiAttempts = cp.Attempts;
                            }
                            if (_state.OnOmshAppliedFromHttp != null) _state.OnOmshAppliedFromHttp(cp.OmshCode);
                        }
                        string aiJson = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":{0},\"fxc_valid\":{1},\"repaired\":{2},\"deterministic_fallback\":{3},\"attempts\":{4},\"fxc_ms\":{5:0.00},\"total_ms\":{6:0.0},\"source\":\"{7}\",\"omsh\":\"{8}\"}}",
                            cp.Ok ? "true" : "false",
                            cp.FxcValidated ? "true" : "false",
                            cp.RepairedAfterFxcError ? "true" : "false",
                            cp.UsedLocalDeterministicCompiler ? "true" : "false",
                            cp.Attempts,
                            cp.FxcValidateMs,
                            cp.TotalMs,
                            OmegaEngineOmshCoPilot.EscapeJson(cp.ModelSource),
                            OmegaEngineOmshCoPilot.EscapeJson(cp.OmshCode));
                        WriteHttp(ns, "application/json; charset=utf-8", aiJson);
                        return;
                    }
                    if (path.Equals("/api/keyframes", StringComparison.OrdinalIgnoreCase))
                    {
                        string act = ParseQueryString(query, "action");
                        if (string.IsNullOrEmpty(act)) act = "status";
                        string trackFile = "";
                        if (act.Equals("clear", StringComparison.OrdinalIgnoreCase))
                        {
                            _state.KeyframeTrack.Clear();
                            lock (_state.Sync) { _state.SplinePlaybackActive = false; }
                        }
                        else if (act.Equals("add", StringComparison.OrdinalIgnoreCase))
                        {
                            float dtStep = ParseQueryFloat(query, "dt", 1.5f);
                            _state.KeyframeTrack.RecordFromArenaState(_state, dtStep);
                        }
                        else if (act.Equals("orbit8", StringComparison.OrdinalIgnoreCase))
                        {
                            float rad = ParseQueryFloat(query, "radius", 9.6f);
                            float dur = ParseQueryFloat(query, "dur", 6.0f);
                            int sym; float k1, k2, k3, k4, aur, blm, cmx;
                            lock (_state.Sync)
                            {
                                sym = _state.SymmetryMode;
                                k1 = _state.K1; k2 = _state.K2; k3 = _state.K3; k4 = _state.K4;
                                aur = _state.WeatherAurora; blm = _state.HdrBloomStrength; cmx = _state.CustomMix;
                            }
                            ArenaKeyframeTrack orb = ArenaKeyframeTrack.BuildCanonicalTurntableTrack(8, rad, 2.65f, dur, sym, k1, k2, k3, k4, aur, blm, cmx);
                            _state.KeyframeTrack.Clear();
                            ArenaSplineKeyframe[] ks = orb.GetKnotsSnapshot();
                            for (int i = 0; i < ks.Length; i++) _state.KeyframeTrack.AddKeyframe(ks[i]);
                            _state.KeyframeTrack.LoopClosed = true;
                        }
                        else if (act.Equals("play", StringComparison.OrdinalIgnoreCase))
                        {
                            lock (_state.Sync)
                            {
                                _state.SplinePlaybackCursorSec = 0.0;
                                _state.SplinePlaybackActive = (_state.KeyframeTrack.Count >= 2);
                            }
                        }
                        else if (act.Equals("stop", StringComparison.OrdinalIgnoreCase))
                        {
                            lock (_state.Sync) { _state.SplinePlaybackActive = false; }
                        }
                        else if (act.Equals("export", StringComparison.OrdinalIgnoreCase))
                        {
                            string outDir;
                            lock (_state.Sync) { outDir = _state.ExportsDir; }
                            trackFile = Path.Combine(outDir, "OmegaKeyframeTrack.omkf.json");
                            _state.KeyframeTrack.ExportTrackJson(trackFile);
                        }

                        bool playingNow;
                        lock (_state.Sync) { playingNow = _state.SplinePlaybackActive; }
                        string kfJson = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":true,\"action\":\"{0}\",\"count\":{1},\"loop_closed\":{2},\"duration_sec\":{3:0.00},\"playing\":{4},\"track_path\":\"{5}\"}}",
                            OmegaEngineArenaState.EscJson(act),
                            _state.KeyframeTrack.Count,
                            _state.KeyframeTrack.LoopClosed ? "true" : "false",
                            _state.KeyframeTrack.TotalDurationSec,
                            playingNow ? "true" : "false",
                            OmegaEngineArenaState.EscJson(trackFile));
                        WriteHttp(ns, "application/json; charset=utf-8", kfJson);
                        return;
                    }
                    if (path.Equals("/api/render_turntable", StringComparison.OrdinalIgnoreCase))
                    {
                        int frames = (int)ParseQueryFloat(query, "frames", 12.0f);
                        int wReq = (int)ParseQueryFloat(query, "w", 320.0f);
                        int hReq = (int)ParseQueryFloat(query, "h", 180.0f);
                        float fpsReq = ParseQueryFloat(query, "fps", 24.0f);
                        int symReq = (int)ParseQueryFloat(query, "sym", -1.0f);

                        TurntableSequenceResult tt;
                        if (_state.OnRenderTurntableRequestedFromHttp != null)
                        {
                            tt = _state.OnRenderTurntableRequestedFromHttp(frames, wReq, hReq, fpsReq, symReq);
                        }
                        else
                        {
                            string outRoot;
                            int symCur; float k1, k2, k3, k4, aur, blm, cmx;
                            string activeOmsh;
                            lock (_state.Sync)
                            {
                                outRoot = Path.Combine(_state.ExportsDir, "turntable_http_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
                                symCur = (symReq >= 0 && symReq <= 7) ? symReq : _state.SymmetryMode;
                                k1 = _state.K1; k2 = _state.K2; k3 = _state.K3; k4 = _state.K4;
                                aur = _state.WeatherAurora; blm = _state.HdrBloomStrength; cmx = _state.CustomMix;
                                activeOmsh = _state.ActiveOmshSnippet;
                            }
                            ArenaKeyframeTrack trk = (_state.KeyframeTrack.Count >= 2 && symReq < 0)
                                ? _state.KeyframeTrack
                                : ArenaKeyframeTrack.BuildCanonicalTurntableTrack(8, 9.6f, 2.65f, frames / Math.Max(1.0f, fpsReq), symCur, k1, k2, k3, k4, aur, blm, cmx);
                            using (OmegaEngineDirectComputeGpu tmpGpu = new OmegaEngineDirectComputeGpu())
                            {
                                tmpGpu.Initialize(IntPtr.Zero, wReq, hReq, activeOmsh);
                                tt = OmegaTurntableSequenceRenderer.RenderKeyframeSequencePngs(tmpGpu, trk, outRoot, wReq, hReq, frames, fpsReq, 1.0f, true);
                            }
                            lock (_state.Sync)
                            {
                                _state.LastTurntableDir = tt.OutputDir;
                                _state.LastTurntableFrames = tt.FrameCount;
                            }
                        }

                        string firstPng = (tt.PngPaths.Count > 0) ? tt.PngPaths[0] : "";
                        string firstSha = (tt.FrameSha256.Count > 0) ? tt.FrameSha256[0] : "";
                        string ttJson = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":{0},\"output_dir\":\"{1}\",\"manifest_path\":\"{2}\",\"track_json\":\"{3}\",\"frames\":{4},\"width\":{5},\"height\":{6},\"fps\":{7:0.0},\"total_ms\":{8:0.0},\"first_png\":\"{9}\",\"first_sha256\":\"{10}\"}}",
                            tt.Ok ? "true" : "false",
                            OmegaEngineArenaState.EscJson(tt.OutputDir),
                            OmegaEngineArenaState.EscJson(tt.ManifestPath),
                            OmegaEngineArenaState.EscJson(tt.TrackJsonPath),
                            tt.FrameCount, tt.Width, tt.Height, tt.Fps, tt.TotalMs,
                            OmegaEngineArenaState.EscJson(firstPng),
                            OmegaEngineArenaState.EscJson(firstSha));
                        WriteHttp(ns, "application/json; charset=utf-8", ttJson);
                        return;
                    }

                    if (path.Equals("/api/export_fx", StringComparison.OrdinalIgnoreCase))
                    {
                        float[] snap = new float[80];
                        string snippet;
                        string outDir;
                        lock (_state.Sync)
                        {
                            Array.Copy(_state.U, snap, 80);
                            snippet = _state.ActiveOmshSnippet;
                            outDir = _state.ExportsDir;
                        }
                        string stubPath, sha;
                        string fxPath = OmegaEngineHlslAndReshade.ExportReshadeFxAndAddonStub(outDir, snap, snippet, out stubPath, out sha);
                        lock (_state.Sync) { _state.LastExportedFxPath = fxPath; }
                        string json = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":true,\"fx_path\":\"{0}\",\"addon_stub_path\":\"{1}\",\"sha256\":\"{2}\"}}",
                            OmegaEngineArenaState.EscJson(fxPath), OmegaEngineArenaState.EscJson(stubPath), sha);
                        WriteHttp(ns, "application/json; charset=utf-8", json);
                        return;
                    }

                    WriteHttp(ns, "text/html; charset=utf-8", BuildRemotePadHtml());
                }
            }
            catch { }
            finally
            {
                try { cli.Close(); } catch { }
            }
        }

        private static string ParseQueryString(string query, string key)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(key)) return "";
            string[] parts = query.Split('&');
            string prefix = key + "=";
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(parts[i].Substring(prefix.Length).Replace('+', ' '));
                }
            }
            return "";
        }

        private static float ParseQueryFloat(string query, string key, float defVal)
        {
            Match m = Regex.Match(query ?? "", "(?:^|&)" + Regex.Escape(key) + "=([-0-9.]+)");
            float v;
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return defVal;
        }

        private static void WriteHttp(NetworkStream ns, string contentType, string body)
        {
            byte[] b = Encoding.UTF8.GetBytes(body);
            string hdr = "HTTP/1.1 200 OK\r\nContent-Type: " + contentType +
                         "\r\nContent-Length: " + b.Length.ToString(CultureInfo.InvariantCulture) +
                         "\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(hdr);
            ns.Write(hb, 0, hb.Length);
            ns.Write(b, 0, b.Length);
        }

        private static string BuildRemotePadHtml()
        {
            return "<!DOCTYPE html><html><head><meta charset='utf-8'/>" +
                   "<meta name='viewport' content='width=device-width,initial-scale=1'/>" +
                   "<title>OmegaEngine v1.9.0 (:19795) Remote Pad</title>" +
                   "<style>body{margin:0;background:#070b14;color:#e2e8f0;font-family:Segoe UI,sans-serif;padding:16px;}" +
                   "h2{margin:0 0 10px 0;color:#38bdf8;font-size:16px;}" +
                   ".grid{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:12px;}" +
                   "button{padding:14px 10px;border:1px solid #38bdf8;border-radius:8px;background:#0f172a;color:#fff;font-weight:600;cursor:pointer;}" +
                   "button.boom{grid-column:span 2;background:#0284c7;font-size:15px;}" +
                   "pre{background:#0b1120;padding:10px;border-radius:8px;font-size:11px;overflow:auto;border:1px solid #1e293b;}</style></head><body>" +
                   "<h2>STATION 15 :: OMEGAENGINE v1.5.0 (:19795)</h2>" +
                   "<div class='grid'>" +
                   "<button class='boom' onclick=\"cmd('/api/shockwave?strength=1.35')\">FIRE THU'UM / CYMATIC SHOCKWAVE</button>" +
                   "<button onclick=\"cmd('/api/preset?sym=0')\">5D Penrose P3</button>" +
                   "<button onclick=\"cmd('/api/preset?sym=1')\">8D E8 Coxeter</button>" +
                   "<button onclick=\"cmd('/api/preset?sym=2')\">Einstein Hat</button>" +
                   "<button onclick=\"cmd('/api/preset?sym=3')\">Chiral Spectre</button>" +
                   "<button onclick=\"cmd('/api/preset?sym=4')\">4D Hopf Villarceau</button>" +
                   "<button onclick=\"cmd('/api/preset?sym=5')\">4D 24/120-Cell H4</button>" +
                   "<button onclick=\"cmd('/api/preset?sym=6')\">Poincare {5,3,4}</button>" +
                   "<button onclick=\"cmd('/api/preset?sym=7')\">24D Leech Golay</button>" +
                   "<button onclick=\"cmd('/api/bake_dc_obj?res=64')\">Bake DC .OBJ (64^3)</button>" +
                   "<button onclick=\"cmd('/api/bake_pbr?res=2048')\">Bake 2048 PBR PNGs</button>" +
                   "<button class='boom' onclick=\"cmd('/api/forge_bundle?grid=64&tex=2048')\">1-CLICK 3D + 2048 PBR ASSET FORGE</button>" +
                   "<button onclick=\"cmd('/api/shader_cache')\">DXBC Cache Stats</button>" +
                   "<button onclick=\"cmd('/api/export_fx')\">Export ReShade .fx</button>" +
                   "<button onclick=\"cmd('/api/skse64')\">SKSE64 Bridge Status</button>" +
                   "<button onclick=\"cmd('/api/skyrim_mod')\">Build Vortex Mod .zip</button>" +
                   "</div><pre id='out'>Ready.</pre>" +
                   "<script>function cmd(u){fetch(u).then(r=>r.text()).then(t=>document.getElementById('out').textContent=t);}</script>" +
                   "</body></html>";
        }

        public void Dispose()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
            try { if (_acceptThread != null && _acceptThread.IsAlive) _acceptThread.Join(250); } catch { }
        }
    }

    internal sealed class ArenaViewportPanel : Panel
    {
        public Bitmap CpuFallbackBitmap;
        public bool UseSwapChain;

        public ArenaViewportPanel()
        {
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Color.Black;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!UseSwapChain && CpuFallbackBitmap != null)
            {
                e.Graphics.DrawImage(CpuFallbackBitmap, ClientRectangle);
            }
        }
    }

    public sealed class OmegaEngineCockpitForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NATIVE_MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern bool PeekMessageW(out NATIVE_MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);
        [DllImport("psapi.dll", EntryPoint = "EmptyWorkingSet")] private static extern bool EmptyWorkingSetLocal(IntPtr hProcess);

        private readonly string _engineDir;
        private readonly string _suiteRoot;
        private readonly string _sharedExportsDir;
        private readonly OmegaEngineArenaState _state;
        private readonly OmegaTriEngineUniformBus _mmfBus;
        private readonly OmegaTriEngineAudioAndBridge _audio;
        private readonly OmegaEngineDirectComputeGpu _gpu;
        private readonly OmegaEngineHttpServer _http;

        private readonly ArenaViewportPanel _viewport;
        private Bitmap _cpuBmp;
        private int[] _cpuPixels;

        private readonly Label _lblTopHud;
        private readonly Label _lblStatusBot;
        private readonly Label _lblCompileInfo;
        private readonly ComboBox _cmbPreset;
        private readonly ComboBox _cmbSymmetry;
        private readonly ComboBox _cmbVsync;
        private readonly ComboBox _cmbQuality;
        private readonly CheckBox _chkFlyMode;
        private readonly CheckBox _chkMuteSynth;
        private readonly TrackBar _trkK1, _trkK2, _trkK3, _trkK4, _trkAurora, _trkBloom, _trkOmshMix;
        private readonly TextBox _txtAiPrompt;
        private readonly Button _btnAiPromptGo;
        private readonly Button _btnAddKf;
        private readonly Button _btnPlaySpline;
        private readonly TextBox _txtOmshEditor;
        private volatile bool _aiCoPilotInFlight = false;

        private readonly Stopwatch _simClock = Stopwatch.StartNew();
        private double _prevSec = 0.0;
        private double _fpsWindowStart = 0.0;
        private int _fpsFrames = 0;
        private int _hudUpdateTick = 0;
        private Process _rsProbeProc = null;
        private Process _skseProbeProc = null;
        private Button _btnRsProbe = null;
        private Button _btnSkseProbe = null;
        private Button _btnTopBarToggle = null;
        private readonly OmegaTopBarController _topBarCtrl;
        private readonly bool _autoStartRsProbe = false;
        private readonly bool _autoStartSkseProbe = false;
        private uint _vsyncInterval = 0u;
        private int _targetFpsCap = 144;
        private double _nextFrameGateSec = 0.0;
        private float _renderScale = 1.25f;
        private float _casSharpness = 0.75f;
        private float _bloomStrength = 0.32f;
        private bool _enableRayCone = true;
        private readonly float[] _uSnap80 = new float[80];
        private readonly float[] _busMerge80 = new float[80];
        private readonly double[] _feat8 = new double[8];
        private volatile bool _loopActive = false;
        private bool _draggingLook = false;
        private Point _lastMousePt;
        private bool _spaceWasDown = false;
        private bool _syncingUi = false;

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public OmegaEngineCockpitForm(string engineDir, OmegaHardwareProfile hwProf, bool autoStartRsProbe, bool autoStartSkseProbe)
        {
            _autoStartRsProbe = autoStartRsProbe;
            _autoStartSkseProbe = autoStartSkseProbe;
            _engineDir = engineDir;
            DirectoryInfo parent = Directory.GetParent(engineDir);
            _suiteRoot = (parent != null) ? parent.FullName : engineDir;
            string localExports = Path.Combine(engineDir, "exports");
            _sharedExportsDir = Path.Combine(Path.Combine(_suiteRoot, "downloads"), "omega_engine_exports");
            Directory.CreateDirectory(localExports);
            Directory.CreateDirectory(_sharedExportsDir);

            _state = new OmegaEngineArenaState(localExports);
            _state.GpuAdapterName = (hwProf != null && !string.IsNullOrEmpty(hwProf.PrimaryGpuName)) ? hwProf.PrimaryGpuName : "DirectCompute cs_5_0";
            _mmfBus = new OmegaTriEngineUniformBus(OmegaTriEngineUniformBus.DefaultMapName);
            _audio = new OmegaTriEngineAudioAndBridge();
            _topBarCtrl = new OmegaTopBarController(_state, _mmfBus);
            _state.SharedTopBar = _topBarCtrl;
            _topBarCtrl.OnStateMutated = delegate
            {
                if (!IsDisposed)
                {
                    try { BeginInvoke((Action)delegate { SyncControlsFromStateAndCompile(); }); } catch { }
                }
            };
            _topBarCtrl.OnFireThuumRequested = delegate
            {
                _audio.TriggerShockwaveAcoustic(1.30f, 58.0f);
            };
            _gpu = new OmegaEngineDirectComputeGpu();
            _http = new OmegaEngineHttpServer(_state);

            _state.OnPresetRequestedFromHttp = delegate(int idx)
            {
                if (!IsDisposed)
                {
                    try { BeginInvoke((Action)delegate { SyncControlsFromStateAndCompile(); }); } catch { }
                }
            };
            _state.OnShockwaveRequestedFromHttp = delegate(float str, float frq)
            {
                _audio.TriggerShockwaveAcoustic(str, 62.0f);
            };
            _state.OnBakePbrRequestedFromHttp = delegate(int texRes, int symReq, bool dxFlip)
            {
                float[] snap = new float[80];
                lock (_state.Sync) { Array.Copy(_state.U, snap, 80); }
                OmegaEngineDirectComputeGpu.PbrBakeResult r = _gpu.BakePbrMaterialSet(_sharedExportsDir, texRes, symReq, dxFlip, snap);
                CopyFileSafe(r.AlbedoPath, Path.Combine(_state.ExportsDir, Path.GetFileName(r.AlbedoPath)));
                CopyFileSafe(r.NormalPath, Path.Combine(_state.ExportsDir, Path.GetFileName(r.NormalPath)));
                CopyFileSafe(r.HeightPath, Path.Combine(_state.ExportsDir, Path.GetFileName(r.HeightPath)));
                CopyFileSafe(r.EmissivePath, Path.Combine(_state.ExportsDir, Path.GetFileName(r.EmissivePath)));
                CopyFileSafe(r.MtlPath, Path.Combine(_state.ExportsDir, Path.GetFileName(r.MtlPath)));
                lock (_state.Sync) { _state.LastBakedPbrPrefix = r.Prefix; }
                return r;
            };
            _state.OnOmshAppliedFromHttp = delegate(string appliedCode)
            {
                if (!IsDisposed)
                {
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            _syncingUi = true;
                            try
                            {
                                _txtOmshEditor.Text = appliedCode ?? "";
                                lock (_state.Sync)
                                {
                                    _trkOmshMix.Value = ClampTrack(_trkOmshMix, (int)Math.Round(_state.CustomMix * 100.0f));
                                }
                            }
                            finally { _syncingUi = false; }
                            HotCompileFromEditor();
                        });
                    }
                    catch { }
                }
            };
            _state.OnRenderTurntableRequestedFromHttp = delegate(int frames, int wReq, int hReq, float fpsReq, int symReq)
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    try
                    {
                        return (TurntableSequenceResult)Invoke((Func<TurntableSequenceResult>)delegate
                        {
                            return RenderTurntableSequenceInternal(frames, wReq, hReq, fpsReq, symReq);
                        });
                    }
                    catch { }
                }
                return RenderTurntableSequenceInternal(frames, wReq, hReq, fpsReq, symReq);
            };
            _state.OnReshadeProbeRequestedFromHttp = delegate(string mode)
            {
                bool wantOn = IsRsProbeRunning();
                if (string.Equals(mode, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "true", StringComparison.OrdinalIgnoreCase)) wantOn = true;
                else if (string.Equals(mode, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "false", StringComparison.OrdinalIgnoreCase)) wantOn = false;
                else wantOn = !wantOn;
                SetRsProbeStateThreadSafe(wantOn);
                return IsRsProbeRunning();
            };
            _state.OnSkseProbeRequestedFromHttp = delegate(string mode)
            {
                bool wantOn = IsSkseProbeRunning();
                if (string.Equals(mode, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "true", StringComparison.OrdinalIgnoreCase)) wantOn = true;
                else if (string.Equals(mode, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "false", StringComparison.OrdinalIgnoreCase)) wantOn = false;
                else wantOn = !wantOn;
                SetSkseProbeStateThreadSafe(wantOn);
                return IsSkseProbeRunning();
            };
            _state.OnBuildSkyrimModRequestedFromHttp = delegate(string explicitGameDir, bool syncGame)
            {
                float[] snap = new float[80];
                string code;
                lock (_state.Sync) { Array.Copy(_state.U, snap, 80); code = _state.ActiveOmshSnippet; }
                string wsDir = Path.Combine(_suiteRoot, "workspace");
                SkyrimModBundleResult mb = OmegaEngineSkseModBuilder.BuildCompleteSkyrimModPackage(
                    _sharedExportsDir, explicitGameDir ?? "", wsDir, snap, code, syncGame);
                lock (_state.Sync) { _state.LastVortexModZipPath = mb.VortexZipPath; }
                return mb;
            };

            Text = "ACT-Omega Suite v3.9.0 :: Station 15 OmegaEngine v1.9.0 (:19795) [AI .omsh Co-Pilot (:19791) + Keyframe Turntable + QEF Dual-Contour + 2K PBR]";
            Size = new Size(1560, 910);
            MinimumSize = new Size(1180, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C(7, 11, 19);
            ForeColor = Color.White;
            KeyPreview = true;

            try
            {
                string ic = Path.Combine(Path.Combine(_suiteRoot, "icons"), "omega_engine.ico");
                if (File.Exists(ic)) Icon = new Icon(ic);
            }
            catch { }

            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 36;
            topBar.BackColor = C(11, 17, 31);
            _lblTopHud = new Label();
            _lblTopHud.Dock = DockStyle.Fill;
            _lblTopHud.Padding = new Padding(12, 0, 12, 0);
            _lblTopHud.TextAlign = ContentAlignment.MiddleLeft;
            _lblTopHud.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
            _lblTopHud.ForeColor = C(56, 189, 248);
            _lblTopHud.Text = "Station 15 OmegaEngine v1.9.0 (:19795) :: Loading SHA-256 DXBC Cache + FP16 HDR Pipeline...";
            _btnTopBarToggle = new Button();
            _btnTopBarToggle.Dock = DockStyle.Right;
            _btnTopBarToggle.Width = 148;
            _btnTopBarToggle.FlatStyle = FlatStyle.Flat;
            _btnTopBarToggle.FlatAppearance.BorderSize = 1;
            _btnTopBarToggle.FlatAppearance.BorderColor = C(245, 184, 81);
            _btnTopBarToggle.BackColor = C(26, 34, 52);
            _btnTopBarToggle.ForeColor = C(245, 184, 81);
            _btnTopBarToggle.Font = new Font("Consolas", 8.5f, FontStyle.Bold);
            _btnTopBarToggle.Text = "TOP HUD BAR [F10]";
            _btnTopBarToggle.Click += delegate
            {
                _topBarCtrl.ToggleDesktopBar();
                _btnTopBarToggle.BackColor = _topBarCtrl.IsDesktopBarVisible ? C(5, 150, 105) : C(26, 34, 52);
                _btnTopBarToggle.ForeColor = _topBarCtrl.IsDesktopBarVisible ? Color.White : C(245, 184, 81);
                _viewport.Focus();
            };
            topBar.Controls.Add(_lblTopHud);
            topBar.Controls.Add(_btnTopBarToggle);

            Panel botBar = new Panel();
            botBar.Dock = DockStyle.Bottom;
            botBar.Height = 26;
            botBar.BackColor = C(6, 9, 17);
            _lblStatusBot = new Label();
            _lblStatusBot.Dock = DockStyle.Fill;
            _lblStatusBot.Padding = new Padding(12, 0, 12, 0);
            _lblStatusBot.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatusBot.Font = new Font("Consolas", 8.8f);
            _lblStatusBot.ForeColor = C(52, 211, 153);
            _lblStatusBot.Text = "Controls: WASD Move | Shift Sprint | Mouse Drag Look | Scroll FOV | SPACE = Fire Thu'um Shockwave | F5 = Async Live HLSL";
            botBar.Controls.Add(_lblStatusBot);

            Panel rightDock = new Panel();
            rightDock.Dock = DockStyle.Right;
            rightDock.Width = 396;
            rightDock.BackColor = C(12, 18, 33);
            rightDock.Padding = new Padding(10);

            int y = 6;
            Button btnShock = MakeBtn("[SPACE] FIRE THU'UM / CYMATIC SHOCKWAVE", 10, y, 374, 29, C(2, 132, 199), 8.7f);
            btnShock.Click += delegate { FirePlayerShockwave(); _viewport.Focus(); };
            rightDock.Controls.Add(btnShock);
            y += 33;

            Button btnExportFx = MakeBtn("Export .fx", 10, y, 96, 26, C(5, 150, 105), 8.2f);
            btnExportFx.Click += delegate { ExportReshadeBundleFromUi(); };
            Button btnVortexMod = MakeBtn("Vortex Mod", 110, y, 102, 26, C(124, 58, 237), 8.2f);
            btnVortexMod.Click += delegate { BuildVortexModFromUi(); };
            Button btnDeployGame = MakeBtn("Deploy Game", 216, y, 104, 26, C(13, 148, 136), 8.2f);
            btnDeployGame.Click += delegate { DeployBridgeToGameFromUi(); };
            Button btnOpenExp = MakeBtn("Open", 324, y, 60, 26, C(30, 58, 138), 8.2f);
            btnOpenExp.Click += delegate { Process.Start("explorer.exe", "\"" + _sharedExportsDir + "\""); };
            rightDock.Controls.Add(btnExportFx);
            rightDock.Controls.Add(btnVortexMod);
            rightDock.Controls.Add(btnDeployGame);
            rightDock.Controls.Add(btnOpenExp);
            y += 29;

            // Row 3: Hermite Dual Contouring QEF .OBJ (64^3 / 96^3) + 2048x2048 GPU 4-Map PBR Baker
            Button btnDc64 = MakeBtn("DC .OBJ 64", 10, y, 90, 25, C(14, 116, 144), 8.1f);
            btnDc64.Click += delegate { BakeDualContourObjFromUi(64); };
            Button btnDc96 = MakeBtn("DC .OBJ 96", 104, y, 90, 25, C(3, 105, 161), 8.1f);
            btnDc96.Click += delegate { BakeDualContourObjFromUi(96); };
            Button btnPbr2k = MakeBtn("Bake 2K PBR", 198, y, 100, 25, C(5, 150, 105), 8.1f);
            btnPbr2k.Click += delegate { BakePbr2048FromUi(false); };
            Button btnSavePng = MakeBtn("Save PNG", 302, y, 82, 25, C(91, 33, 182), 8.1f);
            btnSavePng.Click += delegate { SaveGpuPngSnapshotFromUi(); };
            rightDock.Controls.Add(btnDc64);
            rightDock.Controls.Add(btnDc96);
            rightDock.Controls.Add(btnPbr2k);
            rightDock.Controls.Add(btnSavePng);
            y += 28;

            // Row 4: ForgeAll + ArenaOBJ + LinkMesh + RS:LIVE + SK:LIVE (zero word-wrap at 125% DPI)
            Button btnForgeAll = MakeBtn("ForgeAll", 10, y, 76, 25, C(124, 58, 237), 7.7f);
            btnForgeAll.Click += delegate { RunOneClickCompleteForgeFromUi(); };
            Button btnExportObj = MakeBtn("ArenaOBJ", 90, y, 76, 25, C(51, 65, 85), 7.7f);
            btnExportObj.Click += delegate { ExportObjFromUi(); };
            Button btnLinkSyPn = MakeBtn("LinkMesh", 170, y, 74, 25, C(180, 83, 9), 7.7f);
            btnLinkSyPn.Click += delegate { LaunchCompanionSynthAndPenrose(); };
            _btnRsProbe = MakeBtn("RS:LIVE", 248, y, 66, 25, C(15, 118, 110), 7.7f);
            _btnRsProbe.Click += delegate { ToggleRsLiveProbeFromUi(); };
            _btnSkseProbe = MakeBtn("SK:LIVE", 318, y, 66, 25, C(3, 105, 161), 7.7f);
            _btnSkseProbe.Click += delegate { ToggleSkseLiveProbeFromUi(); };
            rightDock.Controls.Add(btnForgeAll);
            rightDock.Controls.Add(btnExportObj);
            rightDock.Controls.Add(btnLinkSyPn);
            rightDock.Controls.Add(_btnRsProbe);
            rightDock.Controls.Add(_btnSkseProbe);
            y += 28;

            // Row 5 (Task #7): Deterministic Keyframe Spline Recorder & Lossless PNG Turntable Sequence Renderer
            _btnAddKf = MakeBtn("+Key (0)", 10, y, 88, 25, C(14, 116, 144), 7.8f);
            _btnAddKf.Click += delegate { AddKeyframeFromUi(); };
            _btnPlaySpline = MakeBtn("Play Spline", 102, y, 102, 25, C(5, 150, 105), 7.8f);
            _btnPlaySpline.Click += delegate { ToggleSplinePlaybackFromUi(); };
            Button btnClearKf = MakeBtn("Clear", 208, y, 54, 25, C(71, 85, 105), 7.8f);
            btnClearKf.Click += delegate { ClearKeyframesFromUi(); };
            Button btnTurntable = MakeBtn("Turntable PNG", 266, y, 118, 25, C(147, 51, 234), 7.8f);
            btnTurntable.Click += delegate { RenderTurntableFromUi(); };
            rightDock.Controls.Add(_btnAddKf);
            rightDock.Controls.Add(_btnPlaySpline);
            rightDock.Controls.Add(btnClearKf);
            rightDock.Controls.Add(btnTurntable);
            y += 29;

            rightDock.Controls.Add(MakeLbl("Arena Preset:", 10, y, 104));
            _cmbPreset = new ComboBox();
            _cmbPreset.SetBounds(116, y - 2, 268, 24);
            _cmbPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbPreset.BackColor = C(7, 11, 19);
            _cmbPreset.ForeColor = Color.White;
            for (int i = 0; i < OmegaEngineHlslAndReshade.BuiltInPresets.Length; i++)
                _cmbPreset.Items.Add(OmegaEngineHlslAndReshade.BuiltInPresets[i]);
            _cmbPreset.SelectedIndex = 0;
            _cmbPreset.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                _state.ApplyPreset(_cmbPreset.SelectedIndex);
                SyncControlsFromStateAndCompile();
                _viewport.Focus();
            };
            rightDock.Controls.Add(_cmbPreset);
            y += 28;

            rightDock.Controls.Add(MakeLbl("Tile Mode:", 10, y, 104));
            _cmbSymmetry = new ComboBox();
            _cmbSymmetry.SetBounds(116, y - 2, 268, 24);
            _cmbSymmetry.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbSymmetry.BackColor = C(7, 11, 19);
            _cmbSymmetry.ForeColor = Color.White;
            _cmbSymmetry.Items.AddRange(new object[]
            {
                "0: 5D de Bruijn Penrose P3",
                "1: 8D E8 Coxeter Voronoi",
                "2: Einstein Hat (arXiv:2303.10798)",
                "3: Chiral Spectre (arXiv:2305.17743)",
                "4: 4D Hopf Villarceau (S^3 -> S^2)",
                "5: 4D 24-Cell & 120-Cell (Hurwitz + H4)",
                "6: Hyperbolic Poincare {5,3,4} & {4,3,5}",
                "7: 24D Leech Lattice + Golay [24,12,8]"
            });
            _cmbSymmetry.SelectedIndex = 0;
            _cmbSymmetry.SelectedIndexChanged += delegate
            {
                if (_syncingUi) return;
                lock (_state.Sync) { _state.SymmetryMode = _cmbSymmetry.SelectedIndex; }
                _viewport.Focus();
            };
            rightDock.Controls.Add(_cmbSymmetry);
            y += 28;

            rightDock.Controls.Add(MakeLbl("Pacing:", 10, y, 64));
            _cmbVsync = new ComboBox();
            _cmbVsync.SetBounds(74, y - 2, 104, 24);
            _cmbVsync.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbVsync.BackColor = C(7, 11, 19);
            _cmbVsync.ForeColor = Color.White;
            _cmbVsync.Items.AddRange(new object[] { "VSync (60)", "120 FPS", "144 FPS", "Uncapped" });
            _cmbVsync.SelectedIndex = 2;
            _vsyncInterval = 0u;
            _targetFpsCap = 144;
            _cmbVsync.SelectedIndexChanged += delegate
            {
                int si = _cmbVsync.SelectedIndex;
                _vsyncInterval = (si == 0) ? 1u : 0u;
                _targetFpsCap = (si == 1) ? 120 : ((si == 2) ? 144 : 0);
                _viewport.Focus();
            };
            rightDock.Controls.Add(_cmbVsync);

            _cmbQuality = new ComboBox();
            _cmbQuality.SetBounds(182, y - 2, 140, 24);
            _cmbQuality.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbQuality.BackColor = C(7, 11, 19);
            _cmbQuality.ForeColor = C(125, 211, 252);
            _cmbQuality.Items.AddRange(new object[]
            {
                "1.25x HDR + CAS",
                "1.50x Cone SSAA",
                "2.00x Cone SSAA",
                "1.00x HDR + CAS",
                "1.00x Raw (No Cone)"
            });
            _cmbQuality.SelectedIndex = 0;
            _cmbQuality.SelectedIndexChanged += delegate
            {
                int qi = _cmbQuality.SelectedIndex;
                _renderScale   = (qi == 0) ? 1.25f : ((qi == 1) ? 1.50f : ((qi == 2) ? 2.00f : 1.00f));
                _casSharpness  = (qi == 4) ? 0.00f : ((qi == 2) ? 0.88f : ((qi == 1) ? 0.82f : 0.75f));
                _enableRayCone = (qi != 4);
                lock (_state.Sync)
                {
                    _state.RenderScale = _renderScale;
                    _state.RayConePrepassEnabled = _enableRayCone;
                }
                _viewport.Focus();
            };
            rightDock.Controls.Add(_cmbQuality);

            Button btnResetCam = MakeBtn("Reset", 326, y - 3, 58, 25, C(51, 65, 85), 8.4f);
            btnResetCam.Click += delegate
            {
                lock (_state.Sync)
                {
                    _state.CamX = 0.0f; _state.CamY = 2.35f; _state.CamZ = -10.2f;
                    _state.CamYaw = 0.0f; _state.CamPitch = -0.07f; _state.FovDeg = 64.0f;
                }
                _viewport.Focus();
            };
            rightDock.Controls.Add(btnResetCam);
            y += 27;

            _chkFlyMode = new CheckBox();
            _chkFlyMode.Text = "6-DoF Fly Mode (Q/E)";
            _chkFlyMode.SetBounds(12, y, 180, 22);
            _chkFlyMode.ForeColor = C(196, 181, 253);
            _chkFlyMode.CheckedChanged += delegate { lock (_state.Sync) { _state.FlyMode = _chkFlyMode.Checked; } _viewport.Focus(); };
            rightDock.Controls.Add(_chkFlyMode);

            _chkMuteSynth = new CheckBox();
            _chkMuteSynth.Text = "Mute Internal 48kHz Synth";
            _chkMuteSynth.SetBounds(198, y, 186, 22);
            _chkMuteSynth.ForeColor = C(253, 186, 116);
            _chkMuteSynth.CheckedChanged += delegate { _audio.AudioMuted = _chkMuteSynth.Checked; _viewport.Focus(); };
            rightDock.Controls.Add(_chkMuteSynth);
            y += 26;

            _trkK1 = AddSliderRow(rightDock, "k1 Monolith Scale", ref y, 40, 180, 100, delegate(int v) { lock (_state.Sync) _state.K1 = v / 100.0f; });
            _trkK2 = AddSliderRow(rightDock, "k2 6D Phason", ref y, 0, 200, 100, delegate(int v) { lock (_state.Sync) _state.K2 = v / 100.0f; });
            _trkK3 = AddSliderRow(rightDock, "k3 Gyroid Carve", ref y, 0, 180, 85, delegate(int v) { lock (_state.Sync) _state.K3 = v / 100.0f; });
            _trkK4 = AddSliderRow(rightDock, "k4 Crystal IOR", ref y, 130, 205, 154, delegate(int v) { lock (_state.Sync) _state.K4 = v / 100.0f; });
            _trkAurora = AddSliderRow(rightDock, "Aurora Sky", ref y, 0, 140, 78, delegate(int v) { lock (_state.Sync) _state.WeatherAurora = v / 100.0f; });
            _trkBloom = AddSliderRow(rightDock, "HDR Bloom", ref y, 0, 80, 32, delegate(int v)
            {
                _bloomStrength = v / 100.0f;
                lock (_state.Sync) _state.HdrBloomStrength = _bloomStrength;
            });
            _trkOmshMix = AddSliderRow(rightDock, "Custom .omsh", ref y, 0, 100, 25, delegate(int v) { lock (_state.Sync) _state.CustomMix = v / 100.0f; });

            y += 2;
            _txtAiPrompt = new TextBox();
            _txtAiPrompt.SetBounds(10, y, 274, 23);
            _txtAiPrompt.BackColor = C(8, 13, 24);
            _txtAiPrompt.ForeColor = C(186, 230, 253);
            _txtAiPrompt.Font = new Font("Segoe UI", 8.6f);
            _txtAiPrompt.BorderStyle = BorderStyle.FixedSingle;
            _txtAiPrompt.Text = "golden hopf villarceau rings + bass pulse + emerald rim";
            _txtAiPrompt.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter)
                {
                    ke.SuppressKeyPress = true;
                    RequestTensorOmshCoPilot(_txtAiPrompt.Text);
                }
            };
            rightDock.Controls.Add(_txtAiPrompt);

            _btnAiPromptGo = MakeBtn("Ask :19791", 288, y - 1, 96, 24, C(13, 148, 136), 8.2f);
            _btnAiPromptGo.Click += delegate { RequestTensorOmshCoPilot(_txtAiPrompt.Text); };
            rightDock.Controls.Add(_btnAiPromptGo);
            y += 27;

            Label lEd = MakeLbl("Live .omsh:", 10, y, 90);
            lEd.ForeColor = C(56, 189, 248);
            rightDock.Controls.Add(lEd);

            Button btnAiOmsh = MakeBtn("AI Cycle (:19791)", 104, y - 4, 132, 24, C(15, 118, 110), 8.0f);
            btnAiOmsh.Click += delegate { RequestTensorOmshCoPilot(null); };
            rightDock.Controls.Add(btnAiOmsh);

            Button btnCompile = MakeBtn("Hot-Compile (F5)", 240, y - 4, 144, 24, C(124, 58, 237), 8.0f);
            btnCompile.Click += delegate { HotCompileFromEditor(); };
            rightDock.Controls.Add(btnCompile);
            y += 25;

            _lblCompileInfo = new Label();
            _lblCompileInfo.Dock = DockStyle.Bottom;
            _lblCompileInfo.Height = 34;
            _lblCompileInfo.Padding = new Padding(8, 4, 6, 2);
            _lblCompileInfo.BackColor = C(6, 11, 20);
            _lblCompileInfo.Font = new Font("Consolas", 8.4f);
            _lblCompileInfo.ForeColor = C(110, 231, 183);
            _lblCompileInfo.Text = "SHA-256 DXBC Cache + FP16 HDR Ready | HTTP :19795";
            rightDock.Controls.Add(_lblCompileInfo);

            _txtOmshEditor = new TextBox();
            _txtOmshEditor.Multiline = true;
            _txtOmshEditor.ScrollBars = ScrollBars.Vertical;
            _txtOmshEditor.AcceptsTab = true;
            _txtOmshEditor.BackColor = C(6, 9, 16);
            _txtOmshEditor.ForeColor = C(167, 243, 208);
            _txtOmshEditor.Font = new Font("Consolas", 9.2f);
            _txtOmshEditor.BorderStyle = BorderStyle.FixedSingle;
            int edH = Math.Max(80, rightDock.ClientSize.Height - y - 42);
            _txtOmshEditor.SetBounds(10, y, 374, edH);
            _txtOmshEditor.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _txtOmshEditor.Text = _state.ActiveOmshSnippet;
            rightDock.Controls.Add(_txtOmshEditor);

            _viewport = new ArenaViewportPanel();
            _viewport.Dock = DockStyle.Fill;
            _viewport.MouseDown += delegate(object s, MouseEventArgs e)
            {
                _viewport.Focus();
                if (e.Button == MouseButtons.Left)
                {
                    _draggingLook = true;
                    _lastMousePt = e.Location;
                }
                else if (e.Button == MouseButtons.Right)
                {
                    FirePlayerShockwave();
                }
            };
            _viewport.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (_draggingLook)
                {
                    int dx = e.X - _lastMousePt.X;
                    int dy = e.Y - _lastMousePt.Y;
                    _lastMousePt = e.Location;
                    lock (_state.Sync)
                    {
                        _state.CamYaw += dx * 0.0045f;
                        _state.CamPitch = Math.Max(-1.42f, Math.Min(1.42f, _state.CamPitch - dy * 0.0045f));
                    }
                }
            };
            _viewport.MouseUp += delegate { _draggingLook = false; };
            _viewport.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                lock (_state.Sync)
                {
                    _state.FovDeg = Math.Max(38.0f, Math.Min(96.0f, _state.FovDeg - Math.Sign(e.Delta) * 3.0f));
                }
            };

            Controls.Add(_viewport);
            Controls.Add(rightDock);
            Controls.Add(topBar);
            Controls.Add(botBar);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5)
                {
                    e.SuppressKeyPress = true;
                    HotCompileFromEditor();
                }
                else if (e.KeyCode == Keys.Space && ActiveControl != _txtOmshEditor && ActiveControl != _txtAiPrompt)
                {
                    e.SuppressKeyPress = true;
                    FirePlayerShockwave();
                }
            };

            Shown += OnCockpitShown;
            FormClosing += OnCockpitClosing;
        }

        private void OnCockpitShown(object sender, EventArgs e)
        {
            timeBeginPeriod(1u);
            try { _http.Start(19795, false); } catch { }
            _audio.Start(true);

            int w = Math.Max(320, _viewport.ClientSize.Width);
            int h = Math.Max(240, _viewport.ClientSize.Height);
            bool gpuOk = _gpu.Initialize(_viewport.Handle, w, h, _state.ActiveOmshSnippet);
            _viewport.UseSwapChain = gpuOk && _gpu.HasSwapChain;
            RefreshGpuTelemetryInState(gpuOk);

            if (gpuOk)
            {
                bool zeroFxc = (_gpu.D3DCompileInvocations == 0);
                _lblCompileInfo.ForeColor = C(110, 231, 183);
                _lblCompileInfo.Text = zeroFxc
                    ? string.Format(CultureInfo.InvariantCulture,
                        "DXBC Cache HIT: 8 cs_5_0 blobs in {0:0.00} ms (0 FXC) | FP16 HDR + Cone LIVE",
                        _gpu.InitShaderLoadMs)
                    : string.Format(CultureInfo.InvariantCulture,
                        "cs_5_0 Ready in {0:0.0} ms (DXBC saved -> next boot <0.5 ms) | FP16 HDR LIVE",
                        _gpu.InitShaderLoadMs);
            }
            else
            {
                _lblCompileInfo.ForeColor = C(252, 165, 165);
                _lblCompileInfo.Text = "CPU Fallback (" + (_gpu.LastError ?? "No D3D11") + ")";
            }

            if (_autoStartRsProbe)
            {
                SetRsProbeStateThreadSafe(true);
            }
            if (_autoStartSkseProbe)
            {
                SetSkseProbeStateThreadSafe(true);
            }
            OmegaEngineDirectComputeGpu.TrimWorkingSetNow();
            _viewport.Focus();
            _loopActive = true;
            _topBarCtrl.StartTimer60Hz();
            Application.Idle += OnApplicationIdle;
        }

        private void RefreshGpuTelemetryInState(bool gpuOk)
        {
            lock (_state.Sync)
            {
                _state.GpuCs50Active = gpuOk;
                _state.InitShaderLoadMs = _gpu.InitShaderLoadMs;
                _state.DxbcCacheHits = _gpu.DiskCacheHits;
                _state.DxbcCacheMisses = _gpu.DiskCacheMisses;
                _state.D3DCompileCalls = _gpu.D3DCompileInvocations;
                _state.DxbcFilesOnDisk = _gpu.CountCachedDxbcFiles();
            }
        }

        private void OnCockpitClosing(object sender, FormClosingEventArgs e)
        {
            _loopActive = false;
            Application.Idle -= OnApplicationIdle;
            try { _topBarCtrl.Dispose(); } catch { }
            StopRsProbeProcess();
            StopSkseProbeProcess();
            try { _http.Dispose(); } catch { }
            try { _audio.Dispose(); } catch { }
            try { _gpu.Dispose(); } catch { }
            try { _mmfBus.Dispose(); } catch { }
            if (_cpuBmp != null) { _cpuBmp.Dispose(); _cpuBmp = null; }
            timeEndPeriod(1u);
        }

        private void OnApplicationIdle(object sender, EventArgs e)
        {
            NATIVE_MSG msg;
            while (_loopActive && !IsDisposed && !PeekMessageW(out msg, IntPtr.Zero, 0u, 0u, 0u))
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    Thread.Sleep(25);
                    break;
                }
                if (_targetFpsCap > 0 && _vsyncInterval == 0u)
                {
                    double nowGate = _simClock.Elapsed.TotalSeconds;
                    double waitSec = _nextFrameGateSec - nowGate;
                    if (waitSec > 0.002) { Thread.Sleep(1); continue; }
                    if (waitSec > 0.0) { Thread.SpinWait(120); continue; }
                    _nextFrameGateSec = Math.Max(nowGate, _nextFrameGateSec) + (1.0 / _targetFpsCap);
                }
                StepAndRenderFrame();
            }
        }

        private void CheckAsyncCompileCompletion()
        {
            bool ok, cached, diskHit;
            double ms;
            string snip, err;
            if (_gpu.PollAsyncArenaCompile(out ok, out cached, out diskHit, out ms, out snip, out err))
            {
                RefreshGpuTelemetryInState(_gpu.IsAvailable);
                if (ok)
                {
                    lock (_state.Sync) { _state.ActiveOmshSnippet = snip; }
                    _lblCompileInfo.ForeColor = C(167, 243, 208);
                    if (cached && !diskHit)
                    {
                        _lblCompileInfo.Text = "[VRAM 0.00 ms] Instant cs_5_0 shader swap (zero stall)";
                    }
                    else if (diskHit)
                    {
                        _lblCompileInfo.Text = string.Format(CultureInfo.InvariantCulture,
                            "[DXBC HIT {0:0.00} ms] Loaded SHA-256 .dxbc (0 FXC calls)", ms);
                    }
                    else
                    {
                        _lblCompileInfo.Text = string.Format(CultureInfo.InvariantCulture,
                            "[ASYNC PASS] Background cs_5_0 compiled & cached in {0:0.0} ms (0 frame stall)", ms);
                    }
                }
                else
                {
                    _lblCompileInfo.ForeColor = C(252, 165, 165);
                    _lblCompileInfo.Text = "[HLSL ERROR] " + (err ?? "Compile failed (kept previous shader)");
                }
            }
        }

        private void StepAndRenderFrame()
        {
            CheckAsyncCompileCompletion();

            double nowSec = _simClock.Elapsed.TotalSeconds;
            float dt = (float)Math.Max(0.0005, Math.Min(0.08, nowSec - _prevSec));
            _prevSec = nowSec;

            if (ContainsFocus && ActiveControl != _txtOmshEditor && ActiveControl != _txtAiPrompt)
            {
                bool kW = (GetAsyncKeyState((int)Keys.W) & 0x8000) != 0 || (GetAsyncKeyState((int)Keys.Up) & 0x8000) != 0;
                bool kS = (GetAsyncKeyState((int)Keys.S) & 0x8000) != 0 || (GetAsyncKeyState((int)Keys.Down) & 0x8000) != 0;
                bool kA = (GetAsyncKeyState((int)Keys.A) & 0x8000) != 0 || (GetAsyncKeyState((int)Keys.Left) & 0x8000) != 0;
                bool kD = (GetAsyncKeyState((int)Keys.D) & 0x8000) != 0 || (GetAsyncKeyState((int)Keys.Right) & 0x8000) != 0;
                bool kQ = (GetAsyncKeyState((int)Keys.Q) & 0x8000) != 0;
                bool kE = (GetAsyncKeyState((int)Keys.E) & 0x8000) != 0;
                bool kShift = (GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0;
                bool kSpace = (GetAsyncKeyState((int)Keys.Space) & 0x8000) != 0;

                if (kSpace && !_spaceWasDown) FirePlayerShockwave();
                _spaceWasDown = kSpace;

                float moveSpeed = (kShift ? 11.5f : 5.6f) * dt;
                lock (_state.Sync)
                {
                    float fx, fy, fz, rx, ry, rz, ux, uy, uz;
                    OmegaTriEngineMath.BuildCameraBasis(_state.CamYaw, _state.CamPitch, out fx, out fy, out fz, out rx, out ry, out rz, out ux, out uy, out uz);
                    float flatFx = (float)Math.Sin(_state.CamYaw);
                    float flatFz = (float)Math.Cos(_state.CamYaw);

                    float moveFwd = (kW ? 1.0f : 0.0f) - (kS ? 1.0f : 0.0f);
                    float moveStr = (kD ? 1.0f : 0.0f) - (kA ? 1.0f : 0.0f);
                    if (_state.FlyMode)
                    {
                        _state.CamX += (fx * moveFwd + rx * moveStr) * moveSpeed;
                        _state.CamY += (fy * moveFwd + ((kE ? 1.0f : 0.0f) - (kQ ? 1.0f : 0.0f))) * moveSpeed;
                        _state.CamZ += (fz * moveFwd + rz * moveStr) * moveSpeed;
                    }
                    else
                    {
                        float nx = _state.CamX + (flatFx * moveFwd + rx * moveStr) * moveSpeed;
                        float nz = _state.CamZ + (flatFz * moveFwd + rz * moveStr) * moveSpeed;
                        double rNext = Math.Sqrt(nx * nx + nz * nz);
                        if (rNext > 1.68 && rNext < 16.8)
                        {
                            _state.CamX = nx;
                            _state.CamZ = nz;
                        }
                        float targetEyeY = OmegaTriEngineMath.SampleArenaWalkHeight(_state.CamX, _state.CamZ) + 1.75f;
                        _state.CamY += (targetEyeY - _state.CamY) * Math.Min(1.0f, dt * 12.0f);
                    }
                }
            }

            // Native 1:1 viewport output resolution (supersampling happens in FP16 _hdrSceneTex before ACES + CAS!)
            int outW = Math.Max(64, _viewport.ClientSize.Width);
            int outH = Math.Max(64, _viewport.ClientSize.Height);
            float[] uSnap = _uSnap80;
            uint activeMask = OmegaTriEngineUniformBus.MaskOmegaEngine;
            if (_audio.SynthStationOnline) activeMask |= OmegaTriEngineUniformBus.MaskOmegaSynth;
            if (_audio.PenroseStationOnline) activeMask |= OmegaTriEngineUniformBus.MaskOmegaPenrose;

            ArenaSplineKeyframe splinePose = new ArenaSplineKeyframe();
            bool hasSplineOverride = false;
            lock (_state.Sync)
            {
                if (_state.SplinePlaybackActive)
                {
                    if (_state.KeyframeTrack.Count >= 2)
                    {
                        _state.SplinePlaybackCursorSec += dt;
                        float totalDur = _state.KeyframeTrack.TotalDurationSec;
                        if (!_state.KeyframeTrack.LoopClosed && _state.SplinePlaybackCursorSec > totalDur)
                        {
                            _state.SplinePlaybackCursorSec = 0.0;
                        }
                        if (_state.KeyframeTrack.EvaluateAtTime(_state.SplinePlaybackCursorSec, out splinePose))
                        {
                            hasSplineOverride = true;
                            _state.CamX = splinePose.CamX;
                            _state.CamY = splinePose.CamY;
                            _state.CamZ = splinePose.CamZ;
                            _state.CamYaw = splinePose.CamYaw;
                            _state.CamPitch = splinePose.CamPitch;
                            _state.FovDeg = splinePose.FovDeg;
                            _state.SymmetryMode = splinePose.SymmetryMode;
                            _state.K1 = splinePose.K1;
                            _state.K2 = splinePose.K2;
                            _state.K3 = splinePose.K3;
                            _state.K4 = splinePose.K4;
                            _state.WeatherAurora = splinePose.WeatherAurora;
                            _state.HdrBloomStrength = splinePose.HdrBloom;
                            _bloomStrength = splinePose.HdrBloom;
                            _state.CustomMix = splinePose.CustomMix;
                        }
                    }
                    else
                    {
                        _state.SplinePlaybackActive = false;
                    }
                }

                if (_state.ImpulseLiveStrength > 0.002f)
                {
                    _state.ImpulseAgeSec += dt;
                    _state.ImpulseRadius += dt * 11.5f;
                    _state.ImpulseLiveStrength = _state.ImpulsePeakStrength * (float)Math.Exp(-_state.ImpulseAgeSec * 1.15);
                    if (_state.ImpulseLiveStrength < 0.003f) _state.ImpulseLiveStrength = 0.0f;
                }

                double phasonT = nowSec * 0.22 * _state.K2;
                OmegaTriEngineMath.ComputeZeroSumPentagridShifts(phasonT, _state.Gamma5);

                float fx, fy, fz, rx, ry, rz, ux, uy, uz;
                OmegaTriEngineMath.BuildCameraBasis(_state.CamYaw, _state.CamPitch, out fx, out fy, out fz, out rx, out ry, out rz, out ux, out uy, out uz);

                float qx, qy, qz, qw;
                if (hasSplineOverride)
                {
                    qx = splinePose.QuatX; qy = splinePose.QuatY; qz = splinePose.QuatZ; qw = splinePose.QuatW;
                }
                else if (_audio.SpatialMmfOnline)
                {
                    qx = _audio.SpatialQuatX; qy = _audio.SpatialQuatY; qz = _audio.SpatialQuatZ; qw = _audio.SpatialQuatW;
                }
                else
                {
                    OmegaTriEngineMath.EulerYawPitchToQuat(_state.CamYaw, _state.CamPitch, 0.0f, out qx, out qy, out qz, out qw);
                }

                for (int k = 0; k < 8; k++) _feat8[k] = _audio.FftBands8[k] + 0.15 * Math.Cos(nowSec * 0.4 + k);
                double sqnr;
                _state.NearestE8Root = OmegaTriEngineMath.FindNearestE8RootFast(_feat8, out sqnr);
                _state.E8SqnrDb = sqnr;

                float fovTan = (float)Math.Tan((_state.FovDeg * Math.PI / 180.0) * 0.5);
                float[] U = _state.U;
                U[0] = outW; U[1] = outH; U[2] = (float)nowSec; U[3] = fovTan;
                U[4] = _state.CamX; U[5] = _state.CamY; U[6] = _state.CamZ; U[7] = _state.CamYaw;
                U[8] = _state.CamPitch; U[9] = 0.08f; U[10] = 180.0f; U[11] = _state.FlyMode ? 2.0f : 1.0f;
                U[12] = _audio.Bass; U[13] = _audio.Mid; U[14] = _audio.High; U[15] = _audio.Peak;
                for (int k = 0; k < 8; k++) U[16 + k] = _audio.FftBands8[k];
                U[24] = qx; U[25] = qy; U[26] = qz; U[27] = qw;
                U[28] = (float)_state.Gamma5[0]; U[29] = (float)_state.Gamma5[1]; U[30] = (float)_state.Gamma5[2]; U[31] = (float)_state.Gamma5[3];
                U[32] = (float)_state.Gamma5[4];
                U[33] = (float)(0.25 * Math.Sin(nowSec * 0.31) * _state.K2);
                U[34] = (float)(0.25 * Math.Cos(nowSec * 0.27) * _state.K2);
                U[35] = (float)(0.18 * Math.Sin(nowSec * 0.19));
                U[36] = _state.SymmetryMode; U[37] = _state.TileScale; U[38] = 0.032f; U[39] = 1.0f;
                U[40] = 0.20f; U[41] = 0.045f; U[42] = 0.58f; U[43] = -0.14f;
                U[44] = _audio.SynthChladniM; U[45] = _audio.SynthChladniN; U[46] = _audio.SynthBpm; U[47] = activeMask;
                U[48] = 0.65f; U[49] = _casSharpness; U[50] = (float)_state.E8SqnrDb; U[51] = _state.NearestE8Root;
                U[52] = _state.ImpulseOrigX; U[53] = _state.ImpulseOrigY; U[54] = _state.ImpulseOrigZ; U[55] = _state.ImpulseAgeSec;
                U[56] = _state.ImpulseLiveStrength; U[57] = _state.ImpulseRadius; U[58] = _state.ImpulseFreq; U[59] = _state.WeatherAurora;
                U[60] = _state.K1; U[61] = _state.K2; U[62] = _state.K3; U[63] = _state.K4;
                U[64] = 3.4f; U[65] = 0.055f; U[66] = 2.2f; U[67] = 0.38f;
                U[68] = 0.42f; U[69] = 0.78f; U[70] = -0.46f; U[71] = 0.013f;
                U[72] = fx; U[73] = fy; U[74] = fz; U[75] = 1.12f;
                U[76] = rx; U[77] = ry; U[78] = rz; U[79] = _state.CustomMix;
                Array.Copy(U, uSnap, 80);
                Array.Copy(U, _busMerge80, 80);
            }

            ulong seq; uint crc; uint liveMask; uint addonFrames;
            if (_mmfBus.PublishAndMergeMesh(_busMerge80, activeMask, out seq, out crc, out liveMask, out addonFrames))
            {
                uint snapApi = ((liveMask & OmegaTriEngineUniformBus.MaskReshadeAddon) != 0u)
                    ? _mmfBus.LastAddonApiVersion
                    : 0u;
                float skH1 = ((liveMask & OmegaTriEngineUniformBus.MaskSkse64Bridge) != 0u)
                    ? _busMerge80[67]
                    : 0.0f;
                lock (_state.Sync)
                {
                    _state.LastSeqlockSeq = seq;
                    _state.LastPayloadCrc32 = crc;
                    _state.LiveMeshMask = liveMask;
                    _state.ReshadeAddonFrames = addonFrames;
                    _state.ReshadeAddonApiVersion = snapApi;
                    _state.LastSheafH1State = skH1;
                }

                // Feed companion audio/phason/tensor channels into local Arena uSnap while keeping
                // Station 15's interactive WASD/Mouse camera (U[18..19]), Tile Mode (U[9].x),
                // atmospheric fog (U[17].w = 0.013), and smooth monolith spin (U[16].w = 0.38) untouched!
                uSnap[47] = _busMerge80[47];
                if ((liveMask & OmegaTriEngineUniformBus.MaskOmegaSynth) != 0u)
                {
                    for (int k = 12; k < 24; k++) uSnap[k] = _busMerge80[k];
                    uSnap[44] = _busMerge80[44];
                    uSnap[45] = _busMerge80[45];
                    uSnap[46] = _busMerge80[46];
                    uSnap[50] = _busMerge80[50];
                    uSnap[51] = _busMerge80[51];
                }
                if ((liveMask & OmegaTriEngineUniformBus.MaskOmegaPenrose) != 0u)
                {
                    for (int k = 28; k < 35; k++) uSnap[k] = _busMerge80[k];
                    if ((liveMask & OmegaTriEngineUniformBus.MaskOmegaTensor) == 0u) uSnap[35] = _busMerge80[35];
                }
                if ((liveMask & OmegaTriEngineUniformBus.MaskOmegaAv) != 0u)
                {
                    uSnap[48] = _busMerge80[48];
                }
                if ((liveMask & OmegaTriEngineUniformBus.MaskOmegaTensor) != 0u)
                {
                    uSnap[35] = _busMerge80[35];
                    if ((liveMask & OmegaTriEngineUniformBus.MaskOmegaSynth) == 0u)
                    {
                        uSnap[50] = _busMerge80[50];
                        uSnap[51] = _busMerge80[51];
                    }
                }
                if ((liveMask & OmegaTriEngineUniformBus.MaskSkse64Bridge) != 0u && !IsSkseProbeRunning())
                {
                    if (_busMerge80[56] > uSnap[56])
                    {
                        for (int k = 52; k < 59; k++) uSnap[k] = _busMerge80[k];
                    }
                }
            }

            if (_gpu.IsAvailable && _gpu.HasSwapChain)
            {
                _gpu.RenderArenaFrameEx(outW, outH, _renderScale, _bloomStrength, _enableRayCone, uSnap, true, _vsyncInterval, null);
            }
            else
            {
                int rw = Math.Max(160, outW / 2);
                int rh = Math.Max(90, outH / 2);
                if (_cpuPixels == null || _cpuPixels.Length != rw * rh)
                {
                    _cpuPixels = new int[rw * rh];
                    if (_cpuBmp != null) _cpuBmp.Dispose();
                    _cpuBmp = new Bitmap(rw, rh, PixelFormat.Format32bppPArgb);
                    _viewport.CpuFallbackBitmap = _cpuBmp;
                }
                uSnap[0] = rw;
                uSnap[1] = rh;
                if (_gpu.IsAvailable) _gpu.RenderArenaFrameEx(rw, rh, 1.0f, _bloomStrength, _enableRayCone, uSnap, false, 0u, _cpuPixels);
                else OmegaEngineDirectComputeGpu.RenderCpuFallbackArena(rw, rh, uSnap, _cpuPixels);

                BitmapData bd = _cpuBmp.LockBits(new Rectangle(0, 0, rw, rh), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try { Marshal.Copy(_cpuPixels, 0, bd.Scan0, rw * rh); }
                finally { _cpuBmp.UnlockBits(bd); }
                _viewport.Invalidate();
            }

            _fpsFrames++;
            double span = nowSec - _fpsWindowStart;
            if (span >= 0.45)
            {
                double fps = _fpsFrames / span;
                double fms = 1000.0 / Math.Max(1.0, fps);
                _fpsFrames = 0;
                _fpsWindowStart = nowSec;
                lock (_state.Sync)
                {
                    _state.LiveFps = fps;
                    _state.LiveFrameMs = fms;
                }
                if ((++_hudUpdateTick & 1) == 0)
                {
                    double ramMb = 0.0;
                    try { using (Process cur = Process.GetCurrentProcess()) { ramMb = cur.WorkingSet64 / (1024.0 * 1024.0); if (ramMb > 52.0) EmptyWorkingSetLocal(cur.Handle); } } catch { }
                    uint mMask = _state.LiveMeshMask;
                    string syTag = ((mMask & OmegaTriEngineUniformBus.MaskOmegaSynth) != 0u) ? "SHM" : (_audio.SynthStationOnline ? "HTTP" : "--");
                    string pnTag = ((mMask & OmegaTriEngineUniformBus.MaskOmegaPenrose) != 0u) ? "SHM" : (_audio.PenroseStationOnline ? "HTTP" : "--");
                    string avTag = ((mMask & OmegaTriEngineUniformBus.MaskOmegaAv) != 0u) ? "SHM" : "--";
                    string tnTag = ((mMask & OmegaTriEngineUniformBus.MaskOmegaTensor) != 0u) ? "SHM" : "--";
                    string pxTag = (((mMask & OmegaTriEngineUniformBus.MaskPixel10Live) != 0u) ? "10" : "-") + "/" + (((mMask & OmegaTriEngineUniformBus.MaskPixel8Live) != 0u) ? "8" : "-");
                    bool rsOn = (mMask & OmegaTriEngineUniformBus.MaskReshadeAddon) != 0u;
                    bool skOn = (mMask & OmegaTriEngineUniformBus.MaskSkse64Bridge) != 0u;
                    string rsTag = rsOn
                        ? ("LIVE#" + _state.ReshadeAddonFrames.ToString(CultureInfo.InvariantCulture))
                        : "idle";
                    string skTag = skOn ? "LIVE" : "--";
                    if (_btnRsProbe != null && !_btnRsProbe.IsDisposed)
                    {
                        bool probeAlive = IsRsProbeRunning();
                        _btnRsProbe.BackColor = probeAlive ? C(5, 150, 105) : C(51, 65, 85);
                        _btnRsProbe.Text = probeAlive ? "RS:ON" : "RS:LIVE";
                    }
                    if (_btnSkseProbe != null && !_btnSkseProbe.IsDisposed)
                    {
                        bool skAlive = IsSkseProbeRunning();
                        _btnSkseProbe.BackColor = skAlive ? C(2, 132, 199) : C(51, 65, 85);
                        _btnSkseProbe.Text = skAlive ? "SK:ON" : "SK:LIVE";
                    }
                    _lblTopHud.Text = string.Format(CultureInfo.InvariantCulture,
                        "{0:0.0} FPS ({1:0.00} ms) | {2:0.00}x HDR | RAM {3:0.0} MB | Seq #{4} | Sy:{5} Pn:{6} AV:{7} Tn:{8} Px:{9} RS:{10} SK:{11}",
                        fps, fms, _renderScale, ramMb, _state.LastSeqlockSeq,
                        syTag, pnTag, avTag, tnTag, pxTag, rsTag, skTag);
                }
            }
        }

        private void FirePlayerShockwave()
        {
            _state.TriggerShockwave(1.35f, 9.5f);
            _audio.TriggerShockwaveAcoustic(1.30f, 58.0f);
            _lblStatusBot.Text = string.Format(CultureInfo.InvariantCulture,
                "[THU'UM IMPULSE FIRED] World Origin=({0:0.0}, {1:0.0}, {2:0.0}) -> Streaming into U[13]..U[14] & 48 kHz Wavefield",
                _state.ImpulseOrigX, _state.ImpulseOrigY, _state.ImpulseOrigZ);
        }

        private void HotCompileFromEditor()
        {
            string code = _txtOmshEditor.Text ?? "";
            _gpu.StartAsyncArenaCompile(code);
            if (_gpu.IsAsyncCompileInFlight)
            {
                _lblCompileInfo.ForeColor = C(253, 224, 71);
                _lblCompileInfo.Text = "[ASYNC FXC...] Compiling cs_5_0 in background (viewport unlocked)...";
            }
            else
            {
                CheckAsyncCompileCompletion();
            }
        }

        public static string SaveGpuPngSnapshotStatic(OmegaEngineDirectComputeGpu gpu, string outDir, int w, int h, float[] u80)
        {
            Directory.CreateDirectory(outDir);
            string pngPath = Path.Combine(outDir, "OmegaEngine_Render_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".png");
            int[] px = new int[w * h];
            float[] uCopy = new float[80];
            Array.Copy(u80, uCopy, 80);
            uCopy[0] = w;
            uCopy[1] = h;
            if (gpu != null && gpu.IsAvailable)
            {
                gpu.RenderArenaFrameEx(w, h, 1.25f, 0.32f, true, uCopy, false, 0u, px);
            }
            else
            {
                OmegaEngineDirectComputeGpu.RenderCpuFallbackArena(w, h, uCopy, px);
            }
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { Marshal.Copy(px, 0, bd.Scan0, w * h); }
                finally { bmp.UnlockBits(bd); }
                bmp.Save(pngPath, ImageFormat.Png);
            }
            return pngPath;
        }

        private void ExportObjFromUi()
        {
            float k1, k2;
            lock (_state.Sync) { k1 = _state.K1; k2 = _state.K2; }
            int vc, fc;
            OmegaTriEngineMath.ExportWavefrontObjArena6D(_state.ExportsDir, k1, k2, out vc, out fc);
            string objShared = OmegaTriEngineMath.ExportWavefrontObjArena6D(_sharedExportsDir, k1, k2, out vc, out fc);
            _lblStatusBot.Text = string.Format(CultureInfo.InvariantCulture,
                "[3D MESH EXPORTED] {0} ({1} vertices, {2} triangles -> ready for Blender / NifSkope / Creation Kit)",
                objShared, vc, fc);
            _viewport.Focus();
        }

        private static void CopyFileSafe(string src, string dst)
        {
            try
            {
                if (!string.IsNullOrEmpty(src) && File.Exists(src) && !string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, true);
                }
            }
            catch { }
        }

        private void BakeDualContourObjFromUi(int gridRes)
        {
            float k1, k2, k3, qx, qy, qz, qw;
            int sym;
            lock (_state.Sync)
            {
                k1 = _state.K1; k2 = _state.K2; k3 = _state.K3;
                qx = _state.U[24]; qy = _state.U[25]; qz = _state.U[26]; qw = (Math.Abs(_state.U[27]) > 0.01f) ? _state.U[27] : 1.0f;
                sym = _state.SymmetryMode;
            }
            OmegaTriEngineMath.DualContourBakeResult dc = OmegaTriEngineMath.BakeHermiteDualContourObj(
                _sharedExportsDir, gridRes, sym, k1, k2, k3, qx, qy, qz, qw, null);
            CopyFileSafe(dc.ObjPath, Path.Combine(_state.ExportsDir, Path.GetFileName(dc.ObjPath)));
            CopyFileSafe(dc.MtlPath, Path.Combine(_state.ExportsDir, Path.GetFileName(dc.MtlPath)));
            lock (_state.Sync) { _state.LastExportedObjPath = dc.ObjPath; }
            OmegaEngineDirectComputeGpu.TrimWorkingSetNow();
            _lblStatusBot.Text = string.Format(CultureInfo.InvariantCulture,
                "[DUAL CONTOUR {0}^3] {1} | {2} verts, {3} tris, {4} sharp creases, 0 non-manifold ({5:0.0} ms)",
                dc.GridRes, dc.ObjPath, dc.VertexCount, dc.TriangleCount, dc.SharpCreaseCount, dc.ElapsedMs);
            _viewport.Focus();
        }

        private void BakePbr2048FromUi(bool directXFlipY)
        {
            float[] snap = new float[80];
            int sym;
            lock (_state.Sync) { Array.Copy(_state.U, snap, 80); sym = _state.SymmetryMode; }
            OmegaEngineDirectComputeGpu.PbrBakeResult pbr = _gpu.BakePbrMaterialSet(_sharedExportsDir, 2048, sym, directXFlipY, snap);
            CopyFileSafe(pbr.AlbedoPath, Path.Combine(_state.ExportsDir, Path.GetFileName(pbr.AlbedoPath)));
            CopyFileSafe(pbr.NormalPath, Path.Combine(_state.ExportsDir, Path.GetFileName(pbr.NormalPath)));
            CopyFileSafe(pbr.HeightPath, Path.Combine(_state.ExportsDir, Path.GetFileName(pbr.HeightPath)));
            CopyFileSafe(pbr.EmissivePath, Path.Combine(_state.ExportsDir, Path.GetFileName(pbr.EmissivePath)));
            CopyFileSafe(pbr.MtlPath, Path.Combine(_state.ExportsDir, Path.GetFileName(pbr.MtlPath)));
            lock (_state.Sync) { _state.LastBakedPbrPrefix = pbr.Prefix; }
            _lblStatusBot.Text = string.Format(CultureInfo.InvariantCulture,
                "[2048x2048 4-MAP PBR BAKED] {0}_[Albedo/Normal/Height/Emissive].png | GPU={1:0.00} ms, Total={2:0.0} ms",
                Path.Combine(_sharedExportsDir, pbr.Prefix), pbr.GpuDispatchMs, pbr.TotalMs);
            _viewport.Focus();
        }

        private void RunOneClickCompleteForgeFromUi()
        {
            float[] snap = new float[80];
            float k1, k2, k3, qx, qy, qz, qw;
            int sym;
            lock (_state.Sync)
            {
                Array.Copy(_state.U, snap, 80);
                k1 = _state.K1; k2 = _state.K2; k3 = _state.K3;
                qx = _state.U[24]; qy = _state.U[25]; qz = _state.U[26]; qw = (Math.Abs(_state.U[27]) > 0.01f) ? _state.U[27] : 1.0f;
                sym = _state.SymmetryMode;
            }
            OmegaEngineDirectComputeGpu.PbrBakeResult pbr = _gpu.BakePbrMaterialSet(_sharedExportsDir, 2048, sym, false, snap);
            OmegaTriEngineMath.DualContourBakeResult dc = OmegaTriEngineMath.BakeHermiteDualContourObj(
                _sharedExportsDir, 64, sym, k1, k2, k3, qx, qy, qz, qw, pbr.Prefix);
            CopyFileSafe(pbr.AlbedoPath, Path.Combine(_state.ExportsDir, Path.GetFileName(pbr.AlbedoPath)));
            CopyFileSafe(pbr.NormalPath, Path.Combine(_state.ExportsDir, Path.GetFileName(pbr.NormalPath)));
            CopyFileSafe(pbr.HeightPath, Path.Combine(_state.ExportsDir, Path.GetFileName(pbr.HeightPath)));
            CopyFileSafe(pbr.EmissivePath, Path.Combine(_state.ExportsDir, Path.GetFileName(pbr.EmissivePath)));
            CopyFileSafe(dc.ObjPath, Path.Combine(_state.ExportsDir, Path.GetFileName(dc.ObjPath)));
            CopyFileSafe(dc.MtlPath, Path.Combine(_state.ExportsDir, Path.GetFileName(dc.MtlPath)));
            OmegaEngineDirectComputeGpu.TrimWorkingSetNow();
            _lblStatusBot.Text = string.Format(CultureInfo.InvariantCulture,
                "[1-CLICK FORGE READY] {0} ({1} tris) + 4x 2048x2048 PBR PNGs ({2:0.0} ms total)",
                Path.GetFileName(dc.ObjPath), dc.TriangleCount, dc.ElapsedMs + pbr.TotalMs);
            _viewport.Focus();
        }

        private void SaveGpuPngSnapshotFromUi()
        {
            int w = Math.Max(640, _viewport.ClientSize.Width);
            int h = Math.Max(360, _viewport.ClientSize.Height);
            float[] snap = new float[80];
            lock (_state.Sync) { Array.Copy(_state.U, snap, 80); }
            string png = SaveGpuPngSnapshotStatic(_gpu, _sharedExportsDir, w, h, snap);
            _lblStatusBot.Text = "[GPU FP16 HDR PNG CAPTURED] Saved lossless frame -> " + png;
            _viewport.Focus();
        }

        private void LaunchCompanionSynthAndPenrose()
        {
            try
            {
                string syExe = Path.Combine(_suiteRoot, "04_omega_synth", "OmegaSynth.exe");
                string pnExe = Path.Combine(_suiteRoot, "07_omega_penrose", "OmegaPenrose.exe");
                string avExe = Path.Combine(_suiteRoot, "03_omega_av", "OmegaAV.exe");
                string tnExe = Path.Combine(_suiteRoot, "13_omega_tensor", "OmegaTensor.exe");
                if (File.Exists(syExe) && Process.GetProcessesByName("OmegaSynth").Length == 0)
                    Process.Start(new ProcessStartInfo(syExe) { WorkingDirectory = Path.GetDirectoryName(syExe) });
                if (File.Exists(pnExe) && Process.GetProcessesByName("OmegaPenrose").Length == 0)
                    Process.Start(new ProcessStartInfo(pnExe) { WorkingDirectory = Path.GetDirectoryName(pnExe) });
                if (File.Exists(avExe) && Process.GetProcessesByName("OmegaAV").Length == 0)
                    Process.Start(new ProcessStartInfo(avExe) { WorkingDirectory = Path.GetDirectoryName(avExe) });
                if (File.Exists(tnExe) && Process.GetProcessesByName("OmegaTensor").Length == 0)
                    Process.Start(new ProcessStartInfo(tnExe) { WorkingDirectory = Path.GetDirectoryName(tnExe) });
                _lblStatusBot.Text = "[5-STATION + DUAL-PIXEL MESH] Launched Synth (:19782) + Penrose (:19785) + AV (:19779) + OmegaTensor (:19791)!";
            }
            catch (Exception ex)
            {
                _lblStatusBot.Text = "[LINK NOTE] " + ex.Message;
            }
            _viewport.Focus();
        }

        private static readonly string[] QuickCyclePrompts = new string[]
        {
            "golden hopf villarceau torus with audio bass pulse and emerald rim",
            "frost sovngarde 24d leech golay dodecad wireframe with thuum shockwave",
            "crimson hyperbolic poincare {5,3,4} geodesic horizon with solar flare glow",
            "violet 4d 120-cell h4 polychoron chambers with iridescent prism aura",
            "plasma neon 8d e8 coxeter voronoi lattice with fast harmonic pulse"
        };
        private int _quickPromptCursor = 0;

        private void RequestTensorOmshCoPilot(string explicitPrompt)
        {
            if (_aiCoPilotInFlight)
            {
                _lblStatusBot.Text = "[AI .OMSH CO-PILOT] Background OpenAI v1 + FXC synthesis already running...";
                return;
            }

            string promptToUse = explicitPrompt;
            if (string.IsNullOrEmpty(promptToUse))
            {
                int idx = (_quickPromptCursor++) % QuickCyclePrompts.Length;
                promptToUse = QuickCyclePrompts[idx];
                if (_txtAiPrompt != null) _txtAiPrompt.Text = promptToUse;
            }

            int activeSym;
            lock (_state.Sync) { activeSym = _state.SymmetryMode; }
            string tnExe = Path.Combine(Path.Combine(_suiteRoot, "13_omega_tensor"), "OmegaTensor.exe");

            _aiCoPilotInFlight = true;
            if (_btnAiPromptGo != null) _btnAiPromptGo.Text = "FXC...";
            _lblCompileInfo.ForeColor = C(253, 224, 71);
            _lblCompileInfo.Text = "[AI :19791 -> FXC] Synthesizing & FXC-validating .omsh off-thread...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                OmshCoPilotResult cp = OmegaEngineOmshCoPilot.QueryCoPilotWithAutoFxc(
                    _gpu, promptToUse, activeSym, null, tnExe);
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((Action)delegate
                        {
                            _aiCoPilotInFlight = false;
                            if (_btnAiPromptGo != null) _btnAiPromptGo.Text = "Ask :19791";
                            if (cp.Ok && !string.IsNullOrEmpty(cp.OmshCode))
                            {
                                lock (_state.Sync)
                                {
                                    _state.LastAiPrompt = cp.Prompt;
                                    _state.LastAiModelSource = cp.ModelSource;
                                    _state.LastAiFxcValid = cp.FxcValidated;
                                    _state.LastAiAttempts = cp.Attempts;
                                    if (_state.CustomMix < 0.48f) _state.CustomMix = 0.48f;
                                }
                                _txtOmshEditor.Text = cp.OmshCode;
                                if (_trkOmshMix != null && _trkOmshMix.Value < 48) _trkOmshMix.Value = 48;
                                _lblStatusBot.Text = string.Format(CultureInfo.InvariantCulture,
                                    "[AI .OMSH VERIFIED] Source={0} | Attempts={1} | FXC={2:0.0} ms -> Async GPU Swap",
                                    cp.ModelSource, cp.Attempts, cp.FxcValidateMs);
                                HotCompileFromEditor();
                            }
                            else
                            {
                                _lblCompileInfo.ForeColor = C(252, 165, 165);
                                _lblCompileInfo.Text = "[AI .OMSH REJECTED] " + (cp.FinalError ?? "FXC validation failed");
                            }
                        });
                    }
                    else
                    {
                        _aiCoPilotInFlight = false;
                    }
                }
                catch
                {
                    _aiCoPilotInFlight = false;
                }
            });
            _viewport.Focus();
        }

        private void AddKeyframeFromUi()
        {
            int count = _state.KeyframeTrack.RecordFromArenaState(_state, 1.50f);
            if (_btnAddKf != null) _btnAddKf.Text = "+Key (" + count.ToString(CultureInfo.InvariantCulture) + ")";
            string jsonPath = Path.Combine(_sharedExportsDir, "OmegaKeyframeTrack.omkf.json");
            _state.KeyframeTrack.ExportTrackJson(jsonPath);
            CopyFileSafe(jsonPath, Path.Combine(_state.ExportsDir, "OmegaKeyframeTrack.omkf.json"));
            _lblStatusBot.Text = string.Format(CultureInfo.InvariantCulture,
                "[KEYFRAME #{0} RECORDED] Spline duration={1:0.00}s (Catmull-Rom XYZ + S^3 SQUAD Quat saved -> {2})",
                count, _state.KeyframeTrack.TotalDurationSec, jsonPath);
            _viewport.Focus();
        }

        private void ToggleSplinePlaybackFromUi()
        {
            bool nextPlay;
            lock (_state.Sync)
            {
                nextPlay = !_state.SplinePlaybackActive;
            }
            if (nextPlay && _state.KeyframeTrack.Count < 2)
            {
                int sym; float k1, k2, k3, k4, aur, blm, cmx;
                lock (_state.Sync)
                {
                    sym = _state.SymmetryMode;
                    k1 = _state.K1; k2 = _state.K2; k3 = _state.K3; k4 = _state.K4;
                    aur = _state.WeatherAurora; blm = _state.HdrBloomStrength; cmx = _state.CustomMix;
                }
                ArenaKeyframeTrack orb = ArenaKeyframeTrack.BuildCanonicalTurntableTrack(8, 9.6f, 2.65f, 8.0f, sym, k1, k2, k3, k4, aur, blm, cmx);
                _state.KeyframeTrack.Clear();
                ArenaSplineKeyframe[] ks = orb.GetKnotsSnapshot();
                for (int i = 0; i < ks.Length; i++) _state.KeyframeTrack.AddKeyframe(ks[i]);
                _state.KeyframeTrack.LoopClosed = true;
                if (_btnAddKf != null) _btnAddKf.Text = "+Key (" + ks.Length.ToString(CultureInfo.InvariantCulture) + ")";
            }
            lock (_state.Sync)
            {
                _state.SplinePlaybackCursorSec = 0.0;
                _state.SplinePlaybackActive = nextPlay;
            }
            if (_btnPlaySpline != null)
            {
                _btnPlaySpline.Text = nextPlay ? "Stop Spline" : "Play Spline";
                _btnPlaySpline.BackColor = nextPlay ? C(217, 119, 6) : C(5, 150, 105);
            }
            _lblStatusBot.Text = nextPlay
                ? string.Format(CultureInfo.InvariantCulture,
                    "[SPLINE PLAYBACK LIVE] Evaluating {0}-knot Catmull-Rom + S^3 SQUAD trajectory ({1:0.0}s loop) at 144 FPS!",
                    _state.KeyframeTrack.Count, _state.KeyframeTrack.TotalDurationSec)
                : "[SPLINE STOPPED] Returned camera & uniform control to interactive WASD / Mouse.";
            _viewport.Focus();
        }

        private void ClearKeyframesFromUi()
        {
            _state.KeyframeTrack.Clear();
            lock (_state.Sync)
            {
                _state.SplinePlaybackActive = false;
                _state.SplinePlaybackCursorSec = 0.0;
            }
            if (_btnAddKf != null) _btnAddKf.Text = "+Key (0)";
            if (_btnPlaySpline != null)
            {
                _btnPlaySpline.Text = "Play Spline";
                _btnPlaySpline.BackColor = C(5, 150, 105);
            }
            _lblStatusBot.Text = "[KEYFRAMES CLEARED] Ready to record new camera/uniform spline knots or auto-orbit turntable.";
            _viewport.Focus();
        }

        private TurntableSequenceResult RenderTurntableSequenceInternal(int frames, int wReq, int hReq, float fpsReq, int symOverride)
        {
            int sym; float k1, k2, k3, k4, aur, blm, cmx;
            lock (_state.Sync)
            {
                sym = (symOverride >= 0 && symOverride <= 7) ? symOverride : _state.SymmetryMode;
                k1 = _state.K1; k2 = _state.K2; k3 = _state.K3; k4 = _state.K4;
                aur = _state.WeatherAurora; blm = _state.HdrBloomStrength; cmx = _state.CustomMix;
            }
            ArenaKeyframeTrack activeTrack = (_state.KeyframeTrack.Count >= 2 && symOverride < 0)
                ? _state.KeyframeTrack
                : ArenaKeyframeTrack.BuildCanonicalTurntableTrack(8, 9.6f, 2.65f, frames / Math.Max(1.0f, fpsReq), sym, k1, k2, k3, k4, aur, blm, cmx);

            string stamp = string.Format(CultureInfo.InvariantCulture, "Turntable_Mode{0}_{1}",
                sym, DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            string outDir = Path.Combine(_sharedExportsDir, stamp);
            TurntableSequenceResult tt = OmegaTurntableSequenceRenderer.RenderKeyframeSequencePngs(
                _gpu, activeTrack, outDir, wReq, hReq, frames, fpsReq, 1.0f, _enableRayCone);

            string localMirror = Path.Combine(_state.ExportsDir, stamp);
            CopyFileSafe(tt.ManifestPath, Path.Combine(localMirror, "turntable_manifest.json"));
            CopyFileSafe(tt.TrackJsonPath, Path.Combine(localMirror, "turntable_spline.omkf.json"));
            if (tt.PngPaths.Count > 0)
            {
                CopyFileSafe(tt.PngPaths[0], Path.Combine(localMirror, Path.GetFileName(tt.PngPaths[0])));
            }
            lock (_state.Sync)
            {
                _state.LastTurntableDir = tt.OutputDir;
                _state.LastTurntableFrames = tt.FrameCount;
            }
            return tt;
        }

        private void RenderTurntableFromUi()
        {
            TurntableSequenceResult tt = RenderTurntableSequenceInternal(24, 640, 360, 24.0f, -1);
            _lblStatusBot.Text = string.Format(CultureInfo.InvariantCulture,
                "[TURNTABLE PNG SEQUENCE READY] {0} lossless frames ({1}x{2}) -> {3} ({4:0.0} ms)",
                tt.FrameCount, tt.Width, tt.Height, tt.OutputDir, tt.TotalMs);
            _viewport.Focus();
        }

        private void DeployBridgeToGameFromUi()
        {
            try
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Select Game Executable Folder (where SkyrimSE.exe / dxgi.dll / ReShade64 lives):";
                    SkyrimInstallInfo autoSky = OmegaEngineSkseModBuilder.DiscoverSkyrimAeInstallation("");
                    string[] steamGuesses = new string[]
                    {
                        autoSky.Found ? autoSky.GameDir : @"F:\SteamLibrary\steamapps\common\Skyrim Special Edition",
                        @"F:\SteamLibrary\steamapps\common\Skyrim Special Edition",
                        @"F:\Games\Skyrim Special Edition",
                        @"F:\Skyrim Special Edition",
                        @"E:\SteamLibrary\steamapps\common\Skyrim Special Edition",
                        @"D:\SteamLibrary\steamapps\common\Skyrim Special Edition",
                        @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition"
                    };
                    for (int i = 0; i < steamGuesses.Length; i++)
                    {
                        if (Directory.Exists(steamGuesses[i]))
                        {
                            fbd.SelectedPath = steamGuesses[i];
                            break;
                        }
                    }
                    if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(fbd.SelectedPath))
                    {
                        float[] snap = new float[80];
                        string code;
                        lock (_state.Sync) { Array.Copy(_state.U, snap, 80); code = _state.ActiveOmshSnippet; }
                        string detail;
                        bool ok = OmegaEngineHlslAndReshade.DeployBridgeToGameDirectory(fbd.SelectedPath, snap, code, out detail);
                        _lblStatusBot.Text = ok ? ("[GAME BRIDGE DEPLOYED] " + detail) : ("[DEPLOY NOTE] " + detail);
                    }
                }
            }
            catch (Exception ex)
            {
                _lblStatusBot.Text = "[DEPLOY ERROR] " + ex.Message;
            }
            _viewport.Focus();
        }

        private void ExportReshadeBundleFromUi()
        {
            float[] snap = new float[80];
            string code;
            lock (_state.Sync)
            {
                Array.Copy(_state.U, snap, 80);
                code = _state.ActiveOmshSnippet;
            }
            string stub1, sha1, stub2, sha2;
            string fxLocal = OmegaEngineHlslAndReshade.ExportReshadeFxAndAddonStub(_state.ExportsDir, snap, code, out stub1, out sha1);
            string fxShared = OmegaEngineHlslAndReshade.ExportReshadeFxAndAddonStub(_sharedExportsDir, snap, code, out stub2, out sha2);
            lock (_state.Sync) { _state.LastExportedFxPath = fxShared; }
            _lblStatusBot.Text = "Exported ReShade .fx -> " + fxShared + " (SHA256=" + sha2.Substring(0, 16) + " | Local copy: " + fxLocal + ")";
        }

        private void SyncControlsFromStateAndCompile()
        {
            _syncingUi = true;
            try
            {
                lock (_state.Sync)
                {
                    _cmbPreset.SelectedIndex = _state.PresetIndex;
                    _cmbSymmetry.SelectedIndex = _state.SymmetryMode;
                    _trkK1.Value = ClampTrack(_trkK1, (int)Math.Round(_state.K1 * 100.0f));
                    _trkK2.Value = ClampTrack(_trkK2, (int)Math.Round(_state.K2 * 100.0f));
                    _trkK3.Value = ClampTrack(_trkK3, (int)Math.Round(_state.K3 * 100.0f));
                    _trkK4.Value = ClampTrack(_trkK4, (int)Math.Round(_state.K4 * 100.0f));
                    _trkAurora.Value = ClampTrack(_trkAurora, (int)Math.Round(_state.WeatherAurora * 100.0f));
                    _trkBloom.Value = ClampTrack(_trkBloom, (int)Math.Round(_state.HdrBloomStrength * 100.0f));
                    _trkOmshMix.Value = ClampTrack(_trkOmshMix, (int)Math.Round(_state.CustomMix * 100.0f));
                    _txtOmshEditor.Text = _state.ActiveOmshSnippet;
                }
            }
            finally
            {
                _syncingUi = false;
            }
            HotCompileFromEditor();
        }

        private static int ClampTrack(TrackBar tb, int val)
        {
            return Math.Max(tb.Minimum, Math.Min(tb.Maximum, val));
        }

        private static Label MakeLbl(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = text;
            l.SetBounds(x, y, w, 20);
            l.Font = new Font("Segoe UI Semibold", 8.7f);
            l.ForeColor = C(203, 213, 225);
            return l;
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, Color bg, float ptSize)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.AutoEllipsis = false;
            b.Padding = new Padding(0);
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", ptSize);
            b.FlatAppearance.BorderColor = C(56, 189, 248);
            return b;
        }

        private static TrackBar AddSliderRow(Panel host, string title, ref int y, int min, int max, int val, Action<int> onVal)
        {
            Label lbl = MakeLbl(title, 10, y + 2, 132);
            Label valLbl = MakeLbl((val / 100.0).ToString("0.00", CultureInfo.InvariantCulture), 338, y + 2, 46);
            valLbl.Font = new Font("Consolas", 8.6f);
            valLbl.ForeColor = C(125, 211, 252);

            TrackBar tb = new TrackBar();
            tb.AutoSize = false;
            tb.TickStyle = TickStyle.None;
            tb.Minimum = min;
            tb.Maximum = max;
            tb.Value = val;
            tb.SetBounds(142, y, 194, 22);
            tb.ValueChanged += delegate
            {
                valLbl.Text = (tb.Value / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
                onVal(tb.Value);
            };

            host.Controls.Add(lbl);
            host.Controls.Add(tb);
            host.Controls.Add(valLbl);
            y += 26;
            return tb;
        }

        private bool IsRsProbeRunning()
        {
            try
            {
                return _rsProbeProc != null && !_rsProbeProc.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private void StopRsProbeProcess()
        {
            try
            {
                if (_rsProbeProc != null)
                {
                    if (!_rsProbeProc.HasExited)
                    {
                        try { _rsProbeProc.Kill(); } catch { }
                        try { _rsProbeProc.WaitForExit(500); } catch { }
                    }
                    _rsProbeProc.Dispose();
                    _rsProbeProc = null;
                }
            }
            catch { _rsProbeProc = null; }
        }

        private void SetRsProbeStateThreadSafe(bool enable)
        {
            if (!enable)
            {
                StopRsProbeProcess();
                return;
            }
            if (IsRsProbeRunning()) return;
            try
            {
                string selfExe = Application.ExecutablePath;
                ProcessStartInfo psi = new ProcessStartInfo(selfExe, "--reshade-host --parent-pid " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                _rsProbeProc = Process.Start(psi);
            }
            catch { }
        }

        private void ToggleRsLiveProbeFromUi()
        {
            bool next = !IsRsProbeRunning();
            SetRsProbeStateThreadSafe(next);
            _lblStatusBot.Text = next
                ? "[RS:LIVE PROBE ACTIVE] Cross-process Win64 host loaded OmegaTriEngine.addon64 (API v20, KERNEL32.dll only) over Local\\OmegaTriEngineUniforms!"
                : "[RS:LIVE PROBE STOPPED] Detached cross-process ReShade host; waiting ~2s heartbeat TTL -> RS:idle.";
            _viewport.Focus();
        }

        private bool IsSkseProbeRunning()
        {
            try
            {
                return _skseProbeProc != null && !_skseProbeProc.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private void StopSkseProbeProcess()
        {
            try
            {
                if (_skseProbeProc != null)
                {
                    if (!_skseProbeProc.HasExited)
                    {
                        try { _skseProbeProc.Kill(); } catch { }
                        try { _skseProbeProc.WaitForExit(500); } catch { }
                    }
                    _skseProbeProc.Dispose();
                    _skseProbeProc = null;
                }
            }
            catch { _skseProbeProc = null; }
        }

        private void SetSkseProbeStateThreadSafe(bool enable)
        {
            if (!enable)
            {
                StopSkseProbeProcess();
                return;
            }
            if (IsSkseProbeRunning()) return;
            try
            {
                string selfExe = Application.ExecutablePath;
                ProcessStartInfo psi = new ProcessStartInfo(selfExe, "--skse64-host --parent-pid " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                _skseProbeProc = Process.Start(psi);
            }
            catch { }
        }

        private void ToggleSkseLiveProbeFromUi()
        {
            bool next = !IsSkseProbeRunning();
            SetSkseProbeStateThreadSafe(next);
            _lblStatusBot.Text = next
                ? "[SK:LIVE PROBE ACTIVE] Cross-process SKSE64 2.2.6 host loaded OmegaSkyrimWorldBridge.dll (Sovngarde + Thu'um + H1=0 Guard -> U[13..19])!"
                : "[SK:LIVE PROBE STOPPED] Detached SKSE64 world-space host; waiting ~1.5s TTL -> SK:--.";
            _viewport.Focus();
        }

        private void BuildVortexModFromUi()
        {
            try
            {
                float[] snap = new float[80];
                string code;
                lock (_state.Sync) { Array.Copy(_state.U, snap, 80); code = _state.ActiveOmshSnippet; }
                string wsDir = Path.Combine(_suiteRoot, "workspace");
                SkyrimModBundleResult mb = OmegaEngineSkseModBuilder.BuildCompleteSkyrimModPackage(
                    _sharedExportsDir, "", wsDir, snap, code, false);
                lock (_state.Sync) { _state.LastVortexModZipPath = mb.VortexZipPath; }
                _lblStatusBot.Text = "[VORTEX MOD ARCHIVE READY] " + mb.Detail;
            }
            catch (Exception ex)
            {
                _lblStatusBot.Text = "[VORTEX BUILD ERROR] " + ex.Message;
            }
            _viewport.Focus();
        }

        [STAThread]
        public static int Main(string[] args)
        {
            bool startWithRsProbe = false;
            bool startWithSkseProbe = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--rslive-probe", StringComparison.OrdinalIgnoreCase))
                    {
                        startWithRsProbe = true;
                    }
                    if (string.Equals(args[i], "--skse-probe", StringComparison.OrdinalIgnoreCase))
                    {
                        startWithSkseProbe = true;
                    }
                    if (string.Equals(args[i], "--skse64-host", StringComparison.OrdinalIgnoreCase))
                    {
                        int verifyFrames = 0;
                        int parentPid = 0;
                        for (int j = i + 1; j < args.Length; j++)
                        {
                            if (string.Equals(args[j], "--verify-frames", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out verifyFrames);
                            else if (string.Equals(args[j], "--parent-pid", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parentPid);
                        }
                        return OmegaEngineStressSuite.RunCrossProcessSkse64HostLoop(verifyFrames, parentPid);
                    }
                    if (string.Equals(args[i], "--build-skyrim-mod", StringComparison.OrdinalIgnoreCase))
                    {
                        string gDir = "";
                        bool syncG = false;
                        for (int j = i + 1; j < args.Length; j++)
                        {
                            if (string.Equals(args[j], "--game-dir", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                gDir = args[j + 1];
                            else if (string.Equals(args[j], "--sync-game", StringComparison.OrdinalIgnoreCase))
                                syncG = true;
                        }
                        OmegaHardwareProfile p = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
                        string outDir = Path.Combine(p.DownloadsDir, "omega_engine_exports");
                        string wsDir = Path.Combine(p.LabRoot, "workspace");
                        SkyrimModBundleResult mb = OmegaEngineSkseModBuilder.BuildCompleteSkyrimModPackage(
                            outDir, gDir, wsDir,
                            OmegaEngineStressSuite.BuildCanonicalTestUniforms(1920, 1080, 1.0f, 0),
                            OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet,
                            syncG);
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "[SKYRIM_MOD_READY] ok={0} vortexZip={1} espBytes={2} pexCompiledViaCk={3} detail={4}",
                            mb.Ok, mb.VortexZipPath, mb.EspBytes, mb.PexCompiledViaCk, mb.Detail));
                        return mb.Ok ? 0 : 1;
                    }
                    if (string.Equals(args[i], "--reshade-host", StringComparison.OrdinalIgnoreCase))
                    {
                        int verifyFrames = 0;
                        int parentPid = 0;
                        for (int j = i + 1; j < args.Length; j++)
                        {
                            if (string.Equals(args[j], "--verify-frames", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                            {
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out verifyFrames);
                            }
                            else if (string.Equals(args[j], "--parent-pid", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                            {
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parentPid);
                            }
                        }
                        return OmegaEngineStressSuite.RunCrossProcessReshadeHostLoop(verifyFrames, parentPid);
                    }
                    if (string.Equals(args[i], "--warm-shader-cache", StringComparison.OrdinalIgnoreCase))
                    {
                        using (OmegaEngineDirectComputeGpu warmGpu = new OmegaEngineDirectComputeGpu())
                        {
                            Stopwatch swW = Stopwatch.StartNew();
                            bool okW = warmGpu.Initialize(IntPtr.Zero, 128, 72, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
                            int countW = okW ? warmGpu.WarmAllShadersAndPresets() : 0;
                            swW.Stop();
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "[DXBC_CACHE_WARM] ok={0} files={1} dir={2} initShaderMs={3:0.000} totalMs={4:0.0} fxcCalls={5}",
                                okW, countW, warmGpu.ShaderCacheDir, warmGpu.InitShaderLoadMs, swW.Elapsed.TotalMilliseconds, warmGpu.D3DCompileInvocations));
                            return okW ? 0 : 1;
                        }
                    }
                    if (string.Equals(args[i], "--bench-cold-boot", StringComparison.OrdinalIgnoreCase))
                    {
                        using (OmegaEngineDirectComputeGpu bootGpu = new OmegaEngineDirectComputeGpu())
                        {
                            bool okB = bootGpu.Initialize(IntPtr.Zero, 128, 72, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
                            Stopwatch swPresets = Stopwatch.StartNew();
                            if (okB)
                            {
                                for (int p = 0; p < OmegaEngineHlslAndReshade.BuiltInPresets.Length; p++)
                                {
                                    bootGpu.CompileArenaShader(OmegaEngineHlslAndReshade.BuiltInPresets[p].CustomOmshSnippet);
                                }
                            }
                            swPresets.Stop();
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "[DXBC_COLD_BOOT_BENCH] ok={0} shaderLoadMs={1:0.000} preset8SwapMs={2:0.000} d3dDeviceInitMs={3:0.00} hits={4} fxcCalls={5} dxbcFiles={6}",
                                okB, bootGpu.InitShaderLoadMs, swPresets.Elapsed.TotalMilliseconds, bootGpu.InitTotalMs,
                                bootGpu.DiskCacheHits, bootGpu.D3DCompileInvocations, bootGpu.CountCachedDxbcFiles()));
                            return (okB && bootGpu.D3DCompileInvocations == 0) ? 0 : 1;
                        }
                    }
                    if (string.Equals(args[i], "--stress-test", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            EngineStressSummary sum = OmegaEngineStressSuite.RunAll12Vectors();
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "[OMEGAENGINE v1.8.0 44-VECTOR PHASE-2 AUDIT] {0}/{1} PASS | {2} trials | RAM={3:0.00} MB ({4:0.0} ms)",
                                sum.PassedCount, sum.TotalCount, sum.TotalTrials, sum.TrimmedWorkingSetMb, sum.TotalMs));
                            for (int k = 0; k < sum.Vectors.Count; k++)
                            {
                                EngineStressVector v = sum.Vectors[k];
                                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                    "  [{0}] {1,-34} -> {2}", v.Passed ? "PASS" : "FAIL", v.Code, v.Detail));
                            }
                            Console.Out.Flush();
                            return sum.AllPassed ? 0 : 1;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine("[OMEGAENGINE STRESS ERROR] " + ex.Message);
                            return 2;
                        }
                    }
                    if (string.Equals(args[i], "--export-fx", StringComparison.OrdinalIgnoreCase))
                    {
                        OmegaHardwareProfile p = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
                        string outDir = Path.Combine(p.DownloadsDir, "omega_engine_exports");
                        string stub, sha;
                        string fx = OmegaEngineHlslAndReshade.ExportReshadeFxAndAddonStub(
                            outDir, OmegaEngineStressSuite.BuildCanonicalTestUniforms(1920, 1080, 1.0f, 0),
                            OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet, out stub, out sha);
                        Console.WriteLine("Exported: " + fx + " (SHA256=" + sha + ")");
                        return 0;
                    }
                    if (string.Equals(args[i], "--ai-omsh", StringComparison.OrdinalIgnoreCase))
                    {
                        string promptArg = (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                            ? args[i + 1]
                            : "golden hopf villarceau torus with audio bass pulse";
                        int symArg = 4;
                        string epArg = null;
                        for (int j = i + 1; j < args.Length; j++)
                        {
                            if (string.Equals(args[j], "--sym", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out symArg);
                            else if (string.Equals(args[j], "--endpoint", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                epArg = args[j + 1];
                        }
                        using (OmegaEngineDirectComputeGpu aiGpu = new OmegaEngineDirectComputeGpu())
                        {
                            aiGpu.Initialize(IntPtr.Zero, 128, 72, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
                            OmshCoPilotResult cp = OmegaEngineOmshCoPilot.QueryCoPilotWithAutoFxc(aiGpu, promptArg, symArg, epArg, null);
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "[AI_OMSH_READY] ok={0} fxcValid={1} attempts={2} fxcMs={3:0.00} source={4}",
                                cp.Ok, cp.FxcValidated, cp.Attempts, cp.FxcValidateMs, cp.ModelSource));
                            return (cp.Ok && cp.FxcValidated) ? 0 : 1;
                        }
                    }
                    if (string.Equals(args[i], "--render-turntable", StringComparison.OrdinalIgnoreCase))
                    {
                        int framesT = 12, wT = 640, hT = 360, symT = 4;
                        float fpsT = 24.0f;
                        string outT = "";
                        for (int j = i + 1; j < args.Length; j++)
                        {
                            if (string.Equals(args[j], "--frames", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out framesT);
                            else if (string.Equals(args[j], "--w", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out wT);
                            else if (string.Equals(args[j], "--h", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hT);
                            else if (string.Equals(args[j], "--sym", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out symT);
                            else if (string.Equals(args[j], "--fps", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                float.TryParse(args[j + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out fpsT);
                            else if (string.Equals(args[j], "--out", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                outT = args[j + 1];
                        }
                        OmegaHardwareProfile p = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
                        if (string.IsNullOrEmpty(outT))
                        {
                            outT = Path.Combine(Path.Combine(p.DownloadsDir, "omega_engine_exports"), "Turntable_Showcase_Mode" + symT.ToString(CultureInfo.InvariantCulture));
                        }
                        using (OmegaEngineDirectComputeGpu ttGpu = new OmegaEngineDirectComputeGpu())
                        {
                            ttGpu.Initialize(IntPtr.Zero, wT, hT, OmegaEngineHlslAndReshade.BuiltInPresets[Math.Max(0, Math.Min(7, symT))].CustomOmshSnippet);
                            ArenaKeyframeTrack trk = ArenaKeyframeTrack.BuildCanonicalTurntableTrack(
                                8, 9.6f, 2.65f, framesT / Math.Max(1.0f, fpsT), symT, 1.15f, 1.05f, 0.90f, 1.62f, 0.82f, 0.32f, 0.35f);
                            TurntableSequenceResult tt = OmegaTurntableSequenceRenderer.RenderKeyframeSequencePngs(
                                ttGpu, trk, outT, wT, hT, framesT, fpsT, 1.0f, true);
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "[TURNTABLE_COMPLETE] ok={0} frames={1} res={2}x{3} dir={4} totalMs={5:0.0} sha0={6}",
                                tt.Ok, tt.FrameCount, tt.Width, tt.Height, tt.OutputDir, tt.TotalMs,
                                tt.FrameSha256.Count > 0 ? tt.FrameSha256[0].Substring(0, 16) : "none"));
                            return tt.Ok ? 0 : 1;
                        }
                    }
                    if (string.Equals(args[i], "--bake-forge", StringComparison.OrdinalIgnoreCase))
                    {
                        int gridR = 64, texR = 2048, symM = 0;
                        for (int j = i + 1; j < args.Length; j++)
                        {
                            if (string.Equals(args[j], "--grid", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out gridR);
                            else if (string.Equals(args[j], "--tex", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out texR);
                            else if (string.Equals(args[j], "--sym", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                                int.TryParse(args[j + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out symM);
                        }
                        OmegaHardwareProfile p = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
                        string outDir = Path.Combine(p.DownloadsDir, "omega_engine_exports");
                        using (OmegaEngineDirectComputeGpu fgGpu = new OmegaEngineDirectComputeGpu())
                        {
                            fgGpu.Initialize(IntPtr.Zero, 128, 72, OmegaEngineHlslAndReshade.BuiltInPresets[0].CustomOmshSnippet);
                            float[] uBase = OmegaEngineStressSuite.BuildCanonicalTestUniforms(1920, 1080, 1.0f, symM);
                            OmegaEngineDirectComputeGpu.PbrBakeResult pbr = fgGpu.BakePbrMaterialSet(outDir, texR, symM, false, uBase);
                            OmegaTriEngineMath.DualContourBakeResult dc = OmegaTriEngineMath.BakeHermiteDualContourObj(
                                outDir, gridR, symM, 1.0f, 1.0f, 0.85f, 0.0f, 0.0f, 0.0f, 1.0f, pbr.Prefix);
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "[FORGE_COMPLETE] ok={0} obj={1} verts={2} tris={3} tex={4}x{4} gpuMs={5:0.00} dcMs={6:0.0}",
                                pbr.Ok && dc.Ok, dc.ObjPath, dc.VertexCount, dc.TriangleCount, pbr.TexSize, pbr.GpuDispatchMs, dc.ElapsedMs));
                            return (pbr.Ok && dc.Ok) ? 0 : 1;
                        }
                    }
                }
            }

            try { SetProcessDPIAware(); } catch { }
            OmegaHardwareProfile prof = OmegaHardwareConfig.LoadAndApplyEnvironment(false);
            string engDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            bool topBarOnlyMode = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--top-bar", StringComparison.OrdinalIgnoreCase))
                    {
                        topBarOnlyMode = true;
                    }
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (topBarOnlyMode)
            {
                OmegaEngineArenaState tbState = new OmegaEngineArenaState(Path.Combine(engDir, "exports"));
                using (OmegaTriEngineUniformBus tbBus = new OmegaTriEngineUniformBus(OmegaTriEngineUniformBus.DefaultMapName))
                using (OmegaTopBarController tbCtrl = new OmegaTopBarController(tbState, tbBus))
                {
                    tbCtrl.ShowDesktopBar(true);
                    tbCtrl.StartTimer60Hz();
                    tbCtrl.BarForm.FormClosed += delegate { Application.ExitThread(); };
                    Application.Run();
                }
                return 0;
            }
            Application.Run(new OmegaEngineCockpitForm(engDir, prof, startWithRsProbe, startWithSkseProbe));
            return 0;
        }
    }
}