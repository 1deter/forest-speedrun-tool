using System.Text;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using ForestOverlay.Data;
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
//   FOREST_ORIGIN_SECRET  when set, every request must carry it in the
//                       X-Forest-Origin header - a Cloudflare Transform Rule
//                       adds it, so a request straight to the VPS (past
//                       Cloudflare, with a made-up CF-Connecting-IP) is
//                       refused (site/deploy/README.md *Origin lock*).
//   FOREST_DISCORD_WEBHOOK  a Discord webhook URL: a new PB on a community
//                       spot or a run spot is posted there (PbWebhook).
//                       Unset = off. Env or appsettings.json.
//   FOREST_SITE_URL     the public address for links in those posts and
//                       in link previews' og:url (default
//                       https://forest.deter.cloud) - never the request's
//                       Host header.
// ------------------------------------------------------------------
const int MaxBody = 4 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxBody);

string dataDir = Environment.GetEnvironmentVariable("FOREST_DATA") ?? Path.Combine(AppContext.BaseDirectory, "data");
string adminToken = Environment.GetEnvironmentVariable("FOREST_ADMIN_TOKEN") ?? "";
string originSecret = Environment.GetEnvironmentVariable("FOREST_ORIGIN_SECRET") ?? "";
var store = new Store(dataDir);
var categories = new Categories(store);
var runs = new Runs(store, c => categories.IsPublished(c));
var attempts = new Attempts(store, dataDir, null, categories);
var webhook = new PbWebhook(builder.Configuration["FOREST_DISCORD_WEBHOOK"], builder.Configuration["FOREST_SITE_URL"]);
// Link previews name the configured address, not the request's Host
// (security audit, 2026-10-04: a Host header is the client's to choose).
string siteUrl = PbNews.SiteUrl(builder.Configuration["FOREST_SITE_URL"]);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(runs);
builder.Services.AddSingleton(webhook);

// Cloudflare names the visitor; everything else sees Caddy's address.
// The header is only as honest as the origin lock (FOREST_ORIGIN_SECRET):
// without it a request straight to the VPS can name any address.
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
    // session is a few hundred an hour at most. Keyed on the address, not
    // the Authorization header: a made-up header per request would be a
    // fresh limiter each (kept an hour - memory), before the token is even
    // checked.
    o.AddPolicy("upload", c => RateLimitPartition.GetFixedWindowLimiter(ClientIp(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromHours(1) }));
    // Run mode attempts (docs/run-mode.md phase 2): a start, a checkpoint a
    // minute and a log per attempt - a runner resetting every few seconds
    // makes a few hundred an hour.
    o.AddPolicy("attempt", c => RateLimitPartition.GetFixedWindowLimiter(ClientIp(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 3000, Window = TimeSpan.FromHours(1) }));
    // The admin page makes a few calls per view; a long random token is
    // out of reach of guessing at this rate.
    o.AddPolicy("admin", c => RateLimitPartition.GetFixedWindowLimiter(ClientIp(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
webhook.Log = m => app.Logger.LogWarning("{m}", m);

// The origin lock (above): nothing else runs for a request that did not
// come through Cloudflare. Constant-time, like the admin token.
if (originSecret.Length > 0)
{
    byte[] expected = Encoding.UTF8.GetBytes(originSecret);
    app.Use((c, next) =>
    {
        string given = c.Request.Headers["X-Forest-Origin"].FirstOrDefault() ?? "";
        if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), expected))
            return next(c);
        c.Response.StatusCode = 403;
        return Task.CompletedTask;
    });
}

// Security headers on every answer (docs/website.md *Security*). Scripts
// are the site's own only - three.js is served from wwwroot/vendor, not a
// CDN - so a script injected anywhere could not run, and the admin token
// in localStorage stays out of reach. Fonts are Google's. The one frame
// allowed is YouTube's no-cookie player (/compare, compare.js), driven by
// postMessage - no YouTube script runs on the page.
const string Csp = "default-src 'self'; script-src 'self'; style-src 'self' https://fonts.googleapis.com; " +
                   "frame-src https://www.youtube-nocookie.com; " +
                   "font-src https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; " +
                   "worker-src 'self' blob:; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
