import { describe, expect, it } from 'vitest';
import { supported } from '../../coach/check.ts';
import { apiKeyFromEnv } from '../../coach/env.ts';
import type { DrillInput } from './facts.ts';
import { drillFacts } from './facts.ts';
import { drillDebrief } from './debrief.ts';

const key = apiKeyFromEnv();

describe.skipIf(!key)('live Anthropic ghost coach', () => {
  it('writes a debrief that is either llm-checked or a clean fallback', async () => {
    const input: DrillInput = {
      player: 'player-1',
      ghostOf: 'rival-1',
      scenarioId: 'moment-1',
      attempts: [
        {
          won: false,
          durationS: 12,
          events: [],
          traces: [
            {
              bot: 'ghost-1',
              ghostOf: 'rival-1',
              round: 1,
              t: 1,
              action: { kind: 'push', zone: 'a-site' },
              reason: 'site looks empty',
              matched: null,
              matchDistance: null,
              fallback: true,
              fallbackReason: 'no-close-recording',
              skillScale: 1,
              situation: {
                selfPos: { x: 0, z: 0 },
                selfZone: 'mid',
                selfHp: 100,
                selfArmor: 0,
                weapon: 'vandal',
                ammo: null,
                abilitiesReady: { Q: true, E: true, X: false },
                enemyVisible: false,
                enemyRel: null,
                enemyLastSeenZone: null,
                sinceSeen: null,
                tookDamageRecent: false,
                gotKillRecent: false,
                side: 'defense',
                spikeState: 'none',
                roundTime: 30,
                teammatesAlive: 1,
                enemiesAlive: 1,
              },
            },
          ],
        },
      ],
    };

    const out = await drillDebrief(input, { timeoutMs: 20000 });
    expect(['llm', 'template']).toContain(out.source);
    expect(out.text.length).toBeGreaterThan(0);
    expect(supported(out.text, drillFacts(input).sentences)).toBe(true);
  });
});
