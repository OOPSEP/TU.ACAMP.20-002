using System;

namespace TU.ACAMP.Domain.Tools;

/// <summary>
/// Internationalized TimeZone and DateTime helper for TU.ACAMP application.
/// Supports dynamic timezone resolution across Linux/Windows (IANA & Windows TimeZone IDs),
/// tenant/user timezone preferences, and standardized Reporting Period normalization worldwide.
/// </summary>
public static class AppTimeZone
{
    public const string DefaultTimeZoneId = "Asia/Bangkok";
    private static string _configuredTimeZoneId = DefaultTimeZoneId;
    private static TimeZoneInfo _cachedDefaultZone = ResolveTimeZone(DefaultTimeZoneId);

    /// <summary>
    /// Configures the global default timezone ID for the application (e.g. from appsettings "App:TimeZone").
    /// </summary>
    public static void SetDefaultTimeZone(string? timeZoneId)
    {
        #region hide [2026-08-24]
        if (string.IsNullOrWhiteSpace(timeZoneId)) return;

        try
        {
            var zone = ResolveTimeZone(timeZoneId);
            _cachedDefaultZone = zone;
            _configuredTimeZoneId = timeZoneId;
        }
        catch
        {
            // Keep existing default if invalid
        }
        #endregion
    }

    /// <summary>
    /// Gets the application default TimeZoneInfo.
    /// </summary>
    public static TimeZoneInfo Zone => _cachedDefaultZone;

    /// <summary>
    /// Gets the current UTC DateTime.
    /// </summary>
    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>
    /// Gets the current local DateTime in the specified or default TimeZone.
    /// </summary>
    public static DateTime GetLocalNow(TimeZoneInfo? targetZone = null)
    {
        #region hide [2026-08-24]
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, targetZone ?? Zone);
        #endregion
    }

    /// <summary>
    /// Gets the current local DateTime in the default TimeZone.
    /// </summary>
    public static DateTime LocalNow => GetLocalNow(Zone);

    /// <summary>
    /// Converts a UTC DateTime to a local DateTime in the target TimeZone (default or specified).
    /// </summary>
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo? targetZone = null)
    {
        #region hide [2026-08-24]
        var zone = targetZone ?? Zone;

        if (utc.Kind == DateTimeKind.Local)
        {
            utc = utc.ToUniversalTime();
        }
        else if (utc.Kind == DateTimeKind.Unspecified)
        {
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        #endregion
    }

    /// <summary>
    /// Converts a UTC DateTime to a local DateTime in the specified TimeZone ID.
    /// </summary>
    public static DateTime ToLocal(DateTime utc, string timeZoneId)
    {
        #region hide [2026-08-24]
        return ToLocal(utc, ResolveTimeZone(timeZoneId));
        #endregion
    }

    /// <summary>
    /// Converts a local DateTime to UTC DateTime using the target TimeZone (default or specified).
    /// </summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo? targetZone = null)
    {
        #region hide [2026-08-24]
        if (local.Kind == DateTimeKind.Utc)
        {
            return local;
        }

        var zone = targetZone ?? Zone;
        var unspecifiedLocal = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecifiedLocal, zone);
        #endregion
    }

    /// <summary>
    /// Converts a local DateTime to UTC DateTime using the specified TimeZone ID.
    /// </summary>
    public static DateTime ToUtc(DateTime local, string timeZoneId)
    {
        #region hide [2026-08-24]
        return ToUtc(local, ResolveTimeZone(timeZoneId));
        #endregion
    }

    /// <summary>
    /// Normalizes any DateTime to a Reporting Period (1st day of month 00:00:00 in target local timezone).
    /// </summary>
    public static DateTime ToReportingPeriod(DateTime any, TimeZoneInfo? targetZone = null)
    {
        #region hide [2026-08-24]
        DateTime localTime = any.Kind == DateTimeKind.Utc ? ToLocal(any, targetZone) : any;
        return new DateTime(localTime.Year, localTime.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        #endregion
    }

    /// <summary>
    /// Normalizes a DateTimeOffset (e.g. from international API ingestion) to a Reporting Period (1st of month 00:00:00 in target local timezone).
    /// </summary>
    public static DateTime ToReportingPeriod(DateTimeOffset offset, TimeZoneInfo? targetZone = null)
    {
        #region hide [2026-08-24]
        var zone = targetZone ?? Zone;
        DateTime localTime = TimeZoneInfo.ConvertTime(offset, zone).DateTime;
        return new DateTime(localTime.Year, localTime.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        #endregion
    }

    /// <summary>
    /// Formats a UTC DateTime as a local time string in the target TimeZone.
    /// </summary>
    public static string FormatLocal(DateTime utc, string format = "dd/MM/yyyy HH:mm:ss", TimeZoneInfo? targetZone = null)
    {
        #region hide [2026-08-24]
        return ToLocal(utc, targetZone).ToString(format);
        #endregion
    }

    /// <summary>
    /// Formats a Reporting Period DateTime as "yyyy-MM".
    /// </summary>
    public static string FormatPeriod(DateTime period)
    {
        #region hide [2026-08-24]
        return period.ToString("yyyy-MM");
        #endregion
    }

    /// <summary>
    /// Resolves a TimeZoneInfo from an IANA or Windows TimeZone ID cross-platform.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        #region hide [2026-08-24]
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return Zone;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Try IANA to Windows conversion if running on Windows
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var winId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(winId); } catch { }
            }

            // Try Windows to IANA conversion if running on Linux/macOS
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); } catch { }
            }

            // Fallback for Bangkok Windows ID
            if (timeZoneId.Equals("Asia/Bangkok", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            if (timeZoneId.Equals("SE Asia Standard Time", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
            }

            return TimeZoneInfo.Utc;
        }
        #endregion
    }
}
