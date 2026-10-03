// The 3D view's world: the game's own models and collision, read from its
// files offline (scripts/world-extract.py export -> /world/, uploaded by the
// owner). world.json lists the meshes, materials, models (a mesh + its
// materials + render / collide + layer) and 250 m chunks of instances per
// area (surface, caves, endgame); a chunk is loaded when it comes within
// RADIUS of what the camera looks at and dropped past DROP. Every model is
// one InstancedMesh over all loaded chunks, so the draw calls stay at the
// number of models in view, not of objects. Files are fetched with
// ?v=<build> (world.json's export time): cached for a day, never stale.
// Files are named by index, so a page holding an older world.json would
// draw a new upload's files in the old places (leaves on cave walls): the
// server refuses another build's ?v= (404) and the page then re-reads
// world.json and starts over (stale()); each 3D view reads it afresh.
// Meshes came in packs (version 2: "packs", each mesh's "pack" = [pack,
// offset, length]), a few per chunk; version 1
// exports (one m/<i>.bin per mesh) are still read. Textures too since
// 2026-10-02 ("texpacks", "textures"[i] = [jpg pack, off, len, png pack,
// off, len]): a texture is cut out of its pack as a blob URL; exports
// without them read t/<i>.jpg / .png. Version 3 (2026-10-02): everything -
// chunks' instances, meshes, textures - in a few b/<i>.bin files ("files"),
// each blob's place [file, offset, length]. Far copies (2026-10-02,
// scripts/world_pack.py): a mesh's "far" = lighter copies, each with its
// error "e" (mesh units); an instance is drawn with the lightest one whose
// error, at its scale and distance from the camera, is under FAR_PIXELS on
// the screen - each model is then one InstancedMesh per copy, its instances
// split between them by distance (split()). Collision keeps the full mesh.
// split() also leaves out instances outside the view cone, and those under
// the ground seen from above it (not over the sinkhole) - every model.
// The server sends .bin / .json Brotli'd or gzipped (the upload's copies).
//
// World (Unity): x east, y up, z north; drawn at (x, y, -z) as map3d.js. An
// instance's matrix is S * M with S = diag(1, 1, -1) on the mesh's own
// (Unity) vertices - mirrored; with the indices turned (geometry()) front
// faces come out counter-clockwise, so solid materials cull back faces as
// Unity does (a cave shell seen from outside is see-through, not a grey
// wall). Cut-outs (leaf cards) stay double-sided.
// Served by the site (wwwroot/vendor, the npm package's file, checked
// against its integrity hash): the CSP allows the site's own scripts only.
import * as THREE from "/vendor/three-0.170.0.module.min.js?v=0.170.0";

const RADIUS = 700, DROP = 1100;           // metres, horizontally from the camera's target
const CUT_MARGIN = 4;                      // metres kept in front of the target, underground
const S = new THREE.Matrix4().makeScale(1, 1, -1);
// A far copy is drawn where its error is under this many pixels on the
// screen (the drawing buffer's: the Detail button's density moves the
// switches out). 0 = full meshes only (world.setFar, for comparing).
export const FAR_PIXELS = 1;
const SPLIT_MOVE = 2;                      // metres the camera moves before the copies are picked again
// A far model's instances outside the view cone (the screen's corners) widened
// by CULL_MARGIN are not drawn; the camera turning CULL_TURN picks again at
// once, so turning under CULL_MARGIN - CULL_TURN between picks never shows a gap.
const CULL_MARGIN = 30 * Math.PI / 180;
const CULL_TURN = 10 * Math.PI / 180;
const CULLED = 254;                        // split(): level of an instance not drawn
// Under the ground: with the camera above opaque terrain, an instance wholly
// below it is hidden by it - except through the sinkhole (the terrain's
// holes), so one whose line from the camera passes over a hole is kept.
// The terrain is drawn from the heights' samples (every COARSE_MAX-th away
// from the detail patch, map3d.js COARSE; a cell touching a hole left out):
// its surface over a footprint is never below the lowest sample within
// COARSE_MAX samples of it, and every drawn cell (a step of 1, 2 or 4
// samples, aligned to it) lies inside one aligned COARSE_MAX x COARSE_MAX
// cell, so its surface is never above the highest corner of those.
const COARSE_MAX = 4;                      // samples (map3d.js COARSE: 2, phones 4)
const GROUND_BLOCK = 8;                    // samples per block of the min / max grid
const UNDER_MARGIN = 0.5;                  // metres
const HIT_GROUP = 32;                      // triangles per box in firstHit()'s groups

/// Does the segment o + t d, t in [0, tMax], touch the box at boxes[b..b+5]
/// (min x, y, z, max x, y, z)? Slabs.
function segmentBox(o, d, tMax, boxes, b) {
  let t0 = 0, t1 = tMax;
  for (let j = 0; j < 3; j++) {
    const oj = j === 0 ? o.x : j === 1 ? o.y : o.z, dj = j === 0 ? d.x : j === 1 ? d.y : d.z;
    const lo = boxes[b + j], hi = boxes[b + j + 3];
    if (Math.abs(dj) < 1e-12) { if (oj < lo || oj > hi) return false; continue; }
    let a = (lo - oj) / dj, c = (hi - oj) / dj;
    if (a > c) { const x = a; a = c; c = x; }
    if (a > t0) t0 = a;
    if (c < t1) t1 = c;
    if (t0 > t1) return false;
  }
  return true;
}

// Kinds of model, each with its own switch (the map's "separate toggles per
// kind"; 2026-10-01). The game's layers do not say it alone - trees are in
// treeMid / treeSmall but so are cave panels' planks, cliffs sit in Prop /
// ReflectBig - so a model's layer is read with its mesh's and materials'
// names (checked against the live world.json: 125 tree, 249 rock, 268
// pickup models).
const LAYER_TREE_MID = 11, LAYER_TREE_SMALL = 12, LAYER_PICKUP = 28;
const TREE_NAME = /afsTREE|tree|bush|sapling|fern|skog|blombuske/i;
const NOT_TREE = /plank|crucifix|stone/i;
const ROCK_NAME = /rock|stone|cliff|boulder|poros|wallstone|mountain|cave|sinkhole|dirtrock|climbwall/i;
export const KINDS = [
  ["trees", "Trees", "Trees, bushes, saplings, ferns and plants"],
  ["rocks", "Rocks", "Rocks, cliffs, mountains, cave walls and floors"],
  ["props", "Props", "Buildings, the plane, the endgame, everything else the game placed"],
  ["pickups", "Pickups", "Things the player can pick up"],
];

/// A model's kind: "collision", "pickups", "trees", "rocks" or "props".
export function kindOf(meta, model) {
  if (model.kind === "collide") return "collision";
  if (model.layer === LAYER_PICKUP) return "pickups";
  const name = (meta.meshes[model.mesh] || {}).name || "";
  const mats = model.mats.map(i => i >= 0 ? meta.materials[i].name || "" : "").join(" ");
  if ((model.layer === LAYER_TREE_MID || model.layer === LAYER_TREE_SMALL) && !NOT_TREE.test(name + " " + mats)) return "trees";
  if (TREE_NAME.test(name)) return "trees";
  if (ROCK_NAME.test(name) || ROCK_NAME.test(mats)) return "rocks";
  return "props";
}

