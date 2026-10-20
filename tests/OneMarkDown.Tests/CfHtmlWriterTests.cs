using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OneMarkDown.Clipboard;
using Xunit;

namespace OneMarkDown.Tests
{
    public class CfHtmlWriterTests
    {
        [Theory]
        [InlineData("<p>hello</p>")]
        [InlineData("<p>中文内容，含标点。</p>")]
        [InlineData("<p>emoji 😀👍🏽 和全角字符</p>")]
        public void Offsets_AreUtf8BytePositions(string fragment)
        {
            var payload = CfHtmlWriter.Build(fragment);
            var bytes = Encoding.UTF8.GetBytes(payload);

            var startHtml = ReadOffset(payload, "StartHTML");
            var endHtml = ReadOffset(payload, "EndHTML");
            var startFragment = ReadOffset(payload, "StartFragment");
            var endFragment = ReadOffset(payload, "EndFragment");

            Assert.True(startHtml > 0);
            Assert.Equal(bytes.Length, endHtml);
            Assert.True(startFragment > startHtml);

            // StartFragment 处必须正好是片段开头。
            var fragmentBytes = Encoding.UTF8.GetBytes(fragment);
            Assert.Equal(fragmentBytes.Length, endFragment - startFragment);

            var actual = new byte[fragmentBytes.Length];
            System.Array.Copy(bytes, startFragment, actual, 0, fragmentBytes.Length);
            Assert.Equal(fragment, Encoding.UTF8.GetString(actual));
        }

        [Fact]
        public void Header_HasFixedWidthOffsets_SoPlaceholderMathHolds()
        {
            var payload = CfHtmlWriter.Build("<p>x</p>");
            var lines = payload.Split(new[] { "\r\n" }, System.StringSplitOptions.None);

            Assert.Equal("Version:0.9", lines[0]);
            Assert.Matches("^StartHTML:\\d{10}$", lines[1]);
            Assert.Matches("^EndHTML:\\d{10}$", lines[2]);
            Assert.Matches("^StartFragment:\\d{10}$", lines[3]);
            Assert.Matches("^EndFragment:\\d{10}$", lines[4]);
            Assert.StartsWith("SourceURL:", lines[5]);
        }

        [Fact]
        public void Payload_ContainsFragmentMarkers()
        {
            var payload = CfHtmlWriter.Build("<p>x</p>");
            Assert.Contains("<!--StartFragment-->", payload);
            Assert.Contains("<!--EndFragment-->", payload);
            Assert.Contains("<p>x</p>", payload);
        }

        private static int ReadOffset(string payload, string name)
        {
            var match = Regex.Match(payload, name + @":(\d{10})");
            Assert.True(match.Success, "没有找到 " + name);
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }
}
