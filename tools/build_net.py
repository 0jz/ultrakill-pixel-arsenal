# Builds Melty's ULTRAKILL add-on with Pixel Arsenal's small changes, then zips dist/PixelArsenal-<version>.zip.
# Usage: python tools/build_net.py <version>. Fetches Melty's add-on (tools/fetch_kit.py) the first time.
# The game: ULTRAKILL_DIR if set, else the first ULTRAKILL with BepInEx in it (Melty installs BepInEx there) in Steam's libraries.
import glob, os, re, shutil, subprocess, sys, zipfile

VERSION = sys.argv[1]

def steam_libraries():
    roots = []
    try:
        import winreg
        k = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Valve\Steam')
        roots.append(winreg.QueryValueEx(k, 'SteamPath')[0])
    except OSError:
        pass
    roots += ['C:/Program Files (x86)/Steam', 'C:/Program Files/Steam']
    libs = []
    for r in roots:
        vdf = os.path.join(r, 'steamapps', 'libraryfolders.vdf')
        if os.path.exists(vdf):
            libs += [p.replace('\\\\', '/') for p in re.findall(r'"path"\s+"([^"]+)"', open(vdf, encoding='utf8').read())]
        libs.append(r)
    return libs

def find_game():
    tried = [os.environ.get('ULTRAKILL_DIR', '')]
    tried += [os.path.join(l, 'steamapps', 'common', 'ULTRAKILL') for l in steam_libraries()]
    tried += glob.glob(os.path.expanduser('~/Desktop/**/ULTRAKILL/ULTRAKILL.exe'), recursive=True)
    for t in tried:
        d = os.path.dirname(t) if t.endswith('.exe') else t
        if d and os.path.exists(os.path.join(d, 'BepInEx', 'core', 'BepInEx.dll')):
            return d.replace('\\', '/')
    raise SystemExit('No ULTRAKILL with BepInEx found. Play the mashup once through Melty (it installs BepInEx), or set ULTRAKILL_DIR. Tried: ' + ', '.join(t for t in tried if t))

GAME = find_game()
print('ULTRAKILL:', GAME)
if not os.path.exists('work/meltykit/addons/kit/MeltyStart.cs'):
    subprocess.run([sys.executable, '-I', 'tools/fetch_kit.py'], check=True)
ADDON = 'work/meltykit/addons/games/ultrakill/'

def sub(name, a, b):
    p = ADDON + name
    s = open(p, encoding='utf8').read()
    if b in s:
        return
    if a not in s:
        raise SystemExit('no match in ' + name + ': ' + a[:60])
    open(p, 'w', encoding='utf8').write(s.replace(a, b, 1))

shutil.copy('tools/net/PixelArsenalBridge.cs', ADDON)
sub('MeltyNetUltrakill.Gear.cs', '''        var held = gear.Held;
''', '''        var held = gear.Held;
        if (PixelArsenalBridge.ShowHeld != null && PixelArsenalBridge.ShowHeld(puppet, held)) return GearShows.Held;
''')
sub('MeltyNetUltrakill.cs', '''        if (index > 2) index = -1;
''', '''        if (index > 2 || (PixelArsenalBridge.HasGun != null && PixelArsenalBridge.HasGun(puppet))) index = -1;
''')
sub('MeltyNetUltrakill.cs', '''        float speed, running;
''', '''        float shotCount; int lastShot;
        if (AnimSync.TryRead(payload, "MeltyShots", out shotCount))
        {
            if (pgShots.TryGetValue(puppet, out lastShot) && lastShot != (int)shotCount && PixelArsenalBridge.Shot != null) PixelArsenalBridge.Shot(puppet);
            pgShots[puppet] = (int)shotCount;
        }
        float speed, running;
''')
sub('MeltyNetUltrakill.cs', '''    readonly System.Collections.Generic.Dictionary<GameObject, int> shownWeapon =''', '''    readonly System.Collections.Generic.Dictionary<GameObject, int> pgShots = new System.Collections.Generic.Dictionary<GameObject, int>();
    readonly System.Collections.Generic.Dictionary<GameObject, int> shownWeapon =''')
sub('MeltyNetUltrakill.cs', '''        try { shared.Update(player); }
''', '''        PixelArsenalBridge.Fired = what => { Acted("fire", Melty.Net.Addon.ActPhase.Start, what); Acted("fire", Melty.Net.Addon.ActPhase.End); };
        PixelArsenalBridge.Others = Net != null ? Net.Peers.Count : 0;
        try { shared.Update(player); }
''')
sub('MeltyNetUltrakill.Sounds.cs', '''                h.DueShot = Math.Min(4, h.DueShot + shots);
''', '''                h.DueShot = Math.Min(4, h.DueShot + shots);
                PlayedBy(e.Id.Owner, "fire", true, "PG3D shot");
''')
# the room's folder and the mashup, as setup_multiplayer asks
sub('melty-net.cfg', '''slug=ultrakill
''', '''slug=ultrakill
folder={localappdata}/UltrakillPixelArsenal
mashup=ultrakill-pixel-arsenal
''')

def run(*cmd):
    r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors='replace')
    tail = [l for l in r.stdout.splitlines() if ' error ' in l or 'Error(s)' in l]
    print('\n'.join(sorted(set(l[:260] for l in tail))))
    if r.returncode:
        raise SystemExit('build failed')

run('dotnet', 'build', ADDON + 'MeltyNet.Ultrakill.csproj', '-c', 'Release',
    '-p:GameManaged=' + GAME + '/ULTRAKILL_Data/Managed', '-p:BepInExDir=' + GAME + '/BepInEx/core', '-p:CoreTargetFramework=netstandard2.1')
run('dotnet', 'build', 'PixelArsenal.csproj', '-c', 'Release', '-o', 'work/build', '-p:UkDir=' + GAME)

os.makedirs('dist/PixelArsenal', exist_ok=True)
for f in ('work/build/PixelArsenal.dll', 'lib/AssetsTools.NET.dll', 'lib/THIRD-PARTY-NOTICES.txt'):
    if os.path.exists(f): shutil.copy(f, 'dist/PixelArsenal/')
out = next(os.path.join(ADDON, 'bin/Release', d) for d in os.listdir(ADDON + 'bin/Release'))
z = 'dist/PixelArsenal-' + VERSION + '.zip'
with zipfile.ZipFile(z, 'w', zipfile.ZIP_DEFLATED) as f:
    for n in os.listdir('dist/PixelArsenal'):
        f.write('dist/PixelArsenal/' + n, 'PixelArsenal/' + n)
    for n in ('MeltyNet.Unity.dll', 'MeltyNet.Ultrakill.dll', 'melty-net.cfg'):
        f.write(os.path.join(out, n), 'MeltyNet/' + n)
print(z, os.path.getsize(z))
