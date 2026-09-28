using System;
using System.IO;

namespace TU.ACAMP.Domain.Tools;

/// <summary>
/// Helper for generating standardized export file names across ACAMP system.
/// Format: {entity}_{name}_{date}_{time}.{ext}
/// </summary>
public static class ExportFileNameHelper
{
    // [2026-09-21][Gemini] Standard Export Filename Standard implementation: {entity}_{name}_{date}_{time}.{ext}

    /// <summary>
    /// Generates a standardized export file name in format: {entity}_{name}_{date}_{time}.{extension}
    /// </summary>
    /// <param name="entity">Entity name (e.g. "topics", "events", "emission_sources")</param>
    /// <param name="rawName">Context, facility, organization, or topic name</param>
    /// <param name="extension">File extension without leading dot (default: "xlsx")</param>
    /// <param name="timestamp">Optional timestamp; defaults to AppTimeZone.LocalNow</param>
    /// <param name="fallback">Fallback name if rawName is null or empty (default: "all")</param>
    /// <returns>Standardized export file name</returns>
    public static string Generate(
        string entity,
        string? rawName,
        string extension = "xlsx",
        DateTime? timestamp = null,
        string fallback = "all")
    {
        // [2026-09-21][Gemini] Build standardized filename components
        var cleanEntity = SanitizeEntity(entity);
        var cleanName = SanitizeName(rawName, fallback);
        var now = timestamp ?? AppTimeZone.LocalNow;
        var cleanExt = (extension ?? "xlsx").TrimStart('.').ToLowerInvariant();

        return $"{cleanEntity}_{cleanName}_{now:yyyyMMdd}_{now:HHmmss}.{cleanExt}";
    }

    /// <summary>
    /// Sanitizes the entity name into lowercase snake_case without invalid characters.
    /// </summary>
    public static string SanitizeEntity(string? entity)
    {
        if (string.IsNullOrWhiteSpace(entity)) return "data";
        var text = entity.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(text) ? "data" : text.Trim('_');
    }

    /// <summary>
    /// Sanitizes a context/facility/topic name, stripping category prefixes and invalid file characters.
    /// </summary>
    public static string SanitizeName(string? rawName, string fallback = "all")
    {
        if (string.IsNullOrWhiteSpace(rawName)) return fallback;

        // Strip prefixes such as "Facility: " or "Company: " if present
        var idx = rawName.IndexOf(':');
        var text = (idx >= 0 ? rawName[(idx + 1)..] : rawName).Trim();

        if (string.IsNullOrWhiteSpace(text)) return fallback;

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }
}
