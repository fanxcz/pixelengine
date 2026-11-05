namespace PixelFight;

// Звук на Android v1: заглушка (штатного бипера нет, MediaPlayer — следующим шагом).
// API 1-в-1 как на десктопе, чтобы логика игры не менялась.
public static class Sfx
{
    public static void Init() { }
    public static bool Enabled = true;
    public static void Swing() { }
    public static void Hit() { }
    public static void Clang() { }
    public static void Death() { }
    public static void Jump() { }
    public static void Horn() { }
    public static void Drum(bool accent) { }
    public static void Tick() { }
    public static void Heal() { }
    public static void Stomp() { }
}
