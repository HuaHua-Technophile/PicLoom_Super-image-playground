using PicLoom.Api.Security;
using PicLoom.Core;

namespace PicLoom.Api;

public static class Endpoints
{
    public sealed record BootstrapInput(string Token);

    public static void MapPicLoomEndpoints(this WebApplication app)
    {
        app.MapGet("/health", (IProviderRegistry registry) =>
            Results.Ok(new { status = "ok", providers = registry.Ids }));
        app.MapPost("/api/session/bootstrap", (BootstrapInput input, LocalSessionService sessions, HttpContext context) =>
        {
            if (!sessions.TryRedeem(input.Token, out var cookie, out var csrf))
                return Results.Json(new { code = "invalid_bootstrap" }, statusCode: 401);
            context.Response.Cookies.Append(LocalSessionService.CookieName, cookie, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = false,
                Path = "/",
                MaxAge = TimeSpan.FromHours(8),
                IsEssential = true
            });
            return Results.Ok(new { csrfToken = csrf });
        });
        app.MapGet("/api/session", (HttpContext context) => Results.Ok(new { csrfToken = context.Items["csrf"] }));

        var templates = app.MapGroup("/api/templates");
        templates.MapGet("/", (IPicLoomService service, CancellationToken ct) => service.GetTemplatesAsync(ct));
        templates.MapGet("/{id:guid}", (Guid id, IPicLoomService service, CancellationToken ct) => service.GetTemplateAsync(id, ct));
        templates.MapPost("/", async (TemplateInput input, IPicLoomService service, CancellationToken ct) =>
        {
            var value = await service.CreateTemplateAsync(input, ct);
            return Results.Created($"/api/templates/{value.Id}", value);
        });
        templates.MapPut("/{id:guid}", (Guid id, TemplateInput input, IPicLoomService service, CancellationToken ct) => service.UpdateTemplateAsync(id, input, ct));
        templates.MapDelete("/{id:guid}", async (Guid id, IPicLoomService service, CancellationToken ct) =>
        {
            await service.DeleteTemplateAsync(id, ct);
            return Results.NoContent();
        });

        var resolutions = app.MapGroup("/api/resolutions");
        resolutions.MapGet("/", (IPicLoomService service, CancellationToken ct) => service.GetResolutionsAsync(ct));
        resolutions.MapGet("/{id:guid}", (Guid id, IPicLoomService service, CancellationToken ct) => service.GetResolutionAsync(id, ct));
        resolutions.MapPost("/", async (ResolutionInput input, IPicLoomService service, CancellationToken ct) =>
        {
            var value = await service.CreateResolutionAsync(input, ct);
            return Results.Created($"/api/resolutions/{value.Id}", value);
        });
        resolutions.MapPut("/{id:guid}", (Guid id, ResolutionInput input, IPicLoomService service, CancellationToken ct) => service.UpdateResolutionAsync(id, input, ct));
        resolutions.MapDelete("/{id:guid}", async (Guid id, IPicLoomService service, CancellationToken ct) =>
        {
            await service.DeleteResolutionAsync(id, ct);
            return Results.NoContent();
        });

        var groups = app.MapGroup("/api/groups");
        groups.MapGet("/", (IPicLoomService service, CancellationToken ct) => service.GetGroupsAsync(ct));
        groups.MapGet("/{id:guid}", (Guid id, IPicLoomService service, CancellationToken ct) => service.GetGroupAsync(id, ct));
        groups.MapPost("/", async (GroupInput input, IPicLoomService service, CancellationToken ct) =>
        {
            var value = await service.CreateGroupAsync(input, ct);
            return Results.Created($"/api/groups/{value.Id}", value);
        });
        groups.MapPut("/{id:guid}", (Guid id, GroupInput input, IPicLoomService service, CancellationToken ct) => service.UpdateGroupAsync(id, input, ct));
        groups.MapDelete("/{id:guid}", async (Guid id, IPicLoomService service, CancellationToken ct) =>
        {
            await service.DeleteGroupAsync(id, ct);
            return Results.NoContent();
        });

