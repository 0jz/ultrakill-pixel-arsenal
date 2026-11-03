// Runs the PG3D reader outside the game: scans the catalog, then dumps a few guns as OBJ + WAV for inspection.
using System; using System.IO; using System.Linq; using System.Diagnostics; using System.Globalization; using System.Text;
using PixelGunArsenal;
static class Harness {
  static void Main(string[] a) {
    string pg = a[0], outDir = a[1]; Directory.CreateDirectory(outDir);
    var sw = Stopwatch.StartNew();
    var cat = PgCatalog.LoadOrScan(pg, PgCatalog.DefaultCacheDir(pg), Path.Combine(outDir, "catalog.tsv"), c => Console.Write("\r" + c.scanned + "/" + c.total));
    Console.WriteLine("\ncatalog " + cat.guns.Count + " guns, " + cat.cabs.Count + " cabs, " + cat.icons.Count + " icons in " + sw.Elapsed.TotalSeconds.ToString("F1") + "s");
    foreach (var g in cat.guns.GroupBy(g => g.category).OrderBy(x => x.Key)) Console.WriteLine(" cat " + g.Key + ": " + g.Count());
    Console.WriteLine(" named: " + cat.guns.Count(g => !g.name.StartsWith("Weapon")) + ", icons found: " + cat.guns.Count(g => cat.IconRef(g) != null));
    var ids = a.Length > 2 && a[2] == "all" ? cat.guns.Select(x => x.weaponId).ToArray() : a.Skip(2).Select(int.Parse).ToArray();
    foreach (var id in ids) {
      var g = cat.guns.First(x => x.weaponId == id);
      sw.Restart();
      using (var r = new PgReader(cat.cabs)) {
        var gd = r.LoadGun(g.bundle, id, 128);
        var sb = new StringBuilder(); int vbase = 1;
        foreach (var p in gd.parts) {
          for (int i = 0; i < p.pos.Length / 3; i++) sb.AppendFormat(CultureInfo.InvariantCulture, "v {0} {1} {2}\n", p.pos[i*3], p.pos[i*3+1], p.pos[i*3+2]);
          for (int i = 0; i < p.tris.Length; i += 3) sb.AppendFormat("f {0} {1} {2}\n", p.tris[i]+vbase, p.tris[i+1]+vbase, p.tris[i+2]+vbase);
          vbase += p.pos.Length / 3;
        }
        File.WriteAllText(Path.Combine(outDir, "w" + id + ".obj"), sb.ToString());
        Console.WriteLine(id + " " + g.name + " cat" + g.category + " parts=" + gd.parts.Count + " tex=" + string.Join(",", gd.parts.Select(p => p.tex == null ? "none" : p.tex.name + ":" + p.tex.format + ":" + p.tex.width + "x" + p.tex.height + "m" + p.tex.mipCount))
          + " shoot=" + (gd.shoot == null ? "none" : gd.shoot.samples.Length + "@" + gd.shoot.frequency + "x" + gd.shoot.channels) + " " + sw.ElapsedMilliseconds + "ms\n   log: " + gd.log);
        if (gd.shoot != null) Wav(Path.Combine(outDir, "w" + id + "_shoot.wav"), gd.shoot);
      }
    }
  }
  static void Wav(string path, AudioData a) {
    using (var w = new BinaryWriter(File.Create(path))) {
      int n = a.samples.Length; w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)a.channels);
      w.Write(a.frequency); w.Write(a.frequency * a.channels * 2); w.Write((short)(a.channels * 2)); w.Write((short)16); w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
      foreach (var s in a.samples) w.Write((short)Math.Max(-32768, Math.Min(32767, s * 32767)));
    }
  }
}
