using System;
using System.IO;
using OneMarkDown.Markdown;
using Xunit;

namespace OneMarkDown.Tests
{
    public class ImageEmbedderTests : IDisposable
    {
        // 1x1 透明 PNG
        private const string OnePixelPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        private readonly string _directory;

        public ImageEmbedderTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "OneMarkDown.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, true);
            }
            catch
            {
            }
        }

        [Fact]
        public void RelativeImage_IsInlinedAsDataUri()
        {
            File.WriteAllBytes(Path.Combine(_directory, "pixel.png"), Convert.FromBase64String(OnePixelPngBase64));

            var result = MarkdownRenderer.Render("![像素](pixel.png)", _directory);

            Assert.Contains("data:image/png;base64,", result.Html);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void MissingImage_DegradesToText()
        {
            var result = MarkdownRenderer.Render("![缺失](nope.png)", _directory);

            Assert.Contains("图片缺失", result.Html);
            Assert.Contains("nope.png", result.Html);
            Assert.NotEmpty(result.Warnings);
        }

        [Fact]
        public void RemoteImage_IsNotDownloaded()
        {
            var result = MarkdownRenderer.Render("![远程](https://example.com/a.png)", null);

            Assert.Contains("网络图片", result.Html);
            Assert.DoesNotContain("data:", result.Html);
            Assert.NotEmpty(result.Warnings);
        }

        [Fact]
        public void RelativeImageWithoutBaseDirectory_DegradesWithHint()
        {
            var result = MarkdownRenderer.Render("![图](pixel.png)", null);

            Assert.Contains("图片缺失", result.Html);
            Assert.Contains("从 .md 文件插入", result.Html);
        }
    }
}
