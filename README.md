# Toon Ground Awakening

Quick Unity 6 (URP) prototype of One Piece's Gear 5: the ground turns to rubber and ripples around the player.

## Try it

Open the project, open `Assets/Scenes/SampleScene`, press Play. No scene setup is needed:
`Gear5Bootstrap` swaps the sample ground block for a rubber sheet, makes the sample character the player,
points the camera at them and builds a small playground: soft walls around the edge, a two-wall
bounce shaft and a lone slab, three orange chevron launch ramps, six pinball enemies and a few test props.

| Input | Keyboard | Gamepad |
| --- | --- | --- |
| Clap Soften (Gear 5) on / off | G | Right bumper |
| Move | WASD / arrows | Left stick |
| Jump / wall jump | Space | South button |
| Ground pound (in the air) | C | East button |
| Punch | Left click / Enter | West button |

These are the `Gear5`, `Move`, `Jump`, `Crouch` and `Attack` actions in `Assets/InputSystem_Actions`
(project-wide actions), so they can be rebound there.

## Soften (the Peppy World mechanics)

These are ported from *Peppy World: Director's Cut* (Unreal 5.9, Epic MegaJam 2026). Soften is the same idea as Gear 5.

- **Clap and meter:** turning Soften on is a clap that sends a gold jelly ripple out to the radius. A power meter
  (top left) drains while it's on (8 s) and refills while it's off (5 s); you need 20% to clap.
- **Rubber floors:** the ground ripples, and while Soften is on it **auto-hops** you. Jumps are **1.6x**.
- **Ground pound catapult:** pound (C) in the air. With Soften on, landing catapults you up at **2.2x** a jump,
  punches a big ripple and knocks nearby enemies away. Off, it's just a stomp.
- **Stretchy walls:** jump into a softened wall and you cling to it, sinking slowly while the wall dents in.
  The longer you hang on (up to 1 s), the bigger the charge; jump to be flung off. Any wall gives a plain wall jump.
- **Launch ramps:** run up an orange chevron ramp to be launched off the top; softened ramps launch 1.6x harder.
- **Pinball enemies:** blobs waddle toward you and shove you. Punch or pound a softened one and it shoots off,
  ricochets off walls without losing speed and knocks other enemies along. Unsoftened, they only get knocked back.
- **Look:** everything softened turns marshmallow cream with a gold jelly band running through it, and squashes and stretches.

Outside the radius, or with Soften off, everything behaves normally.

## Scripts (`Assets/Scripts/Gear5`)

- `RubberGround` - runtime grid mesh simulated as a 2D wave equation with springs; exposes impulses, height/velocity sampling and a trampoline launch.
- `Gear5Aura` - the Soften clap input, power meter, wave pulses, radius scan and the gold ring.
- `ClapRipple` - the gold ring that races out when you clap.
- `Rubberized` - added at runtime to things inside the aura (bounce, cream/gold tint, squash).
- `Gear5Player` - rigidbody controller: auto-hops, 1.6x jumps, ground-pound catapult, wall cling and launches, punches, squash/stretch.
- `SoftWall` - wall you cling to and stretch while softened.
- `LaunchRamp` - orange chevron ramp that flings rigidbodies.
- `PinballEnemy` - blob enemy that ricochets like a pinball when hit softened.
- `ToonFollowCamera` - follow camera with screen shake.
- `Gear5Bootstrap` - scripts-only scene setup; it skips itself when a scene already has a `RubberGround`.

Tuning lives in public fields on each component (aura radius, wave interval and strength, wave speed, damping, bounce gain).
