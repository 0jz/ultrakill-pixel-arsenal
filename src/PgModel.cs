// Turns GunData (read from the player's PG3D) into ULTRAKILL objects: meshes on ULTRAKILL's own weapon material,
// textures cut down to ULTRAKILL's chunky point-filtered look, and decoded sounds.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PixelGunArsenal
{
    public static class PgModel
    {
        public static Material BaseMat;
        public static int Layer;
        static bool calibrated;

        // Reads how ULTRAKILL draws its revolver (shader, HUD layer) so PG3D guns get the same look.
        public static void Calibrate(GunSetter gs)
        {
            if (calibrated) return;
            var prefab = gs.revolverPierce.ToAssets().FirstOrDefault(p => p != null);
            if (prefab == null) return;
            // an inactive copy placed exactly as ULTRAKILL places its revolver (under GunSetter), so nothing of it runs
            var holder = new GameObject("pga-calibrate");
            holder.SetActive(false);
            holder.transform.SetParent(gs.transform, false);
            var rev = Object.Instantiate(prefab, holder.transform);
            try
            {
                Renderer main = null; float best = 0;
                foreach (var r in rev.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.sharedMaterial == null) continue;
                    string sh = r.sharedMaterial.shader.name;
                    if (sh.Contains("Sprite") || sh.Contains("UI") || sh.Contains("Particle")) continue;
                    var mesh = r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh : r.GetComponent<MeshFilter>() ? r.GetComponent<MeshFilter>().sharedMesh : null;
                    if (mesh == null) continue;
                    var s = Vector3.Scale(mesh.bounds.size, r.transform.lossyScale);
                    float vol = s.x * s.y * s.z;
                    if (vol > best) { best = vol; main = r; }
                }
                if (main == null) return;
                BaseMat = main.sharedMaterial;
                Layer = main.gameObject.layer;
                calibrated = true;
            }
            finally { Object.Destroy(holder); }
            if (BaseMat) Plugin.Log.LogInfo("Gun look from ULTRAKILL's revolver: shader " + BaseMat.shader.name + ", layer " + Layer);
        }

        // Where PG3D holds a gun in view: lower right of the camera, in metres of camera space.
        public static Vector3 ViewPos = new Vector3(0.24f, -0.25f, 0.72f);
        public static float ViewLength = 0.52f;

        // Builds the gun under `root` (a GunSetter child) posed in camera space like PG3D's viewmodel.
        // Returns the model and its muzzle-flash object (hidden until a shot); muzzle = flash/barrel point in root space.
        public static GameObject Build(GunData d, Transform root, Transform cam, int texMax, float scale, out GameObject flash, out Vector3 muzzle)
        {
            flash = null;
            var gunParts = d.parts.Where(p => !p.flash).ToList();
            var all = new List<Vector3>();
            foreach (var p in gunParts) foreach (var t in p.tris) all.Add(new Vector3(p.pos[t * 3], p.pos[t * 3 + 1], p.pos[t * 3 + 2]));
            if (all.Count == 0) { muzzle = root.InverseTransformPoint(cam.TransformPoint(ViewPos + Vector3.forward * 0.3f)); return null; }
            var bounds = new Bounds(all[0], Vector3.zero);
            foreach (var v in all) bounds.Encapsulate(v);
            float sWorld = ViewLength * scale / Mathf.Max(0.01f, Mathf.Max(bounds.size.z, bounds.size.x * 0.8f, bounds.size.y * 0.8f));

            var model = new GameObject("PG3D Model");
            model.layer = Layer;
            model.transform.SetParent(root, false);
            model.transform.rotation = cam.rotation;
            model.transform.localScale = Vector3.one * sWorld / Mathf.Max(1e-4f, root.lossyScale.x);
            model.transform.position = cam.TransformPoint(ViewPos);
            var geo = new GameObject("Geometry").transform; // centred, so recoil and swings turn the gun around itself
            geo.gameObject.layer = Layer;
            geo.SetParent(model.transform, false);
            geo.localPosition = -bounds.center;
            foreach (var g in gunParts.GroupBy(p => p.tex != null ? p.tex.name + "|" + p.tex.data.Length : "flat|" + string.Join(",", p.color.Select(c => c.ToString("F2")).ToArray())))
                Part(g.ToList(), geo, Mat(g.First().tex, g.First().color, texMax, false));

            var flashParts = d.parts.Where(p => p.flash).ToList();
            if (flashParts.Count > 0)
            {
                var fo = new GameObject("PG3D Flash");
                fo.layer = Layer;
                fo.transform.SetParent(geo, false);
                foreach (var g in flashParts.GroupBy(p => p.tex != null ? p.tex.name : "flat"))
                    Part(g.ToList(), fo.transform, Mat(g.First().tex, g.First().color, texMax, true));
                fo.SetActive(false);
                flash = fo;
            }
            muzzle = root.InverseTransformPoint(geo.TransformPoint(new Vector3(bounds.center.x, bounds.center.y, bounds.max.z)));
            return model;
        }

        static void Part(List<PartData> parts, Transform parent, Material mat)
        {
            var verts = new List<Vector3>(); var nrms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            foreach (var p in parts)
            {
                var map = new Dictionary<int, int>();
                foreach (var t in p.tris)
                {
                    int ni;
                    if (!map.TryGetValue(t, out ni))
                    {
                        ni = verts.Count; map[t] = ni;
                        verts.Add(new Vector3(p.pos[t * 3], p.pos[t * 3 + 1], p.pos[t * 3 + 2]));
                        nrms.Add(new Vector3(p.nrm[t * 3], p.nrm[t * 3 + 1], p.nrm[t * 3 + 2]).normalized);
                        uvs.Add(new Vector2(p.uv[t * 2], p.uv[t * 2 + 1]));
                    }
                    tris.Add(ni);
                }
            }
            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetNormals(nrms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("Part");
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Material Mat(TexData t, float[] color, int texMax, bool glow)
        {
            string key = (glow ? "glow|" : "") + (t != null ? t.name + "|" + t.data.Length : "flat");
            Material m;
            if (t != null && mats.TryGetValue(key, out m)) return m;
            var sprite = Shader.Find("Sprites/Default");
            m = glow && sprite ? new Material(sprite) : BaseMat != null ? new Material(BaseMat) : new Material(Shader.Find("Standard"));
            foreach (var n in m.GetTexturePropertyNames())
                if (n != "_MainTex" && (n.Contains("Emiss") || n.Contains("Glow") || n.Contains("Mask") || n.Contains("Detail"))) m.SetTexture(n, null);
            var tex = t != null ? Texture(t, texMax) : null;
            if (tex == null) { tex = new Texture2D(1, 1); tex.SetPixel(0, 0, new Color(color[0], color[1], color[2], 1)); tex.Apply(); }
            m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.color = Color.white;
            if (t != null) mats[key] = m;
            return m;
        }

        static int Bpp(int f) { switch (f) { case 1: case 63: return 1; case 7: case 13: case 62: return 2; case 3: return 3; case 4: case 5: case 14: return 4; default: return 0; } }
        static int Block(int f) { switch (f) { case 10: case 26: return 8; case 12: case 24: case 25: case 27: return 16; default: return 0; } }
        static int Size(int f, int w, int h) { int b = Block(f); return b > 0 ? Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4) * b : w * h * Bpp(f); }

        // ULTRAKILL downgrade: the largest stored mip at or below texMax, point filtered; uncompressed colours cut to 5 bits.
        public static Texture2D Texture(TexData t, int texMax)
        {
            try
            {
                int f = t.format;
                if (Bpp(f) == 0 && Block(f) == 0) return null; // crunched or exotic: caller uses a flat colour
                int k = 0, off = 0;
                while (k < t.mipCount - 1 && Mathf.Max(t.width >> k, t.height >> k) > texMax && Mathf.Min(t.width >> (k + 1), t.height >> (k + 1)) >= (Block(f) > 0 ? 4 : 1))
                { off += Size(f, Mathf.Max(1, t.width >> k), Mathf.Max(1, t.height >> k)); k++; }
                int w = Mathf.Max(1, t.width >> k), h = Mathf.Max(1, t.height >> k), size = Size(f, w, h);
                if (off + size > t.data.Length) { k = 0; off = 0; w = t.width; h = t.height; size = Size(f, w, h); }
                if (size > t.data.Length) return null;
                var raw = new byte[size];
                System.Buffer.BlockCopy(t.data, off, raw, 0, size);
                var tex = new Texture2D(w, h, (TextureFormat)f, false);
                tex.LoadRawTextureData(raw);
                if (Block(f) == 0)
                {
                    var px = tex.GetPixels32();
                    int step = Mathf.Max(1, Mathf.Max(w, h) / texMax);
                    if (step > 1)
                    {
                        int nw = Mathf.Max(1, w / step), nh = Mathf.Max(1, h / step);
                        var o = new Color32[nw * nh];
                        for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++) o[y * nw + x] = px[(y * step) * w + x * step];
                        px = o; w = nw; h = nh;
                    }
                    for (int i = 0; i < px.Length; i++) { var c = px[i]; px[i] = new Color32((byte)(c.r & 0xF8), (byte)(c.g & 0xF8), (byte)(c.b & 0xF8), c.a); }
                    tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    tex.SetPixels32(px);
                }
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.name = t.name;
                tex.Apply(false, true);
                return tex;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("texture " + t.name + ": " + e.Message); return null; }
        }

        public static AudioClip Clip(AudioData a, string name)
        {
            if (a == null || a.samples == null || a.samples.Length < a.channels) return null;
            var c = AudioClip.Create(name, a.samples.Length / a.channels, a.channels, a.frequency, false);
            c.SetData(a.samples, 0);
            return c;
        }
    }
}
