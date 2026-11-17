using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OmegaConductorStudio
{
    public sealed class OmegaConductorCoordinator : IDisposable
    {
        private readonly string _baseDir;
        private readonly string _suiteRoot;
        private readonly object _lk = new object();
        private readonly ConductorZeroCopySharedBus _shm;
        private readonly DopplerGestureClassifier _fsm = new DopplerGestureClassifier();
        private readonly ConductorHudSnapshot _snap = new ConductorHudSnapshot();

        private QuatS3 _simQ = QuatS3.Identity;
        private QuatS3 _liveQ = QuatS3.Identity;
        private QuatS3 _smoothedQ = QuatS3.Identity;
        private long _lastExternalWandTicks = 0;
        private long _lastSonarTicks = 0;
        private double _extRangeM = 0.64;
        private double _extDopplerMps = 0.0;
        private double _extAzimuthDeg = 0.0;
        private double _extAccelG = 0.15;
        private string _extGestureHint = "";
        private string _extSourceName = "";

        private int _trailHead = 0;
        private int _dispatchDivider = 0;
        private volatile bool _probing = false;
        private volatile bool _dispatching = false;
        public volatile bool ConductLiveEnabled = true;
        public volatile int BatonInputMode = 0; // 0 = Auto (Pixel10/Sonar/Figure-8), 1 = Mouse 3D Baton, 2 = Pure 15.965Hz Lissajous
        public volatile bool TriggerTensorOnPulse = false;

        private SpatialGestureKind _prevDispatchedGesture = SpatialGestureKind.CONDUCT_GLIDE;

        public ConductorZeroCopySharedBus SharedBus { get { return _shm; } }

        public OmegaConductorCoordinator(string baseDir)
        {
            _baseDir = baseDir;
            DirectoryInfo parent = Directory.GetParent(baseDir.TrimEnd('\\', '/'));
            _suiteRoot = parent != null ? parent.FullName : baseDir;
            _shm = new ConductorZeroCopySharedBus(ConductorZeroCopySharedBus.DefaultMapName);

            string[] names = new string[]
            {
                "OmegaAV v0.7.5 (:19779)",
                "OmegaSynth v1.1.1 (:19782)",
                "OmegaPenrose v1.0.2 (:19785)",
                "OmegaTensor v1.0.2 (:19791)"
            };
            int[] ports = new int[] { 19779, 19782, 19785, 19791 };
            string[] roles = new string[]
            {
                "24D Leech Orbit + Fluid Splash/Mode Steering",
                "MPE Pitch/Roll Wavefield + Chladni (m,n) Morph",
                "8D->3D Quasicrystal Phason + Icosahedral Orbit",
                "5-Engine 9.0 TOPS Edge Sensor Sync & Burst"
            };
            for (int i = 0; i < 4; i++)
            {
                ConductorTargetTelemetry t = new ConductorTargetTelemetry();
                t.Index = i;
                t.Name = names[i];
                t.Port = ports[i];
                t.Role = roles[i];
                _snap.Targets[i] = t;
            }
        }

        public void IngestExternalWandPacket(double yawDeg, double pitchDeg, double rollDeg, double accelG, string gestureHint, string srcLabel)
        {
            lock (_lk)
            {
                _liveQ = QuatS3.FromEulerDeg(yawDeg, pitchDeg, rollDeg);
                _extAccelG = Math.Max(0.0, accelG);
                if (!string.IsNullOrEmpty(gestureHint)) _extGestureHint = gestureHint;
                _extSourceName = string.IsNullOrEmpty(srcLabel) ? "Pixel 10 OLED Baton (:19790)" : srcLabel;
                _lastExternalWandTicks = DateTime.UtcNow.Ticks;
            }
        }

        public void SetMouseWandRay(double normX, double normY, bool pulseClick)
        {
            double yaw = Math.Max(-85.0, Math.Min(85.0, normX * 75.0));
            double pitch = Math.Max(-75.0, Math.Min(75.0, normY * 65.0));
            double roll = normX * normY * 45.0;
            IngestExternalWandPacket(yaw, pitch, roll, pulseClick ? 2.9 : 0.2, pulseClick ? "DOUBLE_TAP_PULSE" : "", "Viewport 3D Mouse Baton");
        }

        public void FireManualGesture(SpatialGestureKind g)
        {
            lock (_lk)
            {
                _extGestureHint = g.ToString();
                if (g == SpatialGestureKind.DOUBLE_TAP_PULSE) _extAccelG = 3.0;
            }
        }

        public void StepFusionTick(double timeSec, double dtSec)
        {
            ConductorBusFrame busFrame = new ConductorBusFrame();
            bool shouldFanOut = false;
            HopfRayS2 hopfCopy;
            SpatialGestureKind gestCopy;
            double yawCopy, pitchCopy, rollCopy, dopplerCopy, rangeCopy;
            bool gestureChanged = false;

            lock (_lk)
            {
                // 15.965 Hz carrier harmonic figure-8 choreography baseline
                double wCarrier = 2.0 * Math.PI * (15.965 / 32.0);
                double simYaw = 48.0 * Math.Sin(wCarrier * timeSec);
                double simPitch = 34.0 * Math.Sin(2.0 * wCarrier * timeSec + 0.4);
                double simRoll = 28.0 * Math.Cos(wCarrier * timeSec);
                _simQ = QuatS3.FromEulerDeg(simYaw, simPitch, simRoll);

                long nowTicks = DateTime.UtcNow.Ticks;
                bool freshExt = (nowTicks - _lastExternalWandTicks) < TimeSpan.FromSeconds(3.5).Ticks;
                bool freshSonar = (nowTicks - _lastSonarTicks) < TimeSpan.FromSeconds(3.5).Ticks;

                QuatS3 targetQ;
                if (BatonInputMode == 2)
                {
                    targetQ = _simQ;
                    _snap.InputSource = "15.965Hz Lissajous S^3 Baton";
                }
                else if (freshExt)
                {
                    targetQ = _liveQ;
                    _snap.InputSource = _extSourceName + (freshSonar ? " + Golay Sonar (:19783)" : "");
                }
                else if (freshSonar)
                {
                    targetQ = _liveQ;
                    _snap.InputSource = "OmegaSonar (:19783) 19kHz Golay + 6-DoF";
                }
                else
                {
                    targetQ = _simQ;
                    _snap.InputSource = "Auto 15.965Hz S^3 Figure-8 (Wave P10 or Drag Viewport)";
                }

                QuatS3 prevQ = _smoothedQ;
                _smoothedQ = QuatS3.Slerp(_smoothedQ, targetQ, 0.32);
                double angRad = QuatS3.GeodesicAngleRad(prevQ, _smoothedQ);
                double omegaRadS = angRad / Math.Max(0.004, dtSec);

                double rangeM = freshSonar ? _extRangeM : (0.68 + 0.24 * Math.Cos(wCarrier * timeSec * 0.8));
                double dopplerMps = freshSonar ? _extDopplerMps : (-0.22 * Math.Sin(wCarrier * timeSec * 0.8));
                double azDeg = freshSonar ? _extAzimuthDeg : simYaw * 0.65;

                string hint = _extGestureHint;
                _extGestureHint = "";
                double accG = _extAccelG;
                _extAccelG *= 0.65;

                SpatialGestureKind gest = _fsm.ClassifyFrame(rangeM, dopplerMps, omegaRadS, accG, hint);
                HopfRayS2 hopf = OmegaConductorMath.ProjectHopfS3ToS2(_smoothedQ);

                double yDeg, pDeg, rDeg;
                _smoothedQ.ToEulerDeg(out yDeg, out pDeg, out rDeg);

                double[] x8 = new double[8]
                {
                    _smoothedQ.W, _smoothedQ.X, _smoothedQ.Y, _smoothedQ.Z,
                    hopf.Rx, hopf.Ry, rangeM * 0.5, dopplerMps
                };
                double[] q8 = new double[8];
                int e8Idx;
                OmegaConductorMath.QuantizeE8Spatial8D(x8, 2.0, q8, out e8Idx);

                double[][] stalks = new double[4][];
                for (int s = 0; s < 4; s++)
                    stalks[s] = new double[4] { q8[0], q8[1], q8[4], q8[5] };
                double maxCob;
                string sheafSum;
                bool sheafOk = OmegaConductorMath.AuditFourTargetSheaf(stalks, 1e-4, out maxCob, out sheafSum);

                busFrame.UtcTicks = nowTicks;
                busFrame.Quat = _smoothedQ;
                busFrame.Hopf = hopf;
                busFrame.RangeM = rangeM;
                busFrame.DopplerMps = dopplerMps;
                busFrame.AzimuthDeg = azDeg;
                busFrame.Gesture = gest;
                busFrame.E8RootIndex = e8Idx;

                int slot = _shm.WriteFrameLockFree(ref busFrame);
                for (int i = 0; i < 64; i++)
                    _snap.RingOccupancy[i] *= 0.965f;
                _snap.RingOccupancy[slot & 63] = 1.0f;

                // Shift 64-point trail
                _trailHead = (_trailHead + 1) & 63;
                for (int i = 0; i < 63; i++)
                {
                    _snap.TrailXY[i * 2 + 0] = _snap.TrailXY[(i + 1) * 2 + 0];
                    _snap.TrailXY[i * 2 + 1] = _snap.TrailXY[(i + 1) * 2 + 1];
                }
                _snap.TrailXY[63 * 2 + 0] = (float)hopf.Rx;
                _snap.TrailXY[63 * 2 + 1] = (float)hopf.Ry;

                _snap.Quat = _smoothedQ;
                _snap.Hopf = hopf;
                _snap.YawDeg = yDeg;
                _snap.PitchDeg = pDeg;
                _snap.RollDeg = rDeg;
                _snap.RangeM = rangeM;
                _snap.DopplerMps = dopplerMps;
                _snap.AzimuthDeg = azDeg;
                _snap.Gesture = gest;
                _snap.GestureCount = _fsm.TotalGestureTransitions;
                _snap.E8RootIndex = e8Idx;
                _snap.ShmSequence = busFrame.Sequence;
                _snap.ActiveShmSlot = slot;
                _snap.SheafH1Zero = sheafOk;
                _snap.SheafSummary = sheafSum;

                hopfCopy = hopf;
                gestCopy = gest;
                yawCopy = yDeg;
                pitchCopy = pDeg;
                rollCopy = rDeg;
                dopplerCopy = dopplerMps;
                rangeCopy = rangeM;

                if (gest != _prevDispatchedGesture)
                {
                    gestureChanged = true;
                    _prevDispatchedGesture = gest;
                }

                _dispatchDivider++;
                if (ConductLiveEnabled && !_dispatching && (gestureChanged || (_dispatchDivider % 4) == 0))
                    shouldFanOut = true;
            }

            if (shouldFanOut)
            {
                _dispatching = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        DispatchConductionFanOut(hopfCopy, gestCopy, gestureChanged, yawCopy, pitchCopy, rollCopy, rangeCopy, dopplerCopy);
                    }
                    catch { }
                    finally { _dispatching = false; }
                });
            }
        }

        private void DispatchConductionFanOut(HopfRayS2 hopf, SpatialGestureKind gest, bool gestureChanged, double yaw, double pitch, double roll, double rangeM, double dopplerMps)
        {
            string uStr = hopf.U01.ToString("0.000", CultureInfo.InvariantCulture);
            string vStr = hopf.V01.ToString("0.000", CultureInfo.InvariantCulture);
            string rxStr = hopf.Rx.ToString("0.000", CultureInfo.InvariantCulture);
            string ryStr = hopf.Ry.ToString("0.000", CultureInfo.InvariantCulture);
            string twStr = hopf.FiberTwistRad.ToString("0.000", CultureInfo.InvariantCulture);

            // 0: OmegaAV (:19779)
            if (_snap.Targets[0].Online)
            {
                Stopwatch sw = Stopwatch.StartNew();
                string ep = "http://127.0.0.1:19779/api/wand?x=" + rxStr + "&y=" + ryStr + "&twist=" + twStr;
                string act = "Orbit (" + rxStr + "," + ryStr + ")";
                if (gestureChanged && gest == SpatialGestureKind.DOUBLE_TAP_PULSE)
                {
                    ep = "http://127.0.0.1:19779/api/splash";
                    act = "24D Fluid Splash!";
                }
                else if (gestureChanged && gest == SpatialGestureKind.ORBIT_SWEEP)
                {
                    ep = "http://127.0.0.1:19779/api/fluid";
                    act = "Cycle Fluid Shader!";
                }
                string r = HttpGetFast(ep, 260);
                sw.Stop();
                if (!string.IsNullOrEmpty(r))
                {
                    lock (_lk)
                    {
                        _snap.Targets[0].PacketsRouted++;
                        _snap.Targets[0].LastRttMs = sw.Elapsed.TotalMilliseconds;
                        _snap.Targets[0].LastAction = act;
                    }
                }
            }

            // 1: OmegaSynth (:19782)
            if (_snap.Targets[1].Online && gest != SpatialGestureKind.HOVER_LOCK)
            {
                Stopwatch sw = Stopwatch.StartNew();
                double expr = Math.Max(0.25, Math.Min(1.0, 0.65 - dopplerMps * 0.5));
                string url = string.Format(CultureInfo.InvariantCulture,
                    "http://127.0.0.1:19782/api/mpe?gben=1&gb={0:0.0}&gg={1:0.0}&kx={2}&ky={3}&t=77:{2}:{3}:{4:0.00}",
                    pitch, roll, uStr, vStr, expr);
                string r = HttpGetFast(url, 260);
                sw.Stop();
                if (!string.IsNullOrEmpty(r))
                {
                    lock (_lk)
                    {
                        _snap.Targets[1].PacketsRouted++;
                        _snap.Targets[1].LastRttMs = sw.Elapsed.TotalMilliseconds;
                        _snap.Targets[1].LastAction = "MPE Wavefield (" + uStr + "," + vStr + ")";
                    }
                }
            }

            // 2: OmegaPenrose (:19785)
            if (_snap.Targets[2].Online && gest != SpatialGestureKind.HOVER_LOCK)
            {
                Stopwatch sw = Stopwatch.StartNew();
                int kMode = 0; // 5D/8D Phason
                if (gest == SpatialGestureKind.ORBIT_SWEEP) kMode = 2; // 3D Jewel Orbit
                else if (gest == SpatialGestureKind.DOUBLE_TAP_PULSE) kMode = 1; // Psi Wave Stamp
                string url = "http://127.0.0.1:19785/api/touch?x=" + uStr + "&y=" + vStr + "&k=" + kMode;
                string r = HttpGetFast(url, 260);
                sw.Stop();
                if (!string.IsNullOrEmpty(r))
                {
                    lock (_lk)
                    {
                        _snap.Targets[2].PacketsRouted++;
                        _snap.Targets[2].LastRttMs = sw.Elapsed.TotalMilliseconds;
                        _snap.Targets[2].LastAction = (kMode == 2 ? "3D Jewel Orbit" : (kMode == 1 ? "Psi Stamp!" : "8D Phason")) + " (" + uStr + "," + vStr + ")";
                    }
                }
            }

            // 3: OmegaTensor (:19791) + Pixel 10 (:19788) Sensor Sync
            if (_snap.Targets[3].Online)
            {
                if (gestureChanged && gest == SpatialGestureKind.DOUBLE_TAP_PULSE && TriggerTensorOnPulse)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    string r = HttpGetFast("http://127.0.0.1:19791/api/dispatch?prompt=Conductor+6DoF+Downbeat+Burst", 2800);
                    sw.Stop();
                    if (!string.IsNullOrEmpty(r))
                    {
                        lock (_lk)
                        {
                            _snap.Targets[3].PacketsRouted++;
                            _snap.Targets[3].LastRttMs = sw.Elapsed.TotalMilliseconds;
                            _snap.Targets[3].LastAction = "Fired 5-Engine Split Burst!";
                        }
                    }
                }
            }
        }

        public void PollStationsAndSonarAsync(Action onUpdated)
        {
            if (_probing) return;
            _probing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // 1. Poll upstream OmegaSonar (:19783) if online
                    if (OmegaConductorMath.ProbeTcpNoHandleLeak(19783, 90))
                    {
                        string sJs = HttpGetFast("http://127.0.0.1:19783/api/status", 450);
                        if (!string.IsNullOrEmpty(sJs) && sJs.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            double rm = ExtractNum(sJs, "range_m", 0.65);
                            double vm = ExtractNum(sJs, "velocity_mps", 0.0);
                            double az = ExtractNum(sJs, "azimuth_deg", 0.0);
                            string gs = ExtractStr(sJs, "gesture");
                            double qw = ExtractNum(sJs, "qw", 1.0);
                            double qx = ExtractNum(sJs, "qx", 0.0);
                            double qy = ExtractNum(sJs, "qy", 0.0);
                            double qz = ExtractNum(sJs, "qz", 0.0);
                            lock (_lk)
                            {
                                _extRangeM = rm;
                                _extDopplerMps = vm;
                                _extAzimuthDeg = az;
                                if ((DateTime.UtcNow.Ticks - _lastExternalWandTicks) > TimeSpan.FromSeconds(2.0).Ticks)
                                    _liveQ = new QuatS3(qw, qx, qy, qz).Normalize();
                                if (!string.IsNullOrEmpty(gs) && gs.IndexOf("IDLE", StringComparison.OrdinalIgnoreCase) < 0 && gs.IndexOf("NONE", StringComparison.OrdinalIgnoreCase) < 0)
                                    _extGestureHint = gs;
                                _lastSonarTicks = DateTime.UtcNow.Ticks;
                            }
                        }
                    }

                    // 2. Poll 4 downstream target ports with zero-handle-leak Socket.Poll
                    int[] ports = new int[] { 19779, 19782, 19785, 19791 };
                    bool[] states = new bool[4];
                    for (int i = 0; i < 4; i++)
                        states[i] = OmegaConductorMath.ProbeTcpNoHandleLeak(ports[i], 90);

                    // 3. Push compact fused 6-DoF telemetry frame into Pixel 10 worker :19788 if live
                    if (OmegaConductorMath.ProbeTcpNoHandleLeak(19788, 90))
                    {
                        ConductorHudSnapshot c = GetSnapshotCopy();
                        string sensPayload = string.Format(CultureInfo.InvariantCulture,
                            "{{\"source\":\"12_omega_conductor_19790\",\"gesture\":\"{0}\",\"range_m\":{1:0.000},\"doppler_mps\":{2:0.000},\"hopf\":[{3:0.000},{4:0.000},{5:0.000}],\"shm_seq\":{6}}}",
                            c.Gesture, c.RangeM, c.DopplerMps, c.Hopf.Rx, c.Hopf.Ry, c.Hopf.Rz, c.ShmSequence);
                        HttpPostFast("http://127.0.0.1:19788/api/sensors/ingest", sensPayload, 350);
                    }

                    lock (_lk)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            _snap.Targets[i].Online = states[i];
                            if (states[i] && _snap.Targets[i].LastAction == "Standby")
                                _snap.Targets[i].LastAction = "Connected & Conducting";
                            else if (!states[i])
                                _snap.Targets[i].LastAction = "Offline (Click '3. Wake Trio')";
                        }
                    }

                    if (onUpdated != null) onUpdated();
                }
                catch { }
                finally { _probing = false; }
            });
        }

        public ConductorHudSnapshot GetSnapshotCopy()
        {
            lock (_lk)
            {
                ConductorHudSnapshot c = new ConductorHudSnapshot();
                c.Quat = _snap.Quat;
                c.Hopf = _snap.Hopf;
                c.YawDeg = _snap.YawDeg;
                c.PitchDeg = _snap.PitchDeg;
                c.RollDeg = _snap.RollDeg;
                c.RangeM = _snap.RangeM;
                c.DopplerMps = _snap.DopplerMps;
                c.AzimuthDeg = _snap.AzimuthDeg;
                c.Gesture = _snap.Gesture;
                c.GestureCount = _snap.GestureCount;
                c.E8RootIndex = _snap.E8RootIndex;
                c.E8SqnrDb = _snap.E8SqnrDb;
                c.GolayHealedBits = _snap.GolayHealedBits;
                c.ShmSequence = _snap.ShmSequence;
                c.ActiveShmSlot = _snap.ActiveShmSlot;
                c.SheafH1Zero = _snap.SheafH1Zero;
                c.SheafSummary = _snap.SheafSummary;
                c.InputSource = _snap.InputSource;
                Array.Copy(_snap.TrailXY, c.TrailXY, 128);
                Array.Copy(_snap.RingOccupancy, c.RingOccupancy, 64);
                for (int i = 0; i < 4; i++)
                {
                    ConductorTargetTelemetry s = _snap.Targets[i];
                    ConductorTargetTelemetry d = new ConductorTargetTelemetry();
                    d.Index = s.Index;
                    d.Name = s.Name;
                    d.Port = s.Port;
                    d.Role = s.Role;
                    d.Online = s.Online;
                    d.PacketsRouted = s.PacketsRouted;
                    d.LastRttMs = s.LastRttMs;
                    d.LastAction = s.LastAction;
                    c.Targets[i] = d;
                }
                return c;
            }
        }

        public string WakeCreativeTrioStations()
        {
            string[][] apps = new string[][]
            {
                new string[] { "OmegaAV", Path.Combine(_suiteRoot, "03_omega_av", "OmegaAV.exe") },
                new string[] { "OmegaSynth", Path.Combine(_suiteRoot, "04_omega_synth", "OmegaSynth.exe") },
                new string[] { "OmegaPenrose", Path.Combine(_suiteRoot, "07_omega_penrose", "OmegaPenrose.exe") },
                new string[] { "OmegaSonar", Path.Combine(_suiteRoot, "05_omega_sonar", "OmegaSonar.exe") }
            };
            List<string> started = new List<string>();
            for (int i = 0; i < apps.Length; i++)
            {
                try
                {
                    Process[] existing = Process.GetProcessesByName(apps[i][0]);
                    if (existing != null && existing.Length > 0)
                    {
                        started.Add(apps[i][0] + "(already live)");
                        continue;
                    }
                    if (File.Exists(apps[i][1]))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(apps[i][1]);
                        psi.WorkingDirectory = Path.GetDirectoryName(apps[i][1]);
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                        started.Add(apps[i][0] + "(launched)");
                    }
                }
                catch { }
            }
            return "[CONDUCTOR STAGE] " + string.Join(", ", started.ToArray());
        }

        private static double ExtractNum(string js, string key, double defVal)
        {
            Match m = Regex.Match(js ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            double v;
            return (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) ? v : defVal;
        }

        private static string ExtractStr(string js, string key)
        {
            Match m = Regex.Match(js ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string HttpGetFast(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Proxy = null;
                r.KeepAlive = false;
                r.Method = "GET";
                r.Timeout = timeoutMs;
                r.ReadWriteTimeout = timeoutMs;
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        private static void HttpPostFast(string url, string jsonBody, int timeoutMs)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Proxy = null;
                r.KeepAlive = false;
                r.Method = "POST";
                r.ContentType = "application/json";
                r.Timeout = timeoutMs;
                r.ReadWriteTimeout = timeoutMs;
                byte[] b = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
                r.ContentLength = b.Length;
                using (Stream rs = r.GetRequestStream()) rs.Write(b, 0, b.Length);
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse()) { }
            }
            catch { }
        }

        public void Dispose()
        {
            _shm.Dispose();
        }
    }

    public sealed class OmegaConductorBusServer : IDisposable
    {
        public const int DefaultPort = 19790;
        private readonly int _port;
        private readonly OmegaConductorCoordinator _coord;
        private TcpListener _tcp;
        private UdpClient _udp;
        private volatile bool _running;

        public OmegaConductorBusServer(int port, OmegaConductorCoordinator coord)
        {
            _port = port;
            _coord = coord;
        }

        public bool Start()
        {
            bool tcpOk = false;
            try
            {
                _tcp = new TcpListener(IPAddress.Loopback, _port);
                _tcp.Start();
                _running = true;
                Thread thTcp = new Thread(AcceptTcpLoop);
                thTcp.IsBackground = true;
                thTcp.Name = "OmegaConductorHttp19790";
                thTcp.Start();
                tcpOk = true;
            }
            catch { }

            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, _port));
                Thread thUdp = new Thread(ReceiveUdpLoop);
                thUdp.IsBackground = true;
                thUdp.Name = "OmegaConductorUdp19790";
                thUdp.Start();
            }
            catch { }

            return tcpOk;
        }

        private void AcceptTcpLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient cli = _tcp.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleTcpClient, cli);
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void ReceiveUdpLoop()
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            while (_running && _udp != null)
            {
                try
                {
                    byte[] dgram = _udp.Receive(ref ep);
                    if (dgram != null && dgram.Length > 0)
                    {
                        string js = Encoding.UTF8.GetString(dgram);
                        ParseAndIngestWandPayload("", js, "UDP :19790 Fast Baton");
                    }
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void HandleTcpClient(object state)
        {
            TcpClient cli = state as TcpClient;
            if (cli == null) return;
            using (cli)
            {
                try
                {
                    cli.ReceiveTimeout = 3000;
                    cli.SendTimeout = 3000;
                    NetworkStream ns = cli.GetStream();
                    byte[] buf = new byte[8192];
                    int n = ns.Read(buf, 0, buf.Length);
                    if (n <= 0) return;

                    string raw = Encoding.UTF8.GetString(buf, 0, n);
                    int eol = raw.IndexOf('\n');
                    string first = (eol > 0 ? raw.Substring(0, eol) : raw).Trim();
                    string[] parts = first.Split(' ');
                    string method = parts.Length > 0 ? parts[0].ToUpperInvariant() : "GET";
                    string fullUrl = parts.Length > 1 ? parts[1] : "/";
                    int qPos = fullUrl.IndexOf('?');
                    string path = qPos >= 0 ? fullUrl.Substring(0, qPos) : fullUrl;
                    string query = qPos >= 0 ? fullUrl.Substring(qPos + 1) : "";
                    int bodyIdx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    string body = bodyIdx >= 0 ? raw.Substring(bodyIdx + 4).Trim() : "";

                    if (path == "/api/health" || path == "/api/status")
                    {
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", FormatStatusJson(_coord.GetSnapshotCopy()));
                        return;
                    }

                    if (path == "/api/wand" || path == "/api/imu")
                    {
                        ParseAndIngestWandPayload(query, body, "Pixel 10 OLED Baton (:19790)");
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", "{\"ok\":true}");
                        return;
                    }

                    if (path == "/api/gesture")
                    {
                        string gName = ExtractParam(query, "g");
                        if (string.IsNullOrEmpty(gName)) gName = body;
                        SpatialGestureKind gk = SpatialGestureKind.DOUBLE_TAP_PULSE;
                        if (gName.IndexOf("ORBIT", StringComparison.OrdinalIgnoreCase) >= 0) gk = SpatialGestureKind.ORBIT_SWEEP;
                        else if (gName.IndexOf("HOVER", StringComparison.OrdinalIgnoreCase) >= 0) gk = SpatialGestureKind.HOVER_LOCK;
                        else if (gName.IndexOf("SURGE", StringComparison.OrdinalIgnoreCase) >= 0) gk = SpatialGestureKind.SURGE_CRESCENDO;
                        _coord.FireManualGesture(gk);
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", "{\"ok\":true,\"gesture\":\"" + gk + "\"}");
                        return;
                    }

                    if (path == "/api/shm")
                    {
                        ConductorBusFrame bf;
                        int retries;
                        bool ok = _coord.SharedBus.TryReadFrameLockFree(out bf, out retries);
                        string shmJs = string.Format(CultureInfo.InvariantCulture,
                            "{{\"ok\":{0},\"map\":\"{1}\",\"seq\":{2},\"retries\":{3},\"gesture\":\"{4}\",\"e8_root\":{5},\"golay24\":\"0x{6:X6}\",\"crc32\":\"0x{7:X8}\"}}",
                            ok ? "true" : "false", JsonEsc(_coord.SharedBus.MapName), bf.Sequence, retries, bf.Gesture, bf.E8RootIndex, bf.GolayCodeword24, bf.Crc32);
                        WriteHttp(ns, "200 OK", "application/json; charset=utf-8", shmJs);
                        return;
                    }

                    WriteHttp(ns, "200 OK", "text/html; charset=utf-8", BuildMobileConductorBatonHtml());
                }
                catch { }
            }
        }

        private void ParseAndIngestWandPayload(string query, string body, string src)
        {
            double yaw = ParseNumAny(query, body, "alpha", ParseNumAny(query, body, "yaw", 0.0));
            double pitch = ParseNumAny(query, body, "beta", ParseNumAny(query, body, "pitch", 0.0));
            double roll = ParseNumAny(query, body, "gamma", ParseNumAny(query, body, "roll", 0.0));
            double acc = ParseNumAny(query, body, "acc", 0.2);
            string gest = ExtractParam(query, "g");
            if (string.IsNullOrEmpty(gest))
            {
                Match mg = Regex.Match(body ?? "", "\"gesture\"\\s*:\\s*\"([^\"]+)\"");
                if (mg.Success) gest = mg.Groups[1].Value;
            }
            _coord.IngestExternalWandPacket(yaw, pitch, roll, acc, gest, src);
        }

        private static double ParseNumAny(string query, string body, string key, double defVal)
        {
            string qv = ExtractParam(query, key);
            double v;
            if (!string.IsNullOrEmpty(qv) && double.TryParse(qv, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            Match m = Regex.Match(body ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return defVal;
        }

        private static string ExtractParam(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return "";
            string[] parts = query.Split('&');
            string pfx = key + "=";
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(parts[i].Substring(pfx.Length).Replace('+', ' '));
            }
            return "";
        }

        private static string FormatStatusJson(ConductorHudSnapshot s)
        {
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\"ok\":true,\"service\":\"OmegaConductor v1.0.0 GM\",\"port\":19790,");
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "\"shm_map\":\"Local\\\\ACT_OMEGA_CONDUCTOR_6DOF_BUS_V1\",\"shm_seq\":{0},\"gesture\":\"{1}\",\"gesture_transitions\":{2},\"range_m\":{3:0.000},\"doppler_mps\":{4:0.000},\"quat\":[{5:0.0000},{6:0.0000},{7:0.0000},{8:0.0000}],\"hopf_s2\":[{9:0.0000},{10:0.0000},{11:0.0000}],\"fiber_twist_rad\":{12:0.000},\"e8_root\":{13},\"sheaf_h1_zero\":{14},\"sheaf\":\"{15}\",\"source\":\"{16}\",\"targets\":[",
                s.ShmSequence, s.Gesture, s.GestureCount, s.RangeM, s.DopplerMps,
                s.Quat.W, s.Quat.X, s.Quat.Y, s.Quat.Z,
                s.Hopf.Rx, s.Hopf.Ry, s.Hopf.Rz, s.Hopf.FiberTwistRad,
                s.E8RootIndex, s.SheafH1Zero ? "true" : "false", JsonEsc(s.SheafSummary), JsonEsc(s.InputSource));
            for (int i = 0; i < 4; i++)
            {
                ConductorTargetTelemetry t = s.Targets[i];
                if (i > 0) sb.Append(",");
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"name\":\"{0}\",\"port\":{1},\"online\":{2},\"routed\":{3},\"rtt_ms\":{4:0.0},\"action\":\"{5}\"}}",
                    JsonEsc(t.Name), t.Port, t.Online ? "true" : "false", t.PacketsRouted, t.LastRttMs, JsonEsc(t.LastAction));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string BuildMobileConductorBatonHtml()
        {
            return "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1,user-scalable=no'>" +
                "<title>OmegaConductor :19790 OLED 6-DoF Baton</title>" +
                "<style>body{margin:0;padding:12px;background:#060a14;color:#e2e8f0;font-family:Consolas,monospace;touch-action:none}" +
                "h2{margin:0 0 8px;color:#34d399;font-size:16px}.card{background:#0f172a;border:1px solid #1e293b;border-radius:10px;padding:10px;margin-bottom:10px}" +
                "#pad{height:44vh;background:radial-gradient(circle,#132e4a 0%,#060913 80%);border:1px solid #38bdf8;border-radius:12px;display:flex;align-items:center;justify-content:center;color:#7dd3fc;font-weight:700}" +
                "button{background:#0284c7;color:#fff;border:1px solid #38bdf8;border-radius:8px;padding:10px 11px;font-weight:700;margin:3px;width:47%}</style></head><body>" +
                "<h2>OMEGACONDUCTOR v1.0.0 (:19790) :: 6-DoF WAND</h2>" +
                "<div class='card' id='st'>Tap 'Arm 6-DoF IMU' or drag the Hopf Conduction Pad below</div>" +
                "<div id='pad'>DRAG / WAVE 6-DoF HOPF RAY (AV + SYNTH + PENROSE)</div>" +
                "<div style='margin-top:8px;text-align:center'>" +
                "<button onclick='arm()' style='background:#059669;width:96%'>1. Arm 60Hz Pixel 10 IMU + Downbeat Sensor</button>" +
                "<button onclick='gest(\"DOUBLE_TAP_PULSE\")' style='background:#b45309'>DOUBLE_TAP_PULSE</button>" +
                "<button onclick='gest(\"ORBIT_SWEEP\")' style='background:#0369a1'>ORBIT_SWEEP</button>" +
                "<button onclick='gest(\"HOVER_LOCK\")' style='background:#6d28d9'>HOVER_LOCK</button>" +
                "<button onclick='gest(\"SURGE_CRESCENDO\")' style='background:#be185d'>SURGE_CRESCENDO</button></div>" +
                "<script>var busy=false,lastAcc=0.2;function sendW(y,p,r,g){if(busy&&!g)return;busy=true;" +
                "fetch('/api/wand',{method:'POST',body:JSON.stringify({yaw:y,pitch:p,roll:r,acc:lastAcc,gesture:g||''})}).finally(function(){busy=false;});}" +
                "function gest(g){fetch('/api/gesture?g='+g);}" +
                "function arm(){if(window.DeviceOrientationEvent&&DeviceOrientationEvent.requestPermission){DeviceOrientationEvent.requestPermission();}" +
                "window.addEventListener('deviceorientation',function(e){sendW((e.alpha||0)-180,e.beta||0,e.gamma||0,'');});" +
                "window.addEventListener('devicemotion',function(e){var a=e.accelerationIncludingGravity;if(a){lastAcc=Math.sqrt(a.x*a.x+a.y*a.y+a.z*a.z)/9.81;}});}" +
                "var pd=document.getElementById('pad');function pt(cx,cy){var rc=pd.getBoundingClientRect();" +
                "var nx=((cx-rc.left)/rc.width)*2-1,ny=1-((cy-rc.top)/rc.height)*2;sendW(nx*75,ny*65,nx*ny*40,'');}" +
                "pd.addEventListener('touchmove',function(e){e.preventDefault();if(e.touches.length>0)pt(e.touches[0].clientX,e.touches[0].clientY);});" +
                "pd.addEventListener('mousemove',function(e){if(e.buttons===1)pt(e.clientX,e.clientY);});" +
                "setInterval(async function(){try{var r=await fetch('/api/status');var j=await r.json();" +
                "document.getElementById('st').innerHTML='<b>Gesture: '+j.gesture+'</b> | SHM #'+j.shm_seq+' | Range: '+j.range_m.toFixed(2)+'m<br>'+j.sheaf;}catch(x){}},900);</script></body></html>";
        }

        private static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static void WriteHttp(NetworkStream ns, string code, string ctype, string body)
        {
            byte[] b = Encoding.UTF8.GetBytes(body ?? "");
            string hdr = "HTTP/1.1 " + code + "\r\nContent-Type: " + ctype + "\r\nContent-Length: " + b.Length +
                "\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(hdr);
            ns.Write(hb, 0, hb.Length);
            ns.Write(b, 0, b.Length);
            ns.Flush();
        }

        public void Dispose()
        {
            _running = false;
            try { if (_tcp != null) _tcp.Stop(); } catch { }
            try { if (_udp != null) _udp.Close(); } catch { }
        }
    }

    public sealed class ConductorViewportPanel : Panel
    {
        public ConductorViewportPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            DoubleBuffered = false;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
    }

    public sealed class OmegaConductorCockpitForm : Form
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        private readonly string _baseDir;
        private readonly OmegaConductorCoordinator _coord;
        private readonly OmegaConductorDirectComputeGpu _gpu;
        private readonly OmegaConductorBusServer _server;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer _frameTimer;
        private readonly System.Windows.Forms.Timer _probeTimer;

        private readonly Label _lblTitle;
        private readonly Label _lblMetrics;
        private readonly ComboBox _cmbMode;
        private readonly ComboBox _cmbGesture;
        private readonly Button _btnConduct;
        private readonly CheckBox _chkTensorBeat;
        private readonly ConductorViewportPanel _vp;
        private readonly DataGridView _gridTargets;
        private readonly RichTextBox _rtbLog;

        private double _prevSec = 0.0;
        private bool _mouseDragging = false;
        private string _prevMetricLine = "";

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public OmegaConductorCockpitForm(string baseDir)
        {
            _baseDir = baseDir;
            _coord = new OmegaConductorCoordinator(baseDir);
            _gpu = new OmegaConductorDirectComputeGpu();
            _server = new OmegaConductorBusServer(OmegaConductorBusServer.DefaultPort, _coord);

            AutoScaleMode = AutoScaleMode.None;
            Text = "ACT-Omega Lab :: OmegaConductor v1.0.0 GM (:19790) -- Cross-Station Spatial Gesture & 6-DoF Hopf S^3 Wand Bus";
            ClientSize = new Size(1240, 820);
            MinimumSize = new Size(1060, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C(8, 12, 22);
            ForeColor = Color.White;
            KeyPreview = true;

            try
            {
                DirectoryInfo parent = Directory.GetParent(baseDir.TrimEnd('\\', '/'));
                string ico = Path.Combine(parent != null ? parent.FullName : baseDir, "icons", "omega_conductor.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 96;
            topBar.BackColor = C(12, 18, 34);

            _lblTitle = new Label();
            _lblTitle.UseMnemonic = false;
            _lblTitle.SetBounds(14, 6, 1210, 22);
            _lblTitle.Font = new Font("Segoe UI Semibold", 13.0f, FontStyle.Bold, GraphicsUnit.Pixel);
            _lblTitle.ForeColor = C(52, 211, 153);
            _lblTitle.Text = "OMEGACONDUCTOR v1.0.0 GM (:19790) :: CROSS-STATION 19 kHz GOLAY GESTURE & S^3->S^2 HOPF WAND BUS";

            _lblMetrics = new Label();
            _lblMetrics.UseMnemonic = false;
            _lblMetrics.SetBounds(14, 29, 1210, 20);
            _lblMetrics.Font = new Font("Consolas", 11.3f, FontStyle.Bold, GraphicsUnit.Pixel);
            _lblMetrics.ForeColor = C(56, 189, 248);
            _lblMetrics.Text = "Initializing Local\\ACT_OMEGA_CONDUCTOR_6DOF_BUS_V1 Seqlock Ring & Probing Stations...";

            _cmbMode = new ComboBox();
            _cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbMode.SetBounds(14, 56, 196, 26);
            _cmbMode.BackColor = C(6, 9, 17);
            _cmbMode.ForeColor = Color.White;
            _cmbMode.Font = new Font("Consolas", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cmbMode.Items.AddRange(new object[] { "Auto: P10 + Sonar + Lissajous", "Mouse 3D Viewport Baton", "Pure 15.965Hz S^3 Figure-8" });
            _cmbMode.SelectedIndex = 0;
            _cmbMode.SelectedIndexChanged += delegate { _coord.BatonInputMode = _cmbMode.SelectedIndex; };

            _cmbGesture = new ComboBox();
            _cmbGesture.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbGesture.SetBounds(218, 56, 154, 26);
            _cmbGesture.BackColor = C(6, 9, 17);
            _cmbGesture.ForeColor = C(250, 204, 21);
            _cmbGesture.Font = new Font("Consolas", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cmbGesture.Items.AddRange(new object[] { "DOUBLE_TAP_PULSE", "ORBIT_SWEEP", "HOVER_LOCK", "SURGE_CRESCENDO" });
            _cmbGesture.SelectedIndex = 0;

            Button bFire = MakeBtn("1. Fire Gesture [Space]", 380, 54, 156, C(180, 83, 9));
            bFire.Click += delegate { TriggerSelectedGesture(); };

            _btnConduct = MakeBtn("2. Conduct: LIVE", 544, 54, 132, C(5, 150, 105));
            _btnConduct.Click += delegate
            {
                _coord.ConductLiveEnabled = !_coord.ConductLiveEnabled;
                _btnConduct.Text = _coord.ConductLiveEnabled ? "2. Conduct: LIVE" : "2. Conduct: PAUSED";
                _btnConduct.BackColor = _coord.ConductLiveEnabled ? C(5, 150, 105) : C(71, 85, 105);
            };

            Button bWakeTrio = MakeBtn("3. Wake Trio (AV+Synth+Penrose)", 684, 54, 204, C(109, 40, 217));
            bWakeTrio.Click += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string msg = _coord.WakeCreativeTrioStations();
                    AppendLog(msg, C(192, 132, 252));
                });
            };

            Button bBeam = MakeBtn("4. Beam P10 Baton", 896, 54, 132, C(2, 132, 199));
            bBeam.Click += delegate { BeamBatonToPixel10Async(); };

            Button bStress = MakeBtn("5. 8-Vec Stress", 1036, 54, 104, C(147, 51, 234));
            bStress.Click += delegate { RunStressSuiteAsync(); };

            Button bPng = MakeBtn("6. PNG", 1148, 54, 78, C(14, 116, 144));
            bPng.Click += delegate
            {
                string fn = _gpu.SaveDiagnosticSnapshotPng(_baseDir, _coord.GetSnapshotCopy());
                AppendLog("[SNAPSHOT SAVED] " + fn, C(56, 189, 248));
            };

            topBar.Controls.AddRange(new Control[]
            {
                _lblTitle, _lblMetrics, _cmbMode, _cmbGesture,
                bFire, _btnConduct, bWakeTrio, bBeam, bStress, bPng
            });

            Panel subBar = new Panel();
            subBar.Dock = DockStyle.Top;
            subBar.Height = 32;
            subBar.BackColor = C(15, 23, 42);

            Label lHint = new Label();
            lHint.UseMnemonic = false;
            lHint.SetBounds(14, 7, 860, 18);
            lHint.Font = new Font("Consolas", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            lHint.ForeColor = C(167, 243, 208);
            lHint.Text = "Zero-Copy SHM: Local\\ACT_OMEGA_CONDUCTOR_6DOF_BUS_V1 (4,096B Seqlock) | Tip: Click/Drag left 3D dome to steer Hopf wand ray live!";

            _chkTensorBeat = new CheckBox();
            _chkTensorBeat.Text = "Fire OmegaTensor (:19791) Split Burst on DOUBLE_TAP_PULSE";
            _chkTensorBeat.SetBounds(884, 5, 344, 22);
            _chkTensorBeat.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _chkTensorBeat.Font = new Font("Segoe UI Semibold", 10.8f, FontStyle.Regular, GraphicsUnit.Pixel);
            _chkTensorBeat.ForeColor = C(250, 204, 21);
            _chkTensorBeat.CheckedChanged += delegate { _coord.TriggerTensorOnPulse = _chkTensorBeat.Checked; };

            subBar.Controls.AddRange(new Control[] { lHint, _chkTensorBeat });

            SplitContainer mainSplit = new SplitContainer();
            mainSplit.Dock = DockStyle.Fill;
            mainSplit.Orientation = Orientation.Horizontal;
            mainSplit.BackColor = C(30, 41, 59);
            mainSplit.SplitterWidth = 5;

            _vp = new ConductorViewportPanel();
            _vp.Dock = DockStyle.Fill;
            _vp.BackColor = C(7, 11, 20);
            _vp.Cursor = Cursors.Cross;
            _vp.Paint += delegate(object s, PaintEventArgs pe)
            {
                if (!_gpu.IsGpuActive)
                    _gpu.RenderSoftwareFallback(pe.Graphics, _vp.ClientSize.Width, _vp.ClientSize.Height, (float)_clock.Elapsed.TotalSeconds, _coord.GetSnapshotCopy());
            };
            _vp.Resize += delegate
            {
                if (_gpu.IsGpuActive)
                    _gpu.ResizeSwapChain(_vp.ClientSize.Width, _vp.ClientSize.Height);
            };
            _vp.MouseDown += delegate(object s, MouseEventArgs me)
            {
                _mouseDragging = true;
                SteerFromViewportMouse(me.X, me.Y, me.Button == MouseButtons.Right);
            };
            _vp.MouseMove += delegate(object s, MouseEventArgs me)
            {
                if (_mouseDragging)
                    SteerFromViewportMouse(me.X, me.Y, false);
            };
            _vp.MouseUp += delegate { _mouseDragging = false; };
            _vp.DoubleClick += delegate
            {
                _coord.FireManualGesture(SpatialGestureKind.DOUBLE_TAP_PULSE);
                AppendLog("[VIEWPORT DOUBLE-TAP] Fired DOUBLE_TAP_PULSE -> AV Fluid Splash + Penrose Psi Stamp!", C(250, 204, 21));
            };
            mainSplit.Panel1.Controls.Add(_vp);

            SplitContainer botSplit = new SplitContainer();
            botSplit.Dock = DockStyle.Fill;
            botSplit.Orientation = Orientation.Vertical;
            botSplit.BackColor = C(30, 41, 59);
            botSplit.SplitterWidth = 5;

            _gridTargets = BuildTargetsGrid();
            botSplit.Panel1.Controls.Add(_gridTargets);

            _rtbLog = new RichTextBox();
            _rtbLog.Dock = DockStyle.Fill;
            _rtbLog.ReadOnly = true;
            _rtbLog.BorderStyle = BorderStyle.None;
            _rtbLog.BackColor = C(6, 9, 17);
            _rtbLog.ForeColor = C(226, 232, 240);
            _rtbLog.Font = new Font("Consolas", 11.2f, FontStyle.Regular, GraphicsUnit.Pixel);
            botSplit.Panel2.Controls.Add(_rtbLog);

            mainSplit.Panel2.Controls.Add(botSplit);
            Controls.Add(mainSplit);
            Controls.Add(subBar);
            Controls.Add(topBar);

            _frameTimer = new System.Windows.Forms.Timer();
            _frameTimer.Interval = 16;
            _frameTimer.Tick += delegate
            {
                double nowS = _clock.Elapsed.TotalSeconds;
                double dt = Math.Max(0.004, Math.Min(0.08, nowS - _prevSec));
                _prevSec = nowS;
                _coord.StepFusionTick(nowS, dt);

                ConductorHudSnapshot snap = _coord.GetSnapshotCopy();
                string mLine = string.Format(CultureInfo.InvariantCulture,
                    "[{0}] Gesture={1} (#{2}) | Hopf S^2=({3:+0.00;-0.00},{4:+0.00;-0.00},{5:+0.00;-0.00}) Twist={6:+0.00;-0.00}rad | Range={7:0.00}m ({8:+0.00;-0.00}m/s) | SHM #{9} | {10}",
                    snap.InputSource, snap.Gesture, snap.GestureCount,
                    snap.Hopf.Rx, snap.Hopf.Ry, snap.Hopf.Rz, snap.Hopf.FiberTwistRad,
                    snap.RangeM, snap.DopplerMps, snap.ShmSequence, snap.SheafSummary);
                if (mLine != _prevMetricLine)
                {
                    _prevMetricLine = mLine;
                    _lblMetrics.Text = mLine;
                }

                if (_gpu.IsGpuActive)
                    _gpu.RenderFrame((float)nowS, snap, _vp.ClientSize.Width, _vp.ClientSize.Height);
                else
                    _vp.Invalidate();
            };

            _probeTimer = new System.Windows.Forms.Timer();
            _probeTimer.Interval = 950;
            _probeTimer.Tick += delegate
            {
                _coord.PollStationsAndSonarAsync(delegate { RefreshGridInPlace(); });
            };

            KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Space)
                {
                    ke.SuppressKeyPress = true;
                    TriggerSelectedGesture();
                }
            };

            Shown += delegate
            {
                try
                {
                    mainSplit.SplitterDistance = Math.Max(360, (mainSplit.Height * 64) / 100);
                    botSplit.SplitterDistance = Math.Max(580, (botSplit.Width * 58) / 100);
                }
                catch { }

                bool gpuOk = _gpu.Initialize(_vp.Handle, _vp.ClientSize.Width, _vp.ClientSize.Height);
                bool srvOk = _server.Start();

                AppendLog("=== OMEGACONDUCTOR v1.0.0 GM (:19790) LIVE [HOPF S^3->S^2 + ZERO-COPY SEQLOCK SHM] ===", C(52, 211, 153));
                AppendLog("  Shared Mem : " + _coord.SharedBus.MapName + " (mapped=" + _coord.SharedBus.IsMapped + ", 4,096B Seqlock + Golay[24,12,8])", C(56, 189, 248));
                AppendLog("  GPU Engine : " + _gpu.BackendLabel + " (active=" + gpuOk + ")", C(192, 132, 252));
                AppendLog("  Bus Server : " + (srvOk ? "HTTP + UDP 127.0.0.1:19790 (/api/status, /api/wand, /api/shm, /wand)" : "Port 19790 busy"), C(250, 204, 21));

                RefreshGridInPlace();
                _frameTimer.Start();
                _probeTimer.Start();
                _coord.PollStationsAndSonarAsync(delegate { RefreshGridInPlace(); });
            };

            FormClosing += delegate
            {
                _frameTimer.Stop();
                _probeTimer.Stop();
                _server.Dispose();
                _gpu.Dispose();
                _coord.Dispose();
            };
        }

        private void SteerFromViewportMouse(int mx, int my, bool pulse)
        {
            int leftW = Math.Max(120, (_vp.ClientSize.Width * 52) / 100);
            double nx = ((mx / (double)leftW) - 0.5) * 2.2;
            double ny = (0.5 - (my / (double)Math.Max(120, _vp.ClientSize.Height))) * 2.2;
            nx = Math.Max(-1.0, Math.Min(1.0, nx));
            ny = Math.Max(-1.0, Math.Min(1.0, ny));
            _coord.SetMouseWandRay(nx, ny, pulse);
        }

        private void TriggerSelectedGesture()
        {
            SpatialGestureKind[] map = new SpatialGestureKind[]
            {
                SpatialGestureKind.DOUBLE_TAP_PULSE,
                SpatialGestureKind.ORBIT_SWEEP,
                SpatialGestureKind.HOVER_LOCK,
                SpatialGestureKind.SURGE_CRESCENDO
            };
            int idx = Math.Max(0, Math.Min(map.Length - 1, _cmbGesture.SelectedIndex));
            _coord.FireManualGesture(map[idx]);
            AppendLog("[GESTURE FIRED] " + map[idx] + " -> Dispatched across live AV (:19779), Synth (:19782), Penrose (:19785)!", C(250, 204, 21));
        }

        private void BeamBatonToPixel10Async()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string adb = ResolveAdbExe();
                    RunCmd(adb, "reverse tcp:19790 tcp:19790", 2000);
                    RunCmd(adb, "shell am start -a android.intent.action.VIEW -d \"http://127.0.0.1:19790/wand\"", 2500);
                    AppendLog("[BEAM OK] Armed adb reverse tcp:19790 & opened 6-DoF Conductor Baton on Pixel 10!", C(56, 189, 248));
                }
                catch (Exception ex)
                {
                    AppendLog("[BEAM WARN] " + ex.Message, C(248, 113, 113));
                }
            });
        }

        private void RunStressSuiteAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    OmegaConductorRedTeamStressSuite.RunAllVectors(_baseDir);
                    string p = Path.Combine(_baseDir, "exports", "omega_conductor_stress_console.txt");
                    if (File.Exists(p))
                        AppendLog(File.ReadAllText(p, Encoding.UTF8).TrimEnd(), C(125, 211, 252));
                }
                catch { }
            });
        }

        private static string ResolveAdbExe()
        {
            string p1 = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe");
            if (File.Exists(p1)) return p1;
            if (File.Exists(@"C:\platform-tools\adb.exe")) return @"C:\platform-tools\adb.exe";
            return "adb.exe";
        }

        private static void RunCmd(string exe, string args, int ms)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            using (Process p = Process.Start(psi))
            {
                if (p != null) p.WaitForExit(ms);
            }
        }

        private static Button MakeBtn(string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 10.8f, FontStyle.Regular, GraphicsUnit.Pixel);
            b.FlatAppearance.BorderColor = C(56, 189, 248);
            return b;
        }

        private static DataGridView BuildTargetsGrid()
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.ReadOnly = true;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.BackgroundColor = C(8, 12, 22);
            g.GridColor = C(30, 41, 59);
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = C(15, 23, 42);
            g.ColumnHeadersDefaultCellStyle.ForeColor = C(52, 211, 153);
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 11.0f, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DefaultCellStyle.BackColor = C(9, 14, 26);
            g.DefaultCellStyle.ForeColor = Color.White;
            g.DefaultCellStyle.SelectionBackColor = C(30, 58, 138);
            g.DefaultCellStyle.Font = new Font("Consolas", 11.0f, FontStyle.Regular, GraphicsUnit.Pixel);
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            g.Columns.Add("st", "Target Station");
            g.Columns.Add("state", "Bus Link");
            g.Columns.Add("pkts", "Routed");
            g.Columns.Add("rtt", "RTT");
            g.Columns.Add("act", "Live Spatial Conduction Mapping");

            g.Columns[0].FillWeight = 24f;
            g.Columns[1].FillWeight = 11f;
            g.Columns[2].FillWeight = 11f;
            g.Columns[3].FillWeight = 10f;
            g.Columns[4].FillWeight = 44f;

            // Pre-allocate 4 fixed rows -> ZERO Rows.Clear() click/scroll races!
            for (int i = 0; i < 4; i++)
                g.Rows.Add("", "PROBING", "0", "0.0ms", "");
            return g;
        }

        private void RefreshGridInPlace()
        {
            if (IsDisposed) return;
            Action act = delegate
            {
                if (IsDisposed || _gridTargets == null || _gridTargets.Rows.Count < 4) return;
                ConductorHudSnapshot s = _coord.GetSnapshotCopy();
                for (int i = 0; i < 4; i++)
                {
                    ConductorTargetTelemetry t = s.Targets[i];
                    DataGridViewRow row = _gridTargets.Rows[i];
                    row.Cells[0].Value = t.Name;
                    row.Cells[1].Value = t.Online ? "LIVE" : "STANDBY";
                    row.Cells[1].Style.ForeColor = t.Online ? C(52, 211, 153) : C(248, 113, 113);
                    row.Cells[2].Value = t.PacketsRouted.ToString(CultureInfo.InvariantCulture);
                    row.Cells[3].Value = t.LastRttMs.ToString("0.0", CultureInfo.InvariantCulture) + "ms";
                    row.Cells[4].Value = t.Role + " :: " + t.LastAction;
                }
            };
            if (InvokeRequired) { try { BeginInvoke(act); } catch { } }
            else act();
        }

        private void AppendLog(string text, Color fg)
        {
            if (IsDisposed || _rtbLog == null) return;
            Action act = delegate
            {
                if (IsDisposed || _rtbLog == null) return;
                if (_rtbLog.TextLength > 24000)
                {
                    _rtbLog.Select(0, 8000);
                    _rtbLog.SelectedText = "";
                }
                _rtbLog.SelectionStart = _rtbLog.TextLength;
                _rtbLog.SelectionLength = 0;
                _rtbLog.SelectionColor = fg;
                _rtbLog.AppendText((text ?? "") + "\r\n");
                _rtbLog.SelectionStart = _rtbLog.TextLength;
                _rtbLog.ScrollToCaret();
            };
            if (InvokeRequired) { try { BeginInvoke(act); } catch { } }
            else act();
        }

        private static void WriteCrashGuardLog(string baseDir, string tag, Exception ex)
        {
            try
            {
                string expDir = Path.Combine(baseDir, "exports");
                Directory.CreateDirectory(expDir);
                string ln = "[" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "] [" + tag + "] " + (ex != null ? ex.ToString() : "unknown") + "\r\n";
                File.AppendAllText(Path.Combine(expDir, "omega_conductor_crash_guard.log"), ln, new UTF8Encoding(false));
            }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--stress-test", StringComparison.OrdinalIgnoreCase))
                    {
                        int rc = OmegaConductorRedTeamStressSuite.RunAllVectors(baseDir);
                        Environment.Exit(rc);
                        return;
                    }
                }
            }

            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs te)
                {
                    WriteCrashGuardLog(baseDir, "UI_THREAD_GUARD", te.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs ue)
                {
                    WriteCrashGuardLog(baseDir, "BG_THREAD_GUARD", ue.ExceptionObject as Exception);
                };
            }
            catch { }

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OmegaConductorCockpitForm(baseDir));
        }
    }
}
