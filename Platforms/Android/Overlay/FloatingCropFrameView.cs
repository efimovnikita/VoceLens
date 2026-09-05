#if ANDROID
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using VoceLens.Models;
using Color = Android.Graphics.Color;
using View = Android.Views.View;
using AndroidButton = Android.Widget.Button;

namespace VoceLens.Platforms.Android.Overlay;

public class FloatingCropFrameView : LinearLayout
{
    private readonly IWindowManager _windowManager;
    private readonly WindowManagerLayoutParams _layoutParams;
    private readonly Action<CropFrameBounds> _onBoundsChanged;
    private readonly Action _onCaptureRequested;
    private readonly Action _onCloseRequested;

    private TextView? _titleText;
    private View? _cropViewport;
    private ImageView? _resizeHandle;
    private LinearLayout? _headerBar;

    public CropFrameBounds CurrentBounds { get; private set; }

    public FloatingCropFrameView(
        Context context,
        IWindowManager windowManager,
        WindowManagerLayoutParams layoutParams,
        CropFrameBounds initialBounds,
        Action<CropFrameBounds> onBoundsChanged,
        Action onCaptureRequested,
        Action onCloseRequested) : base(context)
    {
        _windowManager = windowManager;
        _layoutParams = layoutParams;
        CurrentBounds = initialBounds;
        _onBoundsChanged = onBoundsChanged;
        _onCaptureRequested = onCaptureRequested;
        _onCloseRequested = onCloseRequested;

        Orientation = Orientation.Vertical;
        InitializeUi();
    }

