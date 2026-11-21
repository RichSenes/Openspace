# Interface study

Story S2.1 of [brain-stories.md](../brain-stories.md): what to build Openspace's new interface
with. It looks at every project the person sent, searches for more in each area, and ends with
what to prototype first (S2.2) and the questions only the person can answer.

Facts were checked on 2026-11-21 against the projects' own pages: GitHub (stars, licence, last
push, via the GitHub API), npm (version, licence, release date), Hugging Face (model licences),
Bundlephobia (size) and the vendors' sites. When something could not be checked it says so.
Stars are rounded; they move every day.

**How to read the sizes.** "gz" is the whole package minified and gzipped as Bundlephobia
measures it. Most of these libraries tree-shake, so what Openspace would really ship is often
smaller. Today the frontend ships React 19.3, PixiJS 8.23, TanStack Query, SignalR, marked,
DOMPurify and lucide-react, so every number below is *added* weight.

**Verdicts.** *Use*: a dependency or copied code, licence fine. *Take ideas*: study it, write our
own. *Reference only*: look, do not copy (licence or fit forbids it). *Avoid*: do not depend on it.

## Contents

- [Executive summary](#executive-summary)
- [Where Openspace starts from](#where-openspace-starts-from)
- [1. The projects the person sent](#1-the-projects-the-person-sent)
- [2. Animation for React](#2-animation-for-react)
- [3. Pixel art: assets, tools, light and particles](#3-pixel-art-assets-tools-light-and-particles)
- [4. The map: graph visualisation](#4-the-map-graph-visualisation)
- [5. Components, chat, wiki and whiteboard](#5-components-chat-wiki-and-whiteboard)
- [6. Shaders and 3D: the dome and Shiori's face](#6-shaders-and-3d-the-dome-and-shioris-face)
- [7. Voice](#7-voice)
- [8. Ready-made projects on GitHub](#8-ready-made-projects-on-github)
- [9. The second prototype: Shiori at the centre](#9-the-second-prototype-shiori-at-the-centre)
- [Licences and risks](#licences-and-risks)
- [Proposed prototypes](#proposed-prototypes)
- [Open questions for the person](#open-questions-for-the-person)

## Executive summary

### Top picks per area

| Area | Pick | Why |
|---|---|---|
| UI motion | **Motion** (`motion`, MIT) + React's own **`<ViewTransition>`** | Springs, layout and shared-element animation in React; `ViewTransition` is a stable export of the React 19.3.1 Openspace already has, so page and note transitions cost nothing |
| Micro-interactions | **interior.dev** (MIT, copy-paste) | 54 components whose behaviour lives in a hook with no class names: we keep the hook and restyle in plain CSS. The segmented control is the starting point for the chat's mode switch |
| Pixel effects | **pixelfx** (MIT) | 244 code-made pixel effects (weather, sci-fi, arcane, UI, critters) that draw into a pixel buffer: they fit the building's code-drawn art and its 16 px grid |
| Pixel world rendering | **PixiJS v8** (already there) + **pixi-filters** v6 (MIT) + our own light filter | The filters (CRT, glow, god rays, bloom, shockwave, pixelate) already support v8; the community lights and layers plugins stop at v7, so lighting stays ours, as a v8 filter on top of today's `lighting.ts` |
| Particles | PixiJS v8's own `ParticleContainer`, or **revolt-fx** (MIT, v8) | `@pixi/particle-emitter` is v6/v7 only |
| Pixel assets | **Kenney** and **0x72** (CC0), **Ninja Adventure** (CC0) | The only free packs that can live in a public MIT repository. Everything nicer (LimeZu, Pixel Frog) forbids redistribution |
| Map | **d3-force** (ISC) for the layout, drawn in **PixiJS**; **sigma.js** (MIT) if the vault grows past a few thousand notes | One renderer for building and map, so a "galaxy" or "city" theme reuses the same pixel and glow tools |
| Command palette | **cmdk** (MIT, 16 KB gz) or interior's command palette | Unstyled, keyboard-first |
| Primitives | **Radix Primitives** (MIT), one package at a time (popover, tooltip, tabs) | Accessible behaviour without a style system; Openspace keeps its own CSS |
| Wiki | today's **marked + DOMPurify** with a small `[[wikilink]]` extension | Nothing new to ship; Quartz and SilverBullet (MIT) as references for backlinks and transclusion |
| Whiteboard | **roughjs** + **rough-notation** (MIT, 9 + 4 KB gz) and SVG stroke animation | A board that "writes itself" without a full whiteboard engine; Excalidraw is too heavy, tldraw's licence forbids it |
| Shiori's face | Our own **WebGL shader** in a small canvas: Paper Shaders (Apache-2.0) and ElevenLabs UI's orb (MIT) as starting points | A light, state-driven orb (attention, thinking, emotion) with no 3D framework; react-three-fiber only if a direction needs real 3D |
| Listening | **whisper.cpp** server (MIT, models MIT) with **Silero VAD** (MIT) via `@ricky0123/vad-web` (ISC) | Local, Italian/English/Japanese, already used on this kind of machine |
| Speaking | Listening test between **Kokoro** (Apache-2.0), **Qwen3-TTS** and **CosyVoice 3** (both Apache-2.0, Italian and Japanese) and **Piper** with CC0/CC-BY Italian voices | Kokoro is light but its authors grade Italian C; the two newer Apache models have Italian and Japanese but need more hardware |
| Wake word | **LiveKit WakeWord** (Apache-2.0, runs in the browser) or **openWakeWord** (code Apache-2.0) trained on "Shiori" | Porcupine no longer has a free tier (see [Voice](#7-voice)) |

### Three candidate directions

Each is a whole look for the same screens: the building with the brain's place, Shiori's chat
and face, the map. They share the stack (React, PixiJS, Motion, plain CSS); they differ in how far
they leave pixel art.

**A. Living diorama (pixel all the way).** Everything is in the world's own grain. Panels are
pixel-framed (9-slice borders drawn like the furniture), headings in Silkscreen, body text in
IBM Plex. Shiori's face is two big pixel-art eyes in a bubble of dithered light, blinking and
narrowing like the inhabitants'. The science lab's brain dome is a pixel-art glass dome with
dithered particles orbiting. The map is a pixel night sky: notes are stars, offices are
constellations, links twinkle. The whiteboard is a pixel board where a marker sprite writes.
*Uses*: PixiJS + pixi-filters (glow, bloom, CRT for screens), pixelfx (weather, sparkles,
transitions), Motion for the DOM panels, d3-force for the map, pixelarticons (MIT) beside lucide.
*Strengths*: one coherent world, cheap to ship (almost no new weight), matches today's art rules.
*Risks*: long text panels in a pixel frame tire the eye; detailed art is a lot of drawing.

**B. Holo lab (pixel world, light for the mind).** The building stays pixel art; everything
that belongs to Shiori is made of light: a smooth glowing orb or neural web for the face, glass
panels for the chat, a galaxy map with bloom. The pixel world is the body, the light is the mind.
The brain dome in the science world is where the two meet: pixel dome, shader glow inside.
*Uses*: a WebGL shader orb (Paper Shaders' neuro-noise/dot-orbit/smoke-ring as studies, or the
ElevenLabs orb), optionally react-three-fiber + drei + postprocessing lazy-loaded only on
Shiori's views, a graph with glow (force-graph or 3d-force-graph), Motion and View Transitions,
CSS glass (backdrop blur; SVG displacement only where the browser supports it).
*Strengths*: the most "stunning" first impression; Shiori clearly is not one of the inhabitants.
*Risks*: two visual languages to keep in balance; three.js adds ~180 KB gz if used; glass and
glow need care for WCAG AA contrast.

**C. Storybook atlas (ink and paper over pixels).** Warm, hand-made: the wiki reads like a
notebook, the whiteboard is sketched (rough strokes drawn live), the map is a hand-drawn atlas
(offices as islands of an archipelago, notes as villages, links as roads), Shiori's face is a
neural web of ink lines that tighten when it thinks. The building stays pixel art, framed like an
illustration in the book.
*Uses*: roughjs and rough-notation, SVG path animation, Motion, sigma.js or Pixi with custom
node drawing for the atlas, paper and grain textures from Paper Shaders (paper-texture,
grain-gradient) or plain CSS.
*Strengths*: fits the "worlds" idea (village, archipelago) and the brain as a book of notes;
calm, readable for long sessions.
*Risks*: less "tech"; hand-drawn looks can feel twee if overdone.

A mix is natural: A for the building and inhabitants in every direction, then B or C for
Shiori, the map and the whiteboard.

## Where Openspace starts from

- **Stack**: React 19.3 + TypeScript + Vite 8, PixiJS 8.23 for the building
  (`src/frontend/src/building`), plain CSS with tokens in `src/frontend/src/index.css` (dark
  surfaces, accent `#f2b84b`, Silkscreen / IBM Plex Sans / IBM Plex Mono), lucide icons, no
  Tailwind.
- **Art**: every sprite is drawn in code as rows of characters (`src/frontend/src/art`), 16 px
  tiles shown at whole-number scales, light from the top left, two or three tones per material,
  no dithering on characters, stepped pools of light (`lighting.ts`), windows with sky and
  weather, themes that bring their own floors, walls, desks and furniture (`themes.ts`), a local
  fan pack that git ignores. Rules in [art-style.md](../art-style.md) and
  [sprites.md](../sprites.md).
- **Consequence for this study**: any third-party asset either is CC0/CC-BY and redrawn or
  adapted into these rules, or it clashes. Libraries that assume Tailwind or shadcn are useful
  for their behaviour and ideas, not their styles.

## 1. The projects the person sent

### adewaskar/jarvis (J.A.R.V.I.S)

| | |
|---|---|
| What it is | A browser voice assistant with an Iron Man holographic interface. The face is React + Vite + Three.js + custom GLSL; the brain is Claude Code run headless through the Claude Agent SDK in a small Node bridge over a WebSocket |
| What it gives | Wake word ("Hey Jarvis"), push-to-talk on Space, barge-in (speak while it talks and it stops), a local energy-based VAD, a four-beat boot sequence, an arc-reactor core with orbits and particles (`src/scene/Core.tsx`, `Orbits.tsx`, `Particles.tsx`), a HUD the model writes into through a `display` tool (sanitised with DOMPurify and a class allowlist), tools that let the model change the interface (`ui_theme`, `ui_reactor`, `ui_orbit`, `ui_effect`), a read-only tool gate by default, a diagnostics panel |
| Voice | Wake word through the browser's `SpeechRecognition` with a regex on the name (Porcupine is in the dependencies); speech by the browser's voices, Kokoro (`kokoro-js`) or ElevenLabs (cloud, optional) |
| Licence | MIT. Audio in `public/audio/` ships "for the demo" and its rights are not cleared: do not copy it |
| Stack | React 19, @react-three/fiber 9, drei, postprocessing, framer-motion, zustand, three 0.185 |
| Maintenance | ~980 stars, pushed 2026-11-07 |
| Fit | The bridge pattern is close to what Openspace does already; the scene is r3f, which Openspace does not have |
| Verdict | **Take ideas.** Barge-in, the boot sequence as a moment, the model driving the interface through narrow tools (a `ui_*` set for Shiori: mood, focus, whiteboard), the "always say why the voice did not answer" diagnostics. Not the code as a whole, as [brain.md](../brain.md) already decided |

### patch-gg/pixelfx

| | |
|---|---|
| What it gives | 244 pixel-art effects, each a small JS file: fire, explosions, water, ice, lightning, sci-fi (energy core, force field, hologram, glitch), arcane, holy, shadow, status, nature, weather (aurora, fireflies, cherry blossoms, falling leaves), UI (achievement, confetti, coin collect, countdown), pickups, transitions (iris, blinds, CRT off, glitch wipe), critters (bird, butterfly, fish school, bat). Effects take a palette ramp, flip, angle, seed, and draw into a pixel frame in "back" and "front" layers. Also a sprite-sheet exporter (Aseprite JSON) and a Claude Code skill for making new effects |
| Licence | MIT, code and effects. Parts adapted from Hugo Duprez's MIT effects and Hollow Crown's MIT source; the Patch Pixel font in `gallery/fonts` is **not** covered (see its NOTICE.md) |
| Maintenance | ~8.5k stars, pushed 2026-11-13 |
| Fit | Very good. No dependencies; draws into an `ImageData`-like frame that can become a PixiJS texture, or exported sheets loaded as sprites. Palette ramps map onto Openspace's theme palettes |
| Verdict | **Use** (copy the core and the effects we want, keep the MIT notice; not the font). Fireflies and leaves for the village, sci-fi core and force field for the lab dome, confetti for a closed epic, iris/blinds for world changes. The skill is worth reading for the "draft, render, look, refine" method, which matches the draw, test, correct loop already used for Openspace's sprites |

### ddoemonn/interior (interior.dev)

| | |
|---|---|
| What it gives | 54 React micro-interactions as copy-paste files: accordion, command palette, context menu, drawer, dropdown, hold-to-confirm, icon morph, inline validation, live activity, loading button, modal, new-items pill, presence avatars, press depth, progress bar, segmented control, skeleton swap, streaming text, swipe deck, tabs, task steps, text reveal, tree view, typing indicator, value flash, wizard steps… Each exports a headless hook with all the behaviour and a styled example |
| Ideas | "Nothing moves unless something happened", every state's space reserved before it arrives, every gesture knows how it can be abandoned, the keyboard as a complete second implementation, `prefers-reduced-motion` keeps the information and skips the trip |
| Licence | MIT |
| Weight | Only dependency is `motion`; styled examples use Tailwind classes |
| Maintenance | ~1.3k stars, pushed 2026-10-21 |
| Fit | Good: keep the hooks, rewrite the markup's classes in plain CSS |
| Verdict | **Use** (hooks, restyled). First candidates: segmented control (chat modes), streaming text and typing indicator (Shiori answering), hold-to-confirm (organise → approve plan), live activity and new-items pill (activity feed), task steps, tree view (wiki folders), command palette |

### marmottajr/habblaud (competitor)

Described for the person to look at together; no conclusions drawn here.

| | |
|---|---|
| What it is | "The virtual office for your Claude Code (and Codex) agents": each project with an open session becomes a room along a hallway, each agent a pixel-art character. A Node server tails the files Claude Code writes (`sessions/<pid>.json`, transcripts, subagent transcripts) and pushes state over SSE to a canvas client. Zero runtime dependencies; Vite client; PT/EN/ES/FR |
| Building | Lobby with elevators, kitchen, restrooms, lounge, one room per project. Rooms are set up dark, lit when someone arrives; the last one out turns off the light, leaves by elevator and the room becomes a garden; the farthest room moves into a gap so the building shrinks on its own |
| States | Working (bubble with a tool icon), needs you (rushes to the desk, raises a hand, blinking alert, on-screen notice, optional sound and browser notification, answer from the office), waiting on a shell (hourglass; popcorn, then arms crossed and spinning the chair at 3 min, cobwebs at 10, dozing at 25; confetti or a rain cloud at the end), idle (wanders: coffee, water cooler, mirror, couch) |
| Subagents | Arrive by elevator with a name and badge, work in the room, walk to the agent that called them to hand over the result, then leave |
| Social life | When two or more are idle: TV (football, soap, cartoons), video games, arcade, ping-pong, kitchen chat with gossip about coworkers, rock-paper-scissors for made-up coins, the bathroom mirror. 2–3 traits each from a seed (competition, betting, gossip, vanity, naps, shyness…), friends and rivals, a catchphrase; the simulation runs in the browser, wallets in `localStorage`, "with no effect at all on the real agents" |
| Interface | Top counters and per-account usage (5-hour and weekly), side panel with rooms and agents, details drawer (activity, tasks, subagents, timeline, tools, tokens, cost, lines, model, branch), feed, settings, help with full legend and shortcuts, "My day" stats, day timelapse, session history, a live terminal with take-over, phone layout with drawers |
| Look | Pixel art generated by code (characters, furniture, floors, walls); logo, illustrations and wall pictures generated with AI and converted to pixel art by scripts. Day/night follows local time with sun strips on the floor that lengthen in the afternoon, headlights at night, fireflies in the garden. Seasonal themes by the calendar (Halloween in October, Christmas from November), same layout. Sounds synthesised in the browser, off by default |
| Online "World" | An online pixel city where every Habblaud is a building lit live; on by default pointing at the author's server, but nothing leaves until the person creates an account (waitlist). Sends only theme, floors, statuses, look and task bands; a "see what was sent" card shows the exact JSON. Visits, likes, greeting wheel with ready-made phrases, knocks, leaderboards |
| Things to look at together | Strong storytelling of lifecycles (lights, garden, elevator); the escalating comedy of waiting; delivery walks for subagents; very complete keyboard shortcuts and help; transparency on what goes online. On the other side: it reads Claude Code's undocumented files (its README warns an update may break it); personality comes from a seed and coins from dice, not from what happened; emoji in bubbles beside pixel art; themes are seasonal skins rather than worlds; a dense top bar; an online service on by default |
| Licence | MIT (code). Images in `client/public/assets` are AI-generated for the project |
| Maintenance | ~1.4k stars, pushed 2026-11-17 |
| Verdict | **Reference only** for features and pacing; Openspace's art and code stay its own. Its name and look wink at a well-known pixel hotel game, which the person may want to keep in mind when comparing |

### "Thinking orbs" and "dynamic island" components

| Item | What it gives | Licence | Maintenance | Fit | Verdict |
|---|---|---|---|---|---|
| **elevenlabs/ui** Orb | A Three.js orb (r3f + drei, ~500 lines) with custom colours, audio reactivity and agent states (listening, thinking, talking); part of a shadcn registry of voice components (waveform, live waveform, transcriber, message) | MIT | ~2.5k stars, pushed 2026-11-16 | Needs r3f + three (~240 KB gz together); the shader ideas port to a raw WebGL or Pixi mesh | **Take ideas** (or Use in direction B if r3f comes in) |
| **exprmntl/orb-ui** (`orb-ui`) | React voice orb with adapters for several voice platforms | MIT | ~95 stars, pushed 2026-11-03 | Adapters are for cloud platforms we do not use | **Reference only** |
| **Paper Shaders** `dot-orbit`, `neuro-noise`, `smoke-ring`, `metaballs`, `dithering`, `pulsing-border` | Zero-dependency canvas shaders with React wrappers | Apache-2.0 | ~4k stars, pushed 2026-11-20; npm 0.0.82 (pre-1.0) | Small (63 KB gz for all, less per shader), no framework | **Use** for studies of Shiori's face and backgrounds |
| **React Bits** Orb and backgrounds | Large set of animated React components and WebGL backgrounds | **MIT + Commons Clause**: commercial use allowed, but the components themselves may not be redistributed | ~52k stars, pushed 2026-11-19 | Putting a component in Openspace's public repo would redistribute it | **Reference only** |
| **cult-ui** Dynamic Island | Provider + animated container with size presets (compact, large, tall, long), built on framer-motion | MIT | ~7.6k stars, pushed 2026-10-29 | Tailwind; behaviour easy to port | **Take ideas** |
| **SmoothUI** Dynamic Island | Expandable notification pill with a message queue, actions and accessible announcements; shared-layout morph | MIT | ~1.1k stars, pushed 2026-11-19 | Tailwind + Motion | **Take ideas**: a "Shiori island" at the top for "listening… / thinking… / 2 desks need you" |
| react-live-island | Small dependency-free island | MIT | ~54 stars, last push 2024-04 | Old | **Avoid** |

### 3D, liquid glass, shader and GSAP repositories

| Item | What it gives | Licence | Weight | Maintenance | Verdict |
|---|---|---|---|---|---|
| **three.js** | The WebGL/WebGPU 3D library | MIT | ~181 KB gz (full) | ~119k stars, r187 2026-10-30 | Use only if a direction needs 3D (B) |
| **react-three-fiber** + **drei** | React renderer for three and its helpers | MIT | +56 KB gz (fiber) | ~34k / ~10k stars, fiber 9.8 (2026-10) | Use in direction B, lazy-loaded |
| **pmndrs/postprocessing** / `@react-three/postprocessing` | Bloom, chromatic aberration, noise for three | Zlib / MIT | — | ~3k stars, pushed 2026-11 | Use with r3f if B |
| **OGL** | Minimal WebGL library (programs, meshes, post) | Unlicense (public domain) per package.json; GitHub shows no licence file | ~33 KB gz | ~4.8k stars, last release 1.0.11 (2025-01), last push 2025-04 | Take ideas; a fallback for one shader canvas if raw WebGL feels too bare |
| **shadergradient** (`@shadergradient/react`) | Animated 3D gradient backgrounds | npm says MIT; repo has no licence file | 48 KB gz + three | ~3.2k stars | Reference only (licence unclear at the source; three-based) |
| **rdev/liquid-glass-react** | Apple-style liquid glass in React with SVG displacement and chromatic aberration | MIT | small | ~6.8k stars, last push 2025-06 | Take ideas. Its README warns Safari and Firefox show the effect only partially (no displacement) |
| **shuding/liquid-glass** | Copy-paste liquid glass with SVG filters | MIT | tiny | ~1.2k stars, last push 2026-03 | Take ideas |
| **nikdelvin/liquid-glass** | CSS + SVG filters recreation | MIT | tiny | ~120 stars | Reference only |
| lucasromerodb/liquid-glass-effect-macos | Demo | no licence | — | ~830 stars | Reference only (no licence) |
| **GSAP** (+ SplitText, MorphSVG, ScrollTrigger…) | The animation platform; all plugins now free | **Standard "no charge" licence** from Webflow, not open source: free for commercial use, but forbids using it in a tool that lets people build no-code animations competing with Webflow, forbids removing notices, and Webflow may revise the terms | 27 KB gz core | ~31k stars, 3.15.1 (2026-11-04) | **Avoid as a dependency** of an MIT repo: legally usable, but it is the one non-OSI licence in the stack and Motion covers the same needs. Take ideas from its demos |
| **anime.js** v4 | Timeline animation engine (SVG, DOM, JS objects) | MIT | 40 KB gz | ~75k stars, 4.5.0 (2026-06) | Take ideas; a GSAP-like timeline under MIT if Motion's timelines are not enough |
| **Theatre.js** | Visual motion editor | Apache-2.0 (core) | — | ~13k stars, last push 2024-08 | Avoid (stale) |

### dailyflutterui (settings page inspiration)

The GitHub account `DailyFlutterUI` exists with three repositories (a food delivery UI, an
Instagram redesign, a portfolio), none with a licence and none with a settings page; the name
is probably a social-media account of daily Flutter designs. **The specific settings design
the person saw could not be found**; please share the post. General ideas that usually make
those settings screens feel good and that fit Openspace's Settings
(`src/frontend/src/settings`): a profile/identity card at the top (here: the installation and
Shiori's look), grouped cards with one sentence under each title, big toggles that animate
their knob with a spring, a live preview beside a choice (the theme picked shows a tiny
animated room; the voice picked plays a line), a search field that filters sections, and a
sticky "saved" toast rather than a save button. Verdict: **visual ideas only** (Flutter code
cannot be used, and there is no licence).

## 2. Animation for React

| Item | What it gives | Licence | Weight | Maintenance | Fit | Verdict |
|---|---|---|---|---|---|---|
| **Motion** (`motion`, ex framer-motion) | Springs, gestures, `layout` and `layoutId` shared-element animation, `AnimatePresence` exits, scroll, `useReducedMotion`; also a framework-free `animate()` | MIT (the paid "Motion+" extras are separate and not needed) | 63 KB gz full package; tree-shaken and `LazyMotion` builds are much smaller (not measured) | ~35k stars, 14.14.0 (2026-11-13) | Excellent: React 19, no CSS framework assumed; interior.dev builds on it | **Use** |
| **React `<ViewTransition>`** | Animates a tree during transitions, Suspense reveals and deferred values with the browser's View Transitions API; `name` for shared elements; CSS classes for enter/exit | MIT (React) | 0 (already shipped) | Exported by react 19.3.1 (checked in Openspace's `node_modules`) | The whiteboard turning into a note in the wiki, switching worlds, opening a note from the map | **Use**. Firefox support for View Transitions should be checked when prototyping |
| **react-spring** | Spring physics hooks | MIT | 20 KB gz (`@react-spring/web`) | ~29k stars, 10.x (2026-05) | Overlaps Motion | **Avoid** (one spring library is enough) |
| **AutoAnimate** | One-line list/enter/exit animation | MIT | 3 KB gz | ~14k stars, 0.10.2 (2026-11-06) | Board columns, feeds, wiki lists without writing animation code | **Use** where Motion is overkill |
| **GSAP** | See above | Webflow standard licence | 27 KB gz | active | | **Avoid** as a dependency |
| **anime.js** v4 | Timelines, SVG morph and draw | MIT | 40 KB gz | active | | **Take ideas** |
| **Lottie** (`lottie-web`, `@lottiefiles/dotlottie-web`) | Plays After Effects exports | MIT | dotLottie 31 KB gz + WASM | lottie-web last push 2025-09; dotlottie-web active | Smooth vector look, not pixel art; needs animations made in After Effects | **Avoid** for now |
| **Rive** (`@rive-app/canvas`) | Runtime for interactive Rive state machines | MIT (runtime); the Rive editor is a commercial app | 87 KB gz + WASM | active (2026-11-20) | Good for a stateful Shiori face *if* someone designs it in Rive's editor | **Reference only** unless the person wants to design the face in Rive |
| PixiJS ticker + our own tweens | Animation inside the building | MIT | 0 | — | Already how the building animates (2–4 frames at ~5 fps) | Keep |

## 3. Pixel art: assets, tools, light and particles

### Assets

A public MIT repository can only *ship* art whose licence allows redistribution: CC0, CC-BY
(with credits), OGA-BY, CC-BY-SA (derivatives stay CC-BY-SA). Most beautiful itch.io packs allow
use in games but forbid redistributing the files, which a public repository does. Those can be
studied, bought for the person's own fan pack (git-ignored, as today), or used as a model to
redraw from scratch, not committed.

| Pack | What it gives | Themes | Licence | Verdict |
|---|---|---|---|---|
| **Kenney** (kenney.nl) Tiny Town, Tiny Dungeon, Tiny City, Tiny Farm, Tiny Factory, and the roguelike/RPG packs | Clean 16×16 tiles in a consistent style | town, dungeon/castle interior, city, farm, factory | **CC0** for all game assets (attribution optional; do not use the Kenney logo) | **Use** as raw material and references, recoloured to our palettes |
| **0x72 DungeonTileset II** | 16×16 dungeon tiles and characters, autotiles | castle, dungeon | **CC0** art, MIT code; "no generative AI was used" | **Use** for the magic castle |
| **Ninja Adventure** (pixel-boy) | 50+ characters with animations and face sets, monsters, autotiled floors, interiors and exteriors, 30+ effects, UI, fonts, music, sounds | village, desert town, forest, ruins | **CC0** | **Use**: the best single free source for a medieval village and walking characters |
| **LPC** (Liberated Pixel Cup) and the Universal LPC character generator | Huge set of layered 64×64 characters (bodies, hair, clothes, walk cycles) and 32×32 tiles in one style | village, castle, interiors | **Mixed per file**: CC0, CC-BY, OGA-BY, **CC-BY-SA**, **GPL-3.0**; every author must be credited (CREDITS.csv). The generator's code is GPL-3.0 | **Take ideas** for procedural characters; use individual CC0/OGA-BY/CC-BY sprites only with a credits file; avoid CC-BY-SA/GPL pieces in the repo |
| **LimeZu Modern Office / Modern Interiors** | The reference for "RPG office" pixel art, 16×16 (also 32/48) | office, interiors | Paid (from $2.50); commercial use allowed **with credit**, **no redistribution** | **Reference only** in the repo; fine in the person's local fan pack |
| **Pixel Frog Tiny Swords** | Castles, houses, towers, animated water and shores, units | castle, islands/archipelago | Free; commercial use allowed; **no redistribution even modified** | **Reference only** |
| Sci-fi lab / space station packs on itch.io (Leckie Sci-Fi Mega Pack, Cosmic Lilac, coldrice 16×16 Sci-Fi, GameDevploration Space Station) | Station floors, walls, consoles, cryo pods, servers, teleporters | lab, space station | **No CC0 one found**; licences vary and were not confirmed for each; most are paid | **Reference only** until a licence is confirmed; the lab is likely to be drawn by us |
| AI-made images turned into pixel art (Habblaud's approach) | Fast decoration | any | Depends on the tool's terms | Avoid for the world's core art; Openspace asks the person for images rather than improvising them |

**Recommendation.** Keep drawing the world ourselves in code, following
[art-style.md](../art-style.md), and raise the level of detail rather than switching to a
pack: more tones per material, more props per theme, light and particles (below). Use CC0 packs
as reference and as raw material for new themes (village, castle, archipelago) when they are
recoloured to a theme palette. For a sudden jump in quality the person could commission or buy
art for the local fan pack, which never enters the repository.

### Tools and approaches

| Item | What it gives | Licence | Verdict |
|---|---|---|---|
| Today's sprite format (rows of characters in TS) + `tools/pixelgrid.py` + the Sprite Lab view | Code-reviewable art, layered characters | ours | Keep; the base for everything |
| **Layered procedural characters** (as LPC and Openspace's own `character.ts` do) | Body, head, face, hair, outfit, accessory as separate layers, coloured from ramps | — | Extend: walk cycles in four directions for inhabitants who move between places (today only standing/profile views exist) |
| **Aseprite** JSON import | Already accepted by the sprite loader spec | Aseprite is a paid app (source-available) | Keep as an option for the person or contributors |
| **pixelarticons** | 480+ pixel icons in a 24 px grid | MIT, ~900 stars, pushed 2026-10 | **Use** for in-world UI (bubbles, minimap, pixel panels) beside lucide for dense UI |
| **pixelfx** | See above | MIT | **Use** |

### Light and post effects in PixiJS v8

| Item | What it gives | Licence | v8 support | Verdict |
|---|---|---|---|---|
| **pixi-filters** v6.1.5 | Adjustment, advanced bloom, CRT, glow, godray, glitch, old film, outline, pixelate, RGB split, shockwave, simple lightmap, tilt-shift, zoom blur, drop shadow… | MIT | yes (peer `pixi.js >=8`) | **Use**: CRT and glow for screens, godray for sun through windows, shockwave for a merged epic, simple-lightmap as a starting point for night light |
| `@pixi/lights` (normal-mapped deferred lights) | 2D lights with normal maps | MIT | **no** (v7 only) | Avoid |
| `@pixi/layers` | z-order layers independent of the tree | MIT | **no** (v7 only, last release 2023) | Avoid; Openspace sorts by base line already |
| pixi-lights-and-shadows (dobrado76) | 2.5D lights and shadows for Pixi | MIT | not checked | Reference only (13 stars) |
| **Our own light filter** | A v8 `Filter` that takes today's stepped rings (`lighting.ts`) and applies them on the GPU, with coloured lights, flicker and a night tint | ours | — | **Build**: keeps the hard-stepped look the art rules ask for |

### Particles in PixiJS v8

| Item | Licence | v8 | Verdict |
|---|---|---|---|
| PixiJS `ParticleContainer` + `Particle` (built in) | MIT | yes | **Use** for the dome's orbiting particles, snow, sparkles: tens of thousands of cheap sprites |
| **revolt-fx** | MIT | yes (peer `^7.4 || ^8`), 1.3.6 (2026-05), ~66 stars | **Take ideas / Use** if an editor-made emitter is wanted |
| `@pixi/particle-emitter` 5.0.10 | MIT | **no** (peers `<8.0.0`) | Avoid |
| custom-pixi-particles | GPL ("GNU") on npm | — | **Avoid** (GPL) |
| tsParticles | MIT, ~9k stars | DOM/canvas, not Pixi | Avoid inside the world; maybe for a DOM background |

## 4. The map: graph visualisation

The map is the graph of notes and links, filterable by office, kind and author, with themes
chosen like the building's (a galaxy, a city…). Obsidian's own graph view is closed source;
its look (glowing nodes, soft links, forces that settle) is the reference, not code.

| Item | What it gives | Licence | Weight | Maintenance | Fit | Verdict |
|---|---|---|---|---|---|---|
| **d3-force** | The force layout alone (velocity Verlet), no rendering | ISC | tiny (~10 KB) | ~2k stars, pushed 2026-11 | Layout in a worker, draw in PixiJS with the building's tools: galaxy (glow, twinkle, nebula filters), city (offices as districts, notes as houses), archipelago | **Use** (preferred) |
| **force-graph** / **react-force-graph-2d** | Canvas 2D force graph with zoom, drag, `nodeCanvasObject` custom drawing, particles running along links | MIT | ~56 KB gz | ~2.2k / ~3.4k stars, 2026-09 | Fastest way to a working map; custom drawing allows a pixel or galaxy look | **Use** for a quick prototype |
| **3d-force-graph** | 3D force graph on three.js, bloom examples look like galaxies | MIT | ~394 KB gz | ~6.6k stars, 2026-11 | Only with direction B; heavy | **Take ideas** |
| **sigma.js** v4 + **graphology** | WebGL graph renderer for thousands of nodes, custom node/edge programs, graphology's algorithms (communities, ForceAtlas2) | MIT | ~95 KB gz | ~12k stars, 4.0.6 (2026-11-10) | The safe choice if the vault grows past a few thousand notes | **Use** if size demands it |
| **cosmos.gl** (`@cosmos.gl/graph`) | GPU force layout and rendering for very large graphs | MIT | ~134 KB gz | ~1.3k stars, 3.6.0 (2026-11) | More than we need | **Reference only**. Beware: the older npm package `@cosmograph/cosmos` is **CC-BY-NC-4.0** |
| **Cytoscape.js** | Graph theory + rendering, many layouts | MIT | — | ~11k stars | Analysis-oriented look | Avoid |
| **React Flow** (`@xyflow/react`) | Node editors | MIT | ~86 KB gz | ~39k stars | Not a knowledge map; maybe for plan/task flows in Organise | Reference only |
| **Quartz** | Static site from an Obsidian vault with a graph view and backlinks | MIT | — | ~13.6k stars, 2026-11 | Reference for the map's interactions (local graph of the open note, hover highlights) | **Reference only** |

## 5. Components, chat, wiki and whiteboard

### Primitives and kits

| Item | What it gives | Licence | Weight | Maintenance | Fit | Verdict |
|---|---|---|---|---|---|---|
| **Radix Primitives** | Unstyled, accessible dialog, popover, tooltip, tabs, menu, toggle group… one package each | MIT | small per package | ~19.5k stars, 2026-10 | Plain CSS styling via data attributes | **Use** selectively (popover, tooltip, toggle group for the modes) |
| **Ark UI** | Unstyled components on Zag state machines, many more widgets | MIT | ~290 KB gz full (tree-shakes) | ~5.5k stars, 5.40 (2026-10-29) | Fine, but overlaps Radix | Take ideas |
| **shadcn/ui**, Magic UI, Motion Primitives, cult-ui, SmoothUI | Copy-paste component sets | MIT | — | very active | All Tailwind; behaviour and ideas port | **Take ideas** |
| **Aceternity UI** | Animated sections | not open source as a repo (could not find a public licence) | — | — | | Reference only |
| **8bitcn/ui** | Retro pixel components (shadcn registry) | MIT | — | ~2k stars, 2026-11 | Tailwind | Take ideas for pixel-framed panels (direction A) |
| **NES.css** | NES-style CSS framework | MIT | small | ~22k stars, last release 2.3.0 in 2019 | Plain CSS; dated and loud | Take ideas |
| **RPGUI** | Old-school RPG GUI for the web (frames, bars, cursors) | zlib | small | ~960 stars, last push 2023 | Plain CSS + images | Take ideas (frames, progress bars for the character monitor) |
| **98.css / XP.css** | Old OS look | MIT | small | active-ish | | Reference only |
| **cmdk** | Command palette | MIT | 16 KB gz | ~13k stars, 1.1.1 (2025-03) | Unstyled, React 19 | **Use** (or interior's palette) |
| **pixelarticons** | Pixel icons | MIT | small | active | | **Use** |

### Chat

| Item | What it gives | Licence | Verdict |
|---|---|---|---|
| **assistant-ui** | React primitives for AI chat (threads, branches, streaming, attachments, tool UIs) | MIT, ~13k stars, active | **Take ideas**: tool-call cards, branch switching, composer; runtime model would fight our own SignalR session |
| **Vercel AI Elements** | shadcn-based AI components (message, reasoning, sources, task, tool) | Apache-2.0 (licence file), ~2.5k stars | **Take ideas**: "sources" chips map onto Shiori's citations, "reasoning" onto its thinking state |
| **ElevenLabs UI** | Voice components: orb, waveform, live waveform, transcriber | MIT | **Take ideas** for voice |
| **nlux** | Conversational UI | **MPL-2.0 with an AI-training and code-translation restriction** | **Avoid** |
| chatscope chat-ui-kit-react | Classic chat kit | MIT, last push 2025-05 | Avoid (generic) |
| **Streamdown** (Vercel) | react-markdown replacement for streaming | GitHub shows NOASSERTION; **licence not verified** | Reference only until checked |

**The mode switch (Brainstorm / Decide / Organise).** Recommended pattern, from interior's
segmented control and the ideas above:

- A three-part segmented control always visible above the composer, with a sliding thumb (Motion
  `layoutId`), an icon *and* a word for each mode (shape, not only colour: a cloud of dots,
  a gavel/check, a list of cards), and keyboard shortcuts (for example Alt+1/2/3), announced to
  screen readers.
- The whole session takes the mode's colour quietly: the thumb, the composer's border, Shiori's
  face tint and the whiteboard's header. The composer's placeholder says what the mode does
  ("Brainstorm: nothing will be created").
- Switching writes a line into the transcript ("Switched to Decide") so the history shows where
  decisions became possible.
- Shiori never switches by itself; when it thinks a switch would help, it offers a button inside
  its answer ("Ready to decide? Switch to Decide").
- Going *up* in power (to Decide or Organise) is one click; anything that creates things in
  Organise asks for approval with a hold-to-confirm or an explicit "Approve plan" button.

### Wiki

| Item | What it gives | Licence | Verdict |
|---|---|---|---|
| **marked + DOMPurify** (already shipped) | Markdown to HTML, sanitised | MIT (marked's licence file is MIT with notes on bundled code) / Apache-2.0 or MPL-2.0 (DOMPurify) | **Use**, with a small marked extension for `[[wikilinks]]`, embeds and callouts |
| react-markdown + remark-gfm + remark-wiki-link | Markdown as React elements, plugins | MIT (remark-wiki-link by landakram MIT, last push 2023; the flowershow fork has no licence) | Take ideas; not needed while marked works |
| **Shiki** | Syntax highlighting with VS Code themes | MIT | Use for code blocks in notes (lazy-loaded) |
| Quartz, SilverBullet, Foam | Vault-to-site, backlinks, transclusion | MIT / MIT / licence shows NOASSERTION | Reference only |
| Tiptap, Milkdown, MDXEditor | Rich Markdown editors | MIT | Not needed yet (the vault is edited by agents and in Obsidian); revisit if the person wants to edit notes in Openspace |

### Whiteboard that writes itself and becomes a note

| Item | What it gives | Licence | Weight | Verdict |
|---|---|---|---|---|
| **roughjs** | Sketchy lines, boxes, arrows on canvas or SVG | MIT | 9 KB gz | **Use** (last release 2023, stable) |
| **rough-notation** | Animated hand-drawn underlines, circles, highlights, brackets | MIT | 4 KB gz | **Use** to mark the decision of the session |
| SVG stroke-dashoffset handwriting | Text that appears stroke by stroke (from a single-line "hershey"-style font or paths) | — | 0 | **Build**; a pixel variant draws with a marker sprite in direction A |
| Motion `layout` + `<ViewTransition name>` | The board folding into a note card that flies into the wiki | MIT | 0 extra | **Use** |
| **Excalidraw** | Full whiteboard | MIT | ~344 KB gz | Avoid (too much for a board only Shiori writes) |
| **tldraw** | Infinite canvas SDK | Own licence: **production use needs a licence key** | — | **Avoid** |

### Character monitor (stats page)

No library needed: sparklines and bars in our own SVG with the existing tokens, history as a
list with the event that caused each change. RPGUI's frames and bars are an idea for a game-like
"character sheet" look in direction A. Each inhabitant's portrait is the in-world sprite at a
large whole-number scale.

## 6. Shaders and 3D: the dome and Shiori's face

[brain.md](../brain.md) separates the two: the dome is the brain's *place* in the science world;
the face is what the person talks to.

**The dome (science lab world).** Best drawn in PixiJS, inside the world: a pixel-art glass
dome on a pedestal; inside, a pixel brain (or a cluster of notes) lit from within; particles in
orbit with `ParticleContainer` on elliptical paths in front of and behind the brain (two layers
sorted by depth), pixelfx's energy core and force field for pulses when an agent consults it,
pixi-filters glow/bloom limited to the dome. Agents walk up to it to "read before acting".
No 3D library needed.

**Shiori's face: options**

| Option | How | States it can show | Cost | Verdict |
|---|---|---|---|---|
| Stylised eyes | Pixel-art eyes (direction A) or smooth vector eyes in SVG animated with Motion | attention (eyes follow the cursor/speaker), thinking (eyes look up, narrow, a slow blink), emotion (shape of lids), listening (eyes widen) | tiny | Strong, readable, warm |
| Sphere of light with things orbiting | Fragment shader on a quad (noise-driven glow, Fresnel rim) + orbiting dots for notes it is reading; Paper Shaders' `dot-orbit`, `smoke-ring`, `neuro-noise` as studies; ElevenLabs Orb as a three.js reference | attention (brightens, turns toward the speaker), thinking (orbits speed up, notes it cites come into orbit), speaking (pulses with the audio level), emotion (hue and turbulence) | small in raw WebGL/OGL; +240 KB gz with r3f | Most "AI", fits direction B |
| Neural web | Nodes and links in canvas/SVG or a shader; links light up along paths | thinking (signals run), focus (a cluster tightens), emotion (colour, rhythm) | small | Fits direction C and ties visually to the map |
| Rive state machine | Designed in the Rive editor, played by the MIT runtime | any | 87 KB gz + WASM, plus design work in a commercial editor | Only if the person wants to design it there |

A good face can combine two: a sphere whose surface shows two eyes when it looks at you.
Whatever the choice, it is driven by a small state machine (idle, listening, thinking,
speaking, waiting for you, pleased, worried) that Shiori's session sets through narrow tools,
as J.A.R.V.I.S does with `ui_reactor`. With reduced motion it keeps the colours and shapes and
drops the movement.

## 7. Voice

### Listening (speech to text)

| Item | What it gives | Licence | Maintenance | Verdict |
|---|---|---|---|---|
| **whisper.cpp** (server) | Whisper in C/C++, CPU/GPU, HTTP server mode | MIT; models (`ggerganov/whisper.cpp` on HF, OpenAI Whisper incl. large-v3-turbo) MIT | ~55k stars, pushed 2026-11-20 | **Use** |
| **faster-whisper** | Whisper on CTranslate2 (Python) | MIT | ~27k stars, 2026-11 | Use if the backend side prefers Python |
| **sherpa-onnx** | STT, TTS, VAD, keyword spotting, diarisation on ONNX, many bindings incl. WASM | Apache-2.0 | ~16k stars, 2026-11 | Take ideas / alternative runtime (its keyword-spotting models were not checked for Italian/Japanese) |
| **Silero VAD** + `@ricky0123/vad-web` | Voice activity detection in the browser (end of speech, barge-in) | MIT / ISC | 2026-09 | **Use** |
| Browser `SpeechRecognition` | Cloud speech in Chrome | — | — | **Avoid** (sends audio to a cloud service, against "local") |

### Speaking (text to speech)

| Item | Italian | Japanese | Licence | Size / needs | Verdict |
|---|---|---|---|---|---|
| **Kokoro-82M** (`hexgrad/kokoro`, `kokoro-js`) | 2 voices (if_sara, im_nicola), both graded **C** by the authors, 1–10 h of training data | 5 voices, best **C+** (jf_alpha); four of them CC-BY with attributions listed | Apache-2.0 (code and weights) | 82M; runs on CPU and in the browser (`kokoro-js`, 885 KB gz + model download) | **Candidate**: listen first. English is excellent (A for af_heart) |
| **Piper** | it_IT: **paola** (medium, dataset **CC0**), **serena** (medium/high, dataset **CC-BY-4.0**), **riccardo** (x_low, M-AILABS dataset, licence "see URL") | one voice, **hi_fi_captain**, dataset **CC-BY-NC-SA-4.0** (not usable) | Original `rhasspy/piper` is MIT but **archived**; development moved to `OHF-Voice/piper1-gpl`, **GPL-3.0** (it links espeak-ng). Voices repo on HF is MIT; each voice has its own dataset licence | small, fast, CPU | **Candidate for Italian** (serena-high), run as a separate program the person installs, not bundled; never for Japanese |
| **Qwen3-TTS** (0.6B, 1.7B) | yes | yes | Apache-2.0 (code and weights on HF) | 0.6–1.7B, GPU advisable | **Candidate** for quality; listening test needed |
| **CosyVoice 3** (Fun-CosyVoice3-0.5B) | yes | yes | Apache-2.0 | 0.5B, GPU advisable | **Candidate** |
| **Pocket TTS** (Kyutai) | yes | no | code MIT, weights **CC-BY-4.0** (credit needed) | ~100M, CPU | Candidate for Italian and English only |
| **Chatterbox Multilingual** (Resemble AI) | yes | yes | MIT (weights MIT on HF) | 500M; every output carries an imperceptible **Perth watermark**; the authors say it is **retired and unmaintained since August 2026** | Reference only |
| **MeloTTS** | **no** (EN, ES, FR, ZH, JA, KO) | yes | MIT | small | Avoid for Italian |
| **XTTS-v2** (Coqui) | yes | yes | **Coqui Public Model License**: non-commercial | — | **Avoid** |
| **F5-TTS** | — | — | code MIT, weights **CC-BY-NC-4.0** | — | **Avoid** |

The listening test (part of S2.1 in the stories) was **not run** in this study: it needs audio
playback with the person. Proposed protocol: the same ten sentences per language (a greeting, a
question, a number, a date, an English technical word inside an Italian sentence, a long
sentence, an apology, a joke, a list, a name), generated by each candidate on the person's
machine, played blind in a random order, scored 1–5 for naturalness and for pronunciation.

### Wake word ("Shiori")

| Item | What it gives | Licence | Verdict |
|---|---|---|---|
| **LiveKit WakeWord** | Train a model for any phrase (`--language ja` and others), runs locally in Python, Swift, **browser** and Node through ONNX (~7 MB) | Apache-2.0, ~320 stars, 2026-11 | **Candidate**. Training needs a GPU and a LiveKit Cloud project (cloud TTS for the synthetic clips); detection needs neither. The licence of its pretrained encoder and of the 4 GB of noise/speech it downloads was **not checked** |
| **openWakeWord** | Custom models from synthetic speech (Colab notebook) | Code Apache-2.0; **pre-trained models CC-BY-NC-SA-4.0**; training "only supports English" for now; no browser port (stream audio to Python) | **Candidate** for a model we train ourselves, if the training data's licences allow it |
| microWakeWord | TensorFlow training framework, synthetic samples | Apache-2.0, ~40 stars | Reference only |
| **Porcupine** (Picovoice) | Italian and Japanese, WASM SDK | SDK Apache-2.0, but every engine needs an **AccessKey** from Picovoice Console checked at start-up. Picovoice's FAQ says there are no plans for personal or non-commercial use (enterprise trial only); a community report says free-tier keys were disabled on 2026-06-30 | **Avoid** |
| Whisper on VAD segments + name match | No model to train: VAD wakes a short whisper pass, a regex catches "Shiori" (what J.A.R.V.I.S does with the browser recogniser) | — | **Fallback**: simple, local, slower and hungrier |

## 8. Ready-made projects on GitHub

A second pass, after the person chose their references (Waneella, Kirokaze and Mark Ferrari for
pixel art; *Her*, EVE, the hologram of *Blade Runner 2049*, Iron Man's J.A.R.V.I.S and *Akira* for
Shiori and the atmosphere, all to become themes). It searched GitHub by stars for projects that
could be used as they are or with small changes: agents in pixel worlds, pixel effects and
generators, sci-fi interfaces, voice orbs, shaders. Checked on 2026-11-21 through the GitHub API.

**What the search says first.** The high-density pixel art the person likes (Waneella,
Kirokaze, Ferrari) is hand-made and is not on GitHub under an open licence. Nothing can be
downloaded that looks like it. What GitHub does give is the *machinery* that makes such scenes
feel alive: colour cycling, lights, effects, procedural planets and props, sci-fi frames, orbs.
The density itself comes from drawing at a smaller scale with more pixels (see below).

### Agents in pixel worlds (closest to Openspace)

| Project | What it is | Licence | Stars, last push | Verdict |
|---|---|---|---|---|
| **pixel-agents-hq/pixel-agents** | AI agents (Claude Code and others) as characters in a pixel office: layout editor, wardrobe of bodies, hair, clothes, a pixel editor, external asset packs, phone view | MIT code; characters based on JIK-A-4 *Metro City* (an itch.io pack with its own terms) | ~10.3k, 2026-11 | **Competitor; take ideas**: the asset-pack format and the wardrobe. Its art is the low-density look the person does not want |
| **androoAGI/starnet** | A living pixel-art space station where local agents do real work | MIT code only; the station artwork and sprites are **not** licensed | ~1.9k, 2026-11 | **Competitor; reference only** for the art |
| **a16z-infra/ai-town** | A town where LLM characters live, talk and remember (Convex, PixiJS) | MIT; art from OpenGameArt packs and itch.io UI, each with its own terms | ~10.8k, 2026-11 | **Take ideas** for conversations between inhabitants and memory (phase 5) |
| **joonspk-research/generative_agents** | The Stanford "Smallville" paper's code: memory stream, reflection, planning | Apache-2.0 | ~22k, 2024 | **Take ideas** for relationships from real events and daily plans (phases 4–5) |
| srizzon/git-city | A GitHub profile as a 3D pixel building in a city | AGPL-3.0 | ~6k | Reference only (AGPL) |
| amilich/isometric-city, victorqribeiro/isocity | Isometric city builders in the browser | MIT | ~2.4k, ~3.3k | Take ideas if a theme goes isometric |

### Making pixel scenes feel alive

| Project | What it gives | Licence | Stars, last push | Verdict |
|---|---|---|---|---|
| **jhuckaby/canvascycle** | Mark Ferrari's colour cycling in canvas: water, rain, fire and neon animated by rotating palette entries, with no extra frames | MIT code (the Ferrari scenes in its demo are his and are **not** covered) | ~430, 2024 | **Use the technique**: a PixiJS filter that cycles palette ranges gives water, signs, screens and rain for free on dense art |
| **Deep-Fold/PixelPlanets** | Procedural pixel planets, stars, asteroids, black holes as shaders | MIT | ~1.4k, 2024 | **Use**: the galaxy theme of the brain map, and the sky of a night world |
| **mxgmn/MarkovJunior** | Rule-based procedural generation (rooms, cities, mazes) | MIT | ~8.5k, 2026-03 | Take ideas for generating props and districts |
| mxgmn/WaveFunctionCollapse | Tilemaps generated from one example | custom (MIT-like, not checked) | ~25k | Take ideas |
| **Ttanasart-pt/Pixel-Composer** | Node-based VFX editor for pixel art (a desktop app) | MIT | ~1.5k, 2026-11 | **Tool**: make effect sprite sheets (smoke, holograms, sparks) to import |
| **patch-gg/pixelfx** | 244 pixel effects as small pieces of code | MIT | ~8.6k, 2026-11 | Use (already chosen above) |
| **Orama-Interactive/Pixelorama** | A full pixel art editor | MIT | ~11k, 2026-11 | **Tool** for drawing at higher density, beside Aseprite |
| Siilwyn/awesome-pixel-art | A curated list of pixel art tools, tutorials and resources | CC0 | ~1.3k | Reading list |

### Sci-fi interfaces (J.A.R.V.I.S, *Blade Runner*, *Akira*)

| Project | What it gives | Licence | Stars, last push | Verdict |
|---|---|---|---|---|
| **arwes/arwes** | A futuristic sci-fi UI framework for the web: animated frames, text that types itself, sounds, backgrounds | MIT | ~7.6k, 2026-07 | **Use or take ideas** for a sci-fi / J.A.R.V.I.S theme of the interface |
| **propjockey/augmented-ui** | Cyberpunk cut corners and borders in pure CSS | BSD-2-Clause | ~1.4k, 2024 | **Use** for an *Akira* / neon theme |
| GitSquared/edex-ui | The famous sci-fi terminal with monitors | GPL-3.0, archived | ~45k, 2021 | Reference only |
| Prompt-Surfer/obsidian-jarvis-ui | A 3D vault graph for Obsidian in Iron Man style | AGPL-3.0 | ~70 | Reference only: proof that "brain map as J.A.R.V.I.S" works |

### Shiori's face: orbs and shaders

| Project | What it gives | Licence | Stars, last push | Verdict |
|---|---|---|---|---|
| **elevenlabs/ui** (Orb component) | A voice-agent orb with listening, thinking and speaking states, on shadcn | MIT | ~2.5k, 2026-11 | **Take ideas / use** the orb as a starting point for Shiori's states |
| **paper-design/shaders** | Zero-dependency canvas shaders (neuro noise, orbits, mesh gradients, dithering) | Apache-2.0 | ~4k, 2026-11 | **Use**: fills the orb, the hologram and backgrounds |
| **oframe/ogl** | Minimal WebGL (~30 KB) | Unlicense | ~4.8k, 2025 | **Use** for a hand-made orb or hologram shader, far lighter than three.js |
| weeeha/AI-Orb, mbenhard/lalia, imuvrays1/voice-orb | Audio-reactive orbs for voice agents in one shader | MIT | under 50 | Take ideas (small projects) |
| niccolofanton/dithering-shader | Bayer dithering as a post effect | MIT | ~300 | Take ideas: a hologram that breaks into pixels, as in *Blade Runner 2049* |
| andydbc/HologramShader | A hologram material (Unity) | MIT | ~1k | Take ideas: scanlines, rim glow, flicker |
| Inochi2D/inochi2d | Rigged 2D puppets (an open Live2D) | BSD-2-Clause | ~1.9k | Reference only: only if Shiori ever gets a body |
| kopiro/siriwave | Siri's waveform in JS | MIT | ~1.7k | Take ideas for the "speaking" line under the orb |

### Animated React components

| Project | Licence | Verdict |
|---|---|---|
| **magicuidesign/magicui** (~23k stars): animated text, beams, particles, borders to copy and paste | MIT | **Take ideas / copy** single components |
| DavidHDev/react-bits (~52k stars): the largest set of animated components | **MIT + Commons Clause**: code cannot be sold or resold as is | **Reference only** in a public MIT repository |
| codrops (many demos: rain, transitions, distortions) | per demo, mostly unstated | Take ideas |
| patriciogonzalezvivo/lygia (shader library) | **Prosperity licence**: free only for non-commercial use | **Avoid** |

### How to get the density the person wants

1. **Smaller pixels, more of them.** Today a character is about 16 px tall and drawn at 3–4×.
   Waneella's and Kirokaze's scenes use about 2× on the screen, with characters 24–48 px tall and
   rooms full of small props. The new building should be drawn at native size 2× (3× on phones
   only when needed), with sprites two to three times taller than today.
2. **Light is half the detail.** Coloured lights, glow on screens and signs, colour cycling for
   water, rain and neon, a night tint: pixi-filters plus our own colour-cycling filter.
3. **Layers and depth.** Parallax backgrounds (city, sky, PixelPlanets), foreground silhouettes,
   rain and steam as particles.
4. **Art made, not bought.** Drawn by us in the sprite format with Pixelorama or Aseprite as
   help, effects made with Pixel-Composer, and CC0 packs as raw material. Paid packs at 32/48 px
   (LimeZu and others) only in the person's local fan pack.

## 9. The second prototype: Shiori at the centre

After the first prototype the person chose (2026-11-21):

- **One visual language: dense pixel art.** Worlds and themes change places, palettes, light and
  objects, not the drawing technique. The density is the point: small pixels, many of them, as in
  Waneella's and Kirokaze's scenes, instead of today's large ones.
- **Shiori is the protagonist**, at the centre and large; the chat becomes subtitles under her
  (the last question and her answer as she writes it), with the whole conversation and the board
  in drawers. A long answer turns into a card and Shiori steps back to make room.
- **One being, several bodies.** The person's references (animated studies of light creatures,
  point clouds, scanned heads, ASCII dissolves, HUD callouts) share a mood: an AI in a dystopian
  future, the two of you working together while the world burns, close to *Akira* and
  *Evangelion*. No realism and no real faces.
- Shiori may step a little outside the world's style, but must not clash with it.

`?lab=presence` is that prototype:

| Part | How it is made |
|---|---|
| Bodies | **Swarm** (a membrane of 2,600 points deformed by noise, with worms of light crawling on it), **Medusa** (a pulsing bell and 30 tentacles with signals running down them, small eyes), **Scanline** (a bust drawn as horizontal dashes: each line a cross-section with a front and a back profile, so skull, brow, nose, lips, chin, ears, neck and sloping shoulders read; it turns to follow its gaze and dissolves at the base) |
| Character | One set of numbers per state (energy, speed, eyes, opening, pleased, unsure, lean, heat) shared by every body; blinks, double blinks and saccades; a voice envelope while she answers |
| Eyes | Points that leave the body and gather into two eyes when she looks at you, dissolve back while she thinks, turn into arcs when she is pleased, tilt and shrink when she is unsure |
| HUD | Tags hung on parts of her body on springs, with live coordinates; the notes she reads or cites replace the part names |
| Changes | A new body dissolves into 5×7 pixel glyphs with a glitch and comes back; a mode switch half-dissolves and takes the mode's colour |
| Drawing | One WebGL2 canvas, no new dependency: points into an offscreen target, then glow, ASCII, glitch and, in pixel mode, the world's 8-colour palette with ordered dithering, enlarged without smoothing. A **Smooth / Pixel ×2 ×3 ×4** switch shows both ways |
| World | The first world, a city with fires on the horizon, smoke, embers, lit and flickering windows, a beacon, a stuttering neon sign, drawn on a canvas at the art's own size at 12 frames a second. **Four hours** (dawn, day, dusk, night; *Now* follows the clock) change the sky, the blocks and how many windows are lit; **the seven skies of Openspace's weather** (clear, cloudy, fog, drizzle, rain, snow, storm with lightning) change what falls and what drifts, so the world can follow the real weather |

Calm motion slows everything, drops the glitch and keeps the colours. The 3D noise in the shader
is Ashima Arts' (MIT).

## Licences and risks

**Safe to ship in the MIT repository**: MIT, ISC, BSD, Apache-2.0, zlib, Unlicense, CC0 code and
art; CC-BY art with a credits file (a `docs/credits.md` or `CREDITS` next to the assets, plus a
line in the About card).

**Usable with care**
- *CC-BY-SA art* (parts of LPC): derivatives must stay CC-BY-SA. Keep such files separate with
  their own licence note, or avoid them.
- *OGA-BY*: like CC-BY without the DRM clause; credits needed.
- *pixelfx*: MIT except the Patch Pixel font; keep its NOTICE.
- *GSAP*: free, but a vendor licence that can change; avoid as a dependency.
- *Piper*: the maintained engine is GPL-3.0. Run it as a separate program the person installs
  (as Openspace treats other local engines), never bundle or link it; pick voices by their
  dataset licence (CC0/CC-BY only).
- *Pocket TTS* weights CC-BY-4.0, Kokoro's Japanese voices CC-BY: credit them in the About card.

**Unusable (reference only)**: GPL code inside the frontend (custom-pixi-particles, the LPC
generator), non-commercial licences (openWakeWord's bundled models, Piper's Japanese voice,
XTTS-v2, F5-TTS weights, the old `@cosmograph/cosmos` npm package), Commons Clause (React Bits),
tldraw's licence, nlux's restricted MPL, packs that forbid redistribution (LimeZu, Pixel Frog,
most itch.io packs), anything without a licence (several liquid-glass demos, the DailyFlutterUI
repos, shadergradient's repository).

**Other risks**
- *Weight*: three.js + r3f + drei add ~240 KB gz; lazy-load them only on Shiori's views if
  direction B is chosen.
- *Browser support*: SVG displacement inside `backdrop-filter` (liquid glass) works fully only in
  Chromium per its authors; View Transitions in Firefox to be checked.
- *Contrast*: glow, glass and dithered backgrounds must still give WCAG AA text; the tokens'
  rule (mute for text, line for decoration) stays.
- *Reduced motion*: every animation needs a still state that carries the same information.
- *Performance*: the building already runs a PixiJS app; a second WebGL context for the face is
  fine, a third for the map should reuse Pixi.
- *Voice privacy*: everything local; the browser's `SpeechRecognition` and cloud TTS are out.
- *Wake word false wakes*: "Shiori" is short; a confirmation sound and a visible listening state
  are needed, and push-to-talk stays.
- *Trademarks*: worlds and homages keep to the existing rule (recognisable, never named).

## Proposed prototypes

For S2.2, in this order, each as a separate route in a throwaway branch with made-up data, so
the person can compare directions on the same screens.

1. **The building with the brain's place.** Today's office with a new common room where the
   brain lives (a library-like nook in the office world) and the same building redone as the
   science lab with the dome: pixel dome, `ParticleContainer` orbits in two depth layers,
   pixelfx energy pulse when an agent consults it, our own v8 light filter (night, lamps,
   screens). Measures: frame time with 20 inhabitants, look at 3× and 4×.
2. **Shiori's chat and face.** One page: face on the left (three variants side by side:
   eyes, orb, neural web, all driven by the same state machine and a debug slider), chat in the
   middle with the three-mode segmented control, citations as chips, streaming text, and the
   whiteboard on the right filling in with roughjs strokes and rough-notation highlights, then
   folding into a note with `<ViewTransition>`. Voice stubbed with push-to-talk and a fake
   level meter.
3. **The map.** The same 300 made-up notes laid out with d3-force and drawn in PixiJS in two
   themes (galaxy, archipelago), plus force-graph as a baseline for speed and feel. Hover shows
   the local graph; click opens the note with a shared-element transition.
4. **Voice bench** (outside the UI): the listening test above for Kokoro, Piper (serena),
   Qwen3-TTS, CosyVoice 3 and Pocket TTS; a LiveKit WakeWord model for "Shiori" if the person
   agrees to the cloud step for training.

## Open questions for the person

1. Which direction (A, B, C) or which mix speaks to you after seeing the prototypes?
2. Shiori's face: eyes, orb with orbits, neural web, or a combination?
3. The third world to start with (medieval village, town with shops, magic castle, space
   station, archipelago)? The CC0 packs make the village and castle cheapest to start.
4. Is it acceptable to keep drawing all art ourselves (slower, consistent, shippable), or would
   you buy packs like LimeZu for your local fan pack only?
5. Is three.js (about 240 KB more, only on Shiori's pages) acceptable for a 3D face or map?
6. For the wake word, is a one-time cloud step to *train* the "Shiori" model acceptable
   (detection stays local), or must training be fully local too?
7. Which machine will run the voice: is a GPU available for the larger TTS models, or must it
   run on CPU (Kokoro, Piper, Pocket TTS)?
8. Can you share the dailyflutterui settings design you saw? It could not be found.
9. Do you want to look at Habblaud together before the prototypes, so its good ideas inform them?
