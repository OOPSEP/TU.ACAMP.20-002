using Microsoft.AspNetCore.DataProtection;

namespace TU.ACAMP.WebApp.Services.Common;

public static class LinkProtector
{
    public const string Purpose = "ProtectLink";

    public static string Protect(this IDataProtectionProvider dataProtection, string? plainText)
    {
        #region hide [2026-08-15]
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        try
        {
            var protector = dataProtection.CreateProtector(Purpose);
            return DataProtectionCommonExtensions.Protect(protector, plainText);
        }
        catch
        {
            return string.Empty;
        }
        #endregion
    }

    public static string Protect(this IDataProtectionProvider dataProtection, int value)
    {
        #region hide [2026-08-15]
        return dataProtection.Protect(value.ToString());
        #endregion
    }

    public static string Protect(this IDataProtectionProvider dataProtection, Guid value)
    {
        #region hide [2026-08-15]
        return dataProtection.Protect(value.ToString());
        #endregion
    }

    public static string? Unprotect(this IDataProtectionProvider dataProtection, string? cipherText)
    {
        #region hide [2026-08-15]
        if (string.IsNullOrWhiteSpace(cipherText)) return null;
        try
        {
            var protector = dataProtection.CreateProtector(Purpose);
            return DataProtectionCommonExtensions.Unprotect(protector, cipherText);
        }
        catch
        {
            // Fallback if not encrypted (e.g. legacy/development query params)
            return cipherText;
        }
        #endregion
    }

    public static int? UnprotectInt(this IDataProtectionProvider dataProtection, string? cipherText)
    {
        #region hide [2026-08-15]
        if (string.IsNullOrWhiteSpace(cipherText)) return null;
        try
        {
            var protector = dataProtection.CreateProtector(Purpose);
            var plain = DataProtectionCommonExtensions.Unprotect(protector, cipherText);
            if (int.TryParse(plain, out int result)) return result;
        }
        catch
        {
            // Fallback if plain int was passed
            if (int.TryParse(cipherText, out int fallback)) return fallback;
        }
        return null;
        #endregion
    }

    public static Guid? UnprotectGuid(this IDataProtectionProvider dataProtection, string? cipherText)
    {
        #region hide [2026-08-15]
        if (string.IsNullOrWhiteSpace(cipherText)) return null;
        try
        {
            var protector = dataProtection.CreateProtector(Purpose);
            var plain = DataProtectionCommonExtensions.Unprotect(protector, cipherText);
            if (Guid.TryParse(plain, out Guid result)) return result;
        }
        catch
        {
            if (Guid.TryParse(cipherText, out Guid fallback)) return fallback;
        }
        return null;
        #endregion
    }
}
