# brick-breaker-unity

![ci](https://github.com/Supp3r-Man/brick-breaker-unity/actions/workflows/ci.yml/badge.svg)

**Brick Burst**: a small brick-breaker for **Unity 6**. Launch the ball from the paddle, steer the bounce with where it lands on the paddle, smash a grid of bricks (some take several hits, some carry power-ups), chain hits for a score multiplier, and clear five layouts (then they repeat, tougher).

> **What is verified, and what is not.** I wrote this on a machine with **no Unity editor installed**, so **the game has never been opened or played in Unity**. What *was* checked:
> - The whole game simulation (ball movement, collisions, paddle, bricks, power-ups, scoring, lives, levels) lives in plain C# under `Assets/Scripts/Core` with no Unity types and no Unity physics. **79 tests pass** on it, compiled with the old .NET Framework C# 5 compiler.
> - I broke the rules on purpose **57 ways** (collision maths, bounce angle, combo, speed cap, power-up timers, lives, level bonus, ...). The tests caught **56**. The one survivor changes which of two bricks is processed first when two hits happen at exactly the same instant; that is an equivalent mutant (for axis-aligned hits the two reflections commute, so the final state is identical), not a test gap I could close.
> - Two scripted players run through the real simulation: one that tracks the ball cleared level 1 in 45.7 simulated seconds without losing a life (and level 2 in 137.3 s); one that never moves the paddle lost the game in 7.0 s. During both, a watcher checked that no ball ever moved further in a step than its speed allows, never sank into a brick, and never left the arena.
> - The Unity-facing scripts compile against **hand-written stubs** of the Unity API, under each input-handling configuration. That catches typos and type mistakes in my code. It does **not** check that I used the real Unity API correctly, and it says nothing about how the game looks, sounds or feels.
> - Not checked at all: `Assets/Editor/BuildScript.cs`, the `build-unity` workflow (needs a Unity license, never run), the pinned editor version `6000.0.23f1` and the `com.unity.inputsystem` 1.11.2 package version. (The `ci.yml` logic-test job did run green on GitHub after the first push, under .NET 8; it covers `Core` only.)

## Play it

1. Install **Unity 6** (any 6000.0.x; Hub will offer to upgrade the pinned version if yours differs).
2. Unity Hub → **Add project from disk** → pick this folder. The first open imports everything and generates the `.meta` files (commit them afterwards; Unity expects them in version control).
3. Open any scene (or just the default empty one) and press **Play**. There is no scene to wire up: `Bootstrap.cs` builds the camera, arena, paddle, bricks and HUD at runtime.

| Input | Action |
|---|---|
| Mouse | Move the paddle (it follows the pointer at a capped speed) |
| A / D or left / right arrows | Move the paddle with the keyboard (whichever you used last wins) |
| Space / Enter / Up / left click | Launch the ball; continue after a level |
| P / Esc | Pause (also pauses when the window loses focus) |
| R / Space / click | Restart after game over |

Mouse position is read through `Input.mousePosition` (Input Manager) or `Mouse.current` (Input System); `InputReader` supports the old, the new, or both. With no mouse, the keyboard still works.

**Rules.**
- **Bounce.** Where the ball lands on the paddle sets its angle: centre goes straight up, the edges send it out at up to 60 degrees. After every bounce the ball keeps at least 25% of its direction vertical, so it cannot rattle sideways forever.
- **Bricks.** Plain bricks take 1 to 3 hits (teal, amber, magenta). Power bricks take one hit and apply their effect when destroyed (there are no falling capsules): **green** widens the paddle by 50% for 12 s, **white** adds two balls (up to 5 at once), **blue** slows every ball to 65% for 8 s.
- **Score.** A hit is worth 5 and destroying a brick 10 per starting hit point, both times a multiplier. The multiplier is 1 plus one step per three bricks hit since the ball last touched the paddle (maximum 5). Clearing a level gives 100 times the level number; every third level restores a life (maximum 5).
- **Speed.** Starts at 9 units/s (plus 0.5 per level), rises by 0.05 for every brick hit, capped at 18.
- **Lives.** Three. The ball is lost when it falls below the arena; losing the last ball costs a life and resets speed and power-ups. Best score is kept in `PlayerPrefs`.

## Layout

```
Assets/Scripts/Core/      engine-free simulation: Game (rules + step), Geometry (swept circle vs box), Bricks (layouts + grid), HighScore, Rng, Vec2
Assets/Scripts/           Unity layer: Bootstrap, GameController, InputReader, SpriteFactory, Sfx
Assets/Editor/            BuildScript (menu "Brick Burst" + batch-mode entry points)
Tests/LogicTests/         console test runner that compiles Core without Unity (dotnet run)
.github/workflows/        ci.yml (runs the logic tests), build-unity.yml (reference, needs a Unity license)
```

Design choices worth knowing:
- **Own collision, no Unity physics.** The ball is *swept*: each step finds the earliest contact along its path against the walls, the paddle and every live brick, bounces there, and carries on with the rest of the step's distance. Flat faces use the box grown by the ball radius; corners use a circle around the corner, so corner hits get a diagonal bounce. That is why a very fast ball or a long frame cannot tunnel through anything (tested at 400 to 1500 units/s and with a 0.5 s step).
- **Fixed timestep.** The Unity layer feeds `Game.Step(1/120)` from an accumulator (capped so a long hitch cannot cause a spiral), so the simulation does not depend on frame rate and is deterministic for a given seed and input sequence.
- **Rules separate from engine.** The Unity layer only turns input into a paddle target, mirrors the state onto sprites and plays sounds for the events `Core` reports.
- **No assets, no hand-written scene.** Sprites are generated white shapes tinted at runtime; sounds are synthesised beeps; the scene is empty and the game builds itself at start-up. `Brick Burst → Create Scene` makes the empty `Main.unity` for builds.
- **Own RNG** (xorshift) so launch angles are identical on every platform.

## Run the tests

```
dotnet run --project Tests/LogicTests -c Release     # needs the .NET 8 SDK
```

It prints `79 passed, 0 failed` plus three `[info]` lines for the scripted players, and exits non-zero on any failure. CI runs the same command. (Locally, with no .NET SDK, I compiled the same sources with the old .NET Framework C# 5 compiler, which is why `Core` and the tests avoid newer C# syntax.) The mutation script I used is a throwaway kept outside the repo.

## Build and deploy

- **In the editor:** menu **Brick Burst → Build WebGL** (output `Builds/WebGL`, compression disabled so any static host works) or **Build Windows**.
- **Batch mode:** `Unity -batchmode -quit -projectPath . -executeMethod BrickBurst.Editor.BuildScript.BuildWebGL`.
- **GitHub:** this repo is the source. `build-unity.yml` is a manual (`workflow_dispatch`) game-ci pipeline that needs `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` secrets ([how](https://game.ci/docs/github/activation)). I have not run it. Hosting a playable WebGL build on GitHub Pages is a follow-up: build, then publish the `Builds/WebGL` folder.

## Steps taken

1. **Used the sibling `orb-rush-unity` project as the template** for layout and conventions, and copied its boilerplate (gitignore, workflows, build script, manifest, test csproj), renamed.
2. **Worked out the core problem first**: a ball that can tunnel through bricks is the classic bug in this genre, and Unity physics could not be run or tested here. So the collision is hand-written as a swept circle-versus-box test (grown box for faces, corner circle for corners) in engine-free C# 5.
3. **Wrote the simulation** (`Game`, `Geometry`, `Bricks`) with a fixed step, an iterative "move to first contact, bounce, continue" loop, bounce-angle-by-offset, a minimum vertical component, power-ups with timers, combo scoring, lives and level progression.
4. **Wrote the tests** (dependency-free runner) in layers: the sweep maths on its own, then layouts, paddle, walls, tunnelling at extreme speed and step size, corners, scoring, power-ups, lives and levels, determinism, and finally scripted whole games with per-step invariant checks. Compiled and ran with the old `csc`.
5. **Fixed what the tests found.** A ball that only *grazed* a brick corner (touching it while moving along the surface) counted as a hit and would have damaged the brick; a hit now requires moving into the surface. Two failures were my test mistakes (a fast ball that had not reached the paddle yet; a ball that legitimately bounced off the paddle and hit the same brick twice in one big step).
6. **Mutation-checked** 57 deliberate breaks with a script that reports "pattern missed" instead of silently passing. The first run killed 47 and left 10 survivors. Nine were real gaps (combo after losing a life and on a fresh level, multiplier on non-destroying hits, level bonus factor, "nearest hit wins", distance travelled after a bounce, separation after a bounce, wide effect after losing a ball, inactive balls); I added tests, which killed seven of them. The other two ("nearest hit wins" and "separated after a bounce") survived my first attempts because the scenario did not actually exercise them, so I tightened those tests. The tenth survivor is the equivalent mutant described above. Final: 56 of 57 killed.
7. **Wrote the Unity layer**: runtime bootstrap, generated sprites and sounds, mouse-or-keyboard paddle, fixed-step accumulator, IMGUI HUD, pause/restart, best score. In review I fixed the first mouse reading counting as a "move" (it would have grabbed the paddle at start-up).
8. **Compiled the Unity layer against stubs** for four combinations of `ENABLE_LEGACY_INPUT_MANAGER` / `ENABLE_INPUT_SYSTEM`. The one error (a missing `Mathf.Min(int, int)` overload) was a gap in my stub, not in the game code.
9. **Adapted the editor build script and workflows** (names, output paths) from the template.

## Known gaps

- Never run in Unity: look, feel, sound, difficulty tuning (speeds, sizes, brick HP, power-up durations) are guesses. The scripted players only show the rules are consistent, not that the game is fun or fair.
- Only levels 1 and 2 were played through by a script; levels 3 to 5 are checked for valid layout and spacing but not played.
- Ball physics is circle versus axis-aligned box only: no spin, no paddle momentum, and a hit always reflects perfectly. If one step contains more than 12 bounces (a ball squeezed in a gap barely wider than itself at extreme speed) the rest of that step's distance is dropped.
- Power-ups apply instantly when a brick breaks; there are no falling pickups.
- The paddle bounce resets the combo for any ball, so with multi-ball a second ball touching the paddle breaks the first ball's chain.
- Keyboard and mouse only: no touch or gamepad input.
- No `.meta` files yet, no committed scene, no ProjectSettings beyond the version file; Unity fills these in on first open.

## License

MIT
