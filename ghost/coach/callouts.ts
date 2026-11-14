/**
 * Overlay callouts for the ghost's actions. No model is involved: each line is
 * a short phrase the player can read while the drill runs. Quiet actions
 * (holding, peeking, buying) return null so the overlay stays clean.
 */
import type { Action } from '../contracts.ts';

export type ZoneName = (id: string) => string;

/** The line to show for one action, or null when it needs no callout. */
export function callout(action: Action, zoneName: ZoneName): string | null {
  switch (action.kind) {
    case 'push':
      return `Pushing ${zoneName(action.zone)}`;
    case 'retreat':
      return `Falling back to ${zoneName(action.zone)}`;
    case 'rotate':
      return `Rotating ${zoneName(action.zone)}`;
    case 'use_ability':
      return 'Utility out';
    case 'plant':
      return `Planting ${action.site}`;
    case 'defuse':
      return 'Defusing';
    case 'fight':
      return `Fighting ${action.target}`;
    case 'hold':
    case 'peek_wide':
    case 'peek_jiggle':
    case 'buy':
      return null;
  }
}

/** Everything that makes two actions of the same kind distinct. */
function destination(action: Action): string {
  switch (action.kind) {
    case 'hold':
      return `hold:${action.pos.x},${action.pos.z}:${action.watchYaw}`;
    case 'peek_wide':
    case 'peek_jiggle':
      return `${action.kind}:${action.corner.x},${action.corner.z}:${action.dir}`;
    case 'fight':
      return `fight:${action.target}`;
    case 'push':
    case 'retreat':
    case 'rotate':
      return `${action.kind}:${action.zone}`;
    case 'use_ability':
      return `use_ability:${action.slot}:${action.at ? `${action.at.x},${action.at.z}` : 'none'}`;
    case 'plant':
    case 'defuse':
      return `${action.kind}:${action.site}`;
    case 'buy':
      return `buy:${action.item}`;
  }
}

/** A line only when the action kind or where it goes changed. */
export function calloutChanged(
  prev: Action | null,
  next: Action,
  zoneName: ZoneName,
): string | null {
  if (prev && prev.kind === next.kind && destination(prev) === destination(next)) {
    return null;
  }
  return callout(next, zoneName);
}
