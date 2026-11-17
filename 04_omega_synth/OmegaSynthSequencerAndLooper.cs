using System;
using System.Collections.Generic;
using System.IO;

namespace OmegaSynthStudio
{
    public sealed class PolyrhythmLane
    {
        public string Name;
        public int PresetOverride; // 0..8
        public int OctaveOffset;   // -1, 0, +1
        public int StepLength;     // 4..16
        public bool Muted;
        public readonly bool[] Gate = new bool[16];
        public readonly int[] Degree = new int[16];
        public readonly float[] TimbreY = new float[16];
        public int Cursor = 0;

        public PolyrhythmLane(string name, int preset, int octOff, int stepLen, bool muted)
        {
            Name = name;
            PresetOverride = preset;
            OctaveOffset = octOff;
            StepLength = stepLen;
            Muted = muted;
        }
    }

    public struct MpeGestureEvent
    {
        public int TickOffsetMs;
        public byte Kind; // 0 = Move/Down, 1 = Up
        public int TouchId;
        public float NormX;
        public float NormY;
        public float Pressure;
        public float GyroBeta;
        public float GyroGamma;
        public int PresetIdx;
    }

    public sealed class OmegaSequencerAndLooper
    {
        public readonly PolyrhythmLane[] Lanes = new PolyrhythmLane[4];
        public int SelectedEditLane = 0;

        private readonly object _loopLock = new object();
        private readonly List<MpeGestureEvent> _loopEvents = new List<MpeGestureEvent>(8192);
        public volatile int LoopState = 0; // 0=Stopped/Empty, 1=Recording First Pass, 2=Playing, 3=Overdubbing
        public volatile int LoopBars = 4;
        public volatile int LoopDurationMs = 8000;
        public volatile int LoopPlayheadMs = 0;
        private long _loopAnchorTicks = 0;
        private int _lastPlaybackCursorIdx = 0;

        public OmegaSequencerAndLooper()
        {
            Lanes[0] = new PolyrhythmLane("L1: Sub-Bass", 5, -1, 16, false);
            Lanes[1] = new PolyrhythmLane("L2: Chladni",  1,  0, 12, false);
            Lanes[2] = new PolyrhythmLane("L3: KS Harp",  6,  0,  9, false);
            Lanes[3] = new PolyrhythmLane("L4: Bowed",    0,  1, 16, true);

            ApplyEuclideanToLane(0, 4, 16, 0);
            ApplyEuclideanToLane(1, 5, 12, 2);
            ApplyEuclideanToLane(2, 5,  9, 4);
            ApplyEuclideanToLane(3, 3, 16, 7);
        }

        public int EventCount
        {
            get { lock (_loopLock) { return _loopEvents.Count; } }
        }

        public List<MpeGestureEvent> ExportLoopEventsSnapshot()
        {
            lock (_loopLock)
            {
                return new List<MpeGestureEvent>(_loopEvents);
            }
        }

        public void ImportLoopEventsSnapshot(List<MpeGestureEvent> events, int durationMs, int bars, bool autoPlayIfNonEmpty)
        {
            lock (_loopLock)
            {
                _loopEvents.Clear();
                if (events != null && events.Count > 0)
                {
                    _loopEvents.AddRange(events);
                    _loopEvents.Sort(CompareEventsByTime);
                }
                LoopBars = Math.Max(1, Math.Min(16, bars));
                LoopDurationMs = Math.Max(1500, durationMs);
                LoopPlayheadMs = 0;
                _lastPlaybackCursorIdx = 0;
                _loopAnchorTicks = DateTime.UtcNow.Ticks;
                LoopState = (autoPlayIfNonEmpty && _loopEvents.Count > 0) ? 2 : 0;
            }
        }

        public void ApplyEuclideanToLane(int laneIdx, int pulses, int length, int seed)
        {
            if (laneIdx < 0 || laneIdx >= 4) return;
            PolyrhythmLane ln = Lanes[laneIdx];
            int n = Math.Max(4, Math.Min(16, length));
            int k = Math.Max(1, Math.Min(n, pulses));
            ln.StepLength = n;

            int[] scalePool = new int[] { 0, 2, 4, 7, 9, 12, 14, 16, 19, 12, 7, 4 };
            for (int i = 0; i < 16; i++)
            {
                int prevB = (i * k) / n;
                int nextB = ((i + 1) * k) / n;
                ln.Gate[i] = (i < n) && (nextB > prevB);
                ln.Degree[i] = (laneIdx == 0)
                    ? ((i % 4 == 0) ? 0 : 7)
                    : scalePool[(i + seed + laneIdx * 2) % scalePool.Length];
                ln.TimbreY[i] = (float)(0.25 + 0.62 * (0.5 + 0.5 * Math.Sin(i * 0.9 + laneIdx)));
            }
            if (pulses > 0 && !ln.Gate[0]) ln.Gate[0] = true;
        }

