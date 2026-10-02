// The spot page's 3D view (the map's "3D" button; loaded the first time it is
// opened): the game's terrain as a mesh, the shown runs as lines in their
// colours, a ghost per run at the scrub time and the spot's zones. Orbit
// (drag / right-drag / wheel, one / two fingers) or Follow: the camera rides
// behind the focused run's ghost - a replay freecam.
//
// World (Unity): x east, z north, y up. three.js is right-handed, so a world
// point is drawn at (x, y, -z): north is -Z, and nothing is mirrored.
// The terrain is map.js's (RunMap.terrain: heights.u16 + map.jpg); a coarse
// mesh of the whole island with a hole where a full-resolution patch around
// the runs sits. Textured with the aerial photo tiles when uploaded.
// The game's models and collision stream in around the camera (world3d.js).
// Served by the site (wwwroot/vendor, the npm package's file, checked
// against its integrity hash): the CSP allows the site's own scripts only.
import * as THREE from "/vendor/three-0.170.0.module.min.js?v=0.170.0";
// world3d.js is imported by its stamped URL (index.html's data-world3d-src,
// ?v=<hash>) in create(): a plain "./world3d.js" import has no version, and
// Cloudflare's 4 h browser cache could pair it, old, with a new map3d.js.
let World = null;

const RunMap = window.RunMap;
const terrain = RunMap.terrain, aerial = RunMap.aerial;
const MOBILE = matchMedia("(pointer: coarse)").matches;
// The island mesh: every 2nd height sample (~7 m) on desktop, every 4th on
// phones. Where the detail patch takes over, the shore moved by up to a
// coarse cell - the coast "moving up and down" (author, 2026-10-01).
const COARSE = MOBILE ? 4 : 2;
const DETAIL = MOBILE ? 320 : 512; // the runs' patch, in samples per side at full resolution
const UNDER = 3;                  // metres below the ground that count as underground (map.js)
// Follow: the camera looks at FOLLOW_HEAD metres over the ghost, keeps
// WALL_GAP in front of a wall, comes no closer than MIN_FOLLOW, and closer
// than FOLLOW_LOW aims lower.
const FOLLOW_HEAD = 1.2, WALL_GAP = 0.4, MIN_FOLLOW = 0.8, FOLLOW_LOW = 4;
const ORDER = { terrain: 0, sea: 1, zone: 2, line: 3, xray: 4, ghost: 5, label: 6 };

const P = (x, y, z) => new THREE.Vector3(x, y, -z);

// --- fat lines -----------------------------------------------------------------
// One quad per path segment, widened on screen (WebGL draws 1 px lines only).
// Each end carries its run time: the part already run bright with a dark edge,
// the rest dim and thinner - the cut is exact at the scrub time, per pixel.
// Pulled toward the eye (screen position unchanged) so a line on the ground
// is not buried in the mesh.

const LINE_VERT = `
uniform vec2 uRes; uniform float uWidth; uniform float uPull;
attribute vec3 instanceStart; attribute vec3 instanceEnd; attribute vec2 instanceT;
varying float vT; varying float vSide;
void trimSegment(const in vec4 s, inout vec4 e) {
  float a = projectionMatrix[2][2], b = projectionMatrix[3][2];
  float nearZ = -0.5 * b / a;
  e.xyz = mix(s.xyz, e.xyz, (nearZ - s.z) / (e.z - s.z));
}
vec4 pulled(vec3 p) {
  vec4 v = modelViewMatrix * vec4(p, 1.0);
  float d = length(v.xyz);
  v.xyz *= max(0.05, 1.0 - (uPull + 0.004 * d) / max(d, 1e-3));
  return v;
}
void main() {
  vec4 s = pulled(instanceStart), e = pulled(instanceEnd);
  if (s.z < 0.0 && e.z >= 0.0) trimSegment(s, e); else if (e.z < 0.0 && s.z >= 0.0) trimSegment(e, s);
  vec4 cs = projectionMatrix * s, ce = projectionMatrix * e;
  float aspect = uRes.x / uRes.y;
  vec2 dir = ce.xy / ce.w - cs.xy / cs.w;
  dir.x *= aspect;
  dir = length(dir) > 1e-6 ? normalize(dir) : vec2(1.0, 0.0);
  vec2 off = vec2(-dir.y, dir.x) * position.y + dir * (position.x < 0.5 ? -1.0 : 1.0);
  off.x /= aspect;
  vec4 c = position.x < 0.5 ? cs : ce;
  c.xy += off * uWidth / uRes.y * c.w;
  gl_Position = c;
  vT = mix(instanceT.x, instanceT.y, position.x);
  vSide = position.y;
}`;

const LINE_FRAG = `
uniform vec3 uColor; uniform float uTime; uniform float uDim; uniform float uAlpha;
varying float vT; varying float vSide;
void main() {
  bool done = vT <= uTime;
  float a = abs(vSide);
  if (!done && a > 0.4) discard;
  vec3 col = done && a > 0.6 ? uColor * 0.12 : uColor;
  gl_FragColor = vec4(col, (done ? 1.0 : uDim) * uAlpha);
  #include <colorspace_fragment>
}`;

function lineMesh(path, color, xray) {
  const n = path.length - 1;
  const data = new Float32Array(n * 8);
  for (let i = 0; i < n; i++) {
    const a = path[i], b = path[i + 1], o = i * 8;
    data[o] = a[1]; data[o + 1] = a[2]; data[o + 2] = -a[3];
    data[o + 3] = b[1]; data[o + 4] = b[2]; data[o + 5] = -b[3];
    data[o + 6] = a[0]; data[o + 7] = b[0];
  }
  const g = new THREE.InstancedBufferGeometry();
  g.setAttribute("position", new THREE.Float32BufferAttribute([0, -1, 0, 1, -1, 0, 1, 1, 0, 0, 1, 0], 3));
  g.setIndex([0, 1, 2, 0, 2, 3]);
  const buf = new THREE.InstancedInterleavedBuffer(data, 8, 1);
  g.setAttribute("instanceStart", new THREE.InterleavedBufferAttribute(buf, 3, 0));
  g.setAttribute("instanceEnd", new THREE.InterleavedBufferAttribute(buf, 3, 3));
  g.setAttribute("instanceT", new THREE.InterleavedBufferAttribute(buf, 2, 6));
  g.instanceCount = n;
  const m = new THREE.ShaderMaterial({
    vertexShader: LINE_VERT, fragmentShader: LINE_FRAG, transparent: true, depthWrite: false, depthTest: !xray,
    uniforms: {
      uRes: { value: new THREE.Vector2(1, 1) }, uWidth: { value: xray ? 3 : 5 }, uPull: { value: 1.5 },
      uColor: { value: new THREE.Color(color) }, uTime: { value: 0 }, uDim: { value: 0.3 }, uAlpha: { value: xray ? 0.22 : 1 },
    },
  });
  const mesh = new THREE.Mesh(g, m);
  mesh.userData.width = m.uniforms.uWidth.value;   // CSS pixels
  mesh.frustumCulled = false;
  mesh.renderOrder = xray ? ORDER.xray : ORDER.line;
  return mesh;
}

