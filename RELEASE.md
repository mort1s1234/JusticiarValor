# Justiciar Valor: release package

`JusticiarValor_2026-10-27_3f64218.zip` is a playable copy of the mod built from commit `3f64218` (main, 2026-10-27).
It holds only the files the game loads. Unzipped into `RimWorld/Mods/`, it gives a `Justiciar Valor` folder that runs
on its own (Harmony, Royalty and Vanilla Expanded Framework are still required).

This note lists what the zip keeps and what it leaves out, so the same cut can be made by hand, for example before a
Workshop upload: RimWorld 1.7 uploads every file in the mod folder, including `Source/` and the design documents.
The zip does not pick up anything committed after `3f64218`. For later XML or texture edits, make the cut again with
this list.

## Keep: the game needs these

| Path | What it is |
|---|---|
| `About/About.xml` | Name, description, dependencies |
| `LoadFolders.xml` | Which folders 1.6 and 1.7 load |
| `1.6/Assemblies/JusticiarValor.dll` | The mod's code for RimWorld 1.6 |
| `1.6/AssetBundles/justiciarvalor_win`, `justiciarvalor_mac` | Effect shaders for 1.6 (no file extension, that is normal) |
| `1.7/Assemblies/JusticiarValor.dll` | The mod's code for RimWorld 1.7 |
| `1.7/Defs/ShaderDefs/`, `1.7/Shaders/*.glsl` | Effect shaders for 1.7. The game loads the `.glsl` files; they are not source code |
| `Common/Defs/`, `Common/Patches/`, `Common/Languages/` | All XML content |
| `Common/Textures/` | All textures, **except the 12 atlases listed further down** |

These look like leftovers but are used:

- The `m` files next to the Promachos textures (`PromachosArmor_Male_southm.png`, `PromachosHelmet_icon_m.png`, ...):
  colour masks. The armor and helmet use the `CutoutComplex` shader, which loads them by name.
- `PromachosArmor/Glow/` and `PromachosHelmet/Glow/`: the eltex glow.
- In `Common/Textures/Effects/JusticiarValor/Piloto/`: `Sheet.png` and these 10 atlases, which the effects load:
  AfterimageWake, OrbBurst, OrbGlitter, OrbPetals, Overdrive, PierceShock, PsychicWave, PsyOrb, ThreatLock, WaveShock.

## Leave out: not part of the release

| Path | Why |
|---|---|
| `Source/` | C# code, shader generators, tests, raw art (`Source/Textures`). Only needed to rebuild the DLLs |
| `.git/`, `.gitattributes`, `.gitignore` | Git |
| `.rimsearcher/` | Local tool |
| `Justiciar Valor.docx`, `Justiciar Valor (中文).docx` | Design documents |
| `JusticiarValor_*.zip`, `RELEASE.md` | This package and this note |

### 12 effect atlases nothing uses any more (14 MB)

In `Common/Textures/Effects/JusticiarValor/Piloto/`:

| File | Size |
|---|---|
| `CelestialBlade.png` | 0.4 MB |
| `Conversion.png` | 0.5 MB |
| `Deflect.png` | 1.4 MB |
| `InvictusBurst.png` | 1.1 MB |
| `OrbFlight.png` | 1.8 MB |
| `OrbImpact.png` | 0.4 MB |
| `PsychicShield.png` | 2.0 MB |
| `SermonHalo.png` | 1.7 MB |
| `ShieldBreak.png` | 0.5 MB |
| `ShieldHit.png` | 0.4 MB |
| `ShieldRecharge.png` | 2.0 MB |
| `ThreatPulse.png` | 1.9 MB |

Neither DLL (1.6, 1.7) and no Def refers to these names, so the game never shows them. It still loads every texture
under `Textures/` at startup, though, so they only cost loading time and memory. They are safe to delete from the repo.
If you do, tell Marukun: the Unity baking tool (`Source/Shaders/Unity/JusticiarPilotoBaker.cs`) still lists them and
would bake them again.

## Before publishing

- `About/About.xml` has `<url>https://github.com/Klon501/Mechanoids</url>`, which looks copied from another mod. It
  should probably be `https://github.com/mort1s1234/JusticiarValor`. The zip still has the old url.
- There is no `About/Preview.png`, so the mod list and the Workshop page show no picture.
- To upload to the Workshop from `Mods/Justiciar Valor`, first move everything under "Leave out" out of the folder
  (and put it back afterwards), or upload a folder that holds only the zip's contents.
