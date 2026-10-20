using System.Linq;
using AngleSharp.Dom;

namespace OneMarkDown.Markdown.Transforms
{
    /// <summary>
    /// OneNote 不认 HTML 复选框，任务列表一律降级成 ☐ / ☑ 字符。
    /// </summary>
    internal static class TaskListTransform
    {
        // 不带尾随空格：Markdown 原文里 `- [ ] xxx` 的空格会被 Markdig 保留成文本节点。
        private const string UncheckedGlyph = "\u2610";
        private const string CheckedGlyph = "\u2611";

        internal static void Apply(IDocument document)
        {
            var inputs = document.QuerySelectorAll("input").ToList();
            foreach (var input in inputs)
            {
                var type = input.GetAttribute("type");
                if (!string.Equals(type, "checkbox", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var glyph = input.HasAttribute("checked") ? CheckedGlyph : UncheckedGlyph;
                var parent = input.Parent;
                if (parent == null)
                {
                    continue;
                }

                var textNode = document.CreateTextNode(glyph);
                parent.InsertBefore(textNode, input);
                parent.RemoveChild(input);
            }
        }
    }
}
