using Browser;
using Microsoft.Extensions.Caching.Memory;
using My;

namespace Admin
{
    /// <summary>
    /// Admin UI and JSON API for editing "My Stations".
    /// Served only on the dedicated admin port (Admin:Port, bound to 127.0.0.1). In production nginx in front
    /// of it restricts access to the home network and asks for a password (basic auth); see deploy/.
    /// </summary>
    public static class AdminEndpoints
    {
        public const string CSRF_HEADER = "X-Zcast-Admin";
        public const string STREAM_TEST_CLIENT = "StreamTestClient";
        private const int MAX_IMPORT_BYTES = 1024 * 1024;
        private const int SEARCH_PAGE_SIZE = 50;
        private const string FILTERS_CACHE_KEY = "admin_search_filters";

        private static readonly Dictionary<string, string> StaticFiles = new(StringComparer.Ordinal)
        {
            ["index.html"] = "text/html; charset=utf-8",
            ["app.js"] = "text/javascript; charset=utf-8",
            ["app.css"] = "text/css; charset=utf-8",
        };

        public static void MapAdmin(this WebApplication app)
        {
            var port = app.Configuration.GetValue<int?>("Admin:Port");
            if (port is null or <= 0)
            {
                app.Logger.LogInformation("Admin UI disabled (Admin:Port is not set)");
                return;
            }
            app.Logger.LogInformation("Admin UI enabled on port {Port}", port);

            var admin = app.MapGroup("/admin")
                .AddEndpointFilter(new AdminGuardFilter(port.Value))
                .ExcludeFromDescription();

            // "/admin" → "/admin/" so the page's relative asset and API paths resolve correctly.
            admin.MapGet("/", (HttpContext context) => context.Request.Path.Value?.EndsWith('/') == true
                ? StaticFile("index.html")
                : Results.Redirect("/admin/"));
            admin.MapGet("/{file}", (string file) => StaticFile(file));

            var api = admin.MapGroup("/api");
            api.MapGet("/stations", GetStations);
            api.MapPut("/stations", PutStations);
            api.MapPost("/parse", ParseYaml);
            api.MapGet("/export", (StationStore store) =>
                Results.File(store.ReadRaw(), "application/x-yaml", "stations.yml"));
            api.MapGet("/search", Search);
            api.MapGet("/search/filters", SearchFilters);
            api.MapPost("/test-stream", TestStream);
            api.MapGet("/backups", (StationStore store) => Results.Ok(store.ListBackups()));
            api.MapPost("/backups/{id}/restore", RestoreBackup);
        }

        private static IResult StaticFile(string file)
        {
            if (!StaticFiles.TryGetValue(file, out var contentType))
            {
                return Results.NotFound();
            }
            return Results.File(Path.Combine(AppContext.BaseDirectory, "Admin", file), contentType);
        }

        private static IResult GetStations(StationStore store, HttpContext context)
        {
            try
            {
                var (categories, etag) = store.LoadForEdit();
                context.Response.Headers.ETag = etag;
                return Results.Ok(new StationsDocument(ToDto(categories), etag, store.FilePath));
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                // Send the ETag of the broken file too, so a corrected import can replace it directly.
                var etag = store.CurrentETag();
                context.Response.Headers.ETag = etag;
                return Results.Problem(
                    title: "stations.yml has a format error",
                    detail: $"{ex.Message} Fix the file on the server, import a corrected file or restore a backup.",
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    extensions: new Dictionary<string, object?> { ["etag"] = etag });
            }
        }

        private static IResult PutStations(StationsDocument document, StationStore store, HttpContext context)
        {
            string? expected = context.Request.Headers.IfMatch.FirstOrDefault();
            if (string.IsNullOrEmpty(expected))
            {
                return Results.Problem(title: "If-Match header is required", statusCode: StatusCodes.Status428PreconditionRequired);
            }
            try
            {
                var etag = store.Save(FromDto(document.Categories), expected);
                context.Response.Headers.ETag = etag;
                return Results.Ok(new { etag });
            }
            catch (StationStoreValidationException ex)
            {
                return Results.BadRequest(new { title = "Please fix these problems", errors = ex.Errors });
            }
            catch (StationStoreConflictException ex)
            {
                return Results.Json(new { title = ex.Message, etag = ex.CurrentETag }, statusCode: StatusCodes.Status412PreconditionFailed);
            }
        }