let metaPromise = null;
/// world.json's content, or null (nothing uploaded). fresh: read it again
/// (a new 3D view, a file refused as another upload's).
export function worldMeta(fresh) {
  if (!metaPromise || fresh) metaPromise = fetch("/world/world.json", { cache: "no-cache" })
    .then(r => r.status === 200 ? r.json() : null)
    .then(m => m && (m.version === 1 || m.version === 2 && Array.isArray(m.packs) || m.version === 3 && Array.isArray(m.files))
      && Array.isArray(m.chunks) ? m : null)
    .catch(() => null);
  return metaPromise;
}

/// The game's Lux shader lays a second texture over faces that look up
/// (snow on the snow cliffs, grass on cliffs, moss on rocks): blended in by
/// the world normal's height, tiled by the material's own scale.
function topLayer(mat, tex, scale) {
  mat.onBeforeCompile = shader => {
    shader.uniforms.topMap = { value: tex };
    shader.uniforms.topScale = { value: scale };
    shader.vertexShader = shader.vertexShader
      .replace("#include <common>", "#include <common>\nvarying float vUp;")
      .replace("#include <beginnormal_vertex>", `#include <beginnormal_vertex>
        vec3 wn = objectNormal;
        #ifdef USE_INSTANCING
          wn = mat3(instanceMatrix) * wn;
        #endif
        vUp = normalize(mat3(modelMatrix) * wn).y;`);
    shader.fragmentShader = shader.fragmentShader
      .replace("#include <common>", "#include <common>\nvarying float vUp;\nuniform sampler2D topMap;\nuniform float topScale;")
      .replace("#include <map_fragment>", `#include <map_fragment>
        vec4 top = texture2D(topMap, vMapUv * topScale);
        diffuseColor.rgb = mix(diffuseColor.rgb, top.rgb, smoothstep(0.45, 0.75, vUp));`);
  };
  mat.customProgramCacheKey = () => "top";
}

/// A surface lake's water is drawn only where the ground (the terrain's
/// heights, setGround) is below it: the lakes' models are larger than the
/// lakes, and away from the detail patch the terrain mesh is coarse (~14 m),
/// so water showed over the land round them, coming and going as the patch
/// moved (author, 2026-10-01). Measured: every surface lake model's corners
/// stand on ground above its water. Cave lakes are under the terrain - not
/// clipped.
function groundClip(mat, u) {
  mat.onBeforeCompile = shader => {
    Object.assign(shader.uniforms, u);
    shader.vertexShader = shader.vertexShader
      .replace("#include <common>", "#include <common>\nvarying vec3 vGroundW;")
      .replace("#include <project_vertex>", `#include <project_vertex>
        vec4 gw = vec4(transformed, 1.0);
        #ifdef USE_INSTANCING
          gw = instanceMatrix * gw;
        #endif
        vGroundW = (modelMatrix * gw).xyz;`);
    shader.fragmentShader = shader.fragmentShader
      .replace("#include <common>", `#include <common>
        varying vec3 vGroundW;
        uniform sampler2D groundMap; uniform vec4 groundRect; uniform float groundOn;
        // The terrain's height at Unity (x, z), bilinear as Unity samples it; very low outside.
        float groundAt(vec2 xz) {
          ivec2 size = textureSize(groundMap, 0);
          vec2 p = (xz - groundRect.xy) / groundRect.zw * vec2(size - 1);
          if (p.x < 0.0 || p.y < 0.0 || p.x > float(size.x - 1) || p.y > float(size.y - 1)) return -1e9;
          ivec2 i = min(ivec2(floor(p)), size - 2);
          vec2 f = p - vec2(i);
          float a = texelFetch(groundMap, i, 0).r, b = texelFetch(groundMap, i + ivec2(1, 0), 0).r;
          float c = texelFetch(groundMap, i + ivec2(0, 1), 0).r, d = texelFetch(groundMap, i + ivec2(1, 1), 0).r;
          return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
        }`)
      .replace("#include <clipping_planes_fragment>", `#include <clipping_planes_fragment>
        if (groundOn > 0.5 && groundAt(vec2(vGroundW.x, -vGroundW.z)) > vGroundW.y + 0.05) discard;`);
  };
  mat.customProgramCacheKey = () => "groundclip";
}

export class World {
  /// scene: where the models go; changed(): something new to draw;
  /// controls: an element the Models / Collision switches go into.
  constructor(scene, changed, controls) {
    this.scene = scene;
    this.changed = changed;
    this.group = new THREE.Group();
    scene.add(this.group);
    this.meta = null;
    this.chunks = new Map();      // file -> { state: "loading" | "ready", items: [[model, Matrix4]] }
    this.geoms = new Map();       // mesh index -> Promise<BufferGeometry | null>
    this.geomsDone = new Set();   // mesh indices whose geometry() has settled
    this.fetching = 0;            // meshes rebuild() is waiting for (collision just switched on)
    this.packs = new Map();       // pack index -> Promise<ArrayBuffer | null> (version 2)
    this.texPacks = new Map();    // texture pack index -> Promise<ArrayBuffer | null>
    this.files = new Map();       // file index -> Promise<ArrayBuffer | null> (version 3)
    this.chunkOf = new Map();     // chunk file name -> its world.json entry
    this.textures = new Map();    // texture index -> THREE.Texture
    this.mats = new Map();        // material index -> THREE.Material (render)
    this.drawn = new Map();       // model index -> THREE.InstancedMesh
    this.dirtyModels = new Set();
    this.gen = 0;                 // + 1 on each start-over (stale()): late loads of the old world are dropped
    this.farPixels = FAR_PIXELS;
    this.eye = new THREE.Vector3();   // the camera, three.js space (update())
    this.focal = 0;                   // the camera's focal length in drawing buffer pixels
    this.splitAt = null;              // [eye x, y, z, focal] the copies were last picked for
    this.look = new THREE.Vector3();  // the camera's forward direction, three.js space (update())
    this.cone = 0;                    // half angle from forward to the screen's corner + CULL_MARGIN; 0 = no culling
    this.lookAt = new THREE.Vector3();    // the forward direction the copies were last picked for
    this.grid = null;                 // the terrain's heights by block, for culling under the ground (setGround)
    this.hideUnder = false;           // the camera above opaque terrain: instances under it not drawn
    this.staleAt = 0;
    // firstHit()'s triangle groups per geometry, and its scratch.
    this.hitCache = new WeakMap();
    this.hitRay = new THREE.Raycaster();
    this.hitM = new THREE.Matrix4(); this.hitInv = new THREE.Matrix4();
    for (const k of ["hitDir", "hitO", "hitE", "hitP", "hitA", "hitB", "hitC", "hitF0", "hitF1", "hitF2"]) this[k] = new THREE.Vector3();
    this.show = { trees: true, rocks: true, props: true, pickups: true, collision: false };
    try {
      const s = JSON.parse(localStorage.getItem("forest.world3d") || "null");
      if (s) {
        // Before the kinds: one "models" switch for all four.
        if (typeof s.models === "boolean") for (const [k] of KINDS) if (!(k in s)) s[k] = s.models;
        delete s.models;
        Object.assign(this.show, s);
      }
    } catch (e) { /* defaults */ }
    this.kinds = [];              // model index -> kind (kindOf), filled by use()
    this.fade = 1;
    this.target = null;
    this.next = 0;
    this.water = true;                    // the lakes' surfaces shown (map3d.js: the Water button)
    this.waterMats = new Set();
    this.ground = { groundMap: { value: null }, groundRect: { value: new THREE.Vector4() }, groundOn: { value: 0 } };
    // One plane, always there (off = far below everything), so switching the
    // cutaway never recompiles the materials.
    this.cutPlanes = [new THREE.Plane(new THREE.Vector3(0, 1, 0), 1e9)];
    this.cutNormal = new THREE.Vector3();
    this.collideMat = new THREE.MeshBasicMaterial({ color: 0xff7a2f, transparent: true, opacity: 0.28, side: THREE.DoubleSide, depthWrite: false, clippingPlanes: this.cutPlanes });
    this.collideWire = new THREE.MeshBasicMaterial({ color: 0xffb070, wireframe: true, transparent: true, opacity: 0.35, clippingPlanes: this.cutPlanes });

    const button = (key, label, title) => {
      const b = document.createElement("button");
      b.type = "button"; b.textContent = label; b.title = title;
      b.onclick = () => this.toggle(key, b);
      this.mark(b, this.show[key]);
      return b;
    };
    this.box = document.createElement("div");
    this.box.className = "maplayers map3dworld";
    this.box.hidden = true;
    this.box.append(...KINDS.map(([key, label, title]) => button(key, label, title)),
      button("collision", "Collision", "What the player collides with, invisible walls included"));
    if (controls) controls.append(this.box);
    this.status = document.createElement("span");
    this.status.className = "map3dworldnote";
    this.box.append(this.status);

    worldMeta(true).then(m => this.use(m));
  }

