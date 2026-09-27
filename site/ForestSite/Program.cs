using System.Text;
using System.Threading.RateLimiting;
using ForestSite;
using Microsoft.AspNetCore.RateLimiting;

// ------------------------------------------------------------------
// forest.deter.cloud (docs/website.md). One process: the JSON API under
// /api and the pages from wwwroot. Behind Caddy + Cloudflare in Docker.
//
//   FOREST_DATA         where the database and uploads live (default ./data)
//   FOREST_ADMIN_TOKEN  enables /api/admin (the author's; unset = off)
// ------------------------------------------------------------------
const int MaxBody = 4 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxBody);

string dataDir = Environment.GetEnvironmentVariable("FOREST_DATA") ?? Path.Combine(AppContext.BaseDirectory, "data");
string adminToken = Environment.GetEnvironmentVariable("FOREST_ADMIN_TOKEN") ?? "";
var store = new Store(dataDir);
var runs = new Runs(store);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(runs);

// Cloudflare names the visitor; everything else sees Caddy's address.
static string ClientIp(HttpContext c) =>
    c.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? c.Connection.RemoteIpAddress?.ToString() ?? "?";

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("read", c => RateLimitPartition.GetFixedWindowLimiter(ClientIp(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("register", c => RateLimitPartition.GetFixedWindowLimiter(ClientIp(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1) }));
    // Uploads are automatic, one per finished attempt: a busy practice
    // session is a few hundred an hour at most.
    o.AddPolicy("upload", c => RateLimitPartition.GetFixedWindowLimiter(
        c.Request.Headers.Authorization.FirstOrDefault() ?? ClientIp(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromHours(1) }));
});

var app = builder.Build();
app.UseRateLimiter();
// Scripts and styles are linked as `/app.js?v=<hash of the file>`, so each
// deploy changes their URLs: Cloudflare's Browser Cache TTL (4 h, it
// overrides the origin's no-cache) can never pair an old app.js with a new
// API. The page itself is served with no-cache and never edge-cached.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = f =>
        f.Context.Response.Headers.CacheControl = f.Context.Request.Query.ContainsKey("v")
            ? "public, max-age=31536000, immutable" : "no-cache",
});
string indexHtml = Pages.Index(app.Environment.WebRootPath);

int packs = runs.LoadCommunity(Path.Combine(AppContext.BaseDirectory, "community"), m => app.Logger.LogWarning("{m}", m));
app.Logger.LogInformation("Data in {dir}; {n} community pack(s); admin {admin}", dataDir, packs, adminToken.Length > 0 ? "on" : "off");

static async Task<string> Body(HttpRequest r)
{
    using var reader = new StreamReader(r.Body, Encoding.UTF8);
    return await reader.ReadToEndAsync();
}

static string Bearer(HttpRequest r)
{
    string h = r.Headers.Authorization.FirstOrDefault() ?? "";
    return h.StartsWith("Bearer ", StringComparison.Ordinal) ? h.Substring(7).Trim() : null;
}

static IResult Problem(int status, string message) => Results.Json(new { error = message }, statusCode: status);

var api = app.MapGroup("/api");

// --- reads -----------------------------------------------------------------

api.MapGet("/spots", () => Results.Json(runs.Spots())).RequireRateLimiting("read");

api.MapGet("/spots/{id}", (string id) =>
    runs.Spot(id) is { } s ? Results.Json(s) : Problem(404, "no such spot")).RequireRateLimiting("read");

api.MapGet("/spots/{id}/{route}/runner/{runner}", (string id, string route, string runner) =>
    Results.Json(runs.RunnerRuns(id, route, runner))).RequireRateLimiting("read");

// The plugin's comparisons: each runner's best on a route, as text
// (src/Data/SiteBoard). An unknown spot or route is an empty board.
api.MapGet("/spots/{id}/{route}/board.txt", (string id, string route) =>
    Results.Text(runs.BoardText(id, route), "text/plain; charset=utf-8")).RequireRateLimiting("read");

api.MapGet("/runs/{id:long}", (long id) =>
    runs.Run(id) is { } r ? Results.Json(r) : Problem(404, "no such run")).RequireRateLimiting("read");