        /// <summary>Parses an uploaded stations.yml for the editor (nothing is saved until "Save").</summary>
        private static async Task<IResult> ParseYaml(HttpRequest request)
        {
            if (request.ContentLength > MAX_IMPORT_BYTES)
            {
                return Results.BadRequest(new { title = "File is too large" });
            }
            using var reader = new StreamReader(request.Body);
            var text = await reader.ReadToEndAsync();
            if (text.Length > MAX_IMPORT_BYTES)
            {
                return Results.BadRequest(new { title = "File is too large" });
            }
            try
            {
                var categories = StationStore.Parse(text);
                return Results.Ok(new { categories = ToDto(categories), errors = StationStore.Validate(categories) });
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                return Results.BadRequest(new { title = "Not a valid stations.yml", errors = new[] { ex.Message } });
            }
        }

        /// <summary>
        /// Filtered Radio Browser search. At least a name (2+ characters), country, language or genre is required,
        /// because the index has ~50,000 stations. Results are paged (offset, 50 per page).
        /// </summary>
        private static IResult Search(HttpRequest request, RadioBrowser radioBrowser)
        {
            var query = request.Query;
            string? Text(string key) => string.IsNullOrWhiteSpace(query[key]) ? null : query[key].ToString().Trim();
            var name = Text("name") ?? Text("q");
            var country = Text("country");
            var language = Text("language");
            var tag = Text("tag");
            if (name is { Length: < 2 })
            {
                return Results.BadRequest(new { title = "Enter at least 2 characters of the name" });
            }
            if (name == null && country == null && language == null && tag == null)
            {
                return Results.BadRequest(new { title = "Enter a name or choose a country, language or genre" });
            }
            var filter = new StationSearchFilter(
                name, country, language, tag, Text("codec"),
                BitrateMin: int.TryParse(query["bitrateMin"], out var bitrate) ? Math.Max(0, bitrate) : 0,
                HttpOnly: string.Equals(query["httpOnly"], "true", StringComparison.OrdinalIgnoreCase),
                Order: Text("order"),
                Offset: int.TryParse(query["offset"], out var offset) ? Math.Max(0, offset) : 0,
                Limit: SEARCH_PAGE_SIZE);

            var (stations, hasMore) = radioBrowser.AdvancedSearch(filter);
            var results = stations.Select(s => new
            {
                name = s.Name.Trim(),
                url = string.IsNullOrEmpty(s.UrlResolved) ? s.Url : s.UrlResolved,
                codec = s.Codec,
                bitrate = s.Bitrate,
                country = s.CountryCode,
                language = s.Language,
                votes = s.Votes,
                tags = string.Join(", ", s.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Take(4)),
            });
            return Results.Ok(new { results, offset = filter.Offset, hasMore });
        }

        /// <summary>Countries, languages, genres and codecs for the search filters (cached for 6 hours).</summary>
        private static IResult SearchFilters(RadioBrowser radioBrowser, IMemoryCache cache)
        {
            var filters = cache.GetOrCreate(FILTERS_CACHE_KEY, entry =>
            {
                var result = new SearchFilterOptions(
                    radioBrowser.GetFilterOptions("countries"),
                    radioBrowser.GetFilterOptions("languages"),
                    radioBrowser.GetFilterOptions("tags"),
                    radioBrowser.GetFilterOptions("codecs"));
                // If Radio Browser was unreachable, retry soon instead of caching empty lists for hours.
                var complete = result.Countries.Count > 0 && result.Languages.Count > 0 && result.Tags.Count > 0;
                entry.AbsoluteExpirationRelativeToNow = complete ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(1);
                return result;
            });
            return Results.Ok(filters);
        }

