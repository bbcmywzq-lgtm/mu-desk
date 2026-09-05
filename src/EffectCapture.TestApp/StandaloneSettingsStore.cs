using System.IO;
using System.Text.Json;
using Toolbox.Core;

namespace EffectCapture.TestApp;

public sealed class StandaloneSettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MU Dynamic Capture Test",
        "settings.json");

    public EffectCaptureSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var settings = JsonSerializer.Deserialize<EffectCaptureSettings>(File.ReadAllText(_path), JsonOptions)
                    ?? new EffectCaptureSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new EffectCaptureSettings();
    }

    public bool Save(EffectCaptureSettings settings)
    {
        try
        {
            settings.Normalize();
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
