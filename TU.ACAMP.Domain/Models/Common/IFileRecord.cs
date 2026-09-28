namespace TU.ACAMP.Domain.Common;

public interface IFileRecord
{
    string FileName { get; set; }
    string FilePath { get; set; }
    string ContentType { get; set; }
    long FileSize { get; set; }
}
