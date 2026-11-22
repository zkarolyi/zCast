using Browser;
using My;
using System.Net.Http.Headers;

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

        private static IResult Search(string? q, RadioBrowser radioBrowser)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            {
                return Results.BadRequest(new { title = "Enter at least 2 characters" });
            }
            var results = radioBrowser.Search(q.Trim(), 50).Select(s => new
            {
                name = s.Name.Trim(),
                url = string.IsNullOrEmpty(s.UrlResolved) ? s.Url : s.UrlResolved,
                codec = s.Codec,
                bitrate = s.Bitrate,
                country = s.CountryCode,
                tags = string.Join(", ", s.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Take(4)),
            });
            return Results.Ok(results);
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

            var warnings = new List<string>();
            var client = httpClientFactory.CreateClient(STREAM_TEST_CLIENT);
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
                    return Results.Ok(new StreamTestResult(false, status, contentType, name, finalUri.ToString(), new() { $"The server answered HTTP {status}." }));
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
                return Results.Ok(new StreamTestResult(ok, status, contentType, name, finalUri.ToString(), warnings));
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Results.Ok(new StreamTestResult(false, null, null, null, null, new() { "No answer within the time limit." }));
            }
            catch (HttpRequestException ex)
            {
                // SHOUTcast v1 answers "ICY 200 OK", which is not valid HTTP for HttpClient but plays on most AVRs.
                var hint = ex.HttpRequestError == HttpRequestError.InvalidResponse
                    ? " This may be an old SHOUTcast (ICY) server, which most AVRs can still play."
                    : string.Empty;
                return Results.Ok(new StreamTestResult(false, null, null, null, null, new() { $"Connection failed: {ex.Message}{hint}" }));
            }
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

    public record StreamTestResult(bool Ok, int? Status, string? ContentType, string? Name, string? FinalUrl, List<string> Warnings);
}
