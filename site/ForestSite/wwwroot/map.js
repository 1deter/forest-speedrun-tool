// A top-down map of a spot: its zones, the chosen runs' lines and a ghost
// dot per run at the scrub time. World x runs east, z north (Unity metres);
// drawn turned 180 degrees - south (the snow) at the top, west on the right,
// as the runners' maps are (author, 2026-10-01; UP). Drag to pan, wheel /
// pinch to zoom. Under it all,
// the game's terrain (scripts/terrain-bake.py -> /terrain/): a shaded relief
// image and a height grid, shared by every map on the page; over the relief,
// when uploaded, the aerial photo tiles (scripts/aerial-bake.py -> /aerial/).
"use strict";

window.RunMap = (function () {
  const YELLOW = "#e5c501";
  const SEA = "#0a1822";
  // -1: south up (x grows to the left, z downwards); 1 would be north up.
  // Everything on screen goes through toScreen / fromScreen and blit.
  const UP = -1;

  // The 12 places the plane can crash (PlaneCrashLocations.finalPositions,
  // HullRef, read live 2026-09-27): x, z, yaw. A save uses one of them.
  const PLANES = [
    [-577.19, 1437.1, 333.3], [-117.11, 1496.41, 317.9], [-928.96, 998.02, 31.4], [-211.94, 563.2, 281.3],
    [893.66, 349.33, 290.6], [239.32, 849.33, 20.3], [443.3, 645.09, 240.9], [816.57, 621.1, 121.5],
    [-246.74, 417.52, 174.8], [447.31, 246.9, 97.0], [355.7, 1055.42, 10.6], [-437.69, 1416.01, 296.1],
  ];

  // --- terrain, loaded once per page ---------------------------------------
  const terrain = { meta: null, image: null, heights: null, maps: new Set() };
  const terrainReady = fetch("/terrain/terrain.json").then(r => r.ok ? r.json() : null).then(meta => {
    if (!meta) return;
    terrain.meta = meta;
    const img = new Image();
    img.onload = () => { terrain.image = img; terrain.maps.forEach(m => m.draw()); };
    img.src = "/terrain/" + meta.image;
    return fetch("/terrain/" + meta.heights).then(r => r.ok ? r.arrayBuffer() : null).then(buf => {
      if (buf) { terrain.heights = new Uint16Array(buf); terrain.maps.forEach(m => m.draw()); }
    });
  }).catch(() => { /* no terrain: the plain grid, as before */ });

  // --- aerial photo tiles, loaded as seen ---------------------------------------
  // scripts/aerial-bake.py -> /aerial/: <layer>/<L>/<tx>_<ty>.jpg, 256 px. Level
  // L has 2^L x 2^L tiles over the terrain's square, the north-west corner at
  // (x0, z0 + sizeX); tx east, ty south. Open sea has no tiles (404): the
  // relief shows there. No aerial.json = no photo layer, the map as before.
  const aerial = { meta: null, cache: new Map(), missing: new Set(), redraw: 0 };
  const CACHE_MAX = 500;
  const aerialReady = fetch("/aerial/aerial.json", { cache: "no-cache" }).then(r => r.status === 200 ? r.json() : null).then(meta => {
    if (!meta || !(meta.levels >= 0) || !Array.isArray(meta.layers) || !meta.layers.length) return null;
    aerial.meta = { levels: meta.levels | 0, tile: meta.tile || 256, layers: meta.layers, v: meta.build ? "?v=" + meta.build : "" };
    terrain.maps.forEach(m => m.draw());
    return aerial.meta;
  }).catch(() => null);

  /// The pyramid level whose tile pixels are at least the screen's: a level-L
  /// tile is sizeX / 2^L metres and `tile` px wide.
  function aerialLevel(sizeX, scale, dpr, tile, levels) {
    const L = Math.ceil(Math.log2(sizeX * scale * dpr / tile));
    return L > levels ? levels : L > 0 ? L : 0;
  }

  /// A number per tile (no string per lookup): layer, level, tx, ty.
  function tileKey(li, L, tx, ty) { return ((li * 32 + L) * 65536 + tx) * 65536 + ty; }

  function redrawSoon() {
    if (aerial.redraw) return;
    aerial.redraw = requestAnimationFrame(() => { aerial.redraw = 0; terrain.maps.forEach(m => m.draw()); });
  }

  /// A tile's entry once loaded (refreshed as the newest in the cache),
  /// null while loading or missing. Starts the load the first time asked.
  function tileImage(layer, li, L, tx, ty, request) {
    const key = tileKey(li, L, tx, ty);
    const e = aerial.cache.get(key);
    if (e) {
      aerial.cache.delete(key); aerial.cache.set(key, e);
      return e.ok ? e.img : null;
    }
    if (!request || aerial.missing.has(key)) return null;
    // A missing ancestor means a missing tile (the bake writes every parent).
    for (let k = 1; k <= L; k++) if (aerial.missing.has(tileKey(li, L - k, tx >> k, ty >> k))) { aerial.missing.add(key); return null; }
    const img = new Image(), entry = { img, ok: false };
    img.onload = () => { entry.ok = true; redrawSoon(); };
    img.onerror = () => { aerial.cache.delete(key); aerial.missing.add(key); };
    img.src = "/aerial/" + layer + "/" + L + "/" + tx + "_" + ty + ".jpg" + aerial.meta.v;   // ?v=: a new bake is never an old cached tile
    aerial.cache.set(key, entry);
    if (aerial.cache.size > CACHE_MAX) aerial.cache.delete(aerial.cache.keys().next().value);
    return null;
  }

  /// The ground's height (world y) at x, z, or null outside the terrain.
  function groundAt(x, z) {
    const m = terrain.meta, h = terrain.heights;
    if (!m || !h) return null;
    const n = m.grid - 1, u = (x - m.x0) / m.sizeX * n, v = (z - m.z0) / m.sizeZ * n;
    if (!(u >= 0 && v >= 0 && u <= n && v <= n)) return null;
    const i = Math.min(n - 1, Math.floor(u)), j = Math.min(n - 1, Math.floor(v)), fu = u - i, fv = v - j;
    const g = (a, b) => h[b * m.grid + a];
    const top = g(i, j) * (1 - fu) + g(i + 1, j) * fu, bottom = g(i, j + 1) * (1 - fu) + g(i + 1, j + 1) * fu;
    return m.y0 + (top * (1 - fv) + bottom * fv) / 65535 * m.sizeY;
  }

  function RunMap(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext("2d");
    this.zones = [];       // { kind, at, radius | size + yaw, label, role }
    this.runs = [];        // { color, path: [[t,x,y,z,speed]...] }
    this.time = 0;
    this.view = null;      // { cx, cz, scale } - pixels per metre
    this.pointers = new Map();
    this.showTerrain = true;
    this.layer = "canopy"; // an aerial layer, or "relief"; see setLayer
    terrain.maps.add(this);
    this.bind();
    new ResizeObserver(() => this.draw()).observe(canvas);
  }

  RunMap.prototype.setZones = function (zones) { this.zones = zones; };
  RunMap.prototype.setRuns = function (runs, refit) { this.runs = runs; if (refit || !this.view) this.fit(); this.draw(); };
  RunMap.prototype.setTime = function (t) { this.time = t; this.draw(); };
  /// "canopy" / "ground" (the aerial photo, with or without trees; "-dry":
  /// the sea hidden) or "relief".
  RunMap.prototype.setLayer = function (layer) { this.layer = layer; this.draw(); };

  /// The aerial layer to draw now, or null (none uploaded, or relief chosen).
  RunMap.prototype.photoLayer = function () {
    const a = aerial.meta;
    if (!a || !terrain.meta || !this.showTerrain || this.layer === "relief") return null;
    return a.layers.includes(this.layer) ? this.layer : a.layers[0];
  };

  /// Every zone and line in view, with a margin.
  RunMap.prototype.fit = function () {
    let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity;
    const add = (x, z, r) => { x0 = Math.min(x0, x - r); x1 = Math.max(x1, x + r); z0 = Math.min(z0, z - r); z1 = Math.max(z1, z + r); };
    for (const zn of this.zones) add(zn.at[0], zn.at[2], zn.radius || Math.hypot(zn.size[0], zn.size[2]));
    for (const run of this.runs) for (const s of run.path) add(s[1], s[3], 0);
    if (!isFinite(x0)) { this.view = { cx: 0, cz: 0, scale: 0.2 }; return; }
    const w = this.canvas.clientWidth || 800, h = this.canvas.clientHeight || 500;
    const span = Math.max((x1 - x0) / (w - 60), (z1 - z0) / (h - 60), 0.02);
    this.view = { cx: (x0 + x1) / 2, cz: (z0 + z1) / 2, scale: 1 / span };
  };

  RunMap.prototype.toScreen = function (x, z) {
    const v = this.view, w = this.canvas.clientWidth, h = this.canvas.clientHeight;
    return [w / 2 + UP * (x - v.cx) * v.scale, h / 2 - UP * (z - v.cz) * v.scale];
  };

  /// The world point (x, z) under a screen point.
  RunMap.prototype.fromScreen = function (px, py) {
    const v = this.view, w = this.canvas.clientWidth, h = this.canvas.clientHeight;
    return [v.cx + (px - w / 2) / (UP * v.scale), v.cz - (py - h / 2) / (UP * v.scale)];
  };

  /// Draws an image (or its source rectangle s: [sx, sy, sw, sh]) over the
  /// world rectangle x0..x1, z0..z1 - the image's top edge north - turned
  /// with the map. sr: the screen corners [x, y of (x0, z1); x, y of (x1,
  /// z0)] when the caller rounded them.
  RunMap.prototype.blit = function (img, s, x0, z0, x1, z1, sr) {
    const ctx = this.ctx;
    let [ax, ay] = this.toScreen(x0, z1), [bx, by] = this.toScreen(x1, z0);
    if (sr) [ax, ay, bx, by] = sr;
    const src = s || [0, 0, img.naturalWidth || img.width, img.naturalHeight || img.height];
    if (UP === 1) { ctx.drawImage(img, src[0], src[1], src[2], src[3], ax, ay, bx - ax, by - ay); return; }
    // Turned: drawn from that corner with both axes flipped.
    ctx.save();
    ctx.translate(ax, ay); ctx.scale(-1, -1);
    ctx.drawImage(img, src[0], src[1], src[2], src[3], 0, 0, ax - bx, ay - by);
    ctx.restore();
  };

  /// A run's position at time t, interpolated between samples.
  function at(path, t) {
    if (!path.length) return null;
    if (t <= path[0][0]) return path[0];
    const last = path[path.length - 1];
    if (t >= last[0]) return last;
    let lo = 0, hi = path.length - 1;
    while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (path[mid][0] <= t) lo = mid; else hi = mid; }
    const a = path[lo], b = path[hi], k = (t - a[0]) / Math.max(1e-6, b[0] - a[0]);
    return [t, a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k, a[3] + (b[3] - a[3]) * k, a[4] + (b[4] - a[4]) * k];
  }

  /// Asks for a draw on the next frame: wheel and pointer events come several
  /// times a frame, and each drew the whole map (author, 2026-10-02: zoomed
  /// in to the max, zooming was choppy and a drag lagged behind).
  RunMap.prototype.draw = function () {
    if (this.frame) return;
    this.frame = requestAnimationFrame(() => { this.frame = 0; this.drawNow(); });
  };

  RunMap.prototype.drawNow = function () {
    const c = this.canvas, ctx = this.ctx, dpr = window.devicePixelRatio || 1;
    const w = c.clientWidth, h = c.clientHeight;
    if (!w || !h) return;
    if (c.width !== Math.round(w * dpr) || c.height !== Math.round(h * dpr)) { c.width = Math.round(w * dpr); c.height = Math.round(h * dpr); }
    if (!this.view) this.fit();
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    const photo = this.photoLayer();
    const relief = !!(photo || (this.showTerrain && terrain.image));
    if (relief) this.relief(w, h, photo, dpr);
    this.grid(w, h, relief);
    if (relief) this.planes();
    for (const zn of this.zones) this.zone(zn);

    for (const run of this.runs) {
      const p = run.path;
      if (p.length < 2) continue;
      // The whole line dim, the part already run bright.
      ctx.lineJoin = "round"; ctx.lineCap = "round";
      if (relief) {
        // A dark edge keeps a line readable on light ground.
        ctx.globalAlpha = 0.45; ctx.strokeStyle = "#000"; ctx.lineWidth = 4;
        this.line(p, this.time);
      }
      ctx.globalAlpha = 0.28; ctx.strokeStyle = run.color; ctx.lineWidth = 1.5;
      this.line(p, Infinity);
      ctx.globalAlpha = 1; ctx.lineWidth = 2.2;
      this.line(p, this.time);
    }
    for (const run of this.runs) {
      const s = at(run.path, this.time);
      if (!s) continue;
      const [x, y] = this.toScreen(s[1], s[3]);
      ctx.beginPath(); ctx.arc(x, y, 5, 0, Math.PI * 2);
      ctx.fillStyle = run.color; ctx.fill();
      ctx.lineWidth = 1.5; ctx.strokeStyle = "#000"; ctx.stroke();
    }
    ctx.globalAlpha = 1;
  };

  /// The ground: sea, the relief image at world coordinates, then the aerial
  /// photo tiles of `photo` (a layer name, or null). Faded while every ghost
  /// on the map is underground (a cave or the endgame, which neither shows),
  /// so a cave line does not read as a surface one.
  RunMap.prototype.relief = function (w, h, photo, dpr) {
    const ctx = this.ctx, m = terrain.meta;
    ctx.fillStyle = SEA; ctx.fillRect(0, 0, w, h);
    this.underground = this.runs.length > 0 && this.runs.every(run => {
      const s = at(run.path, this.time), g = s && groundAt(s[1], s[3]);
      return s && g !== null && s[2] < g - 3;
    });
    ctx.imageSmoothingEnabled = true;
    const iw = terrain.image ? terrain.image.naturalWidth || terrain.image.width : 1;
    ctx.imageSmoothingQuality = this.view.scale * dpr * m.sizeX / iw > 1 ? "low" : "high";
    if (terrain.image) this.reliefPart(w, h);
    if (photo) this.tiles(photo, w, h, dpr);
    if (this.underground) {
      // The same as drawing the ground at 25% over the sea, for every layer.
      ctx.globalAlpha = 0.75; ctx.fillStyle = SEA; ctx.fillRect(0, 0, w, h); ctx.globalAlpha = 1;
      ctx.fillStyle = "#bbb"; ctx.font = "600 11px Montserrat, sans-serif";
      ctx.textAlign = "center"; ctx.fillText("UNDERGROUND", w / 2, 16); ctx.textAlign = "start";
    }
  };

  /// Only the relief image's part on screen (whole image pixels, a pixel of
  /// margin): zoomed in close, drawing all of it scaled to many times the
  /// screen made every redraw slow (author, 2026-10-02: the 2D map lags
  /// zoomed in close, responsive zoomed out).
  RunMap.prototype.reliefPart = function (w, h) {
    const v = this.view, m = terrain.meta, img = terrain.image;
    const iw = img.naturalWidth || img.width, ih = img.naturalHeight || img.height;
    const kx = iw / m.sizeX, kz = ih / m.sizeZ, north = m.z0 + m.sizeZ;
    const wx0 = v.cx - w / 2 / v.scale, wx1 = v.cx + w / 2 / v.scale;
    const wz0 = v.cz - h / 2 / v.scale, wz1 = v.cz + h / 2 / v.scale;
    const sx0 = Math.max(0, Math.floor((wx0 - m.x0) * kx) - 1), sx1 = Math.min(iw, Math.ceil((wx1 - m.x0) * kx) + 1);
    const sy0 = Math.max(0, Math.floor((north - wz1) * kz) - 1), sy1 = Math.min(ih, Math.ceil((north - wz0) * kz) + 1);
    if (sx1 <= sx0 || sy1 <= sy0) return;
    this.blit(img, [sx0, sy0, sx1 - sx0, sy1 - sy0],
      m.x0 + sx0 / kx, north - sy1 / kz, m.x0 + sx1 / kx, north - sy0 / kz);
  };

  /// The visible aerial tiles at the level whose pixels are at least the
  /// screen's. A tile still loading shows its nearest loaded ancestor's part
  /// instead, so a zoom never flashes empty; a missing one (sea) shows the
  /// relief under it.
  RunMap.prototype.tiles = function (layer, w, h, dpr) {
    const ctx = this.ctx, v = this.view, m = terrain.meta, a = aerial.meta;
    const li = a.layers.indexOf(layer), size = m.sizeX, north = m.z0 + size;
    const L = aerialLevel(size, v.scale, dpr, a.tile, a.levels), n = 1 << L, ts = size / n;
    ctx.imageSmoothingQuality = ts * v.scale * dpr > a.tile ? "low" : "high";
    // The view is symmetric about its centre: the same world box either way up.
    const wx0 = v.cx - w / 2 / v.scale, wx1 = v.cx + w / 2 / v.scale;
    const wz0 = v.cz - h / 2 / v.scale, wz1 = v.cz + h / 2 / v.scale;
    const tx0 = Math.max(0, Math.floor((wx0 - m.x0) / ts)), tx1 = Math.min(n - 1, Math.floor((wx1 - m.x0) / ts));
    const ty0 = Math.max(0, Math.floor((north - wz1) / ts)), ty1 = Math.min(n - 1, Math.floor((north - wz0) / ts));
    // Tile edges on whole device pixels, shared by neighbours: no seams.
    const px = x => Math.round((w / 2 + UP * (x - v.cx) * v.scale) * dpr) / dpr;
    const py = z => Math.round((h / 2 - UP * (z - v.cz) * v.scale) * dpr) / dpr;
    for (let ty = ty0; ty <= ty1; ty++) {
      const zt = north - ty * ts, zb = zt - ts;
      for (let tx = tx0; tx <= tx1; tx++) {
        const xl = m.x0 + tx * ts, xr = xl + ts;
        const sr = [px(xl), py(zt), px(xr), py(zb)];
        const img = tileImage(layer, li, L, tx, ty, true);
        if (img) { this.blit(img, null, xl, zb, xr, zt, sr); continue; }
        if (aerial.missing.has(tileKey(li, L, tx, ty))) continue;
        for (let k = 1; k <= L; k++) {
          const up = tileImage(layer, li, L - k, tx >> k, ty >> k, false);
          if (!up) continue;
          const part = a.tile / (1 << k);
          this.blit(up, [(tx - ((tx >> k) << k)) * part, (ty - ((ty >> k) << k)) * part, part, part], xl, zb, xr, zt, sr);
          break;
        }
      }
    }
  };

  /// The plane: where the shown runs' saves had it (plugin v0.24.163+, a
  /// `plane` per run) bright, the other crash sites faint; with no run that
  /// knows, every site as it may be. A small hull, nose along its yaw.
  RunMap.prototype.planes = function () {
    const ctx = this.ctx, dpr = window.devicePixelRatio || 1, k = Math.max(6, Math.min(16, 30 * this.view.scale));
    const known = this.runs.filter(r => r.plane).map(r => r.plane);
    const near = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1]) < 30;
    ctx.save();
    ctx.lineWidth = 1;
    // The recorded position is the save's own (it can sit a little off the
    // site's reference point); draw it, and the sites no run used.
    const draw = known.slice();
    for (const site of PLANES) if (!known.some(p => near(p, site))) draw.push(site);
    for (const pl of draw) {
      const [x, z, yaw] = pl;
      const bright = !known.length || known.includes(pl);
      ctx.fillStyle = bright ? "rgba(235,235,235,.85)" : "rgba(235,235,235,.22)";
      ctx.strokeStyle = bright ? "rgba(0,0,0,.7)" : "rgba(0,0,0,.2)";
      const [sx, sy] = this.toScreen(x, z);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.translate(sx, sy); ctx.rotate(yaw * Math.PI / 180 + (UP === 1 ? 0 : Math.PI));
      ctx.beginPath();
      ctx.moveTo(0, -k); ctx.lineTo(k * .18, -k * .2); ctx.lineTo(k * .8, k * .15); ctx.lineTo(k * .18, k * .1);
      ctx.lineTo(k * .12, k * .75); ctx.lineTo(k * .4, k); ctx.lineTo(-k * .4, k); ctx.lineTo(-k * .12, k * .75);
      ctx.lineTo(-k * .18, k * .1); ctx.lineTo(-k * .8, k * .15); ctx.lineTo(-k * .18, -k * .2); ctx.closePath();
      ctx.fill(); ctx.stroke();
    }
    ctx.restore();
  };

  RunMap.prototype.line = function (p, until) {
    const ctx = this.ctx;
    ctx.beginPath();
    let [x, y] = this.toScreen(p[0][1], p[0][3]);
    ctx.moveTo(x, y);
    for (let i = 1; i < p.length; i++) {
      if (p[i][0] > until) { const s = at(p, until); [x, y] = this.toScreen(s[1], s[3]); ctx.lineTo(x, y); break; }
      [x, y] = this.toScreen(p[i][1], p[i][3]);
      ctx.lineTo(x, y);
    }
    ctx.stroke();
  };

  /// Metre grid: a step that keeps lines ~60-150 px apart, labelled.
  RunMap.prototype.grid = function (w, h, relief) {
    const ctx = this.ctx, v = this.view;
    const steps = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];
    let step = steps[steps.length - 1];
    for (const s of steps) if (s * v.scale >= 70) { step = s; break; }
    const x0 = v.cx - w / 2 / v.scale, x1 = v.cx + w / 2 / v.scale;
    const z0 = v.cz - h / 2 / v.scale, z1 = v.cz + h / 2 / v.scale;
    ctx.lineWidth = 1; ctx.strokeStyle = relief ? "rgba(0,0,0,.22)" : "#161616"; ctx.fillStyle = relief ? "rgba(255,255,255,.7)" : "#3a3a3a";
    ctx.font = "500 10px Montserrat, sans-serif";
    ctx.beginPath();
    for (let x = Math.ceil(x0 / step) * step; x <= x1; x += step) {
      const [sx] = this.toScreen(x, 0); ctx.moveTo(Math.round(sx) + .5, 0); ctx.lineTo(Math.round(sx) + .5, h);
      ctx.fillText(Math.round(x), sx + 3, h - 5);
    }
    for (let z = Math.ceil(z0 / step) * step; z <= z1; z += step) {
      const [, sy] = this.toScreen(0, z); ctx.moveTo(0, Math.round(sy) + .5); ctx.lineTo(w, Math.round(sy) + .5);
      ctx.fillText(Math.round(z), 4, sy - 3);
    }
    ctx.stroke();
    // Which way north is.
    ctx.fillStyle = relief ? "#ddd" : "#555"; ctx.fillText(UP === 1 ? "N ↑" : "N ↓", w - 30, h - 8);
  };

  RunMap.prototype.zone = function (zn) {
    const ctx = this.ctx, v = this.view;
    const [x, y] = this.toScreen(zn.at[0], zn.at[2]);
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = zn.role === "check" ? "#8a7a1c" : YELLOW;
    ctx.fillStyle = zn.role === "check" ? "rgba(138,122,28,.10)" : "rgba(229,197,1,.10)";
    ctx.setLineDash(zn.role === "start" ? [] : zn.role === "end" ? [6, 3] : [2, 3]);
    ctx.beginPath();
    if (zn.kind === "box") {
      // Unity yaw: clockwise from north. Local (x, z) -> world.
      const a = (zn.yaw || 0) * Math.PI / 180, cs = Math.cos(a), sn = Math.sin(a);
      const hx = zn.size[0], hz = zn.size[2];
      const corners = [[-hx, -hz], [hx, -hz], [hx, hz], [-hx, hz]];
      corners.forEach(([lx, lz], i) => {
        const [px, py] = this.toScreen(zn.at[0] + lx * cs + lz * sn, zn.at[2] - lx * sn + lz * cs);
        if (i === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
      });
      ctx.closePath();
    } else {
      ctx.arc(x, y, Math.max(2, zn.radius * v.scale), 0, Math.PI * 2);
    }
    ctx.fill(); ctx.stroke(); ctx.setLineDash([]);
    ctx.fillStyle = zn.role === "check" ? "#8a7a1c" : YELLOW;
    ctx.font = "600 10px Montserrat, sans-serif";
    ctx.fillText(zn.label, x + 6, y - 6);
  };

  // --- pan and zoom -----------------------------------------------------------

  /// Zoom by k, the world point under (px, py) staying there.
  RunMap.prototype.zoomAt = function (px, py, k) {
    const v = this.view, w = this.canvas.clientWidth, h = this.canvas.clientHeight;
    const [wx, wz] = this.fromScreen(px, py);
    const most = terrain.meta ? Math.min(w, h) / (1.5 * Math.max(terrain.meta.sizeX, terrain.meta.sizeZ)) : 0.01;
    v.scale = Math.min(200, Math.max(Math.min(most, v.scale), v.scale * k));
    v.cx = wx - (px - w / 2) / (UP * v.scale); v.cz = wz + (py - h / 2) / (UP * v.scale);
    this.draw();
  };

  RunMap.prototype.bind = function () {
    const c = this.canvas;
    c.addEventListener("wheel", e => {
      e.preventDefault();
      const r = c.getBoundingClientRect();
      this.zoomAt(e.clientX - r.left, e.clientY - r.top, Math.exp(-e.deltaY * 0.0015));
    }, { passive: false });
    c.addEventListener("dblclick", () => { this.fit(); this.draw(); });
    let down = null;
    c.addEventListener("pointerdown", e => {
      c.setPointerCapture(e.pointerId); this.pointers.set(e.pointerId, [e.clientX, e.clientY]); c.classList.add("drag");
      down = this.pointers.size === 1 ? [e.clientX, e.clientY] : null;
    });
    const end = e => {
      // A click (no drag): the nearest point of a line, if one is close.
      if (e.type === "pointerup" && down && Math.hypot(e.clientX - down[0], e.clientY - down[1]) < 5 && this.onPick) {
        const r = c.getBoundingClientRect(), hit = this.nearest(e.clientX - r.left, e.clientY - r.top, 14);
        if (hit) this.onPick(hit.run, hit.t);
      }
      down = null;
      this.pointers.delete(e.pointerId); if (!this.pointers.size) c.classList.remove("drag"); this.pinch = 0;
    };
    c.addEventListener("pointerup", end);
    c.addEventListener("pointercancel", end);
    c.addEventListener("pointermove", e => {
      if (!this.pointers.has(e.pointerId) || !this.view) return;
      const prev = this.pointers.get(e.pointerId);
      this.pointers.set(e.pointerId, [e.clientX, e.clientY]);
      if (this.pointers.size === 2) {
        const [a, b] = [...this.pointers.values()];
        const d = Math.hypot(a[0] - b[0], a[1] - b[1]);
        if (this.pinch) {
          const r = c.getBoundingClientRect();
          this.zoomAt((a[0] + b[0]) / 2 - r.left, (a[1] + b[1]) / 2 - r.top, d / this.pinch);
        }
        this.pinch = d;
        return;
      }
      // The ground follows the pointer, either way up.
      this.view.cx -= (e.clientX - prev[0]) / (UP * this.view.scale);
      this.view.cz += (e.clientY - prev[1]) / (UP * this.view.scale);
      this.draw();
    });
  };

  /// The run sample nearest a screen point, within maxPx: { run, t }.
  RunMap.prototype.nearest = function (px, py, maxPx) {
    let best = null, bestD = maxPx;
    for (const run of this.runs) for (const s of run.path) {
      const [x, y] = this.toScreen(s[1], s[3]), d = Math.hypot(x - px, y - py);
      if (d < bestD) { bestD = d; best = { run, t: s[0] }; }
    }
    return best;
  };

  RunMap.at = at;
  RunMap.groundAt = groundAt;
  RunMap.terrain = terrain;
  RunMap.terrainReady = terrainReady;
  RunMap.aerial = aerial;
  RunMap.aerialReady = aerialReady;   // resolves to aerial.json's content, or null
  RunMap.aerialLevel = aerialLevel;
  RunMap.tileKey = tileKey;           // with aerial.missing: tiles known absent (map3d.js)
  return RunMap;
})();
