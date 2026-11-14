// Original low-poly player characters, built from primitives (no textures, no files).
//
// buildCharacter(agentId, team, name?) returns a THREE.Group standing on y = 0 and
// facing -z at rotation.y = 0. The slot ids (jett / omen / sova / sage) are the game's
// internal kit ids; the characters drawn for them are our own designs.
//
// Size follows the hit volumes in game.js `rayPlayer`, which do not change:
//   head sphere centre y 1.62, radius 0.32; body box x ±0.38, y 0..1.45, z ±0.3.
// Every part stays within about 0.1 m of that envelope (the gun barrel excepted).
//
// Team colour is on the torso and both arms of every character; the character's own
// accent colour is only on its signature pieces.
import * as THREE from 'three';

export const TEAM_COLORS = { attack: 0xff4655, defense: 0x00e5cc };
const TAG_COLORS = { attack: '#ff8089', defense: '#7df0e0' };

/** What players see for each kit slot. `config.js` AGENTS carries the ability text. */
export const CHARACTER_INFO = {
  jett: { name: 'SKIFF', role: 'DUELIST', color: '#ffd23f', icon: '🪁', silhouette: 'Slim runner: swept-back helmet fin, two scarf tails' },
  omen: { name: 'BRINE', role: 'CONTROLLER', color: '#b6f24a', icon: '🌫️', silhouette: 'Wide-brim hat, long coat, fog tank with twin stacks' },
  sova: { name: 'SONDE', role: 'INITIATOR', color: '#b388ff', icon: '🛰️', silhouette: 'Box sensor head with one big lens, shoulder antenna mast' },
  sage: { name: 'PATCH', role: 'SENTINEL', color: '#ff8ad8', icon: '🧰', silhouette: 'Stocky: dome helmet, round shoulder guards, square field pack' },
};
const NEUTRAL = { name: 'RECRUIT', role: '', color: '#c9d2da', icon: '🙂', silhouette: 'Plain helmet, no gear' };

// ---------- shared geometry and materials (one copy for every instance) ----------
const geos = new Map();
function geo(key, make) {
  let g = geos.get(key);
  if (!g) { g = make(); geos.set(key, g); }
  return g;
}
const mats = new Map();
function mat(color, extra) {
  const key = `${color}|${extra ? JSON.stringify(extra) : ''}`;
  let m = mats.get(key);
  if (!m) {
    m = new THREE.MeshStandardMaterial({ color, roughness: 0.85, flatShading: true, ...extra });
    mats.set(key, m);
  }
  return m;
}
const box = (w, h, d) => geo(`b${w},${h},${d}`, () => new THREE.BoxGeometry(w, h, d));
const cyl = (rt, rb, h, seg, open = false) =>
  geo(`c${rt},${rb},${h},${seg},${open}`, () => new THREE.CylinderGeometry(rt, rb, h, seg, 1, open));
const ico = (r) => geo(`i${r}`, () => new THREE.IcosahedronGeometry(r, 0));
const cone = (r, h, seg) => geo(`k${r},${h},${seg}`, () => new THREE.ConeGeometry(r, h, seg));
// An arm that hangs from its shoulder pivot (origin at the top).
const armGeo = () => geo('arm', () => new THREE.BoxGeometry(0.15, 0.56, 0.17).translate(0, -0.28, 0));
const domeGeo = () => geo('dome', () => new THREE.SphereGeometry(0.3, 6, 2, 0, Math.PI * 2, 0, Math.PI / 2));

function add(grp, geometry, material, x, y, z, rx = 0, ry = 0, rz = 0, sx = 1, sy = 1, sz = 1) {
  const m = new THREE.Mesh(geometry, material);
  m.position.set(x, y, z);
  m.rotation.set(rx, ry, rz);
  m.scale.set(sx, sy, sz);
  m.castShadow = true;
  grp.add(m);
  return m;
}

const DARK = 0x1c242d, GUN = 0x111417;

/** Legs, belt, torso, arms and gun. Returns the torso mesh (its material is per instance). */
function addBody(grp, team, o) {
  const teamHex = TEAM_COLORS[team] ?? TEAM_COLORS.attack;
  const legs = mat(o.legs), teamMat = mat(teamHex);
  const lw = o.legWide ?? 1;
  add(grp, box(0.2, 0.8, 0.24), legs, -0.16, 0.4, 0, 0, 0, 0, lw, 1, lw);
  add(grp, box(0.2, 0.8, 0.24), legs, 0.16, 0.4, 0, 0, 0, 0, lw, 1, lw);
  add(grp, box(0.56, 0.14, 0.34), mat(o.belt ?? DARK), 0, 0.85, 0);
  // Torso: six-sided, wider at the shoulders. Its own material, so one player can glow when revealed.
  const torsoMat = new THREE.MeshStandardMaterial({ color: teamHex, roughness: 0.8, flatShading: true });
  const torso = add(grp, cyl(0.4, 0.3, 0.52, 6), torsoMat, 0, 1.17, 0, 0, Math.PI / 6, 0, 1, 1, 0.72);
  // Arms in team colour: left hangs, right reaches forward to hold the gun.
  add(grp, armGeo(), teamMat, -0.39, 1.4, 0, 0.2, 0, 0);
  add(grp, armGeo(), teamMat, 0.39, 1.4, 0, 1.15, 0, 0);
  add(grp, box(0.1, 0.1, 0.8), mat(GUN, { roughness: 0.5 }), 0.34, 1.12, -0.42);
  return torso;
}