// --- sprites -----------------------------------------------------------------------

/// A shaded ball: the ghost, tinted by the sprite's colour.
let ballTexture = null;
function ball() {
  if (ballTexture) return ballTexture;
  const c = document.createElement("canvas"), s = 64, x = c.getContext("2d");
  c.width = c.height = s;
  const g = x.createRadialGradient(s * .38, s * .36, 2, s / 2, s / 2, s / 2 - 4);
  g.addColorStop(0, "#fff"); g.addColorStop(.55, "#d8d8d8"); g.addColorStop(1, "#7a7a7a");
  x.beginPath(); x.arc(s / 2, s / 2, s / 2 - 4, 0, Math.PI * 2);
  x.fillStyle = g; x.fill(); x.lineWidth = 5; x.strokeStyle = "#000"; x.stroke();
  ballTexture = new THREE.CanvasTexture(c);
  ballTexture.colorSpace = THREE.SRGBColorSpace;
  return ballTexture;
}

function label(text, color) {
  const c = document.createElement("canvas"), x = c.getContext("2d"), dpr = 2;
  const font = "600 " + 11 * dpr + "px Montserrat, sans-serif";
  x.font = font;
  const w = Math.ceil(x.measureText(text).width) + 10 * dpr, h = 18 * dpr;
  c.width = w; c.height = h;
  x.font = font; x.textBaseline = "middle";
  x.fillStyle = "rgba(0,0,0,.72)"; x.fillRect(0, 0, w, h);
  x.fillStyle = color; x.fillText(text, 5 * dpr, h / 2 + dpr);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: t, depthTest: false, transparent: true, sizeAttenuation: false }));
  s.center.set(0, 0);
  s.userData.px = [w / dpr, h / dpr];
  s.renderOrder = ORDER.label;
  return s;
}

// --- the view ------------------------------------------------------------------------

const DETAIL_KEY = "forest.map3d.detail";

class Map3D {
  constructor(canvas, hooks) {
    this.canvas = canvas;
    this.hooks = hooks || {};
    // Fastest by default (author, 2026-10-02): one pixel per CSS pixel, no
    // antialiasing. The Detail button (kept per browser) draws at the
    // screen's density; its antialiasing needs a new context, so a reload.
    try { this.sharp = localStorage.getItem(DETAIL_KEY) === "on"; } catch (e) { this.sharp = false; }
    const r = this.renderer = new THREE.WebGLRenderer({ canvas, antialias: this.sharp, powerPreference: "high-performance" });
    r.setPixelRatio(this.pixelRatio());
    r.setClearColor(0x050505);
    r.localClippingEnabled = true;     // the world's cutaway underground (world3d.js setCut)
    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(55, 1, 1, 30000);
    this.lookDir = new THREE.Vector3();
    this.followDir = new THREE.Vector3();
    this.followLook = new THREE.Vector3();
    this.scene.add(new THREE.AmbientLight(0xffffff, 0.6 * Math.PI));
    const sun = new THREE.DirectionalLight(0xffffff, 0.55 * Math.PI);
    sun.position.set(-0.6, 1, -0.5);   // north-west, as the relief's own light
    this.scene.add(sun);

    this.runs = []; this.zones = []; this.time = 0; this.focus = null; this.layer = "canopy";
    this.mode = "orbit";
    this.orbit = { target: new THREE.Vector3(0, 50, 0), yaw: 0, pitch: 0.75, dist: 3500 };
    // dist: the wanted distance; cur: the camera's, pulled in front of a wall
    // (followCamera()); hitKey / hitAt / hit: the last wall test.
    this.follow = { yawOff: 0, pitch: 0.22, dist: 12, heading: null, target: null, cur: null, hitKey: "", hitAt: 0, hit: Infinity };
    this.fade = 1; this.fadeTo = 1;
    this.lines = []; this.ghosts = []; this.labels = [];
    this.zoneGroup = new THREE.Group(); this.scene.add(this.zoneGroup);
    this.runGroup = new THREE.Group(); this.scene.add(this.runGroup);
    this.region = null;            // the detail patch, in samples: { i0, j0, i1, j1, step }
    this.islandGen = 0; this.detailGen = 0;   // texture generations: stale tile loads are dropped
    this.island = null;            // the island's photo: { layer, canvas } - the patch's first picture
    this.visible = false; this.dirty = true; this.last = 0;
    // For a session checking the render at a game spot (docs/website.md):
    // window.forest3d.lookFrom(x, y, z, yaw, pitch, dist) - Unity coordinates.
    window.forest3d = this;

    this.note = document.createElement("div");
    this.note.className = "map3dnote";
    this.note.hidden = true;
    this.note.textContent = "Underground";
    canvas.insertAdjacentElement("afterend", this.note);
    this.world = new World(this.scene, () => { this.dirty = true; }, canvas.parentElement);
    const detail = document.createElement("button");
    detail.type = "button"; detail.textContent = "Detail";
    detail.title = "Sharper picture at your screen's density, smoothed edges after a reload (slower). Off = fastest";
    const markDetail = () => { detail.classList.toggle("on", this.sharp); detail.setAttribute("aria-pressed", this.sharp); };
    markDetail();
    detail.onclick = () => {
      this.sharp = !this.sharp;
      markDetail();
      try { localStorage.setItem(DETAIL_KEY, this.sharp ? "on" : "off"); } catch (e) { /* not kept: fine */ }
      this.renderer.setPixelRatio(this.pixelRatio());
      this.dirty = true;
    };
    this.world.box.insertBefore(detail, this.world.status);

    this.bind();
    this.ro = new ResizeObserver(() => { this.dirty = true; });
    this.ro.observe(canvas);
    this.loop = this.loop.bind(this);
    RunMap.terrainReady.then(() => {
      if (this.disposed || !terrain.meta || !terrain.heights) return;
      this.buildTerrain();
      this.updateRegion(true);
    });
    RunMap.aerialReady.then(() => { if (!this.disposed && this.coarse) this.textures(); });
  }

  pixelRatio() { return this.sharp ? Math.min(window.devicePixelRatio || 1, 2) : 1; }

  // --- data from the page ---