app.Use((c, next) =>
{
    var h = c.Response.Headers;
    h.ContentSecurityPolicy = Csp;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    h.StrictTransportSecurity = "max-age=31536000";
    return next(c);
});

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
binaryTypes.Mappings[".gz"] = "application/gzip";
binaryTypes.Mappings[".br"] = "application/octet-stream";
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
    // The world's .bin / .json: the upload's Brotli or gzip copy
    // (Precompressed, Brotli first) to a client that takes it - served as
    // that file by the static files below (ETag, ranges, 304s as before),
    // labelled as the original.
    var served = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dir);
    app.Use((c, next) =>
    {
        if (!c.Request.Path.StartsWithSegments(path, out var rest) || !Precompressed.Compressible(rest.Value ?? "")) return next(c);
        c.Response.Headers.Vary = "Accept-Encoding";
        if (!binaryTypes.TryGetContentType(rest.Value!, out string type)) return next(c);
        string accept = c.Request.Headers.AcceptEncoding;
        foreach (var (suffix, coding) in Precompressed.Codings)
            if (Precompressed.Accepts(accept, coding) && served.GetFileInfo(rest.Value + suffix).Exists)
            {
                c.Items["encoded"] = (type, coding);
                c.Request.Path += suffix;
                break;
            }
        return next(c);
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = served,
        RequestPath = path,
        ContentTypeProvider = binaryTypes,
        OnPrepareResponse = f =>
        {
            var r = f.Context.Response;
            r.Headers.CacheControl = "public, max-age=86400";
            if (f.Context.Items["encoded"] is (string type, string coding))
            {
                r.ContentType = type;
                r.Headers.ContentEncoding = coding;
            }
        },
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

// Run categories (phase 4): the presets once, speedrun.com a minute after
// start and then daily (FOREST_SRC_SYNC=off: never - the tests).
categories.SeedPresets();
if (Environment.GetEnvironmentVariable("FOREST_SRC_SYNC") != "off")
{
    var srcHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromMinutes(1));
        while (true)
        {
            try
            {
                var done = await categories.Sync(srcHttp);
                if (done.Count > 0) store.LogAdmin(Categories.SyncUser, "sync: " + string.Join("; ", done), 200);
            }
            catch (Exception ex) { app.Logger.LogWarning("speedrun.com sync failed: {m}", ex.Message); }
            await Task.Delay(TimeSpan.FromDays(1));
        }
    });
}

int packs = runs.LoadCommunity(Path.Combine(AppContext.BaseDirectory, "community"), m => app.Logger.LogWarning("{m}", m));
app.Logger.LogInformation("Data in {dir}; {n} community pack(s); admin {admin}; Discord PB posts {hook}", dataDir, packs,
                          adminToken.Length > 0 ? "on" : "off", webhook.On ? "on" : "off");
// The world uploaded before Brotli has .gz copies only: its .br ones, once.
_ = Task.Run(() =>
{
    try
    {
        var t = System.Diagnostics.Stopwatch.StartNew();
        int n = Precompressed.Backfill(worldDir);
        if (n > 0) app.Logger.LogInformation("World: {n} Brotli copies written in {s:0} s", n, t.Elapsed.TotalSeconds);
    }
    catch (Exception ex) { app.Logger.LogWarning("World Brotli copies failed: {m}", ex.Message); }
});

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
    // Only queued: the webhook never fails or slows an upload.
    if (res.Pb is { } pb)
        try { webhook.Enqueue(PbNews.Message(pb.Runner, pb.Spot, pb.Time, pb.PreviousBest, PbNews.RunLink(webhook.SiteUrl, pb.Segment, pb.Route, pb.RunId))); }
        catch (Exception ex) { app.Logger.LogWarning("Discord webhook: {m}", ex.Message); }
    return Results.Json(new { added = res.Added, existing = res.Existing, skipped = res.Skipped });
}).RequireRateLimiting("upload");

// A runner deleting their own spot (the game's Practice -> Share ->
// Delete from the website): the same token as uploads proves who asks.
// Logged in the admins' Activity tab as "runner <id>".
api.MapDelete("/spots/{id}", (HttpRequest req, string id) =>
{
    string runner = store.RunnerOf(Bearer(req));
    if (runner == null) return Problem(401, "unknown token");
    var (status, message, n) = runs.DeleteOwnSpot(runner, id);
    store.LogAdmin("runner " + runner, "DELETE /api/spots/" + (id.Length > 80 ? id.Substring(0, 80) : id) + " (" + message + ")", status);
    return status == 200 ? Results.Json(new { runs = n }) : Problem(status, message);
}).RequireRateLimiting("upload");

