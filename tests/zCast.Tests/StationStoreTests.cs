using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using My;
using YamlDotNet.Core;

namespace zCast.Tests;

public class StationStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "zcast-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(dir, "stations.yml");
    private string BackupDir => Path.Combine(dir, "backups");

    public StationStoreTests()
    {
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private StationStore NewStore() => new(NullLogger<StationStore>.Instance, FilePath);

    /// <summary>Store on a pre-created file so the bundled stations.yml is never seeded.</summary>
    private StationStore NewStoreWithFile(string content = "Categories:\n")
    {
        File.WriteAllText(FilePath, content);
        return NewStore();
    }

    /// <summary>Store whose file is missing (any bundled seed is removed again).</summary>
    private StationStore NewStoreMissingFile()
    {
        var store = NewStore();
        if (File.Exists(FilePath)) File.Delete(FilePath);
        return store;
    }

    private static RadioCategories Cats(params (string Category, (string Name, string Url)[] Stations)[] items)
    {
        var r = new RadioCategories();
        foreach (var (c, stations) in items)
        {
            r.Categories[c] = stations.Select(s => new Station { Name = s.Name, Url = s.Url }).ToList();
        }
        return r;
    }

    private static RadioCategories Valid(string name = "A") =>
        Cats(("Cat", new[] { (name, "http://example.com/stream") }));

    private static List<string> Flatten(RadioCategories r) =>
        r.Categories.Select(kv => kv.Key + " => " + string.Join(" | ", kv.Value.Select(s => s.Name + " @ " + s.Url))).ToList();

    private static void SaveDistinct(StationStore store, int count)
    {
        for (var i = 0; i < count; i++)
        {
            store.Save(Valid("S" + i), null);
            Thread.Sleep(5); // backup ids have millisecond resolution
        }
    }

    // 1. Round trip
    [Fact]
    public void RoundTrip_PreservesContentAndOrder()
    {
        var store = NewStoreWithFile();
        var input = Cats(
            ("Zeta", new[] { ("Petőfi Rádió", "http://a.hu/1"), ("Ö1", "https://b.at/2"), ("Alpha", "http://c/3") }),
            ("Alpha: 1", new[] { ("Name: with colon # and hash", "http://d/4") }),
            ("Empty", Array.Empty<(string, string)>()),
            ("Mid", new[] { ("B", "http://e/5"), ("A", "http://f/6") }));

        store.Save(input, null);
        var (loaded, _) = store.LoadForEdit();

        Assert.Equal(Flatten(input), Flatten(loaded));
    }

    [Fact]
    public void Save_WritesUtf8WithoutBom_AndParseable()
    {
        var store = NewStoreWithFile();
        store.Save(Valid("Petőfi Rádió"), null);
        var bytes = File.ReadAllBytes(FilePath);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.Contains("Petőfi Rádió", text);
        Assert.Equal("Petőfi Rádió", StationStore.Parse(text).Categories["Cat"][0].Name);
    }

    // 2. ETag
    [Fact]
    public void ETag_EqualForIdenticalBytes_DifferentOtherwise()
    {
        Assert.Equal(StationStore.ComputeETag(new byte[] { 1, 2, 3 }), StationStore.ComputeETag(new byte[] { 1, 2, 3 }));
        Assert.NotEqual(StationStore.ComputeETag(new byte[] { 1, 2, 3 }), StationStore.ComputeETag(new byte[] { 1, 2, 4 }));
    }

    [Fact]
    public void ETag_ChangesAfterSave_AndMatchesReturnedValue()
    {
        var store = NewStoreWithFile();
        var before = store.CurrentETag();
        var returned = store.Save(Valid(), before);
        Assert.NotEqual(before, returned);
        Assert.Equal(returned, store.CurrentETag());
        Assert.Equal(returned, store.LoadForEdit().ETag);
    }

    [Fact]
    public void Save_StaleETag_ThrowsConflict_FileUnchanged()
    {
        var store = NewStoreWithFile();
        var stale = store.CurrentETag();
        store.Save(Valid("First"), stale);
        var bytes = File.ReadAllBytes(FilePath);

        var ex = Assert.Throws<StationStoreConflictException>(() => store.Save(Valid("Second"), stale));

        Assert.Equal(store.CurrentETag(), ex.CurrentETag);
        Assert.Equal(bytes, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void Save_NullETag_Succeeds()
    {
        var store = NewStoreWithFile();
        store.Save(Valid("One"), null);
        Thread.Sleep(5);
        store.Save(Valid("Two"), null);
        Assert.Equal("Two", store.LoadForEdit().Categories.Categories["Cat"][0].Name);
    }

    [Fact]
    public void ETag_OfMissingFile_IsETagOfEmptyBytes()
    {
        var store = NewStoreMissingFile();
        Assert.Equal(StationStore.ComputeETag(Array.Empty<byte>()), store.CurrentETag());
    }

    [Fact]
    public void Save_ToMissingFile_CreatesFileAndNoBackup()
    {
        var store = NewStoreMissingFile();
        store.Save(Valid(), StationStore.ComputeETag(Array.Empty<byte>()));
        Assert.True(File.Exists(FilePath));
        Assert.Empty(store.ListBackups());
    }

    [Fact]
    public void Constructor_SeedsFromBundledFile_WhenAvailable()
    {
        var store = NewStore();
        if (File.Exists(StationStore.BundledFilePath))
        {
            Assert.True(File.Exists(FilePath));
            Assert.Equal(File.ReadAllBytes(StationStore.BundledFilePath), File.ReadAllBytes(FilePath));
        }
        else
        {
            Assert.False(File.Exists(FilePath));
        }
        Assert.Equal(Path.GetFullPath(FilePath), store.FilePath);
    }

    // 3. Validation
    [Fact]
    public void Validate_ValidHttpAndHttps_Pass()
    {
        var r = Cats(("C", new[] { ("a", "http://x.com"), ("b", "https://x.com:8080/s?x=1") }));
        Assert.Empty(StationStore.Validate(r));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_EmptyCategoryName_Fails(string name)
    {
        Assert.NotEmpty(StationStore.Validate(Cats((name, new[] { ("a", "http://x.com") }))));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a?b")]
    [InlineData("a#b")]
    public void Validate_CategoryWithForbiddenChar_Fails(string name)
    {
        Assert.NotEmpty(StationStore.Validate(Cats((name, new[] { ("a", "http://x.com") }))));
    }

    [Fact]
    public void Validate_DuplicateCategoryNames_CaseInsensitive()
    {
        var r = Cats(("Rock", new[] { ("a", "http://x.com") }), ("rock", new[] { ("a", "http://x.com") }));
        Assert.NotEmpty(StationStore.Validate(r));
    }

    [Fact]
    public void Validate_TooLongNames_Fail()
    {
        Assert.NotEmpty(StationStore.Validate(Cats((new string('c', 201), new[] { ("a", "http://x.com") }))));
        Assert.NotEmpty(StationStore.Validate(Cats(("C", new[] { (new string('n', 201), "http://x.com") }))));
    }

    [Fact]
    public void Validate_EmptyStationName_Fails()
    {
        Assert.NotEmpty(StationStore.Validate(Cats(("C", new[] { ("", "http://x.com") }))));
    }

    [Fact]
    public void Validate_DuplicateStationInCategory_CaseInsensitive_Fails()
    {
        var r = Cats(("C", new[] { ("Jazz", "http://x.com"), ("jazz", "http://y.com") }));
        Assert.NotEmpty(StationStore.Validate(r));
    }

    [Fact]
    public void Validate_SameStationNameInDifferentCategories_Ok()
    {
        var r = Cats(("C1", new[] { ("Jazz", "http://x.com") }), ("C2", new[] { ("Jazz", "http://x.com") }));
        Assert.Empty(StationStore.Validate(r));
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com/stream")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/s")]
    public void Validate_InvalidUrl_Fails(string url)
    {
        Assert.NotEmpty(StationStore.Validate(Cats(("C", new[] { ("a", url) }))));
    }

    [Fact]
    public void Validate_UrlLongerThan2000_Fails()
    {
        var url = "http://x.com/" + new string('a', 2000);
        Assert.NotEmpty(StationStore.Validate(Cats(("C", new[] { ("a", url) }))));
    }

    [Fact]
    public void Save_InvalidData_ThrowsValidation_FileUntouched_NoBackup()
    {
        var store = NewStoreWithFile("Categories:\n# original\n");
        var bytes = File.ReadAllBytes(FilePath);

        var ex = Assert.Throws<StationStoreValidationException>(() =>
            store.Save(Cats(("C", new[] { ("a", "ftp://nope") })), null));

        Assert.NotEmpty(ex.Errors);
        Assert.Equal(bytes, File.ReadAllBytes(FilePath));
        Assert.Empty(store.ListBackups());
    }

    // 4. Backups
    [Fact]
    public void Backups_FirstSaveOnNewFileNone_LaterSavesOneEach()
    {
        var store = NewStoreMissingFile();
        store.Save(Valid("1"), null);
        Assert.Empty(store.ListBackups());
        Thread.Sleep(5);
        store.Save(Valid("2"), null);
        Assert.Single(store.ListBackups());
        Thread.Sleep(5);
        store.Save(Valid("3"), null);
        Assert.Equal(2, store.ListBackups().Count);
    }

    [Fact]
    public void Backups_NeverMoreThanMax_OldestRemoved()
    {
        var store = NewStoreMissingFile();
        SaveDistinct(store, 15);

        var backups = store.ListBackups();
        Assert.Equal(StationStore.MAX_BACKUPS, backups.Count);
        var contents = backups
            .Select(b => StationStore.Parse(File.ReadAllText(Path.Combine(BackupDir, b.Id))).Categories["Cat"][0].Name)
            .ToList();
        // Newest backup holds the second to last version (S13); the oldest (S0..S3) are gone.
        Assert.Equal("S13", contents[0]);
        Assert.DoesNotContain("S0", contents);
        Assert.Contains("S4", contents);
    }

    [Fact]
    public void ListBackups_NewestFirst_WithCreatedUtcFromId()
    {
        var store = NewStoreMissingFile();
        var before = DateTime.UtcNow.AddSeconds(-1);
        SaveDistinct(store, 4);
        var after = DateTime.UtcNow.AddSeconds(1);

        var backups = store.ListBackups();
        Assert.Equal(3, backups.Count);
        Assert.Equal(backups.OrderByDescending(b => b.CreatedUtc).Select(b => b.Id), backups.Select(b => b.Id));
        foreach (var b in backups)
        {
            Assert.Equal(DateTimeKind.Utc, b.CreatedUtc.Kind);
            Assert.InRange(b.CreatedUtc, before, after);
            Assert.Equal(b.CreatedUtc.ToString("yyyyMMdd-HHmmssfff"), b.Id.Substring(9, 18));
            Assert.True(b.Size > 0);
        }
    }

    [Fact]
    public void ListBackups_NoDirectory_Empty_AndIgnoresForeignFiles()
    {
        var store = NewStoreWithFile();
        Assert.Empty(store.ListBackups());
        Directory.CreateDirectory(BackupDir);
        File.WriteAllText(Path.Combine(BackupDir, "stations-1.yml"), "x");
        File.WriteAllText(Path.Combine(BackupDir, "notes.txt"), "x");
        Assert.Empty(store.ListBackups());
    }

    // 5. Restore
    [Fact]
    public void Restore_RestoresExactBytes_BacksUpCurrent_ReturnsETag()
    {
        const string original = "# my comment\nCategories:\n  Jazz:\n    - Name: Ö1 # inline\n      Url: http://a.at\n";
        var store = NewStoreWithFile(original);
        var originalBytes = File.ReadAllBytes(FilePath);
        Thread.Sleep(5);
        store.Save(Valid("New"), null);
        var savedBytes = File.ReadAllBytes(FilePath);
        var backupId = Assert.Single(store.ListBackups()).Id;
        Thread.Sleep(5);

        var etag = store.Restore(backupId);

        Assert.Equal(originalBytes, File.ReadAllBytes(FilePath));
        Assert.Equal(StationStore.ComputeETag(originalBytes), etag);
        Assert.Equal(etag, store.CurrentETag());
        var backups = store.ListBackups();
        Assert.Equal(2, backups.Count);
        Assert.Equal(savedBytes, File.ReadAllBytes(Path.Combine(BackupDir, backups[0].Id)));
    }

    [Theory]
    [InlineData("../x.yml")]
    [InlineData("stations-1.yml")]
    [InlineData("stations-20240101-123456789.yml.bak")]
    [InlineData("..\\stations-20240101-123456789.yml")]
    [InlineData("")]
    public void Restore_InvalidId_Throws(string id)
    {
        var store = NewStoreWithFile();
        Assert.Throws<ArgumentException>(() => store.Restore(id));
    }

    [Fact]
    public void Restore_MissingBackup_Throws()
    {
        var store = NewStoreWithFile();
        Assert.Throws<FileNotFoundException>(() => store.Restore("stations-20240101-123456789.yml"));
    }

    [Fact]
    public void Restore_InvalidYaml_Refused_FileUnchanged()
    {
        var store = NewStoreWithFile("Categories:\n");
        var bytes = File.ReadAllBytes(FilePath);
        Directory.CreateDirectory(BackupDir);
        const string id = "stations-20240101-123456789.yml";
        File.WriteAllText(Path.Combine(BackupDir, id), "Categories: [unclosed\n  - : {");

        Assert.ThrowsAny<YamlException>(() => store.Restore(id));
        Assert.Equal(bytes, File.ReadAllBytes(FilePath));
        Assert.Single(store.ListBackups());
    }

    // 6. Load tolerance
    [Fact]
    public void Load_MissingFile_Empty()
    {
        var store = NewStoreMissingFile();
        Assert.Empty(store.Load().Categories);
    }

    [Fact]
    public void Load_BrokenYaml_Empty_NoThrow()
    {
        var store = NewStoreWithFile("Categories: [unclosed\n  - : {");
        Assert.Empty(store.Load().Categories);
    }

    [Fact]
    public void Load_ValidFile_ReturnsContent()
    {
        var store = NewStoreWithFile();
        store.Save(Valid("X"), null);
        Assert.Equal("X", store.Load().Categories["Cat"][0].Name);
    }

    [Fact]
    public void LoadForEdit_BrokenYaml_ThrowsYamlException()
    {
        var store = NewStoreWithFile("Categories: [unclosed\n  - : {");
        Assert.ThrowsAny<YamlException>(() => store.LoadForEdit());
    }

    [Fact]
    public void LoadForEdit_MissingFile_EmptyWithEmptyETag()
    {
        var store = NewStoreMissingFile();
        var (cats, etag) = store.LoadForEdit();
        Assert.Empty(cats.Categories);
        Assert.Equal(StationStore.ComputeETag(Array.Empty<byte>()), etag);
    }

    // 7. Parse edge cases
    [Fact]
    public void Parse_EmptyString_Empty() => Assert.Empty(StationStore.Parse("").Categories);

    [Fact]
    public void Parse_CategoriesWithoutEntries_Empty() => Assert.Empty(StationStore.Parse("Categories:").Categories);

    [Fact]
    public void Parse_CategoryWithNullList_EmptyList()
    {
        var r = StationStore.Parse("Categories:\n  Jazz:\n");
        Assert.Empty(r.Categories["Jazz"]);
    }

    [Fact]
    public void Parse_StationWithoutUrl_UrlEmptyString()
    {
        var r = StationStore.Parse("Categories:\n  Jazz:\n    - Name: Only name\n");
        var s = Assert.Single(r.Categories["Jazz"]);
        Assert.Equal("Only name", s.Name);
        Assert.Equal(string.Empty, s.Url);
    }

    // 8. Real stations.yml
    [Fact]
    public void RepoStationsYml_ParsesAndValidates()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "zCast.sln"))) d = d.Parent;
        Assert.NotNull(d);
        var path = Path.Combine(d!.FullName, "web", "stations.yml");
        Assert.True(File.Exists(path));

        var parsed = StationStore.Parse(File.ReadAllText(path));
        Assert.NotEmpty(parsed.Categories);
        Assert.Empty(StationStore.Validate(parsed));
    }
}
