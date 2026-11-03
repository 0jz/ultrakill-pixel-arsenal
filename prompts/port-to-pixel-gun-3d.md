# Agent prompt: the Pixel Arsenal armory, inside Pixel Gun 3D, carrying ULTRAKILL's arsenal

Copy everything below the line into a new agent session (Claude Code inside the Melty app works best). It is the reverse direction of *ULTRAKILL: Pixel Arsenal*. Pixel Gun 3D: PC Edition is the host. ULTRAKILL brings its weapons into it through the same six-category, six-per-slot armory menu.

---

You are building a Melty mashup in which **Pixel Gun 3D: PC Edition is the host game** (recipe role "primary") and **ULTRAKILL is a required companion** (role "companion"). The player is in Pixel Gun 3D. Their own installed ULTRAKILL supplies V1's weapons, read from the player's ULTRAKILL folder while the mashup runs: revolver, shotgun, nailgun, railcannon, rocket launcher and their variants, plus the Knuckleblaster and Whiplash arms. They are chosen from an armory menu that copies the one in the existing mashup *ULTRAKILL: Pixel Arsenal* (Melty slug `ultrakill-pixel-arsenal`; source in this PC's folder `Melty/ultrakill-pixel-gun-3d-pc-edition`). Read that project's `sheets/*.json`, `src/ArmoryMenu.cs`, `src/Arsenal.cs` and `src/PgWeapon.cs` before designing anything. The menu, the loadout rules and the sheet-driven workflow carry over. The engine side is reversed.

## Ground rules (do not skip)
1. Connect to Melty's tools first. Call `game_info` on `custom-pixel-gun-3d-pc-edition` and `ultrakill`, `search_mashups` for both, and `mashup_info` on `ultrakill-pixel-arsenal`. Treat `game_info` as the authority on account safety for Pixel Gun 3D. If it flags an account risk, follow exactly what it says. If it does not, build normally without inventing warnings. Never work around any anti-cheat.
2. Use Melty's modding kit (`modding_kit`, then its `skills/mod-any-game/SKILL.md`) for recon, route choice, assets and in-game testing. Publish only on Melty, with Melty's tools.
3. Never copy either game's files into the project or the upload. ULTRAKILL's models, textures, sounds and prefabs are read at runtime from `{game:ultrakill}`, which Melty passes in (launch env, e.g. `PGA_UK_DIR`). Pixel Gun 3D's own files stay where they are.
4. Keep the design in JSON sheets (one row per thing, one column per property), the same way `ultrakill-pixel-arsenal/sheets` does it. Write a preflight that lists every empty cell and every cross-sheet reference that doesn't resolve, and build only when it is clean. Generate the per-row code from the sheets.
5. Talk to the creator in plain words, never shown code or paths. Ask them only about the game itself.

## What the player gets
- Inside Pixel Gun 3D's own modes (whichever ones `game_info` and your recon say are safe to mod), V1's weapons replace or add to the six PG3D categories using this mapping, kept in a `categories` sheet:
  - **Primary**: Nailgun (Attractor, Overheat, Jumpstart)
  - **Backup**: Revolver (Piercer, Marksman, Sharpshooter)
  - **Melee**: Feedbacker parry and Knuckleblaster punch, with Whiplash as a gadget-like pull
  - **Special**: Shotgun (Core Eject, Pump Charge, Sawed-on)
  - **Sniper**: Railcannon (Electric, Screwdriver, Malicious)
  - **Heavy**: Rocket Launcher (Freezeframe, S.R.S. Cannon, Firestarter)
- **The armory menu** is the same as in Pixel Arsenal. It has six category tabs in PG3D's colours, six loadout spots per category, and a searchable icon grid with name, damage, fire rate and type on hover. Click a spot, then a weapon. ULTRAKILL's weapon icons come from the player's ULTRAKILL (its `WeaponDescriptor` sprites). The loadout is saved per player.
- Weapons fire with ULTRAKILL's rules converted into PG3D's damage model: hitscan beams, coin ricochets for the Marksman, nails, rails and rockets. Each weapon is a row in a `weapons` sheet with columns for ukPrefab, pg3dCategory, damage (ULTRAKILL value × a per-category scale), fireRate, clip (ULTRAKILL guns have none, so pick per category and record why), projectile, sound and icon.
- V1's style meter is optional (a `systems` row). If it is in, it is ULTRAKILL's style ranks (Destructive to ULTRAKILL) shown in PG3D's HUD and fed by PG3D kills.

## Technical route you must work out (decide yourself; tell the creator in one line)
- **Pixel Gun 3D is Unity 6000.3, IL2CPP** (`GameAssembly.dll`, `il2cpp_data/Metadata/global-metadata.dat`, Addressables in `StreamingAssets/aa` plus the downloaded cache in `%USERPROFILE%/AppData/LocalLow/Pixel Gun Team/Pixel Gun 3D/AddressablesCache`). It is not in Melty's catalog, so Melty installs no loader for it. The route has to be a loader Melty can install by itself, or code you ship in the release. `one_click_check` decides whether that works. A loader the player installs by hand is not acceptable. If nothing can be shipped in one click, stop and tell the creator exactly why before building.
- Weapon data in PG3D lives in each `Weapons/Models/Weapons/Weapon<N>.prefab` bundle's `WeaponSounds` MonoBehaviour (`categoryNabor` 1-6 = Primary, Backup, Melee, Special, Sniper, Heavy; `ammoInClip`, `fireRateShop`, `damageByTier`, `bazooka`, `railgun`, `isShotGun`, `isMelee`, `flamethrower`, `isZooming`…), with `InnerWeaponPars` holding its AudioClips. Pixel Arsenal's `src/PgCatalog.cs` and `src/PgBundles.cs` already read all of this, and they show how bundles reference each other (CAB names) and where English names live (`Localization/Language_English.prefab`, `mTerms`).
- ULTRAKILL is Unity 2022.3 Mono. Its weapon prefabs come from Addressables (`GunSetter.revolverPierce`, `shotgunGrenade`, `nailOverheat`, `railCannon`, `rocketBlue` …, all `AssetReference[]`) in `ULTRAKILL_Data/StreamingAssets/aa`. Unity 6 cannot load 2022 bundles directly the other way round either, so read ULTRAKILL's meshes, textures, sprites and FSB5 audio with AssetsTools.NET (MIT, ship it with credit) exactly as Pixel Arsenal reads PG3D's. Pixel Arsenal's `PgReader` and `Fsb5` classes work in both directions.
- Spawn the guns as PG3D weapons. Either clone a PG3D weapon prefab of the matching category at runtime, swap its mesh, texture and sounds for ULTRAKILL's and set its `WeaponSounds` numbers from the sheet, or drive the firing yourself. Prefer the clone: PG3D's own networking, ammo HUD and animations then keep working.
- If PG3D modes you touch are online, everything has to stay within what `game_info` allows for this game. If it allows only offline or campaign modes, scope the mashup to those and say so in the listing.

## Build, test, publish (Melty)
1. `create_mod` for a new draft with the title, tagline and description written from the real build. Games: `custom-pixel-gun-3d-pc-edition` (primary) and `ultrakill` (companion). Ask the creator for licence and remix permission. Do not guess.
2. Package only your own code, the AssetsTools.NET DLL and its MIT notice. `inspect_package`, `validate_recipe` (with the slug, to get the test-mode code) and `one_click_check` must all be clean.
3. `start_upload`, PUT the bytes, `finish_upload`, then `submit_release` with the validated recipe. Its `test.checks` should be four checks your mod really reports (e.g. `uk-weapons-scanned`, `armory-opens`, `loadout-applied`, `uk-weapon-fired`), written as `pass <name>` lines to the checks file named in `%LOCALAPPDATA%\Melty\test\<slug>.json`.
4. `test_release`, then `test_result` until it passes. Never close or restart a copy of a game the creator has open. Ask them to close it if the test needs to start it.
5. Get a real in-game screenshot of the armory with ULTRAKILL weapons, or of V1's railcannon in a PG3D map (`add_screenshot`), or let the passing test add one.
6. Tell the creator in a few lines what players get, ask "<title> is ready to publish. Are you ready to publish it?", and call `publish` only on their yes. Then check `mod_status`.
