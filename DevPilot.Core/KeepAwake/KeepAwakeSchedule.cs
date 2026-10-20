using DevPilot.Models;

namespace DevPilot.Core.KeepAwake;

public static class KeepAwakeSchedule
{
    /// <summary>True when keep-awake should be active at <paramref name="now"/>.</summary>
    public static bool IsActive(KeepAwakeSettings s, DateTime now)
    {
        if (!s.Enabled) return false;
        if (!s.ScheduleEnabled) return true;
        int day = now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek;
        if (!TryParse(s.ScheduleFrom, out var from) || !TryParse(s.ScheduleTo, out var to)) return true;
        var t = now.TimeOfDay;
        if (from <= to)
            return s.ScheduleDays.Contains(day) && t >= from && t < to;
        // Overnight window (e.g. 22:00 – 06:00): the part after midnight belongs to the previous day.
        if (t >= from) return s.ScheduleDays.Contains(day);
        int prev = day == 1 ? 7 : day - 1;
        return t < to && s.ScheduleDays.Contains(prev);
    }

    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (TimeSpan.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out value)
            && value >= TimeSpan.Zero && value < TimeSpan.FromDays(1))
            return true;
        return false;
    }
}
