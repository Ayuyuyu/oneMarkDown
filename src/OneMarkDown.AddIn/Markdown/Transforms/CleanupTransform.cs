using System.Linq;
using AngleSharp.Dom;

namespace OneMarkDown.Markdown.Transforms
{
    /// <summary>
    /// 收尾清理：OneNote 不认 class，留着只会让剪贴板载荷变大。
    /// 顺便兜底移除脚本/样式等危险节点。
    /// </summary>
    internal static class CleanupTransform
    {
        internal static void Apply(IDocument document)
        {
            foreach (var element in document.QuerySelectorAll("[class]").ToList())
            {
                element.RemoveAttribute("class");
            }

            foreach (var element in document.QuerySelectorAll("[data-omd]").ToList())
            {
                element.RemoveAttribute("data-omd");
            }

            foreach (var element in document.QuerySelectorAll("script, style, input, textarea, select").ToList())
            {
                var parent = element.Parent;
                if (parent != null)
                {
                    parent.RemoveChild(element);
                }
            }
        }
    }
}
