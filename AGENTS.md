# Working on Drone Warfare (for coding agents)

Drone Warfare is a single-player real-time drone tactics game in Unity 6 (6000.6.7f1, HDRP). Its world is fictional: Norvane against the Dravin Coalition, with no real countries and no civilians as targets. Results read as "mission successful" or "campaign complete", never "war won".

## Who does what
- Two agents build this game together for the owner: Claude (Anthropic) and ChatGPT/Codex (OpenAI).
- Claude integrates. It builds the game, runs the autotest, reviews every change and merges it into `environment-and-asset-hunt`.
- Codex works in this worktree (`C:\Users\ali10\Documents\DroneWarfare-codex`), on the branch it was given:
  - Commit to that branch only, in small commits with clear messages.
  - Never push, merge, rebase, or switch to another branch, and never touch the main checkout (`C:\Users\ali10\Documents\DroneWarfare`).
- Finish with a short report: what changed, why, and what you couldn't verify.

## What you can run here
- **Unity:** don't open or run it in this worktree. It has no imported Library, and importing one costs about 3 GB and 30 minutes. Claude compiles, builds and plays your branch after merging. So write code that compiles first time: check every API you call by reading its source.
- **Blender 5.2:** you may run it headless:
  `"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python Tools/Blender/<script>.py -- <out dir> [preview dir]`
  - Helpers live in `Tools/Blender/common.py`.
  - Models are exported as FBX to `Assets/DroneSandbox/Resources/Models`.
  - Blender's FBX export turns any object three or more levels below the root 90 degrees on its side. Keep meshes at most two levels deep (see `export_model` and `make_wildlife.py`).
- **Python 3.13:** run it with `-I`.
- **Sound:** `Tools/make_sfx.py` generates sound effects with ElevenLabs.
  - The key is in `C:\Users\ali10\.elevenlabs-key`. Never print it, copy it, or put it in a file or commit.
  - Don't run paid generation unless your task asks for it.

## Code style
- **Match the surrounding code.**
  - Put a plain-English comment above each class and method that says what it does and why.
  - Use the same naming as the rest of the file, and don't add a heavier comment density than it has.
- **Constraints:**
  - No `System.Linq` in game code.
  - No new packages.
  - Write C# 9-compatible Unity code.
- **Line endings:** CRLF, like the rest of the repo.
- **Scope:** keep each change to the files the task needs. Other code is being changed at the same time in the main checkout. In particular, don't edit these files unless the task says so:
  - `SandboxGame.cs`, `SandboxHud*.cs`, `Theatre.cs`, `Coach.cs`, `AutoTest.cs` and `Missions.cs`.

## Where things are
- **Game code:** `Assets/DroneSandbox/Scripts`.
  - Missions are built in `Missions.cs` (the Operations) and `Situations.cs` (the campaign's procedural sectors).
  - Units: `EnemyUnit.cs` and `Drone.cs`. Recon and intel levels: `Recon.cs`.
  - The campaign: `Theatre.cs`.
- **Design:** the 1.0 design is settled. The four pillars:
  1. Find before you strike.
  2. Every drone counts.
  3. Grounded and fictional.
  4. Short missions, deep planning.