        public void ToggleRecOrOverdub(int bpm)
        {
            lock (_loopLock)
            {
                double beatMs = 60000.0 / Math.Max(40, Math.Min(240, bpm));
                LoopDurationMs = Math.Max(1500, (int)Math.Round(beatMs * 4.0 * Math.Max(1, LoopBars)));

                if (LoopState == 0)
                {
                    _loopEvents.Clear();
                    _loopAnchorTicks = DateTime.UtcNow.Ticks;
                    LoopPlayheadMs = 0;
                    _lastPlaybackCursorIdx = 0;
                    LoopState = 1;
                }
                else if (LoopState == 1)
                {
                    _loopEvents.Sort(CompareEventsByTime);
                    _loopAnchorTicks = DateTime.UtcNow.Ticks;
                    _lastPlaybackCursorIdx = 0;
                    LoopState = 2;
                }
                else if (LoopState == 2)
                {
                    LoopState = 3;
                }
                else
                {
                    _loopEvents.Sort(CompareEventsByTime);
                    LoopState = 2;
                }
            }
        }

        public void ClearLoop()
        {
            lock (_loopLock)
            {
                LoopState = 0;
                _loopEvents.Clear();
                LoopPlayheadMs = 0;
                _lastPlaybackCursorIdx = 0;
            }
        }

        private static int CompareEventsByTime(MpeGestureEvent a, MpeGestureEvent b)
        {
            return a.TickOffsetMs.CompareTo(b.TickOffsetMs);
        }

        public void RecordGestureIfArmed(byte kind, int touchId, double nx, double ny, double p, float gb, float gg, int presetIdx)
        {
            int st = LoopState;
            if (st != 1 && st != 3) return;
            lock (_loopLock)
            {
                if (_loopEvents.Count >= 24000) return;
                long elapsedMs = (DateTime.UtcNow.Ticks - _loopAnchorTicks) / 10000L;
                int offsetMs = (int)(elapsedMs % Math.Max(1, LoopDurationMs));
                MpeGestureEvent ev = new MpeGestureEvent();
                ev.TickOffsetMs = offsetMs;
                ev.Kind = kind;
                ev.TouchId = touchId;
                ev.NormX = (float)nx;
                ev.NormY = (float)ny;
                ev.Pressure = (float)p;
                ev.GyroBeta = gb;
                ev.GyroGamma = gg;
                ev.PresetIdx = presetIdx;
                _loopEvents.Add(ev);
            }
        }

        public void PumpLooperPlayback(OmegaWavefieldAudioEngine audio)
        {
            int st = LoopState;
            if (st == 0) return;

            List<MpeGestureEvent> fireList = null;
            bool wrapped = false;

            lock (_loopLock)
            {
                int dur = Math.Max(500, LoopDurationMs);
                long elapsedMs = (DateTime.UtcNow.Ticks - _loopAnchorTicks) / 10000L;
                if (st == 1 && elapsedMs >= dur)
                {
                    _loopEvents.Sort(CompareEventsByTime);
                    _loopAnchorTicks = DateTime.UtcNow.Ticks;
                    elapsedMs = 0;
                    _lastPlaybackCursorIdx = 0;
                    LoopState = 2;
                    st = 2;
                }

                int curMs = (int)(elapsedMs % dur);
                if (curMs < LoopPlayheadMs)
                {
                    _lastPlaybackCursorIdx = 0;
                    wrapped = true;
                }
                LoopPlayheadMs = curMs;

                if ((st == 2 || st == 3) && _loopEvents.Count > 0)
                {
                    while (_lastPlaybackCursorIdx < _loopEvents.Count && _loopEvents[_lastPlaybackCursorIdx].TickOffsetMs <= curMs)
                    {
                        if (fireList == null) fireList = new List<MpeGestureEvent>(8);
                        fireList.Add(_loopEvents[_lastPlaybackCursorIdx]);
                        _lastPlaybackCursorIdx++;
                    }
                }
            }

            if (wrapped) audio.ReleaseLooperVoicesOnly();
            if (fireList != null)
            {
                for (int i = 0; i < fireList.Count; i++)
                {
                    MpeGestureEvent ev = fireList[i];
                    int loopTid = 4000 + (Math.Abs(ev.TouchId) % 2000);
                    if (ev.Kind == 0)
                        audio.SetOrTriggerVoiceWithPreset(loopTid, ev.NormX, ev.NormY, ev.Pressure, false, ev.PresetIdx, true);
                    else
                        audio.ReleaseVoice(loopTid);
                }
            }
        }

