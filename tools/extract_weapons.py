# Builds sheets/pg3d_weapons.json: one row per PG3D weapon model prefab, from its WeaponSounds component.
import json, re, sys, UnityPy
from concurrent.futures import ProcessPoolExecutor
B=json.load(open("work/bundles.json"))
KEEP=["categoryNabor","ammoInClip","InitialAmmo","maxAmmo","localizeWeaponKey","ShopId","Tag","rarity","tier",
 "fireRateShop","dpses","damageByTier","countShots","isShotGun","bazooka","grenadeLauncher","countInSeriaBazooka",
 "bazookaExplosionRadius","isMelee","isRoundMelee","radiusRoundMelee","railgun","flamethrower","isLightning",
 "isCharging","chargeTime","isDash","dashMaxImpulse","dashDecaySpeed","isZooming","fieldOfViewZomm","isDoubleShot","isBurstShooting","countShootInBurst",
 "harpoon","isGrenadeWeapon","isUnlimitedAmmo","speedModifier","bulletExplode","freezer","isPoisoning","campaignOnly","CanBuy","IsRoyaleWeapon","IsEvent"]
def row(arg):
    wid,path=arg
    try:
        env=UnityPy.load(path)
        out={"id":wid,"bundle":path,"meshes":0,"skinned":0}
        for o in env.objects:
            n=o.type.name
            if n=="MonoBehaviour":
                t=o.read_typetree()
                if "categoryNabor" in t:
                    for k in KEEP: out[k]=t.get(k)
            elif n=="MeshFilter": out["meshes"]+=1
            elif n=="SkinnedMeshRenderer": out["skinned"]+=1
        return out
    except Exception as e:
        return {"id":wid,"bundle":path,"error":str(e)[:200]}
if __name__=="__main__":
    jobs={}
    for p,v in B.items():
        for c in v["container"]:
            m=re.search(r"AddressableContent/Weapons/Models/Weapons/Weapon(\d+)\.prefab$",c)
            if m: jobs.setdefault(int(m.group(1)),p)
    with ProcessPoolExecutor() as ex:
        rows=sorted(ex.map(row,sorted(jobs.items()),chunksize=8),key=lambda r:r["id"])
    import os; os.makedirs("sheets",exist_ok=True)
    json.dump(rows,open("work/pg3d_weapons_raw.json","w"),indent=1)
    print(len(rows),"rows;",sum(1 for r in rows if "error" in r),"errors;",sum(1 for r in rows if "categoryNabor" not in r and "error" not in r),"no WeaponSounds")
