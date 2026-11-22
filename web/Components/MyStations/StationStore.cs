using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace My
{
    /// <summary>
    /// File-backed storage for "My Stations" (stations.yml): tolerant loading for the AVR endpoints,
    /// and for the admin UI strict loading, validation, atomic saves, backups and optimistic concurrency (ETag).
    /// </summary>
    public class StationStore
    {
        public const int MAX_BACKUPS = 10;
        public const int MAX_NAME_LENGTH = 200;
        public const int MAX_URL_LENGTH = 2000;
        private const string FILE_HEADER =
            "# zCast \"My Stations\". Managed by the zCast admin page: comments are not preserved when saving there.\n";
        private static readonly Regex BackupIdPattern = new(@"^stations-\d{8}-\d{9}\.yml$", RegexOptions.Compiled);
        private static readonly object fileLock = new();

        private readonly ILogger<StationStore> logger;

        public string FilePath { get; }
        public string BackupDirectory => Path.Combine(Path.GetDirectoryName(FilePath)!, "backups");

        public StationStore(ILogger<StationStore> logger, IConfiguration configuration)
            : this(logger, configuration["Stations:File"])
        {
        }

        public StationStore(ILogger<StationStore> logger, string? filePath)
        {
            this.logger = logger;
            FilePath = string.IsNullOrWhiteSpace(filePath)
                ? BundledFilePath
                : Path.GetFullPath(filePath);
            SeedIfMissing();
        }

        /// <summary>stations.yml shipped next to the binaries (initial content, also used when no path is configured).</summary>
        public static string BundledFilePath => Path.Combine(AppContext.BaseDirectory, "stations.yml");

        /// <summary>On first start with a separate data file, copy the bundled stations.yml there.</summary>
        private void SeedIfMissing()
        {
            if (File.Exists(FilePath) || string.Equals(FilePath, BundledFilePath, StringComparison.Ordinal) || !File.Exists(BundledFilePath))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.Copy(BundledFilePath, FilePath);
                logger.LogInformation("Created station configuration '{File}' from the bundled stations.yml", FilePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not create station configuration '{File}': {Message}", FilePath, ex.Message);
            }
        }

        /// <summary>Tolerant load used by the AVR endpoints: returns an empty list on any error.</summary>
        public RadioCategories Load()
        {
            try
            {
                return Parse(File.ReadAllText(FilePath));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                logger.LogWarning("Station configuration '{File}' not found", FilePath);
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                logger.LogWarning("Station configuration format error: {Message}", ex.Message);
            }
            catch (IOException ex)
            {
                logger.LogWarning("Station configuration '{File}' could not be read: {Message}", FilePath, ex.Message);
            }
            return new RadioCategories();
        }

        /// <summary>Strict load for editing. Throws <see cref="YamlDotNet.Core.YamlException"/> on format errors.</summary>
        public (RadioCategories Categories, string ETag) LoadForEdit()
        {
            lock (fileLock)
            {
                var bytes = ReadBytesOrEmpty();
                return (Parse(Encoding.UTF8.GetString(bytes)), ComputeETag(bytes));
            }
        }

        public byte[] ReadRaw()
        {
            lock (fileLock)
            {
                return ReadBytesOrEmpty();
            }
        }

        public string CurrentETag()
        {
            lock (fileLock)
            {
                return ComputeETag(ReadBytesOrEmpty());
            }
        }

        /// <summary>
        /// Validates and saves atomically (temp file + rename), keeping a backup of the previous version.
        /// Throws <see cref="StationStoreConflictException"/> if the file changed since <paramref name="expectedETag"/>.
        /// </summary>
        public string Save(RadioCategories categories, string? expectedETag)
        {
            var errors = Validate(categories);
            if (errors.Count > 0)
            {
                throw new StationStoreValidationException(errors);
            }
            var bytes = new UTF8Encoding(false).GetBytes(FILE_HEADER + Serialize(categories));
            lock (fileLock)
            {
                var current = ComputeETag(ReadBytesOrEmpty());
                if (expectedETag != null && expectedETag != current)
                {
                    throw new StationStoreConflictException(current);
                }
                WriteWithBackup(bytes);
                logger.LogInformation("Station configuration saved ({Categories} categories)", categories.Categories.Count);
                return ComputeETag(bytes);
            }
        }

        public IReadOnlyList<BackupInfo> ListBackups()
        {
            if (!Directory.Exists(BackupDirectory))
            {
                return Array.Empty<BackupInfo>();
            }
            return new DirectoryInfo(BackupDirectory)
                .GetFiles("stations-*.yml")
                .Where(f => BackupIdPattern.IsMatch(f.Name))
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .Select(f => new BackupInfo(f.Name, BackupTimeFromId(f.Name), f.Length))
                .ToList();
        }

        /// <summary>Restores a backup (raw file, comments kept). The current version is backed up first.</summary>
        public string Restore(string backupId)
        {
            if (!BackupIdPattern.IsMatch(backupId))
            {
                throw new ArgumentException("Invalid backup id");
            }
            var path = Path.Combine(BackupDirectory, backupId);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Backup not found", backupId);
            }
            var bytes = File.ReadAllBytes(path);
            Parse(Encoding.UTF8.GetString(bytes)); // refuse to restore a broken file
            lock (fileLock)
            {
                WriteWithBackup(bytes);
                logger.LogInformation("Station configuration restored from backup '{Backup}'", backupId);
                return ComputeETag(bytes);
            }
        }

        public static RadioCategories Parse(string yaml)
        {
            var result = new DeserializerBuilder().Build().Deserialize<RadioCategories>(yaml) ?? new RadioCategories();
            result.Categories ??= new Dictionary<string, List<Station>>();
            foreach (var key in result.Categories.Keys.ToList())
            {
                result.Categories[key] = (result.Categories[key] ?? new List<Station>())
                    .Where(s => s != null)
                    .Select(s => new Station { Name = s.Name ?? string.Empty, Url = s.Url ?? string.Empty })
                    .ToList();
            }
            return result;
        }

        public static string Serialize(RadioCategories categories)
        {
            // Indented sequences match the documented format ("    - Name: ...").
            return new SerializerBuilder().WithIndentedSequences().Build().Serialize(categories);
        }

        /// <summary>Backup ids encode their UTC creation time: stations-yyyyMMdd-HHmmssfff.yml.</summary>
        private static DateTime BackupTimeFromId(string id)
        {
            return DateTime.ParseExact(id.Substring("stations-".Length, 18), "yyyyMMdd-HHmmssfff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        }

        public static List<string> Validate(RadioCategories categories)
        {
            var errors = new List<string>();
            var seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (category, stations) in categories.Categories)
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    errors.Add("A category has an empty name.");
                    continue;
                }
                if (category.Length > MAX_NAME_LENGTH)
                {
                    errors.Add($"Category '{category[..40]}…' is longer than {MAX_NAME_LENGTH} characters.");
                }
                // The category name becomes a URL path segment for the AVR.
                if (category.IndexOfAny(new[] { '/', '\\', '?', '#' }) >= 0)
                {
                    errors.Add($"Category '{category}' must not contain / \\ ? or #.");
                }
                if (!seenCategories.Add(category))
                {
                    errors.Add($"Category '{category}' exists more than once.");
                }
                var seenStations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var station in stations)
                {
                    var label = string.IsNullOrWhiteSpace(station.Name) ? "(no name)" : station.Name;
                    if (string.IsNullOrWhiteSpace(station.Name))
                    {
                        errors.Add($"A station in '{category}' has an empty name.");
                    }
                    else if (station.Name.Length > MAX_NAME_LENGTH)
                    {
                        errors.Add($"Station name in '{category}' is longer than {MAX_NAME_LENGTH} characters.");
                    }
                    else if (!seenStations.Add(station.Name))
                    {
                        errors.Add($"Station '{station.Name}' exists more than once in '{category}'.");
                    }
                    if (station.Url.Length > MAX_URL_LENGTH
                        || !Uri.TryCreate(station.Url, UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        errors.Add($"Station '{label}' in '{category}' needs a valid http:// or https:// URL.");
                    }
                }
            }
            return errors;
        }

        public static string ComputeETag(byte[] bytes)
        {
            return "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant() + "\"";
        }

        private byte[] ReadBytesOrEmpty()
        {
            try
            {
                return File.ReadAllBytes(FilePath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return Array.Empty<byte>();
            }
        }

        // Caller must hold fileLock.
        private void WriteWithBackup(byte[] bytes)
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(FilePath))
            {
                Directory.CreateDirectory(BackupDirectory);
                var backupName = $"stations-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.yml";
                File.Copy(FilePath, Path.Combine(BackupDirectory, backupName), overwrite: true);
                foreach (var old in ListBackups().Skip(MAX_BACKUPS))
                {
                    File.Delete(Path.Combine(BackupDirectory, old.Id));
                }
            }
            // Atomic replace: readers (AVR requests) never see a half-written file.
            var temp = FilePath + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, FilePath, overwrite: true);
        }
    }

    public record BackupInfo(string Id, DateTime CreatedUtc, long Size);

    public class StationStoreConflictException : Exception
    {
        public string CurrentETag { get; }

        public StationStoreConflictException(string currentETag)
            : base("The station list was changed in the meantime.")
        {
            CurrentETag = currentETag;
        }
    }

    public class StationStoreValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public StationStoreValidationException(IReadOnlyList<string> errors)
            : base(string.Join(" ", errors))
        {
            Errors = errors;
        }
    }
}
