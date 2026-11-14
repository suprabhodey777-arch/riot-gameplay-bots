// Map: A and B sites, a mid lane and cover boxes. Low-poly look; collision boxes are the source of truth.
import * as THREE from 'three';

export const SITES = {
  A: { x1: -24, x2: -10, z1: 2, z2: 18 },
  B: { x1: 10, x2: 24, z1: 2, z2: 18 },
};

// 충돌 박스 목록 {min:Vector3, max:Vector3}
export const colliders = [];

function addCollider(minX, minY, minZ, maxX, maxY, maxZ) {
  colliders.push({ min: new THREE.Vector3(minX, minY, minZ), max: new THREE.Vector3(maxX, maxY, maxZ) });
}

// Small seeded random generator so decorations look the same on every load.
function seeded(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// A box whose top face is pulled in by `inset` on every side (a chamfered cap). Origin at the centre.
function capGeometry(w, h, d, inset) {
  const x = w / 2, z = d / 2, y = h / 2;
  const xi = Math.max(0.02, x - inset), zi = Math.max(0.02, z - inset);
  const v = [
    -x, -y, -z, x, -y, -z, x, -y, z, -x, -y, z,          // bottom 0..3
    -xi, y, -zi, xi, y, -zi, xi, y, zi, -xi, y, zi,      // top 4..7
  ];
  const idx = [
    4, 7, 6, 4, 6, 5,   // top
    0, 4, 5, 0, 5, 1,   // -z side
    1, 5, 6, 1, 6, 2,   // +x side
    2, 6, 7, 2, 7, 3,   // +z side
    3, 7, 4, 3, 4, 0,   // -x side
  ];
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(v, 3));
  g.setIndex(idx);
  g.computeVertexNormals();
  return g;
}

