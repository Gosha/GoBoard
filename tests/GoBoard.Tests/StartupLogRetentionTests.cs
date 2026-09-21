using GoBoard.App;
using Xunit;

namespace GoBoard.Tests;

public sealed class StartupLogRetentionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GoBoard.LogRetention.Tests", Guid.NewGuid().ToString("N"));
    private readonly DateTime now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
    private int nextId;

    [Fact]
    public void RemovesLogsOlderThanThirtyDaysButKeepsBoundaryAndRecentLogs()
    {
        var expired = CreateLog(now.AddDays(-30).AddSeconds(-1));
        var boundary = CreateLog(now.AddDays(-30));
        var recent = CreateLog(now.AddDays(-1));

        StartupLogRetention.Cleanup(directory, now);

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(boundary));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void KeepsTwentyNewestLogsIncludingCurrentWriter()
    {
        var logs = Enumerable.Range(0, 25).Select(i => CreateLog(now.AddMinutes(-i))).ToArray();
        using var current = new FileStream(logs[0], FileMode.Open, FileAccess.Write, FileShare.Read);

        StartupLogRetention.Cleanup(directory, now);

        Assert.All(logs.Take(20), path => Assert.True(File.Exists(path)));
        Assert.All(logs.Skip(20), path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void SkipsActiveAndReadOnlyLogsAndContinuesDeletingOthers()
    {
        var active = CreateLog(now.AddDays(-31));
        var readOnly = CreateLog(now.AddDays(-32));
        var expired = CreateLog(now.AddDays(-33));
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        try
        {
            using (var writer = new FileStream(active, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                StartupLogRetention.Cleanup(directory, now);
                Assert.True(File.Exists(active));
                Assert.True(File.Exists(readOnly));
                Assert.False(File.Exists(expired));
                writer.WriteByte(42);
            }
        }
        finally { File.SetAttributes(readOnly, FileAttributes.Normal); }

        // Once the locks are gone, the next launch can remove the skipped logs.
        File.SetLastWriteTimeUtc(active, now.AddDays(-31));
        StartupLogRetention.Cleanup(directory, now);
        Assert.False(File.Exists(active));
        Assert.False(File.Exists(readOnly));
    }

    [Fact]
    public void PreservesUnrelatedFilesAndDoesNotRecurse()
    {
        var expired = CreateLog(now.AddDays(-31));
        var unrelated = new[] { "goboard.log", "goboard.error.log", "goboard-notes.log", "settings.json" }
            .Select(name => Path.Combine(directory, name)).ToArray();
        foreach (var path in unrelated)
        {
            File.WriteAllText(path, "keep");
            File.SetLastWriteTimeUtc(path, now.AddDays(-31));
        }
        var nested = Path.Combine(directory, "nested");
        Directory.CreateDirectory(nested);
        var nestedLog = Path.Combine(nested, Path.GetFileName(expired));
        File.Copy(expired, nestedLog);

        StartupLogRetention.Cleanup(directory, now);

        Assert.False(File.Exists(expired));
        Assert.All(unrelated.Append(nestedLog), path => Assert.Equal("keep", File.ReadAllText(path)));
    }

    [Fact]
    public void MissingDirectoryDoesNotFailOrCreateIt()
    {
        StartupLogRetention.Cleanup(directory, now);
        Assert.False(Directory.Exists(directory));
    }

    private string CreateLog(DateTime modified)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"goboard-{modified:yyyyMMdd-HHmmss-fffffff}-{++nextId}.log");
        File.WriteAllText(path, "keep");
        File.SetLastWriteTimeUtc(path, modified);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