  use(m) {
    if (!m || this.disposed) return;
    this.meta = m; this.v = "?v=" + (m.build || 0); this.box.hidden = false; this.next = 0;
    this.kinds = m.models.map(model => kindOf(m, model));
    this.chunkOf = new Map(m.chunks.map(c => [c.file, c]));
    this.changed();
  }

  /// A file refused (404): the world may have been uploaded again while this
  /// page was open. Read world.json again (at most every 30 s - a file really
  /// missing would ask on every chunk) and, if it changed, start over with it.
  stale() {
    const now = performance.now();
    if (this.disposed || now < this.staleAt) return;
    this.staleAt = now + 30000;
    worldMeta(true).then(m => {
      if (!m || this.disposed || !this.meta || m.build === this.meta.build) return;
      this.gen++;
      for (const [, mesh] of this.drawn) { this.group.remove(mesh); mesh.dispose(); }
      for (const p of this.geoms.values()) p.then(g => g && g.dispose());
      for (const t of this.textures.values()) t.dispose();
      for (const x of this.mats.values()) x.dispose();
      for (const x of [this.drawn, this.geoms, this.geomsDone, this.packs, this.texPacks, this.files, this.textures, this.mats, this.waterMats, this.chunks, this.dirtyModels]) x.clear();
      this.use(m);
    });
  }

  mark(b, on) { b.classList.toggle("on", on); b.setAttribute("aria-pressed", on); }

  toggle(key, b) {
    this.show[key] = !this.show[key];
    this.mark(b, this.show[key]);
    try { localStorage.setItem("forest.world3d", JSON.stringify(this.show)); } catch (e) { /* not kept */ }
    for (const [mi, mesh] of this.drawn) mesh.visible = this.wanted(mi);
    // Collision meshes are fetched when first shown (fetched(), rebuild()).
    if (key === "collision" && this.show.collision)
      for (const [, c] of this.chunks) if (c.state === "ready")
        for (const [mi] of c.items) if (this.meta.models[mi].kind === "collide" && !this.drawn.has(mi)) this.dirtyModels.add(mi);
    this.next = 0;
    this.changed();
  }

  /// Is model mi's mesh worth fetching now? Collision only while its switch is
  /// on (2026-10-02: every collider was fetched and built, hidden).
  fetched(mi) { return this.meta.models[mi].kind !== "collide" || this.show.collision; }

  /// Is model mi switched on? (its kind's switch)
  wanted(mi) { return !!this.show[this.kinds[mi] || "props"]; }

  /// Any switch on: chunks are worth loading.
  anyShown() { return KINDS.some(([k]) => this.show[k]) || this.show.collision; }

  /// The terrain's heights (terrain.json's meta + heights.u16), for the lakes' clip.
  /// holes: the terrain's holes (map3d.js terrainHoles) - no ground there,
  /// so the sinkhole's water (y -304) is not clipped by the heights' y 0.
  setGround(m, heights, holes) {
    const n = m.grid, f = new Float32Array(n * n), k = m.sizeY / 65535;
    for (let i = 0; i < f.length; i++) f[i] = holes && holes[i] ? -1e9 : m.y0 + heights[i] * k;
    const t = new THREE.DataTexture(f, n, n, THREE.RedFormat, THREE.FloatType);
    t.minFilter = t.magFilter = THREE.NearestFilter;
    t.needsUpdate = true;
    if (this.ground.groundMap.value) this.ground.groundMap.value.dispose();
    this.ground.groundMap.value = t;
    this.ground.groundRect.value.set(m.x0, m.z0, m.sizeX, m.sizeZ);
    this.ground.groundOn.value = 1;
    this.grid = groundGrid(m, f, holes);
    // Instances already drawn learn what is under the ground.
    for (const [, mesh] of this.drawn) if (mesh.userData.far) this.markUnder(mesh.userData.far);
    this.splitAt = null; this.next = 0;
    this.changed();
  }

  /// far.under[i] = 1: instance i is wholly below the terrain (underGround).
  markUnder(far) {
    const { spheres, n, under } = far, g = this.grid;
    for (let i = 0; i < n; i++) under[i] = g && underGround(g, spheres[i * 5], spheres[i * 5 + 1], spheres[i * 5 + 2], spheres[i * 5 + 3]) ? 1 : 0;
    far.level.fill(255);
  }

  /// Water off: every lake's surface hidden (the sea is map3d.js's).
  setWater(on) {
    if (on === this.water) return;
    this.water = on;
    for (const m of this.waterMats) m.visible = on;
    this.changed();
  }

  /// Surface models fade with the terrain when the view is underground.
  setFade(f) {
    if (f === this.fade) return;
    if ((f > 0.99) !== (this.fade > 0.99)) { this.splitAt = null; this.next = 0; }   // culling under the ground on / off
    this.fade = f;
    for (const [mi, mesh] of this.drawn) this.applyFade(mi, mesh);
  }

  applyFade(mi, mesh) {
    const surface = mesh.userData.surface;
    for (const m of [].concat(mesh.material)) {
      if (m === this.collideMat || m === this.collideWire) continue;
      const o = (surface ? this.fade : 1) * (m.userData.opacity ?? 1);
      m.transparent = o < 0.99; m.opacity = o; m.depthWrite = o > 0.99;
    }
  }

  /// Underground, everything between the camera and what it looks at (less
  /// CUT_MARGIN) is cut away: the orbit / follow camera over a cave sits in
  /// the rock around it, and the cave's pieces are closed rocks (boulders
  /// placed as walls, not one-sided shells), so it would see only their
  /// outsides. eye / target: three.js space. The renderer needs
  /// localClippingEnabled.
  setCut(on, eye, target) {
    const p = this.cutPlanes[0], n = this.cutNormal.subVectors(target, eye);
    const d = n.length();
    if (!on || d < 0.01) { p.normal.set(0, 1, 0); p.constant = 1e9; return; }
    n.divideScalar(d);
    p.normal.copy(n);
    p.constant = Math.min(CUT_MARGIN, d * 0.5) - n.dot(target);   // kept: beyond target - n * margin
  }

