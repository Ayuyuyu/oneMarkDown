using OneMarkDown.Markdown;
using Xunit;

namespace OneMarkDown.Tests
{
    public class PostProcessorTests
    {
        private const string Sample =
            "# 一级标题\n\n" +
            "正文包含 **加粗**、*斜体* 和 `inline code`，还有 [链接](https://example.com)。\n\n" +
            "## 二级标题\n\n" +
            "- 第一项\n" +
            "- 第二项\n" +
            "  - 嵌套项\n\n" +
            "- [x] 已完成\n" +
            "- [ ] 未完成\n\n" +
            "> 引用一段话\n\n" +
            "```csharp\n" +
            "public void Foo()\n" +
            "{\n" +
            "    var x = 1;\n" +
            "}\n" +
            "```\n\n" +
            "| 列 A | 列 B |\n" +
            "| --- | --- |\n" +
            "| 1 | 2 |\n\n" +
            "---\n";

        private static RenderResult RenderSample()
        {
            return MarkdownRenderer.Render(Sample, null);
        }

        [Fact]
        public void Headings_GetInlineStyles()
        {
            var result = RenderSample();
            Assert.Contains("font-size:20pt", result.Html);
            Assert.Contains("font-size:16pt", result.Html);
        }

        [Fact]
        public void CodeBlock_BecomesSingleCellTable()
        {
            var result = RenderSample();

            Assert.DoesNotContain("<pre", result.Html);
            Assert.Contains("Consolas", result.Html);
            Assert.Contains("#F6F8FA", result.Html);

            // 缩进必须靠 nbsp 保留，否则 OneNote 会把空格折叠掉。
            Assert.Contains("&nbsp;", result.Html);
        }

        [Fact]
        public void TaskList_UsesGlyphs()
        {
            var result = RenderSample();
            Assert.Contains("\u2611", result.Html);
            Assert.Contains("\u2610", result.Html);
            Assert.DoesNotContain("checkbox", result.Html);
        }

        [Fact]
        public void Table_GetsExplicitBorderAttributes()
        {
            var result = RenderSample();
            Assert.Contains("border=\"1\"", result.Html);
            Assert.Contains("cellpadding=\"6\"", result.Html);
            Assert.Contains("bgcolor=\"#F2F2F2\"", result.Html);
        }

        [Fact]
        public void ThematicBreak_BecomesTableRule()
        {
            var result = RenderSample();
            Assert.DoesNotContain("<hr", result.Html);
            Assert.Contains("border-bottom:1px solid #BFBFBF", result.Html);
        }

        [Fact]
        public void Output_HasNoClassAttributes()
        {
            var result = RenderSample();
            Assert.DoesNotContain("class=", result.Html);
        }

        [Fact]
        public void LinksAndInlineCode_AreStyled()
        {
            var result = RenderSample();
            Assert.Contains("color:#0B6BCB", result.Html);
            Assert.Contains("background-color:#F0F1F3", result.Html);
        }

        [Fact]
        public void PlainText_HasNoMarkup()
        {
            var result = RenderSample();
            Assert.DoesNotContain("<", result.PlainText);
            Assert.Contains("一级标题", result.PlainText);
        }
    }
}
