using System.IO.Packaging;
using System.Text;
using System.Xml;
using OpenMcdf;

namespace AgenticMarker.Documents;

public static class DocumentConverter
{
    public static string ConvertToMarkdown(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".doc" => ReadDoc(filePath),
            ".docx" => ReadDocx(filePath),
            ".md" or ".txt" => File.ReadAllText(filePath),
            _ => throw new NotSupportedException($"Unsupported file format: {extension}. Only .doc, .docx, .md, and .txt are supported.")
        };
    }

    private static string ReadDoc(string filePath)
    {
        // Read .doc (OLE2 Compound Binary Format) using OpenMcdf
        // Extract text from the WordDocument stream
        using var cf = RootStorage.OpenRead(filePath);
        using var stream = cf.OpenStream("WordDocument");

        // Read the full WordDocument stream
        var data = new byte[stream.Length];
        _ = stream.Read(data, 0, data.Length);

        // The .doc format stores text in a complex binary format.
        // For a practical approach, we extract readable text using Unicode detection.
        return ExtractTextFromDocBytes(data);
    }

    private static string ExtractTextFromDocBytes(byte[] data)
    {
        // Try to extract text from the Word binary format
        // Word .doc files store text as Unicode (UTF-16LE) in the document stream
        // We use a heuristic approach to extract readable text
        var sb = new StringBuilder();
        var inText = false;
        var textBuffer = new List<byte>();

        for (int i = 0; i < data.Length - 1; i++)
        {
            var b1 = data[i];
            var b2 = data[i + 1];

            // Check for printable ASCII as UTF-16LE (low byte is printable, high byte is 0)
            if (b2 == 0 && b1 >= 0x20 && b1 < 0x7F)
            {
                textBuffer.Add(b1);
                textBuffer.Add(b2);
                inText = true;
                i++; // Skip the high byte
            }
            else if (b2 == 0 && (b1 == 0x0D || b1 == 0x0A))
            {
                // Newline character
                if (textBuffer.Count > 0)
                {
                    textBuffer.Add(b1);
                    textBuffer.Add(b2);
                }
                i++;
            }
            else
            {
                if (inText && textBuffer.Count >= 8) // At least 4 chars worth of UTF-16
                {
                    var text = Encoding.Unicode.GetString(textBuffer.ToArray()).Trim();
                    if (text.Length >= 3) // Only keep meaningful text runs
                    {
                        sb.AppendLine(text);
                    }
                }
                textBuffer.Clear();
                inText = false;
            }
        }

        // Flush remaining buffer
        if (textBuffer.Count >= 8)
        {
            var text = Encoding.Unicode.GetString(textBuffer.ToArray()).Trim();
            if (text.Length >= 3)
            {
                sb.AppendLine(text);
            }
        }

        var result = sb.ToString();

        // If heuristic extraction yields very little, fall back to plain byte scanning
        if (result.Length < 100)
        {
            result = FallbackExtractText(data);
        }

        return result;
    }

    private static string FallbackExtractText(byte[] data)
    {
        // Simple fallback: scan for runs of printable ASCII bytes
        var sb = new StringBuilder();
        var run = new StringBuilder();

        foreach (var b in data)
        {
            if (b >= 0x20 && b < 0x7F)
            {
                run.Append((char)b);
            }
            else if (b == 0x0D || b == 0x0A)
            {
                if (run.Length > 0)
                {
                    run.Append('\n');
                }
            }
            else
            {
                if (run.Length >= 10)
                {
                    sb.Append(run);
                }
                run.Clear();
            }
        }

        if (run.Length >= 10)
        {
            sb.Append(run);
        }

        return sb.ToString();
    }

    private static string ReadDocx(string filePath)
    {
        // Use System.IO.Packaging + raw XML to avoid NPOI numbering-parse bugs
        using var package = Package.Open(filePath, FileMode.Open, FileAccess.Read);
        // Find the main document part via the relationship, not a hardcoded path
        var mainRel = package.GetRelationshipsByType(
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument").First();
        var docPart = package.GetPart(PackUriHelper.ResolvePartUri(mainRel.SourceUri, mainRel.TargetUri));

        var xml = new XmlDocument();
        using (var partStream = docPart.GetStream())
            xml.Load(partStream);

        var nsm = new XmlNamespaceManager(xml.NameTable);
        nsm.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");

        // Load styles to detect headings
        var styles = new Dictionary<string, int>();
        var stylesRel = docPart.GetRelationshipsByType(
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles").FirstOrDefault();
        if (stylesRel != null)
        {
            var stylesUri = PackUriHelper.ResolvePartUri(docPart.Uri, stylesRel.TargetUri);
            var stylesXml = new XmlDocument();
            using (var stylesStream = package.GetPart(stylesUri).GetStream())
                stylesXml.Load(stylesStream);
            var stylesNsm = new XmlNamespaceManager(stylesXml.NameTable);
            stylesNsm.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");

            foreach (XmlNode style in stylesXml.SelectNodes("//w:style[@w:type='paragraph']", stylesNsm)!)
            {
                var styleId = style.Attributes?["w:styleId"]?.Value;
                var outlineLvl = style.SelectSingleNode(".//w:outlineLvl", stylesNsm);
                if (styleId != null && outlineLvl?.Attributes?["w:val"]?.Value is { } val &&
                    int.TryParse(val, out var level))
                {
                    styles[styleId] = level + 1; // outline level is 0-based
                }
            }
        }

        var sb = new StringBuilder();

        // Extract paragraphs
        foreach (XmlNode para in xml.SelectNodes("//w:body/w:p", nsm)!)
        {
            var text = string.Concat(
                para.SelectNodes(".//w:r/w:t", nsm)!
                    .Cast<XmlNode>()
                    .Select(t => t.InnerText));

            var styleId = para.SelectSingleNode("w:pPr/w:pStyle/@w:val", nsm)?.Value;
            if (styleId != null && styles.TryGetValue(styleId, out var headingLevel))
                sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{new string('#', headingLevel)} {text}");
            else
                sb.AppendLine(text);
        }

        // Extract tables
        foreach (XmlNode table in xml.SelectNodes("//w:body/w:tbl", nsm)!)
        {
            foreach (XmlNode row in table.SelectNodes("w:tr", nsm)!)
            {
                sb.Append("| ");
                foreach (XmlNode cell in row.SelectNodes("w:tc", nsm)!)
                {
                    var cellText = string.Concat(
                        cell.SelectNodes(".//w:r/w:t", nsm)!
                            .Cast<XmlNode>()
                            .Select(t => t.InnerText));
                    sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{cellText} | ");
                }
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
