#if ANDROID
using Android.Content;
using Android.OS;
using VoceLens.Models;
using VoceLens.Platforms.Android.Capture;
using VoceLens.Services.Overlay;
using VoceLens.Services.Settings;
using AndroidApplication = global::Android.App.Application;

namespace VoceLens.Platforms.Android.Overlay;

public class AndroidFloatingOverlayService : IFloatingOverlayService
{
    private readonly IAppSettingsService _settingsService;

    public bool IsServiceRunning => FloatingOverlayService.Instance != null;
    public bool HasScreenCapturePermission => ScreenCaptureManager.HasPermission;
    public bool IsCropFrameVisible => _settingsService.IsCropFrameEnabled;
    public byte[]? LastCapturedFullDisplayJpeg => ScreenCaptureManager.LastCapturedFullDisplayJpeg ?? ScreenCaptureManager.LastCapturedCroppedJpeg;

    public event Action<bool>? ServiceStateChanged;
    public event Action<CropFrameBounds>? CropFrameUpdated;

    public AndroidFloatingOverlayService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool CanDrawOverlays()
    {
        var context = AndroidApplication.Context;
        return OverlayPermissionHelper.CanDrawOverlays(context);
    }

    public void RequestOverlayPermission()
    {
        var activity = Platform.CurrentActivity;
        if (activity != null)
        {
            OverlayPermissionHelper.RequestOverlayPermission(activity);
        }
    }

    public void RequestScreenCapturePermission()
    {
        var context = Platform.CurrentActivity ?? AndroidApplication.Context;
        _ = ScreenCaptureManager.RequestPermissionAsync(context);
    }

    public void StartFloatingButton()
    {
        try
        {
            var context = AndroidApplication.Context;
            var intent = new Intent(context, typeof(FloatingOverlayService));

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }

            ServiceStateChanged?.Invoke(true);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("VoceLens", $"Error in StartFloatingButton: {ex}");
            ServiceStateChanged?.Invoke(false);
            throw;
        }
    }

    public void StopFloatingButton()
    {
        try
        {
            ScreenCaptureManager.ResetSession();
            var context = AndroidApplication.Context;
            var intent = new Intent(context, typeof(FloatingOverlayService));
            context.StopService(intent);

            ServiceStateChanged?.Invoke(false);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("VoceLens", $"Error in StopFloatingButton: {ex}");
        }
    }

    public void ShowCropFrame()
    {
        FloatingOverlayService.Instance?.ShowCropFrame();
        CropFrameUpdated?.Invoke(_settingsService.CropFrame);
    }

    public void HideCropFrame()
    {
        FloatingOverlayService.Instance?.HideCropFrame();
    }

    public async Task<byte[]> CaptureScreenAsync(bool cropToFrame = true)
    {
        var context = Platform.CurrentActivity ?? AndroidApplication.Context;
        var cropBounds = cropToFrame ? _settingsService.CropFrame : null;
        return await ScreenCaptureManager.CaptureScreenAsync(context, cropBounds);
    }

    public async Task<byte[]> CaptureFullDisplayAsync()
    {
        var context = Platform.CurrentActivity ?? AndroidApplication.Context;
        return await ScreenCaptureManager.CaptureFullDisplayAsync(context);
    }
}
#endif
