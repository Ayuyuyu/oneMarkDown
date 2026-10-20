using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using AngleSharp.Dom;
using OneMarkDown.Diagnostics;
using OneMarkDown.Markdown.Transforms;

namespace OneMarkDown.Markdown.Images
{
    /// <summary>
    /// 把 &lt;img&gt; 全部换成本地图内嵌的 data URI。
    /// OneNote 从剪贴板导入时不会去下载任何外部资源，所以必须自带数据。
    /// 三级降级：data URI -> 缺失/超限说明文本（首版不做临时文件引用，避免留下垃圾文件）。
    /// </summary>
    internal static class ImageEmbedder
    {
        private const long MaxImageBytes = 4L * 1024 * 1024;
        private const int MaxDisplayWidth = 720;

        internal static IList<string> Embed(IDocument document, string baseDirectory)
        {
            var warnings = new List<string>();
            var images = document.QuerySelectorAll("img").ToList();

            foreach (var image in images)
            {
                var source = image.GetAttribute("src") ?? string.Empty;
                var alt = image.GetAttribute("alt") ?? string.Empty;

                if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    ReplaceWithNote(document, image, "[网络图片: " + SafeName(source, alt) + "]");
                    warnings.Add("网络图片未下载（首版不联网）: " + source);
                    continue;
                }

                var path = ImageResolver.TryResolveLocalPath(source, baseDirectory);
                if (path == null || !File.Exists(path))
                {
                    var hint = baseDirectory == null
                        ? "（从剪贴板粘贴时无法解析相对路径，请改用「从 .md 文件插入」）"
                        : "（文件不存在）";
                    ReplaceWithNote(document, image, "[图片缺失: " + SafeName(source, alt) + "]" + hint);
                    warnings.Add("图片未找到: " + source + "，基准目录: " + (baseDirectory ?? "<无>"));
                    continue;
                }

                try
                {
                    var bytes = File.ReadAllBytes(path);
                    if (bytes.Length > MaxImageBytes)
                    {
                        ReplaceWithNote(document, image, "[图片过大已跳过: " + SafeName(source, alt) + "]");
                        warnings.Add("图片超过 4MB 已跳过: " + path);
                        continue;
                    }

                    var mimeType = ImageResolver.DetectMimeType(bytes);
                    if (mimeType == null)
                    {
                        ReplaceWithNote(document, image, "[不支持的图片格式: " + SafeName(source, alt) + "]");
                        warnings.Add("无法识别图片格式: " + path);
                        continue;
                    }

                    image.SetAttribute("src", ImageResolver.ToDataUri(mimeType, bytes));
                    ApplyDisplaySize(image, bytes, mimeType);
                }
                catch (Exception exception)
                {
                    ReplaceWithNote(document, image, "[图片读取失败: " + SafeName(source, alt) + "]");
                    Log.Error("读取图片失败: " + path, exception);
                }
            }

            return warnings;
        }

        private static void ApplyDisplaySize(IElement image, byte[] bytes, string mimeType)
        {
            if (string.Equals(mimeType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                using (var stream = new MemoryStream(bytes))
                using (var bitmap = Image.FromStream(stream))
                {
                    var width = bitmap.Width;
                    var height = bitmap.Height;

                    if (width <= 0 || height <= 0)
                    {
                        return;
                    }

                    if (width > MaxDisplayWidth)
                    {
                        height = (int)Math.Round(height * (double)MaxDisplayWidth / width);
                        width = MaxDisplayWidth;
                    }

                    image.SetAttribute("width", width.ToString());
                    image.SetAttribute("height", height.ToString());
                }
            }
            catch
            {
                // 尺寸算不出来无所谓，OneNote 会按原始像素铺开。
            }
        }

        private static void ReplaceWithNote(IDocument document, IElement image, string text)
        {
            var span = TransformHelper.CreateElement(document, "span", TransformHelper.Escape(text));
            span.SetAttribute("style", "color:#B31D28");
            TransformHelper.Replace(image, span);
        }

        private static string SafeName(string source, string alt)
        {
            var shortSource = source.Length <= 80 ? source : source.Substring(0, 80) + "...";

            if (string.IsNullOrWhiteSpace(alt) || string.Equals(alt, source, StringComparison.Ordinal))
            {
                return shortSource;
            }

            // alt 和文件名都留下，方便用户知道到底是哪一张图没处理。
            return alt + " / " + shortSource;
        }
    }
}