        public string ExportStandardMidiFile(string rendersDir, int bpm, int baseMidiNote, float octaveSpan)
        {
            Directory.CreateDirectory(rendersDir);
            string path = Path.Combine(rendersDir, "omega_synth_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mid");
            const int ppq = 480;
            int stepTicks = ppq / 4;

            List<byte[]> tracks = new List<byte[]>();

            MemoryStream t0 = new MemoryStream();
            int usPerQuarter = 60000000 / Math.Max(40, Math.Min(240, bpm));
            WriteVarLen(t0, 0);
            t0.Write(new byte[] { 0xFF, 0x51, 0x03, (byte)((usPerQuarter >> 16) & 255), (byte)((usPerQuarter >> 8) & 255), (byte)(usPerQuarter & 255) }, 0, 6);
            WriteVarLen(t0, 0);
            t0.Write(new byte[] { 0xFF, 0x2F, 0x00 }, 0, 3);
            tracks.Add(t0.ToArray());

            for (int lnIdx = 0; lnIdx < 4; lnIdx++)
            {
                PolyrhythmLane ln = Lanes[lnIdx];
                MemoryStream tm = new MemoryStream();
                byte ch = (byte)(lnIdx & 0x0F);
                WriteVarLen(tm, 0);
                tm.Write(new byte[] { (byte)(0xC0 | ch), (byte)((ln.PresetOverride * 11) & 127) }, 0, 2);

                int deltaAcc = 0;
                for (int step = 0; step < 64; step++)
                {
                    int pStep = step % Math.Max(1, ln.StepLength);
                    if (!ln.Muted && ln.Gate[pStep])
                    {
                        int note = Math.Max(12, Math.Min(115, baseMidiNote + ln.OctaveOffset * 12 + ln.Degree[pStep]));
                        byte vel = (byte)Math.Max(40, Math.Min(124, (int)(65 + ln.TimbreY[pStep] * 55)));
                        WriteVarLen(tm, deltaAcc);
                        tm.Write(new byte[] { (byte)(0x90 | ch), (byte)note, vel }, 0, 3);
                        WriteVarLen(tm, stepTicks - 10);
                        tm.Write(new byte[] { (byte)(0x80 | ch), (byte)note, 0 }, 0, 3);
                        deltaAcc = 10;
                    }
                    else
                    {
                        deltaAcc += stepTicks;
                    }
                }
                WriteVarLen(tm, deltaAcc);
                tm.Write(new byte[] { 0xFF, 0x2F, 0x00 }, 0, 3);
                tracks.Add(tm.ToArray());
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 1 }, 0, 10);
                WriteBe16(fs, (ushort)tracks.Count);
                WriteBe16(fs, (ushort)ppq);

                for (int i = 0; i < tracks.Count; i++)
                {
                    fs.Write(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' }, 0, 4);
                    WriteBe32(fs, (uint)tracks[i].Length);
                    fs.Write(tracks[i], 0, tracks[i].Length);
                }
            }
            return path;
        }

        private static void WriteVarLen(Stream s, int val)
        {
            uint v = (uint)Math.Max(0, val);
            uint buffer = v & 0x7Fu;
            while ((v >>= 7) > 0)
            {
                buffer <<= 8;
                buffer |= ((v & 0x7Fu) | 0x80u);
            }
            while (true)
            {
                s.WriteByte((byte)(buffer & 0xFFu));
                if ((buffer & 0x80u) != 0) buffer >>= 8;
                else break;
            }
        }

        private static void WriteBe16(Stream s, ushort v)
        {
            s.WriteByte((byte)((v >> 8) & 255));
            s.WriteByte((byte)(v & 255));
        }

        private static void WriteBe32(Stream s, uint v)
        {
            s.WriteByte((byte)((v >> 24) & 255));
            s.WriteByte((byte)((v >> 16) & 255));
            s.WriteByte((byte)((v >> 8) & 255));
            s.WriteByte((byte)(v & 255));
        }
    }
}
