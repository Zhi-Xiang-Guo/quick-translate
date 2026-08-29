using System;
using System.IO;
using System.Text;

namespace QuickTranslate
{
    internal static class DiagnosticLog
    {
        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string LogPath = Path.Combine(LogDirectory, "quicktranslate.log");
        private const long MaximumLogBytes = 512 * 1024;

        public static void Write(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaximumLogBytes)
                    {
                        File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine;
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
