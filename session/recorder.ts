/**
 * Turns played rounds into a MatchLog, so real play from the 3D client (or any
 * headless match) can feed the analyzer.
 *
 * Usage: `beginRound(stateAtTick0, playerRole)`, then `push(events)` with every
 * step's events, and `endRound()` when the round is over (or let `push` end it
 * on a `round_end` event). Teams come from the agents in the first state, which
 * is what `MatchLog.rounds[].teams` is for. Rounds that never ended are dropped.
 * Event ticks are relative to the round start, as the contract wants.
 */
import type { MatchLog, SimEvent, SimState, Team } from '../shared/contract.ts';

type Round = MatchLog['rounds'][number];
type Role = Round['playerRole'];

export interface Recorder {
  /** Starts a round from the sim's state at tick 0. */
  beginRound(initial: SimState, role?: Role, economy?: Pick<Round, 'roundType' | 'loadout'>): void;
  /** Adds one step's events; a `round_end` event closes the round. */
  push(events: SimEvent[]): void;
  /** Closes the open round (needs a winner: from a round_end event or `winner`). */
  endRound(winner?: Team): void;
  /** The match so far. */
  toLog(): MatchLog;
  /** Rounds closed so far. */
  rounds(): number;
}

export function createRecorder(opts: { matchId: string; mapId: string; playerId: string; role?: Role }): Recorder {
  const rounds: Round[] = [];
  let open: { teams: Record<string, Team>; playerTeam: Team; role: Role; events: SimEvent[]; economy?: Pick<Round, 'roundType' | 'loadout'> } | null = null;

  const close = (winner?: Team): void => {
    if (!open) return;
    const end = open.events.find((e): e is SimEvent & { type: 'round_end' } => e.type === 'round_end');
    const w = winner ?? end?.winner;
    if (w) {
      rounds.push({
        index: rounds.length,
        ...open.economy,
        playerTeam: open.playerTeam,
        playerRole: open.role,
        teams: open.teams,
        events: open.events,
        winner: w,
      });
    }
    open = null;
  };

  return {
    beginRound(initial, role, economy) {
      close();
      const me = initial.agents.find((a) => a.id === opts.playerId);
      if (!me) throw new Error(`recorder: no agent ${opts.playerId} in the state`);
      const teams: Record<string, Team> = {};
      for (const a of initial.agents) teams[a.id] = a.team;
      open = { teams, playerTeam: me.team, economy, role: role ?? opts.role ?? 'duelist', events: [] };
    },
    push(events) {
      if (!open) return;
      for (const e of events) open.events.push(e);
      if (events.some((e) => e.type === 'round_end')) close();
    },
    endRound: close,
    toLog: () => ({ matchId: opts.matchId, mapId: opts.mapId, playerId: opts.playerId, rounds: rounds.map((r) => ({ ...r, events: [...r.events] })) }),
    rounds: () => rounds.length,
  };
}
