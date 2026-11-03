// Pixel Arsenal: ULTRAKILL x Pixel Gun 3D. Entry point: config, keys, the GunSetter hook, catalog scan and Melty test mode.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PixelGunArsenal
{
    [BepInPlugin("melty.pixelarsenal", "Pixel Arsenal", "0.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<Key> ToggleKey, MenuKey, ReloadKey;
        static ConfigEntry<int> texMax;
        static ConfigEntry<float> gunScale, dashScale;
        public static float DashScale { get { return dashScale.Value; } }
        static ConfigEntry<string> pg3dPath;
        public static int TexMax { get { return texMax.Value; } }
        public static float GunScale { get { return gunScale.Value; } }
        public static bool ArsenalOn { get { return Enabled.Value; } }
        static PgCatalog scanning;
        public static ArmoryMenu menu;

        void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "ArsenalEnabled", bool.Parse(Sheets.CfgArsenalEnabledDefault), "Pixel Gun guns replace V1's arsenal (off = ULTRAKILL's own guns)");
            ToggleKey = Config.Bind("Keys", "ToggleKey", (Key)Enum.Parse(typeof(Key), Sheets.CfgToggleKeyDefault), "Switch the Pixel Gun arsenal on/off");
            MenuKey = Config.Bind("Keys", "MenuKey", (Key)Enum.Parse(typeof(Key), Sheets.CfgMenuKeyDefault), "Open the armory");
            ReloadKey = Config.Bind("Keys", "ReloadKey", (Key)Enum.Parse(typeof(Key), Sheets.CfgReloadKeyDefault), "Reload the gun in hand before its clip runs out");
            texMax = Config.Bind("Look", "TextureMaxSize", int.Parse(Sheets.CfgTextureMaxSizeDefault), "Largest texture side after the ULTRAKILL downgrade");
            gunScale = Config.Bind("Look", "GunScale", float.Parse(Sheets.CfgGunScaleDefault, System.Globalization.CultureInfo.InvariantCulture), "Size of guns on screen");
            dashScale = Config.Bind("Look", "DashDistanceScale", float.Parse(Sheets.CfgDashDistanceScaleDefault, System.Globalization.CultureInfo.InvariantCulture), "Metres of ULTRAKILL dash per unit of Pixel Gun dash");
            pg3dPath = Config.Bind("General", "PixelGun3DFolder", "", "Pixel Gun 3D: PC Edition folder (empty = the one Melty passes, else found in your Steam libraries)");
            new Harmony("melty.pixelarsenal").PatchAll(typeof(Plugin).Assembly);
            // ULTRAKILL destroys BepInEx's own object on level load, so the per-frame work lives on one of ours
            var host = new GameObject("PixelArsenal Runner");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<Runner>();
            menu = host.AddComponent<ArmoryMenu>();
            Arsenal.LoadLoadout(Path.Combine(Paths.ConfigPath, "PixelArsenal.loadout.txt"));
            Test.Init();

            string dir = FindPg3d();
            if (dir == null) { Log.LogError("Pixel Gun 3D: PC Edition not found; set PixelGun3DFolder in the config."); Test.Report("guns-scanned", false, "Pixel Gun 3D folder not found"); return; }
            Log.LogInfo("Reading Pixel Gun 3D from " + dir);
            string cacheFile = Path.Combine(Paths.CachePath, "PixelArsenal/catalog.tsv");
            new Thread(() =>
            {
                try
                {
                    var cat = PgCatalog.LoadOrScan(dir, PgCatalog.DefaultCacheDir(dir), cacheFile, c => scanning = c);
                    cat.guns.RemoveAll(g => g.tag.StartsWith("MonsterHunterMechBody")); // mech parts, not guns
                    Log.LogInfo("Pixel Gun 3D guns found: " + cat.guns.Count);
                    int regraded = 0;
                    foreach (var g in cat.guns)
                    {
                        int r;
                        if (Sheets.RarityOverrides.TryGetValue(g.weaponId, out r)) { g.rarity = r; regraded++; } // sheets/rarity_overrides.json
                        g.name = g.name.Replace('_', ' ');
                    }
                    Log.LogInfo("Guns regraded from the wiki: " + regraded);
                    Arsenal.Catalog = cat;
                    Test.Report("guns-scanned", cat.guns.Count > 0, cat.guns.Count + " guns");
                }
                catch (Exception e) { Log.LogError("scan failed: " + e); Test.Report("guns-scanned", false, e.Message); }
                scanning = null;
            }) { IsBackground = true }.Start();
        }

        static string FindPg3d()
        {
            foreach (var c in new[] { pg3dPath.Value, Environment.GetEnvironmentVariable("PGA_PG3D_DIR") })
                if (!string.IsNullOrEmpty(c) && Directory.Exists(Path.Combine(c, "Pixel Gun 3D_Data"))) return c;
            try
            {
                var steam = (string)Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) ?? @"C:\Program Files (x86)\Steam";
                var vdf = Path.Combine(steam, "steamapps/libraryfolders.vdf");
                var libs = File.Exists(vdf) ? Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).ToList() : new System.Collections.Generic.List<string>();
                libs.Add(steam);
                foreach (var l in libs)
                {
                    var d = Path.Combine(l, "steamapps/common/Pixel Gun 3D PC Edition");
                    if (Directory.Exists(Path.Combine(d, "Pixel Gun 3D_Data"))) return d;
                }
            }
            catch { }
            return null;
        }

        static GunSetter waitingSetter;
        class Runner : MonoBehaviour { void Update() { Tick(); } void OnGUI() { DrawGui(); } }

        static void Tick()
        {
            Arsenal.Pump();
            NetBridge.Connect();
            if (menu) menu.KeepRunning();
            var kb = Keyboard.current;
            // the scan finished after the level's weapons were set: fill them now
            if (Arsenal.Catalog != null && waitingSetter) { var gs = waitingSetter; waitingSetter = null; Arsenal.Fill(gs, false); }
            if (kb != null && ArmoryMenu.Open && kb.escapeKey.wasPressedThisFrame) menu.Toggle();
            else if (kb != null && kb[MenuKey.Value].wasPressedThisFrame && !ArmoryMenu.Typing && (ArmoryMenu.Open || MonoSingleton<GunControl>.Instance)) menu.Toggle();
            if (kb != null && kb[ToggleKey.Value].wasPressedThisFrame && !ArmoryMenu.Open && MonoSingleton<GunControl>.Instance)
            {
                Enabled.Value = !Enabled.Value;
                MonoSingleton<GunControl>.Instance.GetComponent<GunSetter>().ResetWeapons();
            }
            Test.Tick();
        }

        static void DrawGui()
        {
            var s = scanning;
            if (s == null || s.total == 0) return;
            GUI.Label(new Rect(20, 20, 900, 30), "<b>Pixel Arsenal</b>: reading your Pixel Gun 3D guns… " + s.scanned + " / " + s.total + " files");
        }

        // while the armory is open the game's own pause and cheat menus stay shut (Esc closes the armory instead)
        [HarmonyPatch(typeof(OptionsManager), nameof(OptionsManager.Pause))]
        static class NoPauseInArmory { static bool Prefix() { return !ArmoryMenu.Open && Time.frameCount != ArmoryMenu.ClosedFrame; } }
        [HarmonyPatch(typeof(CheatsController), nameof(CheatsController.Update))]
        static class NoCheatsInArmory { static bool Prefix() { return !ArmoryMenu.Open; } }

        [HarmonyPatch(typeof(GunSetter), nameof(GunSetter.ResetWeapons))]
        static class FillSlots
        {
            static void Postfix(GunSetter __instance, bool firstTime)
            {
                if (!Enabled.Value) return;
                if (Arsenal.Catalog == null) { waitingSetter = __instance; return; }
                try { Arsenal.Fill(__instance, firstTime); }
                catch (Exception e) { Log.LogError("filling slots failed: " + e); Test.Report("slots-filled", false, e.Message); }
            }
        }
    }

    // Melty test mode: while %LOCALAPPDATA%\Melty\test\ultrakill-pixel-arsenal.json exists, go to the sandbox, fire every slot and report.
    static class Test
    {
        static string checks;
        static bool on, sceneAsked, slotsDone, modelDone, firedDone, hitsDone, armoryDone, dashDone;
        static int dashStep; static Vector3 dashFrom; static float dashShort, dashAt;
        static int hitStep, targetHits; static float hitAt; static EnemyIdentifier target; static GameObject targetRoot; static string hitLog = "";
        static float armoryAt;
        static float t0, lastSwitch;
        static int slot;
        static readonly System.Collections.Generic.HashSet<string> reported = new System.Collections.Generic.HashSet<string>();

        public static void Init()
        {
            string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty/test/ultrakill-pixel-arsenal.json");
            if (!File.Exists(f)) return;
            var m = Regex.Match(File.ReadAllText(f), "\"checks\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return;
            // Melty's two-copy multiplayer lab drives the game itself: this script would fight its autopilot
            var net = Environment.GetEnvironmentVariable("MELTY_NET_DIR");
            var props = Path.Combine(string.IsNullOrEmpty(net) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UltrakillPixelArsenal") : net, "melty.properties");
            if (File.Exists(props) && Regex.IsMatch(File.ReadAllText(props), "^(pilot|film)=1", RegexOptions.Multiline)) return;
            checks = Regex.Unescape(m.Groups[1].Value);
            on = true;
            Plugin.Log.LogInfo("Melty test mode, checks -> " + checks);
        }

        public static void Report(string name, bool ok, string why)
        {
            if (!on || !reported.Add(name)) return;
            try { File.AppendAllText(checks, (ok ? "pass " + name : "fail " + name + ": " + why) + "\n"); } catch { }
            Plugin.Log.LogInfo("test " + name + " " + (ok ? "pass" : "fail") + " " + why);
        }

        // a sandbox enemy held in front of V1: the gun in slot 1, then the melee slot, must land a hit on it
        [HarmonyPatch(typeof(EnemyIdentifier), nameof(EnemyIdentifier.DeliverDamage))]
        static class CountHits
        {
            static void Prefix(EnemyIdentifier __instance, GameObject sourceWeapon)
            {
                if (on && __instance == target && sourceWeapon && sourceWeapon.GetComponent<PgWeapon>()) targetHits++;
            }
        }

        // the same dash at half and full charge along the longest clear line: full must go about twice as far
        static void DashTest(GunControl gc)
        {
            var w = gc.currentWeapon ? gc.currentWeapon.GetComponent<PgWeapon>() : null;
            var nm = MonoSingleton<NewMovement>.Instance;
            if (!w || !nm) { dashDone = true; Report("dash-distance", false, "no weapon or player"); return; }
            if (dashAt == 0)
            {
                var dir = Enumerable.Range(0, 8).Select(i => Quaternion.Euler(0, i * 45f, 0) * Vector3.forward)
                    .OrderByDescending(d => { RaycastHit h; return nm.rb.SweepTest(d, out h, 200f) ? h.distance : 200f; }).First();
                dashFrom = nm.transform.position;
                w.StartDash(dashStep == 0 ? 0.5f : 1f, dir, 40f, 3.5f);
                dashAt = Time.realtimeSinceStartup;
                return;
            }
            if (w.Dashing && Time.realtimeSinceStartup - dashAt < 6f) return;
            var move = nm.transform.position - dashFrom; move.y = 0;
            dashAt = 0;
            Plugin.Log.LogInfo("dash " + dashStep + ": planned path " + w.DashMoved.ToString("0.0") + " m, player moved " + move.magnitude.ToString("0.0") + " m");
            if (dashStep++ == 0) { dashShort = w.DashMoved; return; }
            dashDone = true;
            bool ok = dashShort > 1f && w.DashMoved > dashShort * 1.5f && move.magnitude >= w.DashMoved * 0.8f;
            Report("dash-distance", ok, "half charge " + dashShort.ToString("0.0") + " m, full charge " + w.DashMoved.ToString("0.0") + " m, player moved " + move.magnitude.ToString("0.0") + " m; " + Arsenal.Catalog.guns.Count(g => g.Dashes) + " dash weapons");
        }

        static void HitTest(GunControl gc)
        {
            var slots = new[] { 1, 3 };
            var c = MonoSingleton<CameraController>.Instance.transform;
            if (hitAt == 0)
            {
                gc.SwitchWeapon(slots[hitStep], 0);
                var db = Resources.FindObjectsOfTypeAll<SpawnableObjectsDatabase>().FirstOrDefault();
                var e = db == null ? null : db.enemies.FirstOrDefault(o => o && o.gameObject && o.gameObject.GetComponentInChildren<EnemyIdentifier>(true));
                if (e == null) { hitsDone = true; Report("hits-enemy", false, "no sandbox enemy list loaded"); return; }
                targetRoot = UnityEngine.Object.Instantiate(e.gameObject, c.position + c.forward * 3f, Quaternion.identity);
                targetRoot.SetActive(true);
                target = targetRoot.GetComponentInChildren<EnemyIdentifier>(true);
                targetHits = 0;
                hitAt = Time.realtimeSinceStartup;
                Plugin.Log.LogInfo("hit test on " + e.objectName + ", active " + target.gameObject.activeInHierarchy + ", layer " + target.gameObject.layer);
                return;
            }
            var w = gc.currentWeapon ? gc.currentWeapon.GetComponent<PgWeapon>() : null;
            bool hurt = targetHits > 0 || !target || target.dead;
            if (hurt || Time.realtimeSinceStartup - hitAt > 10f)
            {
                hitLog += (w ? w.gun.name + " as " + w.fire.id : "?") + (hurt ? " hit; " : " missed; ");
                if (targetRoot) UnityEngine.Object.Destroy(targetRoot);
                if (!hurt) { hitsDone = true; Report("hits-enemy", false, hitLog); return; }
                hitAt = 0;
                if (++hitStep >= slots.Length) { hitsDone = true; Report("hits-enemy", true, hitLog); }
                return;
            }
            // aim level and hold the enemy with its body across the aim: its pivot is at its feet
            MonoSingleton<CameraController>.Instance.rotationX = 0;
            if (targetRoot) targetRoot.transform.position = c.position + c.forward * 3f + Vector3.down * 1.5f;
        }

        public static void Tick()
        {
            if (!on || Arsenal.Catalog == null) return;
            if (t0 == 0) t0 = Time.realtimeSinceStartup;
            var gc = MonoSingleton<GunControl>.Instance;
            if (!gc)
            {
                if (!sceneAsked && Time.realtimeSinceStartup - t0 > 3 && MonoSingleton<SceneHelper>.Instance) { sceneAsked = true; SceneHelper.LoadScene("uk_construct"); }
                return;
            }
            if (!slotsDone && gc.slots.Count >= 6 && gc.slots.All(s => s.Count > 0) && gc.slots.SelectMany(s => s).All(w => w && w.GetComponent<PgWeapon>()))
            {
                slotsDone = true;
                Report("slots-filled", true, string.Join(",", gc.slots.Select(s => s.Count.ToString()).ToArray()));
            }
            if (!slotsDone) return;
            if (!modelDone && gc.slots.SelectMany(s => s).Count(w => w && w.GetComponent<PgWeapon>().modelReady && w.GetComponentInChildren<MeshRenderer>(true)) >= 30)
            { modelDone = true; Report("model-built", true, "30+ guns have Pixel Gun models"); }
            // cycle the six slots and fire each, with no one playing
            if (Time.realtimeSinceStartup - lastSwitch > 1.5f && slot < 6)
            {
                lastSwitch = Time.realtimeSinceStartup;
                gc.SwitchWeapon(slot + 1, 0);
                slot++;
            }
            var w0 = gc.currentWeapon ? gc.currentWeapon.GetComponent<PgWeapon>() : null;
            if (w0 && !ArmoryMenu.Open && Time.frameCount % 20 == 0) w0.SendMessage("TestFire", SendMessageOptions.DontRequireReceiver);
            if (!firedDone && slot >= 6 && PgWeapon.ShotsFired >= 6) { firedDone = true; Report("gun-fired", true, PgWeapon.ShotsFired + " shots from 6 slots"); }
            if (firedDone && !hitsDone) { HitTest(gc); return; }
            if (hitsDone && !dashDone) { DashTest(gc); return; }
            // then open the armory and show each category's guns for a few seconds
            if (dashDone && !armoryDone)
            {
                var m = Plugin.menu;
                if (armoryAt == 0) { armoryAt = Time.realtimeSinceStartup; ArmoryMenu.TilesDrawn = 0; if (!ArmoryMenu.Open) m.Toggle(); }
                float t = Time.realtimeSinceStartup - armoryAt;
                m.tab = Mathf.Min(5, (int)(t / 4f));
                if (t > 24f)
                {
                    armoryDone = true;
                    int shown = ArmoryMenu.TilesDrawn;
                    if (ArmoryMenu.Open) m.Toggle();
                    Report("armory-opens", shown > 0, shown + " gun tiles drawn across 6 tabs");
                }
            }
        }
    }
}
