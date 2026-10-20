using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using OneMarkDown.Diagnostics;
using OneMarkDown.Markdown.Transforms;

namespace OneMarkDown.Markdown
{
    public sealed class RenderResult
    {
        public RenderResult(string html, string plainText, IList<string> warnings, int elementCount, long elapsedMilliseconds)
        {
            Html = html;
            PlainText = plainText;
            Warnings = warnings;
            ElementCount = elementCount;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public string Html { get; private set; }

        public string PlainText { get; private set; }

        public IList<string> Warnings { get; private set; }

        public int ElementCount { get; private set; }

        public long ElapsedMilliseconds { get; private set; }
    }

    /// <summary>
    /// Markdown -> OneNote 能正确吃下的 HTML 片段。
    /// 采用 Markdig 解析 + AngleSharp 做 DOM 变换，避免对 HTML 做正则替换（转义、嵌套都会出错）。
    /// </summary>
    public static class MarkdownRenderer
    {
        private static readonly Lazy<MarkdownPipeline> Pipeline = new Lazy<MarkdownPipeline>(BuildPipeline);

        public static RenderResult Render(string markdown, string baseDirectory)
        {
            var stopwatch = Stopwatch.StartNew();

            // 注意：本命名空间就叫 OneMarkDown.Markdown，会遮蔽 Markdig.Markdown，必须写全名。
            var rawHtml = Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline.Value);

            var parser = new HtmlParser();
            var document = parser.ParseDocument(rawHtml);
            var body = document.Body;

            var warnings = new List<string>();

            if (body == null)
            {
                return new RenderResult(string.Empty, string.Empty, warnings, 0, stopwatch.ElapsedMilliseconds);
            }

            TaskListTransform.Apply(document);
            CodeBlockTransform.Apply(document);
            TableTransform.Apply(document);
            BlockTransform.Apply(document);
            InlineTransform.Apply(document);

            var imageWarnings = Images.ImageEmbedder.Embed(document, baseDirectory);
            foreach (var warning in imageWarnings)
            {
                warnings.Add(warning);
            }

            CleanupTransform.Apply(document);

            var html = body.InnerHtml;
            var plainText = NormalizePlainText(body.TextContent);
            var elementCount = body.QuerySelectorAll("*").Length;

            stopwatch.Stop();

            Log.Info("渲染完成: 输入 " + (markdown == null ? 0 : markdown.Length) + " 字符, 输出 " +
                     html.Length + " 字符, 元素 " + elementCount + ", 耗时 " + stopwatch.ElapsedMilliseconds + "ms, 图片告警 " + warnings.Count);

            return new RenderResult(html, plainText, warnings, elementCount, stopwatch.ElapsedMilliseconds);
        }

        private static MarkdownPipeline BuildPipeline()
        {
            return new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
        }

        private static string NormalizePlainText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var lines = text.Replace("\r\n", "\n").Split('\n');
            return string.Join(Environment.NewLine, lines.Select(line => line.TrimEnd()));
        }
    }
}
