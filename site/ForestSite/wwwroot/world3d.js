// The 3D view's world: the game's own models and collision, read from its
// files offline (scripts/world-extract.py export -> /world/, uploaded by the
// owner). world.json lists the meshes, materials, models (a mesh + its
// materials + render / collide + layer) and 250 m chunks of instances per
// area (surface, caves, endgame); a chunk is loaded when it comes within
// RADIUS of what the camera looks at and dropped past DROP. Every model is
// one InstancedMesh over all loaded chunks, so the draw calls stay at the
// number of models in view, not of objects. Files are fetched with
// ?v=<build> (world.json's export time): cached for a day, never stale.
//
// World (Unity): x east, y up, z north; drawn at (x, y, -z) as map3d.js. An
// instance's matrix is S * M with S = diag(1, 1, -1) on the mesh's own
// (Unity) vertices - mirrored; with the indices turned (geometry()) front
// faces come out counter-clockwise, so solid materials cull back faces as
// Unity does (a cave shell seen from outside is see-through, not a grey
// wall). Cut-outs (leaf cards) stay double-sided.
import * as THREE from "https://cdn.jsdelivr.net/npm/three@0.170.0/build/three.module.min.js";

const RADIUS = 700, DROP = 1100;           // metres, horizontally from the camera's target
const CUT_MARGIN = 4;                      // metres kept in front of the target, underground
const S = new THREE.Matrix4().makeScale(1, 1, -1);

