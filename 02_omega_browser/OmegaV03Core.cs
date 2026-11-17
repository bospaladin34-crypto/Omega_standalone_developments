using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OmegaBrowserV03
{
    public sealed class ApiResult
    {
        public int Status = 200;
        public string ContentType = "application/json; charset=utf-8";
        public byte[] Body = new byte[0];

        public static ApiResult Json(int code, string json)
        {
            ApiResult r = new ApiResult(); r.Status = code; r.Body = Encoding.UTF8.GetBytes(json ?? "{}"); return r;
        }

        public static ApiResult Png(byte[] bytes)
        {
            ApiResult r = new ApiResult(); r.Status = 200; r.ContentType = "image/png"; r.Body = bytes ?? new byte[0]; return r;
        }
    }

    public interface IAgentBrowserHost
    {
        System.Threading.Tasks.Task<ApiResult> HandleAgentRequestAsync(string method, string path, Dictionary<string, string> query, string body);
    }

    public sealed class ClosedTabRecord
    {
        public string Workspace = "Research";
        public string Container = "Default";
        public string Title = "";
        public List<string> History = new List<string>();
    }

    public sealed class DomainPref
    {
        public string Host = "";
        public double Zoom = 1.0;
        public bool NightDim = false;
    }

    public sealed class ReceiptRecord
    {
        public string Title = "";
        public string FinalUrl = "";
        public string FetchedUtc = "";
        public string Sha256 = "";
        public int SiteBlocked = 0;
        public long TotalBlocked = 0;
        public string VaultPath = "";
        public string MarkdownBody = "";
    }

    public sealed class CorpusSaveResult
    {
        public bool Saved = false;
        public bool Duplicate = false;
        public string Sha256 = "";
        public string Source = "";
        public string CourseOrPaperId = "";
        public string License = "";
        public string PrimaryPath = "";
        public string ManifoldPath = "";
    }

    public sealed class SiteShieldStat
    {
        public string Host = "";
        public int Blocked = 0;
        public int FirstParty = 0;
        public int ThirdParty = 0;
    }

    public sealed class PaletteItem
    {
        public string Title = "";
        public string Hint = "";
        public Action Run;
        public override string ToString() { return Title + "   [" + Hint + "]"; }
    }

    public sealed class SearchHit
    {
        public string Kind = "";
        public string Title = "";
        public string UrlOrPath = "";
        public string Snippet = "";
        public override string ToString() { return "[" + Kind + "] " + Title + " — " + Snippet; }
    }

    public static class OmegaJson
    {
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string UnwrapJsString(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw == "null") return "";
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
            {
                StringBuilder sb = new StringBuilder(raw.Length);
                for (int i = 1; i < raw.Length - 1; i++)
                {
                    char c = raw[i];
                    if (c == '\\' && i + 1 < raw.Length - 1)
                    {
                        char n = raw[++i];
                        if (n == 'n') sb.Append('\n');
                        else if (n == 'r') sb.Append('\r');
                        else if (n == 't') sb.Append('\t');
                        else if (n == '"') sb.Append('"');
                        else if (n == '\\') sb.Append('\\');
                        else if (n == 'u' && i + 4 < raw.Length - 1)
                        {
                            int code;
                            if (int.TryParse(raw.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code))
                            {
                                sb.Append((char)code); i += 4;
                            }
                        }
                        else sb.Append(n);
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }
            return raw;
        }

        public static string ExtractJsonField(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";
            string needle = "\"" + key + "\"";
            int idx = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            int colon = json.IndexOf(':', idx + needle.Length);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = q1 + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[++i];
                    if (n == 'n') sb.Append('\n'); else if (n == 'r') sb.Append('\r'); else if (n == 't') sb.Append('\t');
                    else if (n == '"') sb.Append('"'); else if (n == '\\') sb.Append('\\');
                    else if (n == 'u' && i + 4 < json.Length)
                    {
                        int code;
                        if (int.TryParse(json.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code)) { sb.Append((char)code); i += 4; }
                    }
                    else sb.Append(n);
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    public static class OmegaUrlCleaner
    {
        private static readonly HashSet<string> JunkParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id", "utm_name",
            "fbclid", "gclid", "dclid", "msclkid", "twclid", "igshid", "mc_cid", "mc_eid",
            "_hsenc", "_hsmi", "mkt_tok", "spm", "si", "ref_src", "ref_url", "vero_id", "oly_enc_id"
        };

        public static string CleanUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            Uri u;
            if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out u)) return raw.Trim();
            if (string.IsNullOrEmpty(u.Query) || u.Query == "?") return u.GetLeftPart(UriPartial.Path) + u.Fragment;

            string[] pairs = u.Query.TrimStart('?').Split('&');
            List<string> kept = new List<string>();
            for (int i = 0; i < pairs.Length; i++)
            {
                if (string.IsNullOrEmpty(pairs[i])) continue;
                int eq = pairs[i].IndexOf('=');
                string k = eq > 0 ? pairs[i].Substring(0, eq) : pairs[i];
                if (k.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || JunkParams.Contains(k)) continue;
                kept.Add(pairs[i]);
            }
            string basePart = u.GetLeftPart(UriPartial.Path);
            return kept.Count == 0 ? (basePart + u.Fragment) : (basePart + "?" + string.Join("&", kept.ToArray()) + u.Fragment);
        }

        public static string ResolveOmniboxOrBang(string raw, string ws)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return "https://omega.home/index.html?ws=" + Uri.EscapeDataString(ws ?? "Research");
            if (s.Equals("omega://home", StringComparison.OrdinalIgnoreCase))
                return "https://omega.home/index.html?ws=" + Uri.EscapeDataString(ws ?? "Research");
            if (s.Equals("omega://performance", StringComparison.OrdinalIgnoreCase))
                return "https://omega.home/performance.html";

            if (s.StartsWith("!"))
            {
                int sp = s.IndexOf(' ');
                string bang = (sp > 0 ? s.Substring(0, sp) : s).ToLowerInvariant();
                string q = sp > 0 ? Uri.EscapeDataString(s.Substring(sp + 1).Trim()) : "";
                if (bang == "!mit") return q.Length > 0 ? "https://ocw.mit.edu/search/?q=" + q : "https://ocw.mit.edu/";
                if (bang == "!arxiv") return q.Length > 0 ? "https://arxiv.org/search/?query=" + q + "&searchtype=all" : "https://arxiv.org/";
                if (bang == "!gh") return q.Length > 0 ? "https://github.com/search?q=" + q : "https://github.com/";
                if (bang == "!rs") return q.Length > 0 ? "https://doc.rust-lang.org/std/?search=" + q : "https://doc.rust-lang.org/std/";
                if (bang == "!mdn") return q.Length > 0 ? "https://developer.mozilla.org/en-US/search?q=" + q : "https://developer.mozilla.org/";
                if (bang == "!scholar") return q.Length > 0 ? "https://scholar.google.com/scholar?q=" + q : "https://scholar.google.com/";
                if (bang == "!wiki") return q.Length > 0 ? "https://en.wikipedia.org/w/index.php?search=" + q : "https://en.wikipedia.org/";
            }

            if (s.Contains("://")) return s;
            if (!s.Contains(" ") && (s.Contains(".") || s.StartsWith("localhost") || s.StartsWith("127."))) return "https://" + s;
            return "https://duckduckgo.com/?q=" + Uri.EscapeDataString(s);
        }
    }

    public static class QrCodeEncoder
    {
        private static readonly int[] Exp = new int[512];
        private static readonly int[] Log = new int[256];

        static QrCodeEncoder()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = x; Log[x] = i; x <<= 1;
                if ((x & 0x100) != 0) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }

        private static int GfMul(int a, int b) { return (a == 0 || b == 0) ? 0 : Exp[Log[a] + Log[b]]; }

        private static int[] RsEncode(List<int> data, int ecLen)
        {
            int[] g = new int[] { 1 };
            for (int i = 0; i < ecLen; i++)
            {
                int[] ng = new int[g.Length + 1];
                int root = Exp[i];
                for (int j = 0; j < g.Length; j++) { ng[j] ^= g[j]; ng[j + 1] ^= GfMul(g[j], root); }
                g = ng;
            }
            int[] rem = new int[ecLen];
            for (int i = 0; i < data.Count; i++)
            {
                int f = data[i] ^ rem[0];
                for (int k = 0; k < ecLen - 1; k++) rem[k] = rem[k + 1];
                rem[ecLen - 1] = 0;
                for (int j = 0; j < ecLen; j++) rem[j] ^= GfMul(g[j + 1], f);
            }
            return rem;
        }

        public static Bitmap RenderBitmap(string text, int scale)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text ?? "");
            if (raw.Length > 134)
            {
                byte[] trunc = new byte[134]; Array.Copy(raw, trunc, 134); raw = trunc;
            }
            int[,] specs = new int[,] {
                {1,21,19,7,1,0}, {2,25,34,10,1,18}, {3,29,55,15,1,22},
                {4,33,80,20,1,26}, {5,37,108,26,1,30}, {6,41,136,18,2,34}
            };
            int idx = 5;
            for (int i = 0; i < 6; i++) { if (raw.Length + 2 <= specs[i, 2]) { idx = i; break; } }
            int sz = specs[idx, 1], dataCap = specs[idx, 2], ecLen = specs[idx, 3], blocks = specs[idx, 4], align = specs[idx, 5];

            List<int> bits = new List<int>();
            PushBits(bits, 4, 4); PushBits(bits, raw.Length, 8);
            for (int i = 0; i < raw.Length; i++) PushBits(bits, raw[i], 8);
            int term = Math.Min(4, dataCap * 8 - bits.Count);
            for (int i = 0; i < term; i++) bits.Add(0);
            while (bits.Count % 8 != 0) bits.Add(0);

            List<int> cws = new List<int>();
            for (int i = 0; i < bits.Count; i += 8)
            {
                int v = 0; for (int k = 0; k < 8; k++) v = (v << 1) | bits[i + k]; cws.Add(v);
            }
            int pi = 0;
            while (cws.Count < dataCap) { cws.Add((pi & 1) == 0 ? 0xEC : 0x11); pi++; }

            List<int> finalCws = new List<int>();
            if (blocks == 1)
            {
                finalCws.AddRange(cws); finalCws.AddRange(RsEncode(cws, ecLen));
            }
            else
            {
                List<int> b1 = cws.GetRange(0, 68), b2 = cws.GetRange(68, 68);
                int[] e1 = RsEncode(b1, ecLen), e2 = RsEncode(b2, ecLen);
                for (int i = 0; i < 68; i++) { finalCws.Add(b1[i]); finalCws.Add(b2[i]); }
                for (int i = 0; i < ecLen; i++) { finalCws.Add(e1[i]); finalCws.Add(e2[i]); }
            }

            int[,] mat = new int[sz, sz];
            bool[,] isFn = new bool[sz, sz];
            int[] fPos = new int[] { 0, 0, 0, sz - 7, sz - 7, 0 };
            for (int f = 0; f < 6; f += 2)
            {
                int tr = fPos[f], tc = fPos[f + 1];
                for (int dr = -1; dr <= 7; dr++)
                    for (int dc = -1; dc <= 7; dc++)
                    {
                        bool dark = (dr >= 0 && dr <= 6 && dc >= 0 && dc <= 6) &&
                            (dr == 0 || dr == 6 || dc == 0 || dc == 6 || (dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4));
                        SetFn(mat, isFn, sz, tr + dr, tc + dc, dark);
                    }
            }
            for (int i = 8; i < sz - 8; i++) { SetFn(mat, isFn, sz, 6, i, i % 2 == 0); SetFn(mat, isFn, sz, i, 6, i % 2 == 0); }
            if (align > 0)
            {
                int[] pts = new int[] { 6, align };
                for (int a = 0; a < 2; a++)
                    for (int b = 0; b < 2; b++)
                    {
                        int ar = pts[a], ac = pts[b];
                        if ((ar == 6 && ac == 6) || (ar == 6 && ac == align) || (ar == align && ac == 6)) continue;
                        for (int dr = -2; dr <= 2; dr++)
                            for (int dc = -2; dc <= 2; dc++)
                                SetFn(mat, isFn, sz, ar + dr, ac + dc, Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1);
                    }
            }
            for (int i = 0; i < 9; i++) if (i != 6) { isFn[8, i] = true; isFn[i, 8] = true; }
            for (int i = 0; i < 8; i++) { isFn[8, sz - 1 - i] = true; isFn[sz - 1 - i, 8] = true; }
            SetFn(mat, isFn, sz, sz - 8, 8, true);

            List<int> ab = new List<int>(finalCws.Count * 8);
            for (int i = 0; i < finalCws.Count; i++) for (int k = 7; k >= 0; k--) ab.Add((finalCws[i] >> k) & 1);

            int bi = 0; bool up = true;
            for (int col = sz - 1; col > 0; col -= 2)
            {
                if (col == 6) col--;
                for (int step = 0; step < sz; step++)
                {
                    int r = up ? (sz - 1 - step) : step;
                    for (int dc = 0; dc >= -1; dc--)
                    {
                        int c = col + dc;
                        if (!isFn[r, c])
                        {
                            int bit = bi < ab.Count ? ab[bi++] : 0;
                            if ((r + c) % 2 == 0) bit ^= 1;
                            mat[r, c] = bit;
                        }
                    }
                }
                up = !up;
            }

            int fmt = 0x77C4;
            for (int i = 0; i < 6; i++) SetFn(mat, isFn, sz, 8, i, ((fmt >> (14 - i)) & 1) == 1);
            SetFn(mat, isFn, sz, 8, 7, ((fmt >> 8) & 1) == 1);
            SetFn(mat, isFn, sz, 8, 8, ((fmt >> 7) & 1) == 1);
            SetFn(mat, isFn, sz, 7, 8, ((fmt >> 6) & 1) == 1);
            for (int i = 0; i < 6; i++) SetFn(mat, isFn, sz, i, 8, ((fmt >> i) & 1) == 1);
            for (int i = 0; i < 7; i++) SetFn(mat, isFn, sz, sz - 1 - i, 8, ((fmt >> (14 - i)) & 1) == 1);
            for (int i = 0; i < 8; i++) SetFn(mat, isFn, sz, 8, sz - 8 + i, ((fmt >> (7 - i)) & 1) == 1);

            int padPx = 4; int total = (sz + padPx * 2) * scale;
            Bitmap bmp = new Bitmap(total, total);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int r = 0; r < sz; r++)
                    for (int c = 0; c < sz; c++)
                        if (mat[r, c] != 0) g.FillRectangle(Brushes.Black, (c + padPx) * scale, (r + padPx) * scale, scale, scale);
            }
            return bmp;
        }

        private static void PushBits(List<int> bits, int val, int count)
        {
            for (int i = count - 1; i >= 0; i--) bits.Add((val >> i) & 1);
        }

        private static void SetFn(int[,] mat, bool[,] isFn, int sz, int r, int c, bool dark)
        {
            if (r >= 0 && r < sz && c >= 0 && c < sz) { mat[r, c] = dark ? 1 : 0; isFn[r, c] = true; }
        }
    }
}