        app.MapGet("/api/tasks", (Guid? groupId, IPicLoomService service, CancellationToken ct) => service.GetTasksAsync(groupId, ct));
        app.MapGet("/api/tasks/{id:guid}", (Guid id, IPicLoomService service, CancellationToken ct) => service.GetTaskAsync(id, ct));
        app.MapPost("/api/tasks", async (CreateTaskInput input, IPicLoomService service, CancellationToken ct) =>
        {
            var value = await service.CreateTaskAsync(input, ct);
            return Results.Accepted($"/api/tasks/{value.Id}", value);
        });

        var settings = app.MapGroup("/api/settings");
        settings.MapGet("/provider", (IProviderSettingsService service, CancellationToken ct) => service.GetAsync(ct));
        settings.MapPut("/provider", (ProviderSettingsInput input, IProviderSettingsService service, CancellationToken ct) =>
            service.UpsertAsync(input, ct));
        settings.MapDelete("/provider", async (IProviderSettingsService service, CancellationToken ct) =>
        {
            await service.ClearAsync(ct);
            return Results.NoContent();
        });
        settings.MapPost("/provider/test", async (HttpContext context, IProviderSettingsService service,
            CancellationToken ct) =>
        {
            var input = await ReadOptionalBodyAsync(context, ct);
            return Results.Ok(await service.TestAsync(input, ct));
        });

        app.MapGet("/api/assets/{id:guid}/content", async (Guid id, IPicLoomService service, IAssetStore store, CancellationToken ct) =>
        {
            var asset = await service.GetAssetAsync(id, ct);
            return Results.Stream(store.OpenRead(asset.RelativePath), asset.MimeType);
        });
        app.MapGet("/api/assets/{id:guid}/thumbnail", async (Guid id, IPicLoomService service, IAssetStore store, CancellationToken ct) =>
        {
            var asset = await service.GetAssetAsync(id, ct);
            return Results.Stream(store.OpenRead(asset.ThumbnailPath), asset.MimeType);
        });

        var references = app.MapGroup("/api/references");
        references.MapPost("/", async (HttpContext context, IPicLoomService service, IReferenceStore store,
            CancellationToken ct) =>
        {
            if (!context.Request.HasFormContentType)
                throw new DomainException("reference_type_invalid");

            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(ct);
            }
            catch (InvalidDataException)
            {
                throw new DomainException("reference_type_invalid");
            }
            catch (BadHttpRequestException)
            {
                throw new DomainException("reference_too_large");
            }

            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) throw new DomainException("reference_type_invalid");
            if (file.Length > ReferenceRules.MaxBytes) throw new DomainException("reference_too_large");

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var view = await service.CreateReferenceAsync(buffer.ToArray(), ct);
            return Results.Created($"/api/references/{view.Id}", view);
        });
        references.MapPost("/from-url", async (ReferenceUrlInput input, IPicLoomService service,
            CancellationToken ct) =>
        {
            var view = await service.CreateReferenceFromUrlAsync(input.Url, ct);
            return Results.Created($"/api/references/{view.Id}", view);
        });
        references.MapGet("/{id:guid}/content", async (Guid id, IPicLoomService service, IReferenceStore store,
            CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(id, ct);
            return Results.Stream(store.OpenRead(reference.RelativePath), reference.MimeType);
        });
        references.MapGet("/{id:guid}/thumbnail", async (Guid id, IPicLoomService service,
            IReferenceStore store, CancellationToken ct) =>
        {
            var reference = await service.GetReferenceAsync(id, ct);
            return Results.Stream(store.OpenRead(reference.ThumbnailPath), reference.MimeType);
        });
        references.MapDelete("/{id:guid}", async (Guid id, IPicLoomService service, CancellationToken ct) =>
        {
            await service.DeleteReferenceAsync(id, ct);
            return Results.NoContent();
        });

        app.Map("/api/{**path}", () => Results.NotFound(new { code = "not_found" }));
    }

    /// <summary>Body of <c>POST /api/references/from-url</c>.</summary>
    public sealed record ReferenceUrlInput(string? Url);

    private static readonly System.Text.Json.JsonSerializerOptions OptionalBodyJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads an optional JSON body: an absent or empty body means "use the stored values", which is
    /// how the connection probe supports both "test before save" and "test what is saved".
    /// The body is read as text first because chunked requests carry no Content-Length.
    /// </summary>
    private static async Task<ProviderSettingsInput?> ReadOptionalBodyAsync(HttpContext context,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(context.Request.Body,
            System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ProviderSettingsInput>(raw, OptionalBodyJson);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new DomainException("validation_failed");
        }
    }
}
