using System.Linq;
using AngleSharp.Dom;

namespace OneMarkDown.Markdown.Transforms
{
    /// <summary>
    /// OneNote 的表格导入必须带显式 border / cellpadding 属性，否则边框会整个丢掉。
    /// 表头额外补 bgcolor，兼容不认 style 的导入路径。
    /// </summary>
    internal static class TableTransform
    {
        private const string TableStyle = "border-collapse:collapse;border-color:#BFBFBF";

        private const string HeaderStyle =
            "border:1px solid #BFBFBF;background-color:#F2F2F2;font-weight:bold;padding:6px";

        private const string CellStyle = "border:1px solid #BFBFBF;padding:6px";

        internal static void Apply(IDocument document)
        {
            var tables = document.QuerySelectorAll("table").ToList();
            foreach (var table in tables)
            {
                // 代码块也用了表格结构，不能按普通表格重刷样式。
                if (table.HasAttribute("data-omd"))
                {
                    continue;
                }

                table.SetAttribute("border", "1");
                table.SetAttribute("cellspacing", "0");
                table.SetAttribute("cellpadding", "6");
                table.SetAttribute("style", TableStyle);

                foreach (var header in table.QuerySelectorAll("th"))
                {
                    header.SetAttribute("style", HeaderStyle);
                    header.SetAttribute("bgcolor", "#F2F2F2");
                }

                foreach (var cell in table.QuerySelectorAll("td"))
                {
                    cell.SetAttribute("style", CellStyle);
                }
            }
        }
    }
}
