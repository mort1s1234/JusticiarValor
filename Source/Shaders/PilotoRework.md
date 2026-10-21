# Justiciar Valor effects

The effects combine the original shield and imperial mission shaders, the equipped sword's own texture, and eight active Piloto animations rendered from the user's `MRKshader合集` Unity project. `PilotoSources.tsv` records the baked prefabs, GUIDs, timing, camera and atlas layout. Older unused atlases remain available, but are not loaded into effect materials. The originals under `Assets/Piloto Studio` are not edited.

The 2026-10-21 revision restores PsychicShield's original eltex dome, directional impact ripples, hit flash, break dust and recharge glow. Sermon_Halo and Conversion_Flare again use the original imperial gold scripture rings and conversion flare. Their procedural shader bodies were recovered from the pre-Piloto generated shader copies, and their original drawing parameters restored from the earlier change records.

Other effects retain the armor's muted gray-green palette. Invictus now plays GaleWind_Burst at its cast location for 0.55 seconds, using a cropped source interval to remove the empty lead-in. The pawn afterimage still samples the actual pawn atlas, with a Piloto wind animation beside it.

Psy-orbs use fixed slots based on maximum capacity, so launching or synthesizing an orb never repositions the other orbs. A wider, slower elliptical orbit separates the smaller spheres from the wearer. The last 30% of synthesis fades the next orb into its vacant slot. The seed is initialized before either launch or drawing, keeping the launch origin consistent even before its first visible frame.

### Composite revision (2026-10-21, second pass)

Four effects are now composed from several Piloto sources each: baked particle animations (atlases, as above) layered with Piloto textures packed into one sheet, `Common/Textures/Effects/JusticiarValor/Piloto/Sheet.png`. `pack_sheet.py` builds the sheet from `Assets/Piloto Studio/Textures`, records each cell's source in `PilotoTextures.tsv`, and generates the cell rects for the shaders (the `SHEET` block of `MJV_FxLib.shaderlib`) and for C# (`Fx/FxSheet.cs`). Mask cells keep their light in every channel and are read from alpha, like the atlases; the debris rocks keep their own colours. `MJV_Sprite` draws any cell as a glow, in its own colours or tinted (dust, shadows).

