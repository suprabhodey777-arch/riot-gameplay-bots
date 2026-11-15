import type { ArmorId, Loadout, RoundType, WeaponId, Weapon } from '../shared/weapons.ts';
import type { AgentAdvice } from '../analyzer/recommend.ts';
import type { AgentId, AbilityCharges, MapDef, MatchLog, PlayerInput, SimEvent, SimState, Team } from '../shared/contract.ts';
import type { Debrief, DrillResult, DrillSpec, Skill, WeaknessReport } from '../shared/coaching.ts';
import type { ArchetypeId } from '../analyzer/drills.ts';

export const PROTOCOL = 'bots-wire.3';
export type Protocol = typeof PROTOCOL | 'bots-wire.2' | 'bots-wire.1';
export type BridgeInput = Omit<PlayerInput, 'agentId'> & { agentId?: string };
export type ClientMessage =
  | { type: 'hello'; protocol: Protocol }
  | { type: 'load_history'; sample: { skill: Skill; seed: number }; logs?: never }
  | { type: 'load_history'; logs: MatchLog[]; sample?: never }
  | { type: 'maps' }
  | { type: 'pick_map'; map: string }
  | { type: 'pick_agent'; agent: AgentId }
  | { type: 'next_drill' }
  | { type: 'retry' }
  | { type: 'rehearse'; index: number }
  | { type: 'versus'; archetype: ArchetypeId; skill: Skill }
  | { type: 'input'; input: BridgeInput }
  | { type: 'match'; map: string; agent: AgentId }
  | { type: 'buy' | 'sell'; item: WeaponId | ArmorId }
  | { type: 'ready' }
  | { type: 'abandon' };

export interface Rules {
  weapons: Record<WeaponId, Weapon>;
  flashMaxRangeM?: number; flashFuseS?: number; flashBlindS?: number;
  dashDistanceM?: number; reconMaxRangeM?: number; reconRadiusM?: number; reconRevealS?: number;
  runHearingM?: number; walkSpeedMps?: number; crouchSpeedMps?: number;
  agents?: Record<AgentId, { role: string; kit: AbilityCharges }>;
  tickRate: number;
  fireIntervalS: number;
  smokeMaxRangeM: number;
  plantTimeS: number;
  defuseTimeS: number;
  moveSpeedMps: number;
}
export type ServerMessage =
  | { type: 'buy_phase'; round: number; roundType: RoundType; credits: number; timeS: number; score: { player: number; enemy: number }; side: Team; team: { id: string; credits: number; loadout: Loadout }[] }
  | { type: 'loadout'; credits: number; primary: WeaponId | null; secondary: WeaponId; armor: ArmorId | null }
  | { type: 'round_result'; winner: Team; how: 'elimination' | 'detonated' | 'defused' | 'time'; creditsEarned: number; score: { player: number; enemy: number }; mvp: string }
  | { type: 'match_end'; score: { player: number; enemy: number }; won: boolean; stats: { kills: number; deaths: number; headshotPct: number } }

  | { type: 'maps'; maps: { id: string; name: string }[] }
  | { type: 'map'; map: MapDef }
  | { type: 'agent'; agent: AgentId; kit: AbilityCharges }
  | { type: 'hello'; protocol: Protocol; map: MapDef; rules: Rules }
  | { type: 'report'; report: WeaknessReport; rehearsals: { index: number; title: string; brief: string; skill: Skill }[];
      staircases: Record<Skill, { difficulty: number }>; played: number; agentAdvice?: AgentAdvice }
  | { type: 'drill'; spec: DrillSpec; intro: string; playerId: string; playerTeam: Team; playerAgent?: AgentId;
      bots: { id: string; team: Team; archetype: string; agent?: AgentId }[]; state: SimState }
  | { type: 'step'; state: SimState; events: SimEvent[]; thoughts: Record<string, string>; done: boolean }
  | { type: 'result'; result: DrillResult; success: boolean; headline: string; staircaseBefore: number; staircaseAfter: number }
  | ({ type: 'debrief' } & Debrief)
  | { type: 'error'; message: string };
