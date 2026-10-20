using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;

namespace OneMarkDown.Markdown.Transforms
{
    /// <summary>
    /// OneNote 没有代码块的语义，通行做法是单格表格 + 灰底 + 等宽字体。
    /// 换行用 &lt;br&gt;，行内空格用 nbsp，保证缩进不塌。
    /// </summary>
    internal static class CodeBlockTransform
    {
        private const string TableStyle =
            "border-collapse:collapse;border-color:#D0D7DE;background-color:#F6F8FA";

        private const string CellStyle =
            "border:1px solid #D0D7DE;background-color:#F6F8FA;padding:8px";

        private const string CodeStyle =
            "font-family:Consolas,'Courier New',monospace;font-size:9pt;color:#1F2328";

        internal static void Apply(IDocument document)
        {
            var blocks = document.QuerySelectorAll("pre").ToList();
            foreach (var block in blocks)
            {
                var text = block.TextContent ?? string.Empty;
                var table = BuildTable(document, text);
                TransformHelper.Replace(block, table);
            }
        }

        private static IElement BuildTable(IDocument document, string text)
        {
            var table = document.CreateElement("table");
            table.SetAttribute("border", "1");
            table.SetAttribute("cellspacing", "0");
            table.SetAttribute("cellpadding", "8");
            table.SetAttribute("style", TableStyle);

            // 标记一下，避免后面的 TableTransform 把它当普通表格重刷样式。
            table.SetAttribute("data-omd", "code");

            var row = document.CreateElement("tr");
            var cell = document.CreateElement("td");
            cell.SetAttribute("style", CellStyle);

            var lines = SplitLines(text);
            var inner = string.Join("<br>", lines.Select(TransformHelper.PreserveSpaces));

            var code = document.CreateElement("span");
            code.SetAttribute("style", CodeStyle);
            code.InnerHtml = inner;

            cell.AppendChild(code);
            row.AppendChild(cell);
            table.AppendChild(row);

            return table;
        }

        private static IList<string> SplitLines(string text)
        {
            var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = new List<string>(normalized.Split('\n'));

            // 去掉围栏自带的尾部空行，避免代码块下面多出一行空白。
            while (lines.Count > 1 && lines[lines.Count - 1].Trim().Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            if (lines.Count == 0)
            {
                lines.Add(string.Empty);
            }

            return lines;
        }
    }
}