const glow = (hex) => mat(hex, { emissive: hex, emissiveIntensity: 0.35, roughness: 0.6 });

// SKIFF: slim runner. Tall swept-back fin on the helmet and two scarf tails behind.
function buildSkiff(grp, team) {
  const acc = glow(0xffd23f), shell = mat(0x2b3440);
  const torso = addBody(grp, team, { legs: 0x2b3440 });
  const head = add(grp, ico(0.26), shell, 0, 1.62, 0);
  add(grp, box(0.34, 0.08, 0.1), acc, 0, 1.64, -0.2);                           // visor strip
  add(grp, cone(0.2, 0.5, 4), acc, 0, 1.8, 0.12, 0.95, 0, 0, 0.18, 1, 1);       // fin, tip up and back
  add(grp, box(0.1, 0.5, 0.03), acc, -0.09, 1.2, 0.33, -0.3, 0, 0);             // scarf tails
  add(grp, box(0.1, 0.42, 0.03), acc, 0.09, 1.24, 0.32, -0.36, 0, 0);
  add(grp, box(0.06, 0.26, 0.1), acc, -0.28, 0.6, 0);                           // knife sheath, left thigh
  return { torso, head };
}

// BRINE: fog keeper. Wide-brim hat, long coat, fog tank with two stacks on the back.
function buildBrine(grp, team) {
  const acc = glow(0xb6f24a), coat = mat(0x3d4a44), hat = mat(0x26302c);
  const torso = addBody(grp, team, { legs: 0x26302c, belt: 0x3d4a44 });
  add(grp, cyl(0.3, 0.42, 0.5, 6, true), mat(0x3d4a44, { side: THREE.DoubleSide }), 0, 0.6, 0, 0, Math.PI / 6, 0, 1, 1, 0.8); // coat skirt
  const head = add(grp, ico(0.24), mat(0xd9c7a8), 0, 1.6, 0);
  add(grp, cyl(0.07, 0.07, 0.12, 6), acc, 0, 1.54, -0.24, Math.PI / 2, 0, 0);   // breather
  add(grp, cyl(0.41, 0.41, 0.04, 8), hat, 0, 1.79, 0);                          // brim
  add(grp, cyl(0.2, 0.25, 0.2, 8), hat, 0, 1.91, 0);                            // crown
  add(grp, cyl(0.255, 0.255, 0.05, 8), acc, 0, 1.835, 0);                       // hat band
  add(grp, box(0.44, 0.5, 0.16), coat, 0, 1.15, 0.3);                           // fog tank
  add(grp, box(0.46, 0.12, 0.18), mat(TEAM_COLORS[team]), 0, 1.2, 0.3);         // team band on the tank
  add(grp, cyl(0.05, 0.05, 0.3, 6), hat, -0.14, 1.5, 0.32);                     // stacks
  add(grp, cyl(0.05, 0.05, 0.3, 6), hat, 0.14, 1.5, 0.32);
  add(grp, cyl(0.06, 0.06, 0.05, 6), acc, -0.14, 1.66, 0.32);
  add(grp, cyl(0.06, 0.06, 0.05, 6), acc, 0.14, 1.66, 0.32);
  return { torso, head };
}

// SONDE: surveyor. Box sensor head with one large lens, antenna mast on the left shoulder.
function buildSonde(grp, team) {
  const acc = glow(0xb388ff), shell = mat(0x39405a), dark = mat(0x23283a);
  const torso = addBody(grp, team, { legs: 0x23283a });
  const head = add(grp, box(0.4, 0.36, 0.4), shell, 0, 1.62, 0);
  add(grp, cyl(0.12, 0.12, 0.06, 8), acc, 0, 1.63, -0.21, Math.PI / 2, 0, 0);   // lens
  add(grp, box(0.44, 0.06, 0.44), dark, 0, 1.82, 0);                            // head cap
  add(grp, box(0.24, 0.08, 0.3), shell, -0.36, 1.44, 0);                        // left shoulder plate
  add(grp, box(0.03, 0.56, 0.03), dark, -0.42, 1.7, 0.1);                       // mast
  add(grp, ico(0.06), acc, -0.42, 1.98, 0.1);                                   // mast tip
  add(grp, box(0.12, 0.03, 0.03), dark, -0.42, 1.84, 0.1);                      // cross bar
  add(grp, box(0.1, 0.3, 0.14), dark, -0.29, 0.62, 0);                          // stake case, left thigh
  add(grp, box(0.08, 0.05, 0.12), acc, -0.29, 0.79, 0);
  return { torso, head };
}

