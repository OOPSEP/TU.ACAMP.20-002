using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TU.ACAMP.WebApp.Data.Converters;

/// <summary>
/// Ensures DateTime properties stored as UTC are read back with DateTimeKind.Utc specified,
/// preventing EF Core / MySQL from returning DateTimeKind.Unspecified.
/// </summary>
public class DateTimeUtcValueConverter : ValueConverter<DateTime, DateTime>
{
    public DateTimeUtcValueConverter() : base(
        v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

public class NullableDateTimeUtcValueConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableDateTimeUtcValueConverter() : base(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}
