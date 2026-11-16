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
        public float reloadTime = 1.8f;        // magazine swap with a round still chambered
        public float emptyReloadTime = 2.4f;   // magazine swap plus charging the bolt

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

        // The reload animation is authored on this timeline (seconds) and stretched to the reload time.
        const float TiltEnd = 0.25f, MagOutEnd = 0.60f, MagInStart = 0.85f, MagSeat = 1.25f;
        const float BoltStart = 1.40f, BoltEnd = 1.85f, ReturnTime = 0.30f;
        const float TacticalLength = 1.70f, EmptyLength = 2.30f;

        float bloom, nextFireTime, lastShotTime = -10f, flashTimer;
        float kick, sprintBlend, reloadPose, busyBlend;
        float reloadElapsed, meleeElapsed;
        bool reloadFromEmpty, magSeated, meleeHitDone;
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
            if (magazine != null) magRest = magazine.localPosition;
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

            UpdateMelee(inp, dt);
            UpdateReload(inp, dt);

            bool busy = player.HandsBusy;
            bool wantsAim = inp.aim && !player.IsSprinting && !IsReloading && !IsMeleeing && !busy;
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
                }
                return;
            }

            reloadElapsed += dt;
            float u = ReloadTimelinePosition();
            if (!magSeated && u >= MagSeat)
            {
                // The rounds only count once the new magazine clicks in.
                magSeated = true;
                Ammo = magazineSize;
            }
            if (u >= (reloadFromEmpty ? EmptyLength : TacticalLength)) IsReloading = false;
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
                softWall.Punch(point, dir, 0.26f);
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
                MeleeHits++;
            }
        }

        // ------------------------------------------------------------------ pose + animation

        static float Ramp(float t, float start, float end)
        {
            return Mathf.Clamp01((t - start) / Mathf.Max(0.0001f, end - start));
        }

        void UpdatePose(float dt, float aim)
        {
            kick = Mathf.MoveTowards(kick, 0f, dt * 9f);

            // Reload: cant the gun, drop the magazine, bring a new one up, seat it, charge the bolt if it ran dry.
            float poseTarget = 0f, magOut = 0f, seatBump = 0f, boltPull = 0f;
            bool magVisible = true;
            if (IsReloading)
            {
                float u = ReloadTimelinePosition();
                float length = reloadFromEmpty ? EmptyLength : TacticalLength;
                poseTarget = Mathf.SmoothStep(0f, 1f, Ramp(u, 0f, TiltEnd)) * (1f - Mathf.SmoothStep(0f, 1f, Ramp(u, length - ReturnTime, length)));

                if (u < TiltEnd) magOut = 0f;
                else if (u < MagOutEnd) { float k = Ramp(u, TiltEnd, MagOutEnd); magOut = k * k; }            // falls away, accelerating
                else if (u < MagInStart) { magOut = 1f; magVisible = false; }                                // fresh magazine is off screen
                else if (u < MagSeat) { float k = 1f - Ramp(u, MagInStart, MagSeat); magOut = k * k; }       // pushed up, slowing into the well
                seatBump = u >= MagSeat ? 1f - Ramp(u, MagSeat, MagSeat + 0.12f) : 0f;

                if (reloadFromEmpty && u > BoltStart && u < BoltEnd)
                {
                    float k = Ramp(u, BoltStart, BoltEnd);
                    boltPull = k < 0.6f ? Mathf.SmoothStep(0f, 1f, k / 0.6f) : 1f - Ramp(k, 0.6f, 0.75f);   // pull back, then let it snap home
                }
            }
            reloadPose = Mathf.MoveTowards(reloadPose, poseTarget, dt * 7f);
            busyBlend = Mathf.MoveTowards(busyBlend, player.HandsBusy ? 1f : 0f, dt * 6f);

            // Melee: a short wind-up, a fast thrust, a slower recovery.
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
                pos += new Vector3(-0.045f, -0.035f, -0.03f) * reloadPose;
                pos += new Vector3(0f, 0.012f, 0f) * seatBump;
                pos += new Vector3(-0.10f, 0.03f, 0.20f) * strike;
                pos += new Vector3(0.04f, -0.34f, -0.05f) * busyBlend; // hands are busy: gun drops out of view
                model.localPosition = pos;

                model.localRotation = Quaternion.Euler(
                    -1.5f * kick * (1f - 0.6f * aim) + 12f * sprintPose - 16f * reloadPose - 10f * strike + 30f * busyBlend,
                    -35f * sprintPose - 8f * reloadPose - 60f * strike,
                    -32f * reloadPose + 35f * strike);
            }

            Vector3 magOffset = new Vector3(0f, -0.26f, -0.02f) * magOut;
            if (magazine != null)
            {
                magazine.localPosition = magRest + magOffset;
                if (magazine.gameObject.activeSelf != magVisible) magazine.gameObject.SetActive(magVisible);
            }
            if (bolt != null) bolt.localPosition = boltRest + new Vector3(0f, 0f, -0.045f) * boltPull;
            // The support hand follows the magazine down and back up, then reaches for the bolt.
            if (leftArm != null) leftArm.localPosition = armRest + magOffset * 0.9f + new Vector3(0.03f, 0.07f, -0.10f) * boltPull;

            if (muzzleFlash != null)
            {
                flashTimer -= dt;
                bool on = flashTimer > 0f;
                if (muzzleFlash.activeSelf != on) muzzleFlash.SetActive(on);
            }
        }
    }
}
