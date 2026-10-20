using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;

namespace OneMarkDown.Markdown.Transforms
{
    /// <summary>
    /// 标题、引用块、分隔线的 OneNote 适配。
    /// 标题靠内联 style 而不是语义标签，因为 OneNote 对 h1~h6 的映射并不稳定。
    /// </summary>
    internal static class BlockTransform
    {
        private static readonly Dictionary<string, string> HeadingStyles = new Dictionary<string, string>
        {
            { "h1", "font-size:20pt;font-weight:bold;color:#1F3864;margin:14pt 0 6pt 0" },
            { "h2", "font-size:16pt;font-weight:bold;color:#2E5496;margin:12pt 0 5pt 0" },
            { "h3", "font-size:13pt;font-weight:bold;color:#1F3864;margin:10pt 0 4pt 0" },
            { "h4", "font-size:12pt;font-weight:bold;color:#1F2328;margin:10pt 0 4pt 0" },
            { "h5", "font-size:11pt;font-weight:bold;color:#1F2328;margin:8pt 0 3pt 0" },
            { "h6", "font-size:10pt;font-weight:bold;color:#57606A;margin:8pt 0 3pt 0" }
        };

        private const string QuoteStyle =
            "border-left:3px solid #C9D1D9;padding-left:12px;margin-left:0;color:#4D5560";

        internal static void Apply(IDocument document)
        {
            foreach (var heading in document.QuerySelectorAll("h1, h2, h3, h4, h5, h6").ToList())
            {
                string style;
                if (HeadingStyles.TryGetValue(heading.LocalName, out style))
                {
                    heading.SetAttribute("style", style);
                }
            }

            foreach (var quote in document.QuerySelectorAll("blockquote").ToList())
            {
                quote.SetAttribute("style", QuoteStyle);
            }

            foreach (var rule in document.QuerySelectorAll("hr").ToList())
            {
                TransformHelper.Replace(rule, BuildSeparator(document));
            }
        }

        private static IElement BuildSeparator(IDocument document)
        {
            var table = document.CreateElement("table");
            table.SetAttribute("border", "0");
            table.SetAttribute("width", "100%");
            table.SetAttribute("cellspacing", "0");
            table.SetAttribute("cellpadding", "0");

            var row = document.CreateElement("tr");
            var cell = document.CreateElement("td");
            cell.SetAttribute("style", "border-bottom:1px solid #BFBFBF;height:1px;font-size:1pt");
            cell.InnerHtml = "&nbsp;";

            row.AppendChild(cell);
            table.AppendChild(row);

            return table;
        }
    }
}