  setZones(zones) {
    this.zones = zones;
    for (const o of [...this.zoneGroup.children]) { this.zoneGroup.remove(o); dispose(o); }
    for (const l of this.labels) { this.scene.remove(l); dispose(l); }
    this.labels = [];
    for (const zn of zones) {
      const col = zn.role === "check" ? 0x8a7a1c : 0xe5c501;
      const fill = new THREE.MeshBasicMaterial({ color: col, transparent: true, opacity: 0.16, depthWrite: false, side: THREE.DoubleSide });
      const edge = new THREE.LineBasicMaterial({ color: col, transparent: true, opacity: 0.9 });
      const at = P(zn.at[0], zn.at[1], zn.at[2]);
      let top;
      if (zn.kind === "box") {
        const g = new THREE.BoxGeometry(zn.size[0] * 2, zn.size[1] * 2, zn.size[2] * 2);
        const box = new THREE.Mesh(g, fill), edges = new THREE.LineSegments(new THREE.EdgesGeometry(g), edge);
        for (const o of [box, edges]) { o.position.copy(at); o.rotation.y = -(zn.yaw || 0) * Math.PI / 180; o.renderOrder = ORDER.zone; this.zoneGroup.add(o); }
        top = zn.size[1];
      } else {
        // A zone is a sphere in game (Data/Segments: distance <= radius).
        const s = new THREE.Mesh(new THREE.SphereGeometry(zn.radius, 32, 16), fill);
        const pts = [];
        for (let i = 0; i < 64; i++) { const a = i / 64 * Math.PI * 2; pts.push(new THREE.Vector3(Math.cos(a) * zn.radius, 0, Math.sin(a) * zn.radius)); }
        const ring = new THREE.LineLoop(new THREE.BufferGeometry().setFromPoints(pts), edge);
        for (const o of [s, ring]) { o.position.copy(at); o.renderOrder = ORDER.zone; this.zoneGroup.add(o); }
        top = zn.radius;
      }
      const l = label(zn.label, zn.role === "check" ? "#8a7a1c" : "#e5c501");
      l.position.copy(at).add(new THREE.Vector3(0, top + 0.5, 0));
      this.labels.push(l); this.scene.add(l);
    }
    this.dirty = true;
  }

  /// runs: [{ id, color, path: [[t, x, y, z, speed]...] }]
  setRuns(runs, refit) {
    this.runs = runs;
    for (const o of [...this.runGroup.children]) { this.runGroup.remove(o); dispose(o); }
    this.lines = []; this.ghosts = [];
    for (const run of runs) {
      if (run.path.length >= 2) {
        const line = lineMesh(run.path, run.color, false), xray = lineMesh(run.path, run.color, true);
        this.runGroup.add(line, xray);
        this.lines.push(line, xray);
      }
      const g = new THREE.Sprite(new THREE.SpriteMaterial({ map: ball(), color: new THREE.Color(run.color), depthTest: false, transparent: true, sizeAttenuation: false }));
      g.renderOrder = ORDER.ghost;
      g.userData.run = run;
      this.runGroup.add(g); this.ghosts.push(g);
    }
    if (refit || !this.fitted) this.fit();
    this.updateRegion(false);
    this.dirty = true;
  }

  /// The camera at a game position (Unity x, y, z) facing yaw degrees (0 =
  /// north, as the game's), pitch degrees down, orbiting a point dist ahead -
  /// the site's picture beside a game shot from the same place (gotcha 68).
  lookFrom(x, y, z, yaw = 0, pitch = 0, dist = 5) {
    const a = yaw * Math.PI / 180, p = pitch * Math.PI / 180, o = this.orbit;
    this.setMode("orbit");
    o.yaw = -a; o.pitch = p; o.dist = dist;
    o.target.set(x + Math.sin(a) * Math.cos(p) * dist, y - Math.sin(p) * dist, -(z + Math.cos(a) * Math.cos(p) * dist));
    this.dirty = true;
  }

  setTime(t) { this.time = t; this.dirty = true; }
  setFocus(id) { this.focus = id; this.dirty = true; }
  setLayer(layer) {
    if (layer === this.layer) return;
    this.layer = layer;
    this.applyWater();
    if (this.coarse) this.textures();
  }

  /// The Water button: a "-dry" layer hides the sea plane and the lakes'
  /// surfaces too - the ground under them, as the 2D map's dry photo (before,
  /// the button changed the terrain's photo only, under the sea: nothing to
  /// see; author, 2026-10-01).
  applyWater() {
    const on = !/-dry$/.test(this.layer);
    if (this.sea) this.sea.visible = on;
    this.world.setWater(on);
    this.dirty = true;
  }

  /// "orbit" or "follow" (behind the focused run's ghost).
  setMode(mode) {
    this.mode = mode;
    if (mode === "follow") { this.follow.yawOff = 0; this.follow.heading = null; this.follow.target = null; this.follow.cur = null; }
    else if (this.follow.target) {
      // Orbit from where the follow camera was: no jump.
      const f = this.follow, o = this.orbit;
      o.target.copy(f.target); o.yaw = (f.heading || 0) + f.yawOff; o.pitch = f.pitch; o.dist = Math.max(8, f.cur ?? f.dist);
    }
    this.dirty = true;
  }

  setVisible(on) {
    this.visible = on;
    this.note.hidden = !on || this.fadeTo === 1;
    this.world.box.classList.toggle("off", !on);
    if (on) { this.dirty = true; this.last = 0; this.raf = requestAnimationFrame(this.loop); }
    else cancelAnimationFrame(this.raf);
  }

  /// Every run and zone in view, from the north and above: south at the top
  /// of the screen, as the 2D map and the runners' maps (author, 2026-10-01).
  fit() {
    const box = new THREE.Box3();
    for (const run of this.runs) for (const s of run.path) box.expandByPoint(P(s[1], s[2], s[3]));
    for (const zn of this.zones) box.expandByPoint(P(zn.at[0], zn.at[1], zn.at[2]));
    const o = this.orbit;
    if (box.isEmpty()) { o.target.set(0, 50, 0); o.dist = 3500; }
    else {
      box.getCenter(o.target);
      // The box's radius inside the narrower of the two fields of view.
      const size = box.getSize(new THREE.Vector3()), c = this.canvas;
      const aspect = (c.clientWidth || 800) / (c.clientHeight || 500), tv = Math.tan(this.camera.fov * Math.PI / 360);
      o.dist = Math.max(40, size.length() / 2 / Math.min(tv, tv * aspect) * 0.9);
    }
    o.yaw = Math.PI; o.pitch = 0.75;
    this.fitted = true;
    this.follow.yawOff = 0;
    this.dirty = true;
  }

  // --- terrain ---

  buildTerrain() {
    const m = terrain.meta, h = terrain.heights, n = m.grid, cell = m.sizeX / (n - 1);
    this.cell = cell;
    const open = openSea(m, h);
    this.holes = terrainHoles(h, open);
    // The whole island, coarse.
    const cn = (n - 1) / COARSE + 1;
    const pos = new Float32Array(cn * cn * 3), uv = new Float32Array(cn * cn * 2);
    for (let j = 0; j < cn; j++) for (let i = 0; i < cn; i++) {
      const k = j * cn + i, si = i * COARSE, sj = j * COARSE;
      pos[k * 3] = m.x0 + si * cell;
      pos[k * 3 + 1] = m.y0 + h[sj * n + si] / 65535 * m.sizeY;
      pos[k * 3 + 2] = -(m.z0 + sj * m.sizeZ / (n - 1));
      uv[k * 2] = si / (n - 1); uv[k * 2 + 1] = sj / (n - 1);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.BufferAttribute(pos, 3));
    g.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
    this.coarseN = cn;
    this.coarseIndex(g, null);
    g.computeVertexNormals();
    this.coarse = new THREE.Mesh(g, terrainMaterial());
    this.coarse.renderOrder = ORDER.terrain;
    this.scene.add(this.coarse);

    // The sea, pushed back where it meets the shore (no flicker there), and
    // only over open water (seaMask) - a plane at sea level also filled every
    // pit below it inland: the sinkhole and the dips round it showed as dark
    // lakes over land (author, 2026-10-01).
    const seaMat = new THREE.MeshBasicMaterial({
      color: 0x0c2231, transparent: true, opacity: 0.9, polygonOffset: true, polygonOffsetFactor: 2, polygonOffsetUnits: 4,
    });
    seaMask(seaMat, m, open);
    const sea = new THREE.Mesh(new THREE.PlaneGeometry(40000, 40000), seaMat);
    sea.rotation.x = -Math.PI / 2;
    sea.position.y = m.sea;
    sea.renderOrder = ORDER.sea;
    this.sea = sea;
    this.scene.add(sea);
    this.world.setGround(m, h, this.holes);
    this.applyWater();
    this.textures();
  }

