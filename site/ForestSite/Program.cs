using System.Text;
using System.Threading.RateLimiting;
using ForestSite;
using Microsoft.AspNetCore.RateLimiting;

// ------------------------------------------------------------------
// forest.deter.cloud (docs/website.md). One process: the JSON API under
// /api and the pages from wwwroot. Behind Caddy + Cloudflare in Docker.
//
//   FOREST_DATA         where the database and uploads live (default ./data)
//   FOREST_ADMIN_TOKEN  the owner's admin token (the author's; unset = no
//                       owner). The owner makes named tokens for other
//                       admins on /admin; they can do everything but that.
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
    // The admin page makes a few calls per view; a long random token is
    // out of reach of guessing at this rate.
    o.AddPolicy("admin", c => RateLimitPartition.GetFixedWindowLimiter(ClientIp(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseRateLimiter();
// Scripts and styles are linked as `/app.js?v=<hash of the file>`, so each
// deploy changes their URLs: Cloudflare's Browser Cache TTL (4 h, it
// overrides the origin's no-cache) can never pair an old app.js with a new
// API. The page itself is served with no-cache and never edge-cached.
// The map's terrain height grid (scripts/terrain-bake.py) is raw uint16s.
var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypes.Mappings[".u16"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = f =>
        f.Context.Response.Headers.CacheControl = f.Context.Request.Query.ContainsKey("v")
            ? "public, max-age=31536000, immutable" : "no-cache",
});
// The map's aerial photo tiles (scripts/aerial-bake.py, uploaded by the
// owner through /api/admin/aerial): too big for git, so they live with the
// data. A missing tile (open sea) is a plain 404, not the page.
// The 3D map's world (scripts/world-extract.py, /api/admin/world) the same
// way: meshes, textures and instance chunks read from the game's files.
string aerialDir = Path.Combine(dataDir, "aerial");
string worldDir = Path.Combine(dataDir, "world");
var binaryTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
binaryTypes.Mappings[".bin"] = "application/octet-stream";
foreach (var (dir, path, meta) in new[] { (aerialDir, "/aerial", "/aerial/aerial.json"), (worldDir, "/world", "/world/world.json") })
{
    Directory.CreateDirectory(dir);
    // A file asked for with another build's ?v= is refused: the files are
    // named by index, so a page still holding the previous upload's json
    // (open since before it, or mid-upload) would get this upload's file at
    // that index - leaves on a cave wall - and the browser / Cloudflare
    // would keep it for a day under the old URL. 404 + no-store: the page
    // re-reads the json (world3d.js) and nothing caches the miss. During an
    // upload (the json goes last; ?clear=1 deleted it) every ?v= is refused.
    var build = new MetaBuild(Path.Combine(dir, Path.GetFileName(meta)));
    app.Use((c, next) =>
    {
        if (!c.Request.Path.StartsWithSegments(path) || c.Request.Path == meta) return next(c);
        string v = c.Request.Query["v"];
        if (v == null || build.Accepts(v)) return next(c);
        c.Response.StatusCode = 404;
        c.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dir),
        RequestPath = path,
        ContentTypeProvider = binaryTypes,
        OnPrepareResponse = f => f.Context.Response.Headers.CacheControl = "public, max-age=86400",
    });
    // Not an endpoint: a matched endpoint makes the static files stand aside.
    app.Use((c, next) =>
    {
        if (!c.Request.Path.StartsWithSegments(path)) return next(c);
        // Nothing uploaded: "nothing here" without a 404 in every visitor's console.
        c.Response.StatusCode = c.Request.Path == meta ? 204 : 404;
        return Task.CompletedTask;
    });
}
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

api.MapGet("/runs/{id:long}", (long id, int? all) =>
    runs.Run(id, all == 1) is { } r ? Results.Json(r) : Problem(404, "no such run")).RequireRateLimiting("read");

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
    var (id, replaced, error) = runs.Submit(runner, await Body(req));
    return error != null ? Problem(400, error) : Results.Json(new { id, replaced });
}).RequireRateLimiting("upload");

// --- the author ----------------------------------------------------------------

// Who is asking: "owner" (the env token) or a named admin. Every change
// (not a read) is logged with its answer's status.
var admin = api.MapGroup("/admin").AddEndpointFilter(async (ctx, next) =>
{
    var http = ctx.HttpContext;
    string given = http.Request.Headers["X-Admin-Token"].FirstOrDefault() ?? "";
    bool owner = adminToken.Length > 0 && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(adminToken));
    string who = owner ? "owner" : store.AdminOf(given);
    if (who == null) return Problem(403, "admin token required");
    http.Items["admin"] = who;
    http.Items["owner"] = owner;

    object result = await next(ctx);
    if (!HttpMethods.IsGet(http.Request.Method))
        store.LogAdmin(who, http.Request.Method + " " + http.Request.Path.Value,
                       result is IStatusCodeHttpResult s ? s.StatusCode ?? 200 : 200);
    return result;
}).RequireRateLimiting("admin");

static bool IsOwner(HttpContext c) => c.Items["owner"] is true;

// The admin page (/admin) asks this first: is the token right, and whose?
admin.MapGet("/check", (HttpContext c) => Results.Json(new { ok = true, name = c.Items["admin"], owner = IsOwner(c) }));
admin.MapGet("/submissions", () => Results.Json(store.Submissions()));
admin.MapGet("/submissions/{id:long}", (long id) =>
    store.SubmissionText(id) is { } t ? Results.File(Encoding.UTF8.GetBytes(t), "text/plain; charset=utf-8", "submission-" + id + ".foseg") : Problem(404, "no such submission"));
