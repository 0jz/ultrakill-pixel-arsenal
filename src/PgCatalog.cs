// Finds every gun in the player's own Pixel Gun 3D: the install's bundles plus the download cache in LocalLow.
// Builds one PgGun row per weapon prefab (columns: sheets/weapon_columns.json) and caches the result as TSV.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PixelGunArsenal
{
    public class PgGun
    {
        public int weaponId, category, rarity, clip, burst;
        public float shotsPerSecondRate, damageRaw, zoomFov, chargeTime, dashImpulse, dashDecay;
        public string name, tag, bundle, nameKey;
        public bool isMelee, railgun, flamethrower, grenadeLauncher, bazooka, isShotGun, unlimited, isDash, isCharging;
        public bool Dashes { get { return isDash || (isMelee && isCharging && dashImpulse != 100f); } }
        public bool DashCharges { get { return Dashes && isCharging && chargeTime > 0; } }
        public float DashUnits { get { return dashDecay > 0 ? dashImpulse / dashDecay : 0; } }
        public float ShotsPerSecond { get { return Math.Max(0.5f, Math.Min(12f, 0.4f * (float)Math.Pow(2, (shotsPerSecondRate - 50f) / 12f))); } }
        public bool Flag(string f)
        {
            switch (f)
            {
                case "isMelee": return isMelee; case "railgun": return railgun; case "flamethrower": return flamethrower;
                case "grenadeLauncher": return grenadeLauncher; case "bazooka": return bazooka; case "isShotGun": return isShotGun;
                default: return false;
            }
        }
    }

    public class PgCatalog
    {
        public List<PgGun> guns = new List<PgGun>();
        public Dictionary<string, string> cabs = new Dictionary<string, string>();
        public Dictionary<string, string> icons = new Dictionary<string, string>(); // lower tag -> "bundle|pathId"
        public volatile int scanned, total;
        public string status = "";

        static readonly Regex WeaponPrefab = new Regex(@"addressablecontent/weapons/models/weapons/weapon(\d+)\.prefab$");
        static readonly Regex Icon = new Regex(@"addressablecontent/weapons/resident/offericons/(.+)_icon1_big\.png$");
        const int CacheVersion = 5;

        public static List<string> BundleFiles(string pg3dDir, string cacheDir)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(cacheDir) && Directory.Exists(cacheDir))
                list.AddRange(Directory.GetFiles(cacheDir, "__data", SearchOption.AllDirectories));
            string aa = Path.Combine(pg3dDir ?? "", "Pixel Gun 3D_Data/StreamingAssets/aa/StandaloneWindows64");
            if (Directory.Exists(aa)) list.AddRange(Directory.GetFiles(aa, "*.bundle"));
            return list;
        }

        public static string DefaultCacheDir(string pg3dDir)
        {
            string company = "Pixel Gun Team", product = "Pixel Gun 3D";
            try
            {
                var info = File.ReadAllLines(Path.Combine(pg3dDir, "Pixel Gun 3D_Data/app.info"));
                if (info.Length >= 2) { company = info[0].Trim(); product = info[1].Trim(); }
            }
            catch { }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "AppData/LocalLow/" + company + "/" + product + "/AddressablesCache");
        }

        static string Stamp(List<string> files)
        {
            long newest = 0;
            foreach (var f in files) newest = Math.Max(newest, File.GetLastWriteTimeUtc(f).Ticks);
            return CacheVersion + ":" + files.Count + ":" + newest;
        }

        public static PgCatalog LoadOrScan(string pg3dDir, string cacheDir, string cacheFile, Action<PgCatalog> progress = null)
        {
            var files = BundleFiles(pg3dDir, cacheDir);
            string stamp = Stamp(files);
            var cat = new PgCatalog();
            if (File.Exists(cacheFile) && cat.Load(cacheFile, stamp)) { cat.status = "ready"; return cat; }
            cat.Scan(files, progress);
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
            cat.Save(cacheFile, stamp);
            cat.status = "ready";
            return cat;
        }

        void Scan(List<string> files, Action<PgCatalog> progress)
        {
            total = files.Count;
            var english = new string[1];
            var gunsBag = new List<PgGun>();
            var lockObj = new object();
            int next = -1;
            int threads = Math.Max(1, Math.Min(6, Environment.ProcessorCount - 2));
            var workers = new List<Thread>();
            for (int t = 0; t < threads; t++)
            {
                var th = new Thread(() =>
                {
                    int i;
                    while ((i = Interlocked.Increment(ref next)) < files.Count)
                    {
                        try { ScanOne(files[i], lockObj, gunsBag, english); }
                        catch (Exception) { }
                        Interlocked.Increment(ref scanned);
                    }
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                th.Start(); workers.Add(th);
            }
            foreach (var w in workers) while (!w.Join(250)) if (progress != null) progress(this);

            // name the guns from the English localization; one gun per id (the cache copy wins, it is listed first)
            var terms = new Dictionary<string, string>();
            if (english[0] != null) ReadTerms(english[0], terms, gunsBag.Select(g => g.nameKey).Where(k => !string.IsNullOrEmpty(k)));
            var seen = new HashSet<int>();
            foreach (var g in gunsBag.OrderBy(g => files.IndexOf(g.bundle)))
            {
                if (!seen.Add(g.weaponId)) continue;
                string n;
                g.name = !string.IsNullOrEmpty(g.nameKey) && terms.TryGetValue(g.nameKey, out n) ? n : null;
                if (g.name == null || Regex.IsMatch(g.name, "^(PRIMARY|BACKUP|MELEE|SPECIAL|SNIPER|HEAVY)$")) // a few new guns point at the tab label
                    g.name = string.IsNullOrEmpty(g.tag) ? "Weapon" + g.weaponId : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(g.tag.Replace('_', ' '));
                guns.Add(g);
            }
            guns.Sort((a, b) => a.weaponId.CompareTo(b.weaponId));
        }

        void ScanOne(string path, object lockObj, List<PgGun> gunsBag, string[] english)
        {
            var am = new AssetsManager();
            try
            {
                var b = am.LoadBundleFile(path, true);
                var names = b.file.GetAllFileNames();
                lock (lockObj) foreach (var n in names) if (n.StartsWith("CAB-", StringComparison.OrdinalIgnoreCase) && n.IndexOf('.') < 0) cabs[n.ToLowerInvariant()] = path;
                int ai = -1;
                for (int i = 0; i < names.Count; i++) if (b.file.IsAssetsFile(i)) { ai = i; break; }
                if (ai < 0) return;
                var f = am.LoadAssetsFileFromBundle(b, ai, false);
                var abInfo = f.file.GetAssetsOfType(AssetClassID.AssetBundle).FirstOrDefault();
                if (abInfo == null) return;
                var ab = am.GetBaseField(f, abInfo);
                int weaponId = -1;
                foreach (var c in ab["m_Container"]["Array"].Children)
                {
                    string key = c["first"].AsString.ToLowerInvariant();
                    var m = WeaponPrefab.Match(key);
                    if (m.Success) weaponId = int.Parse(m.Groups[1].Value);
                    var im = Icon.Match(key);
                    if (im.Success) lock (lockObj) icons[im.Groups[1].Value] = path + "|" + c["second"]["asset"]["m_PathID"].AsLong;
                    if (key.EndsWith("localization/language_english.prefab")) english[0] = path;
                }
                if (weaponId < 0) return;
                foreach (var info in f.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                {
                    var w = am.GetBaseField(f, info);
                    if (w["categoryNabor"].IsDummy) continue;
                    var dmg = w["damageByTier"]["Array"].Children;
                    var g = new PgGun
                    {
                        weaponId = weaponId, bundle = path,
                        category = w["categoryNabor"].AsInt, rarity = w["rarity"].AsInt, clip = w["ammoInClip"].AsInt,
                        burst = Math.Max(1, Math.Min(5, w["countInSeriaBazooka"].AsInt)),
                        shotsPerSecondRate = w["fireRateShop"].AsInt,
                        damageRaw = dmg.Count > 0 ? dmg[dmg.Count - 1].AsFloat : 1f,
                        zoomFov = w["isZooming"].AsBool ? w["fieldOfViewZomm"].AsFloat : 0f,
                        nameKey = w["localizeWeaponKey"].AsString,
                        tag = w["Tag"].AsString.Length > 0 ? w["Tag"].AsString : w["ShopId"].AsString,
                        isMelee = w["isMelee"].AsBool, railgun = w["railgun"].AsBool, flamethrower = w["flamethrower"].AsBool,
                        grenadeLauncher = w["grenadeLauncher"].AsBool, bazooka = w["bazooka"].AsBool, isShotGun = w["isShotGun"].AsBool,
                        unlimited = w["isUnlimitedAmmo"].AsBool,
                        isDash = w["isDash"].AsBool, isCharging = w["isCharging"].AsBool, chargeTime = w["chargeTime"].AsFloat,
                        dashImpulse = w["dashMaxImpulse"].AsFloat, dashDecay = w["dashDecaySpeed"].AsFloat,
                    };
                    lock (lockObj) gunsBag.Add(g);
                    break;
                }
            }
            finally { am.UnloadAll(true); }
        }

        static void ReadTerms(string path, Dictionary<string, string> terms, IEnumerable<string> wanted)
        {
            var want = new HashSet<string>(wanted);
            var am = new AssetsManager();
            try
            {
                var b = am.LoadBundleFile(path, true);
                var f = am.LoadAssetsFileFromBundle(b, 0, false);
                foreach (var info in f.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                {
                    var mb = am.GetBaseField(f, info);
                    if (mb["mTerms"].IsDummy) continue;
                    foreach (var t in mb["mTerms"]["Array"].Children)
                    {
                        string k = t["term"].AsString;
                        if (want.Contains(k)) terms[k] = t["translation"].AsString;
                    }
                }
            }
            finally { am.UnloadAll(true); }
        }

        // ---- TSV cache ----
        static string Esc(string s) { return (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '); }
        static string F(float v) { return v.ToString(CultureInfo.InvariantCulture); }

        void Save(string file, string stamp)
        {
            var sb = new StringBuilder();
            sb.Append("stamp\t").Append(stamp).Append('\n');
            foreach (var g in guns)
                sb.Append(string.Join("\t", new[] { "gun", g.weaponId.ToString(), g.category.ToString(), g.rarity.ToString(), g.clip.ToString(), g.burst.ToString(),
                    F(g.shotsPerSecondRate), F(g.damageRaw), F(g.zoomFov), Esc(g.name), Esc(g.tag), g.bundle,
                    g.isMelee ? "1" : "0", g.railgun ? "1" : "0", g.flamethrower ? "1" : "0", g.grenadeLauncher ? "1" : "0", g.bazooka ? "1" : "0", g.isShotGun ? "1" : "0", g.unlimited ? "1" : "0",
                    g.isDash ? "1" : "0", g.isCharging ? "1" : "0", F(g.chargeTime), F(g.dashImpulse), F(g.dashDecay) })).Append('\n');
            foreach (var kv in cabs) sb.Append("cab\t").Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
            foreach (var kv in icons) sb.Append("icon\t").Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
            File.WriteAllText(file, sb.ToString());
        }

        bool Load(string file, string stamp)
        {
            var lines = File.ReadAllLines(file);
            if (lines.Length == 0 || lines[0] != "stamp\t" + stamp) return false;
            foreach (var l in lines.Skip(1))
            {
                var p = l.Split('\t');
                if (p[0] == "cab" && p.Length == 3) cabs[p[1]] = p[2];
                else if (p[0] == "icon" && p.Length == 3) icons[p[1]] = p[2];
                else if (p[0] == "gun" && p.Length == 24)
                    guns.Add(new PgGun
                    {
                        weaponId = int.Parse(p[1]), category = int.Parse(p[2]), rarity = int.Parse(p[3]), clip = int.Parse(p[4]), burst = int.Parse(p[5]),
                        shotsPerSecondRate = float.Parse(p[6], CultureInfo.InvariantCulture), damageRaw = float.Parse(p[7], CultureInfo.InvariantCulture),
                        zoomFov = float.Parse(p[8], CultureInfo.InvariantCulture), name = p[9], tag = p[10], bundle = p[11],
                        isMelee = p[12] == "1", railgun = p[13] == "1", flamethrower = p[14] == "1", grenadeLauncher = p[15] == "1",
                        bazooka = p[16] == "1", isShotGun = p[17] == "1", unlimited = p[18] == "1",
                        isDash = p[19] == "1", isCharging = p[20] == "1", chargeTime = float.Parse(p[21], CultureInfo.InvariantCulture),
                        dashImpulse = float.Parse(p[22], CultureInfo.InvariantCulture), dashDecay = float.Parse(p[23], CultureInfo.InvariantCulture),
                    });
            }
            return guns.Count > 0;
        }

        public string IconRef(PgGun g)
        {
            string r;
            return g.tag != null && icons.TryGetValue(g.tag.ToLowerInvariant(), out r) ? r : null;
        }
    }
}