export function buildMap(scene) {
  // Light: soft sky, warm sun.
  scene.background = new THREE.Color(0xa9c7d6);
  scene.fog = new THREE.Fog(0xa9c7d6, 70, 190);
  const hemi = new THREE.HemisphereLight(0xe6f2ff, 0x5a6b5c, 0.95);
  scene.add(hemi);
  const sun = new THREE.DirectionalLight(0xfff2dc, 1.15);
  sun.position.set(30, 50, -20);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.camera.left = -45; sun.shadow.camera.right = 45;
  sun.shadow.camera.top = 45; sun.shadow.camera.bottom = -45;
  scene.add(sun);

  const flat = (color, extra) => new THREE.MeshStandardMaterial({ color, roughness: 0.95, flatShading: true, ...extra });
  const mat = {
    ground: flat(0xffffff, { vertexColors: true }),
    wall: flat(0xe4d9c4),        // light walls
    wallCap: flat(0xf3ecdd),
    wallBase: flat(0xb9ab93),
    wallDark: flat(0x53636f),    // dark walls (mid, defender spawn)
    wallDarkCap: flat(0x6c7d89),
    wallDarkBase: flat(0x3d4a54),
    box: flat(0x7f93a1),         // metal boxes
    boxLid: flat(0x9db0bc),
    boxWood: flat(0x9a7a52),     // wooden boxes
    boxWoodLid: flat(0xb8976a),
    accent: flat(0xff4655, { roughness: 0.6 }),
    teal: flat(0x00e5cc, { emissive: 0x00e5cc, emissiveIntensity: 0.4 }),
    post: flat(0x1f2a33),
    rock: flat(0x8d8f8a),
    rockPale: flat(0xb5b3a8),
    leaf: flat(0x6f9a5b),
    leafDark: flat(0x4f7a52),
    trunk: flat(0x6b513a),
    pipe: flat(0x8a5f4a),
    hill: flat(0x8fa39a),
    hillFar: flat(0xa2b6b3),
  };
  const rand = seeded(20261114);
  const deco = new THREE.Group();   // decoration only: never added to `colliders`
  scene.add(deco);
  function prop(geometry, material, x, y, z, ry = 0, sx = 1, sy = 1, sz = 1, shadow = true) {
    const m = new THREE.Mesh(geometry, material);
    m.position.set(x, y, z); m.rotation.y = ry; m.scale.set(sx, sy, sz);
    m.castShadow = shadow; m.receiveShadow = true;
    deco.add(m);
    return m;
  }

  // Ground (70 x 80), flat at y = 0, drawn as a mosaic of slightly different triangles.
  const groundGeo = new THREE.PlaneGeometry(70, 80, 14, 16).toNonIndexed();
  {
    const n = groundGeo.attributes.position.count;
    const colors = new Float32Array(n * 3);
    const base = new THREE.Color(0x9fae8c), c = new THREE.Color();
    for (let i = 0; i < n; i += 3) {
      const k = 0.94 + rand() * 0.12;
      c.copy(base).multiplyScalar(k);
      for (let j = 0; j < 3; j++) colors.set([c.r, c.g, c.b], (i + j) * 3);
    }
    groundGeo.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  }
  const ground = new THREE.Mesh(groundGeo, mat.ground);
  ground.rotation.x = -Math.PI / 2;
  ground.receiveShadow = true;
  scene.add(ground);

  // Site floor markings and site posts.
  for (const [key, s] of Object.entries(SITES)) {
    const w = s.x2 - s.x1, d = s.z2 - s.z1;
    const cx = (s.x1 + s.x2) / 2, cz = (s.z1 + s.z2) / 2;
    const zone = new THREE.Mesh(new THREE.PlaneGeometry(w, d), flat(key === 'A' ? 0xdccfa6 : 0xbdd6cc));
    zone.rotation.x = -Math.PI / 2;
    zone.position.set(cx, 0.02, cz);
    zone.receiveShadow = true;
    scene.add(zone);
    const pillar = new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.34, 5, 6), mat.post);
    pillar.position.set(cx, 2.5, cz);
    scene.add(pillar);
    const top = new THREE.Mesh(new THREE.BoxGeometry(2.4, 1.2, 0.3),
      flat(0x1f2a33, { emissive: key === 'A' ? 0xff4655 : 0x00e5cc, emissiveIntensity: 0.8 }));
    top.position.set(cx, 5.2, cz);
    scene.add(top);
  }

  // Walls and boxes. What is drawn never reaches outside the collision box:
  // a darker base band, the main block, and a chamfered cap that ends at the collider's top.
  const walls = new THREE.Group();
  scene.add(walls);
  const shade = (m) => { m.castShadow = true; m.receiveShadow = true; return m; };
  function wall(x, z, w, h, d, material = mat.wall) {
    const dark = material === mat.wallDark;
    const baseH = 0.35, capH = 0.25, inset = Math.min(0.12, w / 4, d / 4);
    const base = shade(new THREE.Mesh(new THREE.BoxGeometry(w, baseH, d), dark ? mat.wallDarkBase : mat.wallBase));
    base.position.set(x, baseH / 2, z);
    const mainH = h - baseH - capH;
    const m = shade(new THREE.Mesh(new THREE.BoxGeometry(w, mainH, d), material));
    m.position.set(x, baseH + mainH / 2, z);
    const cap = shade(new THREE.Mesh(capGeometry(w, capH, d, inset), dark ? mat.wallDarkCap : mat.wallCap));
    cap.position.set(x, h - capH / 2, z);
    walls.add(base, m, cap);
    addCollider(x - w / 2, 0, z - d / 2, x + w / 2, h, z + d / 2);
    return m;
  }
  function crate(x, z, s = 1.6, h = 1.6, material = mat.box) {
    const wood = material === mat.boxWood;
    const lidH = 0.14;
    const m = shade(new THREE.Mesh(new THREE.BoxGeometry(s, h - lidH, s), material));
    m.position.set(x, (h - lidH) / 2, z);
    const lid = shade(new THREE.Mesh(capGeometry(s, lidH, s, 0.08), wood ? mat.boxWoodLid : mat.boxLid));
    lid.position.set(x, h - lidH / 2, z);
    scene.add(m, lid);
    addCollider(x - s / 2, 0, z - s / 2, x + s / 2, h, z + s / 2);
    return m;
  }

  // Outer walls
  wall(0, -34, 70, 6, 1);
  wall(0, 34, 70, 6, 1);
  wall(-34, 0, 1, 6, 70);
  wall(34, 0, 1, 6, 70);

  // Mid structure
  wall(0, -8, 12, 4, 1.2, mat.wallDark);          // mid wall
  wall(-8, -4, 1.2, 4, 10);                        // mid left
  wall(8, -4, 1.2, 4, 10);                         // mid right
  crate(-5, -12, 1.6, 1.6); crate(5, -12, 1.6, 1.6);
  crate(0, -14, 2.2, 1.2, mat.boxWood);

  // A site (west): boxes and cover
  wall(-17, 2, 14, 3.5, 1, mat.wall);              // A entrance wall
  wall(-27, 10, 1, 3.5, 16);                       // A inner side wall
  crate(-20, 8, 1.6); crate(-18.2, 8, 1.6); crate(-19.1, 8, 1.6, 3.0);
  crate(-14, 12, 2.0, 1.4, mat.boxWood); crate(-22, 14, 1.8);
  wall(-13, 16, 8, 2.5, 1);                        // A back wall
  crate(-17, 5, 1.4, 1.2, mat.boxWood);

  // B site (east), mirrored
  wall(17, 2, 14, 3.5, 1, mat.wall);
  wall(27, 10, 1, 3.5, 16);
  crate(20, 8, 1.6); crate(18.2, 8, 1.6); crate(19.1, 8, 1.6, 3.0);
  crate(14, 12, 2.0, 1.4, mat.boxWood); crate(22, 14, 1.8);
  wall(13, 16, 8, 2.5, 1);
  crate(17, 5, 1.4, 1.2, mat.boxWood);

  // Long lane walls
  wall(-12, -18, 1.2, 3.5, 14);  // A long
  wall(12, -18, 1.2, 3.5, 14);   // B long
  wall(-20, -18, 12, 3.5, 1.2);  // in front of attacker spawn
  wall(20, -18, 12, 3.5, 1.2);
  wall(-20, 24, 10, 3.5, 1.2, mat.wallDark); // defender spawn
  wall(20, 24, 10, 3.5, 1.2, mat.wallDark);

  // Extra cover in the centre
  crate(-3, 4, 1.8, 1.5); crate(3, 4, 1.8, 1.5);
  crate(0, 10, 2.4, 1.2, mat.boxWood);

  // Team signs on the spawn walls
  const atkSign = new THREE.Mesh(new THREE.BoxGeometry(6, 1, 0.4), mat.accent);
  atkSign.position.set(0, 4, -33.2); scene.add(atkSign);
  const defSign = new THREE.Mesh(new THREE.BoxGeometry(6, 1, 0.4), mat.teal);
  defSign.position.set(0, 4, 33.2); scene.add(defSign);

  // ---------- decoration only (no colliders; nothing here blocks movement, sight or shots) ----------
  const rockGeo = new THREE.IcosahedronGeometry(1, 0);
  const tuftGeo = new THREE.ConeGeometry(0.16, 0.34, 4);
  const pineGeo = new THREE.ConeGeometry(1.6, 4.2, 6);
  const trunkGeo = new THREE.CylinderGeometry(0.28, 0.36, 2, 5);
  const hillGeo = new THREE.ConeGeometry(1, 1, 6);

  // Grass tufts and pebbles, ankle high, tucked against the inside of the outer walls.
  for (let i = 0; i < 70; i++) {
    const along = -32 + rand() * 64, off = 33.15 - rand() * 0.35;
    const side = Math.floor(rand() * 4);
    const x = side === 0 ? along : side === 1 ? along : side === 2 ? -off : off;
    const z = side === 0 ? -off : side === 1 ? off : along;
    if (rand() < 0.7) {
      const s = 0.7 + rand() * 0.7;
      prop(tuftGeo, rand() < 0.5 ? mat.leaf : mat.leafDark, x, 0.17 * s, z, rand() * 3, s, s, s, false);
    } else {
      const s = 0.1 + rand() * 0.1;
      prop(rockGeo, rand() < 0.5 ? mat.rock : mat.rockPale, x, s * 0.5, z, rand() * 3, s, s * 0.7, s, false);
    }
  }

  // Pipes high on the inside face of the outer walls (4.6 m up, above every sightline that matters).
  const pipeLong = new THREE.CylinderGeometry(0.12, 0.12, 20, 6);
  const clampGeo = new THREE.BoxGeometry(0.34, 0.34, 0.2);
  for (const [px, pz, alongX] of [[-18, -33.36, true], [18, 33.36, true], [-33.36, -12, false], [33.36, 12, false]]) {
    const p = prop(pipeLong, mat.pipe, px, 4.6, pz, 0, 1, 1, 1, false);
    if (alongX) p.rotation.z = Math.PI / 2; else p.rotation.x = Math.PI / 2;
    for (const k of [-8, 0, 8]) {
      prop(clampGeo, mat.wallDarkBase, alongX ? px + k : px, 4.6, alongX ? pz : pz + k, alongX ? 0 : Math.PI / 2, 1, 1, 1, false);
    }
  }

  // Outside the playable area: rocks at the foot of the outer walls, pines behind them, hills on the horizon.
  for (let i = 0; i < 46; i++) {
    const a = (i / 46) * Math.PI * 2 + rand() * 0.1;
    const r = 39 + rand() * 7;
    const x = Math.cos(a) * r * 1.02, z = Math.sin(a) * r * 1.02;
    if (Math.abs(x) < 35.5 && Math.abs(z) < 35.5) continue;   // keep clear of the arena
    if (i % 3 === 0) {
      const s = 1.2 + rand() * 1.6;
      prop(rockGeo, rand() < 0.5 ? mat.rock : mat.rockPale, x, s * 0.45, z, rand() * 3, s, s * (0.6 + rand() * 0.5), s);
    } else {
      const s = 0.9 + rand() * 0.7;
      prop(trunkGeo, mat.trunk, x, 1 * s, z, 0, s, s, s);
      prop(pineGeo, rand() < 0.5 ? mat.leaf : mat.leafDark, x, (2 + 2.1) * s, z, rand() * 3, s, s, s);
      prop(pineGeo, mat.leafDark, x, (2 + 4.0) * s, z, rand() * 3, s * 0.68, s * 0.75, s * 0.68);
    }
  }
  for (let i = 0; i < 14; i++) {
    const a = (i / 14) * Math.PI * 2 + rand() * 0.3;
    const r = 78 + rand() * 30;
    const h = 14 + rand() * 22, w = 16 + rand() * 16;
    prop(hillGeo, i % 2 ? mat.hill : mat.hillFar, Math.cos(a) * r, h / 2, Math.sin(a) * r, rand() * 3, w, h, w, false);
  }
}

