# Siege Sandbox

A greybox Unity 6 (URP) test range for Rainbow Six Siege-style first-person mechanics.
This is the mechanics-only first pass of a planned roguelike FPS: no menu, tutorial or
enemies yet, just the feel of moving and shooting.

Open `Assets/SiegeSandbox/Scenes/Sandbox.unity` and press Play.

## Controls

| Input | Action |
| --- | --- |
| WASD | Move |
| Shift | Sprint (stands you up, lowers the gun; aiming or firing cancels it) |
| Space | Jump (from prone it stands you up). Jump into cover while holding W to climb over it |
| V | Melee strike |
| C | Crouch (toggle) |
| Ctrl | Prone (toggle) |
| Q / E | Lean left / right (tap to lean, tap again to straighten; `toggleLean` off = hold) |
| F (hold) | Put up a barricade at a doorway or window, or pry one off |
| Right mouse | Aim down sights (hold) |
| Left mouse | Fire |
| R | Reload (animated; longer when the magazine is empty) |
| H | Toggle the hitbox camera |
| Tab | Toggle the controls overlay |
| Esc | Settings menu (also frees the cursor) |

## Settings

Escape opens the settings menu (and frees the mouse); Escape again or Resume goes back to the
game. Changes apply at once and are remembered between sessions.

- **Mouse acceleration** is off by default. On macOS the game reads the mouse's own movement
  through a small native plugin (`Assets/SiegeSandbox/Plugins/macOS`, source and build script in
  `Tools/RawMouse`), so a given distance on the desk is always the same turn. A trackpad, or a
  mouse macOS does not offer for raw input, falls back to the normal pointer; the menu says
  which is in use. On Windows Unity's mouse input is already raw.
- **Sensitivity**, **aiming sensitivity** (as a percentage of normal) and **inverted look**.
- **Lean**, **aim** and **crouch** can each be toggle or hold.
- **Field of view** (vertical, 60 to 100; the aimed view keeps its zoom relative to it).
- **Aspect ratio**: Auto, 4:3, 5:4, 3:2, 16:10, 16:9, 21:9, either stretched to fill the window
  (as in Siege) or with black bars.
- **Frame rate limit** and **volume**.

The values and their defaults are in `GameSettings`; the menu is `SettingsMenu`.

## What is modelled

- **Stances and lean change the hitbox.** Legs, torso and head are separate trigger
  colliders. Crouch and prone resize them (prone lays the body out flat behind the head);
  leaning swings the torso and head sideways while the legs stay planted. The corner
  camera shows the hitbox live.
- **ADS matters.** Hip fire has a spread cone that grows with sustained fire and movement
  (the crosshair gap is the real cone). Aiming down sights removes almost all spread,
  halves recoil, zooms the camera and slows you down.
- **Recoil is a fixed pattern.** `Weapon.recoilPattern` is a list of per-shot kicks, so
  every spray climbs and drifts the same way and can be learned. It resets after a short
  pause in firing. Crouch and prone reduce it further.
- **Bullets are projectiles.** They fly at `Weapon.muzzleVelocity` (180 m/s by default),
  drop with gravity, and only deal damage on arrival, so strafing targets need leading.

- **Jumping costs accuracy.** Space jumps about a metre, with reduced air control and a
  much wider spread cone until you land.
- **Melee is physical.** V swings the weapon at whatever is within about 1.9 m: it drops a
  dummy (which falls the way it was hit), knocks a peek hole in a soft wall, and knocks boards
  out of a barricade. It interrupts a reload.
- **Damage depends on where you hit.** One bullet to the head is always lethal, even after
  passing through a wall. The torso takes full damage (34) and arms and legs take three
  quarters (`DamageZone.multiplier`). The HUD names the part you hit.
- **Damage falls off with range.** Full damage out to 18 m, dropping in a straight line to
  55% at 38 m and beyond (`Weapon.falloffStart`, `falloffEnd`, `falloffMinScale`). Headshots
  still kill at any range. Bullets also drop with gravity.
- **Bullets penetrate soft cover.** Anything with a `Surface` component set to Wood or
  SoftWall lets bullets through at reduced damage (up to four layers); everything else stops
  them.
- **Holes are real holes.** Soft walls and barricades are `HolePanel` sheets: holes are
  painted into the sheet's own texture and cut out by an alpha-clipped material. You can see
  through them, a later bullet through an existing hole loses no damage, and sound comes
  through them.
- **Soft walls, as in Siege.** A bullet leaves a little hole (about 4 cm). Five bullets
  within 12 cm of each other break the piece between them out. A melee strike knocks a
  peek hole about 22 cm across. Neither bullets nor melee remove the wooden frame inside, so
  you can open sight lines but not walk through.
- **Barricades, as in Siege.** Horizontal boards held by two yellow fabric straps with a
  yellow cross sprayed on. Intact, one blocks sight completely. Each board is its own piece:
  a bullet makes a small hole, three bullets in one board break it off (the straps stay),
  and about twenty bullets destroy the whole thing. The first two melee strikes knock out the
  boards around the hit and the third always destroys it. Missing boards leave real gaps: a
  door barricade stops short of the floor, and with its bottom three boards gone you can
  crawl under; a window with its lower four boards gone can be climbed through. Going
  through destroys what is left.
