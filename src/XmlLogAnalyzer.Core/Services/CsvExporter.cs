using System.Globalization;
using System.Text;
using XmlLogAnalyzer.Core.Models;

namespace XmlLogAnalyzer.Core.Services;

public static class CsvExporter
{
    public static byte[] ToCsv(IEnumerable<LogEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Index,Time,SeverityLevel,MachineName,ProcessId,Operation,LogMessage,StackTrace,ConversationId,AppDomainName,TypeName");

        foreach (var e in entries)
        {
            sb.Append(e.Index).Append(',');
            sb.Append(Esc(e.Time?.ToString("o", CultureInfo.InvariantCulture))).Append(',');
            sb.Append(Esc(e.SeverityLevel)).Append(',');
            sb.Append(Esc(e.MachineName)).Append(',');
            sb.Append(Esc(e.ProcessId)).Append(',');
            sb.Append(Esc(e.Operation)).Append(',');
            sb.Append(Esc(e.LogMessage)).Append(',');
            sb.Append(Esc(e.StackTrace)).Append(',');
            sb.Append(Esc(e.ConversationId)).Append(',');
            sb.Append(Esc(e.AppDomainName)).Append(',');
            sb.Append(Esc(e.TypeName));
            sb.AppendLine();
        }

        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }

    private static string Esc(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var v = value.Replace("\"", "\"\"");
        // Always quote — safe + handles embedded commas / newlines.
        return $"\"{v}\"";
    }
}
