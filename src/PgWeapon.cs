// One PG3D gun in V1's hands: its PG3D fire rate, clip and damage, firing ULTRAKILL's own projectiles (sheets/fire_types.json).
using System.Linq;
using UnityEngine;

namespace PixelGunArsenal
{
    public class PgWeapon : MonoBehaviour
    {
        public PgGun gun;
        public Category cat;
        public FireType fire;
        public float damage;
        public bool modelReady;
        int ammo;
        float nextShot, reloadEnd = -1, kick, swing;
        bool zooming;
        AudioSource aud;
        AudioClip shootClip, reloadClip;
        Transform model;
        Vector3 modelHome, muzzle;
        Quaternion modelRot = Quaternion.identity;
        GameObject flash;
        float flashUntil;
        static Material tracerMat;

        // ULTRAKILL prefabs, read once from GunSetter (sheets/fire_types.json ukSource/ukComponentField)
        public static GameObject Beam, Rail, Rocket, Grenade, Nail;
        public static AudioClip SndShot, SndShotgun, SndRail, SndRocket, SndMelee;
        public static int ShotsFired; // for test mode

        public static void Calibrate(GunSetter gs)
        {
            if (Beam != null) return;
            var rev = gs.revolverPierce.ToAssets()[0].GetComponentInChildren<Revolver>(true);
            Beam = rev.revolverBeam; SndShot = rev.gunShots.FirstOrDefault();
            var rc = gs.railCannon.ToAssets()[0].GetComponentInChildren<Railcannon>(true);
            Rail = rc.beam;
            if (rc.fireSound && rc.fireSound.GetComponent<AudioSource>()) SndRail = rc.fireSound.GetComponent<AudioSource>().clip;
            var rl = gs.rocketBlue.ToAssets()[0].GetComponentInChildren<RocketLauncher>(true);
            Rocket = rl.rocket;
            var rla = rl.GetComponent<AudioSource>(); if (rla) SndRocket = rla.clip;
            var sg = gs.shotgunGrenade.ToAssets()[0].GetComponentInChildren<Shotgun>(true);
            Grenade = sg.grenade; SndShotgun = sg.shootSound; SndMelee = sg.smackSound ? sg.smackSound : sg.clickSound;
            Nail = gs.nailOverheat.ToAssets()[0].GetComponentInChildren<Nailgun>(true).heatedNail;
        }

        public static FireType PickFire(PgGun g, Category c)
        {
            foreach (var f in Sheets.FireTypes) if (f.pg3dFlag != "none" && g.Flag(f.pg3dFlag)) return f;
            return Sheets.FireTypes.First(f => f.id == c.defaultFireType);
        }

        public void Init(PgGun g, Category c)
        {
            gun = g; cat = c; fire = PickFire(g, c);
            damage = g.damageRaw * c.damageScale;
            ammo = g.clip;
            aud = gameObject.AddComponent<AudioSource>();
            aud.playOnAwake = false; aud.spatialBlend = 0; aud.volume = 0.6f;
            shootClip = fire.id == "rail" ? SndRail : fire.id == "rocket" || fire.id == "grenade" ? SndRocket : fire.id == "shotgun" ? SndShotgun : fire.id == "melee" ? SndMelee : SndShot;
                    }

        public void ApplyModel(GunData d, int texMax, float scale)
        {
            var cam = MonoSingleton<CameraController>.Instance ? MonoSingleton<CameraController>.Instance.transform : Camera.main.transform;
            var m = PgModel.Build(d, transform, cam, texMax, scale, out flash, out muzzle);
            if (m) { model = m.transform; modelHome = model.localPosition; modelRot = model.localRotation; }
            shootClip = PgModel.Clip(d.shoot, gun.name + " shoot") ?? shootClip;
            reloadClip = PgModel.Clip(d.reload, gun.name + " reload");
            modelReady = true;
        }

        bool Unlimited { get { return gun.unlimited || gun.clip <= 0 || fire.id == "melee"; } }