  /// The first solid thing on the line from `from` towards `to` (three.js
  /// space): its distance from `from`, or Infinity. Solid = a drawn rock or
  /// prop with no cut-out material, hit on a face that looks at `from` (the
  /// side the game draws). Follow's camera is pulled in front of it, so a
  /// cave wall between the ghost and the camera never covers the view. Tests
  /// each model's instances by bounding sphere, then the full mesh's
  /// triangles of the few the line passes through, by groups (hitGroups()):
  /// a cave piece is ~20k triangles, and testing them all cost ~13 ms.
  firstHit(from, to) {
    const dir = this.hitDir.subVectors(to, from), len = dir.length();
    if (!this.meta || len < 0.01) return Infinity;
    dir.divideScalar(len);
    let best = len;
    for (const [mi, holder] of this.drawn) {
      const far = holder.userData.far;
      if (!far || !holder.visible || !this.solid(mi)) continue;
      const b = far.box;
      if (Math.max(from.x, to.x) < b[0] || Math.min(from.x, to.x) > b[3] || Math.max(from.y, to.y) < b[1]
        || Math.min(from.y, to.y) > b[4] || Math.max(from.z, to.z) < b[2] || Math.min(from.z, to.z) > b[5]) continue;
      const { spheres, mats, n } = far, geo = far.levels[0].mesh.geometry;
      for (let i = 0; i < n; i++) {
        const o = i * 5, r = spheres[o + 3];
        const cx = spheres[o] - from.x, cy = spheres[o + 1] - from.y, cz = spheres[o + 2] - from.z;
        const t = Math.max(0, Math.min(best, cx * dir.x + cy * dir.y + cz * dir.z));
        const px = cx - dir.x * t, py = cy - dir.y * t, pz = cz - dir.z * t;
        if (px * px + py * py + pz * pz > r * r) continue;
        best = this.hitInstance(geo, this.hitM.fromArray(mats, i * 16), from, dir, best);
      }
    }
    return best < len ? best : Infinity;
  }

  /// The nearest front face of one instance (matrix m) on the line from
  /// `from` along dir closer than best: its distance, or best. The affine
  /// map keeps ratios along the line, so the local segment's fraction is the
  /// world one.
  hitInstance(geo, m, from, dir, best) {
    const grp = this.hitGroups(geo);
    if (!grp) return best;
    const inv = this.hitInv.copy(m).invert();
    const o = this.hitO.copy(from).applyMatrix4(inv);
    const d = this.hitE.copy(dir).multiplyScalar(best).add(from).applyMatrix4(inv).sub(o);   // local segment, t in [0, 1]
    const ray = this.hitRay.ray;
    ray.origin.copy(o); ray.direction.copy(d);
    const dd = d.lengthSq(), pos = geo.attributes.position, idx = geo.index, boxes = grp.boxes, tris = grp.tris;
    let tBest = 1, found = -1;
    for (let g = 0; g < grp.count; g++) {
      if (!segmentBox(o, d, tBest, boxes, g * 6)) continue;
      const end = Math.min(tris, (g + 1) * HIT_GROUP);
      for (let k = g * HIT_GROUP; k < end; k++) {
        const a = idx ? idx.getX(k * 3) : k * 3, b = idx ? idx.getX(k * 3 + 1) : k * 3 + 1, c = idx ? idx.getX(k * 3 + 2) : k * 3 + 2;
        const p = ray.intersectTriangle(this.hitA.fromBufferAttribute(pos, a), this.hitB.fromBufferAttribute(pos, b),
          this.hitC.fromBufferAttribute(pos, c), false, this.hitP);
        if (!p) continue;
        const t = p.sub(o).dot(d) / dd;
        if (t < 0 || t >= tBest || !this.facing(pos, m, a, b, c, dir)) continue;
        tBest = t; found = k;
      }
    }
    return found < 0 ? best : tBest * best;
  }

  /// Triangles in groups of HIT_GROUP (index order: neighbours in a mesh),
  /// each with its local bounding box - a one-level tree, made once per mesh.
  hitGroups(geo) {
    let grp = this.hitCache.get(geo);
    if (grp !== undefined) return grp;
    const pos = geo.attributes.position, idx = geo.index;
    if (!pos) { this.hitCache.set(geo, null); return null; }
    const tris = Math.floor((idx ? idx.count : pos.count) / 3), count = Math.ceil(tris / HIT_GROUP);
    const boxes = new Float32Array(count * 6);
    for (let g = 0; g < count; g++) {
      const b = g * 6;
      boxes[b] = boxes[b + 1] = boxes[b + 2] = Infinity;
      boxes[b + 3] = boxes[b + 4] = boxes[b + 5] = -Infinity;
      const end = Math.min(tris * 3, (g + 1) * HIT_GROUP * 3);
      for (let j = g * HIT_GROUP * 3; j < end; j++) {
        const v = idx ? idx.getX(j) : j;
        const x = pos.getX(v), y = pos.getY(v), z = pos.getZ(v);
        if (x < boxes[b]) boxes[b] = x; if (x > boxes[b + 3]) boxes[b + 3] = x;
        if (y < boxes[b + 1]) boxes[b + 1] = y; if (y > boxes[b + 4]) boxes[b + 4] = y;
        if (z < boxes[b + 2]) boxes[b + 2] = z; if (z > boxes[b + 5]) boxes[b + 5] = z;
      }
    }
    grp = { boxes, count, tris };
    this.hitCache.set(geo, grp);
    return grp;
  }

  /// Does the face a, b, c, as drawn (front faces counter-clockwise in world
  /// space - the instances' matrices mirror, geometry() turned the indices),
  /// look back along dir?
  facing(pos, m, a, b, c, dir) {
    const A = this.hitF0.fromBufferAttribute(pos, a).applyMatrix4(m);
    const B = this.hitF1.fromBufferAttribute(pos, b).applyMatrix4(m).sub(A);
    const C = this.hitF2.fromBufferAttribute(pos, c).applyMatrix4(m).sub(A);
    return B.cross(C).dot(dir) < 0;
  }

  /// A model the camera cannot see through: a rock or a prop, no cut-outs.
  solid(mi) {
    const kind = this.kinds[mi];
    if (kind !== "rocks" && kind !== "props") return false;
    return this.meta.models[mi].mats.every(x => !(x >= 0 && this.meta.materials[x].cut));
  }

