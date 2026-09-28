using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Common;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Constants;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Activity;

public class EventAttachmentService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EventAttachmentService> _logger;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx", ".xls", ".xlsx"
    };

    public EventAttachmentService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IWebHostEnvironment env,
        ILogger<EventAttachmentService> logger)
    {
        #region hide [2026-08-24]
        _dbFactory = dbFactory;
        _env = env;
        _logger = logger;
        #endregion
    }

    public class AttachmentResult
    {
        public bool Success { get; set; }
        public bool IsDuplicate { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public TbFileEntity? FileEntity { get; set; }
    }

    public async Task<AttachmentResult> AttachFileToEventAsync(
        int subscriptionId,
        int eventId,
        Stream fileStream,
        string fileName,
        string contentType,
        string? tag = null,
        CancellationToken ct = default)
    {
        #region hide [2026-08-24]
        // 1. Tier 1 Validation: Extension
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            return new AttachmentResult
            {
                Success = false,
                ErrorCode = "unsupported_file_type",
                ErrorMessage = $"File extension '{ext}' is not supported."
            };
        }

        // 2. Stream to temp file and compute SHA-256
        var tempPath = Path.Combine(Path.GetTempPath(), $"acamp_attach_{Guid.NewGuid():N}.tmp");
        string fileHash;
        long fileSize = 0;

        try
        {
            using (var tempFileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sha256 = SHA256.Create())
            using (var cryptoStream = new CryptoStream(tempFileStream, sha256, CryptoStreamMode.Write))
            {
                await fileStream.CopyToAsync(cryptoStream, ct);
                cryptoStream.FlushFinalBlock();
                fileHash = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
                fileSize = tempFileStream.Length;
            }

            using var dbCheck = await _dbFactory.CreateDbContextAsync(ct);
            var sub = subscriptionId > 0
                ? await dbCheck.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subscriptionId && !s.IsDeleted, ct)
                : null;
            var maxBytes = FileConstants.GetMaxUploadSizeBytes(sub?.AllowDataUploadCount);

            if (fileSize > maxBytes)
            {
                File.Delete(tempPath);
                return new AttachmentResult
                {
                    Success = false,
                    ErrorCode = "file_too_large",
                    ErrorMessage = $"File size exceeds the allowed limit of {FileConstants.FormatFileSize(maxBytes)}."
                };
            }

            // 3. Tier 3 Validation: Magic Bytes
            byte[] header = new byte[8];
            using (var checkStream = File.OpenRead(tempPath))
            {
                await checkStream.ReadExactlyAsync(header, 0, header.Length, ct);
            }

            if (!VerifyMagicBytes(ext, header))
            {
                File.Delete(tempPath);
                return new AttachmentResult
                {
                    Success = false,
                    ErrorCode = "file_content_mismatch",
                    ErrorMessage = "File content signature does not match extension."
                };
            }

            // 4. Physical Storage Deduplication
            var now = AppTimeZone.UtcNow;
            var subFolder = Path.Combine("events", now.ToString("yyyy"), now.ToString("MM"), fileHash.Substring(0, 2));
            var storeDir = Path.Combine(_env.ContentRootPath, "Files", "store", subFolder);
            if (!Directory.Exists(storeDir))
            {
                Directory.CreateDirectory(storeDir);
            }

            var physicalFilePath = Path.Combine(storeDir, $"{fileHash}.dat");
            if (!File.Exists(physicalFilePath))
            {
                File.Move(tempPath, physicalFilePath);
            }
            else
            {
                File.Delete(tempPath);
            }

            var relativeUrl = Path.Combine("events", now.ToString("yyyy"), now.ToString("MM"), fileHash.Substring(0, 2), $"{fileHash}.dat").Replace('\\', '/');

            var fileEntity = new TbFileEntity
            {
                FileName = Path.GetFileName(fileName),
                Extension = ext,
                ContentType = contentType,
                Size = fileSize,
                Url = relativeUrl,
                UploadDate = now,
                CreatedBy = "API",
                FileHash = fileHash,
                Tag = tag ?? string.Empty,
                StorageProvider = eStorageProvider.Local
            };

            // 5. Append to TbActivityEvent.JsonDataFiles in DB transaction
            using var db = await _dbFactory.CreateDbContextAsync(ct);
            var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.SubscriptionId == subscriptionId && !e.IsDeleted, ct);
            if (ev == null)
            {
                return new AttachmentResult
                {
                    Success = false,
                    ErrorCode = "event_not_found",
                    ErrorMessage = "Activity event not found or not accessible."
                };
            }

            var json = ev.JsonDataFiles?.ToJsonString(null) ?? "[]";
            var files = TbFileEntity.DeserializeListFromJson(json) ?? new List<TbFileEntity>();

            if (files.Any(f => f.FileHash == fileHash))
            {
                return new AttachmentResult
                {
                    Success = true,
                    IsDuplicate = true,
                    FileEntity = files.First(f => f.FileHash == fileHash)
                };
            }

            files.Add(fileEntity);

            ev.JsonDataFiles = JsonNode.Parse(TbFileEntity.SerializeListToJson(files))!;
            await db.SaveChangesAsync(ct);

            return new AttachmentResult
            {
                Success = true,
                IsDuplicate = false,
                FileEntity = fileEntity
            };
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
        #endregion
    }

    private static bool VerifyMagicBytes(string extension, byte[] header)
    {
        #region hide [2026-08-24]
        var ext = extension.ToLowerInvariant();
        if (ext == ".pdf")
        {
            return header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;
        }
        if (ext == ".jpg" || ext == ".jpeg")
        {
            return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        }
        if (ext == ".png")
        {
            return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        }
        if (ext == ".docx" || ext == ".xlsx")
        {
            return header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
        }
        if (ext == ".doc" || ext == ".xls")
        {
            return header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0;
        }

        return true;
        #endregion
    }
}
