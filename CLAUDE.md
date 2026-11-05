# ULTRAKILL: Pixel Arsenal (Melty mashup)

## Project map
- Melty: modId `cce5c183-8458-4966-9af5-93b2d6593faf`, slug `ultrakill-pixel-arsenal`. ULTRAKILL is primary (BepInEx 5). `custom-pixel-gun-3d-pc-edition` is a companion; its folder arrives as env `PGA_PG3D_DIR`.
- Games on this PC: ULTRAKILL `C:\ULTRAKILL` (Unity 2022.3 Mono). PG3D `C:\Program Files (x86)\Steam\steamapps\common\Pixel Gun 3D PC Edition` (Unity 6000.3 IL2CPP, bundles in StreamingAssets/aa plus `%USERPROFILE%\AppData\LocalLow\Pixel Gun Team\Pixel Gun 3D\AddressablesCache`).
- `sheets/*.json`: the design, and the source of truth. `tools/preflight.py <decomp>` checks the sheets and writes `src/Generated.cs`.
- `src/`: PgBundles.cs (AssetsTools.NET reader, mesh/skin/texture/FSB5; no Unity), PgCatalog.cs (scan plus TSV cache; no Unity), PgModel.cs, PgWeapon.cs, Arsenal.cs, ArmoryMenu.cs, Plugin.cs.
- Build: `python tools/build_net.py <version>`. It finds ULTRAKILL with BepInEx (env `ULTRAKILL_DIR`, else Steam libraries, else under Desktop), fetches Melty's add-on into `work/meltykit` the first time (`tools/fetch_kit.py`), patches and builds it, builds the mod (`-p:UkDir=`), then zips `dist/PixelArsenal-<version>.zip` (PixelArsenal/ and MeltyNet/). Set `ULTRAKILL_DIR` to a copy with BepInEx; a plain Steam install of an old version won't work.
- Repo: private GitHub `0jz/ultrakill-pixel-arsenal`. `work/`, `dist/` and `shots/` (game screenshots) aren't in it. Push with the 0jz token (`gh auth token -u 0jz` as an `http.extraheader`), because the Windows credential manager holds neskeee's.
- Co-op: `src/NetBridge.cs` reaches `tools/net/PixelArsenalBridge.cs` by name. Armory only freezes time when alone.
- Offline test with no game: `tools/harness` (net472) → `work/harness/Harness.exe "<pg3d dir>" work/out all|<ids>` dumps OBJ and WAV files and stats.
- ULTRAKILL decompile: `%USERPROFILE%\ultrakill-decomp` (ilspycmd 9.1).
- `work/`, `dist/`: generated output, never edited by hand. `prompts/port-to-pixel-gun-3d.md` is the reverse-port agent prompt.