  /// Each frame: which chunks to have, given the camera's target (a Vector3
  /// in three.js space), and which copy each instance is drawn with, given
  /// the camera (eye, three.js space) and its focal length in drawing buffer
  /// pixels (focal), and which are drawn at all, given its forward direction
  /// (look, unit) and the half angle to the screen's corner (corner, radians).
  /// Rate-limited, except that turning CULL_TURN picks again at once; cheap
  /// when nothing changes.
  update(target, now, eye, focal, look, corner, near) {
    if (!this.meta || this.disposed) return;
    const turned = look && this.splitAt && look.dot(this.lookAt) < Math.cos(CULL_TURN);
    if (now < this.next && !turned) return;
    this.next = now + 400;
    if (eye) { this.eye.copy(eye); this.focal = focal; }
    if (look) { this.look.copy(look); this.cone = Math.min(Math.PI, corner + CULL_MARGIN); }
    const s = this.splitAt;
    // The near plane's corners reach near / cos(corner) from the camera: all of them above the terrain.
    const reach = (near || 5) / Math.cos(corner || 0.9);
    const hide = this.fade > 0.99 && !!this.grid && aboveGround(this.grid, this.eye.x, this.eye.y, this.eye.z, reach);
    if (!s || turned || this.focal !== s[3] || hide !== this.hideUnder || Math.hypot(this.eye.x - s[0], this.eye.y - s[1], this.eye.z - s[2]) > SPLIT_MOVE) {
      this.hideUnder = hide;
      this.splitAt = [this.eye.x, this.eye.y, this.eye.z, this.focal];
      this.lookAt.copy(this.look);
      for (const [, mesh] of this.drawn) if (mesh.userData.far) this.split(mesh);
      this.changed();
    }
    const x = target.x, z = -target.z, size = this.meta.chunk;
    const want = new Set();
    let loading = 0;
    if (this.anyShown()) for (const c of this.meta.chunks) {
      // "bb": where the chunk's instances really reach (a cave ground spans
      // hundreds of metres); older exports: the 250 m column.
      const b = c.bb || [c.x, c.z, c.x + size, c.z + size];
      const dx = Math.max(b[0] - x, 0, x - b[2]), dz = Math.max(b[1] - z, 0, z - b[3]);
      const d = Math.hypot(dx, dz);
      if (d < RADIUS) want.add(c.file);
      if (d < DROP && this.chunks.has(c.file)) want.add(c.file);
    }
    for (const file of want) if (!this.chunks.has(file)) this.load(file);
    for (const [file, c] of this.chunks) {
      if (c.state === "loading") loading++;
      if (!want.has(file) && c.state === "ready") { this.chunks.delete(file); for (const [mi] of c.items) this.dirtyModels.add(mi); }
    }
    if (this.dirtyModels.size) this.rebuild();
    this.status.textContent = loading ? "loading " + loading + "…" : this.fetching ? "loading…" : "";
  }

  async load(file) {
    const entry = { state: "loading", items: [] };
    this.chunks.set(file, entry);
    try {
      // Its own file, or (version 3) its place in a shared one.
      const at = this.meta.files && this.chunkOf.get(file).at;
      const found = at ? await this.place(at) : await this.bytes(file).then(b => b && [b, 0, b.byteLength]);
      if (!found) throw new Error("missing");
      if (this.disposed || this.chunks.get(file) !== entry) return;
      const [buf, off, len] = found;
      const n = len / 52, u = new Uint32Array(buf, off, n * 13), f = new Float32Array(buf, off, n * 13);
      for (let i = 0; i < n; i++) {
        const o = i * 13, m = new THREE.Matrix4();
        m.set(f[o + 1], f[o + 2], f[o + 3], f[o + 4],
              f[o + 5], f[o + 6], f[o + 7], f[o + 8],
              f[o + 9], f[o + 10], f[o + 11], f[o + 12],
              0, 0, 0, 1);
        entry.items.push([u[o], S.clone().multiply(m)]);
      }
      // The models' meshes first, so the chunk appears whole.
      const models = new Set(entry.items.map(it => it[0]).filter(mi => this.meta.models[mi]));
      await Promise.all([...models].filter(mi => this.fetched(mi)).flatMap(mi => this.meshesOf(this.meta.models[mi]).map(gi => this.geometry(gi))));
      if (this.disposed || this.chunks.get(file) !== entry) return;
      entry.state = "ready";
      for (const mi of models) this.dirtyModels.add(mi);
      this.next = 0;
      this.changed();
    } catch (e) {
      entry.state = "failed";
    }
  }

  /// A world file's bytes, or null (refused: maybe another upload - stale()).
  bytes(file) {
    return fetch("/world/" + file + this.v).then(r => {
      if (r.status === 404) this.stale();
      return r.ok ? r.arrayBuffer() : null;
    });
  }

  /// A pack's bytes, fetched once for all its meshes.
  pack(i) {
    if (!this.packs.has(i)) this.packs.set(i, this.bytes(this.meta.packs[i]).catch(() => null));
    return this.packs.get(i);
  }

  /// A version 3 file's bytes, fetched once for all its blobs.
  file(i) {
    if (!this.files.has(i)) this.files.set(i, this.bytes(this.meta.files[i]).catch(() => null));
    return this.files.get(i);
  }

  /// A blob's [buffer, offset, length] (version 3).
  place(at) {
    return this.file(at[0]).then(b => b && [b, at[1], at[2]]);
  }

  /// The meshes a model draws with: its own, then its far copies (lightest
  /// last) - none for collision, or with far copies off.
  meshesOf(model) {
    const far = this.meta.meshes[model.mesh].far;
    return far && this.farPixels > 0 && model.kind !== "collide" ? [model.mesh, ...far] : [model.mesh];
  }

  /// Far copies on (pixels: FAR_PIXELS) or off (0, full meshes only): every
  /// model drawn again. For comparing the two (site-measure.py NOFAR).
  setFar(pixels) {
    this.farPixels = pixels;
    for (const [, c] of this.chunks) if (c.state === "ready") for (const [mi] of c.items) this.dirtyModels.add(mi);
    this.splitAt = null;
    this.next = 0;
    this.changed();
  }

