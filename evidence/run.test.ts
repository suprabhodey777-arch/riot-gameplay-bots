import { expect, test } from 'vitest';
import { DEFAULTS, markdown, runEvidence } from './run.ts';

test('the evidence harness runs, is deterministic, and no one fires before their reaction time', async () => {
  const cfg = { ...DEFAULTS, n: 4, budget: 4, forms: 1, bootstrap: 50 };
  const a = await runEvidence(cfg);
  const b = await runEvidence(cfg);
  expect(JSON.stringify(a)).toBe(JSON.stringify(b));
  expect(a.fairness.fires).toBeGreaterThan(0);
  expect(a.fairness.tooEarly).toBe(0);
  expect(a.rows).toHaveLength(4);
  expect(markdown(a)).toContain('simulated');
}, 60000);