admin.MapPost("/submissions/{id:long}/{status}", (long id, string status) =>
    status is "open" or "approved" or "rejected" && store.SetSubmissionStatus(id, status) ? Results.Ok() : Problem(400, "open / approved / rejected"));
admin.MapGet("/flagged", () =>
{
    using var c = store.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = @"SELECT id, segment_id, runner_id, runner_name, duration, hidden, route, uploaded,
                               COALESCE((SELECT name FROM routes x WHERE x.segment_id = runs.segment_id AND x.route = runs.route), '')
                        FROM runs WHERE flagged = 1 ORDER BY id DESC LIMIT 200";
    var list = new List<object>();
    using var r = cmd.ExecuteReader();
    while (r.Read())
        list.Add(new
        {
            id = r.GetInt64(0), segment = r.GetString(1), runner = r.GetString(2), name = r.GetString(3), duration = r.GetDouble(4),
            hidden = r.GetInt64(5) == 1, route = r.GetString(6), uploaded = r.GetString(7), spot = r.GetString(8),
        });
    return Results.Json(list);
});
admin.MapGet("/runners", () => Results.Json(store.Runners()));
admin.MapDelete("/spots/{id}", (string id) =>
{
    var (n, error) = store.DeleteSpot(id);
    return error == "no such spot" ? Problem(404, error) : error != null ? Problem(400, error) : Results.Json(new { runs = n });
});
admin.MapGet("/log", () => Results.Json(store.AdminLog()));

// Admins: the owner only.
admin.MapGet("/admins", (HttpContext c) => IsOwner(c) ? Results.Json(store.Admins()) : Problem(403, "only the owner manages admins"));
admin.MapPost("/admins", async (HttpContext c) =>
{
    if (!IsOwner(c)) return Problem(403, "only the owner manages admins");
    string name = ForestOverlay.Data.AttemptFormat.Clean((await Body(c.Request)).Trim());
    if (name.Length == 0 || name.Length > 40) return Problem(400, "a name, 1-40 characters");
    var (id, token) = store.AddAdmin(name);
    return Results.Json(new { id, name, token });
});
admin.MapDelete("/admins/{id:long}", (HttpContext c, long id) =>
    !IsOwner(c) ? Problem(403, "only the owner manages admins") : store.RevokeAdmin(id) ? Results.Ok() : Problem(404, "no such admin"));
// Aerial tiles: a zip of <layer>/<level>/<x>_<y>.jpg (+ aerial.json),
// extracted over what is there; ?clear=1 empties the folder first (the
// first of several chunks - Cloudflare caps a request at 100 MB). The 3D
// world likewise (UploadPath.IsWorld).
admin.MapPost("/aerial", (Delegate)((HttpContext c) => Upload(c, aerialDir, UploadPath.IsTile)));   // Delegate: not a RequestDelegate, the IResult counts
admin.MapPost("/world", (Delegate)((HttpContext c) => Upload(c, worldDir, UploadPath.IsWorld)));
async Task<IResult> Upload(HttpContext c, string dir, Func<string, bool> allowed)
{
    if (!IsOwner(c)) return Problem(403, "only the owner uploads map files");
    var size = c.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (size != null && !size.IsReadOnly) size.MaxRequestBodySize = 100L * 1024 * 1024;
    using var buffer = new MemoryStream();
    await c.Request.Body.CopyToAsync(buffer);
    if (c.Request.Query["clear"] == "1")
        foreach (string d in Directory.GetFileSystemEntries(dir))
            if (Directory.Exists(d)) Directory.Delete(d, true); else File.Delete(d);
    int files = 0;
    try
    {
        using var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/")) continue;
            if (!allowed(entry.FullName)) return Problem(400, "not a map file: " + entry.FullName);
            string to = Path.Combine(dir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            using var from = entry.Open();
            using var file = File.Create(to);
            await from.CopyToAsync(file);
            files++;
        }
    }
    catch (InvalidDataException) { return Problem(400, "not a zip"); }
    return Results.Json(new { files });
}
admin.MapPost("/runs/{id:long}/unflag", (long id) =>
    store.Update("UPDATE runs SET flagged = 0 WHERE id = $id", ("$id", id)) == 1 ? Results.Ok() : Problem(404, "no such run"));
admin.MapPost("/runs/{id:long}/hide", (long id) => store.HideRun(id, true) ? Results.Ok() : Problem(404, "no such run"));
admin.MapPost("/runs/{id:long}/show", (long id) => store.HideRun(id, false) ? Results.Ok() : Problem(404, "no such run"));
admin.MapDelete("/runs/{id:long}", (long id) => store.DeleteRun(id) ? Results.Ok() : Problem(404, "no such run"));
admin.MapPost("/runners/{id}/reset-token", (string id) => store.ResetToken(id) ? Results.Ok() : Problem(404, "no such runner"));
admin.MapPost("/runners/{id}/ban", (string id) => store.Ban(id, true) ? Results.Ok() : Problem(404, "no such runner"));
admin.MapPost("/runners/{id}/unban", (string id) => store.Ban(id, false) ? Results.Ok() : Problem(404, "no such runner"));

// Pages are paths the script routes (/spot/<id>, /about, /admin); any
// other path is the app too, except under /api, which answers 404 as JSON.
// The page routes are mapped by name as well: the fallback skips paths that
// look like files, and old segment ids have dots (spot.my.new-spot-3).
IResult Page(HttpContext c)
{
    c.Response.Headers.CacheControl = "no-cache";
    return Results.Content(indexHtml, "text/html; charset=utf-8");
}
app.MapGet("/", Page);
app.MapGet("/spot/{**rest}", Page);
app.MapGet("/admin/{**rest}", Page);
app.MapGet("/about", Page);
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