// A spot for the author to approve.
api.MapPost("/submissions", async (HttpRequest req) =>
{
    string runner = store.RunnerOf(Bearer(req));
    if (runner == null) return Problem(401, "unknown token");
    var (id, replaced, error) = runs.Submit(runner, await Body(req));
    return error != null ? Problem(400, error) : Results.Json(new { id, replaced });
}).RequireRateLimiting("upload");

// --- run mode attempts (Attempts; docs/run-mode.md phase 2) ----------------------

static IResult Answer(Attempts.Answer a) => Results.Json(a.Body, statusCode: a.Status);

// { attempt, category, spot } -> { nonce }: the attempt starts.
api.MapPost("/attempts", async (HttpRequest req) =>
{
    string runner = store.RunnerOf(Bearer(req));
    if (runner == null) return Problem(401, "unknown token");
    JsonObject body;
    try { body = await req.ReadFromJsonAsync<JsonObject>(); }
    catch { return Problem(400, "expected JSON { attempt, category, spot }"); }
    if (body == null) return Problem(400, "expected JSON { attempt, category, spot }");
    return Answer(attempts.Start(runner, Str(body, "attempt"), Str(body, "category"), Str(body, "spot")));
}).RequireRateLimiting("attempt");

// { step, head }: the chain's head after a step, about once a minute.
api.MapPost("/attempts/{id}/checkpoints", async (HttpRequest req, string id) =>
{
    string runner = store.RunnerOf(Bearer(req));
    if (runner == null) return Problem(401, "unknown token");
    JsonObject body;
    try { body = await req.ReadFromJsonAsync<JsonObject>(); }
    catch { return Problem(400, "expected JSON { step, head }"); }
    int step = body?["step"] is JsonValue v && v.TryGetValue(out int s) ? s : 0;
    return Answer(attempts.Checkpoint(runner, id, step, body == null ? null : Str(body, "head")));
}).RequireRateLimiting("attempt");

// The attempt's log (src/Data/AttemptChain) when it ends -> { verdict, why }.
api.MapPost("/attempts/{id}/log", async (HttpRequest req, string id) =>
{
    string runner = store.RunnerOf(Bearer(req));
    if (runner == null) return Problem(401, "unknown token");
    return Answer(attempts.Log(runner, id, await Body(req)));
}).RequireRateLimiting("attempt");

api.MapGet("/attempts/{id}", (string id) =>
    attempts.View(id) is { } a ? Results.Json(a) : Problem(404, "no such attempt")).RequireRateLimiting("read");

api.MapGet("/attempts/{id}/log", (string id) =>
    ForestOverlay.Data.AttemptChain.IsAttemptId(id) && attempts.LogText(id) is { } t
        ? Results.Text(t, "text/plain; charset=utf-8") : Problem(404, "no log for this attempt")).RequireRateLimiting("read");

// Where a code from the video shows in the log.
api.MapGet("/attempts/{id}/code/{code}", (string id, string code) =>
    attempts.FindCode(id, code) is { } f ? Results.Json(f) : Problem(400, "a code is four characters")).RequireRateLimiting("read");

// --- run categories (Categories; docs/run-mode.md phase 4) ----------------------

// The plugin's list: the published categories as text (src/Data/RunCategory).
// The game asks every 2 minutes with the last ETag: unchanged = 304, no body.
api.MapGet("/categories.txt", (HttpContext c) =>
{
    string text = categories.PublishedText();
    string etag = Categories.ETag(text);
    c.Response.Headers.ETag = etag;
    c.Response.Headers.CacheControl = "no-cache";
    if (Categories.Unchanged(c.Request.Headers.IfNoneMatch.ToString(), etag)) return Results.StatusCode(304);
    return Results.Text(text, "text/plain; charset=utf-8");
}).RequireRateLimiting("read");
api.MapGet("/categories/{id}/{version:int}", (string id, int version) =>
    Categories.View(categories.Version(id, version)) is { } v ? Results.Json(v) : Problem(404, "no such category version")).RequireRateLimiting("read");

