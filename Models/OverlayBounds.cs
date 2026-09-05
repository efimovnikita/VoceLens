namespace VoceLens.Models;

public class CropFrameBounds
{
    public bool IsEnabled { get; set; } = true;
    public int TopPercent { get; set; } = 8;
    public int BottomPercent { get; set; } = 8;
    public int LeftPercent { get; set; } = 0;
    public int RightPercent { get; set; } = 0;

    // Legacy pixel fields maintained for compatibility
    public int X { get; set; } = 0;
    public int Y { get; set; } = 0;
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;

    public CropFrameBounds()
    {
    }

    public CropFrameBounds(int topPercent, int bottomPercent, int leftPercent, int rightPercent, bool isEnabled = true)
    {
        TopPercent = topPercent;
        BottomPercent = bottomPercent;
        LeftPercent = leftPercent;
        RightPercent = rightPercent;
        IsEnabled = isEnabled;
    }

    public CropFrameBounds(int x, int y, int width, int height, bool isEnabled, int topPercent = 8, int bottomPercent = 8, int leftPercent = 0, int rightPercent = 0)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        IsEnabled = isEnabled;
        TopPercent = topPercent;
        BottomPercent = bottomPercent;
        LeftPercent = leftPercent;
        RightPercent = rightPercent;
    }
}
