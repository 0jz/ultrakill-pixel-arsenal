// The loadout (6 PG3D guns per category) and putting it into ULTRAKILL's six weapon slots (sheets/hooks.json fillSlots).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace PixelGunArsenal
{
    public static class Arsenal
    {
        public static PgCatalog Catalog;
        public static Dictionary<int, List<int>> Loadout = new Dictionary<int, List<int>>(); // category id -> weapon ids
        static string loadoutFile;

        // ---- loadout ----
        public static void LoadLoadout(string file)
        {
            loadoutFile = file;
            if (File.Exists(file))
                foreach (var l in File.ReadAllLines(file))
                {
                    var p = l.Split(':');
                    int c;
                    if (p.Length == 2 && int.TryParse(p[0], out c))
                        Loadout[c] = p[1].Split(',').Select(s => { int v; return int.TryParse(s, out v) ? v : -1; }).Where(v => v >= 0).ToList();
                }
        }

        public static void SaveLoadout()
        {
            File.WriteAllLines(loadoutFile, Loadout.Select(kv => kv.Key + ":" + string.Join(",", kv.Value.Select(v => v.ToString()).ToArray())).ToArray());
        }

        // Fills missing spots with the category's rarest, newest guns; drops guns the player's PG3D no longer has.
        public static List<int> SlotGuns(Category c)
        {
            List<int> ids;
            if (!Loadout.TryGetValue(c.id, out ids)) Loadout[c.id] = ids = new List<int>();
            var have = new HashSet<int>(Catalog.guns.Where(g => g.category == c.id).Select(g => g.weaponId));
            ids.RemoveAll(i => !have.Contains(i));
            foreach (var g in Catalog.guns.Where(g => g.category == c.id && !ids.Contains(g.weaponId)).OrderByDescending(g => g.rarity).ThenByDescending(g => g.weaponId))
            {
                if (ids.Count >= c.loadoutSize) break;
                ids.Add(g.weaponId);
            }
            if (ids.Count > c.loadoutSize) ids.RemoveRange(c.loadoutSize, ids.Count - c.loadoutSize);
            return ids;
        }

        // ---- filling ULTRAKILL's slots ----
        public static void Fill(GunSetter gs, bool firstTime)
        {
            if (Catalog == null || !Plugin.ArsenalOn) return;
            PgModel.Calibrate(gs);
            PgWeapon.Calibrate(gs);
            var gc = gs.GetComponent<GunControl>();
            // the slot lists themselves: on the first call GunControl.Start hasn't gathered them into gc.slots yet
            var slots = new[] { gc.slot1, gc.slot2, gc.slot3, gc.slot4, gc.slot5, gc.slot6 };
            foreach (var slot in slots) { foreach (var go in slot) if (go) Object.Destroy(go); slot.Clear(); }
            gc.currentWeapon = null;
            foreach (var c in Sheets.Categories)
            {
                var slot = slots[c.ukSlot - 1];
                foreach (var id in SlotGuns(c))
                {
                    var g = Catalog.guns.First(x => x.weaponId == id);
                    var go = new GameObject("PG3D " + g.name);
                    go.SetActive(false);
                    go.transform.SetParent(gs.transform, false);
                    var w = go.AddComponent<PgWeapon>();
                    w.Init(g, c);
                    slot.Add(go);
                    Queue(w);
                }
            }
            gc.UpdateWeaponList(firstTime);
            if (!firstTime && !gc.noWeapons && gc.slots.Count == 6)
            {
                int s = Mathf.Clamp(gc.currentSlotIndex, 1, 6);
                if (slots[s - 1].Count == 0) s = 1;
                gc.SwitchWeapon(s, Mathf.Clamp(gc.currentVariationIndex, 0, slots[s - 1].Count - 1));
            }
            SaveLoadout();
        }

        // ---- background model loading: one reader thread, Unity objects made on the main thread ----
        static readonly Queue<PgWeapon> todo = new Queue<PgWeapon>();
        static readonly Queue<KeyValuePair<PgWeapon, GunData>> done = new Queue<KeyValuePair<PgWeapon, GunData>>();
        static readonly Queue<KeyValuePair<string, System.Action<TexData>>> iconTodo = new Queue<KeyValuePair<string, System.Action<TexData>>>();
        static readonly Queue<System.Action> mainThread = new Queue<System.Action>();
        static Thread worker;
        static readonly object gate = new object();

        // a friend's gun, for their puppet (NetBridge.cs)
        static readonly Queue<KeyValuePair<PgGun, System.Action<GunData>>> gunTodo = new Queue<KeyValuePair<PgGun, System.Action<GunData>>>();
        public static void QueueGun(PgGun g, System.Action<GunData> onMain) { lock (gate) gunTodo.Enqueue(new KeyValuePair<PgGun, System.Action<GunData>>(g, onMain)); Wake(); }

        static void Queue(PgWeapon w) { lock (gate) todo.Enqueue(w); Wake(); }
        public static void QueueIcon(string iconRef, System.Action<TexData> onMain) { lock (gate) iconTodo.Enqueue(new KeyValuePair<string, System.Action<TexData>>(iconRef, onMain)); Wake(); }

        static void Wake()
        {
            lock (gate) if (worker != null && worker.IsAlive) { Monitor.PulseAll(gate); return; }
            worker = new Thread(Work) { IsBackground = true, Name = "PG3D reader" };
            worker.Start();
        }

        static void Work()
        {
            var reader = new PgReader(Catalog.cabs);
            int sinceReset = 0;
            while (true)
            {
                PgWeapon w = null; KeyValuePair<string, System.Action<TexData>> icon = default(KeyValuePair<string, System.Action<TexData>>);
                KeyValuePair<PgGun, System.Action<GunData>> friend = default(KeyValuePair<PgGun, System.Action<GunData>>);
                lock (gate)
                {
                    if (todo.Count == 0 && iconTodo.Count == 0 && gunTodo.Count == 0)
                    {
                        reader.Dispose(); reader = new PgReader(Catalog.cabs); sinceReset = 0; // free unpacked bundles while idle
                        Monitor.Wait(gate);
                        continue;
                    }
                    if (todo.Count > 0) w = todo.Dequeue(); else if (gunTodo.Count > 0) friend = gunTodo.Dequeue(); else icon = iconTodo.Dequeue();
                }
                if (++sinceReset > 60) { reader.Dispose(); reader = new PgReader(Catalog.cabs); sinceReset = 0; }
                if (friend.Key != null)
                {
                    GunData fd;
                    try { fd = reader.LoadGun(friend.Key.bundle, friend.Key.weaponId, Plugin.TexMax); }
                    catch (System.Exception e) { fd = new GunData { log = e.Message }; }
                    var cb = friend.Value;
                    lock (gate) mainThread.Enqueue(() => cb(fd));
                }
                else if (w != null)
                {
                    GunData d;
                    try { d = reader.LoadGun(w.gun.bundle, w.gun.weaponId, Plugin.TexMax); }
                    catch (System.Exception e) { d = new GunData { log = e.Message }; }
                    lock (gate) done.Enqueue(new KeyValuePair<PgWeapon, GunData>(w, d));
                }
                else
                {
                    TexData t = null;
                    try { var p = icon.Key.Split('|'); t = reader.TextureAt(p[0], long.Parse(p[1])); } catch { }
                    var icb = icon.Value; var tt = t;
                    lock (gate) mainThread.Enqueue(() => icb(tt));
                }
            }
        }

        // Called from Plugin.Update: builds at most a few guns per frame.
        public static void Pump()
        {
            for (int i = 0; i < 3; i++)
            {
                KeyValuePair<PgWeapon, GunData> kv;
                lock (gate) { if (done.Count == 0) break; kv = done.Dequeue(); }
                if (kv.Key == null) continue; // destroyed meanwhile
                try { kv.Key.ApplyModel(kv.Value, Plugin.TexMax, Plugin.GunScale); }
                catch (System.Exception e) { Plugin.Log.LogWarning(kv.Key.gun.name + ": " + e); }
                if (kv.Value.parts.Count == 0) Plugin.Log.LogInfo(kv.Key.gun.name + " has no drawable model: " + kv.Value.log);
            }
            for (int i = 0; i < 12; i++)
            {
                System.Action a;
                lock (gate) { if (mainThread.Count == 0) break; a = mainThread.Dequeue(); }
                try { a(); } catch (System.Exception e) { Plugin.Log.LogWarning(e.Message); }
            }
        }
    }
}