  /// The island's triangles, less the cells under the detail patch and the
  /// terrain's holes (terrainHoles).
  coarseIndex(g, r) {
    const cn = this.coarseN, idx = new Uint32Array((cn - 1) * (cn - 1) * 6);
    const n = terrain.meta.grid, holes = this.holes, hole = (i, j) => holes[j * COARSE * n + i * COARSE];
    let k = 0;
    for (let j = 0; j < cn - 1; j++) for (let i = 0; i < cn - 1; i++) {
      if (r && i * COARSE >= r.i0 && (i + 1) * COARSE <= r.i1 && j * COARSE >= r.j0 && (j + 1) * COARSE <= r.j1) continue;
      if (hole(i, j) || hole(i + 1, j) || hole(i, j + 1) || hole(i + 1, j + 1)) continue;
      const a = j * cn + i, b = a + 1, c = a + cn, d = c + 1;
      idx[k++] = a; idx[k++] = c; idx[k++] = b; idx[k++] = b; idx[k++] = c; idx[k++] = d;
    }
    if (g.index) g.dispose();   // frees the old buffers; they upload again on the next frame
    g.setIndex(new THREE.BufferAttribute(idx.subarray(0, k), 1));
  }

  /// The full-resolution patch: the runs' and zones' area plus a margin, at
  /// least 256 samples a side, at most DETAIL (or every 2nd / 4th sample when
  /// the area is bigger). Rebuilt only when the runs leave it.
  updateRegion(force) {
    const m = terrain.meta;
    if (!m || !terrain.heights || !this.coarse) return;
    const n = m.grid, cell = this.cell;
    let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity;
    const add = (x, z) => { x0 = Math.min(x0, x); x1 = Math.max(x1, x); z0 = Math.min(z0, z); z1 = Math.max(z1, z); };
    for (const run of this.runs) for (let k = 0; k < run.path.length; k += 5) add(run.path[k][1], run.path[k][3]);
    for (const zn of this.zones) add(zn.at[0], zn.at[2]);
    if (!isFinite(x0)) return;
    const toI = x => (x - m.x0) / cell, toJ = z => (z - m.z0) / (m.sizeZ / (n - 1));
    const margin = 200 / cell;
    const want = Math.max(256, toI(x1) - toI(x0) + 2 * margin, toJ(z1) - toJ(z0) + 2 * margin);
    const r = this.regionAt((toI(x0) + toI(x1)) / 2, (toJ(z0) + toJ(z1)) / 2, want);
    const cur = this.region;
    const inside = cur && cur.step === r.step && toI(x0) >= cur.i0 && toI(x1) <= cur.i1 && toJ(z0) >= cur.j0 && toJ(z1) <= cur.j1;
    if (!force && inside && !cur.look) return;
    this.setRegion(r);
  }

  /// A patch of `want` samples a side centred on sample (ci, cj).
  regionAt(ci, cj, want) {
    const n = terrain.meta.grid;
    const step = want <= DETAIL ? 1 : want <= DETAIL * 2 ? 2 : 4;
    const side = Math.min(n - 1, Math.ceil(Math.min(want, DETAIL * step) / 4) * 4);
    const place = c => Math.max(0, Math.min(n - 1 - side, Math.floor((c - side / 2) / 4) * 4));
    const r = { i0: place(ci), j0: place(cj), step };
    r.i1 = r.i0 + side; r.j1 = r.j0 + side;
    return r;
  }

  setRegion(r) {
    this.region = r;
    this.buildDetail(r);
    this.coarseIndex(this.coarse.geometry, r);
    this.detailTexture();
  }

  /// The patch follows the orbit's centre once the camera has settled close
  /// to the ground outside it: the runs' patch covers the runs only, and the
  /// island beyond is the coarse mesh with a level-3/4 photo - panning to
  /// the map's middle looked like the textures breaking (author,
  /// 2026-10-01). Rebuilt 0.4 s after the centre stops; a new setRuns puts
  /// the runs' patch back. What must be covered is cut to the map: the patch
  /// is clamped inside it, so a centre near or past the edge (the whole
  /// coast) never counted as covered and the patch was rebuilt every 0.4 s -
  /// the terrain flickering white there (author, 2026-10-01).
  lookRegion(target, dist, now) {
    const m = terrain.meta, cur = this.region;
    if (!m || !cur || !this.coarse || this.mode !== "orbit" || dist > 1500) { this.looking = null; return; }
    const n = m.grid - 1, cell = this.cell, cellZ = m.sizeZ / n;
    const ci = (target.x - m.x0) / cell, cj = (-target.z - m.z0) / cellZ;
    const half = Math.max(250, Math.min(900, dist * 1.5)) / cell;     // samples
    const seen = half * 0.6;
    const a0 = Math.max(0, ci - seen), a1 = Math.min(n, ci + seen), b0 = Math.max(0, cj - seen), b1 = Math.min(n, cj + seen);
    if (a0 >= a1 || b0 >= b1 || (a0 >= cur.i0 && a1 <= cur.i1 && b0 >= cur.j0 && b1 <= cur.j1)) { this.looking = null; return; }
    const l = this.looking;
    if (!l || Math.hypot(l.ci - ci, l.cj - cj) > 4) { this.looking = { ci, cj, since: now }; return; }
    if (now - l.since < 400) return;
    this.looking = null;
    const r = this.regionAt(Math.max(0, Math.min(n, ci)), Math.max(0, Math.min(n, cj)), 2 * half);
    if (r.i0 === cur.i0 && r.j0 === cur.j0 && r.i1 === cur.i1 && r.j1 === cur.j1 && r.step === cur.step) return;
    r.look = true;
    this.setRegion(r);
  }

