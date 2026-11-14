import { describe, expect, it } from 'vitest';

import type { Vec3 } from '../contracts.ts';
import { loadMapExport } from './nav.ts';
import { DEFAULTS } from './policy-types.ts';
import { createPerception } from './view.ts';
import { createCombat } from './combat.ts';

const colliders = loadMapExport().colliders;

function seeded(seed: number): () => number {
  let s = seed >>> 0;
  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 4294967296;
  };
}

describe('fairness', () => {
  it('never fires before the reaction time after the target becomes visible', () => {
    // The bot faces +z (yaw PI); the target stands 10 m in front in the open.
    const self: Vec3 = { x: -3, y: 0, z: -20 };
    const target: Vec3 = { x: -3, y: 0, z: -10 };
    const visibleFrom = 1000;
    const reactionMs = DEFAULTS.reactionMs;
    const perception = createPerception({ reactionMs });
    const combat = createCombat({ yaw: Math.PI, pitch: 0 });
    const rng = seeded(7);
    const tracers: number[] = [];
    const visibleAt: number[] = [];

    for (let now = visibleFrom - 200; now <= visibleFrom + 1200; now += 50) {
      const enemies = now >= visibleFrom ? [{ name: 'dummy', pos: target, alive: true }] : [];
      const view = combat.view();
      const update = perception.update(
        now,
        { pos: self, yaw: view.yaw },
        enemies,
        colliders,
        [],
      );
      if (update.visible.length > 0) visibleAt.push(now);
      const out = combat.update({
        now,
        selfPos: self,
        visible: update.visible,
        weapon: 'vandal',
        moving: false,
        boxes: colliders,
        engage: true,
        targetName: null,
        skill: DEFAULTS,
        rng,
      });
      if (out.tracer) tracers.push(now);
    }

    // Perception itself waits for the reaction delay.
    expect(visibleAt.length).toBeGreaterThan(0);
    expect(Math.min(...visibleAt)).toBeGreaterThanOrEqual(visibleFrom + reactionMs);
    // And no tracer exists before the target could have been perceived.
    expect(tracers.length).toBeGreaterThan(0);
    expect(tracers.every((t) => t >= visibleFrom + reactionMs)).toBe(true);
  });
});
