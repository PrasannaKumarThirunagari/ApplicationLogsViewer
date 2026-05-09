using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Services;

/// <summary>
/// Streaming XML log parser.
///
/// Key design points:
///  * Uses <see cref="XmlReader"/> with conservative settings — DTD disabled, max char count limited,
///    so we're hardened against XXE / billion-laughs / external entity attacks.
///  * Detects whether the file already has a single root element. If not (i.e. the file is a
///    "fragment" of multiple LogData siblings), we synthesise a temporary &lt;Root&gt; on the fly
///    by composing two <see cref="XmlReader"/>s — without rewriting the file or loading it
///    into memory.
///  * Yields entries one at a time so a 5 GB log file uses constant memory.
/// </summary>
public sealed class XmlLogParser : IXmlLogParser
{
    private const string LogElementName = "LogData";
    private const string SyntheticRoot  = "Root";

    private readonly ILogger<XmlLogParser> _logger;
    private readonly AppSettings _settings;

    public XmlLogParser(ILogger<XmlLogParser> logger, IOptions<AppSettings> options)
    {
        _logger = logger;
        _settings = options.Value;
    }

    public async IAsyncEnumerable<LogEntry> StreamAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Log file not found.", filePath);

        var fi = new FileInfo(filePath);
        if (fi.Length > _settings.MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"File too large ({fi.Length:N0} bytes; max {_settings.MaxFileSizeBytes:N0}).");
        if (fi.Length == 0)
            yield break;