        // PG3D's dash: hold to charge, release to lunge along the aim. Distance = charge x impulse / decay (sheets/weapon_columns.json).
        float chargeHeld, dashSpeed, dashDecay;
        Vector3 dashDir;
        readonly System.Collections.Generic.HashSet<EnemyIdentifier> dashHit = new System.Collections.Generic.HashSet<EnemyIdentifier>();
        public bool Dashing { get { return dashSpeed > 0; } }
        public float DashMoved; // metres of the last dash, for test mode

        public void StartDash(float charge, Vector3 dir, float impulse, float decay)
        {
            dashDir = dir.normalized;
            dashDecay = decay;
            dashSpeed = charge * impulse * Plugin.DashScale;
            DashMoved = 0;
            dashHit.Clear();
        }

        void FixedUpdate()
        {
            if (dashSpeed <= 0) return;
            var nm = MonoSingleton<NewMovement>.Instance;
            if (!nm || nm.dead || ArmoryMenu.Open) { dashSpeed = 0; return; }
            float step = dashSpeed * Time.fixedDeltaTime;
            // walls stop it; a ray from mid-body, because a sweep of V1's capsule always touches the floor it stands on
            if (Physics.Raycast(nm.transform.position, dashDir, step + 1f, LayerMaskDefaults.Get(LMD.Environment), QueryTriggerInteraction.Ignore)) { dashSpeed = 0; return; }
            nm.rb.position += dashDir * step;
            DashMoved += step;
            nm.rb.velocity = new Vector3(0, Mathf.Max(nm.rb.velocity.y, 0), 0); // the lunge replaces running and falling
            MeleeHit(MonoSingleton<CameraController>.Instance.transform, dashHit);
            dashSpeed -= dashSpeed * dashDecay * Time.fixedDeltaTime;
            if (dashSpeed < 2f) dashSpeed = 0;
        }

        void MeleeHit(Transform cam, System.Collections.Generic.HashSet<EnemyIdentifier> only)
        {
            var gc = MonoSingleton<GunControl>.Instance;
            var src = gc.currentWeapon ? gc.currentWeapon : gameObject;
            float r = fire.range;
            var hits = Physics.OverlapSphere(cam.position + cam.forward * r * 0.6f, r * 0.6f, LayerMaskDefaults.Get(LMD.EnemiesAndEnvironment));
            foreach (var e in hits.Select(h => h.GetComponent<EnemyIdentifierIdentifier>()).Where(e => e && e.eid && !e.eid.dead).GroupBy(e => e.eid).Select(x => x.First()))
            {
                if (only != null && !only.Add(e.eid)) continue;
                e.eid.hitter = "punch";
                e.eid.DeliverDamage(e.gameObject, cam.forward * 6000f, e.transform.position, damage, false, 0f, src);
            }
        }

        void OnDisable()
        {
            chargeHeld = 0; dashSpeed = 0;
            if (zooming && MonoSingleton<CameraController>.Instance) MonoSingleton<CameraController>.Instance.StopZoom();
            zooming = false;
        }

