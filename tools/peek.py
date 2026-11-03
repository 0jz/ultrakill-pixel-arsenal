import json, sys, UnityPy
b=json.load(open("work/bundles.json"))
key=sys.argv[1]
path=[p for p,v in b.items() if any(c.lower().endswith(key.lower()) for c in v["container"])][0]
print(path)
env=UnityPy.load(path)
import collections
print(collections.Counter(o.type.name for o in env.objects))
for o in env.objects:
    if o.type.name=="MonoBehaviour":
        try:
            t=o.read_typetree()
            sc=o.read().m_Script.read().m_ClassName
            print("==",sc, {k:(v if not isinstance(v,(list,dict)) or len(str(v))<120 else str(v)[:120]) for k,v in t.items() if not k.startswith("m_") })
        except Exception as e: print("ERR",e)
