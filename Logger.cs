using System;
using System.IO;

namespace BGSnowballEngine
{
    /// <summary>
    /// Простой файловый логгер плагина.
    /// Пишет в %AppData%\BGSnowballEngine\logs\BGSnowballEngine_yyyyMMdd.log.
    /// Логирование никогда не должно ронять плагин, поэтому внутренние ошибки глушатся.
    /// </summary>
    public static class Logger
    {
        private static readonly object SyncRoot = new object();
        private static string _logDir;

        private static string LogDir
        {
            get
            {
                if (_logDir == null)
                {
                    _logDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "BGSnowballEngine",
                        "logs");
                    Directory.CreateDirectory(_logDir);
                }
                return _logDir;
            }
        }

        private static string LogFile => Path.Combine(LogDir, $"BGSnowballEngine_{DateTime.Now:yyyyMMdd}.log");

        public static void Log(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // намеренно пусто: логгер не должен ронять плагин
            }
        }

        public static void Log(Exception ex)
        {
            Log(ex?.ToString() ?? "Unknown exception");
        }
    }
}
