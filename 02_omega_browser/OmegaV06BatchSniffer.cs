using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmegaBrowserV03
{
    public sealed partial class BrowserForm
    {
        public async Task OpenBatchLinkSnifferAsync()
        {
            if (_active == null || _active.View == null || _active.View.CoreWebView2 == null)
            {
                _lblStatusL.Text = "Open a live web tab first to sniff downloadable links, PDFs, or audio.";
                return;
            }

            string snifferJs = "(function(){" +
                "var out=[], seen={};" +
                "function add(u,lbl,tg){ if(!u) return; if(u.indexOf('javascript:')===0||u.indexOf('mailto:')===0||u.indexOf('data:')===0||u.indexOf('blob:')===0) return;" +
                "  var clean=u.split('#')[0]; if(!clean||seen[clean]) return; seen[clean]=1;" +
                "  var t=(lbl||'').replace(/[\\r\\n\\t]+/g,' ').replace(/\\s+/g,' ').trim().slice(0,110);" +
                "  out.push(clean+'\\t'+t+'\\t'+tg); }" +
                "var els=document.querySelectorAll('a[href],audio[src],video[src],source[src],embed[src],iframe[src]');" +
                "for(var i=0;i<els.length&&out.length<480;i++){" +
                "  var e=els[i], tg=(e.tagName||'a').toLowerCase();" +
                "  var href=e.href||e.src||'';" +
                "  var lbl=e.innerText||e.getAttribute('download')||e.getAttribute('title')||e.getAttribute('aria-label')||'';" +
                "  add(href,lbl,tg);" +
                "  var m=href.match(/^https?:\\/\\/(?:export\\.)?arxiv\\.org\\/abs\\/([0-9]{4}\\.[0-9]{4,5}(?:v[0-9]+)?)/i);" +
                "  if(m) add('https://arxiv.org/pdf/'+m[1]+'.pdf', '[arXiv PDF] '+(lbl||m[1]), 'arxiv-pdf');" +
                "}" +
                "return out.join('\\n'); })();";

            string rawJson = await _active.View.CoreWebView2.ExecuteScriptAsync(snifferJs);
            string rawLines = OmegaJson.UnwrapJsString(rawJson);

            List<SniffedLinkCandidate> allItems = new List<SniffedLinkCandidate>();
            if (!string.IsNullOrEmpty(rawLines))
            {
                string[] rows = rawLines.Split('\n');
                for (int i = 0; i < rows.Length; i++)
                {
                    string[] cols = rows[i].Split('\t');
                    if (cols.Length < 1 || string.IsNullOrWhiteSpace(cols[0])) continue;
                    string u = OmegaUrlCleaner.CleanUrl(cols[0].Trim());
                    if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        continue;

                    SniffedLinkCandidate c = new SniffedLinkCandidate();
                    c.Url = u;
                    c.FileName = OmegaDownloadRouter.InferCleanFileName("", u);
                    c.Category = OmegaDownloadRouter.ClassifyCategory(c.FileName, u);
                    c.Label = cols.Length >= 2 && !string.IsNullOrWhiteSpace(cols[1]) ? cols[1].Trim() : c.FileName;
                    c.TagSource = cols.Length >= 3 ? cols[2].Trim() : "a";
                    c.IsDownloadableAsset = OmegaDownloadRouter.IsRecognizedAssetUrl(u, c.FileName) || c.TagSource == "audio" || c.TagSource == "source" || c.TagSource == "arxiv-pdf";
                    allItems.Add(c);
                }
            }

            ShowBatchSnifferModal(_active.Title, _active.Url, allItems);
        }

        private void ShowBatchSnifferModal(string pageTitle, string pageUrl, List<SniffedLinkCandidate> allItems)
        {
            Form dlg = new Form();
            dlg.Text = "Batch Link, Paper & Audio Sniffer (Ctrl+Shift+D) :: " + Shorten(pageTitle, 52);
            dlg.Size = new Size(1060, 620);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.BackColor = Color.FromArgb(9, 13, 22);
            dlg.ForeColor = Color.White;
            if (Icon != null) dlg.Icon = Icon;

            Panel top = new Panel(); top.Dock = DockStyle.Top; top.Height = 76; top.BackColor = Color.FromArgb(14, 21, 37); top.Padding = new Padding(10, 6, 10, 6);
            FlowLayoutPanel filterRow = new FlowLayoutPanel(); filterRow.SetBounds(8, 6, 1024, 30); filterRow.WrapContents = false;

            string activeMode = "Assets";
            TextBox txtRegex = new TextBox(); txtRegex.SetBounds(112, 42, 340, 24);
            txtRegex.BackColor = Color.FromArgb(7, 11, 19); txtRegex.ForeColor = Color.White; txtRegex.Font = new Font("Consolas", 9.0f); txtRegex.BorderStyle = BorderStyle.FixedSingle;

            Label lblCount = new Label(); lblCount.SetBounds(464, 45, 560, 20); lblCount.Font = new Font("Consolas", 8.6f); lblCount.ForeColor = Color.FromArgb(56, 189, 248);

            ListView lv = new ListView();
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.CheckBoxes = true;
            lv.FullRowSelect = true;
            lv.BackColor = Color.FromArgb(7, 11, 19);
            lv.ForeColor = Color.FromArgb(226, 232, 240);
            lv.Font = new Font("Consolas", 8.9f);
            lv.BorderStyle = BorderStyle.None;
            EnableListViewDoubleBuffer(lv);
            lv.Columns.Add("Category", 92);
            lv.Columns.Add("Target File Name", 230);
            lv.Columns.Add("Link Title / Context", 290);
            lv.Columns.Add("Source URL", 400);

            Action rebuildFilter = delegate
            {
                string rxTxt = txtRegex.Text.Trim();
                Regex rx = null;
                if (rxTxt.Length > 0)
                {
                    try { rx = new Regex(rxTxt, RegexOptions.IgnoreCase); } catch { rx = null; }
                }

                lv.BeginUpdate();
                lv.Items.Clear();
                for (int i = 0; i < allItems.Count; i++)
                {
                    SniffedLinkCandidate c = allItems[i];
                    bool keep = false;
                    if (activeMode == "All") keep = true;
                    else if (activeMode == "Assets") keep = c.IsDownloadableAsset;
                    else if (activeMode == "Papers") keep = (c.Category == "Papers");
                    else if (activeMode == "Audio") keep = (c.Category == "Audio" || c.Category == "Shaders");
                    else if (activeMode == "DataModels") keep = (c.Category == "Models" || c.Category == "Datasets");
                    else if (activeMode == "CodeArch") keep = (c.Category == "Code" || c.Category == "Archives");

                    if (!keep) continue;
                    if (rx != null)
                    {
                        if (!rx.IsMatch(c.Url) && !rx.IsMatch(c.Label) && !rx.IsMatch(c.FileName)) continue;
                    }
                    else if (rxTxt.Length > 0)
                    {
                        string low = rxTxt.ToLowerInvariant();
                        if (c.Url.ToLowerInvariant().IndexOf(low) < 0 && c.Label.ToLowerInvariant().IndexOf(low) < 0) continue;
                    }

                    ListViewItem item = new ListViewItem(new string[] { c.Category, c.FileName, c.Label, c.Url });
                    item.Tag = c;
                    item.Checked = c.IsDownloadableAsset && (activeMode != "All");
                    item.ForeColor = c.Category == "Papers" ? Color.FromArgb(125, 211, 252)
                        : (c.Category == "Audio" || c.Category == "Shaders" ? Color.FromArgb(52, 211, 153) : Color.FromArgb(226, 232, 240));
                    lv.Items.Add(item);
                }
                lv.EndUpdate();
                lblCount.Text = "Showing " + lv.Items.Count + " / " + allItems.Count + " page links (" + activeMode + " filter)";
            };

            string[][] modes = new string[][]
            {
                new string[] { "Downloadable Assets", "Assets" },
                new string[] { "Papers (.pdf)", "Papers" },
                new string[] { "Audio & Shaders", "Audio" },
                new string[] { "Models & Data", "DataModels" },
                new string[] { "Code & Archives", "CodeArch" },
                new string[] { "All Links (" + allItems.Count + ")", "All" }
            };
            for (int m = 0; m < modes.Length; m++)
            {
                string modeKey = modes[m][1];
                Button bf = AutoBtn(modes[m][0], m == 0 ? Color.FromArgb(2, 132, 199) : Color.FromArgb(30, 41, 59));
                bf.Click += delegate { activeMode = modeKey; rebuildFilter(); };
                filterRow.Controls.Add(bf);
            }

            Button bChkAll = AutoBtn("Check All", Color.FromArgb(15, 118, 110));
            bChkAll.Click += delegate { for (int i = 0; i < lv.Items.Count; i++) lv.Items[i].Checked = true; };
            Button bUnchk = AutoBtn("Uncheck All", Color.FromArgb(51, 65, 85));
            bUnchk.Click += delegate { for (int i = 0; i < lv.Items.Count; i++) lv.Items[i].Checked = false; };
            filterRow.Controls.Add(bChkAll);
            filterRow.Controls.Add(bUnchk);

            Label lRx = new Label(); lRx.Text = "Regex / Filter:"; lRx.SetBounds(10, 45, 98, 20); lRx.ForeColor = Color.FromArgb(167, 139, 250);
            txtRegex.TextChanged += delegate { rebuildFilter(); };

            top.Controls.AddRange(new Control[] { filterRow, lRx, txtRegex, lblCount });

            Panel bot = new Panel(); bot.Dock = DockStyle.Bottom; bot.Height = 48; bot.BackColor = Color.FromArgb(12, 18, 31); bot.Padding = new Padding(10, 8, 10, 8);
            CheckBox chkAutoCorpus = new CheckBox(); chkAutoCorpus.Text = "Auto-Distill Downloaded PDFs -> Sovereign +Corpus";
            chkAutoCorpus.Checked = true; chkAutoCorpus.AutoSize = true; chkAutoCorpus.ForeColor = Color.FromArgb(52, 211, 153); chkAutoCorpus.Location = new Point(12, 13);

            CheckBox chkAutoAv = new CheckBox(); chkAutoAv.Text = "Auto-Send first .wav / .omsh -> OmegaAV v0.7.1";
            chkAutoAv.Checked = false; chkAutoAv.AutoSize = true; chkAutoAv.ForeColor = Color.FromArgb(167, 139, 250); chkAutoAv.Location = new Point(365, 13);

            Button bQueue = AutoBtn("Queue Checked -> HTTP 206 Vault Studio", Color.FromArgb(2, 132, 199));
            bQueue.SetBounds(752, 9, 276, 30);
            bQueue.Click += delegate
            {
                int queued = 0;
                bool sentAvFlag = false;
                for (int i = 0; i < lv.Items.Count; i++)
                {
                    if (!lv.Items[i].Checked) continue;
                    SniffedLinkCandidate c = lv.Items[i].Tag as SniffedLinkCandidate;
                    if (c == null) continue;
                    bool sendThisAv = chkAutoAv.Checked && !sentAvFlag && (c.Category == "Audio" || c.Category == "Shaders");
                    if (sendThisAv) sentAvFlag = true;
                    EnqueueHttpRangeDownload(c.Url, c.FileName, chkAutoCorpus.Checked && c.Category == "Papers", sendThisAv);
                    queued++;
                }
                dlg.Close();
                if (queued > 0)
                {
                    _lblStatusL.Text = "Batch Queued " + queued + " item(s) -> <OMEGA_LAB_ROOT>\\downloads (HTTP 206)!";
                    OpenDownloadStudio();
                }
            };

            bot.Controls.AddRange(new Control[] { chkAutoCorpus, chkAutoAv, bQueue });
            dlg.Controls.Add(lv);
            dlg.Controls.Add(top);
            dlg.Controls.Add(bot);

            rebuildFilter();
            dlg.ShowDialog(this);
        }
    }
}
