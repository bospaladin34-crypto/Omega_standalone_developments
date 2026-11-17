using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaSynthStudio
{
    public sealed class OmegaPatchBankEntry
    {
        public string Title = "";
        public string FilePath = "";
        public override string ToString()
        {
            return Title;
        }
    }

    public static class OmegaSynthPatchBank
    {
        public static List<OmegaPatchBankEntry> EnsureFactoryPatchesAndList(string patchesDir)
        {
            Directory.CreateDirectory(patchesDir);
            SeedFactoryIfMissing(patchesDir, "01_Sovereign_E8_Cathedral_Choir.osynth",
                BuildFactoryJson("01. Sovereign E8 Cathedral Choir", 7, 1, 48, 4, 7, false, 0.35f, 0.62f, 0.56f, 1.35f, true, 0.32f, 0.80f, true, 84,
                    new int[] { 5, 2, 7, 0 }, new int[] { -1, 0, 0, 1 }, new int[] { 16, 12, 9, 16 }, new bool[] { false, false, false, false },
                    new int[] { 4, 5, 5, 4 }));

            SeedFactoryIfMissing(patchesDir, "02_Golden_Phi_Karplus_Harp_9v12v16.osynth",
                BuildFactoryJson("02. Golden Phi Karplus Harp (9v12v16)", 6, 4, 48, 5, 8, true, 0.65f, 0.48f, 0.42f, 1.15f, false, 0.25f, 0.78f, true, 108,
                    new int[] { 5, 6, 6, 2 }, new int[] { -1, 0, 1, 0 }, new int[] { 16, 12, 9, 14 }, new bool[] { false, false, false, false },
                    new int[] { 4, 5, 7, 5 }));

            SeedFactoryIfMissing(patchesDir, "03_Chladni_4_7_Quartz_Singing_Mandala.osynth",
                BuildFactoryJson("03. Chladni(4,7) Quartz Singing Mandala", 2, 1, 57, 4, 7, false, 0.25f, 0.58f, 0.52f, 1.50f, false, 0.20f, 0.78f, false, 78,
                    new int[] { 2, 2, 4, 0 }, new int[] { -1, 0, 0, 1 }, new int[] { 16, 12, 8, 16 }, new bool[] { false, false, false, true },
                    new int[] { 3, 4, 3, 2 }));

            SeedFactoryIfMissing(patchesDir, "04_Sub_Lattice_Tibetan_Gong_Drone.osynth",
                BuildFactoryJson("04. Sub-Lattice Tibetan Gong Drone", 5, 5, 36, 3, 8, false, 0.15f, 0.74f, 0.54f, 1.75f, true, 0.68f, 0.82f, true, 68,
                    new int[] { 5, 1, 2, 7 }, new int[] { 0, 0, 1, 1 }, new int[] { 16, 10, 12, 16 }, new bool[] { false, false, false, false },
                    new int[] { 2, 3, 4, 2 }));

            SeedFactoryIfMissing(patchesDir, "05_Bohlen_Pierce_Glass_Harmonica.osynth",
                BuildFactoryJson("05. Bohlen-Pierce Tritave Glass Harmonica", 4, 3, 48, 6, 9, true, 0.45f, 0.50f, 0.50f, 1.25f, false, 0.40f, 0.76f, true, 96,
                    new int[] { 4, 4, 0, 6 }, new int[] { -1, 0, 0, 1 }, new int[] { 13, 9, 13, 7 }, new bool[] { false, false, false, false },
                    new int[] { 4, 4, 5, 3 }));

            SeedFactoryIfMissing(patchesDir, "06_Handpan_Euclidean_Tribal_Matrix.osynth",
                BuildFactoryJson("06. Titanium Handpan Euclidean Matrix", 3, 0, 48, 5, 7, true, 0.85f, 0.40f, 0.34f, 0.95f, false, 0.25f, 0.80f, true, 122,
                    new int[] { 5, 3, 3, 6 }, new int[] { -1, 0, 1, 0 }, new int[] { 16, 12, 8, 11 }, new bool[] { false, false, false, false },
                    new int[] { 5, 7, 5, 6 }));

            SeedFactoryIfMissing(patchesDir, "07_Ambient_Room_Mic_E8_Cymatic_Pad.osynth",
                BuildFactoryJson("07. Ambient Room Mic -> E8 Cymatic Pad", 0, 4, 48, 4, 9, true, 0.40f, 0.68f, 0.58f, 1.55f, true, 0.42f, 0.80f, false, 88,
                    new int[] { 5, 0, 2, 7 }, new int[] { -1, 0, 0, 1 }, new int[] { 16, 12, 9, 16 }, new bool[] { false, false, false, false },
                    new int[] { 3, 4, 5, 3 }));

            SeedFactoryIfMissing(patchesDir, "08_Pattee_Canyon_Generative_Drift.osynth",
                BuildFactoryJson("08. Pattee Canyon Generative Night Drift", 2, 1, 48, 3, 7, false, 0.50f, 0.64f, 0.62f, 1.65f, true, 0.35f, 0.78f, true, 76,
                    new int[] { 5, 2, 6, 7 }, new int[] { -1, 0, 1, 0 }, new int[] { 16, 11, 9, 13 }, new bool[] { false, false, false, false },
                    new int[] { 3, 4, 4, 3 }));

            List<OmegaPatchBankEntry> entries = new List<OmegaPatchBankEntry>();
            string[] files = Directory.GetFiles(patchesDir, "*.osynth");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string raw = "";
                try { raw = File.ReadAllText(files[i], Encoding.UTF8); } catch { }
                string nm = ExtractStr(raw, "name");
                if (string.IsNullOrEmpty(nm)) nm = Path.GetFileNameWithoutExtension(files[i]);
                OmegaPatchBankEntry e = new OmegaPatchBankEntry();
                e.Title = nm;
                e.FilePath = files[i];
                entries.Add(e);
            }
            return entries;
        }

        private static void SeedFactoryIfMissing(string dir, string fileName, string content)
        {
            string p = Path.Combine(dir, fileName);
            if (!File.Exists(p))
            {
                try { File.WriteAllText(p, content, new UTF8Encoding(false)); } catch { }
            }
        }

        private static string BuildFactoryJson(
            string name, int preset, int scale, int baseMidi, int m, int n, bool autoMN,
            float snap, float e8, float hall, float decay, bool fmtOn, float fmtMorph, float vol,
            bool arpOn, int bpm, int[] lnPresets, int[] lnOcts, int[] lnLens, bool[] lnMutes, int[] euclidPulses)
        {
            OmegaSequencerAndLooper tmp = new OmegaSequencerAndLooper();
            for (int l = 0; l < 4; l++)
            {
                tmp.Lanes[l].PresetOverride = lnPresets[l];
                tmp.Lanes[l].OctaveOffset = lnOcts[l];
                tmp.Lanes[l].Muted = lnMutes[l];
                tmp.ApplyEuclideanToLane(l, euclidPulses[l], lnLens[l], preset + l * 2);
            }
            return SerializeStateToJson(name, preset, scale, baseMidi, m, n, autoMN, snap, e8, hall, decay, fmtOn, fmtMorph, vol, arpOn, bpm, tmp);
        }

        public static void SaveEnginePatchToFile(string filePath, string patchName, OmegaWavefieldAudioEngine audio)
        {
            string json = SerializeStateToJson(
                patchName, audio.PresetIndex, audio.ScaleIndex, audio.BaseMidiNote,
                audio.ChladniM, audio.ChladniN, audio.AutoChladniFromPitch,
                audio.ScaleSnap, audio.E8CouplingOverride, audio.HallShimmerOverride, audio.DecayMultiplier,
                audio.FormantFilterEnabled, audio.FormantVowelMorph, audio.MasterVolume,
                audio.ArpEnabled, audio.ArpBpm, audio.SeqLooper);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
        }

        public static string SerializeStateToJson(
            string patchName, int preset, int scale, int baseMidi, int m, int n, bool autoMN,
            float snap, float e8, float hall, float decay, bool fmtOn, float fmtMorph, float vol,
            bool arpOn, int bpm, OmegaSequencerAndLooper seq)
        {
            SynthPresetSpec resynth = E8WaveLatticeMath.Presets[E8WaveLatticeMath.CustomResynthPresetIndex];
            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine("{");
            sb.AppendLine("  \"format\": \"OmegaSynthPatch_v1.1_Final_GM\",");
            sb.AppendLine("  \"name\": \"" + Esc(patchName) + "\",");
            sb.AppendLine("  \"preset\": " + preset + ",");
            sb.AppendLine("  \"scale\": " + scale + ",");
            sb.AppendLine("  \"base_midi\": " + baseMidi + ",");
            sb.AppendLine("  \"chladni_m\": " + m + ",");
            sb.AppendLine("  \"chladni_n\": " + n + ",");
            sb.AppendLine("  \"auto_mn\": " + (autoMN ? "true" : "false") + ",");
            sb.AppendLine("  \"snap\": " + F(snap) + ",");
            sb.AppendLine("  \"e8_mix\": " + F(e8) + ",");
            sb.AppendLine("  \"hall_mix\": " + F(hall) + ",");
            sb.AppendLine("  \"decay_mult\": " + F(decay) + ",");
            sb.AppendLine("  \"formant_on\": " + (fmtOn ? "true" : "false") + ",");
            sb.AppendLine("  \"formant_morph\": " + F(fmtMorph) + ",");
            sb.AppendLine("  \"master_vol\": " + F(vol) + ",");
            sb.AppendLine("  \"arp_on\": " + (arpOn ? "true" : "false") + ",");
            sb.AppendLine("  \"bpm\": " + bpm + ",");
            sb.AppendLine("  \"resynth_label\": \"" + Esc(E8WaveLatticeMath.CustomResynthSourceLabel) + "\",");
            sb.AppendLine("  \"resynth_decay\": " + F((float)resynth.BaseDecaySec) + ",");
            sb.AppendLine("  \"resynth_bow\": " + F((float)resynth.SustainBowMix) + ",");
            sb.AppendLine("  \"resynth_boundary\": " + resynth.PlateBoundaryMode + ",");
            sb.AppendLine("  \"resynth_ratios\": [" + JoinDoubles(resynth.ModeRatios) + "],");
            sb.AppendLine("  \"resynth_weights\": [" + JoinDoubles(resynth.ModeWeights) + "],");

            for (int l = 0; l < 4; l++)
            {
                PolyrhythmLane ln = seq.Lanes[l];
                sb.AppendLine("  \"lane" + l + "_cfg\": \"" + ln.PresetOverride + "," + ln.OctaveOffset + "," + ln.StepLength + "," + (ln.Muted ? "1" : "0") + "\",");
                StringBuilder st = new StringBuilder();
                for (int s = 0; s < 16; s++)
                {
                    if (s > 0) st.Append("|");
                    st.Append(ln.Gate[s] ? "1" : "0").Append(":").Append(ln.Degree[s]).Append(":").Append(F(ln.TimbreY[s]));
                }
                sb.AppendLine("  \"lane" + l + "_steps\": \"" + st.ToString() + "\",");
            }

            List<MpeGestureEvent> evs = seq.ExportLoopEventsSnapshot();
            sb.AppendLine("  \"loop_bars\": " + seq.LoopBars + ",");
            sb.AppendLine("  \"loop_dur_ms\": " + seq.LoopDurationMs + ",");
            StringBuilder lb = new StringBuilder();
            int cap = Math.Min(evs.Count, 4000);
            for (int i = 0; i < cap; i++)
            {
                MpeGestureEvent ev = evs[i];
                if (i > 0) lb.Append(";");
                lb.Append(ev.TickOffsetMs).Append(",")
                  .Append(ev.Kind).Append(",")
                  .Append(ev.TouchId).Append(",")
                  .Append(F(ev.NormX)).Append(",")
                  .Append(F(ev.NormY)).Append(",")
                  .Append(F(ev.Pressure)).Append(",")
                  .Append(ev.PresetIdx);
            }
            sb.AppendLine("  \"loop_events\": \"" + lb.ToString() + "\"");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string LoadPatchIntoEngine(string filePath, OmegaWavefieldAudioEngine audio, OmegaCymaticsGpuEngine gpu)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return "Patch file not found.";
            string raw = File.ReadAllText(filePath, Encoding.UTF8);
            string nm = ExtractStr(raw, "name");
            if (string.IsNullOrEmpty(nm)) nm = Path.GetFileNameWithoutExtension(filePath);

            double[] rsRatios = ExtractDoubleArray(raw, "resynth_ratios");
            double[] rsWeights = ExtractDoubleArray(raw, "resynth_weights");
            string rsLbl = ExtractStr(raw, "resynth_label");
            if (rsRatios.Length == 8 && rsWeights.Length == 8 && !string.IsNullOrEmpty(rsLbl))
            {
                double rsDec = ExtractFloat(raw, "resynth_decay", 3.8f);
                double rsBow = ExtractFloat(raw, "resynth_bow", 0.62f);
                int rsBnd = ExtractInt(raw, "resynth_boundary", 2, 0, 3);
                E8WaveLatticeMath.UpdateCustomResynthesisPreset(rsLbl, rsRatios, rsWeights, rsDec, rsBow, rsBnd);
            }

            audio.PresetIndex = ExtractInt(raw, "preset", audio.PresetIndex, 0, E8WaveLatticeMath.Presets.Length - 1);
            audio.ScaleIndex = ExtractInt(raw, "scale", audio.ScaleIndex, 0, E8WaveLatticeMath.ScaleNames.Length - 1);
            audio.BaseMidiNote = ExtractInt(raw, "base_midi", audio.BaseMidiNote, 24, 72);
            audio.ChladniM = ExtractInt(raw, "chladni_m", audio.ChladniM, 1, 12);
            audio.ChladniN = ExtractInt(raw, "chladni_n", audio.ChladniN, 1, 12);
            audio.AutoChladniFromPitch = ExtractBool(raw, "auto_mn", audio.AutoChladniFromPitch);
            audio.ScaleSnap = ExtractFloat(raw, "snap", audio.ScaleSnap);
            audio.E8CouplingOverride = ExtractFloat(raw, "e8_mix", audio.E8CouplingOverride);
            audio.HallShimmerOverride = ExtractFloat(raw, "hall_mix", audio.HallShimmerOverride);
            audio.DecayMultiplier = ExtractFloat(raw, "decay_mult", audio.DecayMultiplier);
            audio.FormantFilterEnabled = ExtractBool(raw, "formant_on", audio.FormantFilterEnabled);
            audio.FormantVowelMorph = ExtractFloat(raw, "formant_morph", audio.FormantVowelMorph);
            audio.MasterVolume = ExtractFloat(raw, "master_vol", audio.MasterVolume);
            audio.ArpEnabled = ExtractBool(raw, "arp_on", audio.ArpEnabled);
            audio.ArpBpm = ExtractInt(raw, "bpm", audio.ArpBpm, 45, 220);

            for (int l = 0; l < 4; l++)
            {
                PolyrhythmLane ln = audio.SeqLooper.Lanes[l];
                string cfg = ExtractStr(raw, "lane" + l + "_cfg");
                if (!string.IsNullOrEmpty(cfg))
                {
                    string[] cp = cfg.Split(',');
                    if (cp.Length >= 4)
                    {
                        int po, oo, sl, mu;
                        if (int.TryParse(cp[0], out po)) ln.PresetOverride = Math.Max(0, Math.Min(E8WaveLatticeMath.Presets.Length - 1, po));
                        if (int.TryParse(cp[1], out oo)) ln.OctaveOffset = Math.Max(-2, Math.Min(2, oo));
                        if (int.TryParse(cp[2], out sl)) ln.StepLength = Math.Max(4, Math.Min(16, sl));
                        if (int.TryParse(cp[3], out mu)) ln.Muted = (mu != 0);
                    }
                }
                string steps = ExtractStr(raw, "lane" + l + "_steps");
                if (!string.IsNullOrEmpty(steps))
                {
                    string[] sp = steps.Split('|');
                    for (int s = 0; s < 16 && s < sp.Length; s++)
                    {
                        string[] f = sp[s].Split(':');
                        if (f.Length >= 3)
                        {
                            ln.Gate[s] = (f[0] == "1");
                            int dg; if (int.TryParse(f[1], out dg)) ln.Degree[s] = Math.Max(-12, Math.Min(36, dg));
                            float ty; if (float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out ty)) ln.TimbreY[s] = Math.Max(0.05f, Math.Min(1.0f, ty));
                        }
                    }
                }
            }

            int lBars = ExtractInt(raw, "loop_bars", 4, 1, 16);
            int lDur = ExtractInt(raw, "loop_dur_ms", 8000, 1500, 120000);
            string lEvRaw = ExtractStr(raw, "loop_events");
            List<MpeGestureEvent> evList = new List<MpeGestureEvent>();
            if (!string.IsNullOrEmpty(lEvRaw))
            {
                string[] items = lEvRaw.Split(';');
                for (int i = 0; i < items.Length; i++)
                {
                    string[] f = items[i].Split(',');
                    if (f.Length >= 7)
                    {
                        MpeGestureEvent ev = new MpeGestureEvent();
                        int tms, tid, pidx; byte knd; float nx, ny, pr;
                        if (int.TryParse(f[0], out tms) && byte.TryParse(f[1], out knd) && int.TryParse(f[2], out tid) &&
                            float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out nx) &&
                            float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out ny) &&
                            float.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out pr) &&
                            int.TryParse(f[6], out pidx))
                        {
                            ev.TickOffsetMs = tms; ev.Kind = knd; ev.TouchId = tid;
                            ev.NormX = nx; ev.NormY = ny; ev.Pressure = pr; ev.PresetIdx = pidx;
                            evList.Add(ev);
                        }
                    }
                }
            }
            audio.SeqLooper.ImportLoopEventsSnapshot(evList, lDur, lBars, true);
            if (gpu != null) gpu.ResetSandAndWavefield();
            return nm;
        }

        private static string F(float v) { return v.ToString("0.0000", CultureInfo.InvariantCulture); }
        private static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " "); }

        private static string JoinDoubles(double[] arr)
        {
            if (arr == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(arr[i].ToString("0.0000", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string ExtractStr(string json, string key)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : "";
        }

        private static int ExtractInt(string json, string key, int defVal, int min, int max)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
            int v;
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out v)) return defVal;
            return Math.Max(min, Math.Min(max, v));
        }

        private static float ExtractFloat(string json, string key, float defVal)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            float v;
            if (!m.Success || !float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return defVal;
            return v;
        }

        private static bool ExtractBool(string json, string key, bool defVal)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (!m.Success) return defVal;
            return m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static double[] ExtractDoubleArray(string json, string key)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!m.Success) return new double[0];
            string[] parts = m.Groups[1].Value.Split(',');
            List<double> list = new List<double>();
            for (int i = 0; i < parts.Length; i++)
            {
                double v;
                if (double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) list.Add(v);
            }
            return list.ToArray();
        }
    }
}
