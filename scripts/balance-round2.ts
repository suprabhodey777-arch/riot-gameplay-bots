/** Reproduce the Round 2 balance audit: node scripts/balance-round2.ts.
 * Seeds 1-30, mirrored 5v5 teams; archetype i rotates by (i + seed) % 5.
 * Agent ids are part of the deterministic setup (site plans and bot RNG use them).
 */
import { createMatch, runHeadless } from '../bot/match.ts';
import { loadMap } from '../shared/maps/index.ts';
const styles = ['entry-duelist','utility-controller','patient-lurker','off-angle-anchor','baseline'];
for (const mapId of ['saltworks','ironvale']) {
 const counts: Record<string,number> = {}; let plants=0;
 for(let seed=1;seed<=30;seed++) {
 const agents = (['attack','defense'] as const).flatMap(team => Array.from({length:5},(_,i)=>({id:`${team}-${i}`,team,isBot:true,archetypeId:styles[(i+seed)%5]})));
 const r=runHeadless(createMatch({map:loadMap(mapId),seed,agents}),3000);
 const end=r.events.slice().reverse().find(e=>e.type==='round_end'); const key=`${r.state.winner}:${end?.type==='round_end'?end.reason:'unfinished'}`; counts[key]=(counts[key]??0)+1;
 if(r.events.some(e=>e.type==='plant')) plants++;
 console.log(mapId,seed,r.state.winner,r.ticks/20,end?.type==='round_end'?end.reason:'unfinished',r.events.find(e=>e.type==='plant')?.tick);
 }
 const attack = Object.entries(counts).filter(([key]) => key.startsWith('attack:')).reduce((sum, [, n]) => sum + n, 0);
 console.log('TOTAL',mapId,counts,'plants',plants,`attack ${attack}/30 (${(attack / 30 * 100).toFixed(1)}%)`);
}
