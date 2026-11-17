using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using OmegaZeroCrateVault;

namespace OmegaStegStudio
{
    public sealed class OmegaVaultDrawerDialog : Form
    {
        private TextBox _txtLog;

        public OmegaVaultDrawerDialog()
        {
            Text = "OmegaSteg v3.0.0 + OmegaVault (:19786) :: Zero-Crate CAS Vault, Torn-Power Seqlock WAL, 3-Node CRDT & Supply-Chain Firewall";
            Size = new Size(920, 580);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(10, 15, 28);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.0f);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 48;
            top.BackColor = Color.FromArgb(15, 23, 42);

            Button bRun = new Button();
            bRun.Text = "Re-Run 8-Vector Zero-Crate CAS + Seqlock WAL + CRDT + Supply-Chain Audit";
            bRun.SetBounds(12, 9, 520, 30);
            bRun.FlatStyle = FlatStyle.Flat;
            bRun.BackColor = Color.FromArgb(5, 150, 105);
            bRun.ForeColor = Color.White;
            bRun.Click += delegate { ExecuteAudit(); };
            top.Controls.Add(bRun);

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

            ExecuteAudit();
        }

        private void ExecuteAudit()
        {
            Track03StressSummary s = OmegaVaultStressSuite.RunAll8Vectors();
            _txtLog.AppendText(string.Format(CultureInfo.InvariantCulture,
                "=== ZERO-CRATE VAULT 8-VECTOR AUDIT: {0}/{1} PASS ({2} trials in {3:0.1} ms | RAM={4:0.1} MB) ===\r\n",
                s.PassedCount, s.TotalCount, s.TotalTrials, s.TotalMs, s.TrimmedWorkingSetMb));
            for (int i = 0; i < s.Vectors.Count; i++)
            {
                Track03VectorResult v = s.Vectors[i];
                _txtLog.AppendText("  [" + (v.Passed ? "PASS" : "FAIL") + "] " + v.Code + " (" + v.Matched + "/" + v.Trials + ") -> " + v.Detail + "\r\n");
            }
        }
    }
}