    private void InitializeUi()
    {
        // 1. Header Bar (for dragging and controls)
        _headerBar = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent)
        };
        _headerBar.SetPadding(24, 16, 24, 16);

        var headerBg = new GradientDrawable();
        headerBg.SetColor(Color.Argb(230, 26, 32, 44)); // Dark slate
        headerBg.SetCornerRadii(new float[] { 24, 24, 24, 24, 0, 0, 0, 0 });
        _headerBar.Background = headerBg;

        _titleText = new TextView(Context)
        {
            Text = $"OCR Frame ({CurrentBounds.Width}x{CurrentBounds.Height})",
            TextSize = 13,
            LayoutParameters = new LayoutParams(0, LayoutParams.WrapContent, 1.0f)
        };
        _titleText.SetTextColor(Color.Argb(255, 230, 240, 255));
        _titleText.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);

        var captureButton = new AndroidButton(Context)
        {
            Text = "Scan",
            TextSize = 12,
            LayoutParameters = new LayoutParams(LayoutParams.WrapContent, 80)
        };
        captureButton.SetPadding(20, 0, 20, 0);
        captureButton.SetTextColor(Color.White);
        var captureBg = new GradientDrawable();
        captureBg.SetColor(Color.Argb(255, 79, 70, 229)); // Indigo
        captureBg.SetCornerRadius(16);
        captureButton.Background = captureBg;
        captureButton.Click += (s, e) => _onCaptureRequested();

        var closeButton = new AndroidButton(Context)
        {
            Text = "X",
            TextSize = 12,
            LayoutParameters = new LayoutParams(70, 80)
        };
        closeButton.SetPadding(0, 0, 0, 0);
        closeButton.SetTextColor(Color.White);
        var closeBg = new GradientDrawable();
        closeBg.SetColor(Color.Argb(180, 239, 68, 68)); // Red
        closeBg.SetCornerRadius(16);
        closeButton.Background = closeBg;
        closeButton.Click += (s, e) => _onCloseRequested();

        _headerBar.AddView(_titleText);
        _headerBar.AddView(captureButton);
        _headerBar.AddView(closeButton);

        // 2. Viewport Frame (Transparent center with glowing border)
        var viewportContainer = new RelativeLayout(Context)
        {
            LayoutParameters = new LayoutParams(LayoutParams.MatchParent, 0, 1.0f)
        };

        _cropViewport = new View(Context)
        {
            LayoutParameters = new RelativeLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent)
        };
        var viewportBorder = new GradientDrawable();
        viewportBorder.SetColor(Color.Argb(25, 99, 102, 241)); // Light tint
        viewportBorder.SetStroke(5, Color.Argb(240, 99, 102, 241)); // Glowing indigo border
        _cropViewport.Background = viewportBorder;

        // 3. Resize Handle in bottom right corner
        _resizeHandle = new ImageView(Context);
        var resizeParams = new RelativeLayout.LayoutParams(70, 70);
        resizeParams.AddRule(LayoutRules.AlignParentBottom);
        resizeParams.AddRule(LayoutRules.AlignParentRight);
        _resizeHandle.LayoutParameters = resizeParams;

        var resizeBg = new GradientDrawable();
        resizeBg.SetColor(Color.Argb(230, 99, 102, 241));
        resizeBg.SetCornerRadii(new float[] { 24, 0, 0, 0, 0, 0, 0, 0 });
        _resizeHandle.Background = resizeBg;
        _resizeHandle.SetPadding(16, 16, 16, 16);

        viewportContainer.AddView(_cropViewport);
        viewportContainer.AddView(_resizeHandle);

        AddView(_headerBar);
        AddView(viewportContainer);

        SetupHeaderDragListener();
        SetupResizeHandleTouchListener();
    }

    private void SetupHeaderDragListener()
    {
        if (_headerBar == null) return;

        int initialX = 0, initialY = 0;
        float initialTouchX = 0, initialTouchY = 0;

        _headerBar.Touch += (sender, e) =>
        {
            switch (e.Event?.Action)
            {
                case MotionEventActions.Down:
                    initialX = _layoutParams.X;
                    initialY = _layoutParams.Y;
                    initialTouchX = e.Event.RawX;
                    initialTouchY = e.Event.RawY;
                    e.Handled = true;
                    break;

                case MotionEventActions.Move:
                    _layoutParams.X = initialX + (int)(e.Event.RawX - initialTouchX);
                    _layoutParams.Y = initialY + (int)(e.Event.RawY - initialTouchY);
                    _windowManager.UpdateViewLayout(this, _layoutParams);

                    CurrentBounds.X = _layoutParams.X;
                    CurrentBounds.Y = _layoutParams.Y;
                    _onBoundsChanged(CurrentBounds);
                    e.Handled = true;
                    break;

                default:
                    e.Handled = false;
                    break;
            }
        };
    }

    private void SetupResizeHandleTouchListener()
    {
        if (_resizeHandle == null) return;

        int initialWidth = 0, initialHeight = 0;
        float initialTouchX = 0, initialTouchY = 0;

        _resizeHandle.Touch += (sender, e) =>
        {
            switch (e.Event?.Action)
            {
                case MotionEventActions.Down:
                    initialWidth = _layoutParams.Width;
                    initialHeight = _layoutParams.Height;
                    initialTouchX = e.Event.RawX;
                    initialTouchY = e.Event.RawY;
                    e.Handled = true;
                    break;

                case MotionEventActions.Move:
                    int newWidth = Math.Max(250, initialWidth + (int)(e.Event.RawX - initialTouchX));
                    int newHeight = Math.Max(200, initialHeight + (int)(e.Event.RawY - initialTouchY));

                    _layoutParams.Width = newWidth;
                    _layoutParams.Height = newHeight;
                    _windowManager.UpdateViewLayout(this, _layoutParams);

                    CurrentBounds.Width = newWidth;
                    CurrentBounds.Height = newHeight;

                    if (_titleText != null)
                    {
                        _titleText.Text = $"OCR Frame ({newWidth}x{newHeight})";
                    }

                    _onBoundsChanged(CurrentBounds);
                    e.Handled = true;
                    break;

                default:
                    e.Handled = false;
                    break;
            }
        };
    }
}
#endif
