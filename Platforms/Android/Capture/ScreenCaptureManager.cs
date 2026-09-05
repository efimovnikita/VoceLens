#if ANDROID
using Android.Content;
using Android.Graphics;
using Android.Hardware.Display;
using Android.Media.Projection;
using Android.OS;
using Android.Util;
using Android.Views;
using VoceLens.Models;
using VoceLens.Platforms.Android.Overlay;
using VoceLens.Services.Logging;
using AndroidMedia = Android.Media;
using AndroidGraphics = Android.Graphics;

namespace VoceLens.Platforms.Android.Capture;

public static class ScreenCaptureManager
{
    private const string TAG = "VoceLensCapture";
    private static int _resultCode;
    private static Intent? _resultData;
    private static TaskCompletionSource<bool>? _permissionTcs;

    // Persistent capture session - VirtualDisplay and ImageReader are kept alive across taps!
    private static MediaProjection? _activeMediaProjection;
    private static ScreenCaptureCallback? _activeCallback;
    private static VirtualDisplay? _activeVirtualDisplay;
    private static AndroidMedia.ImageReader? _activeImageReader;
    private static int _activeWidth;
    private static int _activeHeight;
    private static int _activeDensity;
    private static byte[]? _lastCapturedJpeg;
    private static readonly SemaphoreSlim _captureLock = new(1, 1);

    public static byte[]? LastCapturedFullDisplayJpeg { get; private set; }
    public static byte[]? LastCapturedCroppedJpeg => _lastCapturedJpeg;

    public static bool HasPermission => _activeMediaProjection != null || _resultData != null;

    public static void SetMediaProjectionResult(int resultCode, Intent resultData)
    {
        _resultCode = resultCode;
        _resultData = resultData;
    }

    public static void CompletePermissionRequest(bool granted)
    {
        _permissionTcs?.TrySetResult(granted);
        _permissionTcs = null;
    }

