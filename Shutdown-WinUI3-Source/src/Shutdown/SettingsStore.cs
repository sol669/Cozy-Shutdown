using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ShutdownApp;

public sealed class SettingsStore
{
    // The installer grants users write access only to this folder. Portable builds use the
    // same layout next to their executable, so all app-owned state travels with the app.
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Data");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = AppBranding.AutostartValueName;

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
#if DEBUG
        if (App.Preview) { Current = new AppSettings { StartWithWindows = false }; return; }
#endif
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
#if DEBUG
        if (App.Preview) return;
#endif
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
#if DEBUG
        if (App.Preview) return;
#endif
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
#if DEBUG
            if (App.Preview)
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "preview.log"), $"[{DateTime.Now:O}] {ex}\r\n");
                return;
            }
#endif
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
