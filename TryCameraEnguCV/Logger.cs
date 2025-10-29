using System;
using System.IO;
using System.Threading;

namespace TryCameraEnguCV
{
    public static class Logger
    {
        private static readonly string _logDir;
        private static readonly string _logFile;
        private static readonly object _lock = new();

        static Logger()
        {
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BUSV", "Logs");

            Directory.CreateDirectory(_logDir);
            _logFile = Path.Combine(_logDir, "camera_log.txt");
        }

        public static void Write(string message)
        {
            try
            {
                if (new FileInfo(_logFile).Length > 5_000_000) // 5 МБ
                    File.WriteAllText(_logFile, ""); // очистить лог


                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}";
                lock (_lock)
                {
                    File.AppendAllText(_logFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // не выбрасываем исключения — логирование не должно ломать приложение
            }
        }
    }
}