// PATCH: field mender. Stocky, dome helmet, round shoulder guards, square pack with a bedroll.
function buildPatch(grp, team) {
  const acc = glow(0xff8ad8), shell = mat(0xe9e4da), pack = mat(0x6b5a4a);
  const torso = addBody(grp, team, { legs: 0x4a4038, legWide: 1.2, belt: 0x6b5a4a });
  const head = add(grp, domeGeo(), shell, 0, 1.62, 0);
  add(grp, box(0.42, 0.22, 0.42), shell, 0, 1.52, 0);                           // lower helmet
  add(grp, box(0.44, 0.09, 0.1), acc, 0, 1.6, -0.2);                            // visor band
  add(grp, ico(0.13), shell, -0.35, 1.4, 0, 0, 0, 0, 1, 0.75, 1.4);             // shoulder guards
  add(grp, ico(0.13), shell, 0.35, 1.4, 0, 0, 0, 0, 1, 0.75, 1.4);
  add(grp, box(0.5, 0.56, 0.16), pack, 0, 1.12, 0.31);                          // field pack
  add(grp, box(0.52, 0.1, 0.17), mat(TEAM_COLORS[team]), 0, 1.33, 0.31);        // team band on the pack
  add(grp, cyl(0.1, 0.1, 0.03, 6), acc, 0, 1.14, 0.395, Math.PI / 2, 0, 0);     // pack badge (hexagon)
  add(grp, cyl(0.08, 0.08, 0.5, 6), shell, 0, 1.47, 0.3, 0, 0, Math.PI / 2);    // bedroll
  add(grp, box(0.12, 0.12, 0.08), acc, -0.2, 0.84, -0.2);                       // belt pouches
  add(grp, box(0.12, 0.12, 0.08), acc, 0.2, 0.84, -0.2);
  return { torso, head };
}

// RECRUIT: neutral default, used when the agent is unknown (replay logs).
function buildRecruit(grp, team) {
  const torso = addBody(grp, team, { legs: 0x2f3842 });
  const head = add(grp, ico(0.27), mat(0xc9d2da), 0, 1.62, 0);
  add(grp, box(0.32, 0.08, 0.1), mat(0x2f3842), 0, 1.64, -0.21);
  return { torso, head };
}

const BUILDERS = { jett: buildSkiff, omen: buildBrine, sova: buildSonde, sage: buildPatch };

/** Name tag sprite (needs a DOM canvas; only call in the browser). */
export function makeNameTag(name, team) {
  const c = document.createElement('canvas'); c.width = 256; c.height = 64;
  const g = c.getContext('2d');
  g.font = 'bold 30px sans-serif'; g.textAlign = 'center';
  g.fillStyle = 'rgba(0,0,0,0.55)'; g.fillRect(28, 6, 200, 40);
  g.fillStyle = TAG_COLORS[team] ?? TAG_COLORS.attack; g.fillText(name, 128, 37);
  const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: new THREE.CanvasTexture(c), depthTest: false }));
  sp.scale.set(1.6, 0.4, 1);
  return sp;
}

/**
 * Build one character.
 * @param {string|null} agentId  kit slot id ('jett' | 'omen' | 'sova' | 'sage'); anything else draws the neutral recruit
 * @param {'attack'|'defense'} team
 * @param {string} [name]  when given, a name tag is added above the head
 * @returns {THREE.Group} with userData { body (torso mesh, own material), head, tag, teamColor, agentId, info }
 */
export function buildCharacter(agentId, team, name) {
  const grp = new THREE.Group();
  const build = BUILDERS[agentId] || buildRecruit;
  const { torso, head } = build(grp, team);
  let tag = null;
  if (name != null && typeof document !== 'undefined') {
    tag = makeNameTag(String(name), team);
    tag.position.y = 2.25;
    grp.add(tag);
  }
  grp.userData = {
    body: torso, head, tag,
    teamColor: TEAM_COLORS[team] ?? TEAM_COLORS.attack,
    agentId: BUILDERS[agentId] ? agentId : null,
    info: CHARACTER_INFO[agentId] || NEUTRAL,
  };
  return grp;
}
