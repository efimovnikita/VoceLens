#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using VoceLens.Models;
using VoceLens.Platforms.Android.Capture;
using VoceLens.Services.Audio;
using VoceLens.Services.Logging;
using VoceLens.Services.Overlay;
using VoceLens.Services.Settings;
using VoceLens.Services.Workflow;
using Color = Android.Graphics.Color;
using View = Android.Views.View;

namespace VoceLens.Platforms.Android.Overlay;

[Service(Name = "com.companyname.vocelens.FloatingOverlayService", Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback | ForegroundService.TypeMediaProjection)]
public class FloatingOverlayService : Service, View.IOnTouchListener
{
    public const string TAG = "VoceLensDebug";
    private const int NotificationId = 101;
    private const string ChannelId = "VoceLensServiceChannel";
    private const int BUTTON_SIZE = 160;

    private IWindowManager? _windowManager;
    private FrameLayout? _containerView;
    private ImageView? _floatingButton;
    private FrameLayout? _stopButton;
    private WindowManagerLayoutParams? _buttonLayoutParams;

    private int _initialX;
    private int _initialY;
    private float _initialTouchX;
    private float _initialTouchY;
    private long _lastTapTime;
    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private Action? _longPressRunnable;
    private bool _isLongPressed;

    private IAppSettingsService? _settingsService;
    private IOcrSpeechWorkflowController? _workflowController;
    private IAudioPlaybackManager? _audioPlaybackManager;

    public static FloatingOverlayService? Instance { get; private set; }
    public static bool IsButtonVisible => Instance != null && Instance._containerView != null;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        Instance = this;

