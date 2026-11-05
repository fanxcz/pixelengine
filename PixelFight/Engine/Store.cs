using System;
using System.Collections.Generic;
using System.IO;

namespace PixelFight;

// Простое хранилище движка: api.save/load в скриптах (прогресс уровней и т.п.).
// Файл: %APPDATA%\PixelEngine\store.cfg
public static class Store
{
    static Dictionary<string, string> _m;
    static string Path() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PixelEngine", "store.cfg");

    static void Ensure()
    {
        if (_m != null) return;
        _m = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.ReadAllLines(Path()))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) _m[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
        }
        catch { }
    }

    public static string Get(string key, string def = "")
    {
        Ensure();
        return _m.TryGetValue(key, out var v) ? v : def;
    }

    public static void Set(string key, string val)
    {
        Ensure();
        _m[key] = val;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path()));
            var lines = new List<string>();
            foreach (var kv in _m) lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(Path(), lines);
        }
        catch { }
    }
}
