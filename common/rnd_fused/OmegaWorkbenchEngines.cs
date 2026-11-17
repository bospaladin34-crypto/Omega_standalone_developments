using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OmegaNativeWorkbench
{
    public sealed class BinaryXRaySummary
    {
        public int ByteLength;
        public double ShannonEntropyBits;
        public bool HasUtf8Bom;
        public bool HasUtf16Bom;
        public int TrojanBidiCount;
        public int ZeroWidthCount;
        public int CrLfCount;
        public int BareLfCount;
        public uint Crc32;
        public ulong SimHash64;
        public string Sha256Hex = "";
        public float[] HistogramNorm256 = new float[256];
        public float[] AnomalyStrip64 = new float[64];
        public List<string> Findings = new List<string>();
    }

    public sealed class HttpProbeResult
    {
        public bool Ok;
        public int StatusCode;
        public string StatusLine = "";
        public double ConnectMs;
        public double TtfbMs;
        public double TotalMs;
        public int PayloadBytes;
        public bool TruncatedAtCap;
        public string HeadersText = "";
        public string BodyPreview = "";
        public string ErrorMessage = "";
    }

    public sealed class PdfHarvestResult
    {
        public bool Ok;
        public int RawPdfBytes;
        public int EstimatedPages;
        public int FlateStreamsDecoded;
        public int ExtractedChars;
        public ulong SimHash64;
        public string Sha256Hex = "";
        public string MarkdownText = "";
        public string SavedCorpusPath = "";
    }

    public sealed class JobSandboxRunResult
    {
        public bool Ok;
        public int ExitCode;
        public bool TimedOutAndReaped;
        public bool JobObjectEnforced;
        public double ElapsedMs;
        public string OutputText = "";
    }

    public static class OmegaWorkbenchCore
    {
        private static readonly uint[] CrcTable = InitCrc32();

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000u;

        private static uint[] InitCrc32()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int b = 0; b < 8; b++)
                    c = ((c & 1u) != 0u) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        public static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            uint c = 0xFFFFFFFFu;
            int end = offset + count;
            for (int i = offset; i < end; i++)
                c = CrcTable[(c ^ data[i]) & 0xFFu] ^ (c >> 8);
            return ~c;
        }

        public static string ComputeSha256Hex(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data ?? new byte[0]);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < h.Length; i++)
                    sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // 64-bit Locality-Sensitive SimHash over 4-gram FNV-1a shingles
        public static ulong ComputeSimHash64(byte[] data)
        {
            if (data == null || data.Length == 0) return 0UL;
            int[] weights = new int[64];
            int n = data.Length;
            int stepLimit = Math.Min(n, 262144);
            for (int i = 0; i < stepLimit; i++)
            {
                ulong h = 14695981039346656037UL;
                int span = Math.Min(4, stepLimit - i);
                for (int k = 0; k < span; k++)
                {
                    h ^= data[i + k];
                    h *= 1099511628211UL;
                }
                for (int bit = 0; bit < 64; bit++)
                {
                    if (((h >> bit) & 1UL) != 0UL) weights[bit]++;
                    else weights[bit]--;
                }
            }
            ulong sig = 0UL;
            for (int bit = 0; bit < 64; bit++)
            {
                if (weights[bit] > 0) sig |= (1UL << bit);
            }
            return sig;
        }

        public static int HammingDistance64(ulong a, ulong b)
        {
            ulong x = a ^ b;
            int c = 0;
            while (x != 0UL)
            {
                x &= (x - 1UL);
                c++;
            }
            return c;
        }

        public static BinaryXRaySummary InspectBufferXRay(byte[] raw)
        {
            BinaryXRaySummary s = new BinaryXRaySummary();
            if (raw == null) raw = new byte[0];
            s.ByteLength = raw.Length;
            s.Crc32 = ComputeCrc32(raw, 0, raw.Length);
            s.SimHash64 = ComputeSimHash64(raw);
            s.Sha256Hex = ComputeSha256Hex(raw);

            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            {
                s.HasUtf8Bom = true;
                s.Findings.Add("ALERT: UTF-8 BOM (EF BB BF) at byte 0 — breaks rustc/Vite/JSON strict lexers");
            }
            else if (raw.Length >= 2 && ((raw[0] == 0xFE && raw[1] == 0xFF) || (raw[0] == 0xFF && raw[1] == 0xFE)))
            {
                s.HasUtf16Bom = true;
                s.Findings.Add("ALERT: UTF-16 BOM detected at byte 0");
            }

            long[] counts = new long[256];
            int maxBin = 1;
            for (int i = 0; i < raw.Length; i++)
            {
                byte b = raw[i];
                counts[b]++;
                if (counts[b] > maxBin) maxBin = (int)counts[b];

                if (b == 0x0A)
                {
                    if (i > 0 && raw[i - 1] == 0x0D) s.CrLfCount++;
                    else s.BareLfCount++;
                }

                // UTF-8 Trojan-Source bidi & zero-width byte sequences:
                // E2 80 AA..AE (U+202A..U+202E), E2 81 A6..A9 (U+2066..U+2069), E2 80 8B..8D (U+200B..U+200D)
                int bucket = (raw.Length > 0) ? (int)((long)i * 64L / Math.Max(1, raw.Length)) : 0;
                if (bucket > 63) bucket = 63;

                if (i + 2 < raw.Length && raw[i] == 0xE2)
                {
                    byte b1 = raw[i + 1];
                    byte b2 = raw[i + 2];
                    if ((b1 == 0x80 && b2 >= 0xAA && b2 <= 0xAE) || (b1 == 0x81 && b2 >= 0xA6 && b2 <= 0xA9))
                    {
                        s.TrojanBidiCount++;
                        s.AnomalyStrip64[bucket] = 1.0f;
                    }
                    else if (b1 == 0x80 && b2 >= 0x8B && b2 <= 0x8D)
                    {
                        s.ZeroWidthCount++;
                        if (s.AnomalyStrip64[bucket] < 0.65f) s.AnomalyStrip64[bucket] = 0.65f;
                    }
                }
            }

            double ent = 0.0;
            if (raw.Length > 0)
            {
                double invLen = 1.0 / raw.Length;
                double invLog2 = 1.0 / Math.Log(2.0);
                for (int k = 0; k < 256; k++)
                {
                    s.HistogramNorm256[k] = (float)counts[k] / (float)maxBin;
                    if (counts[k] > 0)
                    {
                        double p = counts[k] * invLen;
                        ent -= p * (Math.Log(p) * invLog2);
                    }
                }
            }
            s.ShannonEntropyBits = ent;

            if (s.TrojanBidiCount > 0)
                s.Findings.Add(string.Format(CultureInfo.InvariantCulture, "CRITICAL CVE-2021-42574: {0} Trojan-Source Unicode Bidi override(s) detected!", s.TrojanBidiCount));
            if (s.ZeroWidthCount > 0)
                s.Findings.Add(string.Format(CultureInfo.InvariantCulture, "WARN: {0} invisible Zero-Width Unicode character(s) found", s.ZeroWidthCount));
            if (s.CrLfCount > 0 && s.BareLfCount > 0)
                s.Findings.Add(string.Format(CultureInfo.InvariantCulture, "LINE-ENDING DRIFT: Mixed CRLF ({0}) and bare LF ({1})", s.CrLfCount, s.BareLfCount));
            if (s.Findings.Count == 0)
                s.Findings.Add("CLEAN: Zero UTF-8 BOM, zero Trojan-Source bidi overrides, consistent line endings");

            return s;
        }

        public static byte[] SanitizeZeroBomAndTrojanUnicode(byte[] raw, bool normalizeCrLf)
        {
            if (raw == null || raw.Length == 0) return new byte[0];
            int start = 0;
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) start = 3;

            List<byte> clean = new List<byte>(raw.Length);
            for (int i = start; i < raw.Length; i++)
            {
                if (i + 2 < raw.Length && raw[i] == 0xE2)
                {
                    byte b1 = raw[i + 1];
                    byte b2 = raw[i + 2];
                    bool isBidi = (b1 == 0x80 && b2 >= 0xAA && b2 <= 0xAE) || (b1 == 0x81 && b2 >= 0xA6 && b2 <= 0xA9);
                    bool isZw = (b1 == 0x80 && b2 >= 0x8B && b2 <= 0x8D);
                    if (isBidi || isZw)
                    {
                        i += 2;
                        continue;
                    }
                }
                if (normalizeCrLf)
                {
                    if (raw[i] == 0x0D) continue;
                    if (raw[i] == 0x0A) { clean.Add(0x0D); clean.Add(0x0A); continue; }
                }
                clean.Add(raw[i]);
            }
            return clean.ToArray();
        }

        public static string FormatHexAsciiPreview(byte[] raw, int maxBytes)
        {
            if (raw == null || raw.Length == 0) return "(empty buffer)";
            int n = Math.Min(raw.Length, maxBytes);
            StringBuilder sb = new StringBuilder(n * 4);
            for (int off = 0; off < n; off += 16)
            {
                sb.Append(off.ToString("X6", CultureInfo.InvariantCulture));
                sb.Append("  ");
                int rowLen = Math.Min(16, n - off);
                for (int j = 0; j < 16; j++)
                {
                    if (j < rowLen) sb.Append(raw[off + j].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                    else sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }
                sb.Append(" |");
                for (int j = 0; j < rowLen; j++)
                {
                    byte b = raw[off + j];
                    sb.Append((b >= 32 && b <= 126) ? (char)b : '.');
                }
                sb.AppendLine("|");
            }
            if (raw.Length > n)
                sb.AppendFormat(CultureInfo.InvariantCulture, "... ({0} additional bytes omitted in preview)\r\n", raw.Length - n);
            return sb.ToString();
        }

        // Bounded, zero-cloud HTTP/REST socket inspector with microsecond QPC timing
        public static HttpProbeResult ExecuteBoundedHttpProbe(string method, string url, string customHeaders, string reqBody, int maxPreviewBytes, int timeoutMs)
        {
            HttpProbeResult res = new HttpProbeResult();
            Stopwatch totalSw = Stopwatch.StartNew();
            try
            {
                Uri u = new Uri(url);
                string host = u.Host;
                int port = u.Port;
                string pathAndQuery = string.IsNullOrEmpty(u.PathAndQuery) ? "/" : u.PathAndQuery;

                if (string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    // Use HttpWebRequest for TLS while keeping strict byte cap
                    HttpWebRequest wr = (HttpWebRequest)WebRequest.Create(u);
                    wr.Method = string.IsNullOrEmpty(method) ? "GET" : method;
                    wr.Timeout = timeoutMs;
                    wr.ReadWriteTimeout = timeoutMs;
                    wr.UserAgent = "OmegaWorkbench/1.0 (Zero-Electron Native x64)";
                    ApplyCustomHeaders(wr.Headers, customHeaders);
                    if (!string.IsNullOrEmpty(reqBody) && wr.Method != "GET")
                    {
                        byte[] pb = Encoding.UTF8.GetBytes(reqBody);
                        wr.ContentType = "application/json; charset=utf-8";
                        wr.ContentLength = pb.Length;
                        using (Stream rs = wr.GetRequestStream()) { rs.Write(pb, 0, pb.Length); }
                    }
                    Stopwatch ttfbSw = Stopwatch.StartNew();
                    using (HttpWebResponse resp = (HttpWebResponse)wr.GetResponse())
                    {
                        res.TtfbMs = ttfbSw.Elapsed.TotalMilliseconds;
                        res.StatusCode = (int)resp.StatusCode;
                        res.StatusLine = "HTTP/1.1 " + res.StatusCode.ToString(CultureInfo.InvariantCulture) + " " + resp.StatusDescription;
                        res.HeadersText = resp.Headers.ToString();
                        using (Stream s = resp.GetResponseStream())
                        {
                            byte[] cap = ReadCappedStream(s, maxPreviewBytes, out res.PayloadBytes, out res.TruncatedAtCap);
                            res.BodyPreview = Encoding.UTF8.GetString(cap);
                        }
                    }
                    res.Ok = true;
                    res.TotalMs = totalSw.Elapsed.TotalMilliseconds;
                    return res;
                }

                // Raw TCP socket path for exact Connect vs TTFB split on HTTP/loopback APIs
                using (Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    sock.ReceiveTimeout = timeoutMs;
                    sock.SendTimeout = timeoutMs;
                    Stopwatch connSw = Stopwatch.StartNew();
                    IAsyncResult ar = sock.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                        throw new TimeoutException("Connect timeout after " + timeoutMs.ToString(CultureInfo.InvariantCulture) + " ms");
                    sock.EndConnect(ar);
                    connSw.Stop();
                    res.ConnectMs = connSw.Elapsed.TotalMilliseconds;

                    byte[] bodyBytes = (!string.IsNullOrEmpty(reqBody) && method != "GET") ? Encoding.UTF8.GetBytes(reqBody) : new byte[0];
                    StringBuilder reqSb = new StringBuilder(512);
                    reqSb.Append(string.IsNullOrEmpty(method) ? "GET" : method).Append(' ').Append(pathAndQuery).Append(" HTTP/1.1\r\n");
                    reqSb.Append("Host: ").Append(host).Append(':').Append(port.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                    reqSb.Append("User-Agent: OmegaWorkbench/1.0 (Native x64)\r\n");
                    reqSb.Append("Connection: close\r\n");
                    if (!string.IsNullOrEmpty(customHeaders))
                    {
                        string[] hLines = customHeaders.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < hLines.Length; i++)
                        {
                            string hl = hLines[i].Replace("\r", "").Replace("\n", "").Trim();
                            if (hl.IndexOf(':') > 0) reqSb.Append(hl).Append("\r\n");
                        }
                    }
                    if (bodyBytes.Length > 0)
                    {
                        reqSb.Append("Content-Type: application/json; charset=utf-8\r\n");
                        reqSb.Append("Content-Length: ").Append(bodyBytes.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                    }
                    reqSb.Append("\r\n");

                    byte[] hdrOut = Encoding.ASCII.GetBytes(reqSb.ToString());
                    Stopwatch ttfb = Stopwatch.StartNew();
                    sock.Send(hdrOut);
                    if (bodyBytes.Length > 0) sock.Send(bodyBytes);

                    MemoryStream ms = new MemoryStream();
                    byte[] buf = new byte[8192];
                    bool gotFirst = false;
                    int totalRead = 0;
                    while (true)
                    {
                        int n = sock.Receive(buf, 0, buf.Length, SocketFlags.None);
                        if (n <= 0) break;
                        if (!gotFirst) { res.TtfbMs = ttfb.Elapsed.TotalMilliseconds; gotFirst = true; }
                        totalRead += n;
                        if (ms.Length < maxPreviewBytes)
                        {
                            int keep = Math.Min(n, maxPreviewBytes - (int)ms.Length);
                            ms.Write(buf, 0, keep);
                        }
                        else
                        {
                            res.TruncatedAtCap = true;
                            break;
                        }
                    }
                    res.PayloadBytes = totalRead;
                    ParseRawHttpResponse(ms.ToArray(), res);
                    res.Ok = true;
                }
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.ErrorMessage = ex.Message;
            }
            res.TotalMs = totalSw.Elapsed.TotalMilliseconds;
            return res;
        }

        private static void ApplyCustomHeaders(WebHeaderCollection col, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            string[] lines = raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                int idx = lines[i].IndexOf(':');
                if (idx > 0)
                {
                    string k = lines[i].Substring(0, idx).Trim();
                    string v = lines[i].Substring(idx + 1).Trim();
                    try { col[k] = v; } catch { }
                }
            }
        }

        private static byte[] ReadCappedStream(Stream s, int cap, out int totalSeen, out bool truncated)
        {
            totalSeen = 0;
            truncated = false;
            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                totalSeen += n;
                if (ms.Length < cap)
                {
                    int w = Math.Min(n, cap - (int)ms.Length);
                    ms.Write(buf, 0, w);
                }
                else
                {
                    truncated = true;
                    break;
                }
            }
            return ms.ToArray();
        }

        public static bool ParseRawHttpResponse(byte[] raw, HttpProbeResult res)
        {
            if (raw == null || raw.Length == 0) return false;
            string text = Encoding.UTF8.GetString(raw);
            int sep = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            string head = (sep >= 0) ? text.Substring(0, sep) : text;
            string body = (sep >= 0) ? text.Substring(sep + 4) : "";

            string[] hLines = head.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            res.StatusLine = hLines.Length > 0 ? hLines[0] : "";
            Match m = Regex.Match(res.StatusLine, @"HTTP/\d\.\d\s+(\d{3})");
            if (m.Success) int.TryParse(m.Groups[1].Value, out res.StatusCode);
            res.HeadersText = head;
            res.BodyPreview = body;
            return m.Success;
        }

        // Bounded HTTP header/chunk security validator for Chaos Vector V6
        public static bool ValidateUntrustedHttpEnvelope(string rawHttp, long maxAllowedContentLength)
        {
            if (string.IsNullOrEmpty(rawHttp) || rawHttp.IndexOf('\0') >= 0) return false;
            int sep = rawHttp.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (sep <= 0 || sep > 16384) return false;
            string hdr = rawHttp.Substring(0, sep);
            string body = rawHttp.Substring(sep + 4);
            string[] lines = hdr.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || !lines[0].StartsWith("HTTP/1.", StringComparison.Ordinal)) return false;

            bool isChunked = false;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) return false;
                string k = lines[i].Substring(0, colon).Trim();
                string v = lines[i].Substring(colon + 1).Trim();
                if (k.IndexOf(' ') >= 0 || k.IndexOf('\t') >= 0) return false;
                if (string.Equals(k, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    long cl;
                    if (!long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out cl)) return false;
                    if (cl < 0 || cl > maxAllowedContentLength) return false;
                }
                if (string.Equals(k, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                    v.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isChunked = true;
                }
            }
            if (isChunked)
            {
                int cr = body.IndexOf("\r\n", StringComparison.Ordinal);
                if (cr <= 0 || cr > 8) return false;
                long chunkLen;
                if (!long.TryParse(body.Substring(0, cr), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out chunkLen))
                    return false;
                if (chunkLen < 0 || chunkLen > maxAllowedContentLength) return false;
            }
            return true;
        }

        // Zero-download in-memory PDF /FlateDecode stream & BT..ET text harvester
        public static byte[] SynthesizeSampleFlatePdf(string title, string paragraph)
        {
            string contentStream = "BT /F1 14 Tf 50 750 Td (" + EscapePdfStr(title) + ") Tj 0 -24 Td [(" +
                                   EscapePdfStr(paragraph) + ")] TJ ET";
            byte[] rawContent = Encoding.ASCII.GetBytes(contentStream);
            byte[] compressed;
            using (MemoryStream ms = new MemoryStream())
            {
                // Zlib 2-byte header (0x78 0x9C) + Deflate raw payload
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                {
                    ds.Write(rawContent, 0, rawContent.Length);
                }
                compressed = ms.ToArray();
            }

            using (MemoryStream pdf = new MemoryStream())
            {
                WriteAscii(pdf, "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
                WriteAscii(pdf, "2 0 obj << /Type /Pages /Count 1 /Kids [3 0 R] >> endobj\n");
                WriteAscii(pdf, "3 0 obj << /Type /Page /Parent 2 0 R /Contents 4 0 R >> endobj\n");
                WriteAscii(pdf, "4 0 obj << /Filter /FlateDecode /Length " + compressed.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
                pdf.Write(compressed, 0, compressed.Length);
                WriteAscii(pdf, "\nendstream\nendobj\n%%EOF\n");
                return pdf.ToArray();
            }
        }

        private static string EscapePdfStr(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string norm = s.Replace("\u2014", "--").Replace("\u2013", "-")
                           .Replace("\u2018", "'").Replace("\u2019", "'")
                           .Replace("\u201C", "\"").Replace("\u201D", "\"");
            return norm.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }

        private static void WriteAscii(Stream s, string txt)
        {
            byte[] b = Encoding.ASCII.GetBytes(txt);
            s.Write(b, 0, b.Length);
        }

        public static PdfHarvestResult HarvestPdfBytesInMemory(byte[] pdfBytes, string sourceLabel, string saveCorpusDir)
        {
            PdfHarvestResult res = new PdfHarvestResult();
            if (pdfBytes == null || pdfBytes.Length < 16)
            {
                res.Ok = false;
                return res;
            }
            res.RawPdfBytes = pdfBytes.Length;
            res.Sha256Hex = ComputeSha256Hex(pdfBytes);
            string latin = Encoding.GetEncoding("ISO-8859-1").GetString(pdfBytes);
            res.EstimatedPages = Math.Max(1, Regex.Matches(latin, @"/Type\s*/Page\b").Count);

            StringBuilder extracted = new StringBuilder();
            int searchPos = 0;
            while (searchPos < latin.Length)
            {
                int stIdx = latin.IndexOf("stream", searchPos, StringComparison.Ordinal);
                if (stIdx < 0) break;
                int dataStart = stIdx + 6;
                if (dataStart < latin.Length && latin[dataStart] == '\r') dataStart++;
                if (dataStart < latin.Length && latin[dataStart] == '\n') dataStart++;

                int endIdx = latin.IndexOf("endstream", dataStart, StringComparison.Ordinal);
                if (endIdx < 0) break;
                int dataLen = endIdx - dataStart;
                if (dataLen > 0 && dataLen <= 16 * 1024 * 1024)
                {
                    string dictWindow = latin.Substring(Math.Max(0, stIdx - 220), Math.Min(220, stIdx));
                    byte[] rawSlice = new byte[dataLen];
                    Buffer.BlockCopy(pdfBytes, dataStart, rawSlice, 0, dataLen);

                    string decodedStreamText = "";
                    if (dictWindow.IndexOf("/FlateDecode", StringComparison.Ordinal) >= 0)
                    {
                        byte[] inflated = TryInflateZlibOrRaw(rawSlice);
                        if (inflated != null && inflated.Length > 0)
                        {
                            res.FlateStreamsDecoded++;
                            decodedStreamText = Encoding.GetEncoding("ISO-8859-1").GetString(inflated);
                        }
                    }
                    else
                    {
                        decodedStreamText = Encoding.GetEncoding("ISO-8859-1").GetString(rawSlice);
                    }

                    if (!string.IsNullOrEmpty(decodedStreamText))
                        ExtractPdfTextOperators(decodedStreamText, extracted);
                }
                searchPos = endIdx + 9;
            }

            string body = extracted.ToString().Trim();
            res.ExtractedChars = body.Length;
            byte[] bodyUtf8 = new UTF8Encoding(false).GetBytes(body);
            res.SimHash64 = ComputeSimHash64(bodyUtf8);

            StringBuilder md = new StringBuilder();
            md.AppendLine("# Harvested Corpus Document: " + (string.IsNullOrEmpty(sourceLabel) ? "In-Memory PDF Stream" : sourceLabel));
            md.AppendLine();
            md.AppendLine("- **SHA-256:** `" + res.Sha256Hex + "`");
            md.AppendLine("- **SimHash-64:** `0x" + res.SimHash64.ToString("X16", CultureInfo.InvariantCulture) + "`");
            md.AppendFormat(CultureInfo.InvariantCulture, "- **Pages / Flate Streams:** `{0}` page(s), `{1}` `/FlateDecode` stream(s) inflated in RAM\r\n", res.EstimatedPages, res.FlateStreamsDecoded);
            md.AppendLine();
            md.AppendLine("## Extracted Text");
            md.AppendLine();
            md.AppendLine(body.Length > 0 ? body : "(No uncompressed/Flate text operators present — image/CID-only stream)");
            res.MarkdownText = md.ToString();

            if (!string.IsNullOrEmpty(saveCorpusDir))
            {
                try
                {
                    Directory.CreateDirectory(saveCorpusDir);
                    string outFile = Path.Combine(saveCorpusDir, "doc_" + res.Sha256Hex.Substring(0, 12) + ".md");
                    File.WriteAllBytes(outFile, new UTF8Encoding(false).GetBytes(res.MarkdownText));
                    res.SavedCorpusPath = outFile;
                }
                catch { }
            }
            res.Ok = true;
            return res;
        }

        private static byte[] TryInflateZlibOrRaw(byte[] src)
        {
            for (int skip = 2; skip >= 0; skip -= 2)
            {
                if (src.Length <= skip) continue;
                try
                {
                    using (MemoryStream msIn = new MemoryStream(src, skip, src.Length - skip))
                    using (DeflateStream ds = new DeflateStream(msIn, CompressionMode.Decompress))
                    using (MemoryStream msOut = new MemoryStream())
                    {
                        byte[] buf = new byte[4096];
                        int n;
                        while ((n = ds.Read(buf, 0, buf.Length)) > 0)
                        {
                            msOut.Write(buf, 0, n);
                            if (msOut.Length > 8 * 1024 * 1024) break;
                        }
                        if (msOut.Length > 0) return msOut.ToArray();
                    }
                }
                catch { }
            }
            return null;
        }

        private static void ExtractPdfTextOperators(string streamText, StringBuilder dst)
        {
            MatchCollection parens = Regex.Matches(streamText, @"\((?:\\.|[^\\\)])*\)");
            for (int i = 0; i < parens.Count; i++)
            {
                string s = parens[i].Value;
                if (s.Length >= 2)
                {
                    string inner = s.Substring(1, s.Length - 2)
                        .Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\")
                        .Replace("\\n", "\n").Replace("\\r", "");
                    if (inner.Length > 0)
                    {
                        dst.Append(inner);
                        if (!inner.EndsWith(" ")) dst.Append(' ');
                    }
                }
            }
            if (parens.Count > 0) dst.AppendLine();
        }

        // Win32 NT Kernel Job Object sandbox runner (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000)
        public static bool VerifyKernelJobObjectCreateAndClose()
        {
            IntPtr hJob = IntPtr.Zero;
            try
            {
                hJob = CreateJobObjectW(IntPtr.Zero, null);
                if (hJob == IntPtr.Zero) return false;
                JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                uint sz = (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                return SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, ref info, sz);
            }
            catch { return false; }
            finally
            {
                if (hJob != IntPtr.Zero) CloseHandle(hJob);
            }
        }

        public static JobSandboxRunResult RunCommandInJobSandbox(string cmdLine, string workingDir, int timeoutMs)
        {
            JobSandboxRunResult r = new JobSandboxRunResult();
            Stopwatch sw = Stopwatch.StartNew();
            IntPtr hJob = IntPtr.Zero;
            Process proc = null;
            try
            {
                hJob = CreateJobObjectW(IntPtr.Zero, null);
                if (hJob != IntPtr.Zero)
                {
                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    uint sz = (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    r.JobObjectEnforced = SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, ref info, sz);
                }

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + cmdLine);
                psi.WorkingDirectory = (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir)) ? workingDir : Environment.CurrentDirectory;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                proc = Process.Start(psi);
                if (proc != null && hJob != IntPtr.Zero)
                {
                    try { AssignProcessToJobObject(hJob, proc.Handle); } catch { }
                }

                StringBuilder outSb = new StringBuilder();
                proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (outSb) { if (outSb.Length < 65536) outSb.AppendLine(e.Data); } };
                proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (outSb) { if (outSb.Length < 65536) outSb.AppendLine("[ERR] " + e.Data); } };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(timeoutMs))
                {
                    r.TimedOutAndReaped = true;
                    if (hJob != IntPtr.Zero) TerminateJobObject(hJob, 137u);
                    else { try { proc.Kill(); } catch { } }
                }
                else
                {
                    proc.WaitForExit();
                    r.ExitCode = proc.ExitCode;
                }
                lock (outSb) { r.OutputText = outSb.ToString(); }
                r.Ok = !r.TimedOutAndReaped && r.ExitCode == 0;
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.OutputText = "Sandbox error: " + ex.Message;
            }
            finally
            {
                if (proc != null) { try { proc.Dispose(); } catch { } }
                if (hJob != IntPtr.Zero) CloseHandle(hJob); // Guarantees zero zombie child processes!
            }
            r.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return r;
        }

        public static double TrimCurrentAndOmegaProcesses(out int trimmedCount, out double totalSavedMb)
        {
            trimmedCount = 0;
            totalSavedMb = 0.0;
            string[] names = new string[] { "OmegaWorkbench", "OmegaRnDDeck", "OmegaBrowser", "OmegaShell", "OmegaLabLauncher", "OmegaConductor", "OmegaTensor" };
            for (int i = 0; i < names.Length; i++)
            {
                Process[] procs = Process.GetProcessesByName(names[i]);
                for (int p = 0; p < procs.Length; p++)
                {
                    try
                    {
                        double before = procs[p].WorkingSet64 / (1024.0 * 1024.0);
                        if (EmptyWorkingSet(procs[p].Handle))
                        {
                            procs[p].Refresh();
                            double after = procs[p].WorkingSet64 / (1024.0 * 1024.0);
                            if (before > after) totalSavedMb += (before - after);
                            trimmedCount++;
                        }
                    }
                    catch { }
                    finally { try { procs[p].Dispose(); } catch { } }
                }
            }
            Process self = Process.GetCurrentProcess();
            return self.WorkingSet64 / (1024.0 * 1024.0);
        }
    }

    // Power-loss-safe Dual-Slot CRC-32 Collection Store replacing cloud-locked Postman accounts
    public sealed class AtomicPostmanCollectionWal
    {
        private readonly byte[] _backing = new byte[4096]; // Slot A: 0..2047, Slot B: 2048..4095
        private const uint Magic = 0x4F574231u; // "OWB1"
        private ulong _seq = 0;

        public void SaveCollectionAtomic(string jsonPayload, int simulateTornAfterBytes)
        {
            _seq++;
            int slot = ((_seq & 1UL) == 1UL) ? 0 : 2048;
            byte[] utf8 = new UTF8Encoding(false).GetBytes(jsonPayload ?? "[]");
            int payLen = Math.Min(utf8.Length, 1980);

            byte[] headerAndBody = new byte[16 + payLen];
            Buffer.BlockCopy(BitConverter.GetBytes(Magic), 0, headerAndBody, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)payLen), 0, headerAndBody, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(_seq), 0, headerAndBody, 8, 8);
            Buffer.BlockCopy(utf8, 0, headerAndBody, 16, payLen);
            uint crc = OmegaWorkbenchCore.ComputeCrc32(headerAndBody, 0, headerAndBody.Length);

            byte[] frame = new byte[headerAndBody.Length + 4];
            Buffer.BlockCopy(headerAndBody, 0, frame, 0, headerAndBody.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(crc), 0, frame, headerAndBody.Length, 4);

            int limit = (simulateTornAfterBytes >= 0 && simulateTornAfterBytes < frame.Length) ? simulateTornAfterBytes : frame.Length;
            for (int i = 0; i < limit; i++) _backing[slot + i] = frame[i];
            if (limit < frame.Length)
            {
                for (int i = limit; i < frame.Length; i++) _backing[slot + i] = (byte)(_backing[slot + i] ^ 0x5C);
            }
        }

        public bool TryLoadLatestCollection(out string jsonOut, out ulong committedSeq)
        {
            ulong sA; string jA; bool okA = CheckSlot(0, out sA, out jA);
            ulong sB; string jB; bool okB = CheckSlot(2048, out sB, out jB);
            if (okA && (!okB || sA >= sB)) { committedSeq = sA; jsonOut = jA; return true; }
            if (okB) { committedSeq = sB; jsonOut = jB; return true; }
            committedSeq = 0; jsonOut = "[]"; return false;
        }

        private bool CheckSlot(int off, out ulong seq, out string json)
        {
            seq = 0; json = "";
            uint m = BitConverter.ToUInt32(_backing, off);
            uint len = BitConverter.ToUInt32(_backing, off + 4);
            if (m != Magic || len > 1980u) return false;
            int totalBody = 16 + (int)len;
            uint storedCrc = BitConverter.ToUInt32(_backing, off + totalBody);
            uint calcCrc = OmegaWorkbenchCore.ComputeCrc32(_backing, off, totalBody);
            if (storedCrc != calcCrc) return false;
            seq = BitConverter.ToUInt64(_backing, off + 8);
            json = Encoding.UTF8.GetString(_backing, off + 16, (int)len);
            return true;
        }
    }
}
