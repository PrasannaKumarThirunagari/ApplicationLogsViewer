using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XmlLogAnalyzer.Core.Interfaces;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Services;

/// <summary>
/// Line-oriented parser for plain-text logs in the form:
///   <c>2026-05-10 20:24:26:46:114  -4:00 [ERR] Could not sync settings</c>
///
/// Parsing rules:
///  * The header regex matches: date, time, optional timezone, severity code in brackets, message.
///  * Lines that DON'T match the header pattern are treated as continuations of the previous
///    entry (typical for stack traces).
///  * The time component is split on ':' to extract HH / MM / SS / ms / tail — exposing
///    Seconds independently so the UI can group by it.
///
/// The reader is streaming — yields one entry at a time — so 5 GB log files cost constant memory.
/// </summary>
public sealed partial class TextLogParser : ITextLogParser
{
    private readonly ILogger<TextLogParser> _logger;
    private readonly AppSettings _settings;

    public TextLogParser(ILogger<TextLogParser> logger, IOptions<AppSettings> options)
    {
        _logger = logger;
        _settings = options.Value;
    }

    // ---------------------------------------------------------------
    // Regex: pre-compiled, generated source — fast & allocation-light.
    //
    //   date   = YYYY-MM-DD (or single-digit month/day)
    //   time   = digits/colons/dots
    //   tz     = optional +/-H:MM
    //   sev    = letters or digits inside [..]
    //   msg    = rest of the line
    // ---------------------------------------------------------------
    [GeneratedRegex(
        @"^\s*(?<date>\d{2,4}-\d{1,2}-\d{1,2})\s+(?<time>[\d:.]+)(?:\s+(?<tz>[+-]?\d{1,2}:\d{2}))?\s+\[(?<sev>[A-Za-z0-9]+)\]\s?(?<msg>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    public async IAsyncEnumerable<TextLogEntry> StreamAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists) throw new FileNotFoundException("Text log not found.", filePath);
        if (fi.Length == 0) yield break;
        if (fi.Length > _settings.MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"File too large ({fi.Length:N0} bytes; max {_settings.MaxFileSizeBytes:N0}).");

        await using var fs = new FileStream(
            filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024, useAsync: true);

        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        TextLogEntry? pending = null;
        var index = 0;
        var sb = new StringBuilder(256);

        while (!sr.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await sr.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            var m = HeaderRegex().Match(line);
            if (m.Success)
            {
                // Emit any pending entry (its continuation lines have been appended).
                if (pending is not null)
                {
                    yield return pending;
                }

                pending = BuildEntry(line, m, index++);
                continue;
            }

            // Continuation line. Append to the pending entry's message, or, if none, emit a
            // standalone "raw" entry so nothing is silently lost.
            if (string.IsNullOrWhiteSpace(line) && pending is null) continue;

            if (pending is null)
            {
                pending = new TextLogEntry
                {
                    Index = index++,
                    Message = line,
                    RawLine = line,
                    SeverityCode = "RAW",
                    SeverityLevel = SeverityLevel.Info,
                };
                continue;
            }

            sb.Clear();
            sb.Append(pending.Message);
            if (pending.Message.Length > 0) sb.AppendLine();
            sb.Append(line);
            pending.Message = sb.ToString();
            pending.RawLine = pending.RawLine + "\n" + line;
        }

        if (pending is not null)
            yield return pending;
    }

    // ---------------------------------------------------------------
    private static TextLogEntry BuildEntry(string raw, Match m, int index)
    {
        var date = m.Groups["date"].Value;
        var time = m.Groups["time"].Value;
        var tz   = m.Groups["tz"].Success ? m.Groups["tz"].Value : null;
        var sev  = m.Groups["sev"].Value;
        var msg  = m.Groups["msg"].Value.TrimEnd();

        var entry = new TextLogEntry
        {
            Index = index,
            Date = date,
            Time = time,
            TimezoneOffset = tz,
            SeverityCode = sev,
            SeverityLevel = NormalizeSeverity(sev),
            Message = msg,
            RawLine = raw,
        };

        // Decompose the time into hour / minute / second / ms / tail.
        var parts = time.Split(':', StringSplitOptions.None);
        // Last part might have a dot if it's "26.114" style; split that too.
        TrySetInt(parts, 0, v => entry.Hour    = v);
        TrySetInt(parts, 1, v => entry.Minute  = v);
        TrySetInt(parts, 2, v => entry.Seconds = v);
        TrySetInt(parts, 3, v => entry.Milliseconds = v);
        if (parts.Length > 4)
            entry.TimeFractionTail = string.Join(':', parts.Skip(4));

        // Best-effort full timestamp parse.
        entry.Timestamp = TryParseTimestamp(date, time, tz);

        return entry;
    }

    private static void TrySetInt(string[] parts, int idx, Action<int> set)
    {
        if (idx >= parts.Length) return;
        var p = parts[idx];
        var dot = p.IndexOf('.');
        if (dot >= 0) p = p[..dot]; // strip dotted decimal fragment for integer interpretation
        if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            set(v);
    }

    private static DateTimeOffset? TryParseTimestamp(string date, string time, string? tz)
    {
        // Reduce time to HH:MM:SS[.fff] so DateTimeOffset.TryParse can handle it.
        var parts = time.Split(':');
        if (parts.Length < 3) return null;
        var hms = string.Join(':', parts.Take(3));
        var ms = parts.Length > 3 ? "." + parts[3].PadLeft(3, '0')[..Math.Min(3, parts[3].Length)] : "";
        var tzSuffix = string.IsNullOrEmpty(tz) ? "" : " " + NormaliseTz(tz);
        var s = $"{date} {hms}{ms}{tzSuffix}";

        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
            return dto;
        if (DateTimeOffset.TryParse(s, out dto)) return dto;
        return null;
    }

    private static string NormaliseTz(string tz)
    {
        // "-4:00" -> "-04:00" so DateTimeOffset parser accepts it.
        if (tz.Length < 5)
        {
            var sign = tz[0] == '+' || tz[0] == '-' ? tz[0] : '+';
            var body = tz.TrimStart('+', '-');
            var colon = body.IndexOf(':');
            if (colon > 0)
            {
                var h = body[..colon].PadLeft(2, '0');
                var rest = body[colon..];
                return $"{sign}{h}{rest}";
            }
        }
        return tz;
    }

    private static string NormalizeSeverity(string code) => code.ToUpperInvariant() switch
    {
        "ERR" or "ERROR"   or "FTL" or "FATAL" or "CRITICAL" or "CRIT"   => SeverityLevel.Error,
        "WRN" or "WARN"    or "WARNING"                                   => SeverityLevel.Warning,
        "INF" or "INFO"    or "INFORMATION"                               => SeverityLevel.Info,
        "DBG" or "DEBUG"   or "TRC" or "TRACE" or "VRB" or "VERBOSE"     => SeverityLevel.Debug,
        _ => SeverityLevel.Info, // unknown -> Info so it's still surfaced
    };
}