        private static IResult RestoreBackup(string id, StationStore store)
        {
            try
            {
                return Results.Ok(new { etag = store.Restore(id) });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { title = "Invalid backup id" });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                return Results.BadRequest(new { title = "Backup is not a valid stations.yml", errors = new[] { ex.Message } });
            }
        }

        /// <summary>Checks that a URL answers like an audio stream an AVR can play.</summary>
        private static async Task<IResult> TestStream(StreamTestRequest request, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Results.Ok(new StreamTestResult(false, null, null, null, null, new() { "Not a valid http:// or https:// URL." }));
            }

            var client = httpClientFactory.CreateClient(STREAM_TEST_CLIENT);
            var (result, connectionFailed) = await ProbeStreamAsync(client, uri, cancellationToken);

            // Older AVRs can't play HTTPS (and can't fix certificate problems either): look for a working http:// version.
            if (uri.Scheme == Uri.UriSchemeHttps && (connectionFailed || result.Ok))
            {
                var httpUri = ToHttp(uri);
                var (alternative, _) = await ProbeStreamAsync(client, httpUri, cancellationToken);
                if (alternative.Ok)
                {
                    var warnings = new List<string>(result.Warnings) { $"The http:// version works and is better for the receiver: {httpUri}" };
                    result = result with { Warnings = warnings, SuggestedUrl = httpUri.ToString() };
                }
            }
            return Results.Ok(result);
        }

        private static Uri ToHttp(Uri uri)
        {
            var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttp };
            if (uri.IsDefaultPort || uri.Port == 443)
            {
                builder.Port = -1;
            }
            return builder.Uri;
        }

        /// <summary>Requests the URL and classifies the answer. connectionFailed = no HTTP answer at all (DNS, TLS, timeout…).</summary>
        private static async Task<(StreamTestResult Result, bool ConnectionFailed)> ProbeStreamAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
        {
            var warnings = new List<string>();
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, uri);
                message.Headers.Add("Icy-MetaData", "1");
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var finalUri = response.RequestMessage?.RequestUri ?? uri;
                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
                var name = HeaderValue(response, "icy-name");
                var status = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    return (new StreamTestResult(false, status, contentType, name, finalUri.ToString(), new() { $"The server answered HTTP {status}." }), false);
                }
                if (finalUri.Scheme == Uri.UriSchemeHttps)
                {
                    warnings.Add("HTTPS stream: older AVRs often can't play HTTPS. Use an http:// URL if the station offers one.");
                }

                bool ok;
                if (contentType.StartsWith("audio/mpegurl") || contentType.StartsWith("audio/x-mpegurl")
                    || contentType.Contains("vnd.apple.mpegurl") || contentType == "audio/x-scpls")
                {
                    ok = false;
                    warnings.Add($"This is a playlist ({contentType}), not a direct stream. Use the stream URL inside it (search adds direct URLs).");
                }
                else if (contentType.StartsWith("audio/") || contentType == "application/ogg" || contentType == "application/octet-stream")
                {
                    ok = true;
                }
                else if (contentType.StartsWith("text/html"))
                {
                    ok = false;
                    warnings.Add("This is a web page, not an audio stream.");
                }
                else
                {
                    ok = false;
                    warnings.Add($"Unexpected content type '{contentType}'. The AVR may not be able to play it.");
                }
                return (new StreamTestResult(ok, status, contentType, name, finalUri.ToString(), warnings), false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (new StreamTestResult(false, null, null, null, null, new() { "No answer within the time limit." }), true);
            }
            catch (HttpRequestException ex)
            {
                return (new StreamTestResult(false, null, null, null, null, new() { "Connection failed: " + DescribeConnectionError(ex) }), true);
            }
        }

        /// <summary>Human-readable reason, including the inner TLS/socket error that HttpRequestException hides.</summary>
        private static string DescribeConnectionError(HttpRequestException ex)
        {
            Exception inner = ex;
            while (inner.InnerException != null)
            {
                inner = inner.InnerException;
            }
            var reason = ReferenceEquals(inner, ex) ? ex.Message : inner.Message;

            if (ex.HttpRequestError == HttpRequestError.SecureConnectionError)
            {
                var hint = reason.Contains("UntrustedRoot") || reason.Contains("PartialChain")
                    ? " If this happens for many HTTPS stations, the server's CA certificates may be missing or outdated"
                      + " (Linux: sudo apt install --reinstall ca-certificates)."
                    : string.Empty;
                return $"HTTPS/TLS error: {reason}.{hint}";
            }
            if (ex.HttpRequestError == HttpRequestError.InvalidResponse)
            {
                // SHOUTcast v1 answers "ICY 200 OK", which is not valid HTTP for HttpClient but plays on most AVRs.
                return $"{reason} This may be an old SHOUTcast (ICY) server, which most AVRs can still play.";
            }
            return reason;
        }

        private static string? HeaderValue(HttpResponseMessage response, string name)
        {
            return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
        }

        private static List<CategoryDto> ToDto(RadioCategories categories)
        {
            return categories.Categories
                .Select(c => new CategoryDto(c.Key, c.Value.Select(s => new StationDto(s.Name, s.Url)).ToList()))
                .ToList();
        }

        private static RadioCategories FromDto(List<CategoryDto>? categories)
        {
            var result = new RadioCategories();
            foreach (var category in categories ?? new List<CategoryDto>())
            {
                var name = (category.Name ?? string.Empty).Trim();
                var stations = (category.Stations ?? new List<StationDto>())
                    .Select(s => new Station { Name = (s.Name ?? string.Empty).Trim(), Url = (s.Url ?? string.Empty).Trim() })
                    .ToList();
                if (!result.Categories.TryAdd(name, stations))
                {
                    // Keep the duplicate visible to validation instead of silently merging.
                    result.Categories[name + " (duplicate)"] = stations;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Allows the admin endpoints only on the admin port and blocks cross-site requests:
    /// state-changing requests must carry the custom header (which other sites can't send without CORS)
    /// and, if the browser sends an Origin, it must match the Host.
    /// </summary>
    public class AdminGuardFilter : IEndpointFilter
    {
        private readonly int adminPort;

        public AdminGuardFilter(int adminPort)
        {
            this.adminPort = adminPort;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
        {
            var context = invocationContext.HttpContext;
            if (context.Connection.LocalPort != adminPort)
            {
                return Results.NotFound();
            }

            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            headers["Referrer-Policy"] = "no-referrer";
            headers.CacheControl = "no-store";

            var method = context.Request.Method;
            if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
            {
                if (context.Request.Headers[AdminEndpoints.CSRF_HEADER] != "1")
                {
                    return Results.Problem(title: "Missing admin request header", statusCode: StatusCodes.Status403Forbidden);
                }
                string? origin = context.Request.Headers.Origin;
                if (!string.IsNullOrEmpty(origin)
                    && (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                        || !string.Equals(originUri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.Problem(title: "Cross-origin request blocked", statusCode: StatusCodes.Status403Forbidden);
                }
            }
            return await next(invocationContext);
        }
    }

    public record StationDto(string Name, string Url);

    public record CategoryDto(string Name, List<StationDto> Stations);

    public record StationsDocument(List<CategoryDto> Categories, string? ETag, string? File);

    public record StreamTestRequest(string? Url);

    /// <param name="SuggestedUrl">A working http:// alternative for an https:// URL (older AVRs can't play HTTPS).</param>
    public record StreamTestResult(bool Ok, int? Status, string? ContentType, string? Name, string? FinalUrl, List<string> Warnings,
        string? SuggestedUrl = null);

    public record SearchFilterOptions(List<FilterOption> Countries, List<FilterOption> Languages, List<FilterOption> Tags, List<FilterOption> Codecs);
}
