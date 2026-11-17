using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaBrowserV03
{
    public static class OmegaInjectedJs
    {
        public static string BuildExtractionScript(string mode)
        {
            string m = (mode ?? "full").ToLowerInvariant() == "distilled" ? "distilled" : "full";
            return "(function(){" +
                "var mode='" + m + "'; var bt=String.fromCharCode(96); var fence=bt+bt+bt; var sep='-'+''+'-'+'-';" +
                "function texOf(el){ var a=el.querySelector?el.querySelector('annotation[encoding*=\"tex\"]'):null; if(a&&a.textContent) return a.textContent.trim(); if(el.getAttribute){ var d=el.getAttribute('data-latex')||el.getAttribute('alttext'); if(d) return d.trim(); } return ''; }" +
                "function codeLang(p){ var c=p.querySelector('code')||p; var cls=(c.className||'')+' '+(p.className||''); var m1=cls.match(/(?:language-|lang-|hljs\\s+)([a-zA-Z0-9_+#-]+)/); return m1?m1[1].toLowerCase():((c.getAttribute('data-lang')||'')).toLowerCase(); }" +
                "function tblMd(t){ var rs=t.querySelectorAll('tr'); if(!rs||!rs.length) return ''; var o=[]; for(var r=0;r<rs.length&&r<60;r++){ var cs=rs[r].querySelectorAll('th,td'); if(!cs.length) continue; var ln=[]; for(var c=0;c<cs.length;c++) ln.push((cs[c].innerText||'').replace(/\\s+/g,' ').replace(/\\|/g,'/').trim()); o.push('| '+ln.join(' | ')+' |'); if(r===0){ var s=[]; for(var k=0;k<cs.length;k++) s.push(sep); o.push('| '+s.join(' | ')+' |'); } } return o.join('\\n'); }" +
                "var root=document.querySelector('article,main,[role=\"main\"],.ltx_page_main,#content-inner,#main-content')||document.body;" +
                "var blocks=[]; var ns=root.querySelectorAll('h1,h2,h3,h4,p,pre,table,blockquote,li,mjx-container,span.katex-display,div.math,script[type*=\"math/tex\"]'); var seen=new Set();" +
                "for(var i=0;i<ns.length;i++){ var n=ns[i]; if(seen.has(n)) continue; if(n.closest&&n.closest('nav,footer,header,aside,.sidebar,.cookie,.ad,#omega-ux-layer')) continue; var tg=(n.tagName||'').toUpperCase();" +
                "if(tg==='PRE'){ var sb=n.querySelectorAll('*'); for(var j=0;j<sb.length;j++) seen.add(sb[j]); var tx=(n.innerText||'').replace(/\\r/g,'').trim(); if(tx) blocks.push({k:'code',t:fence+codeLang(n)+'\\n'+tx+'\\n'+fence}); continue; }" +
                "if(tg==='TABLE'){ var sb2=n.querySelectorAll('*'); for(var j2=0;j2<sb2.length;j2++) seen.add(sb2[j2]); var tm=tblMd(n); if(tm) blocks.push({k:'table',t:tm}); continue; }" +
                "if(tg==='SCRIPT'){ var mt=(n.textContent||'').trim(); if(mt) blocks.push({k:'math',t:'$$\\n'+mt+'\\n$$'}); continue; }" +
                "var txm=texOf(n); if(txm){ blocks.push({k:'math',t:'$$ '+txm+' $$'}); continue; } if(n.closest&&n.closest('pre,table')) continue;" +
                "var cl=n.cloneNode(true); var kl=cl.querySelectorAll?cl.querySelectorAll('.katex,mjx-container,math'):[]; for(var m2=0;m2<kl.length;m2++){ var kt=texOf(kl[m2]); if(kt) kl[m2].textContent=' $'+kt+'$ '; }" +
                "var ci=cl.querySelectorAll?cl.querySelectorAll('code'):[]; for(var c2=0;c2<ci.length;c2++) ci[c2].textContent=bt+(ci[c2].textContent||'')+bt;" +
                "var txt=(cl.innerText||cl.textContent||'').replace(/\\s+/g,' ').trim(); if(!txt) continue;" +
                "if(tg==='H1') blocks.push({k:'h',t:'# '+txt}); else if(tg==='H2') blocks.push({k:'h',t:'## '+txt}); else if(tg==='H3'||tg==='H4') blocks.push({k:'h',t:'### '+txt}); else if(tg==='LI') blocks.push({k:'p',t:'- '+txt}); else if(tg==='BLOCKQUOTE') blocks.push({k:'p',t:'> '+txt}); else blocks.push({k:'p',t:txt}); }" +
                "var res=[]; var thm=/^(Theorem|Lemma|Definition|Proposition|Corollary|Proof|Axiom|Algorithm|Invariant|Remark)\\b/i;" +
                "for(var b=0;b<blocks.length;b++){ var it=blocks[b]; if(mode==='distilled'){ if(it.k==='h'||it.k==='code'||it.k==='table'||it.k==='math'||thm.test(it.t)||it.t.indexOf('$')>=0) res.push(it.t); } else res.push(it.t); }" +
                "if(!res.length&&document.body) res.push((document.body.innerText||'').trim()); return res.join('\\n\\n'); })();";
        }

        public static string BuildDocumentGuardScript()
        {
            return "(function(){ if(window.__omegaGuard) return; window.__omegaGuard=true; window.__omegaVim=false;" +
                "function post(m){ try{ if(window.chrome&&window.chrome.webview) window.chrome.webview.postMessage(m); }catch(e){} }" +
                "try{ if(window.Notification){ window.Notification.requestPermission=function(){ post('SHIELD_COOKIE'); return Promise.resolve('denied'); }; } }catch(e){}" +
                "var userClicked=false; document.addEventListener('pointerdown',function(){ userClicked=true; post('PANE_ACTIVE'); },true);" +
                "function sweepCookieAndAutoplay(){" +
                "  var sels=['#onetrust-reject-all-handler','#CybotCookiebotDialogBodyButtonDecline','.qc-cmp2-summary-buttons button[mode=\"secondary\"]','button[data-testid=\"cookie-policy-manage-dialog-btn-reject-all\"]','.didomi-continue-without-agreeing'];" +
                "  for(var i=0;i<sels.length;i++){ var b=document.querySelector(sels[i]); if(b&&b.offsetParent!==null){ b.click(); post('SHIELD_COOKIE'); return; } }" +
                "  var btns=document.querySelectorAll('[id*=\"cookie\" i] button, [class*=\"cookie\" i] button, [id*=\"consent\" i] button');" +
                "  for(var j=0;j<btns.length&&j<25;j++){ var t=(btns[j].innerText||'').trim().toLowerCase(); if(t==='reject all'||t==='decline'||t==='necessary only'||t==='only essentials'){ btns[j].click(); post('SHIELD_COOKIE'); break; } }" +
                "  if(!userClicked){ var vids=document.querySelectorAll('video'); for(var v=0;v<vids.length;v++){ if(!vids[v].paused&&!vids[v].dataset.omegaBlocked){ vids[v].pause(); vids[v].autoplay=false; vids[v].dataset.omegaBlocked='1'; post('SHIELD_AUTOPLAY'); } } }" +
                "}" +
                "setInterval(sweepCookieAndAutoplay, 1400);" +
                "var hoverTimer=null, pill=null;" +
                "function ensurePill(){ if(pill) return pill; pill=document.createElement('div'); pill.id='omega-ux-layer'; pill.style.cssText='position:fixed;left:10px;bottom:10px;z-index:2147483647;background:#0b1324;color:#e2e8f0;border:1px solid #38bdf8;border-radius:7px;padding:6px 10px;font:12px Consolas,monospace;max-width:620px;pointer-events:none;display:none;box-shadow:0 4px 14px rgba(0,0,0,.6)'; document.documentElement.appendChild(pill); return pill; }" +
                "window.__omegaHeadReply=function(u,sz){ if(pill&&pill.dataset.u===u&&sz){ pill.textContent=pill.textContent+' • '+sz; } };" +
                "document.addEventListener('mouseover',function(e){ var a=e.target&&e.target.closest?e.target.closest('a[href]'):null; if(!a){ if(pill) pill.style.display='none'; return; } clearTimeout(hoverTimer); hoverTimer=setTimeout(function(){ var h=a.href||''; if(!h||h.indexOf('javascript:')===0) return; var ext='WEB'; if(/\\.pdf($|\\?)/i.test(h)) ext='PDF'; else if(/\\.(zip|tar|gz|7z|msi|exe)($|\\?)/i.test(h)) ext='ARCHIVE'; var p=ensurePill(); p.dataset.u=h; p.textContent='['+ext+'] '+h.slice(0,95); p.style.display='block'; if(ext!=='WEB') post('PROBE_HEAD|'+h); },180); },true);" +
                "var draftTimer=null; document.addEventListener('input',function(e){ var el=e.target; if(!el||!el.tagName) return; var tg=el.tagName.toUpperCase(); if(tg!=='TEXTAREA'&&tg!=='INPUT') return; if(tg==='INPUT'&&(el.type==='password'||el.type==='hidden')) return; clearTimeout(draftTimer); draftTimer=setTimeout(function(){ var k=el.id||el.name||tg; if((el.value||'').length>2) post('FORM_DRAFT|'+k+'|'+(el.value||'').slice(0,4000)); },700); },true);" +
                "var lastG=0, hints=[], hintBuf='', hintNew=false;" +
                "function clearHints(){ for(var i=0;i<hints.length;i++) if(hints[i].el.parentNode) hints[i].el.parentNode.removeChild(hints[i].el); hints=[]; hintBuf=''; }" +
                "function showHints(newTab){ clearHints(); hintNew=newTab; var as=document.querySelectorAll('a[href],button'); var alpha='ABCDEFGHJKLMNPQRSTUVWXYZ'; var idx=0; for(var i=0;i<as.length&&idx<120;i++){ var r=as[i].getBoundingClientRect(); if(r.width<6||r.height<6||r.top<0||r.left<0||r.top>window.innerHeight||r.left>window.innerWidth) continue; var code=alpha[Math.floor(idx/24)%24]+alpha[idx%24]; idx++; var b=document.createElement('span'); b.textContent=code; b.style.cssText='position:fixed;left:'+Math.round(r.left)+'px;top:'+Math.round(r.top)+'px;z-index:2147483647;background:#facc15;color:#000;font:bold 11px Consolas,monospace;padding:1px 4px;border-radius:3px;border:1px solid #000'; document.documentElement.appendChild(b); hints.push({code:code,el:b,target:as[i]}); } }" +
                "document.addEventListener('keydown',function(e){" +
                "  if(e.ctrlKey&&(e.key==='k'||e.key==='K')){ e.preventDefault(); post('CMD_PALETTE'); return; }" +
                "  if(e.ctrlKey&&(e.key==='f'||e.key==='F')){ e.preventDefault(); post('OPEN_FIND'); return; }" +
                "  if(e.ctrlKey&&e.shiftKey&&(e.key==='a'||e.key==='A')){ e.preventDefault(); post('TAB_SEARCH'); return; }" +
                "  if(e.ctrlKey&&e.shiftKey&&(e.key==='t'||e.key==='T')){ e.preventDefault(); post('REOPEN_TAB'); return; }" +
                "  if(e.ctrlKey&&(e.key==='h'||e.key==='H')){ e.preventDefault(); post('VAULT_SEARCH'); return; }" +
                "  if(e.ctrlKey&&!e.shiftKey&&(e.key==='j'||e.key==='J')){ e.preventDefault(); post('OPEN_DOWNLOADS'); return; }" +
                "  if(e.ctrlKey&&e.shiftKey&&(e.key==='d'||e.key==='D')){ e.preventDefault(); post('OPEN_SNIFFER'); return; }" +
                "  if(!window.__omegaVim) return;" +
                "  var ae=document.activeElement; if(ae&&(ae.tagName==='INPUT'||ae.tagName==='TEXTAREA'||ae.isContentEditable)) return;" +
                "  if(hints.length>0){ if(e.key==='Escape'){ clearHints(); e.preventDefault(); return; } var ch=(e.key||'').toUpperCase(); if(ch.length===1&&ch>='A'&&ch<='Z'){ e.preventDefault(); hintBuf+=ch; for(var i=0;i<hints.length;i++){ if(hints[i].code===hintBuf){ var tgt=hints[i].target; clearHints(); if(hintNew&&tgt.href) post('OPEN_BG|'+tgt.href); else tgt.click(); return; } } if(hintBuf.length>=2) clearHints(); } return; }" +
                "  if(e.ctrlKey||e.altKey||e.metaKey) return;" +
                "  if(e.key==='j'){ window.scrollBy(0,95); e.preventDefault(); } else if(e.key==='k'){ window.scrollBy(0,-95); e.preventDefault(); }" +
                "  else if(e.key==='d'){ window.scrollBy(0,window.innerHeight*0.5); e.preventDefault(); } else if(e.key==='u'){ window.scrollBy(0,-window.innerHeight*0.5); e.preventDefault(); }" +
                "  else if(e.key==='G'){ window.scrollTo(0,document.body.scrollHeight); e.preventDefault(); }" +
                "  else if(e.key==='g'){ var now=Date.now(); if(now-lastG<420){ window.scrollTo(0,0); lastG=0; } else lastG=now; e.preventDefault(); }" +
                "  else if(e.key==='f'){ showHints(false); e.preventDefault(); } else if(e.key==='F'){ showHints(true); e.preventDefault(); }" +
                "},true); })();";
        }

        public static string BuildFindInPageScript(string query, bool matchCase, int step)
        {
            string qEsc = OmegaJson.Esc(query ?? "");
            return "(function(){" +
                "var q=\"" + qEsc + "\"; var mc=" + (matchCase ? "true" : "false") + "; var step=" + step + ";" +
                "if(!window.__omegaFindIdx) window.__omegaFindIdx=0;" +
                "function clearMarks(){ var ms=document.querySelectorAll('mark.omega-find-m'); for(var i=0;i<ms.length;i++){ var p=ms[i].parentNode; if(p){ p.replaceChild(document.createTextNode(ms[i].textContent),ms[i]); p.normalize(); } } }" +
                "if(!q){ clearMarks(); return '0/0'; }" +
                "if(step===0 || window.__omegaLastQ!==q+'|'+mc){ clearMarks(); window.__omegaLastQ=q+'|'+mc; window.__omegaFindIdx=0;" +
                "  var w=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT,null,false); var tnodes=[]; var n;" +
                "  while((n=w.nextNode())){ var p=n.parentElement; if(!p) continue; var tg=p.tagName; if(tg==='SCRIPT'||tg==='STYLE'||tg==='NOSCRIPT'||tg==='MARK'||p.id==='omega-ux-layer') continue; if(n.nodeValue&&n.nodeValue.trim().length>0) tnodes.push(n); }" +
                "  var qCmp=mc?q:q.toLowerCase(); var hits=0;" +
                "  for(var i=0;i<tnodes.length&&hits<800;i++){ var txt=tnodes[i].nodeValue; var hay=mc?txt:txt.toLowerCase(); var pos=hay.indexOf(qCmp); if(pos<0) continue;" +
                "    var frag=document.createDocumentFragment(); var last=0;" +
                "    while(pos>=0&&hits<800){ if(pos>last) frag.appendChild(document.createTextNode(txt.slice(last,pos))); var mk=document.createElement('mark'); mk.className='omega-find-m'; mk.style.cssText='background:#fde047;color:#000;padding:0 1px;border-radius:2px'; mk.textContent=txt.slice(pos,pos+q.length); frag.appendChild(mk); hits++; last=pos+q.length; pos=hay.indexOf(qCmp,last); }" +
                "    if(last<txt.length) frag.appendChild(document.createTextNode(txt.slice(last))); tnodes[i].parentNode.replaceChild(frag,tnodes[i]); } }" +
                "var all=document.querySelectorAll('mark.omega-find-m'); if(!all.length) return '0/0';" +
                "if(step!==0) window.__omegaFindIdx=(window.__omegaFindIdx+step+all.length)%all.length;" +
                "for(var j=0;j<all.length;j++) all[j].style.background=(j===window.__omegaFindIdx)?'#38bdf8':'#fde047';" +
                "all[window.__omegaFindIdx].scrollIntoView({behavior:'smooth',block:'center'});" +
                "return (window.__omegaFindIdx+1)+'/'+all.length; })();";
        }
    }

    public static class OmegaVault
    {
        private static readonly object IoLock = new object();

        public static string ComputeSha256Hex(string text)
        {
            return ComputeSha256Bytes(Encoding.UTF8.GetBytes(text ?? ""));
        }

        public static string Sha256Hex(string text)
        {
            return ComputeSha256Hex(text);
        }

        public static string ComputeSha256File(string path)
        {
            if (!File.Exists(path)) return "";
            using (FileStream fs = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < h.Length; i++) sb.AppendFormat("{0:x2}", h[i]);
                return sb.ToString();
            }
        }

        public static string ComputeSha256Bytes(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data ?? new byte[0]);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < h.Length; i++) sb.AppendFormat("{0:x2}", h[i]);
                return sb.ToString();
            }
        }

        public static ReceiptRecord MintReceiptAndSaveHtml(string labRoot, string title, string finalUrl, string rawHtml, string mdBody, int siteBlocked, long totalBlocked)
        {
            string snapDir = Path.Combine(labRoot, "vault", "snapshots");
            string logDir = Path.Combine(labRoot, "logs");
            Directory.CreateDirectory(snapDir); Directory.CreateDirectory(logDir);

            string cleanMd = (mdBody ?? "").Trim();
            string sha = ComputeSha256Hex(cleanMd);
            DateTime now = DateTime.UtcNow;
            string tsIso = now.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string snapPath = Path.Combine(snapDir, now.ToString("yyyyMMdd_HHmmss") + "_" + sha.Substring(0, 12) + ".html");

            lock (IoLock)
            {
                File.WriteAllText(snapPath, rawHtml ?? "", new UTF8Encoding(false));
                string ledger = "{\"utc\":\"" + OmegaJson.Esc(tsIso) + "\",\"sha256\":\"" + sha +
                    "\",\"title\":\"" + OmegaJson.Esc(title) + "\",\"final_url\":\"" + OmegaJson.Esc(finalUrl) +
                    "\",\"site_blocked\":" + siteBlocked + ",\"total_blocked\":" + totalBlocked +
                    ",\"vault_path\":\"" + OmegaJson.Esc(snapPath) + "\"}";
                File.AppendAllText(Path.Combine(logDir, "receipts_ledger.jsonl"), ledger + Environment.NewLine, new UTF8Encoding(false));
            }

            ReceiptRecord r = new ReceiptRecord();
            r.Title = title ?? "Untitled"; r.FinalUrl = finalUrl ?? ""; r.FetchedUtc = tsIso;
            r.Sha256 = sha; r.SiteBlocked = siteBlocked; r.TotalBlocked = totalBlocked;
            r.VaultPath = snapPath; r.MarkdownBody = cleanMd;
            return r;
        }

        public static string FormatReceiptMarkdown(ReceiptRecord r)
        {
            char bt = (char)96;
            StringBuilder sb = new StringBuilder(r.MarkdownBody.Length + 512);
            sb.AppendLine("# " + r.Title); sb.AppendLine();
            sb.AppendLine("> **Sovereign Receipt (OmegaBrowser v0.6)**");
            sb.AppendLine("> - **Final URL:** " + r.FinalUrl);
            sb.AppendLine("> - **Fetched UTC:** " + r.FetchedUtc);
            sb.AppendLine("> - **Body SHA-256:** " + bt + r.Sha256 + bt);
            sb.AppendLine("> - **Shield Blocked:** " + r.SiteBlocked + " site / " + r.TotalBlocked + " session");
            sb.AppendLine("> - **Vault HTML Snapshot:** " + bt + r.VaultPath + bt);
            sb.AppendLine(); sb.AppendLine(r.MarkdownBody);
            return sb.ToString();
        }

        public static CorpusSaveResult SaveToCorpus(string labRoot, ReceiptRecord r, string extraTags)
        {
            CorpusSaveResult res = new CorpusSaveResult(); res.Sha256 = r.Sha256;
            DetectCorpusMetadata(r.FinalUrl, out res.Source, out res.CourseOrPaperId, out res.License);

            string localCorpus = Path.Combine(labRoot, "corpus"); Directory.CreateDirectory(localCorpus);
            string idxFile = Path.Combine(localCorpus, "corpus_sha256_index.txt");
            string manifoldRoot = (Environment.GetEnvironmentVariable("OMEGA_EXTERNAL_PROJECT_DIR") ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
            string manifoldCorpus = Path.Combine(manifoldRoot, "corpus");
            bool hasManifold = Directory.Exists(manifoldRoot);
            if (hasManifold) Directory.CreateDirectory(manifoldCorpus);

            lock (IoLock)
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(idxFile)) foreach (string s in File.ReadAllLines(idxFile)) if (!string.IsNullOrWhiteSpace(s)) seen.Add(s.Trim());
                if (hasManifold)
                {
                    string mIdx = Path.Combine(manifoldCorpus, "corpus_sha256_index.txt");
                    if (File.Exists(mIdx)) foreach (string s in File.ReadAllLines(mIdx)) if (!string.IsNullOrWhiteSpace(s)) seen.Add(s.Trim());
                }
                if (seen.Contains(r.Sha256)) { res.Duplicate = true; return res; }

                string dateTag = DateTime.UtcNow.ToString("yyyy-MM-dd");
                string fname = DateTime.UtcNow.ToString("yyyyMMdd") + "_" + res.Source + "_" + SanitizeSlug(res.CourseOrPaperId) + "_" + r.Sha256.Substring(0, 12) + ".md";
                string tags = string.IsNullOrWhiteSpace(extraTags) ? res.Source + ",sovereign_v03" : extraTags.Trim();

                StringBuilder doc = new StringBuilder(r.MarkdownBody.Length + 512);
                doc.AppendLine("<!-- SOVEREIGN_CORPUS_ENTRY");
                doc.AppendLine("source: " + res.Source + "\r\ncourse_or_paper_id: " + res.CourseOrPaperId + "\r\ndate: " + dateTag);
                doc.AppendLine("license: " + res.License + "\r\ntags: " + tags + "\r\nsha256: " + r.Sha256);
                doc.AppendLine("final_url: " + r.FinalUrl + "\r\nvault_snapshot: " + r.VaultPath + "\r\n-->\r\n");
                doc.AppendLine("# " + r.Title + "\r\n\r\n" + r.MarkdownBody);

                string jline = "{\"sha256\":\"" + r.Sha256 + "\",\"source\":\"" + OmegaJson.Esc(res.Source) + "\",\"title\":\"" + OmegaJson.Esc(r.Title) + "\",\"url\":\"" + OmegaJson.Esc(r.FinalUrl) + "\",\"file\":\"" + OmegaJson.Esc(fname) + "\"}";
                res.PrimaryPath = Path.Combine(localCorpus, fname);
                File.WriteAllText(res.PrimaryPath, doc.ToString(), new UTF8Encoding(false));
                File.AppendAllText(idxFile, r.Sha256 + Environment.NewLine, new UTF8Encoding(false));
                File.AppendAllText(Path.Combine(localCorpus, "corpus_manifest.jsonl"), jline + Environment.NewLine, new UTF8Encoding(false));
                if (hasManifold)
                {
                    res.ManifoldPath = Path.Combine(manifoldCorpus, fname);
                    File.WriteAllText(res.ManifoldPath, doc.ToString(), new UTF8Encoding(false));
                    File.AppendAllText(Path.Combine(manifoldCorpus, "corpus_sha256_index.txt"), r.Sha256 + Environment.NewLine, new UTF8Encoding(false));
                    File.AppendAllText(Path.Combine(manifoldCorpus, "corpus_manifest.jsonl"), jline + Environment.NewLine, new UTF8Encoding(false));
                }
                res.Saved = true; return res;
            }
        }

        public static string SaveTimeMachineSnapshot(string labRoot, ReceiptRecord r)
        {
            string urlKey = ComputeSha256Hex(OmegaUrlCleaner.CleanUrl(r.FinalUrl)).Substring(0, 14);
            string dir = Path.Combine(labRoot, "vault", "timemachine", urlKey);
            Directory.CreateDirectory(dir);
            string f = Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + r.Sha256.Substring(0, 8) + ".md");
            File.WriteAllText(f, FormatReceiptMarkdown(r), new UTF8Encoding(false));
            return f;
        }

        public static List<string> ListTimeMachineSnapshots(string labRoot, string url)
        {
            string urlKey = ComputeSha256Hex(OmegaUrlCleaner.CleanUrl(url)).Substring(0, 14);
            string dir = Path.Combine(labRoot, "vault", "timemachine", urlKey);
            List<string> res = new List<string>();
            if (Directory.Exists(dir)) res.AddRange(Directory.GetFiles(dir, "*.md"));
            res.Sort(); res.Reverse();
            return res;
        }

        public static string DiffTexts(string oldText, string newText)
        {
            string[] a = (oldText ?? "").Replace("\r", "").Split('\n');
            string[] b = (newText ?? "").Replace("\r", "").Split('\n');
            HashSet<string> setA = new HashSet<string>(a);
            HashSet<string> setB = new HashSet<string>(b);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== TIME MACHINE PROVENANCE DIFF (OLD SNAPSHOT vs LIVE PAGE) ===");
            int changes = 0;
            for (int i = 0; i < a.Length && changes < 180; i++)
                if (a[i].Trim().Length > 0 && !setB.Contains(a[i])) { sb.AppendLine("- [REMOVED] " + a[i]); changes++; }
            for (int j = 0; j < b.Length && changes < 360; j++)
                if (b[j].Trim().Length > 0 && !setA.Contains(b[j])) { sb.AppendLine("+ [ADDED]   " + b[j]); changes++; }
            if (changes == 0) sb.AppendLine("No content differences detected (100% identical to archived snapshot).");
            return sb.ToString();
        }

        public static string GetStickyNote(string labRoot, string url)
        {
            string p = Path.Combine(labRoot, "vault", "sticky_notes.tsv");
            string key = OmegaUrlCleaner.CleanUrl(url);
            if (!File.Exists(p) || string.IsNullOrEmpty(key)) return "";
            foreach (string ln in File.ReadAllLines(p))
            {
                int tab = ln.IndexOf('\t');
                if (tab > 0 && string.Equals(ln.Substring(0, tab), key, StringComparison.OrdinalIgnoreCase))
                    return ln.Substring(tab + 1).Replace("\\n", "\r\n");
            }
            return "";
        }

        public static void SaveStickyNote(string labRoot, string url, string note)
        {
            string p = Path.Combine(labRoot, "vault", "sticky_notes.tsv");
            string key = OmegaUrlCleaner.CleanUrl(url);
            if (string.IsNullOrEmpty(key)) return;
            List<string> outLines = new List<string>();
            if (File.Exists(p))
            {
                foreach (string ln in File.ReadAllLines(p))
                {
                    int tab = ln.IndexOf('\t');
                    if (tab > 0 && string.Equals(ln.Substring(0, tab), key, StringComparison.OrdinalIgnoreCase)) continue;
                    outLines.Add(ln);
                }
            }
            if (!string.IsNullOrWhiteSpace(note))
                outLines.Add(key + "\t" + note.Trim().Replace("\r", "").Replace("\n", "\\n"));
            File.WriteAllLines(p, outLines.ToArray(), new UTF8Encoding(false));
        }

        public static void AppendHighlightToVault(string labRoot, string title, string url, string quote, string comment)
        {
            string p = Path.Combine(labRoot, "vault", "highlights_vault.md");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## " + (string.IsNullOrWhiteSpace(title) ? url : title));
            sb.AppendLine("- **Source:** " + OmegaUrlCleaner.CleanUrl(url) + " (" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC)");
            sb.AppendLine("> " + (quote ?? "").Trim().Replace("\n", "\n> "));
            if (!string.IsNullOrWhiteSpace(comment)) sb.AppendLine("- **Lab Note:** " + comment.Trim());
            sb.AppendLine();
            lock (IoLock) { File.AppendAllText(p, sb.ToString(), new UTF8Encoding(false)); }
        }

        public static void LogHistoryVisit(string labRoot, string title, string url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.StartsWith("omega://")) return;
            string p = Path.Combine(labRoot, "logs", "history_v03.tsv");
            string ln = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + "\t" + (title ?? "").Replace("\t", " ") + "\t" + OmegaUrlCleaner.CleanUrl(url);
            lock (IoLock) { try { File.AppendAllText(p, ln + Environment.NewLine, new UTF8Encoding(false)); } catch { } }
        }

        public static List<SearchHit> SearchHistoryAndVault(string labRoot, string query)
        {
            List<SearchHit> hits = new List<SearchHit>();
            string q = (query ?? "").Trim().ToLowerInvariant();
            string histPath = Path.Combine(labRoot, "logs", "history_v03.tsv");
            if (File.Exists(histPath))
            {
                string[] lines = File.ReadAllLines(histPath);
                for (int i = lines.Length - 1; i >= 0 && hits.Count < 45; i--)
                {
                    string[] p = lines[i].Split('\t');
                    if (p.Length < 3) continue;
                    if (q.Length == 0 || lines[i].ToLowerInvariant().Contains(q))
                    {
                        SearchHit h = new SearchHit(); h.Kind = "History"; h.Title = p[1]; h.UrlOrPath = p[2]; h.Snippet = p[0] + " • " + p[2];
                        hits.Add(h);
                    }
                }
            }
            if (q.Length >= 2)
            {
                string[] dirs = new string[] { Path.Combine(labRoot, "corpus"), Path.Combine(labRoot, "vault") };
                for (int d = 0; d < dirs.Length; d++)
                {
                    if (!Directory.Exists(dirs[d])) continue;
                    string[] mdFiles = Directory.GetFiles(dirs[d], "*.md", SearchOption.AllDirectories);
                    for (int f = 0; f < mdFiles.Length && hits.Count < 80; f++)
                    {
                        string txt = File.ReadAllText(mdFiles[f]);
                        int idx = txt.ToLowerInvariant().IndexOf(q);
                        if (idx >= 0)
                        {
                            int s0 = Math.Max(0, idx - 36);
                            int len = Math.Min(96, txt.Length - s0);
                            SearchHit h = new SearchHit(); h.Kind = "Vault MD";
                            h.Title = Path.GetFileName(mdFiles[f]);
                            h.UrlOrPath = mdFiles[f];
                            h.Snippet = txt.Substring(s0, len).Replace("\r", " ").Replace("\n", " ");
                            hits.Add(h);
                        }
                    }
                }
            }
            return hits;
        }

        private static void DetectCorpusMetadata(string url, out string source, out string id, out string license)
        {
            source = "web"; id = "doc"; license = "fair-research-snapshot";
            Uri u; if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out u)) return;
            string h = (u.Host ?? "").ToLowerInvariant(); string p = u.AbsolutePath.Trim('/');
            if (h.Contains("ocw.mit.edu")) { source = "mit_ocw"; license = "CC-BY-NC-SA-4.0"; string[] s = p.Split('/'); id = s.Length >= 2 ? s[1] : "mit_ocw"; }
            else if (h.Contains("arxiv.org")) { source = "arxiv"; license = "arXiv-non-exclusive"; id = p.Replace("abs/", "").Replace("html/", "").Replace("pdf/", "").Replace('/', '_'); }
            else if (h.Contains("github.com")) { source = "github"; license = "repo-oss"; string[] s = p.Split('/'); id = s.Length >= 2 ? s[0] + "_" + s[1] : p; }
            else { source = h.Replace("www.", "").Replace('.', '_'); id = string.IsNullOrEmpty(p) ? "index" : p.Replace('/', '_'); }
        }

        private static string SanitizeSlug(string s)
        {
            if (string.IsNullOrEmpty(s)) return "item";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length && sb.Length < 28; i++)
                if (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_') sb.Append(s[i]);
            return sb.Length == 0 ? "item" : sb.ToString();
        }
    }
}
