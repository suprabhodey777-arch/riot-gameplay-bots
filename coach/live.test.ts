import { describe, expect, it } from 'vitest';
import { debrief } from './debrief.ts';
import { apiKeyFromEnv } from './env.ts';
import type { CoachInput } from './facts.ts';

const key = apiKeyFromEnv();

describe.skipIf(!key)('live Anthropic coach', () => {
  it('writes a debrief that is either llm-checked or a clean fallback', async () => {
    const input: CoachInput = {
      report: {
        playerId: 'player-1',
        role: 'duelist',
        matchesRead: 3,
        findings: [
          { skill: 'entry', rate: 0.2, benchmark: 0.55, severity: 0.8, sampleSize: 5, summary: 'Died first on entry in 4 of 5 rounds.', evidence: [] },
        ],
      },
    };
    const reasons: string[] = [];
    const out = await debrief(input, { timeoutMs: 20000, onFallback: (r, d) => reasons.push(`${r}: ${d}`) });
    expect(reasons.length === 0).toBe(out.source === 'llm');
    expect(['llm', 'template']).toContain(out.source);
    expect(out.text.length).toBeGreaterThan(0);
  });
});
