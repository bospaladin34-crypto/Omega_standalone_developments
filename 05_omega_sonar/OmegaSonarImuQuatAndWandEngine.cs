using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaSonarStudio
{
    public struct Quat4
    {
        public double W, X, Y, Z;

        public Quat4(double w, double x, double y, double z)
        {
            W = w; X = x; Y = y; Z = z;
        }

        public double Norm()
        {
            return Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
        }

        public Quat4 Normalized()
        {
            double n = Norm();
            if (n < 1e-12) return new Quat4(1.0, 0.0, 0.0, 0.0);
            return new Quat4(W / n, X / n, Y / n, Z / n);
        }

        public static Quat4 Multiply(Quat4 a, Quat4 b)
        {
            return new Quat4(
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);
        }

        public Quat4 Conjugate()
        {
            return new Quat4(W, -X, -Y, -Z);
        }

        public void RotateVector(double vx, double vy, double vz, out double ox, out double oy, out double oz)
        {
            Quat4 vq = new Quat4(0.0, vx, vy, vz);
            Quat4 r = Multiply(Multiply(this, vq), Conjugate());
            ox = r.X; oy = r.Y; oz = r.Z;
        }

        public void ToEulerDegrees(out double rollDeg, out double pitchDeg, out double yawDeg)
        {
            double sinr = 2.0 * (W * X + Y * Z);
            double cosr = 1.0 - 2.0 * (X * X + Y * Y);
            rollDeg = Math.Atan2(sinr, cosr) * 180.0 / Math.PI;

            double sinp = 2.0 * (W * Y - Z * X);
            if (Math.Abs(sinp) >= 1.0)
                pitchDeg = (sinp >= 0.0 ? 90.0 : -90.0);
            else
                pitchDeg = Math.Asin(sinp) * 180.0 / Math.PI;

            double siny = 2.0 * (W * Z + X * Y);
            double cosy = 1.0 - 2.0 * (Y * Y + Z * Z);
            yawDeg = Math.Atan2(siny, cosy) * 180.0 / Math.PI;
        }
    }

    public sealed class Wand6DofState
    {
        public Quat4 Q = new Quat4(1.0, 0.0, 0.0, 0.0);
        public double RollDeg, PitchDeg, YawDeg;
        public double RayX, RayY, RayZ = -1.0;
        public double TipX, TipY, TipZ = -0.45;
        public string SourceLabel = "Sim S^3 Orbit";
        public int PacketsIngested = 0;
        public bool TriggerPressed = false;
        public string LastVirtioGpuReceipt = "Standby (:19789 ready)";
    }

    public sealed class OmegaSonarWandFusionEngine
    {
        private readonly object _lk = new object();
        private Quat4 _q = new Quat4(1.0, 0.0, 0.0, 0.0);
        private DateTime _lastExternalImuUtc = DateTime.MinValue;
        private string _sourceLabel = "Sim S^3 Wand";
        private int _packets = 0;
        private bool _trigger = false;
        private string _lastVirtio = "Standby (click '4. Offload -> @deb :19789')";
        private double _simT = 0.0;

        // 96-point 3D Ribbon Trail (X, Y, Z triples)
        private readonly float[] _trailXyz = new float[96 * 3];
        private int _trailHead = 0;
        private bool _trailSeeded = false;

        // Madgwick S^3 Gradient-Descent 6-DoF IMU Fusion Step (Gyro rad/s + Accel m/s^2)
        public static Quat4 StepMadgwick6Dof(Quat4 qIn, double gx, double gy, double gz, double ax, double ay, double az, double dt, double beta)
        {
            Quat4 q = qIn.Normalized();
            double q0 = q.W, q1 = q.X, q2 = q.Y, q3 = q.Z;

            double qDot0 = 0.5 * (-q1 * gx - q2 * gy - q3 * gz);
            double qDot1 = 0.5 * (q0 * gx + q2 * gz - q3 * gy);
            double qDot2 = 0.5 * (q0 * gy - q1 * gz + q3 * gx);
            double qDot3 = 0.5 * (q0 * gz + q1 * gy - q2 * gx);

            double aNorm = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (aNorm > 1e-6)
            {
                ax /= aNorm; ay /= aNorm; az /= aNorm;

                // Objective f = [2(q1*q3 - q0*q2) - ax, 2(q0*q1 + q2*q3) - ay, 2(0.5 - q1^2 - q2^2) - az]
                double f1 = 2.0 * (q1 * q3 - q0 * q2) - ax;
                double f2 = 2.0 * (q0 * q1 + q2 * q3) - ay;
                double f3 = 1.0 - 2.0 * (q1 * q1 + q2 * q2) - az;

                double s0 = -2.0 * q2 * f1 + 2.0 * q1 * f2;
                double s1 = 2.0 * q3 * f1 + 2.0 * q0 * f2 - 4.0 * q1 * f3;
                double s2 = -2.0 * q0 * f1 + 2.0 * q3 * f2 - 4.0 * q2 * f3;
                double s3 = 2.0 * q1 * f1 + 2.0 * q2 * f2;

                double sNorm = Math.Sqrt(s0 * s0 + s1 * s1 + s2 * s2 + s3 * s3);
                if (sNorm > 1e-9)
                {
                    qDot0 -= beta * (s0 / sNorm);
                    qDot1 -= beta * (s1 / sNorm);
                    qDot2 -= beta * (s2 / sNorm);
                    qDot3 -= beta * (s3 / sNorm);
                }
            }

            Quat4 next = new Quat4(q0 + qDot0 * dt, q1 + qDot1 * dt, q2 + qDot2 * dt, q3 + qDot3 * dt);
            return next.Normalized();
        }

        public void IngestEulerOrGyroJson(string json, string srcTag)
        {
            double alpha = ExtractNum(json, "alpha"); // yaw deg
            double beta = ExtractNum(json, "beta");   // pitch deg
            double gamma = ExtractNum(json, "gamma"); // roll deg
            double qw = ExtractNum(json, "qw");
            double qx = ExtractNum(json, "qx");
            double qy = ExtractNum(json, "qy");
            double qz = ExtractNum(json, "qz");
            bool trig = (json ?? "").IndexOf("\"trig\":true", StringComparison.OrdinalIgnoreCase) >= 0;

            Quat4 qNew;
            if (Math.Abs(qw) + Math.Abs(qx) + Math.Abs(qy) + Math.Abs(qz) > 0.1)
            {
                qNew = new Quat4(qw, qx, qy, qz).Normalized();
            }
            else
            {
                double r = gamma * Math.PI / 360.0;
                double p = beta * Math.PI / 360.0;
                double y = alpha * Math.PI / 360.0;
                double cr = Math.Cos(r), sr = Math.Sin(r);
                double cp = Math.Cos(p), sp = Math.Sin(p);
                double cy = Math.Cos(y), sy = Math.Sin(y);
                qNew = new Quat4(
                    cr * cp * cy + sr * sp * sy,
                    sr * cp * cy - cr * sp * sy,
                    cr * sp * cy + sr * cp * sy,
                    cr * cp * sy - sr * sp * cy).Normalized();
            }

            lock (_lk)
            {
                _q = qNew;
                _lastExternalImuUtc = DateTime.UtcNow;
                _sourceLabel = srcTag;
                _packets++;
                _trigger = trig;
            }
        }

        public string PollPixel10AdbImuOnce()
        {
            try
            {
                string adb = ResolveAdbPath();
                if (string.IsNullOrEmpty(adb)) return "[ADB IMU] adb.exe not found.";
                ProcessStartInfo psi = new ProcessStartInfo(adb, "shell \"dumpsys sensorservice | grep -E -A 2 'Rotation Vector|Game Rotation Vector|Accelerometer' | head -n 18\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string raw = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    Match m = Regex.Match(raw ?? "", "(-?\\d+\\.\\d+)\\s*,\\s*(-?\\d+\\.\\d+)\\s*,\\s*(-?\\d+\\.\\d+)(?:\\s*,\\s*(-?\\d+\\.\\d+))?");
                    if (m.Success)
                    {
                        double v1 = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        double v2 = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        double v3 = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                        Quat4 qAdb;
                        if (m.Groups[4].Success && Math.Abs(v1) <= 1.05 && Math.Abs(v2) <= 1.05 && Math.Abs(v3) <= 1.05)
                        {
                            double v4 = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                            qAdb = new Quat4(v4, v1, v2, v3).Normalized();
                        }
                        else
                        {
                            qAdb = StepMadgwick6Dof(_q, 0.0, 0.0, 0.0, v1, v2, v3, 0.05, 0.25);
                        }
                        lock (_lk)
                        {
                            _q = qAdb;
                            _lastExternalImuUtc = DateTime.UtcNow;
                            _sourceLabel = "Pixel 10 ADB SensorHAL";
                            _packets++;
                        }
                        // Also push fused sensor frame to Pixel 10 worker :19788 if live!
                        TryPostJsonFast("http://127.0.0.1:19788/api/sensors/ingest",
                            string.Format(CultureInfo.InvariantCulture, "{{\"src\":\"omega_sonar_adb\",\"qw\":{0:0.0000},\"qx\":{1:0.0000},\"qy\":{2:0.0000},\"qz\":{3:0.0000}}}", qAdb.W, qAdb.X, qAdb.Y, qAdb.Z));
                        return string.Format(CultureInfo.InvariantCulture, "[Pixel 10 ADB IMU LOCKED] q=({0:0.000}, {1:0.000}, {2:0.000}, {3:0.000}) | ||q||={4:0.000000}", qAdb.W, qAdb.X, qAdb.Y, qAdb.Z, qAdb.Norm());
                    }
                    return "[Pixel 10 ADB IMU] Sensor service queried (open OLED /wand on Pixel 10 for 60Hz streaming).";
                }
            }
            catch (Exception ex)
            {
                return "[Pixel 10 ADB IMU Error] " + ex.Message;
            }
        }

        public string OffloadBatchToAvfVirtioGpu19789()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:19789/api/dispatch/udot");
                req.Method = "GET";
                req.Timeout = 10000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string js = sr.ReadToEnd();
                    double gops = ExtractNum(js, "gops_s");
                    double ms = ExtractNum(js, "elapsed_ms");
                    string note = string.Format(CultureInfo.InvariantCulture, "@deb Virtio-GPU :19789 OpUDot={0:0.00} GOPS/s ({1:0.00} ms)", gops, ms);
                    lock (_lk) { _lastVirtio = note; }
                    return "[AVF @deb VIRTIO-GPU :19789 OFFLOAD PASS] " + note + "\r\n  " + js;
                }
            }
            catch (Exception ex)
            {
                return "[AVF :19789 Standby] Start @deb Virtio-GPU (:19789) in OmegaShell or Launcher first (" + ex.Message + ").";
            }
        }

        public Wand6DofState StepAndFuseWithSonar(double dtSec, double golayRangeM, out float[] trailCopy)
        {
            _simT += dtSec;
            lock (_lk)
            {
                if ((DateTime.UtcNow - _lastExternalImuUtc).TotalSeconds > 2.5)
                {
                    double gx = 0.55 * Math.Cos(_simT * 1.15);
                    double gy = 0.72 * Math.Sin(_simT * 0.92);
                    double gz = 0.85 * Math.Cos(_simT * 0.68);
                    double ax = 1.8 * Math.Sin(_simT * 0.92);
                    double ay = -1.5 * Math.Cos(_simT * 1.15);
                    double az = 9.65;
                    _q = StepMadgwick6Dof(_q, gx, gy, gz, ax, ay, az, dtSec, 0.06);
                    _sourceLabel = "Sim S^3 Madgwick";
                }

                double rx, ry, rz;
                _q.RotateVector(0.0, 0.0, -1.0, out rx, out ry, out rz);
                double rClamped = Math.Max(0.10, Math.Min(1.65, golayRangeM));
                double tx = rx * rClamped;
                double ty = ry * rClamped;
                double tz = rz * rClamped;

                if (!_trailSeeded)
                {
                    for (int s = 0; s < 96; s++)
                    {
                        _trailXyz[s * 3 + 0] = (float)tx;
                        _trailXyz[s * 3 + 1] = (float)ty;
                        _trailXyz[s * 3 + 2] = (float)tz;
                    }
                    _trailSeeded = true;
                }
                if (dtSec > 0.0)
                {
                    _trailXyz[_trailHead * 3 + 0] = (float)tx;
                    _trailXyz[_trailHead * 3 + 1] = (float)ty;
                    _trailXyz[_trailHead * 3 + 2] = (float)tz;
                    _trailHead = (_trailHead + 1) % 96;
                }

                trailCopy = new float[96 * 3];
                for (int i = 0; i < 96; i++)
                {
                    int idx = (_trailHead + i) % 96;
                    trailCopy[i * 3 + 0] = _trailXyz[idx * 3 + 0];
                    trailCopy[i * 3 + 1] = _trailXyz[idx * 3 + 1];
                    trailCopy[i * 3 + 2] = _trailXyz[idx * 3 + 2];
                }

                Wand6DofState st = new Wand6DofState();
                st.Q = _q;
                _q.ToEulerDegrees(out st.RollDeg, out st.PitchDeg, out st.YawDeg);
                st.RayX = rx; st.RayY = ry; st.RayZ = rz;
                st.TipX = tx; st.TipY = ty; st.TipZ = tz;
                st.SourceLabel = _sourceLabel;
                st.PacketsIngested = _packets;
                st.TriggerPressed = _trigger;
                st.LastVirtioGpuReceipt = _lastVirtio;
                return st;
            }
        }

        private static double ExtractNum(string json, string key)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            double v;
            return (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) ? v : 0.0;
        }

        private static void TryPostJsonFast(string url, string body)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "POST";
                r.Timeout = 450;
                r.ContentType = "application/json";
                byte[] b = Encoding.UTF8.GetBytes(body);
                r.ContentLength = b.Length;
                using (Stream s = r.GetRequestStream()) s.Write(b, 0, b.Length);
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse()) { }
            }
            catch { }
        }

        public static string ResolveAdbPath()
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\platform-tools\adb.exe");
            if (File.Exists(local)) return local;
            string[] common = new string[] { @"C:\platform-tools\adb.exe", @"C:\Android\platform-tools\adb.exe" };
            for (int i = 0; i < common.Length; i++) if (File.Exists(common[i])) return common[i];
            return "adb.exe";
        }
    }
}
