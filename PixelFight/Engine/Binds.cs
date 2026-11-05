using System.Windows.Forms;

namespace PixelFight;

// Переназначаемые клавиши игрока 1 (0 в сейве = дефолт).
static class Binds
{
    public static Keys Left = Keys.A, Right = Keys.D, Jump = Keys.W;
    public static Keys Kick = Keys.F, Jab = Keys.E, Potion = Keys.Q;
    public static Keys Pause = Keys.P, Restart = Keys.R;

    static Keys K(int v, Keys d) => v == 0 || !System.Enum.IsDefined(typeof(Keys), v) ? d : (Keys)v;

    public static void Load()
    {
        Left = K(SaveData.KeyL, Keys.A); Right = K(SaveData.KeyR, Keys.D); Jump = K(SaveData.KeyJ, Keys.W);
        Kick = K(SaveData.KeyK, Keys.F); Jab = K(SaveData.KeyJb, Keys.E); Potion = K(SaveData.KeyP, Keys.Q);
        Pause = K(SaveData.KeyPa, Keys.P); Restart = K(SaveData.KeyRe, Keys.R);
    }

    public static void Set(string action, Keys key)
    {
        switch (action)
        {
            case "L": Left = key; SaveData.KeyL = (int)key; break;
            case "R": Right = key; SaveData.KeyR = (int)key; break;
            case "J": Jump = key; SaveData.KeyJ = (int)key; break;
            case "K": Kick = key; SaveData.KeyK = (int)key; break;
            case "Jb": Jab = key; SaveData.KeyJb = (int)key; break;
            case "P": Potion = key; SaveData.KeyP = (int)key; break;
            case "Pa": Pause = key; SaveData.KeyPa = (int)key; break;
            case "Re": Restart = key; SaveData.KeyRe = (int)key; break;
        }
        SaveData.Store();
    }
}