// AABB 충돌 해결: pos(발바닥) + radius + height
export function collideMove(pos, radius = 0.45, height = 1.7) {
  // pos는 THREE.Vector3 (발바닥 기준). XZ와 Y 분리 해결.
  // XZ 해결
  for (const c of colliders) {
    // 플레이어 Y범위와 박스 Y 겹치는지
    if (pos.y + height < c.min.y || pos.y + 0.2 > c.max.y) continue;
    const nx = Math.max(c.min.x, Math.min(pos.x, c.max.x));
    const nz = Math.max(c.min.z, Math.min(pos.z, c.max.z));
    const dx = pos.x - nx, dz = pos.z - nz;
    const d2 = dx * dx + dz * dz;
    if (d2 < radius * radius) {
      if (d2 > 1e-6) {
        const d = Math.sqrt(d2);
        const push = (radius - d) / d;
        pos.x += dx * push; pos.z += dz * push;
      } else {
        // 안에 박힘: 가장 얇은 축으로 밀기
        const px1 = pos.x - c.min.x, px2 = c.max.x - pos.x;
        const pz1 = pos.z - c.min.z, pz2 = c.max.z - pos.z;
        const m = Math.min(px1, px2, pz1, pz2);
        if (m === px1) pos.x = c.min.x - radius;
        else if (m === px2) pos.x = c.max.x + radius;
        else if (m === pz1) pos.z = c.min.z - radius;
        else pos.z = c.max.z + radius;
      }
    }
  }
  // 맵 경계
  pos.x = Math.max(-33, Math.min(33, pos.x));
  pos.z = Math.max(-33, Math.min(33, pos.z));
}

// 바닥 높이 (박스 위에 올라갈 수 있게): 발바닥 y를 받으면 서 있을 높이 반환
export function groundHeight(x, z, y) {
  let g = 0;
  for (const c of colliders) {
    if (x >= c.min.x - 0.3 && x <= c.max.x + 0.3 && z >= c.min.z - 0.3 && z <= c.max.z + 0.3) {
      if (c.max.y <= y + 0.6 && c.max.y > g) g = c.max.y;
    }
  }
  return g;
}
