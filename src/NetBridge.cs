// Co-op beside Melty Net's ULTRAKILL add-on (tools/build_net.py): friends' puppets hold and fire the PG3D gun their player holds.
// Reached by name, so this plugin never needs the add-on's DLL to load.
using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PixelGunArsenal
{
    public static class NetBridge
    {
        static Type bridge;
        static Action<string> fired;
        static System.Reflection.FieldInfo others;

        public static int Others { get { return others != null ? (int)others.GetValue(null) : 0; } }
        public static void Fired(string what) { if (fired != null) fired(what); }

        // from Plugin's Tick until the add-on has loaded (a plugin's load order isn't ours to set)
        public static void Connect()
        {
            if (bridge != null) { if (fired == null) fired = (Action<string>)bridge.GetField("Fired").GetValue(null); return; }
            bridge = AccessTools.TypeByName("PixelArsenalBridge");
            if (bridge == null) return;
            others = bridge.GetField("Others");
            bridge.GetField("ShowHeld").SetValue(null, (Func<GameObject, string, bool>)PuppetGun.Show);
            bridge.GetField("HasGun").SetValue(null, (Func<GameObject, bool>)PuppetGun.Has);
            bridge.GetField("Shot").SetValue(null, (Action<GameObject>)PuppetGun.Shot);
        }
    }

    // The gun in a friend's hands: follows the puppet's right hand, pointing where the puppet faces.
    public class PuppetGun : MonoBehaviour
    {
        const float Length = 1.6f; // V1 stands 3.5 m tall; the size of V2's third-person guns
        string held = "";
        bool found;
        Transform hand, holder;
        AudioClip shoot; // kept so the sender's shot sound, sent by clip name, finds it here

        public static bool Has(GameObject puppet)
        {
            var p = puppet.GetComponent<PuppetGun>();
            return p && p.found;
        }

        public static bool Show(GameObject puppet, string name)
        {
            var p = puppet.GetComponent<PuppetGun>() ?? puppet.AddComponent<PuppetGun>();
            if (p.held != name) p.Hold(name);
            return p.found;
        }

        public static void Shot(GameObject puppet)
        {
            var p = puppet.GetComponent<PuppetGun>();
            if (p && p.holder) PgWeapon.Tracer(p.holder.position, p.holder, 0.04f);
        }

        void Hold(string name)
        {
            held = name;
            if (holder) Destroy(holder.gameObject);
            var g = !name.StartsWith("PG3D ") || Arsenal.Catalog == null ? null : Arsenal.Catalog.guns.FirstOrDefault(x => "PG3D " + x.name == name);
            found = g != null; // a gun this player's PG3D doesn't have: V2's stand-in gun stays in the hand
            if (!found) return;
            holder = new GameObject("PG3D Puppet Gun").transform;
            holder.SetParent(transform, false);
            var want = holder;
            Arsenal.QueueGun(g, d => { if (want) Build(d, want, g); });
        }

        void Build(GunData d, Transform into, PgGun g)
        {
            GameObject flash; Vector3 muzzle;
            var m = PgModel.Build(d, into, into, Plugin.TexMax, 1f, out flash, out muzzle);
            shoot = PgModel.Clip(d.shoot, g.name + " shoot");
            if (!m) return;
            if (flash) Destroy(flash);
            var rs = m.GetComponentsInChildren<Renderer>(true);
            foreach (var t in m.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            m.transform.localScale *= Length / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.y, b.size.z));
            m.transform.localPosition = Vector3.zero;
        }

        void LateUpdate()
        {
            if (!holder) return;
            if (!hand)
            {
                hand = GetComponentsInChildren<Transform>(true).FirstOrDefault(b => { var n = b.name.ToLowerInvariant(); return n.Contains("hand") && (n.Contains(".r") || n.Contains("_r") || n.Contains("right")); }) ?? transform;
            }
            holder.SetPositionAndRotation(hand.position, transform.rotation);
        }
    }
}
