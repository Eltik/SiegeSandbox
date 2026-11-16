using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Full-auto weapon with aim-down-sights, a fixed (learnable) recoil pattern,
    /// hip-fire spread, projectile bullets, a melee strike and a staged reload animation.
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        public static Weapon Instance { get; private set; }

        [Header("Rig")]
        public PlayerController player;
        public Camera cam;
        public Transform model;
        public Transform muzzle;
        public GameObject muzzleFlash;
        public Transform magazine;
        public Transform bolt;
        public Transform leftArm;
        public Material tracerMaterial;
        public Material impactMaterial;

        [Header("Fire")]
        public float roundsPerMinute = 850f;
        public int magazineSize = 30;
        public float muzzleVelocity = 180f;   // m/s, deliberately slow enough to feel the travel time
        public float gravityScale = 1f;
        public float damage = 34f;

        [Header("Damage fall-off (metres)")]
        public float falloffStart = 18f;       // full damage up to here
        public float falloffEnd = 38f;         // minimum damage from here on
        public float falloffMinScale = 0.55f;  // fraction of damage left at long range

        [Header("Reload (seconds)")]
        public float reloadTime = 1.9f;        // magazine swap with a round still chambered
        public float emptyReloadTime = 2.6f;   // magazine swap plus charging the bolt

        [Header("Melee")]
        public float meleeDamage = 100f;
        public float meleeRange = 1.9f;
        public float meleeRadius = 0.3f;
        public float meleeTime = 0.5f;
        public float meleeHitTime = 0.14f;

        [Header("Aim down sights")]
        public float hipFov = 78f;
        public float adsFov = 50f;
        public float sprintFovBoost = 5f;
        public float adsTime = 0.18f;
        public Vector3 hipPosition = new Vector3(0.16f, -0.15f, 0.36f);
        public Vector3 adsPosition = new Vector3(0f, -0.066f, 0.22f);

        [Header("Spread (degrees)")]
        public float hipSpread = 1.6f;
        public float bloomPerShot = 0.22f;
        public float maxBloom = 2.6f;
        public float bloomRecovery = 6f;
        public float moveSpread = 1.4f;
        public float adsSpread = 0.05f;
        public float airHipSpread = 3.0f;      // extra cone while airborne
        public float airAdsSpread = 1.2f;
        public float leanHipSpread = 0.5f;     // extra hip cone while leaned over
        public float leanMoveHipSpread = 1.5f; // extra cone while the lean is still moving
        public float leanMoveAdsSpread = 0.5f;

        [Header("Recoil (degrees per shot: x = right, y = up)")]
        public Vector2[] recoilPattern = DefaultPattern();
        public float adsRecoilScale = 0.5f;
        public float crouchRecoilScale = 0.85f;
        public float proneRecoilScale = 0.7f;
        public float patternResetTime = 0.35f;

        public int Ammo { get; private set; }
        /// <summary>Position in the recoil pattern. Resets after a short pause in firing.</summary>
        public int ShotIndex { get; private set; }
        public int ShotsFired { get; private set; }
        public int MeleeHits { get; private set; }
        public float AimBlend { get; private set; }
        public bool IsReloading { get; private set; }
        public bool IsMeleeing { get; private set; }
        public float CurrentSpreadDeg { get; private set; }

        public event System.Action Fired;

        // The reload is authored on this timeline (seconds) and stretched to the reload time.
        // The support hand drives it: reach, release, pull, stow, fetch, align, seat, slap, (rack), return.
        const float ReachEnd = 0.22f, Release = 0.30f, MagClear = 0.52f, Stowed = 0.72f, Fresh = 0.92f;
        const float Aligned = 1.20f, MagSeat = 1.32f, Slap = 1.42f;
        const float BoltReach = 1.62f, BoltBack = 1.82f, BoltLetGo = 1.90f;
        const float TacticalLength = 1.90f, EmptyLength = 2.60f, ReturnTime = 0.42f;

        // Hand positions in the gun's own space.
        static readonly Vector3 Shoulder = new Vector3(-0.20f, -0.25f, -0.16f);
        static readonly Vector3 ForeGrip = new Vector3(0f, -0.045f, 0.16f);
        static readonly Vector3 MagGrip = new Vector3(0f, -0.155f, 0.07f);
        static readonly Vector3 Belt = new Vector3(-0.17f, -0.44f, -0.10f);
        static readonly Vector3 BoltGrip = new Vector3(-0.05f, 0.025f, -0.02f);

        float bloom, nextFireTime, lastShotTime = -10f, flashTimer;
        float kick, sprintBlend, reloadPose, busyBlend;
        float reloadElapsed, meleeElapsed;
        bool reloadFromEmpty, magSeated, meleeHitDone;
        bool heardMagOut, heardBoltBack, heardBoltForward, firedLastFrame, aimedLastFrame;
        float springPos, springVel;   // a damped bounce for seating the magazine and letting the bolt go
        Quaternion magRestRotation = Quaternion.identity;
        Vector3 armThickness = new Vector3(0.055f, 0.055f, 0.34f);
        Vector3 magRest, boltRest, armRest;

        /// <summary>Climbs hard for the first shots, then drifts right, left, and right again.</summary>
        public static Vector2[] DefaultPattern()
        {
            return new Vector2[]
            {
                new Vector2( 0.05f, 0.90f), new Vector2(-0.05f, 1.00f), new Vector2( 0.10f, 1.10f), new Vector2( 0.15f, 1.10f),
                new Vector2( 0.10f, 1.00f), new Vector2( 0.20f, 0.90f), new Vector2( 0.25f, 0.80f), new Vector2( 0.30f, 0.70f),
                new Vector2( 0.35f, 0.45f), new Vector2( 0.40f, 0.42f), new Vector2( 0.40f, 0.40f), new Vector2( 0.30f, 0.38f),
                new Vector2( 0.20f, 0.36f), new Vector2( 0.00f, 0.34f), new Vector2(-0.20f, 0.32f), new Vector2(-0.35f, 0.30f),
                new Vector2(-0.45f, 0.26f), new Vector2(-0.50f, 0.25f), new Vector2(-0.50f, 0.25f), new Vector2(-0.40f, 0.25f),
                new Vector2(-0.30f, 0.24f), new Vector2(-0.10f, 0.24f), new Vector2( 0.10f, 0.22f), new Vector2( 0.30f, 0.22f),
                new Vector2( 0.40f, 0.20f), new Vector2( 0.45f, 0.20f), new Vector2( 0.40f, 0.20f), new Vector2( 0.30f, 0.20f),
                new Vector2( 0.10f, 0.20f), new Vector2(-0.10f, 0.20f)
            };
        }

        void Awake()
        {
            Instance = this;
            Ammo = magazineSize;
            if (muzzleFlash != null) muzzleFlash.SetActive(false);
            if (magazine != null)
            {
                magRest = magazine.localPosition;
                magRestRotation = magazine.localRotation;
            }
            if (leftArm != null) armThickness = leftArm.localScale;
            if (bolt != null) boltRest = bolt.localPosition;
            if (leftArm != null) armRest = leftArm.localPosition;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ pure rules (also used by tests)

        static float StanceSpreadScale(Stance s)
        {
            return s == Stance.Prone ? 0.6f : (s == Stance.Crouch ? 0.8f : 1f);
        }

        /// <summary>Cone half-angle in degrees for the given state (on the ground).</summary>
        public float GetSpread(float aimBlend, float bloomDeg, float moveFraction, Stance stance)
        {
            float hip = (hipSpread + bloomDeg + moveSpread * Mathf.Clamp01(moveFraction)) * StanceSpreadScale(stance);
            return Mathf.Lerp(hip, adsSpread, Mathf.Clamp01(aimBlend));
        }

        /// <summary>View kick for the Nth shot of a spray. Same input always gives the same kick.</summary>
        public Vector2 GetRecoilKick(int shotIndex, float aimBlend, Stance stance)
        {
            int n = recoilPattern.Length;
            if (n == 0) return Vector2.zero;
            int tail = Mathf.Min(6, n);
            int i = shotIndex < n ? shotIndex : n - tail + ((shotIndex - n) % tail);
            float scale = Mathf.Lerp(1f, adsRecoilScale, Mathf.Clamp01(aimBlend));
            if (stance == Stance.Crouch) scale *= crouchRecoilScale;
            else if (stance == Stance.Prone) scale *= proneRecoilScale;
            return recoilPattern[i] * scale;
        }

        /// <summary>
        /// Recoil pushes along the gun's own up axis. With the gun rolled by a lean, a kick that
        /// would go straight up instead climbs toward the side you are leaning to.
        /// </summary>
        public static Vector2 ApplyLeanToKick(Vector2 kick, float rollDegrees)
        {
            float r = rollDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(kick.x * c + kick.y * s, -kick.x * s + kick.y * c);
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (player == null || cam == null) return;
            float dt = Time.deltaTime;
            PlayerInputState inp = player.input;

            // Pulling the trigger on an empty or open gun just clicks.
            if (inp.fire && !firedLastFrame && (Ammo == 0 || IsReloading)) Sfx.PlayLocal(SoundId.DryFire, 0.6f);
            firedLastFrame = inp.fire;

            UpdateMelee(inp, dt);
            UpdateReload(inp, dt);

            bool busy = player.HandsBusy;
            bool wantsAim = inp.aim && !player.IsSprinting && !IsReloading && !IsMeleeing && !busy;
            if (wantsAim != aimedLastFrame) Sfx.PlayLocal(SoundId.Cloth, 0.22f, wantsAim ? 1.1f : 0.9f); // sling and sleeve
            aimedLastFrame = wantsAim;
            AimBlend = Mathf.MoveTowards(AimBlend, wantsAim ? 1f : 0f, dt / Mathf.Max(0.01f, adsTime));
            player.AimBlend = AimBlend;
            float aim = Mathf.SmoothStep(0f, 1f, AimBlend);

            sprintBlend = Mathf.MoveTowards(sprintBlend, player.IsSprinting ? 1f : 0f, dt * 6f);
            cam.fieldOfView = Mathf.Lerp(hipFov, adsFov, aim) + sprintFovBoost * sprintBlend;

            if (Time.time - lastShotTime > patternResetTime) ShotIndex = 0;

            // The gun has to come back up out of the sprint pose before it can fire.
            bool firing = inp.fire && !IsReloading && !IsMeleeing && !busy && Ammo > 0 && sprintBlend < 0.2f;
            if (!firing) bloom = Mathf.MoveTowards(bloom, 0f, bloomRecovery * dt);

            float moveFraction = player.PlanarSpeed / Mathf.Max(0.01f, player.walkSpeed);
            float spread = GetSpread(AimBlend, bloom, moveFraction, player.Stance);
            if (!player.IsGrounded) spread += Mathf.Lerp(airHipSpread, airAdsSpread, AimBlend);
            // Leaning: a little worse from the hip, and noticeably worse while still swinging over.
            spread += Mathf.Lerp(leanHipSpread, 0f, AimBlend) * Mathf.Abs(player.Lean);
            spread += Mathf.Lerp(leanMoveHipSpread, leanMoveAdsSpread, AimBlend) * Mathf.Clamp01(player.LeanChangeRate / 5f);
            CurrentSpreadDeg = spread;

            if (firing)
            {
                float interval = 60f / Mathf.Max(1f, roundsPerMinute);
                if (nextFireTime < Time.time - interval) nextFireTime = Time.time;
                int burst = 0;
                while (Time.time >= nextFireTime && Ammo > 0 && burst < 3)
                {
                    FireOne();
                    nextFireTime += interval;
                    burst++;
                }
            }

            UpdatePose(dt, aim);
        }

        void FireOne()
        {
            Vector2 offset = Random.insideUnitCircle * CurrentSpreadDeg;
            Quaternion rot = cam.transform.rotation * Quaternion.Euler(-offset.y, offset.x, 0f);
            Vector3 dir = rot * Vector3.forward;
            Sfx.PlayLocal(SoundId.GunShot, 1f);
            Sfx.PlayReflections(cam.transform.position, 1f, true);
            Sfx.PlayLocal(SoundId.ShellCasing, 0.28f, 1f, Random.Range(0.35f, 0.5f)); // brass reaching the floor
            Projectile shot = Projectile.Spawn(cam.transform.position, dir * muzzleVelocity, damage, gravityScale, tracerMaterial, impactMaterial);
            shot.SetFalloff(falloffStart, falloffEnd, falloffMinScale);

            Vector2 k = ApplyLeanToKick(GetRecoilKick(ShotIndex, AimBlend, player.Stance), player.LeanRollDegrees);
            player.AddRecoil(k.y, k.x);

            ShotIndex++;
            ShotsFired++;
            Ammo--;
            bloom = Mathf.Min(maxBloom, bloom + bloomPerShot);
            lastShotTime = Time.time;
            kick = 1f;
            flashTimer = 0.04f;
            if (Fired != null) Fired.Invoke();
        }

        // ------------------------------------------------------------------ reload

        float ReloadTimelinePosition()
        {
            float length = reloadFromEmpty ? EmptyLength : TacticalLength;
            float duration = Mathf.Max(0.1f, reloadFromEmpty ? emptyReloadTime : reloadTime);
            return reloadElapsed * (length / duration);
        }

        void UpdateReload(PlayerInputState inp, float dt)
        {
            if (!IsReloading)
            {
                bool wants = (inp.reloadPressed && Ammo < magazineSize) || (inp.fire && Ammo == 0);
                if (wants && !IsMeleeing && !player.HandsBusy)
                {
                    IsReloading = true;
                    reloadElapsed = 0f;
                    reloadFromEmpty = Ammo == 0;
                    magSeated = false;
                    heardMagOut = heardBoltBack = heardBoltForward = false;
                    Sfx.PlayLocal(SoundId.Cloth, 0.35f);
                }
                return;
            }

            reloadElapsed += dt;
            float u = ReloadTimelinePosition();
            if (!heardMagOut && u >= Release)
            {
                heardMagOut = true;
                Sfx.PlayLocal(SoundId.MagOut, 0.8f);
                if (reloadFromEmpty) DropSpentMagazine(); // an empty magazine is just let go
            }
            if (!magSeated && u >= MagSeat)
            {
                // The rounds only count once the new magazine clicks in.
                magSeated = true;
                Ammo = magazineSize;
                Sfx.PlayLocal(SoundId.MagIn, 0.9f);
                springVel += 5.5f; // the gun jumps as it seats
            }
            if (reloadFromEmpty && !heardBoltBack && u >= BoltReach + 0.04f)
            {
                heardBoltBack = true;
                Sfx.PlayLocal(SoundId.BoltBack, 0.8f);
            }
            if (reloadFromEmpty && !heardBoltForward && u >= BoltLetGo)
            {
                heardBoltForward = true;
                Sfx.PlayLocal(SoundId.BoltForward, 0.9f);
                springVel -= 4.5f; // and jolts forward as the bolt slams home
            }
            if (u >= (reloadFromEmpty ? EmptyLength : TacticalLength)) IsReloading = false;
        }

        /// <summary>Leave a physics copy of the empty magazine to fall to the floor.</summary>
        void DropSpentMagazine()
        {
            if (magazine == null) return;
            GameObject spent = Prims.Cube("SpentMagazine", null, magazine.position, magazine.lossyScale, null, true);
            spent.transform.rotation = magazine.rotation;
            spent.layer = Prims.IgnoreRaycastLayer;
            Renderer source = magazine.GetComponent<Renderer>();
            if (source != null) spent.GetComponent<Renderer>().sharedMaterial = source.sharedMaterial;
            Rigidbody rb = spent.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.AddForce(-cam.transform.up * 1.2f + player.transform.forward * 0.4f, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 3f, ForceMode.VelocityChange);
            Destroy(spent, 6f);
            Sfx.PlayLocal(SoundId.MagDrop, 0.6f, 1f, 0.5f); // it reaches the floor half a second later
        }

        // ------------------------------------------------------------------ melee

        void UpdateMelee(PlayerInputState inp, float dt)
        {
            if (!IsMeleeing)
            {
                if (!inp.meleePressed || player.HandsBusy) return;
                IsMeleeing = true;
                meleeElapsed = 0f;
                meleeHitDone = false;
                Sfx.PlayLocal(SoundId.MeleeSwing, 0.8f);
                // A strike interrupts a reload. If the new magazine is already in, the reload still counts.
                IsReloading = false;
            }

            meleeElapsed += dt;
            if (!meleeHitDone && meleeElapsed >= meleeHitTime)
            {
                meleeHitDone = true;
                MeleeStrike();
            }
            if (meleeElapsed >= meleeTime) IsMeleeing = false;
        }

        static bool IsMeleeTarget(Collider c)
        {
            return c.GetComponentInParent<IDamageable>() != null
                || c.GetComponentInParent<Barricade>() != null
                || c.GetComponentInParent<SoftWall>() != null;
        }

        void MeleeStrike()
        {
            Vector3 origin = cam.transform.position;
            Vector3 forward = cam.transform.forward;
            int mask = PlayerController.WorldMask;
            const QueryTriggerInteraction NoTriggers = QueryTriggerInteraction.Ignore;
            Collider struck = null;
            Vector3 point = origin + forward * meleeRange;
            Vector3 dir = forward;
            RaycastHit hit;

            // 1. Whatever the crosshair is on.
            if (Physics.Raycast(origin, forward, out hit, meleeRange, mask, NoTriggers) && IsMeleeTarget(hit.collider))
            {
                struck = hit.collider;
                point = hit.point;
            }

            // 2. Otherwise sweep a short arc and take the nearest thing worth hitting. A wall in the way still blocks.
            if (struck == null)
            {
                float best = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 d = Quaternion.AngleAxis((i - 1) * 14f, cam.transform.up) * forward;
                    if (Physics.SphereCast(origin, meleeRadius, d, out hit, meleeRange - meleeRadius, mask, NoTriggers)
                        && hit.distance < best && IsMeleeTarget(hit.collider))
                    {
                        best = hit.distance;
                        struck = hit.collider;
                        point = hit.point;
                        dir = d;
                    }
                }
            }

            // 3. A sphere cast misses anything it already overlaps, so check point-blank range separately.
            if (struck == null)
            {
                Collider[] close = Physics.OverlapSphere(origin + forward * meleeRadius, meleeRadius + 0.1f, mask, NoTriggers);
                for (int i = 0; i < close.Length; i++)
                {
                    if (!IsMeleeTarget(close[i])) continue;
                    struck = close[i];
                    point = close[i].ClosestPoint(origin);
                    break;
                }
            }

            // 4. Facing a barricade but aiming at a gap or its frame still counts as hitting it.
            if (struck == null)
            {
                for (int i = 0; i < Barricade.All.Count; i++)
                {
                    Barricade b = Barricade.All[i];
                    if (!b.IsBuilt) continue;
                    Vector3 closest = b.ClosestPoint(origin);
                    Vector3 to = closest - origin;
                    float dist = to.magnitude;
                    if (dist > meleeRange) continue;
                    if (dist > 0.5f && Vector3.Dot(forward, to / dist) < 0.5f) continue;
                    if (dist > 0.1f && Physics.Raycast(origin, to / dist, out hit, dist - 0.05f, mask, NoTriggers)
                        && hit.collider.GetComponentInParent<Barricade>() != b) continue; // something solid in the way
                    b.MeleeHit(closest, forward);
                    MeleeHits++;
                    return;
                }
                return;
            }

            // Barricades lose a third of their planks per strike; soft walls get a hole knocked in them.
            Barricade barricade = struck.GetComponentInParent<Barricade>();
            if (barricade != null)
            {
                barricade.MeleeHit(point, dir);
                MeleeHits++;
                return;
            }
            SoftWall softWall = struck.GetComponentInParent<SoftWall>();
            if (softWall != null)
            {
                softWall.Punch(point, dir);
                MeleeHits++;
                return;
            }

            IDamageable target = struck.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                DamageInfo info = new DamageInfo();
                info.amount = meleeDamage;
                info.point = point;
                info.direction = dir;
                info.zone = struck.GetComponent<DamageZone>();
                info.melee = true;
                info.impulse = 220f;
                target.TakeDamage(info);
                Sfx.Play(SoundId.ImpactFlesh, point, 1f, 0.85f);
                MeleeHits++;
            }
        }

        // ------------------------------------------------------------------ pose + animation

        static float Ramp(float t, float start, float end)
        {
            return Mathf.Clamp01((t - start) / Mathf.Max(0.0001f, end - start));
        }

        static float Ease(float t, float start, float end)
        {
            return Mathf.SmoothStep(0f, 1f, Ramp(t, start, end));
        }

        void UpdatePose(float dt, float aim)
        {
            kick = Mathf.MoveTowards(kick, 0f, dt * 9f);

            // Damped spring: gives the gun a bounce on the events that should have weight.
            springVel += (-240f * springPos - 17f * springVel) * dt;
            springPos += springVel * dt;

            // ---- Reload: follow the support hand through the motions.
            float poseTarget = 0f, boltPull = 0f, rackTilt = 0f, pullTwist = 0f;
            Vector3 hand = ForeGrip;
            Vector3 magOffset = Vector3.zero;
            float magTilt = 0f;
            bool magVisible = true;

            if (IsReloading)
            {
                float u = ReloadTimelinePosition();
                float length = reloadFromEmpty ? EmptyLength : TacticalLength;
                poseTarget = Ease(u, 0f, ReachEnd) * (1f - Ease(u, length - ReturnTime, length));

                Vector3 pulled = MagGrip + new Vector3(0f, -0.20f, -0.015f);   // magazine clear of the well
                Vector3 below = MagGrip + new Vector3(0f, -0.115f, 0.01f);     // fresh one lined up under it

                if (u < Release)
                {
                    hand = Vector3.Lerp(ForeGrip, MagGrip, Ease(u, 0.04f, ReachEnd));
                }
                else if (u < Stowed)
                {
                    float down = Ramp(u, Release, MagClear);
                    hand = Vector3.Lerp(MagGrip, pulled, down * down);                  // tugged out, speeding up
                    hand = Vector3.Lerp(hand, Belt, Ease(u, MagClear, Stowed));          // and away to the belt
                    magOffset = hand - MagGrip;
                    magTilt = 25f * Ease(u, MagClear, Stowed);
                    magVisible = !reloadFromEmpty;                                       // an empty one was dropped
                    pullTwist = Mathf.Sin(Ramp(u, Release, MagClear) * Mathf.PI);
                }
                else if (u < Fresh)
                {
                    hand = Belt;
                    magVisible = false;
                }
                else if (u < MagSeat)
                {
                    float up = Ramp(u, Fresh, Aligned);
                    hand = Vector3.Lerp(Belt, below, 1f - (1f - up) * (1f - up));        // swung up, slowing under the well
                    float push = Ramp(u, Aligned, MagSeat);
                    hand = Vector3.Lerp(hand, MagGrip, push * push * push);              // then driven home
                    magOffset = hand - MagGrip;
                    magTilt = 25f * (1f - Ease(u, Fresh, Aligned));
                }
                else if (!reloadFromEmpty || u < Slap)
                {
                    hand = Vector3.Lerp(MagGrip, ForeGrip, Ease(u, Slap, Slap + 0.26f)); // a slap, then back to the fore-grip
                }
                else
                {
                    // Ran dry: over to the charging handle, haul it back, let it fly, return.
                    Vector3 hauled = BoltGrip + new Vector3(0f, 0f, -0.05f);
                    hand = Vector3.Lerp(MagGrip, BoltGrip, Ease(u, Slap, BoltReach));
                    float haul = Ease(u, BoltReach, BoltBack);
                    hand = Vector3.Lerp(hand, hauled, haul);
                    boltPull = u < BoltLetGo ? haul : 1f - Ramp(u, BoltLetGo, BoltLetGo + 0.03f);
                    hand = Vector3.Lerp(hand, ForeGrip, Ease(u, BoltLetGo + 0.08f, BoltLetGo + 0.36f));
                    rackTilt = Ease(u, Slap, BoltReach) * (1f - Ease(u, BoltLetGo + 0.05f, BoltLetGo + 0.3f));
                }
            }
            reloadPose = Mathf.MoveTowards(reloadPose, poseTarget, dt * 7f);
            busyBlend = Mathf.MoveTowards(busyBlend, player.HandsBusy ? 1f : 0f, dt * 6f);

            // ---- Melee: a short wind-up, a fast thrust, a slower recovery.
            float strike = 0f;
            if (IsMeleeing)
            {
                float t = meleeElapsed;
                strike = t < 0.06f ? -0.25f * Ramp(t, 0f, 0.06f)
                    : (t < 0.16f ? Mathf.Lerp(-0.25f, 1f, Mathf.SmoothStep(0f, 1f, Ramp(t, 0.06f, 0.16f)))
                    : 1f - Mathf.SmoothStep(0f, 1f, Ramp(t, 0.22f, meleeTime)));
            }

            if (model != null)
            {
                float sprintPose = sprintBlend * (1f - reloadPose) * (1f - Mathf.Clamp01(strike));
                Vector2 bob = player.BobOffset;

                Vector3 pos = Vector3.Lerp(hipPosition, adsPosition, aim);
                pos += new Vector3(0f, 0f, -0.035f * kick);
                pos += new Vector3(bob.x * 0.6f, -bob.y * 0.5f, 0f);
                pos += new Vector3(0.05f, -0.06f, -0.05f) * sprintPose;
                pos += new Vector3(-0.055f, -0.02f, -0.035f) * reloadPose;       // brought in toward the chest
                pos += new Vector3(0f, 0.014f * springPos, -0.012f * boltPull);  // bounce, and tugged back by the rack
                pos += new Vector3(-0.10f, 0.03f, 0.20f) * strike;
                pos += new Vector3(0.04f, -0.34f, -0.05f) * busyBlend;           // hands are busy: gun drops out of view
                model.localPosition = pos;

                // Canted so the magazine well faces the support hand; rolled back upright to work the bolt.
                model.localRotation = Quaternion.Euler(
                    -1.5f * kick * (1f - 0.6f * aim) + 12f * sprintPose - 15f * reloadPose + 5f * rackTilt - 2.6f * springPos - 10f * strike + 30f * busyBlend,
                    -35f * sprintPose - 9f * reloadPose - 60f * strike,
                    (-30f + 17f * rackTilt + 4f * pullTwist) * reloadPose + 35f * strike);
            }

            if (magazine != null)
            {
                magazine.localPosition = magRest + magOffset;
                magazine.localRotation = magRestRotation * Quaternion.Euler(magTilt, 0f, -magTilt * 0.4f);
                if (magazine.gameObject.activeSelf != magVisible) magazine.gameObject.SetActive(magVisible);
            }
            if (bolt != null) bolt.localPosition = boltRest + new Vector3(0f, 0f, -0.045f) * boltPull;
            PoseArm(leftArm, Shoulder, hand);

            if (muzzleFlash != null)
            {
                flashTimer -= dt;
                bool on = flashTimer > 0f;
                if (muzzleFlash.activeSelf != on) muzzleFlash.SetActive(on);
            }
        }

        /// <summary>Stretch a forearm block from the shoulder to wherever the hand needs to be.</summary>
        void PoseArm(Transform arm, Vector3 shoulder, Vector3 hand)
        {
            if (arm == null) return;
            Vector3 along = hand - shoulder;
            float length = along.magnitude;
            if (length < 0.001f) return;
            arm.localPosition = shoulder + along * 0.5f;
            arm.localRotation = Quaternion.LookRotation(along / length, Vector3.up);
            arm.localScale = new Vector3(armThickness.x, armThickness.y, length);
        }
    }
}
