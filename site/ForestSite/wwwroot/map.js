// A top-down map of a spot: its zones, the chosen runs' lines and a ghost
// dot per run at the scrub time. World x runs east, z north (Unity metres),
// so z is drawn upwards. Drag to pan, wheel / pinch to zoom.
"use strict";

window.RunMap = (function () {
  const YELLOW = "#e5c501";

  function RunMap(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext("2d");
    this.zones = [];       // { kind, at, radius | size + yaw, label, role }
    this.runs = [];        // { color, path: [[t,x,y,z,speed]...] }
    this.time = 0;
    this.view = null;      // { cx, cz, scale } - pixels per metre
    this.pointers = new Map();
    this.bind();
    new ResizeObserver(() => this.draw()).observe(canvas);
  }

  RunMap.prototype.setZones = function (zones) { this.zones = zones; };
  RunMap.prototype.setRuns = function (runs, refit) { this.runs = runs; if (refit || !this.view) this.fit(); this.draw(); };
  RunMap.prototype.setTime = function (t) { this.time = t; this.draw(); };

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
    return [w / 2 + (x - v.cx) * v.scale, h / 2 - (z - v.cz) * v.scale];
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

  RunMap.prototype.draw = function () {
    const c = this.canvas, ctx = this.ctx, dpr = window.devicePixelRatio || 1;
    const w = c.clientWidth, h = c.clientHeight;
    if (!w || !h) return;
    if (c.width !== Math.round(w * dpr) || c.height !== Math.round(h * dpr)) { c.width = Math.round(w * dpr); c.height = Math.round(h * dpr); }
    if (!this.view) this.fit();
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    this.grid(w, h);
    for (const zn of this.zones) this.zone(zn);

    for (const run of this.runs) {
      const p = run.path;
      if (p.length < 2) continue;
      // The whole line dim, the part already run bright.
      ctx.lineJoin = "round"; ctx.lineCap = "round";
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
  RunMap.prototype.grid = function (w, h) {
    const ctx = this.ctx, v = this.view;
    const steps = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];
    let step = steps[steps.length - 1];
    for (const s of steps) if (s * v.scale >= 70) { step = s; break; }
    const x0 = v.cx - w / 2 / v.scale, x1 = v.cx + w / 2 / v.scale;
    const z0 = v.cz - h / 2 / v.scale, z1 = v.cz + h / 2 / v.scale;
    ctx.lineWidth = 1; ctx.strokeStyle = "#161616"; ctx.fillStyle = "#3a3a3a";
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
    // North arrow.
    ctx.fillStyle = "#555"; ctx.fillText("N ↑", w - 30, h - 8);
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

  RunMap.prototype.zoomAt = function (px, py, k) {
    const v = this.view, w = this.canvas.clientWidth, h = this.canvas.clientHeight;
    const wx = v.cx + (px - w / 2) / v.scale, wz = v.cz - (py - h / 2) / v.scale;
    v.scale = Math.min(200, Math.max(0.01, v.scale * k));
    v.cx = wx - (px - w / 2) / v.scale; v.cz = wz + (py - h / 2) / v.scale;
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
      this.view.cx -= (e.clientX - prev[0]) / this.view.scale;
      this.view.cz += (e.clientY - prev[1]) / this.view.scale;
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
  return RunMap;
})();
