using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace PFMaui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
    ScreenOrientation = ScreenOrientation.Landscape)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // во весь экран (статус-бар прячем, навигацию оставляем свайпом)
        if (Build.VERSION.SdkInt < BuildVersionCodes.R)
            Window.AddFlags(WindowManagerFlags.Fullscreen);
        else
            Window.SetDecorFitsSystemWindows(false);
    }

    // Тач арены: сырые касания -> MainPage (мимо нижних кнопок, им идут свои события).
    public override bool DispatchTouchEvent(MotionEvent e)
    {
        try
        {
            var page = MainPage.Current;
            var v = page?.ArenaView?.Handler?.PlatformView as Android.Views.View;
            if (page != null && v != null && v.Width > 0)
            {
                int[] loc = new int[2];
                v.GetLocationInWindow(loc);
                float density = Resources?.DisplayMetrics?.Density ?? 1;
                float x = (e.GetX() - loc[0]) / density;
                float y = (e.GetY() - loc[1]) / density;
                float w = v.Width / density, h = v.Height / density;
                if (x >= 0 && y >= 0 && x < w && y < h - 110)
                {
                    switch (e.ActionMasked)
                    {
                        case MotionEventActions.Down: page.ArenaDown(x, y); break;
                        case MotionEventActions.Move: page.ArenaMove(x, y); break;
                        case MotionEventActions.Up:
                        case MotionEventActions.Cancel: page.ArenaUp(x, y); break;
                    }
                }
            }
        }
        catch { }
        return base.DispatchTouchEvent(e);
    }
}
