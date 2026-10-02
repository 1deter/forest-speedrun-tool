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
// each blob's place [file, offset, length]; phones draw some meshes as
// lighter copies (a mesh's "lod", in files of their own; collision keeps the
// full one) - scripts/world_pack.py. The server sends .bin / .json gzipped
// (the upload's .gz copies).
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
const MOBILE = matchMedia("(pointer: coarse)").matches;   // as map3d.js: phones and tablets draw the LODs

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
    this.staleAt = 0;
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
    this.changed();
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

  /// Each frame: which chunks to have, given the camera's target (a Vector3
  /// in three.js space). Rate-limited; cheap when nothing changes.
  update(target, now) {
    if (!this.meta || this.disposed || now < this.next) return;
    this.next = now + 400;
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
      await Promise.all([...models].filter(mi => this.fetched(mi)).map(mi => this.geometry(this.meshOf(this.meta.models[mi]))));
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

  /// The mesh a model draws with: on phones a heavy mesh's lighter copy
  /// (version 3 "lod"), except for collision.
  meshOf(model) {
    const lod = this.meta.meshes[model.mesh].lod;
    return MOBILE && lod !== undefined && model.kind !== "collide" ? lod : model.mesh;
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
    const p = src.then(found => {
      if (!found) return null;
      const [buf, base] = found;
      const g = new THREE.BufferGeometry();
      let at = base;
      g.setAttribute("position", new THREE.BufferAttribute(new Float32Array(buf, at, m.nv * 3), 3));
      at += m.nv * 12;
      if (m.uv) { g.setAttribute("uv", new THREE.BufferAttribute(new Float32Array(buf, at, m.nv * 2), 2)); at += m.nv * 8; }
      // A LOD carries its full mesh's normals: it shades as the full one does.
      if (m.n) { g.setAttribute("normal", new THREE.BufferAttribute(new Float32Array(buf, at, m.nv * 3), 3)); at += m.nv * 12; }
      const idx = m.i32 ? new Uint32Array(buf.slice(at, at + m.ni * 4)) : new Uint16Array(buf.slice(at, at + m.ni * 2));
      // Unity winds front faces clockwise: turned, so the normals face out.
      for (let i = 0; i < idx.length; i += 3) { const t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
      g.setIndex(new THREE.BufferAttribute(idx, 1));
      m.sub.forEach(([start, count], i) => g.addGroup(start, count, i));
      if (!m.n) g.computeVertexNormals();
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
    for (const mi of models) {
      const old = this.drawn.get(mi);
      if (old) { this.group.remove(old); old.dispose(); this.drawn.delete(mi); }
      const list = by.get(mi), model = this.meta.models[mi];
      if (!list.length || !model) continue;
      // Glints and particles (the pickups' sheen): not a solid thing.
      if (model.mats.length && model.mats.every(x => x >= 0 && this.meta.materials[x].fx)) continue;
      if (!this.fetched(mi)) continue;     // collision, switched off: built when switched on
      const gi = this.meshOf(model);
      if (!this.geomsDone.has(gi)) {
        // Not here yet (collision just switched on): fetched, then built by a
        // later rebuild - never awaited here, where a second rebuild of the
        // same model could finish first and be dropped.
        this.fetching++;
        this.geometry(gi).then(() => {
          this.fetching--;
          if (gen === this.gen && !this.disposed) { this.dirtyModels.add(mi); this.next = 0; this.changed(); }
        });
        continue;
      }
      const g = await this.geometry(gi);
      if (gen !== this.gen) return;      // started over meanwhile (stale())
      if (!g || this.disposed || this.drawn.has(mi)) continue;
      let mat;
      if (model.kind === "collide") mat = [this.collideMat, this.collideWire];
      else {
        const sf = surface.has(mi);
        mat = model.mats.length > 1 ? model.mats.map(x => this.material(x, sf)) : this.material(model.mats[0] ?? -1, sf);
      }
      const mesh = model.kind === "collide" ? this.collisionMesh(g, list) : new THREE.InstancedMesh(g, mat, list.length);
      if (model.kind !== "collide") {
        list.forEach((m, i) => mesh.setMatrixAt(i, m));
        mesh.instanceMatrix.needsUpdate = true;
        mesh.computeBoundingSphere();
      }
      mesh.userData.surface = surface.has(mi);
      mesh.visible = this.wanted(mi);
      this.applyFade(mi, mesh);
      this.group.add(mesh);
      this.drawn.set(mi, mesh);
    }
    this.changed();
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
