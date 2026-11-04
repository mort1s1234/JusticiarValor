# Justiciar Valor: release package

Build the 1.6 and 1.7 projects in Release configuration, then run `Source/Package-Release.ps1`.
The script writes a dated zip under the system temporary folder's `JusticiarValor-release` directory;
`-OutputPath` selects a different destination. Existing archives are never overwritten.
The zip contains one `Justiciar Valor` folder, ready to install under `RimWorld/Mods/`.

## VPE startup compatibility

- Warlockbane's VEF/VPE ability patches are applied after Def loading and reference resolution. This prevents
  Harmony from initializing VPE's static `FireTornadoDef` field before its ThingDef exists, which left the field null
  and logged a missing `VPE_FireTornado` on RimWorld 1.6.
- `VPE_FlameTornado` remains the ability name and `VPE_FireTornado` remains the spawned ThingDef name.
  Validation with `-WithVPE` now casts the ability and checks that the tornado spawns, alongside the shield checks.

## Mission rewards

- Ordinary Imperial Missions guarantee one displayed Justiciar item. The native random-option generator gives
  weapons weight 95 and armor weight 5 among affordable branches. Rewards use tags, native quality/stuff selection,
  and a market-value cap of 2,800 plus 150 per day of the planned 6–10 day stay (3,700–4,300).
- The native market-value generator can sample only unaffordable qualities. A small subclass supplies an affordable
  normal-quality fallback in that case, preventing those samples from repeatedly excluding the weapon branch.
- Blueprint commissions share the ordinary mission's shuttle and failure handling. They promise one native
  `MJV_JusticiarGear` techprint in place of equipment: three completed sermons, at least 75% attendance at each,
  no refused/cancelled requests, and at least 70% average mood. Early full conversion waives only the sermon count.
  The research project's existing requirement of two applied techprints is unchanged.
- The event's existing reception dialogue offers a codex-auspex or one displayed item with a budget of 2,800–3,000.
  The chosen item is saved, not rerolled when talking again, and delivered only if the existing hospitality conditions
  are met. An unchosen or unearned item is discarded. Ordinary events offer no techprints.
- Other newly generated titled Imperial NPCs have a 2% chance to carry one codex. The postfix only adds an item;
  it does not replace inventory, alter titles or modify pawn-kind defs. Player pawns, justiciars, dead pawns,
  requests forbidding gear, and inventories already containing a codex are excluded. Returning world pawns do not reroll.
- Existing saved missions keep their old completion signals and codex rules. The Disgraced Sermon quest is unchanged.
  New task/event state uses native Scribe, quest rewards, dialogue and drop pods. No global tick scan is added.

## Runtime contents

| Included | Purpose |
|---|---|
| `About/About.xml`, `About/Preview.png`, `About/ModIcon.png`, `About/PublishedFileId.txt`, `LoadFolders.xml` | Metadata, preview, mod icon, Workshop identity and version selection |
| `1.6/Assemblies/JusticiarValor.dll`, `1.6/AssetBundles/justiciarvalor_win`, `justiciarvalor_mac` | RimWorld 1.6 code and shaders |
| `1.7/Assemblies/JusticiarValor.dll`, `1.7/Defs/`, `1.7/Shaders/` | RimWorld 1.7 code and native shaders |
| `Common/Defs/`, `Common/Patches/`, `Common/Languages/`, `Common/Textures/` | Shared runtime content |
| `Common/CE/`, `WeaponTweakData/` | Conditional Combat Extended patches and weapon compatibility data |

Sources, raw art, tests and recordings, local tools, design documents, Git data and this note are omitted.
The twelve obsolete animation atlases listed in the packaging script are already absent from the runtime texture
folder. The baker now excludes them too, and packaging also filters any copies recreated by older tools.
The active ten atlases, packed Sheet, apparel masks and eltex glow textures remain required.
Old serialized classes/fields are retained where existing saves still need them.

## Validation and remaining release work

`Source/Tests/Run-GameValidation.ps1` runs the real game with isolated save data and temporary test dependencies.
Use `-SkipVisuals` for logic/save checks without screenshot assertions, and `-WithVPE` for Vanilla Psycasts Expanded.
Use `-Version 1.6 -GameDir <1.6 installation>` for the Unity version. It also tests real reward generation, preview
and delivery, blueprint boundaries, event reward persistence, and the rare inventory hook.

The package includes the repository's current English text, Russian localization, Combat Extended patch and weapon
compatibility data. The VPE regression checks do not cover Combat Extended's combat behavior.
PublisherPlus exclusions cover development material. For the native Workshop uploader, publish the clean package
contents: the development checkout still contains sources and recordings that should not be uploaded.