static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out string s) ? s : "";

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
        store.LogAdmin(who, http.Request.Method + " " + http.Request.Path.Value + Uri.UnescapeDataString(http.Request.QueryString.Value ?? ""),
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
admin.MapPost("/world", (Delegate)((HttpContext c) => Upload(c, worldDir, UploadPath.IsWorld, true)));
// gzip: each .bin / .json also written Brotli'd and gzipped beside it (Precompressed).
async Task<IResult> Upload(HttpContext c, string dir, Func<string, bool> allowed, bool gzip = false)
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
            using (var from = entry.Open())
            using (var file = File.Create(to))
                await from.CopyToAsync(file);
            if (gzip && Precompressed.Compressible(to)) Precompressed.Write(to);
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
// Attempts and their logs are never edited; deleting one is the owner's
// alone (docs/run-mode.md: reports public, never editable).
admin.MapDelete("/attempts/{id}", (HttpContext c, string id) =>
    !IsOwner(c) ? Problem(403, "only the owner deletes attempts")
    : attempts.Delete(id) ? Results.Ok() : Problem(404, "no such attempt"));

// Mods the moderators allow (phase 3): every mod / patcher / code / patch
// owner a report named, and the allowed ones. ?kind=mod&text=<exact entry>.
admin.MapGet("/allowed", () => Results.Json(attempts.AllowList()));
admin.MapPost("/allowed", (HttpContext c, string kind, string text) =>
    attempts.Allow(kind, text, c.Items["admin"] as string) ? Results.Ok() : Problem(400, "kind is mod, patcher, code or patches; text the exact entry"));
admin.MapDelete("/allowed", (string kind, string text) =>
    attempts.Disallow(kind, text) ? Results.Ok() : Problem(404, "not on the list"));
// Categories: any admin (author, 2026-10-02: one role at this size).
admin.MapGet("/categories", () => Results.Json(new { list = categories.AdminList(), lastSync = categories.LastSync(),
    features = RunCategory.Features.Select(f => new { key = f.Key, label = f.Label, toggle = f.Toggle, def = f.Default }) }));
admin.MapPut("/categories/{id}", async (HttpContext c, string id) =>
{
    var (saved, error) = categories.Save(id, await Body(c.Request), c.Items["admin"] as string ?? "?");
    return error != null ? Problem(400, error) : Results.Json(new { id = saved.Id, version = saved.Version });
});
admin.MapPost("/categories/{id}/accept", (HttpContext c, string id) =>
    categories.AcceptSource(id, c.Items["admin"] as string ?? "?") is { } s ? Results.Json(new { id = s.Id, version = s.Version }) : Problem(404, "no change from speedrun.com waiting"));
admin.MapPost("/categories/{id}/dismiss", (string id) =>
    categories.DismissSource(id) ? Results.Ok() : Problem(404, "no change from speedrun.com waiting"));
admin.MapPost("/categories/sync", async () =>
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        return Results.Json(new { done = await categories.Sync(http) });
    }
    catch (Exception ex) { return Problem(502, "speedrun.com did not answer: " + ex.Message); }
});
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
// A spot's page carries its name and best time as OpenGraph tags, so a link
// pasted in Discord shows a preview (author, QA 2026-09-27). The script
// still builds the page itself.
IResult SpotPage(HttpContext c, string rest)
{
    c.Response.Headers.CacheControl = "no-cache";
    string id = (rest ?? "").Split('/')[0];
    JsonObject s = id.Length > 0 && id.Length <= 80 ? runs.Spot(id) : null;
    if (s == null) return Results.Content(indexHtml, "text/html; charset=utf-8");
    var (title, description) = Pages.SpotSummary(s);
    string url = siteUrl + "/spot/" + Uri.EscapeDataString(id);
    return Results.Content(Pages.WithMeta(indexHtml, title, description, url), "text/html; charset=utf-8");
}
// An attempt's page (run mode, phase 3): its verdict in the link preview.
IResult AttemptPage(HttpContext c, string id)
{
    c.Response.Headers.CacheControl = "no-cache";
    var a = ForestOverlay.Data.AttemptChain.IsAttemptId(id) ? attempts.View(id) : null;
    if (a == null) return Results.Content(indexHtml, "text/html; charset=utf-8");
    var j = System.Text.Json.JsonSerializer.SerializeToNode(a)!.AsObject();
    var (title, description) = Pages.AttemptSummary(j);
    string url = siteUrl + "/attempt/" + id;
    return Results.Content(Pages.WithMeta(indexHtml, title, description, url), "text/html; charset=utf-8");
}
app.MapGet("/", Page);
app.MapGet("/spot/{**rest}", SpotPage).RequireRateLimiting("read");
app.MapGet("/admin/{**rest}", Page);
app.MapGet("/attempt/{id}", AttemptPage).RequireRateLimiting("read");
app.MapGet("/about", Page);
// Two YouTube runs side by side (compare.js): the whole comparison is in
// the query, nothing stored - the link preview says what the page is.
app.MapGet("/compare", (HttpContext c) =>
{
    c.Response.Headers.CacheControl = "no-cache";
    string url = siteUrl + "/compare";
    return Results.Content(Pages.WithMeta(indexHtml, "Compare runs · Forest Practice Runs",
        "Two YouTube runs side by side, frame-timed: each run's start, end and splits, played together.", url), "text/html; charset=utf-8");
});
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
    /// A spot's title and one-line summary for link previews.
    public static (string title, string description) SpotSummary(JsonObject s)
    {
        string name = (string)s["name"] ?? "A spot";
        string by = (string)s["by"] ?? "";
        string category = (string)s["category"] ?? "";
        JsonNode route = s["routes"] is JsonArray r && r.Count > 0 ? r[0] : null;
        int runs = route?["runs"]?.GetValue<int>() ?? 0;
        JsonArray board = route?["board"] as JsonArray;
        var sb = new StringBuilder();
        // The plugin's default category says nothing about the spot.
        bool plain = category.Length == 0 || category.EndsWith("spots", StringComparison.OrdinalIgnoreCase) || category == "Segments";
        sb.Append(plain ? "A spot" : category + " spot");
        if (by.Length > 0) sb.Append(" by ").Append(by);
        sb.Append(" in The Forest. ");
        if (runs > 0 && board != null && board.Count > 0)
        {
            JsonNode best = board[0];
            sb.Append(runs).Append(runs == 1 ? " run" : " runs").Append(" by ").Append(board.Count).Append(board.Count == 1 ? " runner" : " runners");
            double d = best["duration"]?.GetValue<double>() ?? 0;
            sb.Append(", best ").Append(Time(d)).Append(" by ").Append((string)best["name"] ?? "a runner").Append(". ");
        }
        else sb.Append("No runs yet. ");
        sb.Append("Lines, ghosts and splits from ForestOverlay.");
        return (name + " - Forest Practice Runs", sb.ToString());
    }

    /// An attempt's title and verdict for link previews.
    public static (string title, string description) AttemptSummary(JsonObject a)
    {
        string category = (string)a["rules"]?["name"] ?? (string)a["category"] ?? "";
        string name = (string)a["runnerName"] ?? "";
        string verdict = (string)a["verdict"] ?? "";
        string title = (category.Length > 0 ? category + " attempt" : "Run attempt") + (name.Length > 0 ? " by " + name : "");
        string said = verdict switch
        {
            "green" => "Checked: the recording matches what the site saw during the run, and nothing else ran.",
            "amber" => "Partly checked: some of it can be checked by the video's codes only.",
            "red" => "Problems found: see the report.",
            _ => "Still running, or its log has not reached the site yet.",
        };
        if (a["finalTimerMs"] is JsonValue tv && tv.TryGetValue(out long timer) && timer > 0) said = Time(timer / 1000.0) + ". " + said;
        return (title + " - Forest Practice Runs", said + " ForestOverlay run mode.");
    }

    private static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.ToString(@"mm\:ss\.fff") : ((int)t.TotalMinutes) + ":" + t.ToString(@"ss\.fff");
    }

    /// The page with its title, description and OpenGraph / Twitter tags
    /// for this page (HTML-encoded).
    public static string WithMeta(string html, string title, string description, string url)
    {
        string t = System.Net.WebUtility.HtmlEncode(title), d = System.Net.WebUtility.HtmlEncode(description), u = System.Net.WebUtility.HtmlEncode(url);
        // Evaluators, not replacement strings: in a replacement string `$_`,
        // `$`` or `$'` in a runner's spot or name (HtmlEncode keeps `$`) would
        // paste the page's own HTML into the attribute (security audit,
        // 2026-10-04).
        html = System.Text.RegularExpressions.Regex.Replace(html, "<title>[^<]*</title>", _ => "<title>" + t + "</title>");
        html = System.Text.RegularExpressions.Regex.Replace(html, "<meta name=\"description\" content=\"[^\"]*\">", _ =>
            "<meta name=\"description\" content=\"" + d + "\">\n  " +
            "<meta property=\"og:type\" content=\"website\">\n  " +
            "<meta property=\"og:site_name\" content=\"Forest Practice Runs\">\n  " +
            "<meta property=\"og:title\" content=\"" + t + "\">\n  " +
            "<meta property=\"og:description\" content=\"" + d + "\">\n  " +
            "<meta property=\"og:url\" content=\"" + u + "\">\n  " +
            "<meta name=\"twitter:card\" content=\"summary\">");
        return html;
    }

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