        // Open with FileShare.ReadWrite|Delete so we can read files that some other process is
        // currently appending to (typical for live logs).
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024, useAsync: true);

        using var reader = await BuildReaderAsync(stream, ct);

        // Prime the reader to the first node.
        bool primed = false;
        try { primed = await reader.ReadAsync(); }
        catch (XmlException ex) { _logger.LogWarning(ex, "Initial read failed for {File}", filePath); }
        if (!primed) yield break;

        var index = 0;
        while (reader.ReadState == ReadState.Interactive)
        {
            ct.ThrowIfCancellationRequested();

            // Skip anything that isn't a <LogData> open tag.
            if (reader.NodeType != XmlNodeType.Element ||
                !reader.LocalName.Equals(LogElementName, StringComparison.OrdinalIgnoreCase))
            {
                bool advanced = false;
                try { advanced = await reader.ReadAsync(); }
                catch (XmlException ex) { _logger.LogWarning(ex, "Stopping on parse error"); }
                if (!advanced) yield break;
                continue;
            }

            // Read the entire <LogData>...</LogData>.  IMPORTANT: ReadOuterXmlAsync()
            // ALREADY advances the reader to the next sibling node, so we MUST NOT call
            // Read() again at the top of the loop or we'll skip every other entry.
            string? outer = null;
            bool fatal = false;
            try
            {
                outer = await reader.ReadOuterXmlAsync().ConfigureAwait(false);
            }
            catch (XmlException ex)
            {
                _logger.LogWarning(ex, "Malformed <LogData> near index {Index}; stopping read", index);
                fatal = true;
            }
            if (fatal) yield break;

            if (!string.IsNullOrEmpty(outer))
            {
                LogEntry? entry = null;
                try { entry = ParseEntry(outer, index); }
                catch (XmlException ex) { _logger.LogDebug(ex, "Skip bad inner entry"); }

                if (entry is not null)
                {
                    yield return entry;
                    index++;
                }
            }
            // Loop continues with reader already positioned at the next node — no Read().
        }
    }

    public async Task<IReadOnlyList<LogEntry>> ParseStringAsync(string xml, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return Array.Empty<LogEntry>();

        var wrapped = NeedsRootWrap(xml) ? $"<{SyntheticRoot}>{xml}</{SyntheticRoot}>" : xml;

        using var sr = new StringReader(wrapped);
        using var reader = XmlReader.Create(sr, SafeReaderSettings());

        var list = new List<LogEntry>();
        var idx = 0;

        if (!await reader.ReadAsync()) return list;

        while (reader.ReadState == ReadState.Interactive)
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element ||
                !reader.LocalName.Equals(LogElementName, StringComparison.OrdinalIgnoreCase))
            {
                if (!await reader.ReadAsync()) break;
                continue;
            }

            // ReadOuterXml advances past the element automatically.
            var outer = await reader.ReadOuterXmlAsync();
            try { list.Add(ParseEntry(outer, idx++)); }
            catch (XmlException ex) { _logger.LogDebug(ex, "Skip bad entry"); }
        }
        return list;
    }

    // -----------------------------------------------------------------------
    // Reader composition
    // -----------------------------------------------------------------------

    private async Task<XmlReader> BuildReaderAsync(FileStream stream, CancellationToken ct)
    {
        // Sniff the first ~4 KB to decide if the file already has a root. We read a copy into a
        // buffer, scan it, then "rewind" by chaining two readers (buffer + remainder of stream).
        var buf = new byte[Math.Min(4096, stream.Length)];
        var read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct);

        // Strip BOM if present so the heuristic isn't fooled.
        var encoding = DetectEncoding(buf, read);
        var head = encoding.GetString(buf, 0, read);
        var needsWrap = NeedsRootWrap(head);

        // Reset stream to start so we can feed everything to the reader.
        stream.Seek(0, SeekOrigin.Begin);

        if (!needsWrap)
        {
            return XmlReader.Create(stream, SafeReaderSettings());
        }

        // Compose: open <Root>, then file contents, then </Root>.
        // Using a CompositeStream lets us avoid copying the whole file.
        var prefix = encoding.GetBytes($"<{SyntheticRoot}>");
        var suffix = encoding.GetBytes($"</{SyntheticRoot}>");
        var composite = new ConcatStream(
            new MemoryStream(prefix, writable: false),
            stream,
            new MemoryStream(suffix, writable: false));

        var settings = SafeReaderSettings();
        settings.ConformanceLevel = ConformanceLevel.Document;
        return XmlReader.Create(composite, settings);
    }

    private static bool NeedsRootWrap(string head)
    {
        // Strip XML decl and comments / whitespace before deciding.
        var span = head.AsSpan().TrimStart();
        if (span.IsEmpty) return false;

        // Skip <?xml ... ?>
        if (span.StartsWith("<?xml"))
        {
            var end = span.IndexOf("?>");
            if (end < 0) return true; // malformed -> wrap and let reader complain
            span = span[(end + 2)..].TrimStart();
        }

        // Skip leading comments
        while (span.StartsWith("<!--"))
        {
            var close = span.IndexOf("-->");
            if (close < 0) return true;
            span = span[(close + 3)..].TrimStart();
        }

        // The cheapest, most reliable signal: count top-level element opens.
        // If we see two opening element tags before encountering the close of the first,
        // we need a wrapping root.
        int depth = 0;
        int topLevelElements = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] != '<') continue;

            // Skip comments / CDATA / PI inside content
            if (i + 3 < span.Length && span[i + 1] == '!' && span[i + 2] == '-' && span[i + 3] == '-')
            {
                var close = span.Slice(i).IndexOf("-->");
                if (close < 0) break;
                i += close + 2;
                continue;
            }
            if (i + 1 < span.Length && span[i + 1] == '?')
            {
                var close = span.Slice(i).IndexOf("?>");
                if (close < 0) break;
                i += close + 1;
                continue;
            }
            if (i + 1 < span.Length && span[i + 1] == '/')
            {
                depth--;
                if (depth < 0) { /* extra close */ depth = 0; }
                continue;
            }

            // Opening tag
            if (depth == 0) topLevelElements++;
            // Look ahead to see if it's self-closing (<Foo/>)
            var endOfTag = span.Slice(i).IndexOf('>');
            if (endOfTag < 0) break;
            var tagSlice = span.Slice(i, endOfTag + 1);
            if (!tagSlice.EndsWith("/>"))
                depth++;
            i += endOfTag;

            if (topLevelElements >= 2) return true;
        }
        return topLevelElements >= 2;
    }

    private static Encoding DetectEncoding(byte[] buf, int length)
    {
        if (length >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
            return new UTF8Encoding(true);
        if (length >= 2 && buf[0] == 0xFF && buf[1] == 0xFE)
            return Encoding.Unicode;
        if (length >= 2 && buf[0] == 0xFE && buf[1] == 0xFF)
            return Encoding.BigEndianUnicode;
        return new UTF8Encoding(false);
    }

    private static XmlReaderSettings SafeReaderSettings() => new()
    {
        Async = true,
        // SECURITY: disable DTD processing entirely — kills XXE / billion-laughs.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = false,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = true,
        // Keep memory bounded.
        MaxCharactersFromEntities = 1024L,
        ConformanceLevel = ConformanceLevel.Auto,
    };

    // -----------------------------------------------------------------------
    // Entry parsing
    // -----------------------------------------------------------------------

    private static LogEntry ParseEntry(string outerXml, int index)
    {
        var entry = new LogEntry { Index = index, RawXml = outerXml };

        // Use a non-async settings copy (saves the cost of allocating Tasks).
        var settings = SafeReaderSettings();
        settings.Async = false;

        using var sr  = new StringReader(outerXml);
        using var rdr = XmlReader.Create(sr, settings);

        rdr.MoveToContent();
        if (!rdr.Read()) return entry;

        while (rdr.ReadState == ReadState.Interactive)
        {
            // Stop when we reach the closing </LogData>.
            if (rdr.NodeType == XmlNodeType.EndElement &&
                rdr.LocalName.Equals(LogElementName, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (rdr.NodeType == XmlNodeType.Element)
            {
                var name = rdr.LocalName;
                string value;
                if (rdr.IsEmptyElement)
                {
                    // <Foo /> — ReadElementContentAsString does NOT advance for empty elements,
                    // which would cause an infinite loop. Advance manually.
                    value = string.Empty;
                    rdr.Read();
                }
                else
                {
                    // Advances past the matching EndElement.
                    value = rdr.ReadElementContentAsString();
                }
                AssignField(entry, name, value);
                continue;
            }

            if (!rdr.Read()) break;
        }
        return entry;
    }

    private static void AssignField(LogEntry e, string name, string? value)
    {
        if (value is not null) value = value.Trim();
        switch (name)
        {
            case "ConversationId":  e.ConversationId  = value; break;
            case "SeverityLevel":   e.SeverityLevel   = SeverityLevel.Normalize(value); break;
            case "Time":            e.Time            = TryParseTime(value); break;
            case "MachineName":     e.MachineName     = value; break;
            case "ComponentName":   e.ComponentName   = value; break;
            case "TypeName":        e.TypeName        = value; break;
            case "Operation":       e.Operation       = value; break;
            case "LogMessage":
                // LogMessage in the sample sometimes contains the stack trace appended after a comma.
                // Try to split it cleanly.
                SplitLogMessage(e, value);
                break;
            case "ProcessId":       e.ProcessId       = value; break;
            case "ManagedThreadId": e.ManagedThreadId = value; break;
            case "AppDomainID":     e.AppDomainID     = value; break;
            case "AppDomainName":   e.AppDomainName   = value; break;
            case "stkFrame":
            case "StkFrame":        e.StkFrame        = value; break;
            default:                e.ExtraFields[name] = value; break;
        }
    }

    private static void SplitLogMessage(LogEntry e, string? value)
    {
        if (string.IsNullOrEmpty(value)) { e.LogMessage = value; return; }
        var idx = value.IndexOf("Stack Trace", StringComparison.OrdinalIgnoreCase);
        if (idx <= 0) { e.LogMessage = value; return; }
        e.LogMessage = value[..idx].TrimEnd(' ', ',', '\r', '\n');
        e.StackTrace = value[idx..].Trim();
    }

    private static DateTimeOffset? TryParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var v))
            return v;
        if (DateTimeOffset.TryParse(value, out v)) return v;
        return null;
    }
}

/// <summary>
/// Read-only stream that concatenates several streams in order. Used by
/// <see cref="XmlLogParser"/> to bolt synthetic root tags onto the real file
/// without copying its contents into memory.
/// </summary>
internal sealed class ConcatStream : Stream
{
    private readonly Stream[] _streams;
    private int _idx;

    public ConcatStream(params Stream[] streams) { _streams = streams; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        while (_idx < _streams.Length)
        {
            var n = _streams[_idx].Read(buffer, offset, count);
            if (n > 0) return n;
            _idx++;
        }
        return 0;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        while (_idx < _streams.Length)
        {
            var n = await _streams[_idx].ReadAsync(buffer, ct);
            if (n > 0) return n;
            _idx++;
        }
        return 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var s in _streams) s.Dispose();
        base.Dispose(disposing);
    }
}
