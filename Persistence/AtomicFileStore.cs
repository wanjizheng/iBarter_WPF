using System.Text;
using System.IO;

namespace iBarter.Persistence;

/// <summary>
/// Writes a text file without ever truncating the currently valid copy.
/// The sibling .bak file is the immediately previous version; a timestamped
/// recovery copy is also kept when a write would remove all meaningful data.
/// </summary>
public static class AtomicFileStore {
    public static void WriteValidated(
        string path,
        string content,
        Func<string, bool> validator,
        Func<string, bool>? containsMeaningfulData = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(validator);

        if (!validator(content)) {
            throw new InvalidDataException("Refusing to save invalid data.");
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        string tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            string persistedTemp = File.ReadAllText(tempPath, Encoding.UTF8);
            if (!validator(persistedTemp)) {
                throw new InvalidDataException("Temporary save validation failed.");
            }

            if (!File.Exists(fullPath)) {
                File.Move(tempPath, fullPath);
                return;
            }

            string previous = File.ReadAllText(fullPath, Encoding.UTF8);
            if (containsMeaningfulData is not null
                && containsMeaningfulData(previous)
                && !containsMeaningfulData(persistedTemp)) {
                string recoveryPath = fullPath + ".recovery-"
                    + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".json";
                File.Copy(fullPath, recoveryPath, overwrite: false);
            }

            string backupPath = fullPath + ".bak";
            try {
                File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException) {
                File.Copy(fullPath, backupPath, overwrite: true);
                File.Move(tempPath, fullPath, overwrite: true);
            }
            catch (IOException) {
                // Some file systems do not implement Replace even on Windows.
                File.Copy(fullPath, backupPath, overwrite: true);
                File.Move(tempPath, fullPath, overwrite: true);
            }
        }
        finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }

    public static bool TryReadValidated(
        string path,
        Func<string, bool> validator,
        out string content,
        out bool recoveredFromBackup) {
        ArgumentNullException.ThrowIfNull(validator);

        if (TryReadOne(path, validator, out content)) {
            recoveredFromBackup = false;
            return true;
        }

        if (TryReadOne(path + ".bak", validator, out content)) {
            recoveredFromBackup = true;
            return true;
        }

        content = string.Empty;
        recoveredFromBackup = false;
        return false;
    }

    private static bool TryReadOne(string path, Func<string, bool> validator, out string content) {
        content = string.Empty;
        try {
            if (!File.Exists(path)) {
                return false;
            }

            content = File.ReadAllText(path, Encoding.UTF8);
            return validator(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            content = string.Empty;
            return false;
        }
    }
}