    public static Task<bool> RequestPermissionAsync(Context context)
    {
        if (_activeMediaProjection != null && _activeVirtualDisplay != null)
            return Task.FromResult(true);

        _permissionTcs = new TaskCompletionSource<bool>();

        var intent = new Intent(context, typeof(MediaProjectionPermissionActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.MultipleTask | ActivityFlags.NoAnimation);
        context.StartActivity(intent);

        return _permissionTcs.Task;
    }

    public static void ResetSession()
    {
        try
        {
            _activeVirtualDisplay?.Release();
            _activeVirtualDisplay?.Dispose();
        }
        catch { }

        try
        {
            _activeImageReader?.Close();
            _activeImageReader?.Dispose();
        }
        catch { }

        try
        {
            if (_activeMediaProjection != null && _activeCallback != null)
            {
                try { _activeMediaProjection.UnregisterCallback(_activeCallback); } catch { }
            }
            _activeMediaProjection?.Stop();
            _activeMediaProjection?.Dispose();
        }
        catch { }
        finally
        {
            _activeVirtualDisplay = null;
            _activeImageReader = null;
            _activeMediaProjection = null;
            _activeCallback = null;
            _resultData = null;
            _activeWidth = 0;
            _activeHeight = 0;
            _activeDensity = 0;
            _lastCapturedJpeg = null;
            LastCapturedFullDisplayJpeg = null;
            Log.Debug(TAG, ">>> ScreenCaptureManager session completely reset.");
        }
    }

    public static async Task<byte[]> CaptureScreenAsync(Context context, CropFrameBounds? cropBounds = null)
    {
        await _captureLock.WaitAsync();
        try
        {
            var windowManager = context.GetSystemService(Context.WindowService) as IWindowManager;
            if (windowManager == null)
                throw new InvalidOperationException("Failed to obtain Android window manager.");

            var metrics = new DisplayMetrics();
            windowManager.DefaultDisplay?.GetRealMetrics(metrics);

            int screenWidth = metrics.WidthPixels;
            int screenHeight = metrics.HeightPixels;
            int screenDensity = (int)metrics.DensityDpi;

            if (screenWidth <= 0 || screenHeight <= 0)
            {
                screenWidth = 1080;
                screenHeight = 2400;
                screenDensity = (int)DisplayMetricsDensity.Xhigh;
            }

            // 1. Initialize MediaProjection & VirtualDisplay ONCE (Android 14+ prohibits calling CreateVirtualDisplay multiple times)
            if (_activeMediaProjection == null || _activeVirtualDisplay == null || _activeImageReader == null)
            {
                try { _activeVirtualDisplay?.Release(); _activeVirtualDisplay?.Dispose(); } catch { }
                try { _activeImageReader?.Close(); _activeImageReader?.Dispose(); } catch { }
                try { _activeMediaProjection?.Stop(); _activeMediaProjection?.Dispose(); } catch { }
                _activeVirtualDisplay = null;
                _activeImageReader = null;
                _activeMediaProjection = null;

                if (_resultData == null)
                {
                    bool granted = await RequestPermissionAsync(context);
                    if (!granted || _resultData == null)
                        throw new InvalidOperationException("Screen capture permission was not granted.");

                    // Give system permission dialog 450ms to dismiss and fully reveal the background reader app
                    await Task.Delay(450);
                }

                // Foreground service must have TypeMediaProjection active before GetMediaProjection / CreateVirtualDisplay
                FloatingOverlayService.Instance?.EnsureMediaProjectionForegroundService();
                await Task.Delay(100);

                var projectionManager = context.GetSystemService(Context.MediaProjectionService) as MediaProjectionManager;
                if (projectionManager == null)
                    throw new InvalidOperationException("Failed to obtain Android projection service.");

                _activeMediaProjection = projectionManager.GetMediaProjection(_resultCode, (Intent)_resultData.Clone()!);
                if (_activeMediaProjection == null)
                    throw new InvalidOperationException("Failed to acquire MediaProjection session.");

                _activeCallback = new ScreenCaptureCallback(() =>
                {
                    Log.Debug(TAG, ">>> MediaProjection session ended by Android system.");
                    ResetSession();
                });
                _activeMediaProjection.RegisterCallback(_activeCallback, new Handler(Looper.MainLooper!));

                _activeWidth = screenWidth;
                _activeHeight = screenHeight;
                _activeDensity = screenDensity;

                _activeImageReader = AndroidMedia.ImageReader.NewInstance(
                    _activeWidth,
                    _activeHeight,
                    (AndroidGraphics.ImageFormatType)Format.Rgba8888,
                    4
                );

                if (_activeImageReader == null)
                    throw new InvalidOperationException("Failed to create ImageReader for screenshot capture.");

                _activeVirtualDisplay = _activeMediaProjection.CreateVirtualDisplay(
                    "VoceLensCaptureDisplay",
                    _activeWidth,
                    _activeHeight,
                    _activeDensity,
                    (DisplayFlags)VirtualDisplayFlags.AutoMirror,
                    _activeImageReader.Surface,
                    null,
                    null
                );

                Log.Debug(TAG, ">>> Persistent MediaProjection and VirtualDisplay created successfully.");
                AppLog.Info($"VirtualDisplay initialized ({_activeWidth}x{_activeHeight}, {_activeDensity} dpi)", "Capture");
                // Initial delay for virtual display to buffer first frames
                await Task.Delay(250);
            }
            else
            {
                // Verify that virtual display is still valid
                if (_activeVirtualDisplay.Display == null || !_activeVirtualDisplay.Display.IsValid)
                {
                    Log.Warn(TAG, ">>> VirtualDisplay is no longer valid. Resetting session.");
                    ResetSession();
                    throw new InvalidOperationException("Virtual display session expired. Please tap the button again.");
                }

                // If screen orientation/resolution changed, resize the existing VirtualDisplay without recreating it!
                if (screenWidth != _activeWidth || screenHeight != _activeHeight)
                {
                    Log.Debug(TAG, $">>> Display metrics changed ({_activeWidth}x{_activeHeight} -> {screenWidth}x{screenHeight}). Resizing existing VirtualDisplay.");
                    _activeWidth = screenWidth;
                    _activeHeight = screenHeight;
                    _activeDensity = screenDensity;

                    _activeImageReader?.Close();
                    _activeImageReader?.Dispose();

                    _activeImageReader = AndroidMedia.ImageReader.NewInstance(
                        _activeWidth,
                        _activeHeight,
                        (AndroidGraphics.ImageFormatType)Format.Rgba8888,
                        4
                    );

                    _activeVirtualDisplay.Surface = _activeImageReader.Surface;
                    _activeVirtualDisplay.Resize(_activeWidth, _activeHeight, _activeDensity);
                    await Task.Delay(150);
                }
            }

            // 2. Acquire frame from active ImageReader
            // Drain any pre-existing frames in the buffer queue (which may contain the visible floating button)
            try
            {
                AndroidMedia.Image? stale;
                while ((stale = _activeImageReader.AcquireNextImage()) != null)
                {
                    stale.Close();
                    stale.Dispose();
                }
            }
            catch { }

            // Temporarily hide floating overlay so it never appears in the screenshot/OCR
            if (FloatingOverlayService.Instance != null)
            {
                await FloatingOverlayService.Instance.SetOverlayVisibleAsync(false);
            }

            // Allow WindowManager and SurfaceFlinger enough time to composite the screen without the overlay
            await Task.Delay(250);

            AndroidMedia.Image? image = null;
            try
            {
                for (int i = 0; i < 30; i++)
                {
                    image = _activeImageReader.AcquireLatestImage();
                    if (image != null)
                        break;
                    await Task.Delay(25);
                }

                if (image == null)
                {
                    image = _activeImageReader.AcquireNextImage();
                }

                if (image == null)
                {
                    if (_lastCapturedJpeg != null && _lastCapturedJpeg.Length > 0)
                    {
                        Log.Debug(TAG, ">>> Screen is static, reusing last captured frame buffer.");
                        return _lastCapturedJpeg;
                    }
                    throw new InvalidOperationException("No image frame was captured from screen display.");
                }

                var planes = image.GetPlanes();
                if (planes == null || planes.Length == 0)
                    throw new InvalidOperationException("Empty image planes captured.");

                var buffer = planes[0].Buffer;
                if (buffer == null)
                    throw new InvalidOperationException("Empty pixel buffer from screen capture.");

                int pixelStride = planes[0].PixelStride;
                int rowStride = planes[0].RowStride;
                int rowPadding = rowStride - pixelStride * _activeWidth;

                Bitmap? displayBitmap = null;
                Bitmap? croppedBitmap = null;
                try
                {
                    using var fullBitmap = Bitmap.CreateBitmap(
                        _activeWidth + rowPadding / pixelStride,
                        _activeHeight,
                        Bitmap.Config.Argb8888!
                    );

                    fullBitmap.CopyPixelsFromBuffer(buffer);

                    // Trim padding if needed
                    bool needsPaddingTrim = (fullBitmap.Width != _activeWidth || fullBitmap.Height != _activeHeight);
                    if (needsPaddingTrim)
                    {
                        displayBitmap = Bitmap.CreateBitmap(fullBitmap, 0, 0, _activeWidth, _activeHeight);
                    }

                    Bitmap baseDisplay = displayBitmap ?? fullBitmap;

                    // Cache full uncropped display JPEG for calibration preview
                    using (var fullDisplayStream = new MemoryStream())
                    {
                        baseDisplay.Compress(Bitmap.CompressFormat.Jpeg!, 85, fullDisplayStream);
                        LastCapturedFullDisplayJpeg = fullDisplayStream.ToArray();
                    }

                    Bitmap finalBitmap = baseDisplay;

                    // 4. Percentage-based edge cropping (cuts off status bar, address bar, nav bar)
                    if (cropBounds != null && cropBounds.IsEnabled)
                    {
                        int topCrop = (int)(_activeHeight * (Math.Clamp(cropBounds.TopPercent, 0, 45) / 100.0f));
                        int bottomCrop = (int)(_activeHeight * (Math.Clamp(cropBounds.BottomPercent, 0, 45) / 100.0f));
                        int leftCrop = (int)(_activeWidth * (Math.Clamp(cropBounds.LeftPercent, 0, 40) / 100.0f));
                        int rightCrop = (int)(_activeWidth * (Math.Clamp(cropBounds.RightPercent, 0, 40) / 100.0f));

                        int cropX = leftCrop;
                        int cropY = topCrop;
                        int cropW = _activeWidth - leftCrop - rightCrop;
                        int cropH = _activeHeight - topCrop - bottomCrop;

                        if (cropW > 50 && cropH > 50 && (cropW < _activeWidth || cropH < _activeHeight))
                        {
                            Log.Debug(TAG, $">>> Cropping with edge margins: Top={topCrop}px ({cropBounds.TopPercent}%), Bottom={bottomCrop}px ({cropBounds.BottomPercent}%), Left={leftCrop}px ({cropBounds.LeftPercent}%), Right={rightCrop}px ({cropBounds.RightPercent}%). Captured Area: {cropW}x{cropH}");
                            croppedBitmap = Bitmap.CreateBitmap(baseDisplay, cropX, cropY, cropW, cropH);
                            finalBitmap = croppedBitmap;
                        }
                    }

                    int capturedW = finalBitmap.Width;
                    int capturedH = finalBitmap.Height;

                    using var memoryStream = new MemoryStream();
                    finalBitmap.Compress(Bitmap.CompressFormat.Jpeg!, 90, memoryStream);

                    _lastCapturedJpeg = memoryStream.ToArray();
                    AppLog.Info($"Screen frame captured: {capturedW}x{capturedH} ({_lastCapturedJpeg.Length / 1024} KB)", "Capture");
                    return _lastCapturedJpeg;
                }
                finally
                {
                    if (croppedBitmap != null)
                    {
                        try { croppedBitmap.Recycle(); } catch { }
                        try { croppedBitmap.Dispose(); } catch { }
                    }
                    if (displayBitmap != null)
                    {
                        try { displayBitmap.Recycle(); } catch { }
                        try { displayBitmap.Dispose(); } catch { }
                    }
                }
            }
            finally
            {
                if (image != null)
                {
                    try { image.Close(); } catch { }
                    try { image.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(TAG, $"Screen capture failed: {ex.Message}");
            AppLog.Error($"Screen capture failed: {ex.Message}", ex, "Capture");
            if (ex is Java.Lang.IllegalStateException)
            {
                Log.Warn(TAG, "ImageReader buffer error. Rebinding ImageReader surface to preserve active MediaProjection.");
                try
                {
                    _activeImageReader?.Close();
                    _activeImageReader?.Dispose();
                    _activeImageReader = AndroidMedia.ImageReader.NewInstance(
                        _activeWidth,
                        _activeHeight,
                        (AndroidGraphics.ImageFormatType)Format.Rgba8888,
                        4
                    );
                    if (_activeVirtualDisplay != null && _activeImageReader != null)
                    {
                        _activeVirtualDisplay.Surface = _activeImageReader.Surface;
                    }
                }
                catch (Exception reEx)
                {
                    Log.Error(TAG, $"Failed to rebind ImageReader: {reEx.Message}");
                }
            }
            else if (ex is Java.Lang.SecurityException)
            {
                Log.Warn(TAG, "Fatal projection security error. Resetting session.");
                ResetSession();
            }
            throw;
        }
        finally
        {
            // Always restore floating overlay visibility
            FloatingOverlayService.Instance?.SetOverlayVisible(true);

            // CRITICAL: We deliberately do NOT release VirtualDisplay or ImageReader here!
            // Keeping them active allows subsequent taps on the floating button to capture
            // the screen INSTANTLY with ZERO repetitive Android system permission popups!
            _captureLock.Release();
        }
    }

    public static async Task<byte[]> CaptureFullDisplayAsync(Context context)
    {
        return await CaptureScreenAsync(context, cropBounds: null);
    }

    private class ScreenCaptureCallback : MediaProjection.Callback
    {
        private readonly Action _onStop;

        public ScreenCaptureCallback(Action onStop)
        {
            _onStop = onStop;
        }

        public override void OnStop()
        {
            base.OnStop();
            _onStop?.Invoke();
        }
    }
}
#endif
