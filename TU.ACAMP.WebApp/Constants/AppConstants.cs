namespace TU.ACAMP.WebApp.Constants;

public static class GridStyles
{
    public const string GridExtend =
        "--rz-grid-header-text-transform: none;" +
        "--rz-grid-header-font-size: 14px;" +
        "height:auto;" +
        "max-height: calc(100vh - 250px);" +
        "overflow-y:auto;" +
        "overflow-x:auto;";

    public const string Default =
        "--rz-grid-header-text-transform: none;" +
        "--rz-grid-header-font-size: 14px;" +
        "max-height:620px;" +
        "overflow:auto;" +
        "min-height:320px;" +
        "overflow-x:auto;";
}

public static class FileConstants
{
    public const int DefaultMaxUploadSizeMB = 10;
    public const long MaxAllowedSize = 25L * 1024 * 1024; // Absolute ceiling fallback

    public static long GetMaxUploadSizeBytes(int? allowDataUploadCountMb)
    {
        var mb = (allowDataUploadCountMb.HasValue && allowDataUploadCountMb.Value > 0)
            ? allowDataUploadCountMb.Value
            : DefaultMaxUploadSizeMB;
        return (long)mb * 1024 * 1024;
    }

    public static string FormatFileSize(long sizeInBytes)
    {
        return sizeInBytes switch
        {
            >= 1024 * 1024 => $"{sizeInBytes / (1024 * 1024.0):F2} MB",
            >= 1024        => $"{sizeInBytes / 1024.0:F2} KB",
            _              => $"{sizeInBytes} bytes"
        };
    }
}
