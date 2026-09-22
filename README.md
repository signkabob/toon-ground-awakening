# Toon Ground Awakening

Quick Unity 6 (URP) prototype of One Piece's Gear 5: the ground turns to rubber and ripples around the player.

## Try it

Open the project, open `Assets/Scenes/SampleScene`, press Play. No scene setup is needed:
`Gear5Bootstrap` swaps the sample ground block for a rubber sheet, makes the sample character the player,
points the camera at them and scatters test props (crates and balls with physics, fixed posts).

| Input | Keyboard | Gamepad |
| --- | --- | --- |
| Toggle Gear 5 | G | Right bumper |
| Move | WASD / arrows | Left stick |
| Jump | Space | South button |

The toggle is the `Gear5` action in `Assets/InputSystem_Actions` (project-wide actions), so it can be rebound there.

## What Gear 5 does

While Gear 5 is on:
- The ground inside the aura radius turns rubbery and ripples pulse outward from the player.
- Standing on rising ground flings the player up like a trampoline; landing hard or jumping punches a dent that ripples out.
- Everything with a collider inside the radius is rubberized: physics objects get bouncy and ride the waves,
  fixed objects bob with the ripples, and all of them squash and stretch. They ease back to normal once they leave the radius or Gear 5 turns off.

Outside the radius, or with Gear 5 off, the ground is heavily damped and behaves like normal ground.

## Scripts (`Assets/Scripts/Gear5`)

- `RubberGround` - runtime grid mesh simulated as a 2D wave equation with springs; exposes impulses, height/velocity sampling and a trampoline launch.
- `Gear5Aura` - the on/off input, wave pulses, radius scan and the white ring.
- `Rubberized` - added at runtime to things inside the aura.
- `Gear5Player` - small rigidbody controller with Gear 5 jumps, stomps and squash/stretch.
- `ToonFollowCamera` - follow camera with screen shake.
- `Gear5Bootstrap` - scripts-only scene setup; it skips itself when a scene already has a `RubberGround`.

Tuning lives in public fields on each component (aura radius, wave interval and strength, wave speed, damping, bounce gain).
