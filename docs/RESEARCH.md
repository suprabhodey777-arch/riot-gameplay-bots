# Research: what is wrong with bots in Riot's games

Gathered 14 Nov 2026 by web search. Riot's own pages could not be opened directly
from the research environment, so Riot quotes below are taken from search results
and secondary coverage that reproduce them. Check the primary page before putting
a quote on a slide.

Each finding is marked by how much weight it can carry:

- **Riot** – stated by Riot (dev blog, patch notes, staff comment)
- **Press** – reported by a news outlet
- **Players** – forum or Reddit posts; sentiment, not data
- **Thin** – a single or unverified source

## 1. League of Legends

### 1.1 Bot games do not prepare players for PvP

- **Riot.** "Our data shows that regardless of how many co-op vs. AI games a new
  player played prior to entering a PvP queue, these games would not improve the
  new player's chances of winning." Riot also said bots "aren't great at helping
  players practice at a level that'd win them a PvP game."
  [/dev: Leveling Up Bots](https://www.leagueoflegends.com/en-us/news/dev/dev-leveling-up-bots/),
  quoted by [ComicBook](https://comicbook.com/gaming/news/league-of-legends-upgraded-bots)
  and [Gamepressure](https://www.gamepressure.com/newsroom/lol-will-introduce-big-changes-to-ai-bots/z3555d).
- **Press.** A Co-op vs. AI match "doesn't look at all like a game of League as
  played by modern players."
  [PC Gamer](https://pcgamer.com/league-of-legends-ai-bots-are-getting-a-major-reboot)
- **Players.** "Bots fail as a practice tool, and as a result, you get a bunch of
  people who first time champions in actual games."
  [NA boards (archived, old)](https://boards.na.leagueoflegends.com/en/c/gameplay-balance/KgN8zOz6-the-ai-bots-are-a-sad-excuse-as-a-practice-tool)

### 1.2 Riot's response so far

- **Riot.** Patch 14.6 (20 Mar 2024) rebuilt the bots on behavior trees with
  subtrees for fighting, escaping, moving, jungling, buying and levelling. Bots
  now jungle, gank and take objectives as a team. The bots were pulled after a
  September 2023 PBE test and about 80 bugs were fixed before release.
  [/dev: New Bot AI, Oh My!](https://www.leagueoflegends.com/en-us/news/dev/dev-new-bot-ai-oh-my-coming-14-6/),
  [RiftFeed](https://riftfeed.gg/lol-news/lol-practice-bots-are-ready-and-theyll-jungle-better-than-you)
- **Riot.** The stated purpose is "a space to learn and grow" for new players and
  "a lower-stress mode" for experienced ones. Not everyone playing bots wants
  coaching.
- **Open question.** I found no Riot statement on whether the 14.6 bots improved
  PvP outcomes. The mentor deck says combo champions are still unsupported.

### 1.3 Difficulty is a fixed ladder

- **Riot.** A Riot developer wrote that the reworked bots were "way too difficult
  right now for new players so prioritizing ways to reduce their difficulty."
  [dev tracker](https://devtrackers.gg/u/RiotDashiJador?page=4)
- **Players.** Co-op vs. AI matchmaking uses summoner level, not skill.
  [MOBAfire blog](https://www.mobafire.com/profile/satella-309443/blog/state-of-co-op-vs-ai)
- Tiers are Intro, Beginner and Intermediate; there is no per-player fitting.

### 1.4 Bots feel both too dumb and unfair

- **Players.** Bots "dodging skillshots before they're fired", escaping
  knockbacks, and forgetting a player hiding in a bush (same NA boards post).
- **Players.** A December 2024 PBE thread argued beginner bots should not dodge
  at all.
  [r/LeaguePBE](https://reddit.birdcat.cafe/r/LeaguePBE/comments/1h3zawp/1424_beginner_bots_shouldnt_be_able_to_dodge)
- **Riot.** The 2014 bots dodged by reading facing direction and cooldowns, and
  were deliberately not high-level players.
  [Making a More Human Bot (archive)](https://archive.runeterra.net/na/riot-official/vH9A4wxi)
- **Thin.** Older wiki notes say low-tier bots skip item actives and summoner
  spells and Intro bots always use the same five champions.
  [Fandom wiki](https://leagueoflegends.fandom.com/wiki/Co-op_vs._AI)

### 1.5 No feedback, no sparring partner

- **Riot.** The Practice Tool is "a training gym"; a multiplayer sandbox would be
  "a significantly larger undertaking."
  [Ask Riot, 2017](https://nexus.leagueoflegends.com/en-us/2017/06/ask-riot-practice-tool-stays-solo/)
- **Players.** Requests for a 1v1 or live target in the Practice Tool, and
  complaints that not every champion has a bot.
  [PBE boards (old)](https://boards.pbe.leagueoflegends.com/en/c/champions-gameplay-feedback/Kd4qYQiA-why-cant-you-play-1vs1-or-practice-tool)
- **Press.** Third-party trainers exist to fill the gap: Skill Gap aims to be
  "the AimLab for League of Legends"; LoLDodgeGame is used for dodge practice.
  [Esports News UK](https://esports-news.co.uk/2024/12/27/skill-gap-lol-training-tool-aimlab),
  [Dignitas](https://dignitas.gg/articles/going-above-and-beyond-a-guide-on-using-loldodgegame-to-practice-league-of-legends-mechanics)

### 1.6 Coverage and reliability

- Hand-tuning per champion does not scale (163 champions per the mentor deck).
- **Riot.** Mundo bots were removed from custom games in 14.18; a Garen bot
  idle-state bug was fixed in 26.03; push logic was changed to stop bots getting
  stuck. [LoL wiki: Bots](https://wiki.leagueoflegends.com/en-us/Bots),
  [V26.03](https://wiki.leagueoflegends.com/en-us/V26.03)

### 1.7 Side effects on the wider game

- **Riot.** Patch 26.2 moved Co-op vs. AI onto Swiftplay rules; 26.03 (4 Feb
  2026) reverted it because queue times rose "drastically."
  [V26.03](https://wiki.leagueoflegends.com/en-us/V26.03)
- **Riot.** "As of January 2025, new and returning players may experience some
  bots filling their lobby for a number of Normal games."
  [LoL wiki: Bots](https://wiki.leagueoflegends.com/en-us/Bots)
- **Riot / Press.** Levelling-bot accounts used to flood Co-op vs. AI. After
  Vanguard, bot play hours fell from over a million a day to a small fraction
  (secondary sources disagree on the exact figure) and about 3.5 million unsold
  bot accounts were removed. This is an anti-cheat problem, not a gameplay-bot
  problem. [Vanguard retrospective](https://leagueoflegends.com/en-us/news/dev/dev-vanguard-x-lol-retrospective/)

### 1.8 Why this matters now

- **Press.** Senior Riot staff say the player base is ageing and fewer new
  players are joining; smurfs are "endemic."
  [PC Gamer](https://www.pcgamer.com/games/moba/senior-riot-devs-say-the-league-of-legends-playerbase-is-getting-older-with-fewer-newbies-jumping-in-candidly-its-not-the-same-situation-it-was-10-years-ago)
- **Press.** "League Next" (planned 2027) includes a revamped new player
  experience. No bot details are public.
  [Hotspawn](https://www.hotspawn.com/league-of-legends/news/league-next-league-of-legends-update)

## 2. VALORANT

- **Players / Press.** Range bots are targets, not opponents. Patch 9.02 removed
  the plant and defuse scenarios on PC; players called it a downgrade.
  [Dot Esports](https://dotesports.com/valorant/news/valorant-patch-9-02-downgraded-the-practice-range-on-pc-and-its-infuriating-players)
- **Players.** Long-standing requests for bots in custom games at several
  difficulty levels. Riot cited server cost.
  [dev tracker](https://devtrackers.gg/valorant/p/3605f027-ai-bots-should-be-introduced-in-custom-games)
- **Riot.** Onboarding has an optional bot training match (console first, then
  PC with 9.02). Patch 13.05 added a bot-count option in the Range.
  [Patch 13.05](https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-13-05/)
- **Riot.** Leavers are handled with penalties, not bot replacements; about 27
  of every 1,000 players were AFK in 2022.
  [The Loadout](https://www.theloadout.com/valorant/afk-players-august-2022),
  [Sportskeeda](https://www.sportskeeda.com/valorant/should-valorant-add-bots-replace-leavers)
- **Thin.** One August 2026 report claims Riot is studying bots that take over
  for AFK players. Unconfirmed.
  [TalkEsport](https://www.talkesport.com/news/valorant/bots-replacing-afk-players/)

## 3. Other Riot games

- **2XKO. Press.** Riot trains 2XKO bots with reinforcement learning. Wes Kerr
  (AI and Games Conference, Nov 2025): research de-risks agents, central tech
  productionises them, live teams monitor them; Riot does not ship a feature
  that cannot fail safely, and relying on "hope or copium" risks everything.
  [Video Games Industry Memo](https://www.videogamesindustrymemo.com/p/four-things-we-learned-at-this-years).
  I found no player complaints about 2XKO bots.
- **Wild Rift. Thin.** Individual complaints about bot accounts in ranked. No
  Riot statement found.
  [Şikayetvar](https://www.sikayetvar.com/en/riot-games-us/wild-rift-bots-ruined-my-diamond-ranked-game)
- **TFT, Legends of Runeterra.** Not researched.

## 4. Outside Riot

- **Press.** Hidden bots in matchmaking draw strong backlash (Marvel Rivals,
  Fortnite, Call of Duty). "Games that try to sneak bots into multiplayer
  matches are automatically trash."
  [PC Gamer](https://www.pcgamer.com/games/fps/games-that-try-to-sneak-bots-into-multiplayer-matches-are-automatically-trash/)
- **Research.** Training is fastest at roughly 85% success, for
  gradient-descent learners on simple binary tasks; the authors caution against
  over-generalising. Wilson et al., Nature Communications 2019.
  [paper](https://pmc.ncbi.nlm.nih.gov/articles/PMC6831579)
- **From the mentor deck (not re-verified):** a cheap adversary beat superhuman
  KataGo over 97% of the time (Wang et al., ICML 2023); self-play agents
  coordinate badly with humans (Carroll et al., NeurIPS 2019); shipped AI
  teammates keep the LLM for talking and explaining, with fast decisions in a
  behavior tree or small policy.

## 5. The complaints, grouped

| # | Complaint | Strongest evidence |
|---|---|---|
| A | Bot games do not transfer to PvP | Riot's own data (1.1) |
| B | Fixed tiers fit almost no one | Riot dev comment (1.3) |
| C | Bots are predictable yet react inhumanly | Player posts (1.4) |
| D | No feedback or real sparring partner | Riot on Practice Tool; third-party tools (1.5) |
| E | Per-champion tuning does not scale | Removed and buggy bots (1.6); mentor deck |
| F | Bots can fail badly in production | Riot 2XKO talk (3) |
| G | Hidden bots erode trust | Press (4); League lobby fill (1.7) |
| H | Levelling-bot accounts | Largely handled by Vanguard (1.7) |
| I | No bot opponents or leaver fill in VALORANT | Player requests (2) |

A to G share one root: the bots are built as opponents to beat, and none is built
or measured as a teacher. H is anti-cheat. I is a different product.

## 6. Gaps in this research

- Reddit is poorly indexed by the search tool, so recent player sentiment on the
  14.6 bots is under-represented.
- Riot's primary pages were not read in full.
- No data on how many players use Co-op vs. AI or why they leave.
- Wild Rift, TFT and 2XKO player sentiment is thin or missing.
