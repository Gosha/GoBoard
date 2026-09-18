using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoBoard.Core;

internal enum KeySound { CushionedWood, SoftLowThud }

internal sealed record BoardSettings
{
    public int SizePercent { get; init; } = 100;
    public bool SoundEnabled { get; init; } = true;
    public int VolumePercent { get; init; } = 100;
    public KeySound Sound { get; init; } = KeySound.CushionedWood;
    public float Scale => SizePercent / 100f;

    public BoardSettings Normalize() => this with
    {
        SizePercent = Math.Clamp(SizePercent, 50, 150),
        VolumePercent = Math.Clamp(VolumePercent, 0, 100),
        Sound = Enum.IsDefined(Sound) ? Sound : KeySound.CushionedWood
    };

    public static BoardSettings Defaults => new()
    {
        Sound = string.Equals(Environment.GetEnvironmentVariable("GOBOARD_KEY_SOUND"),
            "soft-low-thud", StringComparison.OrdinalIgnoreCase) ? KeySound.SoftLowThud : KeySound.CushionedWood
    };
}

// Each editor changes only its selected field against the latest saved values.
// A per-file mutex and atomic rename keep desktop and VR edits from losing data.
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<KeySound>() },
        IgnoreReadOnlyProperties = true
    };
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoBoard", "settings.json");
    public string FilePath { get; }
    public BoardSettings Current { get; private set; } = BoardSettings.Defaults;
    public string Error { get; private set; }

    public SettingsStore(string path = null)
    {
        FilePath = Path.GetFullPath(path ?? DefaultPath);
        Reload();
    }

    public bool Reload()
    {
        try
        {
            var next = Read();
            var changed = next != Current;
            Current = next;
            Error = null;
            return changed;
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            Error = "Cannot read settings. Last working values are still in use. " + ex.Message;
            return false;
        }
    }

    public bool Update(Func<BoardSettings, BoardSettings> change)
    {
        var name = "Local\\GoBoard.Settings." + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(FilePath.ToUpperInvariant())));
        using var gate = new Mutex(false, name);
        var held = false;
        string temporary = null;
        try
        {
            try { held = gate.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("Another settings editor is busy. Try again.");
            var next = change(Read()).Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, Json));
            File.Move(temporary, FilePath, overwrite: true);
            Current = next;
            Error = null;
            return true;
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            Error = "Settings were not saved. " + ex.Message;
            return false;
        }
        finally
        {
            if (temporary != null)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (held) gate.ReleaseMutex();
        }
    }

    private BoardSettings Read()
    {
        try
        {
            using var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return (JsonSerializer.Deserialize<BoardSettings>(file, Json)
                ?? throw new JsonException("The settings file is empty.")).Normalize();
        }
        catch (FileNotFoundException) { return BoardSettings.Defaults; }
        catch (DirectoryNotFoundException) { return BoardSettings.Defaults; }
    }

    private static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException;
}
