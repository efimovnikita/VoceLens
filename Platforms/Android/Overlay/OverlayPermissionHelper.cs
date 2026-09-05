#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

namespace VoceLens.Platforms.Android.Overlay;

public static class OverlayPermissionHelper
{
    public static bool CanDrawOverlays(Context context)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            return Settings.CanDrawOverlays(context);
        }
        return true;
    }

    public static void RequestOverlayPermission(Activity activity)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            try
            {
                var intent = new Intent(Settings.ActionManageOverlayPermission);
                intent.SetData(AndroidUri.Parse($"package:{activity.PackageName}"));
                intent.AddFlags(ActivityFlags.NewTask);
                activity.StartActivity(intent);
            }
            catch
            {
                try
                {
                    // Fallback without package URI if device doesn't support direct package link
                    var fallbackIntent = new Intent(Settings.ActionManageOverlayPermission);
                    fallbackIntent.AddFlags(ActivityFlags.NewTask);
                    activity.StartActivity(fallbackIntent);
                }
                catch
                {
                    OpenAppDetailsSettings(activity);
                }
            }
        }
    }

    public static void OpenAppDetailsSettings(Activity activity)
    {
        try
        {
            var appDetails = new Intent(Settings.ActionApplicationDetailsSettings);
            appDetails.SetData(AndroidUri.Parse($"package:{activity.PackageName}"));
            appDetails.AddFlags(ActivityFlags.NewTask);
            activity.StartActivity(appDetails);
        }
        catch { }
    }

    public static void OpenMiuiPermissionActivity(Activity activity)
    {
        // For Xiaomi / Redmi / POCO devices running MIUI / HyperOS
        try
        {
            var intent = new Intent("miui.intent.action.APP_PERM_EDITOR");
            intent.SetClassName("com.miui.securitycenter", "com.miui.permcenter.permissions.PermissionsEditorActivity");
            intent.PutExtra("extra_pkgname", activity.PackageName);
            intent.AddFlags(ActivityFlags.NewTask);
            activity.StartActivity(intent);
        }
        catch
        {
            OpenAppDetailsSettings(activity);
        }
    }
}
#endif
