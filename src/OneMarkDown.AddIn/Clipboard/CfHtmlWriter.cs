using System.Globalization;
using System.Text;

namespace OneMarkDown.Clipboard
{
    /// <summary>
    /// 构造 CF_HTML("HTML Format") 剪贴板载荷。
    /// 关键点：所有偏移量是整段字符串按 UTF-8 编码后的【字节】位置，不是字符数。
    /// 头部字段用 10 位定宽数字填充，保证占位计算与最终长度一致。
    /// </summary>
    public static class CfHtmlWriter
    {
        private const string Prefix =
            "<html><head><meta charset=\"utf-8\"></head><body>\r\n<!--StartFragment-->\r\n";

        private const string Suffix = "\r\n<!--EndFragment-->\r\n</body></html>";

        public static string Build(string fragment)
        {
            if (fragment == null)
            {
                fragment = string.Empty;
            }

            // 先用 0 占位求头部自身的字节长度（定宽 10 位，长度与最终值无关）。
            var probe = BuildHeader(0, 0, 0, 0);
            var startHtml = Encoding.UTF8.GetByteCount(probe);
            var startFragment = startHtml + Encoding.UTF8.GetByteCount(Prefix);
            var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
            var endHtml = endFragment + Encoding.UTF8.GetByteCount(Suffix);

            var header = BuildHeader(startHtml, endHtml, startFragment, endFragment);
            return header + Prefix + fragment + Suffix;
        }

        private static string BuildHeader(int startHtml, int endHtml, int startFragment, int endFragment)
        {
            var builder = new StringBuilder(160);
            builder.Append("Version:0.9\r\n");
            builder.Append("StartHTML:").Append(Format(startHtml)).Append("\r\n");
            builder.Append("EndHTML:").Append(Format(endHtml)).Append("\r\n");
            builder.Append("StartFragment:").Append(Format(startFragment)).Append("\r\n");
            builder.Append("EndFragment:").Append(Format(endFragment)).Append("\r\n");
            builder.Append("SourceURL:about:blank\r\n");
            return builder.ToString();
        }

        private static string Format(int value)
        {
            return value.ToString("D10", CultureInfo.InvariantCulture);
        }
    }
}
