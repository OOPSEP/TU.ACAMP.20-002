using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Common;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.WebApp.Constants;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Common;

public class FileStorageService : BaseService
{
    #region class field  
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FileStorageService> _logger;
    #endregion

    #region class constructor
    public FileStorageService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IWebHostEnvironment env,
        AuthenticationStateProvider authStateProvider,
        ILogger<FileStorageService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-17]
        _logger = logger;
        _env = env;
        #endregion
    }
    #endregion

    #region class methods 
    public async Task<TbFileEntity?> SaveFileAsync(IBrowserFile file, string subFolder, long? maxAllowedSizeBytes = null)
    {
        #region hide [2026-08-17]
        try
        {
            if (file == null) return null;

            // Ensure valid subFolder name
            var safeSubFolder = string.IsNullOrWhiteSpace(subFolder) ? "default" : subFolder;

            // Define the storage folder path
            var storeFolderPath = Path.Combine(_env.ContentRootPath, "Files", "store", safeSubFolder);
            if (!Directory.Exists(storeFolderPath))
            {
                Directory.CreateDirectory(storeFolderPath);
            }

            // Generate a unique file name to prevent overwrites
            var extension = Path.GetExtension(file.Name);
            var uniqueFileName = $"{Guid.NewGuid()}{extension}.dat";
            var filePath = Path.Combine(storeFolderPath, uniqueFileName);

            var maxLimit = maxAllowedSizeBytes.HasValue && maxAllowedSizeBytes.Value > 0
                ? maxAllowedSizeBytes.Value
                : FileConstants.MaxAllowedSize;

            // Save file to disk
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.OpenReadStream(maxAllowedSize: maxLimit).CopyToAsync(stream);
            }

            // Build relative path with forward slashes for cross-platform compatibility
            var relativeUrl = $"{safeSubFolder}/{uniqueFileName}".Replace('\\', '/');

            _logger.LogInformation("File {FileName} successfully saved to {Path}", file.Name, relativeUrl);
            var userId = await GetCurrentUserIdAsync();
            return new TbFileEntity
            {
                FileName = file.Name,
                Extension = extension,
                ContentType = file.ContentType,
                Size = file.Size,
                Url = relativeUrl,
                UploadDate = DateTime.UtcNow,
                CreatedBy = userId,
                StorageProvider = eStorageProvider.Local
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file: {FileName}", file?.Name);
            throw;
        }
        #endregion
    }

    public async Task<TbFileEntity?> SaveFileAsync(
        Stream content, string originalFileName, string contentType,
        string subFolder, string createdBy, CancellationToken ct = default)
    {
        try
        {
            if (content == null || string.IsNullOrWhiteSpace(originalFileName)) return null;

            var safeSubFolder = string.IsNullOrWhiteSpace(subFolder) ? "default" : subFolder;
            var storeFolderPath = Path.Combine(_env.ContentRootPath, "Files", "store", safeSubFolder);
            if (!Directory.Exists(storeFolderPath))
            {
                Directory.CreateDirectory(storeFolderPath);
            }

            var extension = Path.GetExtension(originalFileName);
            var uniqueFileName = $"{Guid.NewGuid()}{extension}.dat";
            var filePath = Path.Combine(storeFolderPath, uniqueFileName);

            long size = 0;
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await content.CopyToAsync(fileStream, ct);
                size = fileStream.Length;
            }

            var relativeUrl = $"{safeSubFolder}/{uniqueFileName}".Replace('\\', '/');

            _logger.LogInformation("File {FileName} successfully saved via Stream overload to {Path}", originalFileName, relativeUrl);
            return new TbFileEntity
            {
                FileName = originalFileName,
                Extension = extension,
                ContentType = contentType,
                Size = size,
                Url = relativeUrl,
                UploadDate = TU.ACAMP.Domain.Tools.AppTimeZone.UtcNow,
                CreatedBy = createdBy,
                StorageProvider = eStorageProvider.Local
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file via Stream overload: {FileName}", originalFileName);
            throw;
        }
    }

    public async Task<byte[]?> ReadFileAsync(TbFileEntity fileEntity)
    {
        #region hide [2026-08-17]
        if (fileEntity == null) return null;

        if (fileEntity.StorageProvider == eStorageProvider.GoogleDrive)
        {
            _logger.LogWarning("Google Drive storage provider is not currently configured for direct read: {FileName}", fileEntity.FileName);
            return null;
        }

        return await ReadFileAsync(fileEntity.Url);
        #endregion
    }

    public async Task<byte[]?> ReadFileAsync(string relativeUrl)
    {
        #region hide [2026-08-17]
        try
        {
            var fullPath = GetFullPath(relativeUrl);
            if (string.IsNullOrEmpty(fullPath)) return null;

            if (File.Exists(fullPath))
            {
                return await File.ReadAllBytesAsync(fullPath);
            }

            _logger.LogWarning("Physical file not found for reading: {Path}", fullPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading physical file at relative URL: {Url}", relativeUrl);
            throw;
        }
        #endregion
    }

    public bool DeletePhysicalFile(string relativeUrl)
    {
        #region hide [2026-08-17]
        try
        {
            var fullPath = GetFullPath(relativeUrl);
            if (string.IsNullOrEmpty(fullPath)) return false;

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Physical file deleted successfully: {Path}", fullPath);
                return true;
            }

            _logger.LogWarning("Physical file not found for deletion: {Path}", fullPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting physical file at relative URL: {Url}", relativeUrl);
            return false;
        }
        #endregion
    }

    private string GetFullPath(string relativeUrl)
    {
        #region hide [2026-08-17]
        if (string.IsNullOrWhiteSpace(relativeUrl)) return string.Empty;

        if (relativeUrl.StartsWith("Files/store", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(_env.ContentRootPath, relativeUrl);
        }
        return Path.Combine(_env.ContentRootPath, "Files", "store", relativeUrl);
        #endregion
    }
    #endregion
}