- **Threat pulse** (`ThreatPulseFx`, `MJV_ThreatPulse`): a sight (FX_Tech_Sight_InnerSide) closes on the wearer, a sonar ping (Ring_Shockwave) races out with a soft glow behind it and reveals the HUD ring (Tech_DecalRing2). A chevron (FX_Tech_Shapes_Lined) on the ring points at each pawn taking aim, and the Tech_GroundAOEIndicator lock-on settles over each of them. CompThreatSense passes the pawns it counted.
- **Orb flight** (`OrbWake`, `MJV_OrbTrail`): the baked Projectile_Arcane wake is replaced by a wake anchored to the ground the orb flew over: two Piloto ribbons (Trail13, Trail_Starry_2) drifting back, a hot core, and SparklesPickbook sparkles shed along the path that spread and fade. The wake grows from the launch point, never reaches behind it, and its tail catches up with the impact (`OrbWakeFx`). A StarCrossFlare glint turns on the orb.
- **Orb impact** (`OrbImpactFx`): StarImpactFlare flash and Ring_Shockwave2 ring under three baked bursts: Projectile_Hit_Tech (core and sparks), ARPG_HighTech_Hit (radial petals, along the flight) and Impact_Light (lingering glitter).
- **Celestial Pierce** (`CelestialPierceFx`, `MJV_CelestialBlade`, `MJV_GroundFissure`) plays in beats. A copy of the equipped weapon's own texture starts exactly on the blade in the wielder's hand (the game's aiming pose: `PawnRenderUtility.DrawEquipmentAiming`) and:
  1. grows there, overshooting slightly (0–0.20 s);
  2. lifts straight up, turning point-up (to 0.38 s);
  3. flies in an arc over the target, turning point-down, while a sight closes on the ground beneath (to 0.58 s);
  4. hangs there, drawing back, trembling and whitening (to 0.70 s);
  5. slams down, stretched by its speed (0.76 s), then squashes and shudders in the ground.
  The landing plays a sound (`impactSound`, Longjump_Land) and shakes the camera (`impactShake` 0.3). Radial cracks (GroundCracks_Radial) open where it went in, and the fissure (TentacleCrack) runs both ways along the line at 14 cells/s: back towards the wielder and on to the wall-limited end. The cracks glow hot, then cool to dark, and are drawn at floor altitude so pawns stand over them. Layered over this are the ARPG_Paladin_Sword_Shock burst, a Ring_Shockwave2 ground shock, a StarImpactFlare flash, Shockwave_Dusty and SmokePuff_Quad dust, and Dust_Rocks stones thrown in arcs. The copy stays buried with a glowing entry line and then dissolves. Weapon alpha is thresholded like the game's cutout shader, so a texture's faint border does not show as a box.
  Each foe in the line is struck when the crack reaches it, not at the cast. The targets are still chosen at the cast, with the same damage, stun, cooldown and AI targeting, but the hit is scheduled (`MapComponent_JusticiarStrikes`, saved with the map, which also schedules the landing) 0.76 s or more later and lands with a `PierceHitFx` burst.
- **Conversion**: a sermon convert no longer gets the imperial flare. The game's own conversion feedback remains: the certainty drop that `IdeoConversionAttempt` floats over them, plus the interaction bubble of `InteractionDefOf.ConvertIdeoAttempt` with the new faith's icon, over each convert. The flare branch was removed from `MJV_SermonHalo`.

The other effects were reworked in the same way:

- **Justiciar Valor wave**: a flash, a Ring_Shockwave2 front racing to the wave's actual radius, the Bard Flash Shockwave (Super Realistic 02) over a dimmed Holy explosion (skipping its opening six-pointed star) and dust along the front. No ground cracks.
- **Psychic shield** (plain design, no hexagons): the dome is clean eltex glass, with a rim, a glint, a sky reflection, a few soft lines of drifting energy and smooth ripples where shots are caught; low energy thins it in soft patches. Hits play Projectile_Hit_Tech sparks, a ripple ring and a flash where the shot struck. Breaking is plain: the dome itself flashes, swells and fades, and one thin ring runs off it. Recharging is plain too: one ring closes onto the wearer while the dome fades and grows back in (`CompPsychicShield` remembers the reset), with a soft glow as they meet. These replace the vanilla flash flecks, one of which lit a whole square. The cube-based Tech_ImpactSimple and Tech_CubeBurst atlases are no longer loaded.
- **Invictus**, redone plainly: a flash and two rings racing out, tinted the white-violet the overcharged shield takes on (sheet sprites; the GaleWind_Burst atlas is no longer loaded). During the overdrive a faint Feathering Speed Aura swirls about the wearer. Afterimages are clearer and each has a wind streak back to the previous one.
- **Psy-orbs in orbit**: a soft halo, a short comet tail along the orbit, and sparks spiralling into the slot of an orb being formed.
- **Warlockbane**, in three plain beats: the side of the ward facing the psycaster lights up (Ring_EdgeLower), the psycast flies back as one bright mote with a short tail (Trail_TaperedSoft, StarCrossFlare), and one flash and ring mark the caster. When the bearer also wears the Promachos helmet, its threat pulse usually plays at the same moment, since the psycaster is aiming at them; the recorder takes the helmet off for this scene.

The new atlases are ThreatLock, OrbBurst, OrbGlitter, OrbPetals, PierceShock, WaveShock and Overdrive (`JusticiarPilotoBaker`, with windows cropped to each source's action). ThreatPulse.png, OrbFlight.png, OrbImpact.png, Deflect.png, CelestialBlade.png, Conversion.png, ShieldHit.png, ShieldBreak.png, ShieldRecharge.png and InvictusBurst.png are no longer loaded. The baker also takes `-jvPilotoScout <tsv>` to bake arbitrary prefabs (including the Super Realistic FX bundles) into a scratch folder for comparison.

| In-game effect | Piloto prefab |
| --- | --- |
| Psychic shockwave | Holy / AOE_Explosion_Holy |
| Threat pulse | Sheet: sight, ping ring, HUD ring, chevrons; Tech / Tech_GroundAOEIndicator lock-on |
| Psychic shield | Plain procedural glass dome; Tech / Projectile_Hit_Tech hits; break and recharge: the dome itself and one Ring_Shockwave |
| Invictus release | Sheet: soft glow, cross flare, Ring_Shockwave; Wind / Feathering Speed Aura during the overdrive |
| Celestial Pierce | Equipped weapon texture; Super Realistic / Paladin Sword_Shock; sheet: cracks, fissure, dust, rocks, flare |
| Warlockbane deflection | Sheet: Ring_EdgeLower ward, Trail_TaperedSoft and StarCrossFlare return, Ring_Shockwave and StarImpactFlare on the caster |
| Orbiting psy-orb | Arcane / Orb_Arcane |
| Orb flight | Sheet: Trail13, Trail_Starry_2, sparkles, cross flare |
| Orb impact | Tech / Projectile_Hit_Tech, Light / Impact_Light, Super Realistic 02 / ARPG_HighTech_Hit; sheet: flare, jagged ring |
| Pierce hit | Tech / Projectile_Hit_Tech; sheet: dust, rocks |
| Sermon halo | Original procedural scripture halo |
| Conversion | The game's own conversion bubble and certainty text |
| Afterimage wake | Wind / GaleWind_Woosh |

## Rebuild

Use Unity 2022.3.62f3 with the source project closed. From the mod root:

```powershell
.\Source\Shaders\Invoke-Unity.ps1 -Task BakePiloto
# Rebuild just the revised burst, preserving the other atlas files and source records:
.\Source\Shaders\Invoke-Unity.ps1 -Task BakePiloto -PilotoOnly InvictusBurst
# The packed texture sheet and its cell tables:
python .\Source\Shaders\pack_sheet.py
.\Source\Shaders\Invoke-Unity.ps1 -Task Build
dotnet build Source\JusticiarValor\JusticiarValor.csproj -c Release
dotnet build Source\JusticiarValor\JusticiarValor.1.6.csproj -c Release
```

`BakePiloto` works on temporary prefab/material instances, synchronizes their Shader Graph Built-in transparency, disables depth fading against the absent capture background, and simulates the original particle systems with fixed seeds. Each output is a 2048×2048 atlas containing 64 frames. Alpha stores emission intensity independently of texture color-space decoding. It rejects empty or magenta captures before deployment.

The atlas runtime interpolates frames, overlaps the ends of continuous animations, fades short animations out, and supplies the armor palette. All effects use the existing map drawing component and game-time clock, so pause, speed changes, map changes and save/load retain the same lifecycle. RimWorld 1.7 uses native GLSL shaders; 1.6 uses generated Unity shader bundles. Loose mod PNGs use the same atlas row order on both engines; they do not need the vertical inversion used by some built-in resources. Native pawn-atlas coordinates are kept intact. Neither version instantiates the source Unity particle systems at runtime.

`Invoke-Unity.ps1 -Task Preview` renders the principal effects over a neutral ground. Bake contact sheets are in `%TEMP%\justiciarvalor-build\piloto\Previews`.

## Validation

The game validation harness checks the eleven active atlas bindings, the packed sheet bindings, restored procedural material bindings, stable orb slots, and the equipped sword texture, in addition to abilities, shield damage/recovery, orb launch/impact, afterimages and save/reload. `Run-GameValidation.ps1 -Record -RecordOnly ThreatPulse,PsyOrbs,OrbImpact,CelestialPierce,SermonAndConversion` records just those scenes. `-EffectsOnly` excludes the unrelated random quest lifecycle while still rendering sermon and conversion effects. The ordinary full regression remains available without that flag. The recorder includes both a normal Celestial Pierce cast and four cardinal-direction casts.

The visual stage clears a disposable test area, resets pawn tween positions after paused fast-forward, and holds exact animation frames for screenshots. Damage fixtures avoid random target armor and count actual melee damage rather than injury hediffs (severed parts are not injury hediffs). Shader compilation is checked during the 1.6 bundle build and by the 1.7 runtime.

The earlier 2026-10-20 reports and recordings remain under `Source/Tests/Output/Piloto`. Revision results are kept separately under `Source/Tests/Output/Revision-20261021`. A hidden 1.6 game window can return unusable screenshots; shader preview images and runtime checks do not establish full 1.6 in-game visual verification. For a user-requested visible 1.6 test, `Run-GameValidation.ps1 -Version 1.6 -GameDir <game> -EffectsOnly -VisibleWindow` enables the presented window that Unity's screenshot path requires.

The final 2026-10-21 builds passed runtime checks on 1.7.4982 and 1.6.4633. The 1.7 screenshots contained no magenta fallback pixels, and all ten recording scenes completed at 1920×1080, 60 fps. The requested effects are assembled into a 33.27-second preview. On 1.6 the runtime report passed, but the wrapper correctly failed its screenshot checks because the hidden window returned unchanged images; that visual limitation remains explicit in the accompanying report.
