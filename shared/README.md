# shared/

`contract.ts` is the cross-lane contract (`CONTRACT_VERSION = 'v2'`): types only. Maps live in
`maps/*.json` and load through `maps/index.ts`. `maps/test-box.json` is a placeholder for tests;
the real map is owned by the 3D team and is added the same way (`registerMap`).

## Using the sim from the 3D client

The sim is the source of truth. The client sends input, renders the state it gets back and never
mutates it (`state()` and `step()` return copies). Each round is a fresh sim starting at tick 0.

```ts
import type { PlayerInput, SimState } from '../shared/contract.ts';
import { loadMap } from '../shared/maps/index.ts';
import { createSim } from '../sim/sim.ts';

const sim = createSim({
  map: loadMap('test-box'), seed: 1,
  agents: [{ id: 'me', team: 'attack', isBot: false }, { id: 'bot1', team: 'defense', isBot: true }],
});
let state: SimState = sim.state();
let input: PlayerInput = { agentId: 'me', move: { x: 0, y: 0 }, yaw: 0, walk: false, crouch: false,
  fire: null, smokeAt: null, plantHeld: false, defuseHeld: false }; // updated from keyboard/mouse
setInterval(() => {
  const { state: next, events } = sim.step([input]); // 20 Hz
  state = next; // render this; events drive the kill feed, tracers, sounds
  input = { ...input, fire: null, smokeAt: null }; // one-shot fields are consumed per tick
}, 1000 / state.tickRate);
```

The client raycasts shots itself and sends `fire: { targetId, result }`; the sim still checks line
of sight and smokes. Planting needs `plantHeld` for 4 s inside a site; defusing needs `defuseHeld`
for 7 s within 2 m of the spike.
