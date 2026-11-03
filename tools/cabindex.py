# CAB name -> bundle path index, so a bundle's external references can be loaded.
import json, sys, UnityPy
from concurrent.futures import ProcessPoolExecutor
def cabs(p):
    try:
        env=UnityPy.load(p)
        return p,[k for f in env.files.values() for k in getattr(f,'files',{}) if k.upper().startswith("CAB-") and "." not in k]
    except Exception: return p,[]
if __name__=="__main__":
    b=json.load(open("work/bundles.json"))
    idx={}
    with ProcessPoolExecutor() as ex:
        for p,ks in ex.map(cabs,list(b),chunksize=32):
            for k in ks: idx[k.lower()]=p
    json.dump(idx,open("work/cabs.json","w"))
    print(len(idx))
