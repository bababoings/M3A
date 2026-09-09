using M3A.Api.Routes;

namespace M3A.Api.Extensions;

/// <summary>Builds the HTTP pipeline.</summary>
public static class WebApplicationExtensions
{
    /// <summary>Applies middleware and mounts every route module.</summary>
    public static WebApplication UseM3APipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapM3ARoutes();

        return app;
    }
}
