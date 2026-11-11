using System;
using System.IO;
using System.Text.Json;

namespace UsualAudioConverter;

public class AppConfig
{
    public string FfmpegPath { get; set; } = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    public string ExhalePath { get; set; } = OperatingSystem.IsWindows() ? "exhale.exe" : "exhale";
    public string FdkaacPath { get; set; } = OperatingSystem.IsWindows() ? "fdkaac.exe" : "fdkaac";

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsualAudioConverter",
        "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { }

        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