- **Barricading.** Hold F at an open doorway or window: the barricade is hung from the top,
  unrolls down the frame and is nailed in (2 s). Let go early and it comes away. Hold F at a
  standing barricade to lever it off with a crowbar (1.5 s); it comes off in one piece at
  the end. The window starts boarded up; the doorway starts open.
- **Climbing over cover.** Space always jumps. If the jump carries you into cover up to
  1.3 m high while you hold forward, you climb over it, or through a window once its
  barricade is gone. You stay crouched over open cover and tuck low through a window.
- **The lean you see is the lean you get.** The view, the gun and the hitbox all hang off the
  same pivot: 9 degrees of tilt and a 14 cm shoulder slide.
- **Leaning changes the shot.** Recoil follows the tilted gun, so a spray climbs toward the
  side you lean to. Hip fire is slightly worse while leaned and clearly worse while the lean
  is still moving. Sprinting, jumping and vaulting straighten you up.
- **Crawl spaces.** The low tunnel has 0.8 m of headroom: only a prone player fits, and you
  cannot stand up inside it.
- **Reloads are staged.** The support hand does the work: it reaches for the magazine,
  pulls it, stows it, brings a fresh one up, seats it and slaps it home, and the gun kicks
  as it seats. The rounds only count once it seats. Reloading from empty drops the spent
  magazine on the floor, racks the bolt and takes longer (`Weapon.reloadTime` vs
  `Weapon.emptyReloadTime`). You cannot fire or aim mid-reload.
- **Sprint has weight.** Head and weapon bob scale with speed, the view widens slightly, and
  the gun has to come back up before it can fire.

The range has one opening per mechanic: a boarded-up window, a doorway to lean around (and
barricade), a soft wall, and a prone-only crawl tunnel, plus static and strafing targets, a
platform and a ramp.

## Sound

### How sound travels

World sounds are routed the way Siege's audio team describes routing theirs (`Sfx.Evaluate`).

- **Propagation nodes.** The level is covered in nodes (`SoundPortal`): doorways, windows,
  the ends and tops of walls, the crawl tunnel, and every hole knocked in a soft wall. Nodes
  that can hear each other are linked into a graph.
- **Obstruction.** If the straight line to you is blocked, the sound takes the cheapest route
  through the graph, across as many nodes as it needs. A route costs its length, the angles
  it turns through and whatever it squeezes past. You hear it from the direction of the last
  node, as far away as the whole route is long, with treble taken off for the bending.
- **Occlusion.** What leaks straight through the wall is played as well, from where the sound
  really is, filtered by the materials in the way: glass and wood take a little, soft wall
  more, metal a lot, concrete nearly everything. The two versions play together, which is
  what lets you place a sound behind a wall.
- **Destruction changes the routes at once.** A hole or a missing board opens a way; each
  board on a barricade takes level and treble off what comes through the opening.
- **Reflection.** Loud sounds get a reverb tail chosen by probing the space around the source
  (tight, room, open air), played from the source's position, plus an echo off the nearest
  far wall outdoors, delayed by the distance.
- **No ambience bed.** What you hear is what somebody did. Movement is loud on purpose:
  sprinting and landing rattle your kit, and the strafing dummies have footsteps you can
  track through walls.

With the help panel showing (Tab), the top right of the screen says how the last world sound
reached you, for example `round 2 openings, 21 m -6 dB 9.8 kHz + leak through wall -30 dB`.

### What the sounds are

Out of the box every sound is a **synthesized stand-in** (`Synth`: layered noise bursts and
sine sweeps). They are placeholders, and they sound like it. To use real recordings, put
audio files in `Assets/SiegeSandbox/Resources/Sfx/<SoundName>/` (see the README in that
folder for the names). Each file is used as a variation, and all of the routing, muffling
and reverb above is applied to it unchanged.

## Layout

- `Assets/SiegeSandbox/Runtime` - gameplay scripts (`PlayerController`, `Weapon`,
  `Projectile`, `Target`, `DamageZone`, `Surface`, `HolePanel`, `Barricade`, `SoftWall`,
  `Interactor`, `Hud`, `HitboxDebugCamera`, `GameSettings`, `SettingsMenu`, `MouseCapture`,
  `RawMouseInput`), with the audio in `Runtime/Audio` (`Synth`, `Sfx`,
  `SoundPortal`).
- `Assets/SiegeSandbox/Editor` - `SandboxBuilder` generates the scene, materials and meshes
  from code (**Siege Sandbox > Build Sandbox Scene**). Edit the builder rather than the
  scene if you want changes to survive a rebuild.
  `PlayModeReloadGuard` holds script reloads until you stop playing: a reload in the middle
  of a session wipes the running game's state (barricades stop appearing, for one).
- `Assets/SiegeSandbox/Tests` - play mode tests for the mechanics
  (**Siege Sandbox > Run Mechanics Tests**, results in `Logs/mechanics-tests.txt`).

Tuning values (speeds, heights, lean angle, spread, recoil pattern, fire rate) are public
fields on the Player and Weapon objects in the scene. Because the scene is generated,
change the defaults in the scripts if you want them to stick.
