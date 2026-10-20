using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace OneMarkDown.Diagnostics
{
    /// <summary>
    /// 汇总排查信息，供「诊断信息」按钮一键复制。
    /// </summary>
    public static class DiagnosticsReport
    {
        public static string Build()
        {
            var builder = new StringBuilder();

            try
            {
                var assembly = typeof(DiagnosticsReport).Assembly;
                var version = assembly.GetName().Version;

                builder.AppendLine("OneMarkDown 诊断信息");
                builder.AppendLine("------------------------------");
                builder.AppendLine("加载项版本: " + version);
                builder.AppendLine("程序集路径: " + SafeGetLocation(assembly));
                builder.AppendLine("进程位数: " + (IntPtr.Size * 8) + " 位");
                builder.AppendLine("Windows: " + Environment.OSVersion);
                builder.AppendLine("CLR: " + Environment.Version);
                builder.AppendLine("OneNote 路径: " + SafeGetHostPath());
                builder.AppendLine("OneNote 版本: " + SafeGetHostVersion());
                builder.AppendLine("注册项 LoadBehavior: " + SafeGetLoadBehavior());
                builder.AppendLine("当前用户: " + Environment.UserName);
                builder.AppendLine("日志目录: " + Log.DirectoryPath);

                if (!string.IsNullOrEmpty(Log.LastError))
                {
                    builder.AppendLine();
                    builder.AppendLine("本次会话最后一次错误:");
                    builder.AppendLine(Log.LastError);
                }

                builder.AppendLine();
                builder.AppendLine("日志尾部:");
                builder.AppendLine(SafeTailLog());

                return builder.ToString();
            }
            catch (Exception exception)
            {
                return "生成诊断信息失败: " + exception.Message;
            }
        }

        private static string SafeGetLocation(System.Reflection.Assembly assembly)
        {
            try
            {
                return assembly.Location;
            }
            catch
            {
                return "(未知)";
            }
        }

        private static string SafeGetHostPath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule.FileName;
            }
            catch
            {
                return "(未知)";
            }
        }

        private static string SafeGetHostVersion()
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule.FileName;
                return FileVersionInfo.GetVersionInfo(path).FileVersion;
            }
            catch
            {
                return "(未知)";
            }
        }

        private static string SafeGetLoadBehavior()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Office\OneNote\Addins\OneMarkDown.Connect"))
                {
                    if (key == null)
                    {
                        return "(未找到，可能未安装或安装到了其他用户)";
                    }

                    var value = key.GetValue("LoadBehavior");
                    return value == null ? "(未设置)" : value.ToString();
                }
            }
            catch (Exception exception)
            {
                return "(读取失败: " + exception.Message + ")";
            }
        }

        private static string SafeTailLog()
        {
            try
            {
                if (!File.Exists(Log.FilePath))
                {
                    return "(无日志文件)";
                }

                var lines = File.ReadAllLines(Log.FilePath);
                var start = Math.Max(0, lines.Length - 30);
                var builder = new StringBuilder();
                for (var index = start; index < lines.Length; index++)
                {
                    builder.AppendLine(lines[index]);
                }

                return builder.ToString();
            }
            catch (Exception exception)
            {
                return "(读取日志失败: " + exception.Message + ")";
            }
        }
    }
}
