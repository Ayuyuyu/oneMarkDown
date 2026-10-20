using System.Linq;
using AngleSharp.Dom;

namespace OneMarkDown.Markdown.Transforms
{
    /// <summary>
    /// 行内代码与链接的视觉适配。放在代码块转换之后执行，
    /// 此时剩下的 &lt;code&gt; 一定是行内代码。
    /// </summary>
    internal static class InlineTransform
    {
        private const string CodeStyle =
            "font-family:Consolas,'Courier New',monospace;font-size:9.5pt;background-color:#F0F1F3;color:#B31D28";

        private const string LinkStyle = "color:#0B6BCB";

        internal static void Apply(IDocument document)
        {
            foreach (var code in document.QuerySelectorAll("code").ToList())
            {
                code.SetAttribute("style", CodeStyle);
            }

            foreach (var link in document.QuerySelectorAll("a").ToList())
            {
                if (!link.HasAttribute("href"))
                {
                    continue;
                }

                link.SetAttribute("style", LinkStyle);
            }
        }
    }
}
