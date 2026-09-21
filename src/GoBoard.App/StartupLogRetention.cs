using System.Text.RegularExpressions;

namespace GoBoard.App;

internal static class StartupLogRetention
{
    private const int MaximumFiles = 20;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromDays(30);
    private static readonly Regex LogName = new(
        @"\Agoboard-[0-9]{8}-[0-9]{6}-[0-9]{7}-[0-9]+\.log\z",
        RegexOptions.CultureInvariant);

    internal static void Cleanup(string directory, DateTime utcNow)
    {
        try
        {
            // Only inspect our per-launch logs, never other files or subdirectories.
            var logs = new DirectoryInfo(directory).GetFiles("goboard-*.log")
                .Where(file => LogName.IsMatch(file.Name)
                    && (file.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
            var cutoff = utcNow - MaximumAge;
            for (var index = 0; index < logs.Length; index++)
            {
                var file = logs[index];
                if (index < MaximumFiles && file.LastWriteTimeUtc >= cutoff) continue;
                try
                {
                    // Active startup writers use FileShare.Read, so Windows refuses
                    // deletion until they close. Retry skipped files on the next launch.
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing/inaccessible logs must never prevent the keyboard starting.
        }
    }
}
