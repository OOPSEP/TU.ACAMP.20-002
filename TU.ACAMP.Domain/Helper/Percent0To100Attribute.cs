using System.ComponentModel.DataAnnotations;

namespace TU.ACAMP.Domain.Helper;

/// <summary>
/// Validates that a numeric property value is between 0 (inclusive) and 100 (inclusive).
/// Useful for percentage fields.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class Percent0To100Attribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null) return true; // let [Required] handle nulls

        var v = Convert.ToDouble(value);
        // [GHG-16] Allow 0% to temporarily disable emission share without deleting configuration
        return v >= 0 && v <= 100;
    }

    public override string FormatErrorMessage(string name)
        => $"{name} (%) must be ≥ 0 and ≤ 100.";
}
