import { expect, test } from 'vitest';
import { lineOfSight, pointInPolygon, resolveCircle, segmentsIntersect } from './geometry.ts';
import { rect } from './test-util.ts';

test('point in polygon', () => {
  const sq = rect(0, 0, 4, 4);
  expect(pointInPolygon({ x: 2, y: 2 }, sq)).toBe(true);
  expect(pointInPolygon({ x: 5, y: 2 }, sq)).toBe(false);
});

test('segment intersection', () => {
  const p = (x: number, y: number) => ({ x, y });
  expect(segmentsIntersect(p(0, 0), p(4, 4), p(0, 4), p(4, 0))).toBe(true);
  expect(segmentsIntersect(p(0, 0), p(1, 0), p(0, 1), p(1, 1))).toBe(false);
  expect(segmentsIntersect(p(0, 0), p(2, 0), p(1, 0), p(3, 0))).toBe(true);
});

test('line of sight is blocked by a wall edge and by a smoke circle', () => {
  const walls = [{ points: rect(4, 0, 6, 10) }];
  expect(lineOfSight({ x: 0, y: 5 }, { x: 10, y: 5 }, walls)).toBe(false);
  expect(lineOfSight({ x: 0, y: 15 }, { x: 10, y: 15 }, walls)).toBe(true);
  expect(lineOfSight({ x: 0, y: 15 }, { x: 10, y: 15 }, [], [{ pos: { x: 5, y: 16 }, radius: 2 }])).toBe(false);
  expect(lineOfSight({ x: 0, y: 15 }, { x: 10, y: 15 }, [], [{ pos: { x: 5, y: 20 }, radius: 2 }])).toBe(true);
});

test('circle is pushed out of a wall', () => {
  const walls = [{ points: rect(4, 0, 6, 10) }];
  const out = resolveCircle({ x: 3.8, y: 5 }, 0.4, walls);
  expect(out.x).toBeCloseTo(3.6);
  expect(out.y).toBeCloseTo(5);
});
