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
| F | Build or remove a barricade at a doorway or window |
| Right mouse | Aim down sights (hold) |
| Left mouse | Fire |
| R | Reload (animated; longer when the magazine is empty) |
| H | Toggle the hitbox camera |
| Tab | Toggle the controls overlay |
| Esc | Free the cursor (click the Game view to grab it again) |

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
  dummy (which falls the way it was hit), smashes a third of a barricade per strike, and
  knocks a hole in a soft wall. It interrupts a reload.
- **Damage depends on where you hit.** One bullet to the head is always lethal, even after
  passing through a wall. The torso takes full damage (34) and arms and legs take three
  quarters (`DamageZone.multiplier`). The HUD names the part you hit.
- **Damage falls off with range.** Full damage out to 18 m, dropping in a straight line to
  55% at 38 m and beyond (`Weapon.falloffStart`, `falloffEnd`, `falloffMinScale`). Headshots
  still kill at any range. Bullets also drop with gravity.
- **Bullets penetrate soft cover.** Anything with a `Surface` component set to Wood or
  SoftWall lets bullets through at reduced damage (up to four layers); everything else stops
  them. Soft walls are thin panels over studs: each bullet knocks a panel out, so you can
  open sight lines, but the studs stop you walking through.
- **Barricades.** Wooden planks with slits between them. Two bullets break a plank, three
  melee strikes clear the whole thing, and F builds or removes one (1.5 s / 1 s, weapon
  lowered). The window starts boarded up; the doorway starts open.
- **Climbing over cover.** Space always jumps. If the jump carries you into cover up to
  1.3 m high while you hold forward, you climb over it, or through a window once its
  barricade is gone. You stay crouched over open cover and tuck low through a window.
- **Leaning is subtle on screen, real on the hitbox.** The upper body tilts 14 degrees and
  slides 10 cm; the view only rolls half of that (`cameraRollScale`).
- **Leaning changes the shot.** Recoil follows the tilted gun, so a spray climbs toward the
  side you lean to. Hip fire is slightly worse while leaned and clearly worse while the lean
  is still moving. Sprinting, jumping and vaulting straighten you up.
- **Crawl spaces.** The low tunnel has 0.8 m of headroom: only a prone player fits, and you
  cannot stand up inside it.
- **Reloads are staged.** The gun cants, the old magazine drops, a new one is pushed in and
  the rounds only count once it seats. Reloading from empty also charges the bolt and takes
  longer (`Weapon.reloadTime` vs `Weapon.emptyReloadTime`). You cannot fire or aim mid-reload.
- **Sprint has weight.** Head and weapon bob scale with speed, the view widens slightly, and
  the gun has to come back up before it can fire.

The range has one opening per mechanic: a boarded-up window, a doorway to lean around (and
barricade), a soft wall, and a prone-only crawl tunnel, plus static and strafing targets, a
platform and a ramp.

## Layout

- `Assets/SiegeSandbox/Runtime` - gameplay scripts (`PlayerController`, `Weapon`,
  `Projectile`, `Target`, `DamageZone`, `Surface`, `Breakable`, `Barricade`, `SoftWall`,
  `Interactor`, `Hud`, `HitboxDebugCamera`).
- `Assets/SiegeSandbox/Editor` - `SandboxBuilder` generates the scene, materials and meshes
  from code (**Siege Sandbox > Build Sandbox Scene**). Edit the builder rather than the
  scene if you want changes to survive a rebuild.
- `Assets/SiegeSandbox/Tests` - play mode tests for the mechanics
  (**Siege Sandbox > Run Mechanics Tests**, results in `Logs/mechanics-tests.txt`).

Tuning values (speeds, heights, lean angle, spread, recoil pattern, fire rate) are public
fields on the Player and Weapon objects in the scene. Because the scene is generated,
change the defaults in the scripts if you want them to stick.
