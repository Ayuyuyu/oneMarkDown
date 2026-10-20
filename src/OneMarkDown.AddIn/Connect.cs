using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using OneMarkDown.Diagnostics;
using Microsoft.Office.Core;
using OneMarkDown.Interop;
using OneMarkDown.Ribbon;

namespace OneMarkDown
{
    /// <summary>
    /// OneNote COM 加载项入口。
    /// AutoDispatch 是必须的：Ribbon 回调（OnPasteMarkdown 等）由宿主按名字晚绑定调用，
    /// 只有 AutoDispatch 才会生成带 GetIDsOfNames 的 dispinterface。
    /// </summary>
    [ComVisible(true)]
    [Guid(AddInGuid)]
    [ProgId(ProgIdValue)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class Connect : IDTExtensibility2, IRibbonExtensibility
    {
        public const string AddInGuid = "7C4E1B2A-9F3D-4E58-9A61-2B8D5C0F7E31";
        public const string ProgIdValue = "OneMarkDown.Connect";

        private static string _ribbonXml;

        static Connect()
        {
            // 这段代码跑在 CoCreateInstance 内部（宿主正在创建本对象），是最不能出岔子的位置：
            // 任何文件 I/O、进程信息查询、日志写盘都可能让激活变慢，
            // 而 Office 对加载项激活是有超时判定的（超时即"加载项运行时错误"并把 LoadBehavior 改成 2）。
            // cscript 那种宿主对延迟完全不敏感，所以在那边测不出问题——这正是排查会被误导的地方。
            // 这里只留一个最轻的委托注册，其余全部挪到 OnConnection。
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromAddInDirectory;
            }
            catch
            {
            }
        }

        private static string SafeHostName()
        {
            try
            {
                return Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            }
            catch
            {
                return "(未知)";
            }
        }

        public string GetCustomUI(string ribbonId)
        {
            try
            {
                // 记录宿主实际传入的 ribbonId，是排查"选项卡不显示"的第一手线索。
                Log.Info("GetCustomUI(\"" + (ribbonId ?? "<null>") + "\")");

                if (string.IsNullOrEmpty(ribbonId))
                {
                    return string.Empty;
                }

                if (ribbonId.IndexOf("OneNote", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return string.Empty;
                }

                return GetRibbonXml();
            }
            catch (Exception exception)
            {
                Log.Error("GetCustomUI 失败", exception);
                return string.Empty;
            }
        }

        public void OnConnection(object application, ext_ConnectMode connectMode, object addInInstance, ref Array custom)
        {
            try
            {
                Log.EnsureInitialized();
                Log.Info("OnConnection: mode=" + connectMode +
                         ", host=" + SafeHostName() +
                         ", bitness=" + (IntPtr.Size * 8) +
                         ", log=" + Log.FilePath);
            }
            catch
            {
                // 连接阶段的异常会导致加载项被宿主禁用，这里必须全部吞掉。
            }
        }

        public void OnDisconnection(ext_DisconnectMode removeMode, ref Array custom)
        {
            try
            {
                Log.Info("OnDisconnection: mode=" + removeMode);
            }
            catch
            {
            }
        }

        public void OnAddInsUpdate(ref Array custom)
        {
        }

        public void OnStartupComplete(ref Array custom)
        {
            try
            {
                Log.Info("OnStartupComplete");
            }
            catch
            {
            }
        }

        public void OnBeginShutdown(ref Array custom)
        {
            try
            {
                Log.Info("OnBeginShutdown");
            }
            catch
            {
            }
        }

        // ---- Ribbon 回调：方法名必须与 Ribbon.xml 中一致 ----

        public void OnRibbonLoad(IRibbonUI ribbonUi)
        {
            RibbonController.Instance.OnRibbonLoad(ribbonUi);
        }

        public void OnPasteMarkdown(IRibbonControl control)
        {
            RibbonController.Instance.OnPasteMarkdown(control);
        }

        public void OnInsertFile(IRibbonControl control)
        {
            RibbonController.Instance.OnInsertFile(control);
        }

        public void OnHelp(IRibbonControl control)
        {
            RibbonController.Instance.OnHelp(control);
        }

        private static Assembly ResolveFromAddInDirectory(object sender, ResolveEventArgs args)
        {
            try
            {
                var simpleName = new AssemblyName(args.Name).Name;
                var directory = Path.GetDirectoryName(typeof(Connect).Assembly.Location);
                if (string.IsNullOrEmpty(directory))
                {
                    return null;
                }

                var candidate = Path.Combine(directory, simpleName + ".dll");
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
            }
            catch
            {
            }

            return null;
        }

        private static string GetRibbonXml()
        {
            if (_ribbonXml != null)
            {
                return _ribbonXml;
            }

            var assembly = typeof(Connect).Assembly;
            using (var stream = assembly.GetManifestResourceStream("OneMarkDown.Ribbon.xml"))
            {
                if (stream == null)
                {
                    var names = string.Join(", ", assembly.GetManifestResourceNames());
                    Log.Error("找不到内嵌资源 OneMarkDown.Ribbon.xml，现有资源: " + names, null);
                    return string.Empty;
                }

                using (var reader = new StreamReader(stream))
                {
                    _ribbonXml = reader.ReadToEnd();
                }
            }

            Log.Info("Ribbon XML 长度: " + _ribbonXml.Length);
            return _ribbonXml;
        }
    }
}