  buildDetail(r) {
    if (this.detail) { this.scene.remove(this.detail); dispose(this.detail, this.detail.userData.borrowed); }
    const m = terrain.meta, h = terrain.heights, n = m.grid, cell = this.cell, cellZ = m.sizeZ / (n - 1);
    const cols = (r.i1 - r.i0) / r.step + 1, rows = (r.j1 - r.j0) / r.step + 1;
    // The grid, then a skirt: each edge vertex again 12 m lower, so the
    // seam with the coarse mesh (whose edges only pass through every 4th
    // sample) never shows a crack.
    const edge = [];
    for (let i = 0; i < cols; i++) edge.push(i);
    for (let j = 1; j < rows; j++) edge.push(j * cols + cols - 1);
    for (let i = cols - 2; i >= 0; i--) edge.push((rows - 1) * cols + i);
    for (let j = rows - 2; j >= 0; j--) edge.push(j * cols);
    const count = cols * rows + edge.length;
    const pos = new Float32Array(count * 3), uv = new Float32Array(count * 2);
    for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
      const k = j * cols + i, si = r.i0 + i * r.step, sj = r.j0 + j * r.step;
      pos[k * 3] = m.x0 + si * cell;
      pos[k * 3 + 1] = m.y0 + h[sj * n + si] / 65535 * m.sizeY;
      pos[k * 3 + 2] = -(m.z0 + sj * cellZ);
    }
    const base = cols * rows, idx = new Uint32Array((edge.length + (cols - 1) * (rows - 1)) * 6);
    let q = 0;
    const quad = (a, b, c, d) => { idx[q++] = a; idx[q++] = c; idx[q++] = b; idx[q++] = b; idx[q++] = c; idx[q++] = d; };
    // A vertex on one of the terrain's holes (terrainHoles): no triangle uses it.
    const holes = this.holes, hole = v => holes[(r.j0 + Math.floor(v / cols) * r.step) * n + r.i0 + (v % cols) * r.step];
    edge.forEach((v, e) => {
      const k = base + e;
      pos[k * 3] = pos[v * 3]; pos[k * 3 + 1] = pos[v * 3 + 1] - 12; pos[k * 3 + 2] = pos[v * 3 + 2];
      const w = (e + 1) % edge.length;
      if (!hole(v) && !hole(edge[w])) quad(v, edge[w], k, base + w);
    });
    for (let j = 0; j < rows - 1; j++) for (let i = 0; i < cols - 1; i++) {
      const a = j * cols + i;
      if (hole(a) || hole(a + 1) || hole(a + cols) || hole(a + cols + 1)) continue;
      quad(a, a + 1, a + cols, a + cols + 1);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.BufferAttribute(pos, 3));
    g.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
    g.setIndex(new THREE.BufferAttribute(idx.subarray(0, q), 1));
    g.computeVertexNormals();
    this.detail = new THREE.Mesh(g, terrainMaterial());
    this.detail.renderOrder = ORDER.terrain;
    // Until its own photo is made: the island's (the same picture, coarser).
    // A patch with no texture drew white for a frame.
    const map = this.coarse.material.map;
    if (map) { this.detail.material.map = map; this.detail.userData.borrowed = true; this.detailUv(false); }
    this.detail.material.opacity = this.fade;
    this.scene.add(this.detail);
    this.dirty = true;
  }

  /// UVs over the whole island (map.jpg) or over the patch (its own photo).
  detailUv(local) {
    const g = this.detail.geometry, pos = g.attributes.position.array, uv = g.attributes.uv.array, m = terrain.meta;
    const r = this.region, cell = this.cell, cellZ = m.sizeZ / (m.grid - 1);
    const x0 = local ? m.x0 + r.i0 * cell : m.x0, sx = local ? (r.i1 - r.i0) * cell : m.sizeX;
    const z0 = local ? m.z0 + r.j0 * cellZ : m.z0, sz = local ? (r.j1 - r.j0) * cellZ : m.sizeZ;
    for (let k = 0; k < uv.length / 2; k++) { uv[k * 2] = (pos[k * 3] - x0) / sx; uv[k * 2 + 1] = (-pos[k * 3 + 2] - z0) / sz; }
    g.attributes.uv.needsUpdate = true;
  }

  /// Relief (map.jpg) everywhere; with aerial tiles and a photo layer chosen,
  /// the island from level 4 (3 where the GPU's textures are smaller) and the
  /// patch from the finest level that fits a texture (open sea has no tiles).
  /// The island's photo is made on a layer change only; a moved patch makes
  /// its own (both were remade per patch - the island white while its 4096 px
  /// canvas uploaded again; 2026-10-01).
  textures() { this.islandTexture(); this.detailTexture(); }

  /// The aerial layer drawn, or null (relief).
  shownLayer() {
    const a = aerial.meta;
    return a && this.layer !== "relief" ? (a.layers.includes(this.layer) ? this.layer : a.layers[0]) : null;
  }

  maxTexture() { return Math.min(MOBILE ? 2048 : 4096, this.renderer.capabilities.maxTextureSize); }

  reliefTexture(img) {
    if (!this.reliefTex) { this.reliefTex = texture(img); this.reliefTex.userData.shared = true; }
    return this.reliefTex;
  }

  islandTexture() {
    const gen = ++this.islandGen, m = terrain.meta, a = aerial.meta, layer = this.shownLayer();
    reliefImage().then(img => {
      if (gen !== this.islandGen || this.disposed) return;
      this.island = null;
      if (!layer) { setMap(this.coarse, this.reliefTexture(img)); this.dirty = true; return; }
      const max = this.maxTexture(), level = Math.min(max >= 4096 ? 4 : 3, a.levels);
      const tex = this.photo(() => gen === this.islandGen, img, null, layer, level, { x0: m.x0, z0: m.z0, sx: m.sizeX, sz: m.sizeZ }, Math.min(max, 4096));
      this.island = { layer, canvas: tex.image };
      setMap(this.coarse, tex);
      this.dirty = true;
    });
  }

  detailTexture() {
    if (!this.detail) return;
    const gen = ++this.detailGen, m = terrain.meta, a = aerial.meta, layer = this.shownLayer();
    reliefImage().then(img => {
      if (gen !== this.detailGen || this.disposed || !this.detail) return;
      if (!layer) { this.detailUv(false); setMap(this.detail, this.reliefTexture(img)); this.dirty = true; return; }
      const max = this.maxTexture(), r = this.region, cellZ = m.sizeZ / (m.grid - 1);
      const rect = { x0: m.x0 + r.i0 * this.cell, z0: m.z0 + r.j0 * cellZ, sx: (r.i1 - r.i0) * this.cell, sz: (r.j1 - r.j0) * cellZ };
      const L = Math.max(0, Math.min(a.levels, Math.floor(Math.log2(max * m.sizeX / (Math.max(rect.sx, rect.sz) * a.tile)))));
      // Starts from the island's photo (when it is this layer's), not the
      // bare relief: no flash of the relief while the finer tiles arrive.
      const base = this.island && this.island.layer === layer ? this.island.canvas : null;
      const tex = this.photo(() => gen === this.detailGen, img, base, layer, L, rect, max);
      this.detailUv(true);
      setMap(this.detail, tex);
      this.dirty = true;
    });
  }

  /// A canvas texture of rect (world metres) from level L's tiles, over the
  /// relief - or over base (the island's photo canvas) when given. current():
  /// false once a newer texture replaced this one (its late tiles dropped).
  photo(current, relief, base, layer, L, rect, max) {
    const m = terrain.meta, a = aerial.meta, ts = m.sizeX / (1 << L), ppm = a.tile / ts, north = m.z0 + m.sizeZ;
    const c = document.createElement("canvas");
    c.width = Math.min(max, Math.round(rect.sx * ppm)); c.height = Math.min(max, Math.round(rect.sz * ppm));
    const kx = c.width / rect.sx, kz = c.height / rect.sz, top = rect.z0 + rect.sz;
    const ctx = c.getContext("2d");
    ctx.imageSmoothingQuality = "high";
    // The picture under it all: rect cut out of map.jpg (or the island's photo).
    const under = base || relief, iw = under.width / m.sizeX, ih = under.height / m.sizeZ;
    ctx.drawImage(under, (rect.x0 - m.x0) * iw, (north - top) * ih, rect.sx * iw, rect.sz * ih, 0, 0, c.width, c.height);
    const tex = texture(c);
    const tx0 = Math.max(0, Math.floor((rect.x0 - m.x0) / ts)), tx1 = Math.min((1 << L) - 1, Math.floor((rect.x0 + rect.sx - m.x0 - 1e-6) / ts));
    const ty0 = Math.max(0, Math.floor((north - top) / ts)), ty1 = Math.min((1 << L) - 1, Math.floor((north - rect.z0 - 1e-6) / ts));
    // Tiles map.js already found absent (open sea) are not asked for again;
    // the bake writes every parent, so a missing parent means a missing tile.
    const li = a.layers.indexOf(layer), missing = (x, y, l) => aerial.missing.has(RunMap.tileKey(li, l, x, y));
    for (let ty = ty0; ty <= ty1; ty++) for (let tx = tx0; tx <= tx1; tx++) {
      let gone = false;
      for (let k = 0; k <= L && !gone; k++) gone = missing(tx >> k, ty >> k, L - k);
      if (gone) continue;
      const img = new Image();
      img.onerror = () => aerial.missing.add(RunMap.tileKey(li, L, tx, ty));
      img.onload = () => {
        if (!current() || this.disposed) return;
        const x = (m.x0 + tx * ts - rect.x0) * kx, y = (top - (north - ty * ts)) * kz;
        ctx.drawImage(img, x, y, ts * kx, ts * kz);
        this.upload(tex);
      };
      img.src = "/aerial/" + layer + "/" + L + "/" + tx + "_" + ty + ".jpg" + aerial.meta.v;
    }
    return tex;
  }

  /// A texture re-sent to the GPU at most every 300 ms while tiles arrive.
  upload(tex) {
    if (tex.userData.pending) return;
    tex.userData.pending = setTimeout(() => { tex.userData.pending = 0; tex.needsUpdate = true; this.dirty = true; }, 300);
  }

  // --- each frame ---

  loop(now) {
    if (!this.visible || this.disposed) return;
    this.raf = requestAnimationFrame(this.loop);
    const dt = this.last ? Math.min(0.1, (now - this.last) / 1000) : 0;
    this.last = now;
    const moving = this.frame(dt);
    if (this.dirty || moving) { this.dirty = false; this.render(); }
  }

  /// Ghosts, fade and camera for this frame; true while something still eases.
  frame(dt) {
    let moving = false;
    const under = [];
    for (const g of this.ghosts) {
      const s = RunMap.at(g.userData.run.path, this.time);
      g.visible = !!s;
      if (!s) continue;
      g.position.set(s[1], s[2], -s[3]);
      const ground = RunMap.groundAt(s[1], s[3]);
      under.push({ run: g.userData.run, under: ground !== null && s[2] < ground - UNDER });
    }
    for (const l of this.lines) l.material.uniforms.uTime.value = this.time;

    // Underground: every ghost (orbit) or the followed one (follow) below
    // the ground fades the terrain and the sea, as the 2D map fades its relief.
    const followed = this.followedRun();
    const u = this.mode === "follow" && followed ? under.filter(e => e.run === followed) : under;
    this.fadeTo = u.length && u.every(e => e.under) ? 0.22 : 1;
    if (this.fade !== this.fadeTo) {
      const k = 1 - Math.exp(-dt * 8);
      this.fade += (this.fadeTo - this.fade) * (dt ? k : 1);
      if (Math.abs(this.fade - this.fadeTo) < 0.01) this.fade = this.fadeTo;
      moving = true;
    }
    this.note.hidden = this.fadeTo === 1;
    for (const mesh of [this.coarse, this.detail]) if (mesh) {
      mesh.material.opacity = this.fade;
      mesh.material.depthWrite = this.fade > 0.99;
    }
    if (this.sea) { this.sea.material.opacity = 0.9 * this.fade; this.sea.material.depthWrite = this.fade > 0.99; }
    for (const l of this.lines) if (l.renderOrder === ORDER.xray) l.visible = this.fade > 0.99;

    if (this.mode === "follow" && followed) moving = this.followCamera(followed, dt) || moving;
    else this.orbitCamera();
    this.world.setFade(this.fade);
    const target = this.mode === "follow" && this.follow.target ? this.follow.target : this.orbit.target;
    this.world.setCut(this.fadeTo < 1, this.camera.position, target);
    // The camera's focal length in drawing buffer pixels: where a far copy's
    // error stays under a pixel (world3d.js split()).
    // The view cone: forward, and the half angle to the screen's corner
    // (instances outside it, widened, are not drawn).
    const half = Math.tan(this.camera.fov * Math.PI / 360);
    const focal = this.canvas.height / (2 * half);
    const corner = Math.atan(half * Math.sqrt(1 + this.camera.aspect * this.camera.aspect));
    this.world.update(target, performance.now(), this.camera.position, focal,
      this.camera.getWorldDirection(this.lookDir), corner, this.camera.near);
    if (this.mode === "orbit") this.lookRegion(this.orbit.target, this.orbit.dist, performance.now());
    if (this.looking) moving = true;     // keep frames coming until the patch is placed
    return moving;
  }

  followedRun() {
    return this.runs.find(r => r.id === this.focus && r.path.length) || this.runs.find(r => r.path.length) || null;
  }

  orbitCamera() {
    const o = this.orbit, c = this.camera;
    c.position.set(Math.sin(o.yaw) * Math.cos(o.pitch), Math.sin(o.pitch), Math.cos(o.yaw) * Math.cos(o.pitch))
      .multiplyScalar(o.dist).add(o.target);
    c.lookAt(o.target);
    this.clip(o.dist);
  }

  /// Behind the ghost, facing where it goes (its last 0.4 s), eased; drag to
  /// look around it, wheel / pinch for distance. A solid model between the
  /// ghost and the camera (a cave wall, a rock, a building) pulls the camera
  /// in front of it at once; it eases back out once the way is clear
  /// (author, 2026-10-02: Follow's camera sat in a cave's rock).
  followCamera(run, dt) {
    const f = this.follow, t = this.time;
    const s = RunMap.at(run.path, t), b = RunMap.at(run.path, t - 0.4);
    if (!s) return false;
    const target = new THREE.Vector3(s[1], s[2] + FOLLOW_HEAD, -s[3]);
    const dx = s[1] - b[1], dz = -(s[3] - b[3]);
    let moving = false;
    if (Math.hypot(dx, dz) > 0.05) {
      const want = Math.atan2(-dx, -dz);    // the camera's side: opposite the motion
      if (f.heading === null) f.heading = want;
      let d = want - f.heading;
      d = Math.atan2(Math.sin(d), Math.cos(d));
      f.heading += d * (dt ? 1 - Math.exp(-dt * 3) : 1);
      moving = Math.abs(d) > 0.002;
    } else if (f.heading === null) f.heading = this.orbit.yaw;
    if (!f.target) f.target = target.clone();
    else f.target.lerp(target, dt ? 1 - Math.exp(-dt * 12) : 1);
    if (f.target.distanceToSquared(target) > 1e-4) moving = true;
    const yaw = f.heading + f.yawOff, c = this.camera;
    const back = this.followDir.set(Math.sin(yaw) * Math.cos(f.pitch), Math.sin(f.pitch), Math.cos(yaw) * Math.cos(f.pitch));
    // The wall test: again when the camera's line changed (a centimetre, a
    // tenth of a degree) or every 0.3 s (chunks arriving).
    const now = performance.now();
    const key = [f.target.x, f.target.y, f.target.z].map(v => Math.round(v * 100)).join() + "," + Math.round(yaw * 1000) + "," + Math.round(f.pitch * 1000) + "," + Math.round(f.dist * 100);
    if (key !== f.hitKey || now - f.hitAt > 300) {
      f.hitKey = key; f.hitAt = now;
      f.hit = this.world.firstHit(f.target, c.position.copy(back).multiplyScalar(f.dist).add(f.target));
    }
    const limit = f.hit === Infinity ? f.dist : Math.max(MIN_FOLLOW, f.hit - WALL_GAP);
    if (f.cur === null || limit < f.cur || !dt) f.cur = limit;
    else if (f.cur < limit) {
      f.cur += (limit - f.cur) * (1 - Math.exp(-dt * 4));
      if (limit - f.cur < 0.01) f.cur = limit; else moving = true;
    }
    c.position.copy(back).multiplyScalar(f.cur).add(f.target);
    // Pulled in close, aim lower (towards the ghost from 1.2 m over it), or
    // the ghost drops off the bottom of the screen.
    c.lookAt(this.followLook.copy(f.target).setY(f.target.y - FOLLOW_HEAD * Math.max(0, 1 - f.cur / FOLLOW_LOW)));
    this.clip(f.cur);
    return moving;
  }

  clip(dist) {
    const c = this.camera, near = Math.max(0.1, Math.min(5, dist * 0.004));
    if (c.near !== near) { c.near = near; c.updateProjectionMatrix(); }
  }

  render() {
    const c = this.canvas, w = c.clientWidth, h = c.clientHeight;
    if (!w || !h) return;
    const r = this.renderer, size = r.getSize(new THREE.Vector2());
    if (size.x !== w || size.y !== h) {
      r.setSize(w, h, false);
      this.camera.aspect = w / h; this.camera.updateProjectionMatrix();
    }
    const buf = r.getDrawingBufferSize(new THREE.Vector2());
    for (const l of this.lines) {
      l.material.uniforms.uRes.value.copy(buf);
      l.material.uniforms.uWidth.value = l.userData.width * r.getPixelRatio();
    }
    // Sprites of a fixed pixel size: scale = 2 px / (P11 * height).
    const k = 2 / (this.camera.projectionMatrix.elements[5] * h);
    for (const g of this.ghosts) g.scale.set(14 * k, 14 * k, 1);
    for (const l of this.labels) l.scale.set(l.userData.px[0] * k, l.userData.px[1] * k, 1);
    r.render(this.scene, this.camera);
  }

  // --- input ---

  bind() {
    const c = this.canvas, pointers = new Map();
    let down = null, pinch = 0, mid = null;
    const on = (type, fn, opts) => c.addEventListener(type, fn, opts);
    on("contextmenu", e => e.preventDefault());
    on("wheel", e => {
      e.preventDefault();
      this.zoom(Math.exp(e.deltaY * 0.0012));
    }, { passive: false });
    on("dblclick", () => { if (this.mode === "follow") { this.follow.yawOff = 0; this.follow.pitch = 0.22; this.follow.dist = 12; this.dirty = true; } else this.fit(); });
    on("pointerdown", e => {
      c.setPointerCapture(e.pointerId);
      pointers.set(e.pointerId, [e.clientX, e.clientY]);
      c.classList.add("drag");
      down = pointers.size === 1 ? { x: e.clientX, y: e.clientY, pan: e.button === 2 || e.shiftKey || e.ctrlKey } : null;
      pinch = 0; mid = null;
    });
    const end = e => {
      if (e.type === "pointerup" && down && Math.hypot(e.clientX - down.x, e.clientY - down.y) < 5 && e.button === 0 && this.hooks.onPick) {
        const r = c.getBoundingClientRect(), hit = this.nearest(e.clientX - r.left, e.clientY - r.top, 14);
        if (hit) this.hooks.onPick(hit.run, hit.t);
      }
      down = null; pinch = 0; mid = null;
      pointers.delete(e.pointerId);
      if (!pointers.size) c.classList.remove("drag");
    };
    on("pointerup", end);
    on("pointercancel", end);
    on("pointermove", e => {
      if (!pointers.has(e.pointerId)) return;
      const prev = pointers.get(e.pointerId);
      pointers.set(e.pointerId, [e.clientX, e.clientY]);
      if (pointers.size === 2) {
        // Two fingers: pinch = distance, the midpoint's move = pan.
        const [a, b] = [...pointers.values()];
        const d = Math.hypot(a[0] - b[0], a[1] - b[1]), m = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
        if (pinch) this.zoom(pinch / d);
        if (mid) this.pan(m[0] - mid[0], m[1] - mid[1]);
        pinch = d; mid = m;
        return;
      }
      const dx = e.clientX - prev[0], dy = e.clientY - prev[1];
      if (down && down.pan) this.pan(dx, dy);
      else this.rotate(dx, dy);
    });
  }

  rotate(dx, dy) {
    const o = this.mode === "follow" ? this.follow : this.orbit;
    if (this.mode === "follow") this.follow.yawOff -= dx * 0.006; else o.yaw -= dx * 0.006;
    o.pitch = Math.max(-1.2, Math.min(1.55, o.pitch + dy * 0.005));
    this.dirty = true;
  }

  zoom(k) {
    const o = this.mode === "follow" ? this.follow : this.orbit;
    o.dist = Math.max(this.mode === "follow" ? 2 : 5, Math.min(this.mode === "follow" ? 400 : 9000, o.dist * k));
    this.dirty = true;
  }

  /// Moves the orbit's centre with the pointer, along the ground (no pan
  /// while following: the ghost is the centre).
  pan(dx, dy) {
    if (this.mode === "follow") return;
    const o = this.orbit, h = this.canvas.clientHeight || 1;
    const per = 2 * o.dist * Math.tan(this.camera.fov * Math.PI / 360) / h;
    const right = new THREE.Vector3(Math.cos(o.yaw), 0, -Math.sin(o.yaw));
    const fwd = new THREE.Vector3(-Math.sin(o.yaw), 0, -Math.cos(o.yaw));
    const k = Math.max(0.35, Math.sin(o.pitch));   // a steep view moves less along the ground per pixel
    o.target.addScaledVector(right, -dx * per).addScaledVector(fwd, dy * per / k);
    this.dirty = true;
  }

  /// The run sample nearest a screen point, within maxPx: { run, t }.
  nearest(px, py, maxPx) {
    const w = this.canvas.clientWidth, h = this.canvas.clientHeight, v = new THREE.Vector3();
    this.camera.updateMatrixWorld();
    let best = null, bestD = maxPx;
    for (const run of this.runs) for (const s of run.path) {
      v.set(s[1], s[2], -s[3]).project(this.camera);
      if (v.z > 1 || v.z < -1) continue;
      const d = Math.hypot((v.x + 1) / 2 * w - px, (1 - v.y) / 2 * h - py);
      if (d < bestD) { bestD = d; best = { run, t: s[0] }; }
    }
    return best;
  }

  dispose() {
    this.disposed = true;
    cancelAnimationFrame(this.raf);
    this.ro.disconnect();
    this.note.remove();
    this.world.dispose();
    if (this.sea) this.sea.material.userData.mask.dispose();
    this.scene.traverse(dispose);
    if (this.reliefTex) this.reliefTex.dispose();
    this.renderer.dispose();
    this.renderer.forceContextLoss();
  }
}

