using Admin;
using Browser;
using Microsoft.OpenApi.Models;
using My;
using Helpers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ZCast", Version = "v1" });
});
// stations.yml location: Stations:File (env Stations__File), default next to the binaries.
builder.Services.AddSingleton<StationStore>();
builder.Services.AddScoped<MyStation, MyStation>();
builder.Services.AddScoped<RadioBrowser, RadioBrowser>();
builder.Services.AddScoped<IconFetcher, IconFetcher>();

builder.Services.AddHttpClient(RadioBrowser.CLIENT_NAME, client =>
{
    client.BaseAddress = new Uri(RadioBrowser.API_ENDPOINT);
    client.DefaultRequestHeaders.Add("User-Agent", Generic.USER_AGENT + "/" + Generic.VERSION);
});

// Used by the admin page's "Test stream" check.
builder.Services.AddHttpClient(AdminEndpoints.STREAM_TEST_CLIENT, client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.Add("User-Agent", Generic.USER_AGENT + "/" + Generic.VERSION);
});

builder.Services.AddMemoryCache();

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ZCast V1");
    });
}

app.UseAuthorization();

app.MapControllers();

// Admin page for "My Stations": only on the port set in Admin:Port (env Admin__Port).
app.MapAdmin();

app.Run();
