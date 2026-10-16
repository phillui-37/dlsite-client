namespace DlsiteClient.Core.Services;

/// <summary>Stages an install beside the library; previous installs are retained for recovery.</summary>
public sealed class InstallStaging : IDisposable
{
    private readonly string _destination;
    public string StagePath { get; }
    public string BackupPath { get; }

    public InstallStaging(string destination, string recoveryRoot)
    {
        _destination = Path.GetFullPath(destination);
        var id = Guid.NewGuid().ToString("N");
        StagePath = Path.Combine(recoveryRoot, "staging", id);
        BackupPath = Path.Combine(recoveryRoot, "backups", id);
        Directory.CreateDirectory(StagePath);
    }

    public void Commit(CancellationToken ct = default)
    {
        if (Directory.Exists(_destination))
            PreserveLocalFiles(_destination, StagePath, ct);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(_destination)!);
        var hadInstall = Directory.Exists(_destination);
        if (hadInstall)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            // Save the original location beside the backup for manual recovery after power loss.
            File.WriteAllText(BackupPath + ".origin.txt", _destination);
            Directory.Move(_destination, BackupPath);
        }
        try
        {
            // No cancellation between the two renames. A failed move restores the old install.
            Directory.Move(StagePath, _destination);
        }
        catch
        {
            if (hadInstall && !Directory.Exists(_destination))
                Directory.Move(BackupPath, _destination);
            throw;
        }
    }

    private static void PreserveLocalFiles(string source, string target, CancellationToken ct,
        bool saveDirectory = false)
    {
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var dest = Path.Combine(target, entry.Name);
            if (entry is DirectoryInfo)
            {
                if (File.Exists(dest)) continue; // Original remains in the backup.
                Directory.CreateDirectory(dest);
                var isSave = saveDirectory || entry.Name.Equals("save", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("saves", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("savedata", StringComparison.OrdinalIgnoreCase);
                PreserveLocalFiles(entry.FullName, dest, ct, isSave);
            }
            else if (!Directory.Exists(dest))
            {
                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                var isSave = saveDirectory || extension is ".sav" or ".save" or ".rpgsave"
                    or ".rmmzsave" || (entry.Name.StartsWith("Save", StringComparison.OrdinalIgnoreCase)
                    && extension is ".rvdata" or ".rvdata2" or ".rxdata");
                if (isSave || !File.Exists(dest)) File.Copy(entry.FullName, dest, overwrite: isSave);
            }
        }
    }

    public void Dispose()
    {
        // Never delete retained backups. A locked staging folder can be removed later.
        try { if (Directory.Exists(StagePath)) Directory.Delete(StagePath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