// --- helpers ---------------------------------------------------------------------------

let reliefPromise = null;
function reliefImage() {
  if (terrain.image) return Promise.resolve(terrain.image);
  if (!reliefPromise) reliefPromise = new Promise((ok, fail) => {
    const img = new Image();
    img.onload = () => ok(img);
    img.onerror = () => { reliefPromise = null; fail(new Error("no terrain image")); };
    img.src = "/terrain/" + terrain.meta.image;
  });
  return reliefPromise;
}

function texture(source) {
  const t = source instanceof HTMLCanvasElement ? new THREE.CanvasTexture(source) : new THREE.Texture(source);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 8;
  t.needsUpdate = true;
  return t;
}

/// The water open to the map's edge, as the photo bake draws it
/// (scripts/aerial-bake.py load_heights): a flood fill from the edge over
/// the samples under sea level. 255 = open sea.
function openSea(m, h) {
  const n = m.grid, sea = Math.round((m.sea - m.y0) / m.sizeY * 65535);
  const open = new Uint8Array(n * n), stack = [];
  for (let k = 0; k < n; k++) stack.push(k, (n - 1) * n + k, k * n, k * n + n - 1);
  while (stack.length) {
    const k = stack.pop();
    if (open[k] || h[k] >= sea) continue;
    open[k] = 255;
    const i = k % n, j = (k - i) / n;
    if (i > 0) stack.push(k - 1);
    if (i < n - 1) stack.push(k + 1);
    if (j > 0) stack.push(k - n);
    if (j < n - 1) stack.push(k + n);
  }
  return open;
}

