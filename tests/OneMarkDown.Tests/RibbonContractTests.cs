using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Xunit;

namespace OneMarkDown.Tests
{
    /// <summary>
    /// Ribbon 契约测试。回调名写错、GUID 与安装脚本不一致这类问题，
    /// 在 OneNote 里只会表现为"点了没反应"，必须靠测试提前拦住。
    /// </summary>
    public class RibbonContractTests
    {
        private const string ExpectedGuid = "7C4E1B2A-9F3D-4E58-9A61-2B8D5C0F7E31";

        private static XDocument LoadRibbon()
        {
            var assembly = typeof(OneMarkDown.Connect).Assembly;
            using (var stream = assembly.GetManifestResourceStream("OneMarkDown.Ribbon.xml"))
            {
                Assert.NotNull(stream);
                using (var reader = new StreamReader(stream))
                {
                    return XDocument.Parse(reader.ReadToEnd());
                }
            }
        }

        [Fact]
        public void RibbonResource_IsEmbedded()
        {
            var ribbon = LoadRibbon();
            Assert.Equal("customUI", ribbon.Root.Name.LocalName);
        }

        [Fact]
        public void RibbonXml_HasCustomMarkdownTab()
        {
            var ribbon = LoadRibbon();
            var tab = ribbon.Descendants().FirstOrDefault(e => e.Name.LocalName == "tab");
            Assert.NotNull(tab);
            Assert.Equal("Markdown", tab.Attribute("label").Value);
        }

        [Fact]
        public void EveryCallback_MapsToPublicMethodOnConnect()
        {
            var ribbon = LoadRibbon();
            var connectType = typeof(OneMarkDown.Connect);
            var callbacks = ribbon.Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute => attribute.Name.LocalName == "onAction" || attribute.Name.LocalName == "onLoad")
                .Select(attribute => attribute.Value)
                .Distinct()
                .ToList();

            Assert.NotEmpty(callbacks);

            foreach (var callback in callbacks)
            {
                var method = connectType.GetMethod(callback, BindingFlags.Public | BindingFlags.Instance);
                Assert.True(method != null, "Ribbon.xml 引用了 Connect 上没有的公开方法: " + callback);
            }
        }

        [Fact]
        public void ConnectGuid_MatchesInstaller()
        {
            var attribute = (GuidAttribute)typeof(OneMarkDown.Connect)
                .GetCustomAttributes(typeof(GuidAttribute), false)
                .FirstOrDefault();

            Assert.NotNull(attribute);
            Assert.Equal(ExpectedGuid, attribute.Value, ignoreCase: true);
            Assert.Equal(ExpectedGuid, OneMarkDown.Connect.AddInGuid, ignoreCase: true);
        }

        [Fact]
        public void Connect_IsComVisibleWithStableProgId()
        {
            var type = typeof(OneMarkDown.Connect);
            Assert.True(type.IsPublic);

            var visible = type.GetCustomAttributes(typeof(ComVisibleAttribute), false)
                .Cast<ComVisibleAttribute>()
                .FirstOrDefault();
            Assert.NotNull(visible);
            Assert.True(visible.Value);

            var progId = (ProgIdAttribute)type.GetCustomAttributes(typeof(ProgIdAttribute), false)
                .FirstOrDefault();
            Assert.NotNull(progId);
            Assert.Equal("OneMarkDown.Connect", progId.Value);
        }

        [Fact]
        public void Connect_ImplementsBothRequiredInterfaces()
        {
            var type = typeof(OneMarkDown.Connect);
            Assert.True(typeof(OneMarkDown.Interop.IDTExtensibility2).IsAssignableFrom(type));
            Assert.True(typeof(Microsoft.Office.Core.IRibbonExtensibility).IsAssignableFrom(type));
        }
    }
}
