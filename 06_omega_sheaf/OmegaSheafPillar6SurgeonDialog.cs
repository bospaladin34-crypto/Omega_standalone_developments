using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using OmegaSheafModSurgeon;

namespace OmegaSheafStudio
{
    public sealed class OmegaSheafPillar6SurgeonDialog : Form
    {
        private TextBox _txtLog;

        public OmegaSheafPillar6SurgeonDialog()
        {
            Text = "OmegaSheaf v3.0.0 Pillar 6 (:19784) :: Polyglot repr(C) FFI, Quest/Mod DAG & SIMD AOB Surgeon";
            Size = new Size(920, 620);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(10, 15, 28);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.0f);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 50;
            top.BackColor = Color.FromArgb(15, 23, 42);

            Button bAudit = MakeBtn("1. Run 8-Vector FFI/Mod/AOB Stress Suite", 12, 10, 280, Color.FromArgb(5, 150, 105));
            bAudit.Click += delegate { RunFull8VectorAudit(); };
            top.Controls.Add(bAudit);

            Button bHeaders = MakeBtn("2. Generate Lockstep Rust + C++23 + C# Headers", 300, 10, 310, Color.FromArgb(109, 40, 217));
            bHeaders.Click += delegate { EmitSampleLockstepHeaders(); };
            top.Controls.Add(bHeaders);

            Button bSeh = MakeBtn("3. Sample SEH 0xC0000005 Crash Triage", 618, 10, 270, Color.FromArgb(180, 83, 9));
            bSeh.Click += delegate
            {
                CrashTriageReport cr = OmegaSheafModCore.DiagnoseSehCrashRecord(0xC0000005u, 0x7FF820014080UL, 0x0000000000000018UL);
                _txtLog.AppendText(string.Format(CultureInfo.InvariantCulture,
                    "\r\n[SEH AUTOPSY] {0} (0x{1:X8}) in {2}+0x{3:X} :: {4} -> {5}\r\n",
                    cr.ExceptionName, cr.ExceptionCode, cr.FaultingModule, cr.ModuleRvaOffset, cr.RootCauseCategory, cr.SurgicalPrescription));
            };
            top.Controls.Add(bSeh);

            _txtLog = new TextBox();
            _txtLog.Dock = DockStyle.Fill;
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.BackColor = Color.FromArgb(6, 10, 19);
            _txtLog.ForeColor = Color.FromArgb(167, 243, 208);
            _txtLog.Font = new Font("Consolas", 9.0f);

            Controls.Add(_txtLog);
            Controls.Add(top);

            RunFull8VectorAudit();
        }

        private static Button MakeBtn(string text, int x, int y, int w, Color bg)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg;
            b.ForeColor = Color.White;
            return b;
        }

        private void RunFull8VectorAudit()
        {
            Track05StressSummary s = OmegaSheafModStressSuite.RunAll8Vectors();
            _txtLog.AppendText(string.Format(CultureInfo.InvariantCulture,
                "=== PILLAR 6 SHEAF FFI/MOD/AOB SURGEON: {0}/{1} PASS ({2} trials in {3:0.1} ms | RAM={4:0.1} MB) ===\r\n",
                s.PassedCount, s.TotalCount, s.TotalTrials, s.TotalMs, s.TrimmedWorkingSetMb));
            for (int i = 0; i < s.Vectors.Count; i++)
            {
                Track05VectorResult v = s.Vectors[i];
                _txtLog.AppendText("  [" + (v.Passed ? "PASS" : "FAIL") + "] " + v.Code + " (" + v.Matched + "/" + v.Trials + ") -> " + v.Detail + "\r\n");
            }
        }

        private static PolyglotFieldDecl MakeField(string name, int size, int align, string rs, string cpp, string cs)
        {
            PolyglotFieldDecl f = new PolyglotFieldDecl();
            f.Name = name;
            f.ByteSize = size;
            f.NaturalAlign = align;
            f.RustType = rs;
            f.CppType = cpp;
            f.CSharpType = cs;
            return f;
        }

        private void EmitSampleLockstepHeaders()
        {
            List<PolyglotFieldDecl> fields = new List<PolyglotFieldDecl>();
            fields.Add(MakeField("epoch_seq", 8, 8, "u64", "uint64_t", "ulong"));
            fields.Add(MakeField("actor_form_id", 4, 4, "u32", "uint32_t", "uint"));
            fields.Add(MakeField("quest_stage", 2, 2, "u16", "uint16_t", "ushort"));
            fields.Add(MakeField("flags_mask", 2, 2, "u16", "uint16_t", "ushort"));
            StalkStructLayout layout;
            string bundle = OmegaSheafModCore.GenerateLockstepPolyglotHeaders("SovereignBridgeFrame", fields, out layout);
            _txtLog.AppendText("\r\n--- LOCKSTEP #[repr(C)] STRUCT TRIPLE (TotalSize=" + layout.TotalSize + "B, Align=" + layout.StructAlign + "B) ---\r\n" + bundle + "\r\n");
        }
    }
}
