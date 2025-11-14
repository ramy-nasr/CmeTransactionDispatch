using TransactionDispatch.Domain;

namespace TransactionDispatch.Infrastructure.FileSystem;

public interface IFileDiscoveryService
{
    Task<IReadOnlyCollection<FileEntry>> FindFilesAsync(DispatchJob job, CancellationToken cancellationToken);
}

public sealed record FileEntry(string FullPath, string Name, string ContentType);