        void Update()
        {
            if (model)
            {
                kick = Mathf.MoveTowards(kick, 0, Time.deltaTime * 4f);
                swing = Mathf.MoveTowards(swing, 0, Time.deltaTime * 5f);
                float reloadT = reloadEnd > 0 ? Mathf.Sin(Mathf.Clamp01(1 - (reloadEnd - Time.time) / Mathf.Max(0.01f, cat.reloadSeconds)) * Mathf.PI) : 0;
                model.localPosition = modelHome + new Vector3(0, -0.35f * reloadT, -0.25f * kick);
                model.localRotation = modelRot * Quaternion.Euler(-12f * kick + 40f * reloadT + 35f * swing, -30f * swing, 20f * swing);
                if (flash) flash.SetActive(Time.time < flashUntil);
            }
            if (MonoSingleton<OptionsManager>.Instance.paused || GameStateManager.Instance.PlayerInputLocked || ArmoryMenu.Open) return;
            var input = MonoSingleton<InputManager>.Instance.InputSource;
            var cc = MonoSingleton<CameraController>.Instance;

            if (gun.zoomFov > 0)
            {
                if (input.Fire2.IsPressed) { cc.Zoom(gun.zoomFov); zooming = true; }
                else if (zooming) { cc.StopZoom(); zooming = false; }
            }
            if (reloadEnd > 0 && Time.time >= reloadEnd) { reloadEnd = -1; ammo = gun.clip; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb[Plugin.ReloadKey.Value].wasPressedThisFrame && !Unlimited && reloadEnd < 0 && ammo < gun.clip) StartReload();
            if (gun.Dashes)
            {
                if (gun.DashCharges)
                {
                    if (input.Fire1.IsPressed && Time.time >= nextShot) { chargeHeld += Time.deltaTime; return; }
                    if (chargeHeld <= 0) return;
                }
                else if (!input.Fire1.WasPerformedThisFrame || Time.time < nextShot) return;
                float charge = gun.DashCharges ? Mathf.Clamp01(chargeHeld / gun.chargeTime) : 1f;
                chargeHeld = 0;
                Fire(cc.transform);
                nextShot = Time.time + 1f / gun.ShotsPerSecond;
                if (charge >= 0.1f) StartDash(charge, cc.transform.forward, gun.dashImpulse, gun.dashDecay);
                return;
            }
            bool auto = fire.autoFire == "hold" || (fire.autoFire == "fast" && gun.ShotsPerSecond >= 5f);
            bool trigger = auto ? input.Fire1.IsPressed : input.Fire1.WasPerformedThisFrame; // sheets/fire_types.json autoFire
            if (reloadEnd < 0 && trigger && Time.time >= nextShot)
            {
                Fire(cc.transform);
                nextShot = Time.time + 1f / gun.ShotsPerSecond;
                if (!Unlimited && --ammo <= 0) StartReload();
            }
        }

        void TestFire() { Fire(MonoSingleton<CameraController>.Instance.transform); } // Melty test mode only

        void StartReload()
        {
            reloadEnd = Time.time + Mathf.Max(0.3f, cat.reloadSeconds);
            if (reloadClip) aud.PlayOneShot(reloadClip, 0.8f);
        }

        Quaternion Spread(Transform cam, float deg)
        {
            return cam.rotation * Quaternion.Euler(Random.Range(-deg, deg), Random.Range(-deg, deg), 0);
        }

        int Count(string spec)
        {
            int n;
            return spec == "countInSeriaBazooka" ? gun.burst : int.TryParse(spec, out n) ? n : 1;
        }

        void Fire(Transform cam)
        {
            ShotsFired++;
            NetBridge.Fired(gun.name);
            var gc = MonoSingleton<GunControl>.Instance;
            var src = gc.currentWeapon ? gc.currentWeapon : gameObject;
            Vector3 barrel = transform.TransformPoint(muzzle);
            int n = Count(fire.shotsPerTrigger);
            switch (fire.id)
            {
                case "melee":
                    MeleeHit(cam, null);
                    swing = 1;
                    break;
                case "rail":
                case "hitscan":
                case "shotgun":
                    for (int i = 0; i < n; i++)
                    {
                        var go = Instantiate(fire.id == "rail" ? Rail : Beam, cam.position, Spread(cam, fire.spreadDegrees));
                        var b = go.GetComponent<RevolverBeam>();
                        b.sourceWeapon = src;
                        b.alternateStartPoint = barrel;
                        b.damage = fire.id == "shotgun" ? damage * 1.5f / n : damage;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false; // PG3D look: no ULTRAKILL beam
                        Tracer(barrel, go.transform, fire.id == "rail" ? 0.07f : 0.025f);
                    }
                    break;
                case "rocket":
                    for (int i = 0; i < n; i++)
                    {
                        var go = Instantiate(Rocket, cam.position + cam.forward * 0.3f * i, Spread(cam, i == 0 ? 0 : fire.spreadDegrees));
                        var g = go.GetComponent<Grenade>(); if (g) g.sourceWeapon = src;
                    }
                    break;
                case "grenade":
                    for (int i = 0; i < n; i++)
                    {
                        var go = Instantiate(Grenade, cam.position + cam.forward * 0.5f, Random.rotation);
                        var g = go.GetComponentInChildren<Grenade>(); if (g) g.sourceWeapon = src;
                        var rb = go.GetComponent<Rigidbody>(); if (rb) rb.AddForce(Spread(cam, fire.spreadDegrees) * Vector3.forward * 55f + Vector3.up * 3f, ForceMode.VelocityChange);
                    }
                    break;
                case "flame":
                    for (int i = 0; i < n; i++)
                    {
                        var go = Instantiate(Nail, cam.position + cam.forward, Spread(cam, fire.spreadDegrees));
                        var nl = go.GetComponent<Nail>();
                        if (nl) { nl.sourceWeapon = src; nl.damage = damage; }
                        var rb = go.GetComponent<Rigidbody>(); if (rb) rb.velocity = go.transform.forward * 120f;
                    }
                    break;
            }
            kick = Mathf.Min(1, kick + (fire.id == "rail" || fire.id == "rocket" ? 1f : 0.45f));
            MonoSingleton<CameraController>.Instance.CameraShake(fire.id == "rail" || fire.id == "rocket" || fire.id == "grenade" ? 0.6f : 0.15f);
            if (shootClip) AudioSourceExtensions.PlayOneShot(aud, shootClip, 1f, true);
            flashUntil = Time.time + 0.06f;
        }

