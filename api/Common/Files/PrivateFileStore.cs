using Microsoft.Extensions.Options;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Common.Files;

public sealed record StoredFile(string StorageKey, string OriginalFileName, string ContentType, long SizeBytes);

public sealed record StoredFileStream(Stream Content, string ContentType, string OriginalFileName, long SizeBytes);

/// <summary>
/// Private file storage for payslips, identity documents, receipts and attachments
/// (BRD section 4: SQL stores metadata, files live in private storage; never expose public URLs).
/// </summary>
public interface IPrivateFileStore : IAppService
{
    Task<StoredFile> SaveAsync(Stream content, string originalFileName, long maxBytes,
        IReadOnlyCollection<string> allowedExtensions, CancellationToken cancellationToken = default);

    Task<StoredFileStream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed class PrivateFileStore : IPrivateFileStore
{
    private readonly string _root;
    private readonly string _absoluteRoot;
    private readonly ILogger<PrivateFileStore> _logger;

    public PrivateFileStore(IOptions<FileStorageOptions> options, IHostEnvironment environment, ILogger<PrivateFileStore> logger)
    {
        _root = options.Value.Root;
        _absoluteRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, _root));
        Directory.CreateDirectory(_absoluteRoot);
        _logger = logger;
    }

    public Task<StoredFile> SaveAsync(Stream content, string originalFileName, long maxBytes,
        IReadOnlyCollection<string> allowedExtensions, CancellationToken cancellationToken = default)
        => SaveAsync(content, originalFileName, maxBytes, allowedExtensions, quarantine: false, cancellationToken);

    /// <summary>Variant used by document uploads that must be scanned before download (BR-010).</summary>
    public async Task<StoredFile> SaveAsync(Stream content, string originalFileName, long maxBytes,
        IReadOnlyCollection<string> allowedExtensions, bool quarantine, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new RequestValidationException(errors: new() { ["file"] = ["A file is required."] });

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) ||
            !allowedExtensions.Any(a => string.Equals(a, extension, StringComparison.OrdinalIgnoreCase)))
            throw new RequestValidationException("File type is not allowed.",
                new() { ["file"] = [$"Allowed types: {string.Join(", ", allowedExtensions)}."] });

        if (content.CanSeek && content.Length > maxBytes)
            throw new RequestValidationException("File is too large.",
                new() { ["file"] = [$"Maximum size is {maxBytes / 1024 / 1024} MB."] });

        var storageKey = $"{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension}";
        var fullPath = ResolvePath(storageKey);

        long total = 0;
        await using (var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    target.Close();
                    File.Delete(fullPath);
                    throw new RequestValidationException("File is too large.",
                        new() { ["file"] = [$"Maximum size is {maxBytes / 1024 / 1024} MB."] });
                }
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        _logger.LogInformation("Stored private file {StorageKey} ({Bytes} bytes, quarantine: {Quarantine})",
            storageKey, new FileInfo(fullPath).Length, quarantine);

        return new StoredFile(storageKey, Path.GetFileName(originalFileName), GuessContentType(extension), total);
    }

    public async Task<StoredFileStream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (!File.Exists(fullPath))
            throw new NotFoundException("The requested file is no longer available.");

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var info = new FileInfo(fullPath);
        return new StoredFileStream(stream, GuessContentType(Path.GetExtension(fullPath)),
            Path.GetFileName(fullPath), info.Length);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new NotFoundException("The requested file is no longer available.");

        var relative = storageKey.Replace('\\', '/').TrimStart('/');
        if (relative.Contains("..", StringComparison.Ordinal) || relative.Contains(':'))
            throw new ForbiddenException("Invalid file reference.");

        var fullPath = Path.GetFullPath(Path.Combine(_absoluteRoot, relative));
        if (!fullPath.StartsWith(_absoluteRoot, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Invalid file reference.");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return fullPath;
    }

    private static string GuessContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        ".heic" => "image/heic",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".log" => "text/plain",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".odt" => "application/vnd.oasis.opendocument.text",
        ".rtf" => "application/rtf",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
        ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".odp" => "application/vnd.oasis.opendocument.presentation",
        ".eml" => "message/rfc822",
        ".msg" => "application/vnd.ms-outlook",
        _ => "application/octet-stream"
    };
}
