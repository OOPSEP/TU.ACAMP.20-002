using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using TU.ACAMP.Domain.Enums;

namespace TU.ACAMP.Domain.Common;

public class TbFileEntity
{
    public string Id => !string.IsNullOrEmpty(FileHash) ? FileHash : FileName;

    #region class field
    public const string FileRefAccept = ".jpg,.jpeg,.png,.bmp,.pdf,.doc,.docx,.xls,.xlsx,.csv,.txt";
    public const string FileImgAccept = ".jpg,.jpeg,.png,.bmp";
    private string _fileName = string.Empty;
    private string _extension = string.Empty;
    private string _contentType = string.Empty;
    private string _url = string.Empty;
    private string _createdBy = string.Empty;
    private string _tag = string.Empty;
    private string _fileHash = string.Empty;
    private eStorageProvider _storageProvider = eStorageProvider.Local;
    #endregion

    #region class properties
    [JsonPropertyName("FileName")]
    public string FileName
    {
        get => _fileName;
        set => _fileName = value ?? string.Empty;
    }

    [JsonPropertyName("Extension")]
    public string Extension
    {
        get => _extension;
        set => _extension = value ?? string.Empty;
    }

    [JsonPropertyName("ContentType")]
    public string ContentType
    {
        get => _contentType;
        set => _contentType = value ?? string.Empty;
    }

    [JsonPropertyName("Url")]
    public string Url
    {
        get => _url;
        set => _url = value ?? string.Empty;
    }

    [JsonPropertyName("Size")]
    public long Size { get; set; }

    [JsonPropertyName("UploadDate")]
    public DateTime UploadDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("CreatedBy")]
    public string CreatedBy
    {
        get => _createdBy;
        set => _createdBy = value ?? string.Empty;
    }

    [JsonPropertyName("Tag")]
    public string Tag
    {
        get => _tag;
        set => _tag = value ?? string.Empty;
    }

    [JsonPropertyName("FileHash")]
    public string FileHash
    {
        get => _fileHash;
        set => _fileHash = value ?? string.Empty;
    }

    [JsonPropertyName("StorageProvider")]
    public eStorageProvider StorageProvider
    {
        get => _storageProvider;
        set => _storageProvider = value;
    }
    #endregion

    #region class constructor
    public TbFileEntity()
    {
    }
    #endregion

    #region class methods
    public static List<TbFileEntity> DeserializeListFromJson(string json)
    {
        #region hide [2026-08-17]
        if (string.IsNullOrWhiteSpace(json)) return new List<TbFileEntity>();
        var trimmed = json.Trim();
        if (trimmed == "[{}]" || trimmed == "[]" || trimmed == "{}") return new List<TbFileEntity>();
        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };
            var items = JsonSerializer.Deserialize<List<TbFileEntity>>(json, jsonOptions) ?? new List<TbFileEntity>();
            return items.Where(x => !string.IsNullOrWhiteSpace(x.FileName) || !string.IsNullOrWhiteSpace(x.Url)).ToList();
        }
        catch
        {
            return new List<TbFileEntity>();
        }
        #endregion
    }

    public static string SerializeListToJson(List<TbFileEntity> list)
    {
        #region hide [2026-08-17]
        if (list == null) return "[]";
        return JsonSerializer.Serialize(list);
        #endregion
    }
    #endregion
}
