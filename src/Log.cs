using System;
using System.IO;

namespace TaskbarMonitor;

/// <summary>Пишет ошибки в error.log рядом с exe; одна и та же ошибка подряд пишется один раз.</summary>
internal static class Log
{
    static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "error.log");
    static readonly object Sync = new();
    static string? _last;

    public static void Error(Exception e)
    {
        lock (Sync)
        {
            string text = e.ToString();
            if (text == _last) return;
            _last = text;
            try { File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}{Environment.NewLine}"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
