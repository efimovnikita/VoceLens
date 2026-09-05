using VoceLens.Models;

namespace VoceLens.Services.Overlay;

public interface IFloatingOverlayService
{
    bool IsServiceRunning { get; }
    bool CanDrawOverlays();
    void RequestOverlayPermission();
    bool HasScreenCapturePermission { get; }
    void RequestScreenCapturePermission();
    void StartFloatingButton();
    void StopFloatingButton();
    void ShowCropFrame();
    void HideCropFrame();
    bool IsCropFrameVisible { get; }
    byte[]? LastCapturedFullDisplayJpeg { get; }
    Task<byte[]> CaptureScreenAsync(bool cropToFrame = true);
    Task<byte[]> CaptureFullDisplayAsync();
    event Action<bool>? ServiceStateChanged;
    event Action<CropFrameBounds>? CropFrameUpdated;
}
