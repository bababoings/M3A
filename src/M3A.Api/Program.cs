using M3A.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddM3AApi(builder.Configuration);

var app = builder.Build();

app.UseM3APipeline();

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the API in integration tests.
/// </summary>
public partial class Program;