  geometry(index) {
    if (this.geoms.has(index)) return this.geoms.get(index);
    const m = this.meta.meshes[index];
    // [buffer, where the mesh starts in it]: its place (version 3), its
    // pack's (version 2; offsets 4-byte aligned, the arrays are views) or
    // its own file.
    const src = this.meta.files ? (m.at ? this.place(m.at) : Promise.resolve(null))
      : !this.meta.packs ? this.bytes("m/" + index + ".bin").then(b => b && [b, 0])
      : m.pack ? this.pack(m.pack[0]).then(b => b && [b, m.pack[1]]) : Promise.resolve(null);
    // A shared far copy is only indices, over its full mesh's vertices (and
    // their normals): the full mesh's attributes, uploaded once.
    const full = m.shared ? this.geometry(m.of) : Promise.resolve(null);
    const p = Promise.all([src, full]).then(([found, shared]) => {
      if (!found || (m.shared && !shared)) return null;
      const [buf, base] = found;
      const g = new THREE.BufferGeometry();
      let at = base;
      if (shared) for (const k of ["position", "uv", "normal"]) { if (shared.attributes[k]) g.setAttribute(k, shared.attributes[k]); }
      else {
        g.setAttribute("position", new THREE.BufferAttribute(new Float32Array(buf, at, m.nv * 3), 3));
        at += m.nv * 12;
        if (m.uv) { g.setAttribute("uv", new THREE.BufferAttribute(new Float32Array(buf, at, m.nv * 2), 2)); at += m.nv * 8; }
        // A far copy carries its full mesh's normals: it shades as the full one does.
        if (m.n) { g.setAttribute("normal", new THREE.BufferAttribute(new Float32Array(buf, at, m.nv * 3), 3)); at += m.nv * 12; }
      }
      const idx = m.i32 ? new Uint32Array(buf.slice(at, at + m.ni * 4)) : new Uint16Array(buf.slice(at, at + m.ni * 2));
      // Unity winds front faces clockwise: turned, so the normals face out.
      for (let i = 0; i < idx.length; i += 3) { const t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
      g.setIndex(new THREE.BufferAttribute(idx, 1));
      m.sub.forEach(([start, count], i) => g.addGroup(start, count, i));
      if (!m.n && !shared) g.computeVertexNormals();
      g.computeBoundingSphere();
      return g;
    }).catch(() => null);
    this.geoms.set(index, p);
    const gen = this.gen;
    p.then(() => { if (gen === this.gen) this.geomsDone.add(index); });
    return p;
  }

  /// surface: the model is in a surface chunk (a lake there is clipped to the ground).
  material(index, surface) {
    const d = index >= 0 ? this.meta.materials[index] : null;
    // The lakes' surfaces (shaders "The Forest/Water" / "WaterCave": no texture, a 0.7 grey
    // colour - flat grey sheets over the lakes; author, 2026-10-01) are drawn
    // as the water the photo shows there.
    if (d && /\/Water(Cave)?$/.test(d.shader || "")) {
      const key = surface ? index + "s" : index;
      if (this.mats.has(key)) return this.mats.get(key);
      const water = new THREE.MeshLambertMaterial({ color: new THREE.Color(0x0e2a33), transparent: true, opacity: 0.85,
        depthWrite: false, clippingPlanes: this.cutPlanes });
      water.userData.opacity = 0.85;
      water.visible = this.water;
      if (surface) groundClip(water, this.ground);
      this.waterMats.add(water);
      this.mats.set(key, water);
      return water;
    }
    // The black plane the game lays under a lake's water (its dark depth):
    // part of the water - clipped to the shore with it, off with it (a
    // black lake with the Water button off; 2026-10-01).
    const bed = !!(d && /^LakeFake/.test(d.name || ""));
    const key = bed && surface ? index + "s" : index;
    if (this.mats.has(key)) return this.mats.get(key);
    const c = d ? d.color : [0.7, 0.7, 0.7, 1];
    const mat = new THREE.MeshLambertMaterial({ color: new THREE.Color(c[0], c[1], c[2]).convertSRGBToLinear(), side: d && d.cut ? THREE.DoubleSide : THREE.FrontSide,
      clippingPlanes: this.cutPlanes });
    if (d && d.tex >= 0) mat.map = this.texture(d.tex, d.cut, d.scale);   // scale: the material's tiling
    if (d && d.cut) mat.alphaTest = 0.5;   // leaves, grass, fences: cut out by the texture's alpha
    if (d && d.glass !== undefined) {      // windows, glass walls: see-through, both sides
      mat.transparent = true;
      mat.opacity = Math.min(Math.max(d.glass, 0.1), 0.6);
      mat.depthWrite = false;
      mat.side = THREE.DoubleSide;
    }
    if (d && d.top >= 0 && mat.map) topLayer(mat, this.texture(d.top), d.topScale || 1);   // needs the main UVs
    if (bed) {
      if (surface) groundClip(mat, this.ground);
      mat.visible = this.water;
      this.waterMats.add(mat);
    }
    this.mats.set(key, mat);
    return mat;
  }

  /// A texture pack's bytes, fetched once for all its textures.
  texPack(i) {
    if (!this.texPacks.has(i)) this.texPacks.set(i, this.bytes(this.meta.texpacks[i]).catch(() => null));
    return this.texPacks.get(i);
  }

  texture(i, alpha, scale) {
    const key = (alpha ? i + "a" : "" + i) + (scale ? "x" + scale.join(",") : "");
    let t = this.textures.get(key);
    if (!t) {
      const e = this.meta.textures && this.meta.textures[i];
      if (e && this.meta.files) {
        // Version 3: [jpg place, png place] - the png (alpha) when there is one.
        const png = !!(alpha && e[1]), at = png ? e[1] : e[0] || e[1];
        t = new THREE.Texture();
        if (at) {
          const gen = this.gen;
          this.place(at).then(found => {
            if (!found || gen !== this.gen || this.disposed) return;
            const [buf, off, len] = found;
            const url = URL.createObjectURL(new Blob([new Uint8Array(buf, off, len)], { type: at === e[1] ? "image/png" : "image/jpeg" }));
            const img = new Image();
            img.onload = () => { URL.revokeObjectURL(url); t.image = img; t.needsUpdate = true; this.changed(); };
            img.onerror = () => URL.revokeObjectURL(url);
            img.src = url;
          });
        }
      } else if (e) {
        // From its pack: the png (alpha) when there is one, else the jpg.
        const png = alpha && e[3] >= 0, s = png ? 3 : 0;
        t = new THREE.Texture();
        if (e[s] >= 0) {
          const gen = this.gen;
          this.texPack(e[s]).then(buf => {
            if (!buf || gen !== this.gen || this.disposed) return;
            const url = URL.createObjectURL(new Blob([new Uint8Array(buf, e[s + 1], e[s + 2])], { type: png ? "image/png" : "image/jpeg" }));
            const img = new Image();
            img.onload = () => { URL.revokeObjectURL(url); t.image = img; t.needsUpdate = true; this.changed(); };
            img.onerror = () => URL.revokeObjectURL(url);
            img.src = url;
          });
        }
      } else t = new THREE.TextureLoader().load("/world/t/" + i + (alpha ? ".png" : ".jpg") + this.v, () => this.changed(), undefined, () => this.stale());
      t.colorSpace = THREE.SRGBColorSpace;
      t.wrapS = t.wrapT = THREE.RepeatWrapping;
      if (scale) t.repeat.set(scale[0], scale[1]);
      this.textures.set(key, t);
    }
    return t;
  }

  /// A lake of the surface wherever the export filed it: the export chunks a
  /// model by its origin, and the middle's lakes (BigLake_v2, one plane at
  /// y 48.4 with its origin at 0, 0), a stream, the GeeseLakes and the
  /// sinkhole's pool are filed as caves (gotcha 70) - then unclipped over
  /// the shore and not faded underground. A "The Forest/Water" surface
  /// (not the caves' WaterCave) or a LakeFake bed counts as the surface's;
  /// the sinkhole's pool stays with its floor and cliffs (filed as caves,
  /// not faded; in a terrain hole, so never clipped).
  surfaceWater(mi) {
    const model = this.meta.models[mi];
    return !!model && model.kind !== "collide" && model.mats.some(x => {
      const d = x >= 0 ? this.meta.materials[x] : null;
      return !!d && ((d.shader === "The Forest/Water" && d.name !== "SinkholeWater") || /^LakeFake/.test(d.name || ""));
    });
  }

  /// One InstancedMesh per model over every ready chunk.
  async rebuild() {
    const models = [...this.dirtyModels], gen = this.gen;
    this.dirtyModels.clear();
    const by = new Map(models.map(mi => [mi, []]));
    const surface = new Map();
    for (const [file, c] of this.chunks) {
      if (c.state !== "ready") continue;
      for (const [mi, m] of c.items) if (by.has(mi)) { by.get(mi).push(m); if (file.startsWith("c/surface")) surface.set(mi, true); }
    }
    for (const mi of models) if (this.surfaceWater(mi)) surface.set(mi, true);
    for (const mi of models) {
      const old = this.drawn.get(mi);
      if (old) { this.group.remove(old); old.dispose(); this.drawn.delete(mi); }
      const list = by.get(mi), model = this.meta.models[mi];
      if (!list.length || !model) continue;
      // Glints and particles (the pickups' sheen): not a solid thing.
      if (model.mats.length && model.mats.every(x => x >= 0 && this.meta.materials[x].fx)) continue;
      if (!this.fetched(mi)) continue;     // collision, switched off: built when switched on
      const gis = this.meshesOf(model), missing = gis.filter(gi => !this.geomsDone.has(gi));
      if (missing.length) {
        // Not here yet (collision just switched on, far copies switched on):
        // fetched, then built by a later rebuild - never awaited here, where
        // a second rebuild of the same model could finish first and be dropped.
        this.fetching++;
        Promise.all(missing.map(gi => this.geometry(gi))).then(() => {
          this.fetching--;
          if (gen === this.gen && !this.disposed) { this.dirtyModels.add(mi); this.next = 0; this.changed(); }
        });
        continue;
      }
      const gs = await Promise.all(gis.map(gi => this.geometry(gi)));
      if (gen !== this.gen) return;      // started over meanwhile (stale())
      const g = gs[0];
      if (!g || this.disposed || this.drawn.has(mi)) continue;
      let mat;
      if (model.kind === "collide") mat = [this.collideMat, this.collideWire];
      else {
        const sf = surface.has(mi);
        mat = model.mats.length > 1 ? model.mats.map(x => this.material(x, sf)) : this.material(model.mats[0] ?? -1, sf);
      }
      // Every rendered model goes through split(): one without far copies is
      // still culled per instance (its draw call gone when none is in view).
      const copies = gs.slice(1).every(x => x) ? gis.slice(1).map((gi, k) => [gs[k + 1], this.meta.meshes[gi].e]) : [];
      const mesh = model.kind === "collide" ? this.collisionMesh(g, list) : this.farMesh(g, copies, mat, list);
      mesh.userData.surface = surface.has(mi);
      mesh.visible = this.wanted(mi);
      this.applyFade(mi, mesh);
      this.group.add(mesh);
      this.drawn.set(mi, mesh);
    }
    this.changed();
  }

  /// A rendered model: one InstancedMesh per mesh (full, then the far copies,
  /// if any), each able to hold every instance; split() deals them out, and
  /// each culls by a sphere round its own instances (the full mesh's are the
  /// near ones: off screen behind the camera, not drawn).
  /// copies: [[geometry, error], ...] lightest last; none = culling only.
  farMesh(g, copies, mat, list) {
    const holder = new THREE.Group(), n = list.length;
    const mats = new Float32Array(n * 16), spheres = new Float32Array(n * 5);
    const bs = g.boundingSphere, c = new THREE.Vector3();
    const box = [Infinity, Infinity, Infinity, -Infinity, -Infinity, -Infinity];   // every instance's (firstHit())
    list.forEach((m, i) => {
      mats.set(m.elements, i * 16);
      const e = m.elements, s = Math.sqrt(Math.max(e[0] * e[0] + e[1] * e[1] + e[2] * e[2],
        e[4] * e[4] + e[5] * e[5] + e[6] * e[6], e[8] * e[8] + e[9] * e[9] + e[10] * e[10]));
      c.copy(bs.center).applyMatrix4(m);
      spheres.set([c.x, c.y, c.z, bs.radius * s, s], i * 5);
      for (let j = 0; j < 3; j++) {
        box[j] = Math.min(box[j], c.getComponent(j) - bs.radius * s);
        box[j + 3] = Math.max(box[j + 3], c.getComponent(j) + bs.radius * s);
      }
    });
    const levels = [[g, 0], ...copies].map(([geo, err]) => {
      const mesh = new THREE.InstancedMesh(geo, mat, n);
      mesh.boundingSphere = new THREE.Sphere();
      holder.add(mesh);
      return { mesh, err };
    });
    holder.material = mat;
    holder.userData.far = { levels, mats, spheres, box, n, level: new Uint8Array(n).fill(255), under: new Uint8Array(n) };   // 255: never picked
    this.markUnder(holder.userData.far);
    holder.dispose = () => { for (const ch of holder.children) ch.dispose(); };
    this.split(holder);
    return holder;
  }

  /// Each instance of a far model to the lightest mesh whose error, at the
  /// instance's scale s and distance d from the camera (to its bounding
  /// sphere), stays under farPixels: err * s * focal / d <= farPixels.
  /// An instance whose bounding sphere is outside the view cone (this.cone
  /// round this.look), or wholly under the ground seen from above it
  /// (hideUnder, under, throughHole), is drawn by none of them.
  /// Only a model whose instances changed mesh is written and sent again
  /// (only the used part of each buffer): the camera moving a few metres
  /// moves few instances, and a split of every far instance in view costs
  /// ~1 ms.
  split(holder) {
    const { levels, mats, spheres, n, level, under } = holder.userData.far, last = levels.length - 1;
    const k = this.farPixels > 0 && this.focal > 0 ? this.focal / this.farPixels : Infinity;
    const ex = this.eye.x, ey = this.eye.y, ez = this.eye.z;
    const lx = this.look.x, ly = this.look.y, lz = this.look.z, cone = this.cone, cull = cone > 0 && cone < Math.PI;
    const hide = this.hideUnder && this.grid;
    let changed = false;
    for (let i = 0; i < n; i++) {
      const o = i * 5, dx = spheres[o] - ex, dy = spheres[o + 1] - ey, dz = spheres[o + 2] - ez;
      const d = Math.sqrt(dx * dx + dy * dy + dz * dz), r = spheres[o + 3];
      let L = 0;
      if (cull && d > r && Math.acos(Math.max(-1, Math.min(1, (dx * lx + dy * ly + dz * lz) / d))) > cone + Math.asin(r / d)) L = CULLED;
      else if (hide && under[i] && !throughHole(hide, ex, ey, ez, spheres[o], spheres[o + 1], spheres[o + 2], r)) L = CULLED;
      else {
        const allowed = Math.max(0, d - r) / (spheres[o + 4] * k);
        while (L < last && levels[L + 1].err <= allowed) L++;
      }
      if (level[i] !== L) { level[i] = L; changed = true; }
    }
    if (!changed) return;
    const counts = new Array(levels.length).fill(0), arrays = levels.map(l => l.mesh.instanceMatrix.array);
    const box = levels.map(() => [Infinity, Infinity, Infinity, -Infinity, -Infinity, -Infinity]);
    for (let i = 0; i < n; i++) {
      const L = level[i];
      if (L === CULLED) continue;
      const a = arrays[L], to = counts[L]++ * 16, from = i * 16;
      for (let j = 0; j < 16; j++) a[to + j] = mats[from + j];
      const b = box[L], o = i * 5, r = spheres[o + 3];
      for (let j = 0; j < 3; j++) {
        b[j] = Math.min(b[j], spheres[o + j] - r);
        b[j + 3] = Math.max(b[j + 3], spheres[o + j] + r);
      }
    }
    levels.forEach(({ mesh }, L) => {
      const b = box[L];
      mesh.boundingSphere.center.set((b[0] + b[3]) / 2, (b[1] + b[4]) / 2, (b[2] + b[5]) / 2);
      mesh.boundingSphere.radius = Math.hypot(b[3] - b[0], b[4] - b[1], b[5] - b[2]) / 2;
      mesh.count = counts[L];
      mesh.visible = counts[L] > 0;
      const im = mesh.instanceMatrix;
      im.clearUpdateRanges();
      im.addUpdateRange(0, counts[L] * 16);
      im.needsUpdate = true;
    });
  }

  /// Collision: see-through orange with its wireframe over it.
  collisionMesh(g, list) {
    const holder = new THREE.Group();
    for (const mat of [this.collideMat, this.collideWire]) {
      const m = new THREE.InstancedMesh(g, mat, list.length);
      list.forEach((x, i) => m.setMatrixAt(i, x));
      m.instanceMatrix.needsUpdate = true;
      m.computeBoundingSphere();
      m.renderOrder = 3;
      holder.add(m);
    }
    holder.material = [];
    holder.dispose = () => { for (const c of holder.children) c.dispose(); };
    return holder;
  }

  dispose() {
    this.disposed = true;
    this.box.remove();
    for (const [, mesh] of this.drawn) mesh.dispose();
    for (const p of this.geoms.values()) p.then(g => g && g.dispose());
    for (const t of this.textures.values()) t.dispose();
    for (const m of this.mats.values()) m.dispose();
    this.packs.clear();
    this.texPacks.clear();
    this.files.clear();
    this.collideMat.dispose(); this.collideWire.dispose();
    if (this.ground.groundMap.value) this.ground.groundMap.value.dispose();
    this.scene.remove(this.group);
  }
}

/// The terrain's heights (Unity y): every sample, the lowest of each
/// GROUND_BLOCK x GROUND_BLOCK block, and the blocks holding a hole (as
/// Unity x / z boxes + the highest sample in each) - what underGround /
/// aboveGround / throughHole read.
/// f: every sample's height, -1e9 on a hole (setGround).
function groundGrid(m, f, holes) {
  const n = m.grid, B = GROUND_BLOCK, nb = Math.ceil(n / B);
  const lo = new Float32Array(nb * nb).fill(Infinity);
  const cx = m.sizeX / (n - 1), cz = m.sizeZ / (n - 1), holeBoxes = [];
  for (let j = 0; j < n; j++) for (let i = 0; i < n; i++) {
    const b = Math.floor(j / B) * nb + Math.floor(i / B), y = f[j * n + i];
    if (y < lo[b]) lo[b] = y;
  }
  if (holes) for (let bj = 0; bj < nb; bj++) for (let bi = 0; bi < nb; bi++) {
    let any = false;
    for (let j = bj * B; j < Math.min(n, bj * B + B) && !any; j++)
      for (let i = bi * B; i < Math.min(n, bi * B + B); i++) if (holes[j * n + i]) { any = true; break; }
    // The drawn terrain leaves out the cells touching a hole: the box reaches COARSE_MAX + 1 samples further.
    const e = COARSE_MAX + 1;
    if (!any) continue;
    let hi = -Infinity;
    for (let j = Math.max(0, bj * B - e); j <= Math.min(n - 1, bj * B + B - 1 + e); j++)
      for (let i = Math.max(0, bi * B - e); i <= Math.min(n - 1, bi * B + B - 1 + e); i++) hi = Math.max(hi, f[j * n + i]);
    holeBoxes.push([m.x0 + (bi * B - e) * cx, m.z0 + (bj * B - e) * cz, m.x0 + (bi * B + B - 1 + e) * cx, m.z0 + (bj * B + B - 1 + e) * cz, hi]);
  }
  return { x0: m.x0, z0: m.z0, cx, cz, n, nb, f, lo, holeBoxes };
}

/// The blocks covering Unity x / z +- r, widened by COARSE_MAX samples:
/// [bi0, bj0, bi1, bj1], or null when it reaches outside the terrain.
function blocks(g, x, z, r) {
  const i0 = Math.floor((x - r - g.x0) / g.cx) - COARSE_MAX, i1 = Math.ceil((x + r - g.x0) / g.cx) + COARSE_MAX;
  const j0 = Math.floor((z - r - g.z0) / g.cz) - COARSE_MAX, j1 = Math.ceil((z + r - g.z0) / g.cz) + COARSE_MAX;
  if (i0 < 0 || j0 < 0 || i1 > g.n - 1 || j1 > g.n - 1) return null;
  return [Math.floor(i0 / GROUND_BLOCK), Math.floor(j0 / GROUND_BLOCK), Math.floor(i1 / GROUND_BLOCK), Math.floor(j1 / GROUND_BLOCK)];
}

/// A sphere (three.js space) wholly below the drawn terrain over it.
function underGround(g, x, y, z, r) {
  const b = blocks(g, x, -z, r);
  if (!b) return false;
  let low = Infinity;
  for (let bj = b[1]; bj <= b[3]; bj++) for (let bi = b[0]; bi <= b[2]; bi++) low = Math.min(low, g.lo[bj * g.nb + bi]);
  return y + r < low - UNDER_MARGIN;
}

/// The camera (three.js space) and its near plane (within reach of it)
/// above the drawn terrain: lower by reach, still higher than every corner of
/// the aligned COARSE_MAX cells under x / z +- reach (the drawn cells under
/// it lie in those, whatever their step).
function aboveGround(g, x, y, z, reach) {
  const e = COARSE_MAX, ux = x, uz = -z;
  const i0 = Math.floor(Math.floor((ux - reach - g.x0) / g.cx) / e) * e, i1 = Math.ceil(Math.ceil((ux + reach - g.x0) / g.cx) / e) * e;
  const j0 = Math.floor(Math.floor((uz - reach - g.z0) / g.cz) / e) * e, j1 = Math.ceil(Math.ceil((uz + reach - g.z0) / g.cz) / e) * e;
  if (i0 < 0 || j0 < 0 || i1 > g.n - 1 || j1 > g.n - 1) return false;
  const low = y - reach - UNDER_MARGIN;
  for (let b = j0; b <= j1; b++) for (let a = i0; a <= i1; a++) if (g.f[b * g.n + a] >= low) return false;
  return true;
}

/// Could a line from the camera (ex, ey, ez; above the terrain) to a sphere
/// under it (x, y, z, radius r; three.js space) reach it through a hole? The
/// lines to the sphere's points stay within r of the one to its centre (x, y
/// and z alike). Such a line goes under the drawn terrain somewhere, so it
/// either crosses the terrain or goes down a hole: down a hole only when it
/// is lower than the highest sample round the hole while over it, and only
/// when it was not already under the terrain before (marched in blocks).
function throughHole(g, ex, ey, ez, x, y, z, r) {
  const ax = ex, az = -ez, dx = x - ex, dy = y - ey, dz = -z + ez;
  let first = Infinity;
  for (const h of g.holeBoxes) {
    // The segment against the box widened by r (slabs, x then z).
    const sx = slab(0, 1, ax, dx, h[0] - r, h[2] + r);
    const s = sx && slab(sx[0], sx[1], az, dz, h[1] - r, h[3] + r);
    if (!s || s[0] >= first) continue;
    if (Math.min(ey + s[0] * dy, ey + s[1] * dy) - r < h[4] + UNDER_MARGIN) first = s[0];
  }
  if (first === Infinity) return false;
  const len = Math.hypot(dx, dz), step = GROUND_BLOCK * Math.min(g.cx, g.cz);
  for (let d = step; d < first * len; d += step) {
    const t = d / len, b = blocks(g, ax + t * dx, az + t * dz, r);
    if (!b) return true;   // off the terrain: no telling
    let low = Infinity;
    for (let bj = b[1]; bj <= b[3]; bj++) for (let bi = b[0]; bi <= b[2]; bi++) low = Math.min(low, g.lo[bj * g.nb + bi]);
    if (ey + t * dy + r < low - UNDER_MARGIN) return false;   // under the terrain before the hole: it crossed it
  }
  return true;
}

const slabOut = [0, 0];
/// The part of [t0, t1] where a + t * d is within [lo, hi], or null.
function slab(t0, t1, a, d, lo, hi) {
  if (Math.abs(d) < 1e-9) { if (a < lo || a > hi) return null; }
  else {
    let u = (lo - a) / d, v = (hi - a) / d;
    if (u > v) { const t = u; u = v; v = t; }
    t0 = Math.max(t0, u); t1 = Math.min(t1, v);
    if (t0 > t1) return null;
  }
  slabOut[0] = t0; slabOut[1] = t1;
  return slabOut;
}

