// Pixel Gun 3D-style armory: category tabs, the six loadout spots of the category, and every gun of that category
// from the player's PG3D with its real armory icon and stats. Click a spot, then a gun.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PixelGunArsenal
{
    public class ArmoryMenu : MonoBehaviour
    {
        public static bool Open;
        public static int TilesDrawn; // test mode: tiles drawn while open
        public static int ClosedFrame = -1;
        public static bool Typing { get { return Open && GUIUtility.keyboardControl != 0; } }
        public int tab, spot;
        Vector2 scroll;
        string search = "";
        float prevTimeScale = 1;
        static readonly Dictionary<int, Texture2D> icons = new Dictionary<int, Texture2D>();
        static readonly HashSet<int> requested = new HashSet<int>();
        static readonly Color[] RarityColors = Sheets.Rarities.Select(r => { Color c; ColorUtility.TryParseHtmlString(r.color, out c); return c; }).ToArray();
        static int Rar(PgGun g) { return Mathf.Clamp(g.rarity, 0, Sheets.Rarities.Length - 1); }
        Texture2D white;

        void Awake()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Open) Toggle(); };
        }

        // a friend joined while the armory held the game still
        public void KeepRunning() { if (Open && NetBridge.Others > 0 && Time.timeScale == 0) Time.timeScale = prevTimeScale; }

        public void Toggle()
        {
            if (Arsenal.Catalog == null) return;
            if (!Open && MonoSingleton<OptionsManager>.Instance && MonoSingleton<OptionsManager>.Instance.paused) return;
            Open = !Open;
            var gsm = GameStateManager.Instance;
            if (Open)
            {
                prevTimeScale = Time.timeScale;
                if (NetBridge.Others == 0) Time.timeScale = 0; // with friends in the room the fight goes on
                gsm.RegisterState(new GameState("pga-armory", gameObject) { cursorLock = LockMode.Unlock, playerInputLock = LockMode.Lock, cameraInputLock = LockMode.Lock });
            }
            else
            {
                ClosedFrame = Time.frameCount;
                GUIUtility.keyboardControl = 0;
                Time.timeScale = prevTimeScale;
                if (gsm) gsm.PopState("pga-armory");
                var gs = MonoSingleton<GunControl>.Instance ? MonoSingleton<GunControl>.Instance.GetComponent<GunSetter>() : null;
                if (gs) Arsenal.Fill(gs, false); else Arsenal.SaveLoadout();
            }
        }

        Texture2D Icon(PgGun g)
        {
            Texture2D t;
            if (icons.TryGetValue(g.weaponId, out t)) return t;
            if (requested.Add(g.weaponId))
            {
                var r = Arsenal.Catalog.IconRef(g);
                int id = g.weaponId;
                if (r != null) Arsenal.QueueIcon(r, td => { icons[id] = td != null ? PgModel.Texture(td, 256) : null; });
            }
            return null;
        }

        void OnGUI()
        {
            if (!Open) return;
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            GUI.depth = -100;
            float W = Screen.width, H = Screen.height, u = Mathf.Max(1f, H / 1080f);
            GUI.color = new Color(0, 0, 0, 0.85f); GUI.DrawTexture(new Rect(0, 0, W, H), white); GUI.color = Color.white;
            var big = new GUIStyle(GUI.skin.label) { font = PgWeapon.UkFont, fontSize = (int)(34 * u), alignment = TextAnchor.MiddleLeft };
            var small = new GUIStyle(GUI.skin.label) { font = PgWeapon.UkFont, fontSize = (int)(15 * u), alignment = TextAnchor.UpperCenter, wordWrap = true };
            var rarityStyle = new GUIStyle(small) { fontSize = (int)(12 * u), alignment = TextAnchor.UpperLeft, wordWrap = false };
            var btn = new GUIStyle(GUI.skin.button) { font = PgWeapon.UkFont, fontSize = (int)(20 * u) };
            GUI.Label(new Rect(30 * u, 15 * u, W, 50 * u), "ARMORY  <size=" + (int)(18 * u) + "><color=#aaa>Pixel Gun 3D guns from your own copy · " + Arsenal.Catalog.guns.Count + " found · " + Plugin.MenuKey.Value + " to close</color></size>", big);

            // category tabs
            var cats = Sheets.Categories;
            float tw = (W - 60 * u) / cats.Length;
            for (int i = 0; i < cats.Length; i++)
            {
                Color c; ColorUtility.TryParseHtmlString(cats[i].color, out c);
                GUI.backgroundColor = i == tab ? c : c * 0.5f;
                if (GUI.Button(new Rect(30 * u + i * tw, 70 * u, tw - 6 * u, 46 * u), cats[i].ukSlot + "  " + cats[i].name.ToUpper(), btn)) { tab = i; spot = 0; scroll = Vector2.zero; }
            }
            GUI.backgroundColor = Color.white;
            var cat = cats[tab];
            var lo = Arsenal.SlotGuns(cat);

            // loadout spots
            float sw = 130 * u;
            GUI.Label(new Rect(30 * u, 125 * u, 600 * u, 30 * u), "Slot " + cat.ukSlot + " loadout: press " + cat.ukSlot + " again in game to cycle. Pick a spot, then a gun:", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontSize = (int)(17 * u) });
            for (int i = 0; i < cat.loadoutSize; i++)
            {
                var r = new Rect(30 * u + i * (sw + 8 * u), 158 * u, sw, sw);
                var spotGun = i < lo.Count ? Arsenal.Catalog.guns.First(x => x.weaponId == lo[i]) : null;
                GUI.color = i == spot ? Color.white : spotGun != null ? RarityColors[Rar(spotGun)] : new Color(1, 1, 1, 0.25f); GUI.DrawTexture(r, white); GUI.color = Color.white;
                var inner = new Rect(r.x + 3 * u, r.y + 3 * u, r.width - 6 * u, r.height - 6 * u);
                GUI.color = new Color(.12f, .12f, .14f); GUI.DrawTexture(inner, white); GUI.color = Color.white;
                if (i < lo.Count)
                {
                    var g = spotGun;
                    var ic = Icon(g);
                    if (ic) GUI.DrawTexture(new Rect(inner.x, inner.y, inner.width, inner.height - 34 * u), ic, ScaleMode.ScaleToFit);
                    GUI.Label(new Rect(inner.x, inner.yMax - 36 * u, inner.width, 36 * u), g.name, small);
                }
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) spot = i;
            }

            // gun grid
            GUI.Label(new Rect(W - 430 * u, 125 * u, 80 * u, 30 * u), "Search", new GUIStyle(small) { alignment = TextAnchor.MiddleRight, fontSize = (int)(17 * u) });
            search = GUI.TextField(new Rect(W - 340 * u, 125 * u, 310 * u, 30 * u), search, new GUIStyle(GUI.skin.textField) { fontSize = (int)(17 * u) });
            var list = Arsenal.Catalog.guns.Where(g => g.category == cat.id && (search.Length == 0 || g.name.ToLowerInvariant().Contains(search.ToLowerInvariant())))
                .OrderByDescending(g => g.rarity).ThenBy(g => g.name).ToList();
            float top = 158 * u + sw + 20 * u, tile = 150 * u, gap = 8 * u;
            int cols = Mathf.Max(1, (int)((W - 60 * u) / (tile + gap)));
            int rows = (list.Count + cols - 1) / cols;
            var view = new Rect(30 * u, top, W - 60 * u, H - top - 20 * u);
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0, 0, view.width - 20 * u, rows * (tile + gap)));
            int firstRow = (int)(scroll.y / (tile + gap)), lastRow = firstRow + (int)(view.height / (tile + gap)) + 1;
            PgGun hover = null;
            for (int i = firstRow * cols; i < Mathf.Min(list.Count, (lastRow + 1) * cols); i++)
            {
                var g = list[i];
                var r = new Rect((i % cols) * (tile + gap), (i / cols) * (tile + gap), tile, tile);
                GUI.color = RarityColors[Rar(g)]; GUI.DrawTexture(r, white); GUI.color = Color.white;
                var inner = new Rect(r.x + 3 * u, r.y + 3 * u, r.width - 6 * u, r.height - 6 * u);
                GUI.color = lo.Contains(g.weaponId) ? new Color(.3f, .3f, .34f) : new Color(.1f, .1f, .12f); GUI.DrawTexture(inner, white); GUI.color = Color.white;
                var ic = Icon(g);
                if (ic) GUI.DrawTexture(new Rect(inner.x + 4 * u, inner.y + 4 * u, inner.width - 8 * u, inner.height - 44 * u), ic, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(inner.x, inner.yMax - 40 * u, inner.width, 40 * u), g.name, small);
                rarityStyle.normal.textColor = RarityColors[Rar(g)];
                GUI.Label(new Rect(inner.x + 2 * u, inner.y, inner.width, 18 * u), Sheets.Rarities[Rar(g)].name.ToUpperInvariant(), rarityStyle);
                if (Event.current.type == EventType.Repaint) TilesDrawn++;
                if (r.Contains(Event.current.mousePosition)) hover = g;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    int existing = lo.IndexOf(g.weaponId);
                    if (existing >= 0 && existing != spot) lo[existing] = spot < lo.Count ? lo[spot] : lo[existing];
                    if (spot < lo.Count) lo[spot] = g.weaponId; else lo.Add(g.weaponId);
                    spot = (spot + 1) % cat.loadoutSize;
                }
            }
            GUI.EndScrollView();

            if (hover != null)
            {
                var ft = PgWeapon.PickFire(hover, cat);
                string info = hover.name + "\n" + Sheets.Rarities[Rar(hover)].name + " " + cat.name + "\nDamage " + (hover.damageRaw * cat.damageScale).ToString("0.##") +
                              "  ·  " + hover.ShotsPerSecond.ToString("0.#") + " shots/s\nClip " + (hover.clip > 0 && !hover.unlimited ? hover.clip.ToString() : "∞") + "  ·  fires as ULTRAKILL " + ft.id + (hover.zoomFov > 0 ? "  ·  scope" : "") + (hover.DashCharges ? "  ·  charge to dash" : hover.Dashes ? "  ·  dash" : "");
                var m = Event.current.mousePosition;
                var box = new Rect(Mathf.Min(m.x + 18, W - 360 * u), Mathf.Min(m.y + 18, H - 120 * u), 340 * u, 110 * u);
                GUI.color = new Color(0, 0, 0, 0.92f); GUI.DrawTexture(box, white); GUI.color = Color.white;
                GUI.Label(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12), info, new GUIStyle(small) { alignment = TextAnchor.UpperLeft, fontSize = (int)(16 * u) });
            }
        }
    }
}
