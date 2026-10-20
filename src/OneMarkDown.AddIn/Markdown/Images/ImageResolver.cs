using System;
using System.IO;

namespace OneMarkDown.Markdown.Images
{
    internal static class ImageResolver
    {
        /// <summary>
        /// 把 Markdown 里的图片地址解析成本地绝对路径。解析不出来返回 null。
        /// </summary>
        internal static string TryResolveLocalPath(string source, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            var candidate = source.Trim();

            if (candidate.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                Uri uri;
                if (Uri.TryCreate(candidate, UriKind.Absolute, out uri) && uri.IsFile)
                {
                    return uri.LocalPath;
                }

                return null;
            }

            if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // 首版不联网，网络图片一律降级成链接文本。
                return null;
            }

            try
            {
                candidate = Uri.UnescapeDataString(candidate.Replace('/', Path.DirectorySeparatorChar));
            }
            catch
            {
                // 解码失败就用原样路径。
            }

            if (Path.IsPathRooted(candidate))
            {
                return candidate;
            }

            if (string.IsNullOrEmpty(baseDirectory))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, candidate));
        }

        internal static string DetectMimeType(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
            {
                return null;
            }

            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                return "image/png";
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return "image/jpeg";
            }

            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            {
                return "image/gif";
            }

            if (bytes[0] == 0x42 && bytes[1] == 0x4D)
            {
                return "image/bmp";
            }

            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            {
                return "image/webp";
            }

            var head = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(256, bytes.Length));
            if (head.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "image/svg+xml";
            }

            return null;
        }

        internal static string ToDataUri(string mimeType, byte[] bytes)
        {
            return "data:" + mimeType + ";base64," + Convert.ToBase64String(bytes);
        }
    }
}