        // A short bright line like PG3D's bullet trails, from the gun's barrel to what the shot hits.
        public static void Tracer(Vector3 from, Transform dir, float width)
        {
            RaycastHit hit;
            Vector3 to = Physics.Raycast(dir.position, dir.forward, out hit, 300f, LayerMaskDefaults.Get(LMD.EnemiesAndEnvironment)) ? hit.point : dir.position + dir.forward * 300f;
            if (!tracerMat) tracerMat = new Material(Shader.Find("Sprites/Default"));
            var go = new GameObject("PG3D Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = tracerMat;
            lr.positionCount = 2; lr.SetPosition(0, from); lr.SetPosition(1, to);
            lr.startWidth = width; lr.endWidth = width * 0.6f;
            lr.startColor = new Color(1f, 0.95f, 0.6f, 0.9f); lr.endColor = new Color(1f, 0.8f, 0.3f, 0.2f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Destroy(go, 0.05f);
        }

        static Font ukFont;
        public static Font UkFont
        {
            get
            {
                if (!ukFont) ukFont = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name.ToUpperInvariant().Replace('_', ' ').Contains("VCR OSD"));
                return ukFont;
            }
        }

        static void Shadowed(Rect r, string text, GUIStyle s, Color c, float u)
        {
            s.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 3 * u, r.y + 3 * u, r.width, r.height), text, s);
            s.normal.textColor = c;
            GUI.Label(r, text, s);
        }

        // ULTRAKILL-style counter: its VCR OSD Mono font, a big white count over a small gun name in the category colour
        void OnGUI()
        {
            if (ArmoryMenu.Open || !isActiveAndEnabled) return;
            float u = Screen.height / 1080f;
            string ammoText = !modelReady ? "LOADING" : chargeHeld > 0 ? "CHARGE " + Mathf.RoundToInt(Mathf.Clamp01(chargeHeld / gun.chargeTime) * 100) + "%" : Unlimited ? "INF" : reloadEnd > 0 ? "RELOAD" : ammo + "<size=" + (int)(30 * u) + ">/" + gun.clip + "</size>";
            var big = new GUIStyle(GUI.skin.label) { font = UkFont, fontSize = (int)(64 * u), alignment = TextAnchor.LowerRight, richText = true };
            var small = new GUIStyle(big) { fontSize = (int)(24 * u) };
            var r1 = new Rect(Screen.width - 640 * u, Screen.height - 230 * u, 600 * u, 80 * u);
            var r2 = new Rect(r1.x, r1.yMax, r1.width, 34 * u);
            Color col; ColorUtility.TryParseHtmlString(cat.color, out col);
            Shadowed(r1, ammoText, big, reloadEnd > 0 ? new Color(1f, .3f, .3f) : Color.white, u);
            Shadowed(r2, gun.name.ToUpperInvariant() + " - " + cat.name.ToUpperInvariant(), small, col, u);
        }
    }
}