/// The terrain's holes: samples at its lowest height (0) away from the open
/// sea - the sinkhole. The game does not draw its terrain there (its floor,
/// cliffs and water are models down to y -304), but the heights cross the
/// pit at y 0, and that flat floor hid the sinkhole in 3D (author,
/// 2026-10-01). The only other height-0 samples inland of the coast are
/// under the open sea (the yacht's cove) and stay.
function terrainHoles(h, open) {
  const holes = new Uint8Array(h.length);
  for (let k = 0; k < h.length; k++) if (h[k] === 0 && !open[k]) holes[k] = 1;
  return holes;
}

/// The sea only over the open water, widened by one sample so the sea runs
/// under the shore. Outside the map: sea everywhere.
function seaMask(mat, m, open) {
  const n = m.grid;
  const mask = open.slice();
  for (let k = 0; k < n * n; k++) {
    if (open[k]) continue;
    const i = k % n;
    if ((i > 0 && open[k - 1]) || (i < n - 1 && open[k + 1]) || (k >= n && open[k - n]) || (k < n * n - n && open[k + n])) mask[k] = 255;
  }
  const t = new THREE.DataTexture(mask, n, n, THREE.RedFormat, THREE.UnsignedByteType);
  t.minFilter = t.magFilter = THREE.LinearFilter;
  t.needsUpdate = true;
  const u = { seaMask: { value: t }, seaRect: { value: new THREE.Vector4(m.x0, m.z0, m.sizeX, m.sizeZ) } };
  mat.onBeforeCompile = shader => {
    Object.assign(shader.uniforms, u);
    shader.vertexShader = shader.vertexShader
      .replace("#include <common>", "#include <common>\nvarying vec2 vSeaXZ;")
      .replace("#include <project_vertex>", "#include <project_vertex>\nvec4 sw = modelMatrix * vec4(transformed, 1.0);\nvSeaXZ = vec2(sw.x, -sw.z);");
    shader.fragmentShader = shader.fragmentShader
      .replace("#include <common>", "#include <common>\nvarying vec2 vSeaXZ;\nuniform sampler2D seaMask;\nuniform vec4 seaRect;")
      .replace("#include <clipping_planes_fragment>", `#include <clipping_planes_fragment>
        vec2 sp = (vSeaXZ - seaRect.xy) / seaRect.zw;    // 0..1 over the map; samples at k / (n - 1)
        if (sp.x > 0.0 && sp.y > 0.0 && sp.x < 1.0 && sp.y < 1.0) {
          vec2 sn = vec2(textureSize(seaMask, 0));
          if (texture2D(seaMask, (sp * (sn - 1.0) + 0.5) / sn).r < 0.5) discard;
        }`);
  };
  mat.customProgramCacheKey = () => "seamask";
  mat.userData.mask = t;
}

