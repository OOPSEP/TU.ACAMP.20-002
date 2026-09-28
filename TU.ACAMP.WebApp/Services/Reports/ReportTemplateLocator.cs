namespace TU.ACAMP.WebApp.Services.Reports;

/// <summary>
/// [2026-09-23][Claude] ค้นหาไฟล์เทมเพลตรายงานจุดเดียว ใช้ร่วมทั้ง Excel (AR5) และ Word (รายงาน CFO)
/// ลำดับ: ค่าใน config -> สแกน Files/Template, Files/Excels ใต้ ContentRoot / WebRoot / BaseDirectory ตามรูปแบบชื่อไฟล์
/// </summary>
public static class ReportTemplateLocator
{
    // [2026-09-23][Claude] ย้ายมาจาก CfoReportService.TemplateSubFolders เดิม ลำดับเหมือนเดิม
    public static readonly string[] TemplateSubFolders = { "Template", "Excels" };

    /// <param name="searchPattern">เช่น "*.xlsx" หรือ "*.docx"</param>
    /// <param name="nameMarker">คำที่ต้องมีในชื่อไฟล์ เช่น "AR5"</param>
    /// <returns>path เต็มของไฟล์ หรือ string.Empty ถ้าไม่พบ</returns>
    public static string Find(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger logger,
        string configKey,
        string searchPattern,
        string nameMarker)
    {
        var configured = configuration[configKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var configuredPath = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(env.ContentRootPath, configured);

            if (File.Exists(configuredPath))
            {
                return configuredPath;
            }

            logger.LogWarning("ไม่พบไฟล์ template ตามค่าใน config '{Key}' = '{Path}' จะสแกนโฟลเดอร์มาตรฐานแทน", configKey, configuredPath);
        }

        var roots = new[] { env.ContentRootPath, env.WebRootPath, AppContext.BaseDirectory };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;

            foreach (var sub in TemplateSubFolders)
            {
                var dir = Path.Combine(root, "Files", sub);
                if (!Directory.Exists(dir)) continue;

                var match = Directory.EnumerateFiles(dir, searchPattern)
                    .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                    .Where(f => Path.GetFileName(f).Contains(nameMarker, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (match != null)
                {
                    return match;
                }
            }
        }

        return string.Empty;
    }
}
