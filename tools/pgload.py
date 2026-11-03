# Load a PG3D bundle together with the bundles it references.
import json, UnityPy, re
B=json.load(open("work/bundles.json")); C=json.load(open("work/cabs.json"))
def find(suffix):
    s=suffix.lower()
    return [p for p,v in B.items() if any(c.lower().endswith(s) for c in v["container"])]
def load(path, depth=1):
    paths=[path]; env=UnityPy.load(path)
    for _ in range(depth):
        for f in list(env.files.values()):
            for sf in getattr(f,'files',{}).values():
                for e in getattr(sf,'externals',[]):
                    m=re.search(r"(cab-[0-9a-f]+)",e.path.lower())
                    if m and m.group(1) in C and C[m.group(1)] not in paths:
                        paths.append(C[m.group(1)])
        env=UnityPy.load(*paths)
    return env
