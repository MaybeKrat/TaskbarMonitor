using System;
using Microsoft.Win32;

namespace TaskbarMonitor;

/// <summary>
/// Автозапуск вместе с Windows — значение TaskbarMonitor в HKCU\...\Run. Учитывает отключение
/// в Диспетчере задач: он не удаляет значение, а ставит отдельную отметку в StartupApproved.
/// </summary>
internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string Name = "TaskbarMonitor";

    public static bool Enabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(Name) == null) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(Name) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;   // нечётный байт — отключён
        }
        set
        {
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
                approved?.DeleteValue(Name, throwOnMissingValue: false);
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) run.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else run.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
