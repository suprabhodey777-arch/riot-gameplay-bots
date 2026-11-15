import type { AgentId } from './contract.ts';
import type { BotTraits } from './coaching.ts';
import type { WeaponId } from './weapons.ts';

export type PersonaId = 'zephyr' | 'atlas' | 'ember' | 'wisp' | 'nova';
export interface Persona {
  id: PersonaId; name: string; style: string; tagline: string;
  lines: [string, string, string]; colors: [string, string]; agent: AgentId;
  signature: string; beatTip: string;
}
export const PERSONAS: Persona[] = [
  { id: 'zephyr', name: 'Zephyr', style: 'hyper-aggressive flick duelist', tagline: 'The corner is only the beginning.', lines: ['Keep up with the wind.', 'One more angle, one more chance.', 'I make the opening.'], colors: ['#ff4655', '#ffd166'], agent: 'rook', signature: 'Wide-swing reaction: 150 ms', beatTip: 'Zephyr wide-swings: hold a tight angle off the corner, not the corner itself.' },
  { id: 'atlas', name: 'Atlas', style: 'calm in-game leader', tagline: 'Five steps, one plan.', lines: ['Your space is our space.', 'I have the next contact.', 'Let the smoke draw the route.'], colors: ['#6c8cff', '#dce6ff'], agent: 'haze', signature: 'Trade window: 1.5 s', beatTip: 'Atlas follows a teammate: break sight after a kill and deny the trade.' },
  { id: 'ember', name: 'Ember', style: 'operator anchor', tagline: 'One angle is all I need.', lines: ['The long road ends here.', 'That perch is spent.', 'Find me at the next angle.'], colors: ['#ff914d', '#532d48'], agent: 'pike', signature: 'Shots per perch: 1', beatTip: 'Ember relocates after each shot: bait the Longshot, then cross during the move.' },
  { id: 'wisp', name: 'Wisp', style: 'lurker', tagline: 'The quiet lane remembers.', lines: ['Let them watch the noise.', 'I will arrive after the thunder.', 'Check the lane you forgot.'], colors: ['#b49bff', '#34495e'], agent: 'pike', signature: 'Silent flank speed: 3 m/s', beatTip: 'Wisp waits for your push: leave a flank watcher and clear behind you.' },
  { id: 'nova', name: 'Nova', style: 'utility general', tagline: 'Make the angle before the fight.', lines: ['Eyes first, feet second.', 'Your opening is on its way.', 'Follow the light, then take the space.'], colors: ['#53e0cb', '#f7f3ad'], agent: 'wren', signature: 'Site entry order: 2', beatTip: 'Nova recons then flashes for an entry: break the scan and turn from the pop flash.' },
];
export const isPersona = (id: unknown): id is PersonaId => PERSONAS.some(p => p.id === id);
export function personaById(id: string): Persona {
  const p = PERSONAS.find(p => p.id === id);
  if (!p) throw new Error(`Unknown persona ${id}`);
  return p;
}
export const PERSONA_TRAITS: Record<PersonaId, BotTraits> = {
  zephyr: { reactionMs: 150, headAccuracy: .48, bodyAccuracy: .7, aggression: 1, offAngle: .1, patience: 0, utility: .3 },
  atlas: { reactionMs: 250, headAccuracy: .25, bodyAccuracy: .7, aggression: .55, offAngle: .4, patience: .6, utility: 1 },
  ember: { reactionMs: 280, headAccuracy: .45, bodyAccuracy: .8, aggression: .15, offAngle: .95, patience: .8, utility: .3 },
  wisp: { reactionMs: 260, headAccuracy: .3, bodyAccuracy: .65, aggression: .35, offAngle: .8, patience: .95, utility: .3 },
  nova: { reactionMs: 250, headAccuracy: .25, bodyAccuracy: .65, aggression: .55, offAngle: .4, patience: .45, utility: 1 },
};
export const PERSONA_WEAPONS: Record<PersonaId, WeaponId[]> = {
  zephyr: ['viper', 'hammer'], atlas: ['lancer'], ember: ['longshot', 'hammer'], wisp: ['wasp', 'viper'], nova: ['lancer'],
};
