using System.Diagnostics;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using PicLoom.Api.Security;
using PicLoom.Core;
using PicLoom.Infrastructure;

namespace PicLoom.Api;

public static class ApiApplication
{
    public static async Task<WebApplication> CreateAsync(string[] args, Action<WebApplicationBuilder>? customize = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(ApiApplication).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });
        customize?.Invoke(builder);
        var port = builder.Configuration.GetValue("PicLoom:Port", 5278);
        if (port is < 1024 or > 65535) throw new InvalidOperationException("Invalid local port.");
        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
            throw new InvalidOperationException("Custom Kestrel endpoints are not supported.");
        var dataDirectory = Path.GetFullPath(builder.Configuration["PicLoom:DataDirectory"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PicLoom"));
        var options = new PicLoomOptions(port, dataDirectory, builder.Environment.IsDevelopment());
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, port);
            server.Limits.MaxRequestBodySize = 131072;
            server.AddServerHeader = false;
        });
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
        builder.Services.Configure<RouteHandlerOptions>(routing => routing.ThrowOnBadRequest = true);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<LocalSessionService>();
        builder.Services.AddSingleton(_ => new DataDirectoryLease(dataDirectory));
        builder.Services.AddPicLoomInfrastructure(dataDirectory);
        var app = builder.Build();
        try
        {
            _ = app.Services.GetRequiredService<DataDirectoryLease>();
            await InfrastructureRegistration.InitializeAsync(app.Services);
            app.Use(async (context, next) =>
            {
                // The global Kestrel limit stays small; only the multipart reference upload gets a
                // larger body budget, and only while its size is still adjustable.
                if (HttpMethods.IsPost(context.Request.Method)
                    && context.Request.Path.Equals("/api/references", StringComparison.OrdinalIgnoreCase))
                {
                    var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                    if (sizeFeature is { IsReadOnly: false })
                        sizeFeature.MaxRequestBodySize = ReferenceRules.MaxRequestBodyBytes;
                }

                await next(context);
            });
            app.UseMiddleware<LocalAccessMiddleware>();
            app.Use(async (context, next) =>
            {
                try { await next(context); }
                catch (DomainException ex)
                {
                    context.Response.StatusCode = ex.Code switch
                    {
                        "not_found" or "reference_not_found" => 404,
                        "group_reserved" or "group_not_empty" or "conflict" or "reference_in_use" => 409,
                        _ => 400
                    };
                    await context.Response.WriteAsJsonAsync(new { code = ex.Code });
                }
                catch (BadHttpRequestException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { code = "validation_failed" });
                }
                catch (DbUpdateConcurrencyException)
                {
                    context.Response.StatusCode = 409;
                    await context.Response.WriteAsJsonAsync(new { code = "conflict" });
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    app.Logger.LogError("Request failed: {ErrorType}", ex.GetType().Name);
                    if (context.Response.HasStarted) throw;
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new { code = "internal_error" });
                }
            });
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapPicLoomEndpoints();
            app.MapFallbackToFile("index.html");
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public static void OpenBrowser(IServiceProvider services, bool developmentUi = false)
    {
        var uri = services.GetRequiredService<LocalSessionService>().CreateLaunchUri(developmentUi);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
