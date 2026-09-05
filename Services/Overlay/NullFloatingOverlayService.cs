using VoceLens.Models;

namespace VoceLens.Services.Overlay;

public class NullFloatingOverlayService : IFloatingOverlayService
{
    public bool IsServiceRunning { get; private set; }
    public bool HasScreenCapturePermission { get; set; } = true;
    public bool IsCropFrameVisible { get; private set; }
    public byte[]? LastCapturedFullDisplayJpeg => null;

    public event Action<bool>? ServiceStateChanged;
    public event Action<CropFrameBounds>? CropFrameUpdated;

    public bool CanDrawOverlays() => true;

    public void RequestOverlayPermission()
    {
    }

    public void RequestScreenCapturePermission()
    {
        HasScreenCapturePermission = true;
    }

    public void StartFloatingButton()
    {
        IsServiceRunning = true;
        ServiceStateChanged?.Invoke(true);
    }

    public void StopFloatingButton()
    {
        IsServiceRunning = false;
        ServiceStateChanged?.Invoke(false);
    }

    public void ShowCropFrame()
    {
        IsCropFrameVisible = true;
        CropFrameUpdated?.Invoke(new CropFrameBounds(50, 100, 300, 400));
    }

    public void HideCropFrame()
    {
        IsCropFrameVisible = false;
    }

    public Task<byte[]> CaptureScreenAsync(bool cropToFrame = true)
    {
        // Return dummy 1x1 image for testing in windows/desktop preview
        byte[] dummyPng = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        };
        return Task.FromResult(dummyPng);
    }

    public Task<byte[]> CaptureFullDisplayAsync()
    {
        return CaptureScreenAsync(false);
    }
}
