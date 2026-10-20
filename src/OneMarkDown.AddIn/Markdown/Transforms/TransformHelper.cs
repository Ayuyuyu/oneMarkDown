using System.Text;
using AngleSharp.Dom;

namespace OneMarkDown.Markdown.Transforms
{
    internal static class TransformHelper
    {
        /// <summary>把纯文本转成 HTML 安全的文本（只处理三个必须转义的字符）。</summary>
        internal static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Length + 16);
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '&':
                        builder.Append("&amp;");
                        break;
                    case '<':
                        builder.Append("&lt;");
                        break;
                    case '>':
                        builder.Append("&gt;");
                        break;
                    default:
                        builder.Append(ch);
                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 代码行的空白处理策略：
        /// 行首缩进全部换成 nbsp（否则 OneNote 折叠空格，缩进全丢）；
        /// 行内只保留连续 2 个以上的空格（对齐用），单个空格留成普通空格，
        /// 这样长代码行还能正常折行，复制回文本时也不会到处是 nbsp。
        /// 空行给一个 nbsp，否则行高会塌。
        /// </summary>
        internal static string PreserveSpaces(string line)
        {
            if (line == null || line.Length == 0)
            {
                return "&nbsp;";
            }

            var builder = new StringBuilder(line.Length + 8);
            var index = 0;

            while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
            {
                builder.Append(line[index] == '\t' ? "&nbsp;&nbsp;&nbsp;&nbsp;" : "&nbsp;");
                index++;
            }

            var pendingSpaces = 0;
            for (; index < line.Length; index++)
            {
                var ch = line[index];
                if (ch == ' ')
                {
                    pendingSpaces++;
                    continue;
                }

                if (ch == '\t')
                {
                    pendingSpaces += 4;
                    continue;
                }

                FlushSpaces(builder, ref pendingSpaces);
                builder.Append(Escape(ch.ToString()));
            }

            return builder.ToString();
        }

        private static void FlushSpaces(StringBuilder builder, ref int count)
        {
            if (count == 1)
            {
                builder.Append(' ');
            }
            else if (count > 1)
            {
                for (var index = 0; index < count; index++)
                {
                    builder.Append("&nbsp;");
                }
            }

            count = 0;
        }

        internal static IElement CreateElement(IDocument document, string tagName, string innerHtml = null)
        {
            var element = document.CreateElement(tagName);
            if (innerHtml != null)
            {
                element.InnerHtml = innerHtml;
            }

            return element;
        }

        internal static void Replace(IElement target, IElement replacement)
        {
            var parent = target.Parent;
            if (parent == null)
            {
                return;
            }

            parent.InsertBefore(replacement, target);
            parent.RemoveChild(target);
        }
    }
}