        ResolveDependencies();
        Log.Debug(TAG, ">>> FloatingOverlayService created.");
    }

    private void ResolveDependencies()
    {
        try
        {
            var services = IPlatformApplication.Current?.Services;
            if (services != null)
            {
                _settingsService ??= services.GetService<IAppSettingsService>();
                if (_workflowController == null)
                {
                    _workflowController = services.GetService<IOcrSpeechWorkflowController>();
                    if (_workflowController != null)
                    {
                        _workflowController.StatusChanged -= OnWorkflowStatusChanged;
                        _workflowController.StatusChanged += OnWorkflowStatusChanged;
                    }
                }
                _audioPlaybackManager ??= services.GetService<IAudioPlaybackManager>();
            }
        }
        catch (Exception ex)
        {
            Log.Warn(TAG, $"ResolveDependencies error: {ex.Message}");
        }
    }

    private IOcrSpeechWorkflowController? GetWorkflowController()
    {
        if (_workflowController == null)
        {
            ResolveDependencies();
        }
        return _workflowController;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Log.Debug(TAG, ">>> FloatingOverlayService: OnStartCommand invoked.");

        if (_containerView != null)
        {
            Log.Debug(TAG, ">>> FloatingOverlayService already active. Preserving existing foreground service state.");
            return StartCommandResult.Sticky;
        }

        CreateNotificationChannel();
        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("VoceLens Active")
            .SetContentText("Floating screen reader is active.")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .Build();

        try
        {
            if ((int)Build.VERSION.SdkInt >= 29)
            {
                StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(TAG, $"StartForeground fallback: {ex.Message}");
            StartForeground(NotificationId, notification);
        }

        CreateFloatingButton();

        return StartCommandResult.Sticky;
    }

    public void EnsureMediaProjectionForegroundService()
    {
        try
        {
            var notification = new Notification.Builder(this, ChannelId)
                .SetContentTitle("VoceLens Active")
                .SetContentText("Floating screen reader is active.")
                .SetSmallIcon(Resource.Mipmap.appicon)
                .Build();

            if ((int)Build.VERSION.SdkInt >= 29)
            {
                StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback | ForegroundService.TypeMediaProjection);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
            Log.Debug(TAG, ">>> Successfully promoted foreground service to TypeMediaPlayback | TypeMediaProjection!");
        }
        catch (Exception ex)
        {
            Log.Error(TAG, $"Failed to promote foreground service to TypeMediaProjection: {ex.Message}");
        }
    }

    public void ResetForegroundServiceType()
    {
        try
        {
            var notification = new Notification.Builder(this, ChannelId)
                .SetContentTitle("VoceLens Active")
                .SetContentText("Floating screen reader is active.")
                .SetSmallIcon(Resource.Mipmap.appicon)
                .Build();

            if ((int)Build.VERSION.SdkInt >= 29)
            {
                StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
            Log.Debug(TAG, ">>> Reset foreground service back to normal type.");
        }
        catch (Exception ex)
        {
            Log.Error(TAG, $"ResetForegroundServiceType failed: {ex.Message}");
        }
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channelName = "VoceLens Service";
        var channel = new NotificationChannel(ChannelId, channelName, NotificationImportance.Low);
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
        Log.Debug(TAG, ">>> Notification channel created.");
    }

    private void CreateFloatingButton()
    {
        try
        {
            Log.Debug(TAG, ">>> Starting button creation...");
            _windowManager = GetSystemService(WindowService).JavaCast<IWindowManager>();

            int startX = 100;
            int startY = 240;

            _containerView = new FrameLayout(this);

            // 1. Main Floating Orb
            _floatingButton = new ImageView(this);
            _floatingButton.SetOnTouchListener(this);
            _floatingButton.SetScaleType(ImageView.ScaleType.CenterInside);
            int pad = (int)(5 * Resources?.DisplayMetrics?.Density ?? 10);
            _floatingButton.SetPadding(pad, pad, pad, pad);

            var bg = new GradientDrawable();
            bg.SetShape(ShapeType.Oval);
            bg.SetColor(Color.ParseColor("#0B1829")); // Deep navy base matching book icon
            bg.SetStroke(6, Color.White); // Crisp white border
            _floatingButton.Background = bg;

            LoadFloatingButtonIcon();

            var fabParams = new FrameLayout.LayoutParams(BUTTON_SIZE, BUTTON_SIZE)
            {
                Gravity = GravityFlags.Left | GravityFlags.Bottom
            };
            _containerView.AddView(_floatingButton, fabParams);

            // 2. Mini Cancel / Stop Button (Clean native white square, zero emoji font artifacts)
            _stopButton = new FrameLayout(this)
            {
                Clickable = true,
                Focusable = false
            };

            var stopBg = new GradientDrawable();
            stopBg.SetShape(ShapeType.Oval);
            stopBg.SetColor(Color.ParseColor("#EF4444")); // Vibrant Red
            stopBg.SetStroke(3, Color.White); // Crisp clean white border
            _stopButton.Background = stopBg;
            _stopButton.Visibility = ViewStates.Gone; // Only visible while playing or paused!

            // Pure solid white square in the center (no emoji font, no orange border artifacts)
            var innerSquare = new View(this)
            {
                Clickable = false,
                Focusable = false
            };
            var squareDrawable = new GradientDrawable();
            squareDrawable.SetShape(ShapeType.Rectangle);
            squareDrawable.SetColor(Color.White);
            squareDrawable.SetCornerRadius(3); // Smooth slightly rounded corners
            innerSquare.Background = squareDrawable;

            var innerParams = new FrameLayout.LayoutParams(22, 22)
            {
                Gravity = GravityFlags.Center
            };
            _stopButton.AddView(innerSquare, innerParams);

            var stopParams = new FrameLayout.LayoutParams(64, 64)
            {
                Gravity = GravityFlags.Right | GravityFlags.Top
            };
            _containerView.AddView(_stopButton, stopParams);

            _stopButton.Click += (s, e) =>
            {
                Log.Debug(TAG, ">>> Mini stop button clicked!");
                StopAndClearPlayback();
            };

            int containerWidth = BUTTON_SIZE + 24;
            int containerHeight = BUTTON_SIZE + 24;

            _buttonLayoutParams = new WindowManagerLayoutParams(
                containerWidth, containerHeight,
                WindowManagerTypes.ApplicationOverlay,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutNoLimits,
                Format.Translucent
            );

            _buttonLayoutParams.Gravity = GravityFlags.Left | GravityFlags.Top;
            _buttonLayoutParams.X = startX;
            _buttonLayoutParams.Y = startY;

            _windowManager?.AddView(_containerView, _buttonLayoutParams);

            Log.Debug(TAG, ">>> FLOATING BUTTON ADDED TO WINDOWMANAGER SUCCESSFULLY!");

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Toast.MakeText(this, "🎙️ VoceLens button is visible on your screen!", ToastLength.Short)?.Show();
            });
        }
        catch (Exception ex)
        {
            Log.Error(TAG, $"!!! ERROR in CreateFloatingButton: {ex.Message}");
        }
    }

    public bool OnTouch(View? v, MotionEvent? e)
    {
        if (e == null || _buttonLayoutParams == null || _windowManager == null || _containerView == null) return false;

        switch (e.Action)
        {
            case MotionEventActions.Down:
                Log.Debug(TAG, ">>> Floating button PRESSED");
                try
                {
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                }
                catch { }

                _initialX = _buttonLayoutParams.X;
                _initialY = _buttonLayoutParams.Y;
                _initialTouchX = e.RawX;
                _initialTouchY = e.RawY;
                _isLongPressed = false;

                // Long-press detection: holding 550ms shuts down VoceLens and terminates background monitoring
                _longPressRunnable = () =>
                {
                    _isLongPressed = true;
                    Log.Debug(TAG, ">>> Long press detected on floating button: CLOSE APP & STOP MONITORING");
                    CloseAppAndExit();
                };
                _mainHandler.PostDelayed(_longPressRunnable, 550);
                return true;

            case MotionEventActions.Move:
                var dX = e.RawX - _initialTouchX;
                var dY = e.RawY - _initialTouchY;

                const float DragThreshold = 12.0f;
                if (Math.Abs(dX) > DragThreshold || Math.Abs(dY) > DragThreshold)
                {
                    if (_longPressRunnable != null)
                    {
                        _mainHandler.RemoveCallbacks(_longPressRunnable);
                        _longPressRunnable = null;
                    }

                    _buttonLayoutParams.X = _initialX + (int)dX;
                    _buttonLayoutParams.Y = _initialY + (int)dY;
                    _windowManager.UpdateViewLayout(_containerView, _buttonLayoutParams);
                }
                return true;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                Log.Debug(TAG, ">>> Floating button RELEASED");
                if (_longPressRunnable != null)
                {
                    _mainHandler.RemoveCallbacks(_longPressRunnable);
                    _longPressRunnable = null;
                }

                if (_isLongPressed)
                {
                    return true;
                }

                var totalDx = Math.Abs(e.RawX - _initialTouchX);
                var totalDy = Math.Abs(e.RawY - _initialTouchY);

                if (totalDx < 15 && totalDy < 15)
                {
                    // Double tap within 350ms triggers Stop & Clear
                    long now = SystemClock.UptimeMillis();
                    if (now - _lastTapTime < 350)
                    {
                        _lastTapTime = 0;
                        Log.Debug(TAG, ">>> Double tap detected on floating button: STOP & CLEAR");
                        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { }
                        StopAndClearPlayback();
                        return true;
                    }
                    _lastTapTime = now;

                    OnButtonClicked();
                }
                return true;
        }

        return false;
    }

    public void StopAndClearPlayback()
    {
        Log.Debug(TAG, ">>> StopAndClearPlayback: Stopping audio and clearing queue/context.");
        AppLog.Info("⏹ Playback stopped & context cleared by user", "Overlay");
        var workflow = GetWorkflowController();
        workflow?.StopPlaybackAndClear();
        _audioPlaybackManager?.Stop();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateFabVisualState(AppProcessingState.Idle, 0, 0);
            Toast.MakeText(this, "⏹ Playback stopped & context cleared", ToastLength.Short)?.Show();
        });
    }

    public void CloseAppAndExit()
    {
        Log.Debug(TAG, ">>> CloseAppAndExit: Shutting down VoceLens and releasing screen monitoring.");
        AppLog.Info("🛑 Long press on floating button: closing VoceLens and stopping screen capture", "Overlay");

        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        }
        catch { }

        try
        {
            var workflow = GetWorkflowController();
            workflow?.StopPlaybackAndClear();
            _audioPlaybackManager?.Stop();
        }
        catch { }

        try
        {
            ScreenCaptureManager.ResetSession();
        }
        catch { }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (_containerView != null && _windowManager != null)
                {
                    _containerView.Visibility = ViewStates.Gone;
                    _windowManager.RemoveView(_containerView);
                    _containerView = null;
                }
            }
            catch { }

            try
            {
                Toast.MakeText(this, "🛑 VoceLens закрыто", ToastLength.Short)?.Show();
            }
            catch { }

            try
            {
                var activity = Platform.CurrentActivity;
                activity?.FinishAffinity();
            }
            catch { }

            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                {
                    StopForeground(StopForegroundFlags.Remove);
                }
                else
                {
#pragma warning disable CS0618
                    StopForeground(true);
#pragma warning restore CS0618
                }
                StopSelf();
            }
            catch { }

            Task.Run(async () =>
            {
                await Task.Delay(400);
                try
                {
                    global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
                }
                catch { }
            });
        });
    }

    private void OnButtonClicked()
    {
        Log.Debug(TAG, ">>> OnButtonClicked: trigger scan & read or pause/resume");
        AppLog.Info("Floating button clicked", "Overlay");

        var workflow = GetWorkflowController();
        if (workflow == null)
        {
            Log.Error(TAG, ">>> WorkflowController is null and could not be resolved.");
            AppLog.Error("WorkflowController could not be resolved from services", null, "Overlay");
            return;
        }

        if (workflow.CurrentState == AppProcessingState.Playing)
        {
            AppLog.Info("Pausing audio playback", "Overlay");
            _audioPlaybackManager?.Pause();
            return;
        }
        else if (workflow.CurrentState == AppProcessingState.Paused)
        {
            AppLog.Info("Resuming audio playback", "Overlay");
            _audioPlaybackManager?.Resume();
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await workflow.ProcessScreenCaptureAndReadAsync();
            }
            catch (Exception ex)
            {
                Log.Error(TAG, $"Workflow execution error: {ex.Message}");
                AppLog.Error($"Workflow execution error: {ex.Message}", ex, "Overlay");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Toast.MakeText(this, $"VoceLens: {ex.Message}", ToastLength.Long)?.Show();
                });
            }
        });
    }

    private void OnWorkflowStatusChanged(object? sender, ProcessingStatusChangedEventArgs e)
    {
        if (e.State == AppProcessingState.Error)
        {
            AppLog.Error($"Status: {e.State} - {e.Message}", null, "Workflow");
        }
        else
        {
            AppLog.Info($"Status: {e.State} - {e.Message}", "Workflow");
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateFabVisualState(e.State, e.CurrentChunkIndex, e.TotalChunks);
            ShowStatusNotificationToast(e.State, e.Message);
        });
    }

    private void ShowStatusNotificationToast(AppProcessingState state, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message == "Ready")
            return;

        if (state == AppProcessingState.Error)
        {
            string friendly = message;
            if (friendly.Contains("Mistral API key is missing", StringComparison.OrdinalIgnoreCase))
                friendly = "Mistral API key is missing. Enter it in app settings.";
            else if (friendly.Contains("Screenshot capture failed", StringComparison.OrdinalIgnoreCase))
                friendly = "Failed to capture screen.";
            else if (friendly.Contains("Virtual display session expired", StringComparison.OrdinalIgnoreCase))
                friendly = "Screen capture session renewed. Tap the button again.";
            else if (friendly.Contains("401", StringComparison.OrdinalIgnoreCase) || friendly.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
                friendly = "Error 401: Invalid Mistral API key.";
            else if (friendly.Contains("429", StringComparison.OrdinalIgnoreCase))
                friendly = "Error 429: Mistral rate limit exceeded.";
            else if (friendly.Contains("No image frame was captured", StringComparison.OrdinalIgnoreCase))
                friendly = "No screen frame captured. Please tap again.";

            Toast.MakeText(this, $"⚠️ VoceLens: {friendly}", ToastLength.Long)?.Show();
        }
        else if (message.Contains("No text detected", StringComparison.OrdinalIgnoreCase))
        {
            Toast.MakeText(this, "ℹ️ VoceLens: No text detected on screen", ToastLength.Short)?.Show();
        }
    }

    private void LoadFloatingButtonIcon()
    {
        try
        {
            int resId = Resources?.GetIdentifier("vocelens_fab", "drawable", PackageName) ?? 0;
            if (resId != 0)
            {
                _floatingButton?.SetImageResource(resId);
                return;
            }

            using var stream = Assets?.Open("vocelens_fab.png");
            if (stream != null)
            {
                var bmp = global::Android.Graphics.BitmapFactory.DecodeStream(stream);
                if (bmp != null)
                {
                    _floatingButton?.SetImageBitmap(bmp);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not load floating button icon", ex, "Overlay");
        }
    }

    private void UpdateFabVisualState(AppProcessingState state, int currentChunk, int totalChunks)
    {
        if (_floatingButton?.Background is GradientDrawable bg)
        {
            int strokeWidth = 6;
            Color strokeColor = Color.White;
            Color bgColor = Color.ParseColor("#0B1829");

            switch (state)
            {
                case AppProcessingState.Idle:
                    strokeWidth = 6;
                    strokeColor = Color.White;
                    bgColor = Color.ParseColor("#0B1829");
                    break;
                case AppProcessingState.CapturingScreen:
                    strokeWidth = 8;
                    strokeColor = Color.ParseColor("#F59E0B"); // Amber
                    bgColor = Color.ParseColor("#2A2535");
                    break;
                case AppProcessingState.ScanningOcr:
                    strokeWidth = 8;
                    strokeColor = Color.ParseColor("#EAB308"); // Yellow
                    bgColor = Color.ParseColor("#2A2535");
                    break;
                case AppProcessingState.TextChunking:
                    strokeWidth = 7;
                    strokeColor = Color.ParseColor("#3B82F6"); // Blue
                    bgColor = Color.ParseColor("#0B1829");
                    break;
                case AppProcessingState.GeneratingAudio:
                    strokeWidth = 8;
                    strokeColor = Color.ParseColor("#06B6D4"); // Cyan
                    bgColor = Color.ParseColor("#132A45");
                    break;
                case AppProcessingState.Playing:
                    strokeWidth = 8;
                    strokeColor = Color.ParseColor("#10B981"); // Emerald Green
                    bgColor = Color.ParseColor("#10302B");
                    break;
                case AppProcessingState.Paused:
                    strokeWidth = 6;
                    strokeColor = Color.ParseColor("#94A3B8"); // Muted Gray
                    bgColor = Color.ParseColor("#0B1829");
                    break;
                case AppProcessingState.Error:
                    strokeWidth = 8;
                    strokeColor = Color.ParseColor("#EF4444"); // Red
                    bgColor = Color.ParseColor("#3A151D");
                    break;
            }

            bg.SetColor(bgColor);
            bg.SetStroke(strokeWidth, strokeColor);
            _floatingButton.Invalidate();
        }

        if (_stopButton != null)
        {
            _stopButton.Visibility = (state == AppProcessingState.Playing || state == AppProcessingState.Paused)
                ? ViewStates.Visible
                : ViewStates.Gone;
        }
    }

    public void ShowCropFrame()
    {
    }

    public void HideCropFrame()
    {
    }

    public void ToggleCropFrame()
    {
    }

    public Task SetOverlayVisibleAsync(bool visible)
    {
        var tcs = new TaskCompletionSource<bool>();

        void ApplyVisibility()
        {
            try
            {
                if (_containerView != null && _windowManager != null && _buttonLayoutParams != null)
                {
                    _containerView.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
                    _buttonLayoutParams.Alpha = visible ? 1.0f : 0.0f;
                    _windowManager.UpdateViewLayout(_containerView, _buttonLayoutParams);
                }
                else if (_containerView != null)
                {
                    _containerView.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
                }
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Log.Warn(TAG, $"Failed to set overlay visibility: {ex.Message}");
                tcs.TrySetResult(false);
            }
        }

        if (MainThread.IsMainThread)
        {
            ApplyVisibility();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(ApplyVisibility);
        }

        return tcs.Task;
    }

    public void SetOverlayVisible(bool visible)
    {
        _ = SetOverlayVisibleAsync(visible);
    }

    public override void OnDestroy()
    {
        Instance = null;

        ScreenCaptureManager.ResetSession();

        if (_workflowController != null)
        {
            _workflowController.StatusChanged -= OnWorkflowStatusChanged;
        }

        if (_windowManager != null)
        {
            if (_containerView != null)
            {
                try { _windowManager.RemoveView(_containerView); } catch { }
                _containerView = null;
            }
            _floatingButton = null;
            _stopButton = null;
        }

        base.OnDestroy();
    }
}
#endif
