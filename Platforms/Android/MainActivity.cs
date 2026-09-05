using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Util;
using AndroidUri = Android.Net.Uri;
using VoceLens.Platforms.Android.Overlay;

namespace VoceLens;

[Activity(
    Theme = "@style/Maui.SplashTheme", 
    MainLauncher = true, 
    LaunchMode = LaunchMode.SingleTop, 
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density
)]
public class MainActivity : MauiAppCompatActivity
{
    public const string TAG = "VoceLensDebug";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Log.Debug(TAG, ">>> MainActivity.OnCreate: App started.");

        CheckPermissionAndStartFloatingButton();
    }

    protected override void OnResume()
    {
        base.OnResume();

        // When returning to the app after granting overlay permission in Settings, start the button automatically
        CheckPermissionAndStartFloatingButton();
    }

    public void CheckPermissionAndStartFloatingButton()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M && !Settings.CanDrawOverlays(this))
        {
            Log.Debug(TAG, ">>> Overlay permission not granted yet. Requesting...");
            try
            {
                var intent = new Intent(Settings.ActionManageOverlayPermission, AndroidUri.Parse($"package:{PackageName}"));
                intent.AddFlags(ActivityFlags.NewTask);
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                Log.Warn(TAG, $"Failed to open direct package overlay settings: {ex.Message}");
            }
        }
        else
        {
            if (FloatingOverlayService.Instance == null)
            {
                StartFloatingButtonService();
            }
            else
            {
                Log.Debug(TAG, ">>> FloatingOverlayService is already running. Skipping redundant start.");
            }
        }
    }

    public void StartFloatingButtonService()
    {
        try
        {
            Log.Debug(TAG, ">>> MainActivity: Launching FloatingOverlayService...");
            var intent = new Intent(this, typeof(FloatingOverlayService));

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                StartForegroundService(intent);
            }
            else
            {
                StartService(intent);
            }
            Log.Debug(TAG, ">>> MainActivity: FloatingOverlayService intent sent.");
        }
        catch (Exception ex)
        {
            Log.Error(TAG, $"Error starting FloatingOverlayService: {ex.Message}");
        }
    }
}
