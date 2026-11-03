// Reads Pixel Gun 3D asset bundles (Unity 6) straight from the player's own install/cache with AssetsTools.NET.
// ULTRAKILL runs Unity 2022 and cannot load these bundles itself, so the meshes, textures and sounds are read here
// as plain data. No UnityEngine here, so tools/harness can run it outside the game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace PixelGunArsenal
{
    public class TexData { public int width, height, format, mipCount; public byte[] data; public string name; }
    public class AudioData { public float[] samples; public int channels, frequency; }
    public class PartData
    {
        public string name;
        public float[] pos, nrm, uv; // xyz, xyz, uv per vertex, in weapon-root space
        public int[] tris;
        public TexData tex;
        public float[] color = { 1, 1, 1, 1 };
        public bool flash; // PG3D muzzle flash mesh, shown only when firing
    }
    public class GunData { public List<PartData> parts = new List<PartData>(); public AudioData shoot, reload; public string log = ""; }

    // Row-major 4x4 matrix, enough for transform composition.
    public struct M4
    {
        public float[] m;
        public static M4 Identity { get { var r = new M4 { m = new float[16] }; r.m[0] = r.m[5] = r.m[10] = r.m[15] = 1; return r; } }
        public static M4 operator *(M4 a, M4 b)
        {
            var r = new M4 { m = new float[16] };
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++)
                { float s = 0; for (int k = 0; k < 4; k++) s += a.m[i * 4 + k] * b.m[k * 4 + j]; r.m[i * 4 + j] = s; }
            return r;
        }
        public static M4 TRS(float[] t, float[] q, float[] s)
        {
            float x = q[0], y = q[1], z = q[2], w = q[3];
            var r = new M4 { m = new float[16] };
            r.m[0] = (1 - 2 * (y * y + z * z)) * s[0]; r.m[1] = (2 * (x * y - z * w)) * s[1]; r.m[2] = (2 * (x * z + y * w)) * s[2]; r.m[3] = t[0];
            r.m[4] = (2 * (x * y + z * w)) * s[0]; r.m[5] = (1 - 2 * (x * x + z * z)) * s[1]; r.m[6] = (2 * (y * z - x * w)) * s[2]; r.m[7] = t[1];
            r.m[8] = (2 * (x * z - y * w)) * s[0]; r.m[9] = (2 * (y * z + x * w)) * s[1]; r.m[10] = (1 - 2 * (x * x + y * y)) * s[2]; r.m[11] = t[2];
            r.m[15] = 1;
            return r;
        }
        public void Point(float x, float y, float z, float w, float[] o, int i)
        {
            o[i] += w * (m[0] * x + m[1] * y + m[2] * z + m[3]);
            o[i + 1] += w * (m[4] * x + m[5] * y + m[6] * z + m[7]);
            o[i + 2] += w * (m[8] * x + m[9] * y + m[10] * z + m[11]);
        }
        public void Dir(float x, float y, float z, float w, float[] o, int i)
        {
            o[i] += w * (m[0] * x + m[1] * y + m[2] * z);
            o[i + 1] += w * (m[4] * x + m[5] * y + m[6] * z);
            o[i + 2] += w * (m[8] * x + m[9] * y + m[10] * z);
        }
    }

    public class PgReader : IDisposable
    {
        readonly AssetsManager am = new AssetsManager();
        readonly Dictionary<string, string> cabToBundle;           // lower cab name -> bundle path
        readonly Dictionary<string, AssetsFileInstance> files = new Dictionary<string, AssetsFileInstance>();
        readonly Dictionary<string, BundleFileInstance> bundles = new Dictionary<string, BundleFileInstance>();
        static readonly Regex Cab = new Regex(@"(cab-[0-9a-f]+)", RegexOptions.IgnoreCase);

        public PgReader(Dictionary<string, string> cabIndex) { cabToBundle = cabIndex; am.UseTemplateFieldCache = true; }
        public void Dispose() { am.UnloadAll(true); files.Clear(); bundles.Clear(); }

        BundleFileInstance Bundle(string path)
        {
            BundleFileInstance b;
            if (!bundles.TryGetValue(path, out b)) { b = am.LoadBundleFile(path, true); bundles[path] = b; }
            return b;
        }

        public AssetsFileInstance Open(string bundlePath, string cab = null)
        {
            var b = Bundle(bundlePath);
            string key = (cab ?? b.file.GetFileName(0)).ToLowerInvariant();
            AssetsFileInstance f;
            if (files.TryGetValue(key, out f)) return f;
            int idx = 0;
            var names = b.file.GetAllFileNames();
            for (int i = 0; i < names.Count; i++)
                if (names[i].ToLowerInvariant() == key || (cab == null && b.file.IsAssetsFile(i))) { idx = i; break; }
            f = am.LoadAssetsFileFromBundle(b, idx, false);
            files[names[idx].ToLowerInvariant()] = f;
            return f;
        }

        AssetsFileInstance FileFor(AssetsFileInstance from, int fileId)
        {
            if (fileId == 0) return from;
            var ext = from.file.Metadata.Externals;
            if (fileId - 1 >= ext.Count) return null;
            var m = Cab.Match(ext[fileId - 1].PathName);
            if (!m.Success) return null;
            string cab = m.Groups[1].Value.ToLowerInvariant();
            AssetsFileInstance f;
            if (files.TryGetValue(cab, out f)) return f;
            string bp;
            if (!cabToBundle.TryGetValue(cab, out bp)) return null;
            return Open(bp, cab);
        }

        public AssetTypeValueField Field(AssetsFileInstance f, long pathId)
        {
            var info = f.file.GetAssetInfo(pathId);
            return info == null ? null : am.GetBaseField(f, info);
        }

        // Follows a PPtr; returns the target's file and field.
        public AssetTypeValueField Deref(AssetsFileInstance from, AssetTypeValueField pptr, out AssetsFileInstance file)
        {
            file = null;
            if (pptr == null || pptr.IsDummy) return null;
            long pid = pptr["m_PathID"].AsLong;
            if (pid == 0) return null;
            file = FileFor(from, pptr["m_FileID"].AsInt);
            return file == null ? null : Field(file, pid);
        }

        // Bytes stored beside an asset (.resS / .resource) in its bundle.
        byte[] Resource(AssetsFileInstance near, string path, long offset, long size)
        {
            var m = Cab.Match(path ?? "");
            if (!m.Success || size <= 0) return null;
            string cab = m.Groups[1].Value.ToLowerInvariant();
            string bp;
            if (!cabToBundle.TryGetValue(cab, out bp)) return null;
            var b = Bundle(bp);
            string fname = Path.GetFileName(path);
            int idx = -1;
            var names = b.file.GetAllFileNames();
            for (int i = 0; i < names.Count; i++) if (string.Equals(names[i], fname, StringComparison.OrdinalIgnoreCase)) idx = i;
            if (idx < 0) return null;
            long start, len;
            b.file.GetFileRange(idx, out start, out len);
            var r = b.file.DataReader;
            r.Position = start + offset;
            return r.ReadBytes((int)size);
        }

        static byte[] Bytes(AssetTypeValueField f)
        {
            if (f == null || f.IsDummy) return new byte[0];
            if (f.Value != null && f.Value.ValueType == AssetValueType.ByteArray) return f.AsByteArray;
            var arr = f.Children.Count == 1 && f.Children[0].FieldName == "Array" ? f.Children[0] : f;
            if (arr.Value != null && arr.Value.ValueType == AssetValueType.ByteArray) return arr.AsByteArray;
            var o = new byte[arr.Children.Count];
            for (int i = 0; i < o.Length; i++) o[i] = arr.Children[i].AsByte;
            return o;
        }
        static List<AssetTypeValueField> Arr(AssetTypeValueField f)
        {
            if (f == null || f.IsDummy) return new List<AssetTypeValueField>();
            var a = f["Array"];
            return a.IsDummy ? f.Children : a.Children;
        }
        static float[] V3(AssetTypeValueField f) { return new[] { f["x"].AsFloat, f["y"].AsFloat, f["z"].AsFloat }; }
        static float[] Q(AssetTypeValueField f) { return new[] { f["x"].AsFloat, f["y"].AsFloat, f["z"].AsFloat, f["w"].AsFloat }; }

        // ---------------- weapon prefab -> GunData ----------------
        static readonly Regex SkipName = new Regex(@"^(arms?(_mesh)?|fps_player.*|.*[_ ]arms?|hands?)$|(^|[_ ])(flash|gunflash|smoke|muzzle|trail|vfx|shell|sleeve)([_ \d]|$)", RegexOptions.IgnoreCase);

        public GunData LoadGun(string bundlePath, int weaponId, int texMax)
        {
            var gun = new GunData();
            var f = Open(bundlePath);
            // all GameObjects/Transforms of the prefab, by path id
            var goName = new Dictionary<long, string>();
            var goActive = new Dictionary<long, bool>();
            var tfLocal = new Dictionary<long, M4>();
            var tfFather = new Dictionary<long, long>();
            var tfGo = new Dictionary<long, long>();
            var goTf = new Dictionary<long, long>();
            long rootTf = 0;
            AssetTypeValueField weaponPars = null;
            foreach (var info in f.file.AssetInfos)
            {
                if (info.TypeId == (int)AssetClassID.GameObject)
                {
                    var g = am.GetBaseField(f, info);
                    goName[info.PathId] = g["m_Name"].AsString;
                    goActive[info.PathId] = g["m_IsActive"].AsBool;
                }
                else if (info.TypeId == (int)AssetClassID.Transform || info.TypeId == (int)AssetClassID.RectTransform)
                {
                    var t = am.GetBaseField(f, info);
                    tfLocal[info.PathId] = M4.TRS(V3(t["m_LocalPosition"]), Q(t["m_LocalRotation"]), V3(t["m_LocalScale"]));
                    tfFather[info.PathId] = t["m_Father"]["m_PathID"].AsLong;
                    long go = t["m_GameObject"]["m_PathID"].AsLong;
                    tfGo[info.PathId] = go; goTf[go] = info.PathId;
                }
                else if (info.TypeId == (int)AssetClassID.MonoBehaviour && weaponPars == null)
                {
                    var mb = am.GetBaseField(f, info);
                    if (!mb["shoot"].IsDummy && !mb["reload"].IsDummy && !mb["renderArms"].IsDummy) weaponPars = mb;
                }
            }
            foreach (var kv in tfFather) if (kv.Value == 0) { rootTf = kv.Key; if (goName[tfGo[kv.Key]] == "Weapon" + weaponId) break; }

            var world = new Dictionary<long, M4>();
            Func<long, M4> W = null;
            W = id =>
            {
                M4 m;
                if (world.TryGetValue(id, out m)) return m;
                long fa;
                m = (tfFather.TryGetValue(id, out fa) && fa != 0 && tfLocal.ContainsKey(fa)) ? W(fa) * tfLocal[id] : M4.Identity; // root = identity
                world[id] = m;
                return m;
            };
            Func<long, bool> activeInHierarchy = tf =>
            {
                for (long cur = tf; cur != 0 && tfGo.ContainsKey(cur); cur = tfFather.ContainsKey(cur) ? tfFather[cur] : 0)
                    if (!goActive[tfGo[cur]]) return false;
                return true;
            };

            foreach (var info in f.file.AssetInfos)
            {
                bool skinned = info.TypeId == (int)AssetClassID.SkinnedMeshRenderer;
                if (!skinned && info.TypeId != (int)AssetClassID.MeshRenderer) continue;
                var r = am.GetBaseField(f, info);
                long go = r["m_GameObject"]["m_PathID"].AsLong;
                string name = goName.ContainsKey(go) ? goName[go] : "";
                bool flash = name.ToLowerInvariant().Contains("flash");
                if (!goTf.ContainsKey(go) || (!flash && (!r["m_Enabled"].AsBool || SkipName.IsMatch(name) || !activeInHierarchy(goTf[go])))) { gun.log += "skip " + name + "; "; continue; }

                AssetsFileInstance mf; AssetTypeValueField mesh;
                if (skinned) mesh = Deref(f, r["m_Mesh"], out mf);
                else
                {
                    mesh = null; mf = null;
                    foreach (var c in Arr(Field(f, go)["m_Component"]))
                    {
                        var cp = c["component"];
                        var ci = f.file.GetAssetInfo(cp["m_PathID"].AsLong);
                        if (cp["m_FileID"].AsInt == 0 && ci != null && ci.TypeId == (int)AssetClassID.MeshFilter)
                            mesh = Deref(f, am.GetBaseField(f, ci)["m_Mesh"], out mf);
                    }
                }
                if (mesh == null) continue;

                var mats = new List<AssetTypeValueField>(); var matFiles = new List<AssetsFileInstance>();
                foreach (var mp in Arr(r["m_Materials"]))
                {
                    AssetsFileInstance matf; var mat = Deref(f, mp, out matf);
                    mats.Add(mat); matFiles.Add(matf);
                }

                M4[] boneMats = null;
                if (skinned)
                {
                    var bones = Arr(r["m_Bones"]);
                    var bind = Arr(mesh["m_BindPose"]);
                    boneMats = new M4[bones.Count];
                    for (int i = 0; i < bones.Count; i++)
                    {
                        long b = bones[i]["m_PathID"].AsLong;
                        var bp = new M4 { m = new float[16] };
                        if (i < bind.Count) for (int k = 0; k < 16; k++) bp.m[k] = bind[i]["e" + (k / 4) + (k % 4)].AsFloat;
                        else bp = M4.Identity;
                        boneMats[i] = (b != 0 && tfLocal.ContainsKey(b) ? W(b) : W(goTf[go])) * bp;
                    }
                }
                try { int before = gun.parts.Count; ReadMesh(mf, mesh, skinned ? null : (M4?)W(goTf[go]), boneMats, mats, matFiles, texMax, name, gun); for (int pi = before; pi < gun.parts.Count; pi++) gun.parts[pi].flash = flash; }
                catch (Exception e) { gun.log += "mesh " + name + " failed: " + e.Message + "; "; }
            }

            if (weaponPars != null)
            {
                gun.shoot = Audio(f, weaponPars["shoot"]);
                gun.reload = Audio(f, weaponPars["reload"]);
            }
            return gun;
        }

        static int FmtSize(int fmt) { switch (fmt) { case 0: case 10: case 11: return 4; case 1: case 4: case 5: case 8: case 9: return 2; default: return 1; } }
        static float ReadComp(byte[] d, int o, int fmt)
        {
            switch (fmt)
            {
                case 0: return BitConverter.ToSingle(d, o);
                case 1: return Half(BitConverter.ToUInt16(d, o));
                case 2: return d[o] / 255f;
                case 3: return Math.Max((sbyte)d[o] / 127f, -1f);
                case 4: return BitConverter.ToUInt16(d, o) / 65535f;
                case 5: return Math.Max(BitConverter.ToInt16(d, o) / 32767f, -1f);
                case 6: return d[o];
                case 7: return (sbyte)d[o];
                case 8: return BitConverter.ToUInt16(d, o);
                case 9: return BitConverter.ToInt16(d, o);
                case 10: return BitConverter.ToUInt32(d, o);
                default: return BitConverter.ToInt32(d, o);
            }
        }
        static float Half(ushort h)
        {
            int s = (h >> 15) & 1, e = (h >> 10) & 31, m = h & 1023;
            float v = e == 0 ? m / 1024f * (float)Math.Pow(2, -14) : e == 31 ? float.PositiveInfinity : (1 + m / 1024f) * (float)Math.Pow(2, e - 15);
            return s == 1 ? -v : v;
        }

        void ReadMesh(AssetsFileInstance mf, AssetTypeValueField mesh, M4? rigid, M4[] bones, List<AssetTypeValueField> mats, List<AssetsFileInstance> matFiles, int texMax, string name, GunData gun)
        {
            if (!mesh["m_MeshCompression"].IsDummy && mesh["m_MeshCompression"].AsInt != 0) { gun.log += name + " compressed mesh; "; return; }
            var vd = mesh["m_VertexData"];
            int vc = (int)vd["m_VertexCount"].AsUInt;
            byte[] data = Bytes(vd["m_DataSize"]);
            var sd = mesh["m_StreamData"];
            if (data.Length == 0 && !sd.IsDummy && sd["size"].AsLong > 0)
                data = Resource(mf, sd["path"].AsString, sd["offset"].AsLong, sd["size"].AsLong) ?? new byte[0];
            if (vc == 0 || data.Length == 0) return;

            var ch = Arr(vd["m_Channels"]);
            int[] cStream = new int[ch.Count], cOff = new int[ch.Count], cFmt = new int[ch.Count], cDim = new int[ch.Count];
            var stride = new int[8];
            for (int i = 0; i < ch.Count; i++)
            {
                cStream[i] = ch[i]["stream"].AsByte; cOff[i] = ch[i]["offset"].AsByte; cFmt[i] = ch[i]["format"].AsByte; cDim[i] = ch[i]["dimension"].AsByte & 0xF;
                if (cDim[i] > 0) stride[cStream[i]] = Math.Max(stride[cStream[i]], cOff[i] + cDim[i] * FmtSize(cFmt[i]));
            }
            var sOff = new int[8]; int acc = 0;
            for (int s = 0; s < 8; s++) { sOff[s] = acc; acc += stride[s] * vc; acc = (acc + 15) & ~15; }
            Func<int, int, int, float> get = (c, v, k) => ReadComp(data, sOff[cStream[c]] + v * stride[cStream[c]] + cOff[c] + k * FmtSize(cFmt[c]), cFmt[c]);
            Func<int, bool> has = c => c < ch.Count && cDim[c] > 0;

            var pos = new float[vc * 3]; var nrm = new float[vc * 3]; var uv = new float[vc * 2];
            for (int v = 0; v < vc; v++)
            {
                float x = get(0, v, 0), y = get(0, v, 1), z = get(0, v, 2);
                float nx = 0, ny = 1, nz = 0;
                if (has(1)) { nx = get(1, v, 0); ny = get(1, v, 1); nz = get(1, v, 2); }
                if (has(4)) { uv[v * 2] = get(4, v, 0); uv[v * 2 + 1] = get(4, v, 1); }
                if (bones != null && has(13))
                {
                    int dim = cDim[13];
                    float wsum = 0;
                    for (int k = 0; k < dim; k++)
                    {
                        float w = has(12) && k < cDim[12] ? get(12, v, k) : (k == dim - 1 ? 1 - wsum : 0);
                        if (!has(12) && dim == 1) w = 1;
                        wsum += w;
                        int bi = (int)get(13, v, k);
                        if (w <= 0 || bi < 0 || bi >= bones.Length) continue;
                        bones[bi].Point(x, y, z, w, pos, v * 3);
                        bones[bi].Dir(nx, ny, nz, w, nrm, v * 3);
                    }
                }
                else
                {
                    var m = rigid ?? (bones != null && bones.Length > 0 ? bones[0] : M4.Identity);
                    m.Point(x, y, z, 1, pos, v * 3); m.Dir(nx, ny, nz, 1, nrm, v * 3);
                }
            }

            byte[] ib = Bytes(mesh["m_IndexBuffer"]);
            bool i32 = !mesh["m_IndexFormat"].IsDummy && mesh["m_IndexFormat"].AsInt == 1;
            var subs = Arr(mesh["m_SubMeshes"]);
            for (int s = 0; s < subs.Count; s++)
            {
                var sm = subs[s];
                if (sm["topology"].AsInt != 0) continue;
                int first = (int)sm["firstByte"].AsUInt, count = (int)sm["indexCount"].AsUInt, baseV = sm["baseVertex"].AsInt;
                var tris = new int[count];
                for (int i = 0; i < count; i++)
                    tris[i] = baseV + (i32 ? (int)BitConverter.ToUInt32(ib, first + i * 4) : BitConverter.ToUInt16(ib, first + i * 2));
                int mi = Math.Min(s, mats.Count - 1);
                if (mi >= 0 && mats[mi] != null && mats[mi]["m_Name"].AsString.ToLowerInvariant() == "player") { gun.log += "skip arms " + name + "#" + s + "; "; continue; } // PG3D's arm skin
                var part = new PartData { name = name + "#" + s, pos = pos, nrm = nrm, uv = uv, tris = tris };
                if (mi >= 0 && mats[mi] != null) Material(matFiles[mi], mats[mi], part);
                gun.parts.Add(part);
            }
        }

        void Material(AssetsFileInstance mf, AssetTypeValueField mat, PartData part)
        {
            var props = mat["m_SavedProperties"];
            foreach (var c in Arr(props["m_Colors"]))
                if (c["first"].AsString == "_Color" || c["first"].AsString == "_BaseColor")
                { var v = c["second"]; part.color = new[] { v["r"].AsFloat, v["g"].AsFloat, v["b"].AsFloat, v["a"].AsFloat }; }
            AssetTypeValueField best = null;
            foreach (var t in Arr(props["m_TexEnvs"]))
            {
                string n = t["first"].AsString;
                var p = t["second"]["m_Texture"];
                if (p["m_PathID"].AsLong == 0) continue;
                if (n == "_MainTex" || n == "_BaseMap") { best = p; break; }
                if (best == null && !n.ToLowerInvariant().Contains("bump") && !n.ToLowerInvariant().Contains("normal") && !n.ToLowerInvariant().Contains("mask")) best = p;
            }
            if (best == null) return;
            AssetsFileInstance tf; var tex = Deref(mf, best, out tf);
            if (tex != null) part.tex = Texture(tf, tex);
        }

        public TexData Texture(AssetsFileInstance f, AssetTypeValueField t)
        {
            if (t["m_TextureFormat"].IsDummy) return null; // not a Texture2D (e.g. cubemap)
            var td = new TexData
            {
                name = t["m_Name"].AsString, width = t["m_Width"].AsInt, height = t["m_Height"].AsInt,
                format = t["m_TextureFormat"].AsInt, mipCount = Math.Max(1, t["m_MipCount"].AsInt)
            };
            td.data = Bytes(t["image data"]);
            var sd = t["m_StreamData"];
            if (td.data.Length == 0 && !sd.IsDummy)
                td.data = Resource(f, sd["path"].AsString, sd["offset"].AsLong, (long)sd["size"].AsUInt) ?? new byte[0];
            return td.data.Length == 0 ? null : td;
        }

        public TexData TextureAt(string bundlePath, long pathId)
        {
            var f = Open(bundlePath);
            var t = Field(f, pathId);
            return t == null ? null : Texture(f, t);
        }

        AudioData Audio(AssetsFileInstance from, AssetTypeValueField pptr)
        {
            try
            {
                AssetsFileInstance af; var clip = Deref(from, pptr, out af);
                if (clip == null) return null;
                var res = clip["m_Resource"];
                var bytes = Resource(af, res["m_Source"].AsString, (long)res["m_Offset"].AsULong, (long)res["m_Size"].AsULong);
                return bytes == null ? null : Fsb5.Decode(bytes);
            }
            catch { return null; }
        }
    }

    // FMOD FSB5 as Unity stores AudioClips: PCM16 and IMA ADPCM are decoded; Vorbis returns null (the caller falls back).
    public static class Fsb5
    {
        static readonly int[] Freq = { 4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };
        static readonly int[] Steps = {
            7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
        static readonly int[] IndexTab = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

        public static AudioData Decode(byte[] b)
        {
            if (b.Length < 64 || b[0] != 'F' || b[1] != 'S' || b[2] != 'B' || b[3] != '5') return null;
            int version = BitConverter.ToInt32(b, 4);
            int sampleHeaders = BitConverter.ToInt32(b, 12), nameTable = BitConverter.ToInt32(b, 16), dataSize = BitConverter.ToInt32(b, 20), mode = BitConverter.ToInt32(b, 24);
            int headerSize = version == 0 ? 64 : 60;
            ulong h = BitConverter.ToUInt64(b, headerSize);
            int p = headerSize + 8;
            int freq = Freq[Math.Min((int)((h >> 1) & 0xF), Freq.Length - 1)];
            int chans = new[] { 1, 2, 6, 8 }[(int)((h >> 5) & 3)];
            int dataOff = (int)((h >> 7) & 0x7FFFFFF) * 32;
            int numSamples = (int)(h >> 34);
            bool more = (h & 1) != 0;
            while (more)
            {
                uint c = BitConverter.ToUInt32(b, p); p += 4;
                more = (c & 1) != 0; int size = (int)((c >> 1) & 0xFFFFFF); int type = (int)(c >> 25);
                if (type == 1) chans = b[p];
                else if (type == 2) freq = BitConverter.ToInt32(b, p);
                p += size;
            }
            int start = headerSize + sampleHeaders + nameTable + dataOff;
            int len = Math.Min(dataSize - dataOff, b.Length - start);
            float[] o;
            if (mode == 2) // PCM16
            {
                o = new float[len / 2];
                for (int i = 0; i < o.Length; i++) o[i] = BitConverter.ToInt16(b, start + i * 2) / 32768f;
            }
            else if (mode == 7) o = ImaAdpcm(b, start, len, chans, numSamples);
            else return null;
            return new AudioData { samples = o, channels = chans, frequency = freq };
        }

        // FMOD/Xbox IMA ADPCM: per channel 36-byte blocks (4-byte header + 32 bytes = 65 samples), channels interleaved in 4-byte words.
        static float[] ImaAdpcm(byte[] b, int start, int len, int chans, int numSamples)
        {
            int block = 36 * chans, blocks = len / block;
            var o = new float[Math.Min(numSamples, blocks * 65) * chans];
            int frames = o.Length / chans;
            for (int bl = 0; bl < blocks; bl++)
            {
                int bs = start + bl * block;
                for (int c = 0; c < chans; c++)
                {
                    int pred = BitConverter.ToInt16(b, bs + c * 4), idx = Math.Min(Math.Max((int)b[bs + c * 4 + 2], 0), 88);
                    int frame = bl * 65;
                    if (frame < frames) o[frame * chans + c] = pred / 32768f;
                    for (int word = 0; word < 8; word++)
                    {
                        int wp = bs + chans * 4 + (word * chans + c) * 4;
                        for (int nb = 0; nb < 8; nb++)
                        {
                            int nib = (b[wp + nb / 2] >> ((nb & 1) * 4)) & 0xF;
                            int step = Steps[idx], diff = step >> 3;
                            if ((nib & 1) != 0) diff += step >> 2;
                            if ((nib & 2) != 0) diff += step >> 1;
                            if ((nib & 4) != 0) diff += step;
                            if ((nib & 8) != 0) diff = -diff;
                            pred = Math.Max(-32768, Math.Min(32767, pred + diff));
                            idx = Math.Max(0, Math.Min(88, idx + IndexTab[nib]));
                            int fi = frame + 1 + word * 8 + nb;
                            if (fi < frames) o[fi * chans + c] = pred / 32768f;
                        }
                    }
                }
            }
            return o;
        }
    }
}