api.MapGet("/runs/{id:long}/file", (long id) =>
{
    var (_, text) = runs.RunFile(id);
    return text == null ? Problem(404, "no such run")
        : Results.File(Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", id + ".run");
}).RequireRateLimiting("read");

// --- the plugin --------------------------------------------------------------

// { "runner": "r-<16 hex>", "name": "..." } -> { "token": "..." }. The
// first registration owns the id; the plugin keeps the token.
api.MapPost("/register", async (HttpRequest req) =>
{
    RegisterRequest body;
    try { body = await req.ReadFromJsonAsync<RegisterRequest>(); }
    catch { return Problem(400, "expected JSON { runner, name }"); }
    if (body == null || !RunnerIds.Valid(body.runner)) return Problem(400, "runner must be r- and 16 hex digits");
    string name = ForestOverlay.Data.AttemptFormat.Clean(body.name ?? "");
    if (name.Length > 40) name = name.Substring(0, 40);
    string token = store.Register(body.runner, name);
    return token == null ? Problem(409, "this runner id is already registered") : Results.Json(new { token });
}).RequireRateLimiting("register");

// A .foseg (segment + attempts) in the body; Authorization: Bearer <token>.
api.MapPost("/runs", async (HttpRequest req) =>
{
    string runner = store.RunnerOf(Bearer(req));
    if (runner == null) return Problem(401, "unknown token");
    var res = runs.Upload(runner, await Body(req));
    if (res.Error != null) return Problem(400, res.Error);
    if (res.Added.Count == 0 && res.Existing.Count == 0) return Problem(422, string.Join("; ", res.Skipped));
    return Results.Json(new { added = res.Added, existing = res.Existing, skipped = res.Skipped });
}).RequireRateLimiting("upload");

// A spot for the author to approve.
api.MapPost("/submissions", async (HttpRequest req) =>
{
    string runner = store.RunnerOf(Bearer(req));
    if (runner == null) return Problem(401, "unknown token");
    var (id, error) = runs.Submit(runner, await Body(req));
    return error != null ? Problem(400, error) : Results.Json(new { id });
}).RequireRateLimiting("upload");

// --- the author ----------------------------------------------------------------

var admin = api.MapGroup("/admin").AddEndpointFilter(async (ctx, next) =>
{
    string given = ctx.HttpContext.Request.Headers["X-Admin-Token"].FirstOrDefault() ?? "";
    bool ok = adminToken.Length > 0 && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(adminToken));
    return ok ? await next(ctx) : Problem(403, "admin token required");
}).RequireRateLimiting("register");

admin.MapGet("/submissions", () =>
{
    using var c = store.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT id, runner_id, segment_id, name, uploaded, status FROM submissions ORDER BY id DESC LIMIT 200";
    var list = new List<object>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
        list.Add(new { id = r.GetInt64(0), runner = r.GetString(1), segment = r.GetString(2), name = r.GetString(3), uploaded = r.GetString(4), status = r.GetString(5) });
    return Results.Json(list);
});
admin.MapGet("/submissions/{id:long}", (long id) =>
    store.SubmissionText(id) is { } t ? Results.File(Encoding.UTF8.GetBytes(t), "text/plain; charset=utf-8", "submission-" + id + ".foseg") : Problem(404, "no such submission"));
admin.MapPost("/submissions/{id:long}/{status}", (long id, string status) =>
    status is "open" or "approved" or "rejected" && store.SetSubmissionStatus(id, status) ? Results.Ok() : Problem(400, "open / approved / rejected"));
admin.MapGet("/flagged", () =>
{
    using var c = store.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT id, segment_id, runner_id, runner_name, duration, hidden FROM runs WHERE flagged = 1 ORDER BY id DESC LIMIT 200";
    var list = new List<object>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
        list.Add(new { id = r.GetInt64(0), segment = r.GetString(1), runner = r.GetString(2), name = r.GetString(3), duration = r.GetDouble(4), hidden = r.GetInt64(5) == 1 });
    return Results.Json(list);
});
admin.MapPost("/runs/{id:long}/hide", (long id) => store.HideRun(id, true) ? Results.Ok() : Problem(404, "no such run"));
admin.MapPost("/runs/{id:long}/show", (long id) => store.HideRun(id, false) ? Results.Ok() : Problem(404, "no such run"));
admin.MapDelete("/runs/{id:long}", (long id) => store.DeleteRun(id) ? Results.Ok() : Problem(404, "no such run"));
admin.MapPost("/runners/{id}/reset-token", (string id) => store.ResetToken(id) ? Results.Ok() : Problem(404, "no such runner"));
admin.MapPost("/runners/{id}/ban", (string id) => store.Ban(id, true) ? Results.Ok() : Problem(404, "no such runner"));
admin.MapPost("/runners/{id}/unban", (string id) => store.Ban(id, false) ? Results.Ok() : Problem(404, "no such runner"));

// Pages use hash routes (#/spot/<id>); any other path is the app, except
// under /api, which answers 404 as JSON.
IResult Page(HttpContext c)
{
    c.Response.Headers.CacheControl = "no-cache";
    return Results.Content(indexHtml, "text/html; charset=utf-8");
}
app.MapGet("/", Page);
api.MapFallback(() => Problem(404, "no such endpoint"));
app.MapFallback(Page);

app.Run();

public sealed class RegisterRequest
{
    public string runner { get; set; }
    public string name { get; set; }
}

public static class Pages
{
    /// index.html with every local script / stylesheet link stamped with a
    /// hash of that file.
    public static string Index(string webRoot)
    {
        string html = File.ReadAllText(Path.Combine(webRoot, "index.html"));
        return System.Text.RegularExpressions.Regex.Replace(html, @"(src|href)=""/([a-z0-9_.-]+\.(?:js|css))""", m =>
        {
            string path = Path.Combine(webRoot, m.Groups[2].Value);
            if (!File.Exists(path)) return m.Value;
            byte[] hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            return m.Groups[1].Value + "=\"/" + m.Groups[2].Value + "?v=" + Convert.ToHexString(hash, 0, 5).ToLowerInvariant() + "\"";
        });
    }
}

public static class RunnerIds
{
    /// `r-` + 16 hex digits (Game/RunnerIdentity).
    public static bool Valid(string id)
    {
        if (id == null || id.Length != 18 || !id.StartsWith("r-", StringComparison.Ordinal)) return false;
        for (int i = 2; i < id.Length; i++)
            if (!Uri.IsHexDigit(id[i])) return false;
        return true;
    }
}

public partial class Program { }
