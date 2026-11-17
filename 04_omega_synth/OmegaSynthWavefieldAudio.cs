using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OmegaSynthStudio
{
    public struct MpeVoiceSnapshot
    {
        public bool Active;
        public int TouchId;
        public double NormX;
        public double NormY;
        public double Pressure;
        public double FreqHz;
        public int NearestE8Root;
    }

    public sealed class OmegaWavefieldAudioEngine : IDisposable
    {
        public const int SampleRate = 48000;
        public const int FramesPerBuffer = 512;
        public const int BufferCount = 4;
        public const int MaxVoices = 10;
        public const int PartialsPerVoice = 8;
        private const int KsDelayCap = 4096;

        private const uint WAVE_MAPPER = 0xFFFFFFFFu;
        private const ushort WAVE_FORMAT_PCM = 1;
        private const uint CALLBACK_EVENT = 0x00050000u;
        private const uint WHDR_DONE = 0x00000001u;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int waveOutClose(IntPtr hwo);

        private sealed class VoiceState
        {
            public bool Gate;
            public bool DroneHold;
            public bool IsLooperVoice;
            public int TouchId = -1;
            public int VoicePresetIdx = 0;
            public double TargetX = 0.5, TargetY = 0.5, SmoothX = 0.5, SmoothY = 0.5;
            public double TargetPressure = 0.0, Env = 0.0;
            public double TargetHz = 220.0, SmoothHz = 220.0;
            public double StrikeBurst = 0.0, NoiseState = 0.13579;
            public int E8Root = 0;
            public long LastUpdateTicks = 0;
            public bool IsRemoteMpe = false;

            public readonly double[] ModeRe = new double[PartialsPerVoice];
            public readonly double[] ModeIm = new double[PartialsPerVoice];
            public readonly double[] ModeEnergy = new double[PartialsPerVoice];

            public readonly float[] KsLine = new float[KsDelayCap];
            public int KsWritePos = 0;
            public float KsLpState = 0.0f;
            public float KsApInPrev = 0.0f;
            public float KsApOutPrev = 0.0f;

            public VoiceState()
            {
                for (int p = 0; p < PartialsPerVoice; p++) { ModeRe[p] = 1.0; ModeIm[p] = 0.0; }
            }
        }

        private readonly object _voiceLock = new object();
        private readonly VoiceState[] _voices = new VoiceState[MaxVoices];
        public readonly OmegaSequencerAndLooper SeqLooper = new OmegaSequencerAndLooper();
        public readonly OmegaWasapiLoopbackPipe WasapiPipe = new OmegaWasapiLoopbackPipe();
        public readonly OmegaAmbientMicCapture MicCapture = new OmegaAmbientMicCapture();

        // Optional Laptop Microphone Live Ambient & Voice Synthesis (Zero Speaker Echo / Zero Feedback!)
        public volatile bool MicInputActive = false;
        public volatile int MicSynthMode = 0; // 0=Silent Cymatic Visualizer, 1=Voice Pitch->Bowed, 2=Room->E8 Chord, 3=Clap->Plate Striker
        private readonly short[] _micWinL = new short[256];
        private readonly short[] _micWinR = new short[256];
        private int _micTapRoundRobin = 0;

        // Ambient Generative Auto-Evolve Drift (Golden-Ratio LFO + Markov Microtonal Harmonic Walker)
        public volatile bool AutoEvolveEnabled = false;
        public volatile float AutoEvolveDensity = 0.50f;
        private double _evolveSampleTimer = 0.0;
        private double _evolveLfoPhase = 0.0;
        private int _evolveStepCounter = 0;

        // Uploaded Sound-File Clip Deck
        private readonly object _clipLock = new object();
        public OmegaDecodedAudioClip ActiveClip = null;
        public volatile bool ClipPlaying = false;
        public volatile bool ClipLooping = true;
        public volatile bool ClipSympatheticFx = false;
        public volatile int ClipPlayheadSample = 0;
        public volatile int ClipTotalSamples = 0;

        // Live OmegaBrowser / YouTube WASAPI Pipe Mode (Zero Acoustic Feedback!)
        public volatile bool WebPipeActive = false;
        public volatile bool WebPipeSympatheticDrone = false;
        private readonly short[] _pipeWinL = new short[256];
        private readonly short[] _pipeWinR = new short[256];
        private int _pipePitchFrameSkip = 0;

        // Precision Acoustic Lab Generator (0=Off, 1=Sweep 55..880Hz, 2=432Hz, 3=528Hz, 4=Binaural 15.965Hz)
        public volatile int LabToneMode = 0;
        private double _labPhaseL = 0.0, _labPhaseR = 0.0, _labSweepPhase = 0.0;

        private readonly float[] _e8U = new float[E8WaveLatticeMath.RootCount];
        private readonly float[] _e8V = new float[E8WaveLatticeMath.RootCount];
        private readonly float[] _e8Snap = new float[E8WaveLatticeMath.RootCount];

        private readonly float[] _dly1 = new float[1687];
        private readonly float[] _dly2 = new float[2273];
        private readonly float[] _dly3 = new float[2953];
        private readonly float[] _dly4 = new float[3571];
        private int _dp1, _dp2, _dp3, _dp4;
        private double _dcInL, _dcOutL, _dcInR, _dcOutR;

        private double _svfLp1L, _svfBp1L, _svfLp2L, _svfBp2L, _svfLp3L, _svfBp3L;
        private double _svfLp1R, _svfBp1R, _svfLp2R, _svfBp2R, _svfLp3R, _svfBp3R;

        private readonly short[] _scopeL = new short[256];
        private readonly short[] _scopeR = new short[256];
        private readonly double[] _bands8 = new double[8];
        private readonly float[] _specBins32 = new float[32];

        public volatile bool ArpEnabled = false;
        public volatile int ArpBpm = 112;
        private double _seqSamplePhase = 0.0;

        public volatile int PresetIndex = 0;
        public volatile int ScaleIndex = 0;
        public volatile int BaseMidiNote = 48;
        public volatile float OctaveSpan = 3.0f;
        public volatile float ScaleSnap = 0.35f;
        public volatile float MasterVolume = 0.78f;
        public volatile float E8CouplingOverride = 0.48f;
        public volatile float HallShimmerOverride = 0.38f;
        public volatile float DecayMultiplier = 1.0f;
        public volatile bool DroneLatchMode = false;

        public volatile int ChladniM = 3;
        public volatile int ChladniN = 5;
        public volatile bool AutoChladniFromPitch = true;
        public volatile bool FormantFilterEnabled = false;
        public volatile float FormantVowelMorph = 0.25f;

        public volatile bool GyroPitchBendEnabled = true;
        public volatile float GyroBetaDeg = 0.0f;
        public volatile float GyroGammaDeg = 0.0f;
        public volatile float GyroBendRangeSemis = 2.0f;
        private long _lastGyroTicks = 0;

        public volatile bool IsAudioOpen = false;
        public volatile float PeakLevelL = 0.0f;
        public volatile float PeakLevelR = 0.0f;
        public volatile int ActiveVoiceCount = 0;
        public volatile float LeadVoiceHz = 0.0f;
        public volatile int LeadVoiceRoot = 0;
        public string AudioStatus = "Initializing waveOut 48kHz...";

        private readonly object _recLock = new object();
        private List<short> _recL = null, _recR = null;
        public volatile bool IsRecordingWav = false;
        public volatile int RecordedSeconds = 0;
        public string LastSavedWavPath = "";

        private IntPtr _hWaveOut = IntPtr.Zero;
        private AutoResetEvent _waveEvent;
        private Thread _pumpThread;
        private volatile bool _running = false;
        private GCHandle[] _pcmPins, _hdrPins;
        private short[][] _pcmBuffers;
        private byte[][] _hdrBytes;

        public OmegaWavefieldAudioEngine()
        {
            for (int i = 0; i < MaxVoices; i++) _voices[i] = new VoiceState();
        }

        public void SetLoadedAudioClip(OmegaDecodedAudioClip clip, bool autoPlay)
        {
            lock (_clipLock)
            {
                ActiveClip = clip;
                ClipPlayheadSample = 0;
                ClipTotalSamples = (clip != null && clip.PcmL != null) ? clip.PcmL.Length : 0;
                ClipPlaying = autoPlay && (ClipTotalSamples > 0);
            }
            if (clip != null)
            {
                ChladniM = clip.SuggestedM;
                ChladniN = clip.SuggestedN;
                LeadVoiceHz = (float)clip.DominantHz;
            }
        }

        public void SeekClipNormalized(double pos01)
        {
            lock (_clipLock)
            {
                if (ActiveClip != null && ActiveClip.PcmL != null && ActiveClip.PcmL.Length > 0)
                {
                    int idx = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, pos01)) * (ActiveClip.PcmL.Length - 1));
                    ClipPlayheadSample = idx;
                }
            }
        }

        public void SetWebPipeEnabled(bool enable, string title, string url)
        {
            if (!string.IsNullOrEmpty(title)) WasapiPipe.PipeTitle = title;
            if (!string.IsNullOrEmpty(url)) WasapiPipe.PipeUrl = url;
            if (enable)
            {
                ClipPlaying = false;
                WasapiPipe.Start();
                WebPipeActive = true;
            }
            else
            {
                WebPipeActive = false;
                ReleaseVoice(790);
            }
        }

        public void SetMicInputEnabled(bool enable, int deviceChoiceIndex)
        {
            if (enable)
            {
                ClipPlaying = false;
                MicCapture.Start(deviceChoiceIndex);
                MicInputActive = true;
            }
            else
            {
                MicInputActive = false;
                MicCapture.Stop();
                ReleaseVoice(791);
                ReleaseVoice(792);
                ReleaseVoice(793);
                ReleaseVoice(794);
            }
        }

        public bool Start()
        {
            if (_running) return IsAudioOpen;
            _running = true;
            _waveEvent = new AutoResetEvent(false);

            try
            {
                WAVEFORMATEX fmt = new WAVEFORMATEX();
                fmt.wFormatTag = WAVE_FORMAT_PCM;
                fmt.nChannels = 2;
                fmt.nSamplesPerSec = (uint)SampleRate;
                fmt.wBitsPerSample = 16;
                fmt.nBlockAlign = 4;
                fmt.nAvgBytesPerSec = (uint)(SampleRate * 4);
                fmt.cbSize = 0;

                int hr = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt, _waveEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
                if (hr == 0 && _hWaveOut != IntPtr.Zero)
                {
                    int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
                    _pcmBuffers = new short[BufferCount][];
                    _pcmPins = new GCHandle[BufferCount];
                    _hdrBytes = new byte[BufferCount][];
                    _hdrPins = new GCHandle[BufferCount];

                    for (int b = 0; b < BufferCount; b++)
                    {
                        _pcmBuffers[b] = new short[FramesPerBuffer * 2];
                        _pcmPins[b] = GCHandle.Alloc(_pcmBuffers[b], GCHandleType.Pinned);
                        _hdrBytes[b] = new byte[hdrSize];
                        _hdrPins[b] = GCHandle.Alloc(_hdrBytes[b], GCHandleType.Pinned);

                        WAVEHDR hdr = new WAVEHDR();
                        hdr.lpData = _pcmPins[b].AddrOfPinnedObject();
                        hdr.dwBufferLength = (uint)(FramesPerBuffer * 4);
                        hdr.dwFlags = 0u;
                        Marshal.StructureToPtr(hdr, _hdrPins[b].AddrOfPinnedObject(), false);

                        waveOutPrepareHeader(_hWaveOut, _hdrPins[b].AddrOfPinnedObject(), (uint)hdrSize);
                        RenderStereoBlock(_pcmBuffers[b], FramesPerBuffer);
                        waveOutWrite(_hWaveOut, _hdrPins[b].AddrOfPinnedObject(), (uint)hdrSize);
                    }
                    IsAudioOpen = true;
                    AudioStatus = "48.0 kHz Stereo FDTD + KS LIVE";
                }
                else
                {
                    IsAudioOpen = false;
                    AudioStatus = "Silent Sim (waveOut 0x" + hr.ToString("X") + ")";
                }
            }
            catch (Exception ex)
            {
                IsAudioOpen = false;
                AudioStatus = "Silent Sim (" + ex.Message + ")";
            }

            _pumpThread = new Thread(AudioPumpLoop);
            _pumpThread.IsBackground = true;
            _pumpThread.Priority = ThreadPriority.Highest;
            _pumpThread.Name = "OmegaSynthV10Audio";
            _pumpThread.Start();
            return IsAudioOpen;
        }

        private void AudioPumpLoop()
        {
            uint hdrSize = (uint)Marshal.SizeOf(typeof(WAVEHDR));
            short[] scratch = new short[FramesPerBuffer * 2];
            while (_running)
            {
                if (IsAudioOpen && _hWaveOut != IntPtr.Zero)
                {
                    bool wrote = false;
                    for (int b = 0; b < BufferCount; b++)
                    {
                        IntPtr pHdr = _hdrPins[b].AddrOfPinnedObject();
                        WAVEHDR hdr = (WAVEHDR)Marshal.PtrToStructure(pHdr, typeof(WAVEHDR));
                        if ((hdr.dwFlags & WHDR_DONE) != 0u)
                        {
                            RenderStereoBlock(_pcmBuffers[b], FramesPerBuffer);
                            waveOutWrite(_hWaveOut, pHdr, hdrSize);
                            wrote = true;
                        }
                    }
                    if (!wrote) _waveEvent.WaitOne(8);
                }
                else
                {
                    RenderStereoBlock(scratch, FramesPerBuffer);
                    Thread.Sleep(10);
                }
            }
        }

        public void SetOrTriggerVoice(int touchId, double xNorm, double yNorm, double pressure, bool isRemoteMpe)
        {
            SeqLooper.RecordGestureIfArmed(0, touchId, xNorm, yNorm, pressure, GyroBetaDeg, GyroGammaDeg, PresetIndex);
            SetOrTriggerVoiceWithPreset(touchId, xNorm, yNorm, pressure, isRemoteMpe, PresetIndex, false);
        }

        public void SetOrTriggerVoiceWithPreset(int touchId, double xNorm, double yNorm, double pressure, bool isRemoteMpe, int presetOverride, bool isLooperVoice)
        {
            long now = DateTime.UtcNow.Ticks;
            double cx = Math.Max(0.0, Math.Min(1.0, xNorm));
            double cy = Math.Max(0.0, Math.Min(1.0, yNorm));
            double cp = Math.Max(0.05, Math.Min(1.0, pressure));
            int pIdx = Math.Max(0, Math.Min(E8WaveLatticeMath.Presets.Length - 1, presetOverride));

            double bendSemis = GetEffectiveGyroBendSemis();
            double hz = E8WaveLatticeMath.MapNormalizedXToHz(cx, BaseMidiNote, OctaveSpan, ScaleIndex, ScaleSnap, bendSemis);
            int rootIdx = E8WaveLatticeMath.FindNearestExcitationRoot(cx, cy, GyroBetaDeg / 45.0, GyroGammaDeg / 45.0);

            if (AutoChladniFromPitch)
            {
                int deg = (int)Math.Round(cx * 12.0);
                ChladniM = 1 + (deg % 7);
                ChladniN = 2 + ((deg + (int)(cy * 6.0)) % 9);
                if (ChladniM == ChladniN) ChladniN++;
            }

            lock (_voiceLock)
            {
                int slot = -1;
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (_voices[i].Gate && _voices[i].TouchId == touchId) { slot = i; break; }
                }

                bool freshNoteOn = false;
                if (slot < 0)
                {
                    freshNoteOn = true;
                    double quietest = 999.0;
                    int steal = 0;
                    for (int i = 0; i < MaxVoices; i++)
                    {
                        if (!_voices[i].Gate) { slot = i; break; }
                        if (_voices[i].Env < quietest) { quietest = _voices[i].Env; steal = i; }
                    }
                    if (slot < 0) slot = steal;
                }

                VoiceState v = _voices[slot];
                v.TouchId = touchId;
                v.VoicePresetIdx = pIdx;
                v.IsLooperVoice = isLooperVoice;
                v.TargetX = cx;
                v.TargetY = cy;
                v.TargetPressure = cp;
                v.TargetHz = hz;
                v.E8Root = rootIdx;
                v.LastUpdateTicks = now;
                v.IsRemoteMpe = isRemoteMpe;
                v.DroneHold = DroneLatchMode && !isLooperVoice;

                if (freshNoteOn || !v.Gate)
                {
                    v.Gate = true;
                    v.SmoothX = cx;
                    v.SmoothY = cy;
                    v.SmoothHz = hz;
                    v.StrikeBurst = 0.55 + 0.45 * cp;

                    SynthPresetSpec pr = E8WaveLatticeMath.Presets[pIdx];
                    for (int p = 0; p < PartialsPerVoice; p++)
                    {
                        double chGain = E8WaveLatticeMath.EvalChladniModalGain(p, cx, cy, pr.PlateBoundaryMode);
                        double mallet = Math.Pow(p + 1.0, -(1.35 - 0.85 * pr.StrikeMalletHardness * (0.4 + 0.6 * cy)));
                        v.ModeEnergy[p] = Math.Min(1.6, v.ModeEnergy[p] * 0.25 + cp * pr.ModeWeights[p] * chGain * mallet);
                    }

                    if (pr.EngineKind == 1)
                    {
                        int period = Math.Max(8, Math.Min(KsDelayCap - 8, (int)Math.Round(SampleRate / Math.Max(28.0, hz))));
                        int pickOffset = Math.Max(1, (int)Math.Round(period * (0.12 + 0.38 * cy)));
                        double ns = 0.314159 + cx * 0.71;
                        float[] rawNoise = new float[period];
                        for (int k = 0; k < period; k++)
                        {
                            ns = (ns * 16807.0 + 0.1357) % 1.0;
                            rawNoise[k] = (float)((ns * 2.0 - 1.0) * cp * 0.92);
                        }
                        for (int k = 0; k < period; k++)
                        {
                            int combIdx = (k - pickOffset + period) % period;
                            int dstIdx = (v.KsWritePos - period + k + KsDelayCap) & (KsDelayCap - 1);
                            v.KsLine[dstIdx] = rawNoise[k] - rawNoise[combIdx] * 0.72f;
                        }
                    }

                    _e8V[rootIdx] += (float)(0.22 * cp);
                }
            }
        }

        public void ReleaseVoice(int touchId)
        {
            if (touchId < 4000 && touchId != 790)
                SeqLooper.RecordGestureIfArmed(1, touchId, 0.0, 0.0, 0.0, GyroBetaDeg, GyroGammaDeg, PresetIndex);

            lock (_voiceLock)
            {
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (_voices[i].TouchId == touchId && !_voices[i].DroneHold)
                    {
                        _voices[i].Gate = false;
                        _voices[i].TouchId = -1;
                    }
                }
            }
        }

        public void ReleaseLooperVoicesOnly()
        {
            lock (_voiceLock)
            {
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (_voices[i].IsLooperVoice)
                    {
                        _voices[i].Gate = false;
                        _voices[i].TouchId = -1;
                    }
                }
            }
        }

        public void ReleaseAllVoices(bool clearDrones)
        {
            LabToneMode = 0;
            lock (_voiceLock)
            {
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (clearDrones || !_voices[i].DroneHold)
                    {
                        _voices[i].Gate = false;
                        _voices[i].DroneHold = false;
                        _voices[i].TouchId = -1;
                    }
                }
            }
        }

        public void TriggerChordPreview(int chordType)
        {
            double[] semis = (chordType == 0)
                ? new double[] { 0.0, 7.0, 12.0, 16.0, 19.0 }
                : (chordType == 1 ? new double[] { 0.0, 3.86, 7.02, 10.88, 14.04 } : new double[] { 0.0, 5.0, 7.0, 12.0, 17.0 });
            for (int k = 0; k < semis.Length; k++)
            {
                double x = Math.Max(0.02, Math.Min(0.98, (semis[k] / (12.0 * OctaveSpan)) + 0.18));
                int tid = 900 + k;
                SetOrTriggerVoiceWithPreset(tid, x, 0.28 + 0.14 * k, 0.82, false, PresetIndex, false);
                if (!DroneLatchMode) ReleaseVoice(tid);
            }
        }

        public void UpdatePixelGyro(double betaDeg, double gammaDeg)
        {
            GyroBetaDeg = (float)Math.Max(-60.0, Math.Min(60.0, betaDeg));
            GyroGammaDeg = (float)Math.Max(-60.0, Math.Min(60.0, gammaDeg));
            Interlocked.Exchange(ref _lastGyroTicks, DateTime.UtcNow.Ticks);
        }

        private double GetEffectiveGyroBendSemis()
        {
            if (!GyroPitchBendEnabled) return 0.0;
            long ageMs = (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastGyroTicks)) / 10000L;
            if (ageMs > 2500) return 0.0;
            return (GyroGammaDeg / 35.0) * GyroBendRangeSemis;
        }

        public void BeginWavRecording()
        {
            lock (_recLock)
            {
                _recL = new List<short>(SampleRate * 30);
                _recR = new List<short>(SampleRate * 30);
                RecordedSeconds = 0;
                IsRecordingWav = true;
            }
        }

        public string StopAndSaveWavRecording(string rendersDir)
        {
            short[] left, right;
            lock (_recLock)
            {
                IsRecordingWav = false;
                left = (_recL != null) ? _recL.ToArray() : new short[0];
                right = (_recR != null) ? _recR.ToArray() : new short[0];
                _recL = null; _recR = null;
            }
            if (left.Length < 256) return "";
            Directory.CreateDirectory(rendersDir);
            string outPath = Path.Combine(rendersDir, "omega_synth_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".wav");
            File.WriteAllBytes(outPath, EncodeRiffWav16Stereo(left, right, SampleRate));
            LastSavedWavPath = outPath;
            return outPath;
        }

        private void RenderStereoBlock(short[] interleavedDst, int frames)
        {
            SeqLooper.PumpLooperPlayback(this);
            if (AutoEvolveEnabled) PumpGenerativeAutoEvolve(frames);
            if (MicInputActive && MicCapture.IsCapturing) PumpMicSynthesisModes();

            double e8Mix = Math.Max(0.0, Math.Min(1.0, E8CouplingOverride));
            double hallMix = Math.Max(0.0, Math.Min(1.0, HallShimmerOverride));
            double decayScale = Math.Max(0.2, Math.Min(4.0, DecayMultiplier));
            double bendSemis = GetEffectiveGyroBendSemis();
            double gyroTiltMod = Math.Max(-1.0, Math.Min(1.0, GyroBetaDeg / 45.0));

            long nowTicks = DateTime.UtcNow.Ticks;
            int activeCount = 0;
            double leadHz = 0.0, leadP = -1.0;
            int leadRoot = 0;

            double[,] rotCos = new double[MaxVoices, PartialsPerVoice];
            double[,] rotSin = new double[MaxVoices, PartialsPerVoice];
            double[,] modeDecay = new double[MaxVoices, PartialsPerVoice];
            double[,] bowFeed = new double[MaxVoices, PartialsPerVoice];
            double[] voicePanL = new double[MaxVoices];
            double[] voicePanR = new double[MaxVoices];
            int[] voiceRoot = new int[MaxVoices];
            int[] voiceKind = new int[MaxVoices];
            bool[] voiceAlive = new bool[MaxVoices];

            lock (_voiceLock)
            {
                for (int vIdx = 0; vIdx < MaxVoices; vIdx++)
                {
                    VoiceState v = _voices[vIdx];
                    if (v.Gate && v.IsRemoteMpe && !v.DroneHold && ((nowTicks - v.LastUpdateTicks) > 6500000L))
                    {
                        v.Gate = false; v.TouchId = -1;
                    }

                    SynthPresetSpec pr = E8WaveLatticeMath.Presets[v.VoicePresetIdx % E8WaveLatticeMath.Presets.Length];
                    if (v.Gate)
                    {
                        v.TargetHz = E8WaveLatticeMath.MapNormalizedXToHz(v.TargetX, BaseMidiNote, OctaveSpan, ScaleIndex, ScaleSnap, bendSemis);
                        v.SmoothX += (v.TargetX - v.SmoothX) * 0.24;
                        v.SmoothY += (v.TargetY - v.SmoothY) * 0.24;
                        v.SmoothHz += (v.TargetHz - v.SmoothHz) * 0.22;
                        activeCount++;
                        if (v.TargetPressure > leadP) { leadP = v.TargetPressure; leadHz = v.SmoothHz; leadRoot = v.E8Root; }
                    }

                    double eSum = v.Env;
                    for (int p = 0; p < PartialsPerVoice; p++) eSum += v.ModeEnergy[p];
                    if (pr.EngineKind == 1)
                    {
                        for (int k = 0; k < 32; k += 4)
                            eSum += Math.Abs(v.KsLine[(v.KsWritePos - k - 1 + KsDelayCap) & (KsDelayCap - 1)]);
                    }
                    if (!v.Gate && eSum < 0.0002) continue;

                    voiceAlive[vIdx] = true;
                    voiceRoot[vIdx] = v.E8Root;
                    voiceKind[vIdx] = pr.EngineKind;
                    double pan = 0.20 + 0.60 * v.SmoothX;
                    voicePanL[vIdx] = Math.Cos(pan * Math.PI * 0.5);
                    voicePanR[vIdx] = Math.Sin(pan * Math.PI * 0.5);

                    for (int p = 0; p < PartialsPerVoice; p++)
                    {
                        double disp = 1.0 + pr.PlateDispersion * 0.004 * p * p + gyroTiltMod * 0.003 * p;
                        double fHz = v.SmoothHz * pr.ModeRatios[p] * disp;
                        if (fHz > SampleRate * 0.44) fHz = SampleRate * 0.44;
                        double w = 2.0 * Math.PI * fHz / SampleRate;
                        rotCos[vIdx, p] = Math.Cos(w);
                        rotSin[vIdx, p] = Math.Sin(w);
                        double pDecaySec = (pr.BaseDecaySec * decayScale) / (1.0 + 0.48 * p);
                        modeDecay[vIdx, p] = Math.Exp(-1.0 / Math.Max(48.0, pDecaySec * SampleRate));
                        double chGain = E8WaveLatticeMath.EvalChladniModalGain(p, v.SmoothX, v.SmoothY, pr.PlateBoundaryMode);
                        double yBright = Math.Pow(p + 1.0, -(1.45 - 1.05 * v.SmoothY));
                        bowFeed[vIdx, p] = v.Gate ? (pr.SustainBowMix * v.TargetPressure * pr.ModeWeights[p] * chGain * yBright * 0.0025) : 0.0;
                    }
                }
            }

            bool anyChoir = FormantFilterEnabled || (PresetIndex == 7);
            double vowelPos = FormantVowelMorph;
            if (PresetIndex == 7 && !FormantFilterEnabled && activeCount > 0)
                vowelPos = Math.Max(0.0, Math.Min(1.0, 0.5 + GyroBetaDeg / 70.0));
            double ff1, ff2, ff3;
            E8WaveLatticeMath.InterpolateVowelFormants(vowelPos, out ff1, out ff2, out ff3);
            double svfF1 = 2.0 * Math.Sin(Math.PI * Math.Min(9000.0, ff1) / SampleRate);
            double svfF2 = 2.0 * Math.Sin(Math.PI * Math.Min(9000.0, ff2) / SampleRate);
            double svfF3 = 2.0 * Math.Sin(Math.PI * Math.Min(9000.0, ff3) / SampleRate);
            const double svfQ = 0.11;

            double samplesPerSixteenth = (SampleRate * 60.0) / (Math.Max(40, Math.Min(260, ArpBpm)) * 4.0);
            float pkL = 0.0f, pkR = 0.0f;
            float e8SymGain = (float)e8Mix;
            float masterGain = MasterVolume;
            int[] nbTable = E8WaveLatticeMath.NeighborIdx;
            float[] pwL = E8WaveLatticeMath.PickupWeightL;
            float[] pwR = E8WaveLatticeMath.PickupWeightR;
            float e8PickupL = 0.0f, e8PickupR = 0.0f;

            // Grab uploaded clip reference safely for lock-free per-sample read
            short[] clipL = null, clipR = null;
            int clipPos = ClipPlayheadSample;
            bool playClipNow = ClipPlaying;
            bool clipSymFx = ClipSympatheticFx;
            if (playClipNow)
            {
                lock (_clipLock)
                {
                    if (ActiveClip != null && ActiveClip.PcmL != null && ActiveClip.PcmL.Length > 0)
                    {
                        clipL = ActiveClip.PcmL;
                        clipR = ActiveClip.PcmR;
                    }
                    else playClipNow = false;
                }
            }

            int labMode = LabToneMode;

            for (int f = 0; f < frames; f++)
            {
                if (ArpEnabled)
                {
                    _seqSamplePhase += 1.0;
                    if (_seqSamplePhase >= samplesPerSixteenth)
                    {
                        _seqSamplePhase -= samplesPerSixteenth;
                        for (int lnIdx = 0; lnIdx < 4; lnIdx++)
                        {
                            PolyrhythmLane ln = SeqLooper.Lanes[lnIdx];
                            int st = ln.Cursor % Math.Max(1, ln.StepLength);
                            ln.Cursor = (st + 1) % Math.Max(1, ln.StepLength);
                            if (!ln.Muted && ln.Gate[st])
                            {
                                double semi = ln.OctaveOffset * 12.0 + ln.Degree[st];
                                double sx = Math.Max(0.02, Math.Min(0.98, (semi / (12.0 * OctaveSpan)) + 0.34));
                                int tid = 800 + lnIdx * 8 + (st & 3);
                                SetOrTriggerVoiceWithPreset(tid, sx, ln.TimbreY[st], 0.76, false, ln.PresetOverride, false);
                                if (!DroneLatchMode) ReleaseVoice(tid);
                            }
                        }
                    }
                }

                double dryL = 0.0, dryR = 0.0;
                for (int vIdx = 0; vIdx < MaxVoices; vIdx++)
                {
                    if (!voiceAlive[vIdx]) continue;
                    VoiceState v = _voices[vIdx];
                    if (v.Gate) v.Env += (v.TargetPressure - v.Env) * 0.0035;
                    else v.Env *= 0.9988;

                    double vSum = 0.0;
                    if (voiceKind[vIdx] == 1)
                    {
                        double exactDel = Math.Max(6.0, Math.Min(KsDelayCap - 8.0, SampleRate / Math.Max(28.0, v.SmoothHz)));
                        int dInt = (int)exactDel;
                        float frac = (float)(exactDel - dInt);
                        int r0 = (v.KsWritePos - dInt + KsDelayCap) & (KsDelayCap - 1);
                        int r1 = (r0 - 1 + KsDelayCap) & (KsDelayCap - 1);
                        float delayed = v.KsLine[r0] * (1.0f - frac) + v.KsLine[r1] * frac;

                        float bright = (float)(0.22 + 0.68 * v.SmoothY);
                        v.KsLpState = delayed * bright + v.KsLpState * (1.0f - bright);
                        float apCoeff = 0.32f;
                        float apOut = apCoeff * (v.KsLpState - v.KsApOutPrev) + v.KsApInPrev;
                        v.KsApInPrev = v.KsLpState;
                        v.KsApOutPrev = apOut;

                        float fbLoss = (float)Math.Min(0.9992, 0.991 + 0.0075 * Math.Min(1.8, DecayMultiplier));
                        float sustainInject = v.Gate ? (float)(Math.Sin(f * 0.15) * 0.0008 * v.TargetPressure) : 0.0f;
                        v.KsLine[v.KsWritePos] = apOut * fbLoss + sustainInject;
                        v.KsWritePos = (v.KsWritePos + 1) & (KsDelayCap - 1);
                        vSum = apOut * 1.35;
                    }
                    else
                    {
                        double strikeSample = 0.0;
                        if (v.StrikeBurst > 0.0005)
                        {
                            v.NoiseState = (v.NoiseState * 16807.0 + 0.12345) % 1.0;
                            strikeSample = (v.NoiseState * 2.0 - 1.0) * v.StrikeBurst * 0.14;
                            v.StrikeBurst *= 0.991;
                        }
                        vSum = strikeSample;
                        for (int p = 0; p < PartialsPerVoice; p++)
                        {
                            double re = v.ModeRe[p], im = v.ModeIm[p];
                            double nr = re * rotCos[vIdx, p] - im * rotSin[vIdx, p];
                            double ni = re * rotSin[vIdx, p] + im * rotCos[vIdx, p];
                            if ((f & 63) == 0)
                            {
                                double inv = 1.0 / Math.Sqrt(Math.Max(1e-12, nr * nr + ni * ni));
                                nr *= inv; ni *= inv;
                            }
                            v.ModeRe[p] = nr; v.ModeIm[p] = ni;
                            double en = v.ModeEnergy[p] * modeDecay[vIdx, p] + bowFeed[vIdx, p];
                            if (en > 1.5) en = 1.5;
                            v.ModeEnergy[p] = en;
                            vSum += ni * en;
                        }
                    }

                    dryL += vSum * voicePanL[vIdx];
                    dryR += vSum * voicePanR[vIdx];
                    if ((f & 3) == 0) _e8V[voiceRoot[vIdx]] += (float)(vSum * 0.036);
                }

                // Precision Acoustic Lab Generator (Sweep / 432Hz / 528Hz / 15.965Hz Binaural)
                if (labMode > 0)
                {
                    double lHz = 432.0, rHz = 432.0;
                    if (labMode == 1)
                    {
                        _labSweepPhase += 1.0 / (SampleRate * 14.0);
                        if (_labSweepPhase >= 1.0) _labSweepPhase -= 1.0;
                        lHz = 55.0 * Math.Pow(16.0, _labSweepPhase);
                        rHz = lHz * 1.002;
                        leadHz = lHz;
                        if ((f & 255) == 0 && AutoChladniFromPitch)
                        {
                            int swStep = (int)(_labSweepPhase * 24.0);
                            ChladniM = 1 + (swStep % 8);
                            ChladniN = 2 + ((swStep * 3) % 10);
                            if (ChladniM == ChladniN) ChladniN++;
                        }
                    }
                    else if (labMode == 2) { lHz = 432.0; rHz = 432.0; leadHz = 432.0; }
                    else if (labMode == 3) { lHz = 528.0; rHz = 528.0; leadHz = 528.0; }
                    else if (labMode == 4) { lHz = 216.0; rHz = 216.0 + 15.965; leadHz = 216.0; }

                    _labPhaseL += 2.0 * Math.PI * lHz / SampleRate;
                    _labPhaseR += 2.0 * Math.PI * rHz / SampleRate;
                    if (_labPhaseL > Math.PI * 200.0) _labPhaseL -= Math.PI * 200.0;
                    if (_labPhaseR > Math.PI * 200.0) _labPhaseR -= Math.PI * 200.0;
                    double tL = (Math.Sin(_labPhaseL) + 0.25 * Math.Sin(_labPhaseL * 2.0)) * 0.42;
                    double tR = (Math.Sin(_labPhaseR) + 0.25 * Math.Sin(_labPhaseR * 2.0)) * 0.42;
                    dryL += tL; dryR += tR;
                    if ((f & 7) == 0) _e8V[(int)(_labSweepPhase * 239.0) % 240] += (float)(tL * 0.05);
                }

                // Uploaded Audio File Playback + E8 Wavefield Excitation
                double fileDirectL = 0.0, fileDirectR = 0.0;
                if (playClipNow && clipL != null)
                {
                    if (clipPos >= clipL.Length)
                    {
                        if (ClipLooping) clipPos = 0;
                        else { playClipNow = false; ClipPlaying = false; }
                    }
                    if (playClipNow && clipPos < clipL.Length)
                    {
                        fileDirectL = clipL[clipPos] / 32768.0;
                        fileDirectR = clipR[clipPos] / 32768.0;
                        clipPos++;
                        if ((f & 3) == 0)
                        {
                            int rIdx = Math.Abs((int)((fileDirectL + 1.0) * 119.0)) % 240;
                            _e8V[rIdx] += (float)((fileDirectL + fileDirectR) * 0.055);
                        }
                        if (clipSymFx)
                        {
                            dryL += fileDirectL * 0.90;
                            dryR += fileDirectR * 0.90;
                        }
                    }
                }

                if (anyChoir)
                {
                    _svfLp1L += svfF1 * _svfBp1L; double hp1L = dryL - _svfLp1L - svfQ * _svfBp1L; _svfBp1L += svfF1 * hp1L;
                    _svfLp2L += svfF2 * _svfBp2L; double hp2L = dryL - _svfLp2L - svfQ * _svfBp2L; _svfBp2L += svfF2 * hp2L;
                    _svfLp3L += svfF3 * _svfBp3L; double hp3L = dryL - _svfLp3L - svfQ * _svfBp3L; _svfBp3L += svfF3 * hp3L;

                    _svfLp1R += svfF1 * _svfBp1R; double hp1R = dryR - _svfLp1R - svfQ * _svfBp1R; _svfBp1R += svfF1 * hp1R;
                    _svfLp2R += svfF2 * _svfBp2R; double hp2R = dryR - _svfLp2R - svfQ * _svfBp2R; _svfBp2R += svfF2 * hp2R;
                    _svfLp3R += svfF3 * _svfBp3R; double hp3R = dryR - _svfLp3R - svfQ * _svfBp3R; _svfBp3R += svfF3 * hp3R;

                    dryL = dryL * 0.30 + (_svfBp1L * 1.0 + _svfBp2L * 0.72 + _svfBp3L * 0.45) * 0.34;
                    dryR = dryR * 0.30 + (_svfBp1R * 1.0 + _svfBp2R * 0.72 + _svfBp3R * 0.45) * 0.34;
                }

                if ((f & 3) == 0)
                {
                    const float c2 = 0.165f, damp = 0.9978f, k0 = 0.012f, k3 = 0.085f, inv56 = 1.0f / 56.0f;
                    float accL = 0.0f, accR = 0.0f;
                    for (int i = 0; i < E8WaveLatticeMath.RootCount; i++)
                    {
                        int bOff = i * 56;
                        float nbSum = 0.0f;
                        for (int k = 0; k < 56; k++) nbSum += _e8U[nbTable[bOff + k]];
                        float ui = _e8U[i];
                        float vi = (_e8V[i] + c2 * (nbSum * inv56 - ui) - k0 * ui - k3 * ui * ui * ui) * damp;
                        if (vi > 2.5f) vi = 2.5f; else if (vi < -2.5f) vi = -2.5f;
                        _e8V[i] = vi;
                    }
                    for (int i = 0; i < E8WaveLatticeMath.RootCount; i++)
                    {
                        float ui = _e8U[i] + _e8V[i];
                        if (ui > 2.5f) ui = 2.5f; else if (ui < -2.5f) ui = -2.5f;
                        _e8U[i] = ui;
                        accL += ui * pwL[i];
                        accR += ui * pwR[i];
                    }
                    e8PickupL = accL; e8PickupR = accR;
                }

                double mixL = dryL * 0.34 + e8PickupL * e8SymGain * 0.42 + (clipSymFx ? 0.0 : fileDirectL * 0.78);
                double mixR = dryR * 0.34 + e8PickupR * e8SymGain * 0.42 + (clipSymFx ? 0.0 : fileDirectR * 0.78);

                float s1 = _dly1[_dp1], s2 = _dly2[_dp2], s3 = _dly3[_dp3], s4 = _dly4[_dp4];
                float fb = Math.Min(0.78f, 0.40f + 0.32f * (float)hallMix);
                float inL = (float)mixL, inR = (float)mixR;
                _dly1[_dp1] = inL + (s1 + s3) * 0.5f * fb;
                _dly2[_dp2] = inR + (s2 - s4) * 0.5f * fb;
                _dly3[_dp3] = inL * 0.7f + (s3 - s1) * 0.5f * fb;
                _dly4[_dp4] = inR * 0.7f + (s4 + s2) * 0.5f * fb;
                if (++_dp1 >= _dly1.Length) _dp1 = 0;
                if (++_dp2 >= _dly2.Length) _dp2 = 0;
                if (++_dp3 >= _dly3.Length) _dp3 = 0;
                if (++_dp4 >= _dly4.Length) _dp4 = 0;

                double outL = mixL + (s1 + s4 * 0.7) * hallMix * 0.42;
                double outR = mixR + (s2 + s3 * 0.7) * hallMix * 0.42;

                double dcL = outL - _dcInL + 0.996 * _dcOutL; _dcInL = outL; _dcOutL = dcL;
                double dcR = outR - _dcInR + 0.996 * _dcOutR; _dcInR = outR; _dcOutR = dcR;

                double limL = FastSoftClip(dcL * masterGain);
                double limR = FastSoftClip(dcR * masterGain);
                float al = (float)Math.Abs(limL); if (al > pkL) pkL = al;
                float ar = (float)Math.Abs(limR); if (ar > pkR) pkR = ar;

                short pcmL = (short)(limL * 32200.0);
                short pcmR = (short)(limR * 32200.0);
                interleavedDst[f * 2] = pcmL;
                interleavedDst[f * 2 + 1] = pcmR;
                if (f < 256) { _scopeL[f] = pcmL; _scopeR[f] = pcmR; }
            }

            if (clipL != null) ClipPlayheadSample = clipPos;

            // Zero-Feedback Live Web/OmegaBrowser WASAPI Pipe Overlay onto Scope, Spectrum & E8 Cymatic Geometry!
            if (WebPipeActive && WasapiPipe.CopyLatestWindow(_pipeWinL, _pipeWinR))
            {
                for (int i = 0; i < 256; i++)
                {
                    int ml = _scopeL[i] + _pipeWinL[i];
                    int mr = _scopeR[i] + _pipeWinR[i];
                    _scopeL[i] = (short)Math.Max(-32760, Math.Min(32760, ml));
                    _scopeR[i] = (short)Math.Max(-32760, Math.Min(32760, mr));
                }
                pkL = Math.Max(pkL, WasapiPipe.PeakL);
                pkR = Math.Max(pkR, WasapiPipe.PeakR);
            }

            // Zero-Feedback Optional Laptop Microphone Overlay onto Scope, Spectrum & E8 Cymatic Geometry!
            bool micVisualActive = MicInputActive && MicCapture.IsCapturing && MicCapture.CopyLatestWindow(_micWinL, _micWinR);
            if (micVisualActive)
            {
                for (int i = 0; i < 256; i++)
                {
                    int ml = _scopeL[i] + _micWinL[i];
                    int mr = _scopeR[i] + _micWinR[i];
                    _scopeL[i] = (short)Math.Max(-32760, Math.Min(32760, ml));
                    _scopeR[i] = (short)Math.Max(-32760, Math.Min(32760, mr));
                }
                pkL = Math.Max(pkL, MicCapture.PeakLevel);
                pkR = Math.Max(pkR, MicCapture.PeakLevel);
            }

            PeakLevelL = PeakLevelL * 0.72f + pkL * 0.28f;
            PeakLevelR = PeakLevelR * 0.72f + pkR * 0.28f;

            UpdateSpectralAnalysis();

            // Drive E8 visual vertices & live Chladni eigenmodes when Sound-File, Web Pipe, or Laptop Mic is active!
            if (WebPipeActive || playClipNow || micVisualActive)
            {
                for (int i = 0; i < E8WaveLatticeMath.RootCount; i++)
                {
                    float specMod = _specBins32[i % 28] * (float)Math.Sin(i * 0.37 + _scopeL[i & 255] * 0.0002);
                    _e8Snap[i] = _e8U[i] + specMod * 0.85f;
                }
                if (++_pipePitchFrameSkip >= 6)
                {
                    _pipePitchFrameSkip = 0;
                    TrackLiveDominantBinAndChladni(ref leadHz, ref leadRoot);
                }
            }
            else
            {
                for (int i = 0; i < E8WaveLatticeMath.RootCount; i++) _e8Snap[i] = _e8U[i];
            }

            ActiveVoiceCount = activeCount;
            if (leadHz > 20.0) { LeadVoiceHz = (float)leadHz; LeadVoiceRoot = leadRoot; }
            else if (activeCount == 0 && !WebPipeActive && !micVisualActive && !playClipNow && labMode == 0) LeadVoiceHz = 0.0f;

            if (IsRecordingWav)
            {
                lock (_recLock)
                {
                    if (IsRecordingWav && _recL != null && _recL.Count < SampleRate * 300)
                    {
                        for (int f = 0; f < frames; f++)
                        {
                            _recL.Add(interleavedDst[f * 2]);
                            _recR.Add(interleavedDst[f * 2 + 1]);
                        }
                        RecordedSeconds = _recL.Count / SampleRate;
                    }
                }
            }
        }

        private void PumpMicSynthesisModes()
        {
            int mode = MicSynthMode;
            float rms = MicCapture.RmsLevel;
            float gate = Math.Max(0.015f, MicCapture.NoiseGate * 1.4f);

            if (mode == 0)
            {
                if (MicCapture.DetectedHz > 65.0f && MicCapture.PitchConfidence > 0.58f)
                    LeadVoiceHz = MicCapture.DetectedHz;
                return;
            }
            if (mode == 1)
            {
                float hz = MicCapture.DetectedHz;
                if (hz >= 65.0f && hz <= 1150.0f && MicCapture.PitchConfidence >= 0.58f && rms > gate)
                {
                    double midi = 69.0 + 12.0 * (Math.Log(hz / 440.0) / Math.Log(2.0));
                    double normX = (midi - BaseMidiNote) / Math.Max(12.0, 12.0 * OctaveSpan);
                    normX = Math.Max(0.02, Math.Min(0.98, normX));
                    double pres = Math.Min(0.88, 0.25 + rms * 1.8);
                    SetOrTriggerVoice(791, normX, Math.Min(0.92, 0.35 + rms * 1.2), pres, false);
                }
                else
                {
                    ReleaseVoice(791);
                }
            }
            else if (mode == 2)
            {
                if (rms > gate * 1.25f)
                {
                    double p = Math.Min(0.72, rms * 1.45);
                    double y = Math.Min(0.90, 0.30 + _specBins32[6] * 0.65);
                    SetOrTriggerVoice(792, 0.22, y, p, false);
                    SetOrTriggerVoice(793, 0.44, y * 0.9, p * 0.85, false);
                    SetOrTriggerVoice(794, 0.64, y * 0.8, p * 0.75, false);
                }
                else
                {
                    ReleaseVoice(792);
                    ReleaseVoice(793);
                    ReleaseVoice(794);
                }
            }
            else if (mode == 3)
            {
                float strength;
                if (MicCapture.ConsumeTransientTrigger(out strength))
                {
                    double[] degX = new double[] { 0.16, 0.31, 0.46, 0.58, 0.74, 0.86 };
                    int slot = _micTapRoundRobin % 3;
                    double nx = degX[_micTapRoundRobin % degX.Length];
                    _micTapRoundRobin++;
                    int vid = 795 + slot;
                    SetOrTriggerVoice(vid, nx, Math.Min(0.92, 0.45 + strength * 0.45), Math.Min(0.95, 0.45 + strength * 0.5), false);
                    ReleaseVoice(vid);
                }
            }
        }

        private void PumpGenerativeAutoEvolve(int frames)
        {
            const double Phi = 1.61803398875;
            _evolveLfoPhase += (double)frames / SampleRate * 0.21;
            if (FormantFilterEnabled)
            {
                FormantVowelMorph = (float)(0.50 + 0.44 * Math.Sin(_evolveLfoPhase * Phi));
            }

            double intervalSec = 3.4 - Math.Max(0.05f, Math.Min(1.0f, AutoEvolveDensity)) * 2.3;
            if (MicInputActive && MicCapture.IsCapturing)
                intervalSec = Math.Max(0.45, intervalSec * (1.0 - Math.Min(0.55, MicCapture.RmsLevel * 1.2)));

            _evolveSampleTimer += frames;
            if (_evolveSampleTimer >= SampleRate * intervalSec)
            {
                _evolveSampleTimer = 0.0;
                int[] scaleWalk = new int[] { 0, 4, 7, 11, 14, 9, 12, 16, 19, 7, 11, 21 };
                int semi = scaleWalk[_evolveStepCounter % scaleWalk.Length];
                int vid = 780 + (_evolveStepCounter % 3);
                double nx = Math.Max(0.04, Math.Min(0.95, semi / Math.Max(12.0, 12.0 * OctaveSpan)));
                double ny = 0.35 + 0.45 * Math.Abs(Math.Sin(_evolveStepCounter * Phi));
                SetOrTriggerVoice(vid, nx, ny, 0.68, false);
                ReleaseVoice(vid);

                if ((_evolveStepCounter % 3) == 0)
                {
                    int mNew = 2 + ((_evolveStepCounter / 3) % 6);
                    int nNew = mNew + 1 + ((_evolveStepCounter * 2) % 5);
                    if (nNew > 12) nNew = 11;
                    ChladniM = mNew;
                    ChladniN = nNew;
                }
                _evolveStepCounter++;
            }
        }

        private void TrackLiveDominantBinAndChladni(ref double leadHz, ref int leadRoot)
        {
            int bestBin = 0;
            float bestMag = 0.04f;
            float centroidNum = 0.0f, centroidDen = 0.001f;
            for (int b = 0; b < 28; b++)
            {
                float m = _specBins32[b];
                float bHz = (float)(55.0 * Math.Pow(2.0, b * 0.23));
                centroidNum += bHz * m;
                centroidDen += m;
                if (m > bestMag) { bestMag = m; bestBin = b; }
            }
            if (bestMag > 0.05f)
            {
                double domHz = 55.0 * Math.Pow(2.0, bestBin * 0.23);
                leadHz = domHz;
                leadRoot = (bestBin * 8 + (int)(centroidNum / centroidDen) % 8) % 240;
                if (AutoChladniFromPitch)
                {
                    int mNew = 1 + (bestBin % 8);
                    int nNew = 2 + (((int)(centroidNum / Math.Max(60.0f, (float)domHz)) + bestBin) % 10);
                    if (mNew == nNew) nNew++;
                    ChladniM = mNew;
                    ChladniN = nNew;
                }
                if (WebPipeActive && WebPipeSympatheticDrone)
                {
                    double nx = Math.Max(0.04, Math.Min(0.96, Math.Log(Math.Max(55.0, domHz) / 130.81) / (Math.Log(2.0) * OctaveSpan)));
                    SetOrTriggerVoiceWithPreset(790, nx, 0.45, Math.Min(0.55, bestMag * 0.45), false, PresetIndex, false);
                }
                else if (WebPipeActive)
                {
                    ReleaseVoice(790);
                }
            }
            else if (WebPipeActive && WebPipeSympatheticDrone)
            {
                ReleaseVoice(790);
            }
        }

        private void UpdateSpectralAnalysis()
        {
            const int N = 256;
            for (int b = 0; b < 32; b++)
            {
                double hz = 55.0 * Math.Pow(2.0, b * 0.23);
                double w = (2.0 * Math.PI * hz) / SampleRate;
                double cf = 2.0 * Math.Cos(w), s1 = 0.0, s2 = 0.0;
                for (int n = 0; n < N; n++)
                {
                    double x = ((_scopeL[n] + _scopeR[n]) * 0.5 / 32768.0);
                    double s0 = x + cf * s1 - s2; s2 = s1; s1 = s0;
                }
                double mag = Math.Sqrt(Math.Max(0.0, s1 * s1 + s2 * s2 - cf * s1 * s2)) / (N * 0.14);
                _specBins32[b] = (float)(_specBins32[b] * 0.60 + Math.Min(1.5, mag * 2.4) * 0.40);
                if ((b & 3) == 0) _bands8[b >> 2] = _specBins32[b];
            }
        }

        public void CopyTelemetrySnapshots(float[] dstE8U240, short[] dstWaveL256, short[] dstWaveR256, float[] dstSpec32, MpeVoiceSnapshot[] dstVoices8)
        {
            if (dstE8U240 != null && dstE8U240.Length >= 240) Array.Copy(_e8Snap, dstE8U240, 240);
            if (dstWaveL256 != null && dstWaveL256.Length >= 256) Array.Copy(_scopeL, dstWaveL256, 256);
            if (dstWaveR256 != null && dstWaveR256.Length >= 256) Array.Copy(_scopeR, dstWaveR256, 256);
            if (dstSpec32 != null && dstSpec32.Length >= 32) Array.Copy(_specBins32, dstSpec32, 32);

            if (dstVoices8 != null)
            {
                lock (_voiceLock)
                {
                    for (int i = 0; i < 8 && i < dstVoices8.Length; i++)
                    {
                        VoiceState v = _voices[i];
                        MpeVoiceSnapshot s = new MpeVoiceSnapshot();
                        s.Active = v.Gate || v.Env > 0.03;
                        s.TouchId = v.TouchId;
                        s.NormX = v.SmoothX;
                        s.NormY = v.SmoothY;
                        s.Pressure = v.Gate ? v.TargetPressure : v.Env;
                        s.FreqHz = v.SmoothHz;
                        s.NearestE8Root = v.E8Root;
                        dstVoices8[i] = s;
                    }
                }
            }
        }

        private static double FastSoftClip(double x)
        {
            if (x > 2.5) return 0.985;
            if (x < -2.5) return -0.985;
            double x2 = x * x;
            return x * (27.0 + x2) / (27.0 + 9.0 * x2);
        }

        public static byte[] EncodeRiffWav16Stereo(short[] left, short[] right, int sampleRate)
        {
            int frames = Math.Min(left.Length, right.Length);
            int dataBytes = frames * 4;
            byte[] wav = new byte[44 + dataBytes];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
            BitConverter.GetBytes(36 + dataBytes).CopyTo(wav, 4);
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((ushort)1).CopyTo(wav, 20);
            BitConverter.GetBytes((ushort)2).CopyTo(wav, 22);
            BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
            BitConverter.GetBytes(sampleRate * 4).CopyTo(wav, 28);
            BitConverter.GetBytes((ushort)4).CopyTo(wav, 32);
            BitConverter.GetBytes((ushort)16).CopyTo(wav, 34);
            Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
            BitConverter.GetBytes(dataBytes).CopyTo(wav, 40);

            int o = 44;
            for (int i = 0; i < frames; i++)
            {
                short l = left[i], r = right[i];
                wav[o++] = (byte)(l & 255); wav[o++] = (byte)((l >> 8) & 255);
                wav[o++] = (byte)(r & 255); wav[o++] = (byte)((r >> 8) & 255);
            }
            return wav;
        }

        public void Stop()
        {
            _running = false;
            WasapiPipe.Stop();
            MicCapture.Stop();
            try { if (_waveEvent != null) _waveEvent.Set(); } catch { }
            try { if (_pumpThread != null && _pumpThread.IsAlive) _pumpThread.Join(350); } catch { }
            if (_hWaveOut != IntPtr.Zero)
            {
                try
                {
                    waveOutReset(_hWaveOut);
                    int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
                    if (_hdrPins != null)
                    {
                        for (int b = 0; b < _hdrPins.Length; b++)
                            if (_hdrPins[b].IsAllocated) waveOutUnprepareHeader(_hWaveOut, _hdrPins[b].AddrOfPinnedObject(), (uint)hdrSize);
                    }
                    waveOutClose(_hWaveOut);
                }
                catch { }
                _hWaveOut = IntPtr.Zero;
            }
            if (_pcmPins != null) for (int i = 0; i < _pcmPins.Length; i++) if (_pcmPins[i].IsAllocated) _pcmPins[i].Free();
            if (_hdrPins != null) for (int i = 0; i < _hdrPins.Length; i++) if (_hdrPins[i].IsAllocated) _hdrPins[i].Free();
            if (_waveEvent != null) { try { _waveEvent.Close(); } catch { } _waveEvent = null; }
        }

        public void Dispose() { Stop(); }
    }
}
