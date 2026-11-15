import { PERSONAS, PERSONA_TRAITS } from '../shared/personas.ts';
import type { Archetype, BotTraits } from '../shared/coaching.ts';

/** The plain generic bot: average everything. */
export const DEFAULT_TRAITS: BotTraits = {
  reactionMs: 250,
  headAccuracy: 0.2,
  bodyAccuracy: 0.6,
  aggression: 0.5,
  offAngle: 0.5,
  patience: 0.5,
  utility: 0.5,
};

export const ARCHETYPES: Archetype[] = [
  {
    id: 'baseline',
    name: 'Baseline',
    description: 'The generic comparison bot: average aim, average timing, no adaptation.',
    traits: { ...DEFAULT_TRAITS },
  },
  {
    id: 'entry-duelist',
    name: 'Entry Duelist',
    description: 'Swings first and fast: runs straight at the site, little patience, quick reactions.',
    traits: {
      reactionMs: 210,
      headAccuracy: 0.25,
      bodyAccuracy: 0.65,
      aggression: 0.95,
      offAngle: 0.1,
      patience: 0.1,
      utility: 0.2,
    },
  },
  {
    id: 'patient-lurker',
    name: 'Patient Lurker',
    description: 'Waits away from the pack and commits late, walking the last stretch.',
    traits: {
      reactionMs: 260,
      headAccuracy: 0.2,
      bodyAccuracy: 0.6,
      aggression: 0.3,
      offAngle: 0.5,
      patience: 0.9,
      utility: 0.4,
    },
  },
  {
    id: 'off-angle-anchor',
    name: 'Off-Angle Anchor',
    description: 'Holds a site from an unusual angle instead of the common spot, and stays put.',
    traits: {
      reactionMs: 240,
      headAccuracy: 0.3,
      bodyAccuracy: 0.6,
      aggression: 0.25,
      offAngle: 0.95,
      patience: 0.7,
      utility: 0.3,
    },
  },
  {
    id: 'utility-controller',
    name: 'Utility Controller',
    description: 'Smokes the choke before it commits, and smokes off enemies it has lost.',
    traits: {
      reactionMs: 260,
      headAccuracy: 0.15,
      bodyAccuracy: 0.6,
      aggression: 0.45,
      offAngle: 0.3,
      patience: 0.5,
      utility: 1,
    },
  },
];

export const archetypeById = (id: string): Archetype => {
  const persona = PERSONAS.find(p => p.id === id);
  if (persona) return { id, name: persona.name, description: persona.style, traits: { ...PERSONA_TRAITS[persona.id] } };
  const a = ARCHETYPES.find((x) => x.id === id);
  if (!a) throw new Error(`Unknown archetype ${id}`);
  return a;
};
