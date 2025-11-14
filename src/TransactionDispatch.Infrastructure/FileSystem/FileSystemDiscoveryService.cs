using TransactionDispatch.Domain;

namespace TransactionDispatch.Infrastructure.FileSystem;

public sealed class FileSystemDiscoveryService : IFileDiscoveryService
{
    public Task<IReadOnlyCollection<FileEntry>> FindFilesAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(job.FolderPath))
        {
            throw new DirectoryNotFoundException($"Folder '{job.FolderPath}' was not found.");
        }

        var extensions = new HashSet<string>(job.AllowedExtensions.Select(e => e.StartsWith('.') ? e : $".{e}"), StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(job.FolderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => extensions.Count == 0 || extensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileEntry(path, Path.GetFileName(path), ResolveContentType(path)))
            .ToArray();

        job.AddExpectedFiles(files.Length);
        return Task.FromResult<IReadOnlyCollection<FileEntry>>(files);
    }

    private static string ResolveContentType(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return "application/xml";
        }

        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return "application/json";
        }

        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return "text/csv";
        }

        return "application/octet-stream";
    }
}
