# Lists every PG3D bundle (install StreamingAssets + user AddressablesCache) with its container paths.
import os, sys, json, glob
from concurrent.futures import ProcessPoolExecutor
import UnityPy

def scan(path):
    try:
        env = UnityPy.load(path)
        for o in env.objects:
            if o.type.name == "AssetBundle":
                d = o.read()
                return path, d.m_Name, sorted({k for k, _ in d.m_Container})
        return path, None, []
    except Exception as e:
        return path, "ERR " + str(e)[:80], []

if __name__ == "__main__":
    game, cache, out = sys.argv[1:4]
    files = glob.glob(os.path.join(game, "Pixel Gun 3D_Data/StreamingAssets/aa/StandaloneWindows64/*.bundle"))
    files += [f for f in glob.glob(os.path.join(cache, "*/*/__data"))]
    res = {}
    with ProcessPoolExecutor() as ex:
        for p, name, cont in ex.map(scan, files, chunksize=32):
            res[p] = {"name": name, "container": cont}
    json.dump(res, open(out, "w"), indent=0)
    print(len(files), "bundles")
