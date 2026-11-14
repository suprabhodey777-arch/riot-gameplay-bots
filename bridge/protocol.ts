import type { AgentAdvice } from '../analyzer/recommend.ts';
import type { AgentId, AbilityCharges, MapDef, MatchLog, PlayerInput, SimEvent, SimState, Team } from '../shared/contract.ts';
import type { Debrief, DrillResult, DrillSpec, Skill, WeaknessReport } from '../shared/coaching.ts';
import type { ArchetypeId } from '../analyzer/drills.ts';

export const PROTOCOL = 'bots-wire.2';
export type Protocol = typeof PROTOCOL | 'bots-wire.1';
export type BridgeInput = Omit<PlayerInput, 'agentId'> & { agentId?: string };
export type ClientMessage =
  | { type: 'hello'; protocol: Protocol }
  | { type: 'load_history'; sample: { skill: Skill; seed: number }; logs?: never }
  | { type: 'load_history'; logs: MatchLog[]; sample?: never }
  | { type: 'maps' }
  | { type: 'pick_map'; map: string }
  | { type: 'pick_agent'; agent: AgentId }
  | { type: 'next_drill' }
  | { type: 'rehearse'; index: number }
  | { type: 'versus'; archetype: ArchetypeId; skill: Skill }
  | { type: 'input'; input: BridgeInput }
  | { type: 'abandon' };

export interface Rules {
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
