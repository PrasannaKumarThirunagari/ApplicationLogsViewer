using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace XmlLogAnalyzer.Core.Services;

/// <summary>
/// Pure utility helpers for the XML viewers (pretty-print, JSON conversion, tree shape).
/// </summary>
public static class XmlConverter
{
    public static string Pretty(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineOnAttributes = false,
                OmitXmlDeclaration = true,
            };
            var sb = new StringBuilder();
            using (var w = XmlWriter.Create(sb, settings))
                doc.Save(w);
            return sb.ToString();
        }
        catch
        {
            return xml; // best effort
        }
    }

    public static string ToJson(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            if (doc.Root is null) return "{}";
            var obj = ElementToObject(doc.Root);
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }
        catch
        {
            return "{}";
        }
    }

    private static object ElementToObject(XElement e)
    {
        if (!e.HasElements && !e.HasAttributes)
            return e.Value;

        var dict = new Dictionary<string, object?>();
        foreach (var attr in e.Attributes())
            dict["@" + attr.Name.LocalName] = attr.Value;

        foreach (var grp in e.Elements().GroupBy(x => x.Name.LocalName))
        {
            var arr = grp.Select(ElementToObject).ToList();
            dict[grp.Key] = arr.Count == 1 ? arr[0] : arr;
        }

        if (!e.HasElements && e.Attributes().Any())
            dict["#text"] = e.Value;

        return dict;
    }
}
