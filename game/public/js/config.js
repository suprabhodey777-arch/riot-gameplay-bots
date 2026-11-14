// Weapon and agent balance
export const WEAPONS = {
  classic:  { name:'STUB',      price:0,    damage:26, headMult:2.5, mag:12, reserve:36,  auto:false, rpm:400, spread:0.025, reload:1.6, range:50 },
  sheriff:  { name:'WARDEN',    price:800,  damage:55, headMult:2.6, mag:6,  reserve:24,  auto:false, rpm:250, spread:0.012, reload:2.0, range:60 },
  spectre:  { name:'HORNET',    price:1600, damage:22, headMult:2.4, mag:30, reserve:90,  auto:true,  rpm:780, spread:0.035, reload:2.1, range:35 },
  phantom:  { name:'DRIFT',     price:2900, damage:35, headMult:2.5, mag:25, reserve:75,  auto:true,  rpm:660, spread:0.022, reload:2.3, range:45 },
  vandal:   { name:'RIVET',     price:2900, damage:40, headMult:2.5, mag:25, reserve:75,  auto:true,  rpm:600, spread:0.020, reload:2.4, range:60 },
  operator: { name:'LONGSHOT',  price:4700, damage:120,headMult:2.0, mag:5,  reserve:15,  auto:false, rpm:60,  spread:0.002, reload:3.2, range:100, sniper:true },
  knife:    { name:'KNIFE',     price:0,    damage:50, headMult:2.0, mag:Infinity, reserve:Infinity, auto:false, rpm:90, spread:0, reload:0, range:2.6, melee:true },
};

export const AGENTS = {
  // The keys are internal kit ids used by the server, bots and ability code. Display fields are our own characters.
  jett: {
    name:'SKIFF', role:'DUELIST', icon:'🪁', color:'#ffd23f',
    desc:'A rooftop courier who is never where you aimed.',
    q:{ name:'Spring Heel', icon:'⬆️', desc:'Jump high' },
    e:{ name:'Bolt Step', icon:'💨', desc:'Dash forward' },
    x:{ name:'Fan of Five', icon:'🔪', desc:'Five accurate knives' },
  },
  omen: {
    name:'BRINE', role:'CONTROLLER', icon:'🌫️', color:'#b6f24a',
    desc:'A fog keeper who decides what the enemy gets to see.',
    q:{ name:'Salt Glare', icon:'👁️', desc:'Blind enemies ahead' },
    e:{ name:'Fog Bank', icon:'☁️', desc:'Place a smoke' },
    x:{ name:'Short Cut', icon:'🌀', desc:'Teleport to a spot' },
  },
  sova: {
    name:'SONDE', role:'INITIATOR', icon:'🛰️', color:'#b388ff',
    desc:'A surveyor who maps the enemy before the fight starts.',
    q:{ name:'Ping Stake', icon:'📡', desc:'Reveal enemies on the minimap' },
    e:{ name:'Arc Stake', icon:'⚡', desc:'40 area damage' },
    x:{ name:'Bore Line', icon:'☄️', desc:'80 damage beam through walls' },
  },
  sage: {
    name:'PATCH', role:'SENTINEL', icon:'🧰', color:'#ff8ad8',
    desc:'A field mender who keeps the team standing.',
    q:{ name:'Mire Field', icon:'🕸️', desc:'Slowing field' },
    e:{ name:'Quick Stitch', icon:'🩹', desc:'+60 to self or aimed ally' },
    x:{ name:'Second Wind', icon:'✨', desc:'Revive a fallen ally' },
  },
};

export const SHOP_ITEMS = [
  { id:'classic', desc:'Basic pistol' },
  { id:'sheriff', desc:'Heavy revolver · one-tap headshot' },
  { id:'spectre', desc:'Fast SMG · strong up close' },
  { id:'phantom', desc:'Steady rifle · fires through smoke' },
  { id:'vandal', desc:'Accurate rifle · one-tap headshot' },
  { id:'operator', desc:'Bolt sniper · one-shot body' },
];
