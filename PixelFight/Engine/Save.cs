using System;
using System.IO;

namespace PixelFight;

// Простые сохранения движка: кубышка монет, прогресс кампании, настройки.
// Файл: %APPDATA%\PixelFight\save.cfg
static class SaveData
{
    public static int Bank;      // всего монет накоплено
    public static int Unlocked;  // открыто этапов (старая кампания, не используется)
    public static int Unlj, Unld, Unlr; // прогресс кампаний Японии/Германии/Руси (0..5)
    public static int Unlf, Unle, Unlh; // прогресс Франции/Англии/Орды (0..5)
    public static int Shop;      // биты купленных вещей
    public static int Potions;   // зелья в запасе (0..5)
    public static int Diff;      // сложность: 0 легко, 1 норма, 2 хард
    public static int BestCombo; // рекорд комбо
    public static int BestCoins; // рекорд монет за забег
    public static int CampWins;  // пройдено кампаний
    public static int Ach;       // биты достижений (0 чистота, 1 комбо10, 2 пари5, 3 обезоруж10, 4 убийства100, 5 все кампании)
    public static int Kills, Parries, Disarms; // пожизненные счётчики
    public static bool Sound = true;
    public static int Lang;      // 0 — RU, 1 — EN
    public static int Sens = 14;   // чувствительность мыши 5..20
    public static bool Pad = true; // геймпад XInput
    public static int KeyL, KeyR, KeyJ, KeyK, KeyJb, KeyP, KeyPa, KeyRe; // 0 = дефолт

    static string Path()
    {
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelFight", "save.cfg");
    }

    public static void Load()
    {
        try
        {
            foreach (var line in File.ReadAllLines(Path()))
            {
                var kv = line.Split('=');
                if (kv.Length != 2) continue;
                switch (kv[0])
                {
                    case "bank": Bank = int.Parse(kv[1]); break;
                    case "unlocked": Unlocked = Math.Max(0, Math.Min(5, int.Parse(kv[1]))); break;
                    case "unlj": Unlj = Math.Max(0, Math.Min(5, int.Parse(kv[1]))); break;
                    case "unld": Unld = Math.Max(0, Math.Min(5, int.Parse(kv[1]))); break;
                    case "unlr": Unlr = Math.Max(0, Math.Min(5, int.Parse(kv[1]))); break;
                    case "unlf": Unlf = Math.Max(0, Math.Min(5, int.Parse(kv[1]))); break;
                    case "unle": Unle = Math.Max(0, Math.Min(5, int.Parse(kv[1]))); break;
                    case "unlh": Unlh = Math.Max(0, Math.Min(5, int.Parse(kv[1]))); break;
                    case "shop": Shop = int.Parse(kv[1]); break;
                    case "potions": Potions = Math.Max(0, Math.Min(5, int.Parse(kv[1]))); break;
                    case "bestcombo": BestCombo = int.Parse(kv[1]); break;
                    case "bestcoins": BestCoins = int.Parse(kv[1]); break;
                    case "campwins": CampWins = int.Parse(kv[1]); break;
                    case "ach": Ach = int.Parse(kv[1]); break;
                    case "kills": Kills = int.Parse(kv[1]); break;
                    case "parries": Parries = int.Parse(kv[1]); break;
                    case "disarms": Disarms = int.Parse(kv[1]); break;
                    case "diff": Diff = Math.Max(0, Math.Min(2, int.Parse(kv[1]))); break;
                    case "sound": Sound = kv[1] == "1"; break;
                    case "lang": Lang = kv[1] == "1" ? 1 : 0; break;
                    case "sens": Sens = Math.Max(5, Math.Min(20, int.Parse(kv[1]))); break;
                    case "pad": Pad = kv[1] == "1"; break;
                    case "keyl": KeyL = int.Parse(kv[1]); break;
                    case "keyr": KeyR = int.Parse(kv[1]); break;
                    case "keyj": KeyJ = int.Parse(kv[1]); break;
                    case "keyk": KeyK = int.Parse(kv[1]); break;
                    case "keyjb": KeyJb = int.Parse(kv[1]); break;
                    case "keyp": KeyP = int.Parse(kv[1]); break;
                    case "keypa": KeyPa = int.Parse(kv[1]); break;
                    case "keyre": KeyRe = int.Parse(kv[1]); break;
                }
            }
        }
        catch { }
    }

    public static void Store()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path()));
            File.WriteAllLines(Path(), new[] {
                "bank=" + Bank, "unlocked=" + Unlocked,
                "unlj=" + Unlj, "unld=" + Unld, "unlr=" + Unlr, "unlf=" + Unlf, "unle=" + Unle, "unlh=" + Unlh,
                "shop=" + Shop, "diff=" + Diff, "potions=" + Potions,
                "bestcombo=" + BestCombo, "bestcoins=" + BestCoins, "campwins=" + CampWins,
                "ach=" + Ach, "kills=" + Kills, "parries=" + Parries, "disarms=" + Disarms,
                "sound=" + (Sound ? "1" : "0"), "lang=" + Lang,
                "sens=" + Sens, "pad=" + (Pad ? "1" : "0"),
                "keyl=" + KeyL, "keyr=" + KeyR, "keyj=" + KeyJ, "keyk=" + KeyK,
                "keyjb=" + KeyJb, "keyp=" + KeyP, "keypa=" + KeyPa, "keyre=" + KeyRe,
            });
        }
        catch { }
    }

    static string ArenaPath()
    {
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelFight", "arena.cfg");
    }

    public static System.Collections.Generic.List<string> LoadArena()
    {
        try { return new System.Collections.Generic.List<string>(File.ReadAllLines(ArenaPath())); }
        catch { return new System.Collections.Generic.List<string>(); }
    }

    public static void StoreArena(System.Collections.Generic.List<string> lines)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ArenaPath()));
            File.WriteAllLines(ArenaPath(), lines);
        }
        catch { }
    }
}
