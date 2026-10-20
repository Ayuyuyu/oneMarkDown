using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace OneMarkDown.Diagnostics
{
    /// <summary>
    /// 轻量滚动文件日志。只记录长度、计数、耗时与异常类型，绝不写入笔记正文。
    /// 注意：初始化过程中只能用私有字段，不能访问 FilePath/DirectoryPath，
    /// 否则属性会回调 EnsureInitialized，而 lock 是可重入的，会递归到栈溢出。
    /// </summary>
    public static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private const int MaxFiles = 3;

        private static readonly object Gate = new object();
        private static bool _initialized;
        private static string _directory;
        private static string _filePath;

        public static string DirectoryPath
        {
            get
            {
                EnsureInitialized();
                return _directory;
            }
        }

        public static string FilePath
        {
            get
            {
                EnsureInitialized();
                return _filePath;
            }
        }

        public static string LastError { get; private set; }

        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (Gate)
            {
                if (_initialized)
                {
                    return;
                }

                string directory;
                try
                {
                    directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OneMarkDown",
                        "logs");
                    System.IO.Directory.CreateDirectory(directory);
                }
                catch
                {
                    directory = Path.GetTempPath();
                }

                _directory = directory;
                _filePath = Path.Combine(directory, "addin.log");

                try
                {
                    if (!File.Exists(_filePath))
                    {
                        using (var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        {
                            var banner = Encoding.UTF8.GetBytes(
                                "==== OneMarkDown " + typeof(Log).Assembly.GetName().Version + " " +
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " ====" +
                                Environment.NewLine);
                            stream.Write(banner, 0, banner.Length);
                        }
                    }
                }
                catch
                {
                }

                _initialized = true;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            LastError = message + (exception == null ? string.Empty : " | " + exception.GetType().Name + ": " + exception.Message);
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                EnsureInitialized();
                RotateIfNeeded();

                var builder = new StringBuilder(256);
                builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                builder.Append(" [").Append(level).Append("] ");
                builder.Append(message);

                if (exception != null)
                {
                    builder.AppendLine();
                    builder.Append("    ").Append(exception.GetType().FullName).Append(": ").Append(exception.Message);
                    if (exception.StackTrace != null)
                    {
                        builder.AppendLine();
                        builder.Append(exception.StackTrace);
                    }
                }

                builder.AppendLine();

                lock (Gate)
                {
                    File.AppendAllText(_filePath, builder.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败绝不能影响宿主。
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var info = new FileInfo(_filePath);
                if (!info.Exists || info.Length < MaxBytes)
                {
                    return;
                }

                for (var index = MaxFiles - 1; index >= 1; index--)
                {
                    var source = _filePath + "." + index;
                    var destination = _filePath + "." + (index + 1);
                    if (File.Exists(source))
                    {
                        if (File.Exists(destination))
                        {
                            File.Delete(destination);
                        }

                        File.Move(source, destination);
                    }
                }

                if (File.Exists(_filePath + ".1"))
                {
                    File.Delete(_filePath + ".1");
                }

                File.Move(_filePath, _filePath + ".1");
            }
            catch
            {
            }
        }
    }
}