function terrainMaterial() {
  return new THREE.MeshLambertMaterial({ color: 0xffffff, side: THREE.DoubleSide, transparent: true });
}

/// Swaps a mesh's texture, freeing the old one unless it is the shared relief
/// or borrowed (the patch showing the island's photo until its own is made).
function setMap(mesh, tex) {
  const old = mesh.material.map;
  if (old === tex) return;
  if (old && !old.userData.shared && !mesh.userData.borrowed) { clearTimeout(old.userData.pending); old.dispose(); }
  mesh.userData.borrowed = false;
  mesh.material.map = tex;
  mesh.material.needsUpdate = true;
}

/// keepMap: the texture is another mesh's (a borrowed one).
function dispose(o, keepMap) {
  if (o.geometry) o.geometry.dispose();
  const m = o.material;
  if (m) {
    if (m.map && !keepMap && m.map !== ballTexture && !m.map.userData.shared) { clearTimeout(m.map.userData.pending); m.map.dispose(); }
    m.dispose();
  }
}

export async function create(canvas, hooks) {
  if (!World) {
    const src = (document.querySelector("[data-world3d-src]") || { dataset: {} }).dataset.world3dSrc || "/world3d.js";
    World = (await import(src)).World;
  }
  return new Map3D(canvas, hooks);
}
