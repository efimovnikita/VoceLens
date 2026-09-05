#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media.Projection;
using Android.OS;
using Android.Util;
using VoceLens.Platforms.Android.Overlay;

namespace VoceLens.Platforms.Android.Capture;

[Activity(
    Label = "VoceLens Capture Permission",
    Theme = "@android:style/Theme.Translucent.NoTitleBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize,
    NoHistory = true,
    ExcludeFromRecents = true,
    TaskAffinity = "",
    LaunchMode = LaunchMode.SingleInstance,
    Exported = false
)]
public class MediaProjectionPermissionActivity : Activity
{
    private const string TAG = "VoceLensCapture";
    private const int RequestCodeScreenCapture = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var projectionManager = (MediaProjectionManager?)GetSystemService(MediaProjectionService);
        if (projectionManager != null)
        {
            Intent captureIntent;
            // On Android 14+ (API 34+), restrict to default display to remove the "single app vs entire screen" question!
            if ((int)Build.VERSION.SdkInt >= 34)
            {
                try
                {
                    var config = MediaProjectionConfig.CreateConfigForDefaultDisplay();
                    captureIntent = projectionManager.CreateScreenCaptureIntent(config);
                }
                catch (Exception ex)
                {
                    Log.Warn(TAG, $"Failed to create config for default display: {ex.Message}. Falling back.");
                    captureIntent = projectionManager.CreateScreenCaptureIntent();
                }
            }
            else
            {
                captureIntent = projectionManager.CreateScreenCaptureIntent();
            }

            StartActivityForResult(captureIntent, RequestCodeScreenCapture);
        }
        else
        {
            ScreenCaptureManager.CompletePermissionRequest(false);
            MoveTaskToBack(true);
            Finish();
            OverridePendingTransition(0, 0);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == RequestCodeScreenCapture)
        {
            if (resultCode == Result.Ok && data != null)
            {
                Log.Debug(TAG, ">>> Screen capture permission granted by user.");
                FloatingOverlayService.Instance?.EnsureMediaProjectionForegroundService();
                ScreenCaptureManager.SetMediaProjectionResult((int)resultCode, data);
                ScreenCaptureManager.CompletePermissionRequest(true);
            }
            else
            {
                Log.Warn(TAG, ">>> Screen capture permission rejected or cancelled.");
                ScreenCaptureManager.CompletePermissionRequest(false);
            }
        }

        MoveTaskToBack(true);
        Finish();
        OverridePendingTransition(0, 0);
    }
}
#endif
