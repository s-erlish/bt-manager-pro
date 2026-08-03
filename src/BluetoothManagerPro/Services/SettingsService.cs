using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BluetoothManagerPro.Models;

namespace BluetoothManagerPro.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON under %LOCALAPPDATA%.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public SettingsService()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothManagerPro");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // A corrupt settings file must never stop the app from starting.
            Debug.WriteLine($"Settings load failed: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            // Write beside the target and swap, so a crash mid-write cannot truncate the file.
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Settings save failed: {ex.Message}");
        }
    }
}
