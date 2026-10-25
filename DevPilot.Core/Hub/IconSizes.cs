namespace DevPilot.Core.Hub;

/// <summary>Which pixel size to ask for, and which frame of a multi-size icon to take.</summary>
public static class IconSizes
{
    private static readonly int[] TraySizes = [16, 20, 24, 32, 48];

    /// <summary>Tray icon size for a display DPI (96 = 100%): 16 px at 100%, 20 at 125%, 24 at 150%, 32 at 200%, 48 at 300%.</summary>
    public static int TrayPixels(int dpi)
    {
        var wanted = 16.0 * Math.Max(96, dpi) / 96.0;
        foreach (var size in TraySizes)
            if (size >= wanted - 0.5) return size;
        return TraySizes[^1];
    }

    /// <summary>The smallest frame that is at least <paramref name="wanted"/> pixels, else the largest one. -1 when there are none.</summary>
    public static int PickFrame(IReadOnlyList<int> available, int wanted)
    {
        if (available.Count == 0) return -1;
        var best = -1;
        for (var i = 0; i < available.Count; i++)
        {
            var size = available[i];
            if (size >= wanted && (best < 0 || size < available[best])) best = i;
        }
        if (best >= 0) return best;
        best = 0;
        for (var i = 1; i < available.Count; i++)
            if (available[i] > available[best]) best = i;
        return best;
    }
}
