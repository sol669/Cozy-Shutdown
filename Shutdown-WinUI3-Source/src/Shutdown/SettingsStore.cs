using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ShutdownApp;

public sealed class SettingsStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shutdown");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Shutdown";

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        if (App.Preview) { Current = new AppSettings { StartWithWindows = false }; return; }
        try
        {
            Directory.CreateDirectory(Folder);
            if (!File.Exists(FilePath))
            {
                Current = new AppSettings();
                Save();
                return;
            }

            Current = SettingsCodec.Read(File.ReadAllText(FilePath));
        }
        catch (Exception ex)
        {
            Log(ex);
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        if (App.Preview) return;
        Directory.CreateDirectory(Folder);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, SettingsCodec.Write(Current));
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
        else File.Move(temporary, FilePath);
        ApplyAutostart();
    }

    public void Replace(AppSettings value)
    {
        var previous = Current;
        ActionPolicy.Normalize(value);
        Current = value;
        try { Save(); }
        catch { Current = previous; throw; }
    }

    public void ApplyAutostart()
    {
        if (App.Preview) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (Current.StartWithWindows)
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public static void Log(Exception ex)
    {
        try
        {
            if (App.Preview)
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "preview.log"), $"[{DateTime.Now:O}] {ex}\r\n");
                return;
            }
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, "error.log"),
                $"[{DateTime.Now:O}] {ex}\r\n\r\n");
        }
        catch
        {
            Debug.WriteLine(ex);
        }
    }
}