let metaPromise = null;
/// world.json's content, or null (nothing uploaded).
export function worldMeta() {
  if (!metaPromise) metaPromise = fetch("/world/world.json", { cache: "no-cache" })
    .then(r => r.status === 200 ? r.json() : null)
    .then(m => m && m.version === 1 && Array.isArray(m.chunks) ? m : null)
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
    this.textures = new Map();    // texture index -> THREE.Texture
    this.mats = new Map();        // material index -> THREE.Material (render)
    this.drawn = new Map();       // model index -> THREE.InstancedMesh
    this.dirtyModels = new Set();
    this.show = { models: true, collision: false };
    try { const s = JSON.parse(localStorage.getItem("forest.world3d") || "null"); if (s) Object.assign(this.show, s); } catch (e) { /* defaults */ }
    this.fade = 1;
    this.target = null;
    this.next = 0;
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
    this.box.append(button("models", "Models", "The game's models: rocks, cliffs, buildings, caves, the endgame"),
      button("collision", "Collision", "What the player collides with, invisible walls included"));
    if (controls) controls.append(this.box);
    this.status = document.createElement("span");
    this.status.className = "map3dworldnote";
    this.box.append(this.status);

    worldMeta().then(m => { if (!m || this.disposed) return; this.meta = m; this.v = "?v=" + (m.build || 0); this.box.hidden = false; this.next = 0; this.changed(); });
  }

  mark(b, on) { b.classList.toggle("on", on); b.setAttribute("aria-pressed", on); }

  toggle(key, b) {
    this.show[key] = !this.show[key];
    this.mark(b, this.show[key]);
    try { localStorage.setItem("forest.world3d", JSON.stringify(this.show)); } catch (e) { /* not kept */ }
    for (const [mi, mesh] of this.drawn) mesh.visible = this.wanted(this.meta.models[mi]);
    this.next = 0;
    this.changed();
  }

  wanted(model) { return model.kind === "collide" ? this.show.collision : this.show.models; }

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
      const o = surface ? this.fade : 1;
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
    if (this.show.models || this.show.collision) for (const c of this.meta.chunks) {
      const dx = Math.max(c.x - x, 0, x - (c.x + size)), dz = Math.max(c.z - z, 0, z - (c.z + size));
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
    this.status.textContent = loading ? "loading " + loading + "…" : "";
  }

  async load(file) {
    const entry = { state: "loading", items: [] };
    this.chunks.set(file, entry);
    try {
      const r = await fetch("/world/" + file + this.v);
      if (!r.ok) throw new Error(r.status);
      const buf = await r.arrayBuffer();
      if (this.disposed || this.chunks.get(file) !== entry) return;
      const n = buf.byteLength / 52, u = new Uint32Array(buf), f = new Float32Array(buf);
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
      await Promise.all([...models].map(mi => this.geometry(this.meta.models[mi].mesh)));
      if (this.disposed || this.chunks.get(file) !== entry) return;
      entry.state = "ready";
      for (const mi of models) this.dirtyModels.add(mi);
      this.next = 0;
      this.changed();
    } catch (e) {
      entry.state = "failed";
    }
  }

  geometry(index) {
    if (this.geoms.has(index)) return this.geoms.get(index);
    const m = this.meta.meshes[index];
    const p = fetch("/world/m/" + index + ".bin" + this.v).then(r => r.ok ? r.arrayBuffer() : null).then(buf => {
      if (!buf) return null;
      const g = new THREE.BufferGeometry();
      let at = 0;
      g.setAttribute("position", new THREE.BufferAttribute(new Float32Array(buf, 0, m.nv * 3), 3));
      at = m.nv * 12;
      if (m.uv) { g.setAttribute("uv", new THREE.BufferAttribute(new Float32Array(buf, at, m.nv * 2), 2)); at += m.nv * 8; }
      const idx = m.i32 ? new Uint32Array(buf.slice(at, at + m.ni * 4)) : new Uint16Array(buf.slice(at, at + m.ni * 2));
      // Unity winds front faces clockwise: turned, so the normals face out.
      for (let i = 0; i < idx.length; i += 3) { const t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
      g.setIndex(new THREE.BufferAttribute(idx, 1));
      m.sub.forEach(([start, count], i) => g.addGroup(start, count, i));
      g.computeVertexNormals();
      g.computeBoundingSphere();
      return g;
    }).catch(() => null);
    this.geoms.set(index, p);
    return p;
  }

  material(index) {
    if (this.mats.has(index)) return this.mats.get(index);
    const d = index >= 0 ? this.meta.materials[index] : null;
    const c = d ? d.color : [0.7, 0.7, 0.7, 1];
    const mat = new THREE.MeshLambertMaterial({ color: new THREE.Color(c[0], c[1], c[2]).convertSRGBToLinear(), side: d && d.cut ? THREE.DoubleSide : THREE.FrontSide,
      clippingPlanes: this.cutPlanes });
    if (d && d.tex >= 0) mat.map = this.texture(d.tex, d.cut, d.scale);   // scale: the material's tiling
    if (d && d.cut) mat.alphaTest = 0.5;   // leaves, grass, fences: cut out by the texture's alpha
    if (d && d.top >= 0 && mat.map) topLayer(mat, this.texture(d.top), d.topScale || 1);   // needs the main UVs
    this.mats.set(index, mat);
    return mat;
  }

  texture(i, alpha, scale) {
    const key = (alpha ? i + "a" : "" + i) + (scale ? "x" + scale.join(",") : "");
    let t = this.textures.get(key);
    if (!t) {
      t = new THREE.TextureLoader().load("/world/t/" + i + (alpha ? ".png" : ".jpg") + this.v, () => this.changed());
      t.colorSpace = THREE.SRGBColorSpace;
      t.wrapS = t.wrapT = THREE.RepeatWrapping;
      if (scale) t.repeat.set(scale[0], scale[1]);
      this.textures.set(key, t);
    }
    return t;
  }

  /// One InstancedMesh per model over every ready chunk.
  async rebuild() {
    const models = [...this.dirtyModels];
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
      const g = await this.geometry(model.mesh);
      if (!g || this.disposed || this.drawn.has(mi)) continue;
      let mat;
      if (model.kind === "collide") mat = [this.collideMat, this.collideWire];
      else mat = model.mats.length > 1 ? model.mats.map(x => this.material(x)) : this.material(model.mats[0] ?? -1);
      const mesh = model.kind === "collide" ? this.collisionMesh(g, list) : new THREE.InstancedMesh(g, mat, list.length);
      if (model.kind !== "collide") {
        list.forEach((m, i) => mesh.setMatrixAt(i, m));
        mesh.instanceMatrix.needsUpdate = true;
        mesh.computeBoundingSphere();
      }
      mesh.userData.surface = surface.has(mi);
      mesh.visible = this.wanted(model);
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
    this.collideMat.dispose(); this.collideWire.dispose();
    this.scene.remove(this.group);
  }
}
