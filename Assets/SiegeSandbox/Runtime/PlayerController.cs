using UnityEngine;
using UnityEngine.InputSystem;

namespace SiegeSandbox
{
    public enum Stance { Stand, Crouch, Prone }

    /// <summary>One frame of player intent. Filled from keyboard/mouse, or directly by tests.</summary>
    [System.Serializable]
    public struct PlayerInputState
    {
        public Vector2 move;        // x = strafe, y = forward
        public Vector2 look;        // mouse delta in pixels
        public bool sprint;
        public bool crouchPressed;  // true only on the frame the key goes down
        public bool pronePressed;
        public float lean;          // -1 = left (Q), +1 = right (E)
        public bool fire;
        public bool aim;
        public bool reloadPressed;
        public bool jumpPressed;
        public bool meleePressed;
        public bool interactHeld;   // barricading needs the key held down
    }

    /// <summary>
    /// First-person controller with Siege-style stances and leaning, plus sprint and jump.
    /// Standing, crouching, going prone and leaning all reshape the player's hitbox.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public const int PlayerLayer = 8;
        public const int HitboxLayer = 9;

        /// <summary>Everything bullets and lean/stance checks can collide with (not the player).</summary>
        public static int WorldMask
        {
            get { return ~((1 << PlayerLayer) | (1 << HitboxLayer) | (1 << 2)); }
        }

        public static PlayerController Instance { get; private set; }

        [Header("Rig")]
        public Transform leanPivot;
        public Transform cameraPivot;
        public Camera cam;
        public Transform legsHitbox;
        public Transform torsoHitbox;
        public Transform headHitbox;

        [Header("Input")]
        public bool useDeviceInput = true;
        public bool toggleLean = true;   // Siege default: tap Q/E to lean, tap again to straighten
        public float mouseSensitivity = 0.08f;
        public float adsSensitivityScale = 0.65f;
        public PlayerInputState input;

        [Header("Movement (m/s)")]
        public float walkSpeed = 4.2f;
        public float sprintSpeed = 6.8f;
        public float crouchSpeed = 2.4f;
        public float proneSpeed = 1.0f;
        public float adsMoveScale = 0.6f;
        public float leanMoveScale = 0.85f;
        public float acceleration = 40f;
        public float gravity = -22f;

        [Header("Jump")]
        public float jumpHeight = 1.0f;
        public float airAcceleration = 12f;
        public float coyoteTime = 0.1f;

        [Header("Vault")]
        public float vaultMinHeight = 0.35f;
        public float vaultMaxHeight = 1.3f;
        public float vaultReach = 0.8f;
        public float vaultMaxDepth = 1.4f;
        public float vaultSpeed = 4.5f;        // m/s along the climb

        [Header("Stances (m)")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.15f;
        public float proneHeight = 0.6f;
        public float standEye = 1.62f;
        public float crouchEye = 1.0f;
        public float proneEye = 0.38f;
        public float stanceSpeed = 3.2f;

        [Header("Lean")]
        // One angle for everything: the view, the gun and the hitbox all tilt together.
        public float leanAngle = 9f;
        public float leanShift = 0.14f;        // the shoulders slide sideways as well as tilting
        public float leanSmoothTime = 0.13f;

        public Stance Stance { get; private set; }
        /// <summary>0 = hip, 1 = fully aimed down sights. Written by the weapon.</summary>
        public float AimBlend { get; set; }
        /// <summary>Current lean, -1 (full left) to +1 (full right).</summary>
        public float Lean { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsGrounded { get; private set; }
        /// <summary>True while a timed action (building a barricade) has the player's hands full.</summary>
        public bool IsBusy { get; set; }
        /// <summary>True while climbing over cover or through a window.</summary>
        public bool IsVaulting { get; private set; }
        /// <summary>The weapon cannot be used: mid-vault or mid-action.</summary>
        public bool HandsBusy { get { return IsBusy || IsVaulting; } }
        /// <summary>How far the view is rolled by the lean, in degrees (positive = right).</summary>
        public float LeanRollDegrees { get { return Lean * CurrentLeanAngle(); } }
        /// <summary>How fast the lean is changing, in lean units per second. Shooting mid-lean is less accurate.</summary>
        public float LeanChangeRate { get; private set; }
        /// <summary>Walk/sprint bob in metres (x = sideways, y = up). The weapon reads this too.</summary>
        public Vector2 BobOffset { get; private set; }
        /// <summary>Look pitch in degrees, positive = up.</summary>
        public float Pitch { get; private set; }
        public float Yaw { get; private set; }
        public float CurrentHeight { get; private set; }
        public float CurrentEyeHeight { get; private set; }
        public float PlanarSpeed { get { return new Vector3(velocity.x, 0f, velocity.z).magnitude; } }

        CharacterController cc;
        Collider legsCol, torsoCol, headCol;
        Vector3 velocity;
        Vector2 recoilPending;      // x = yaw right, y = pitch up
        float leanToggleState;
        bool fireBlockedUntilRelease;

        /// <summary>
        /// True once the player has clicked into the game and it owns the mouse. Tracked here
        /// rather than read back from Cursor.lockState, which the editor can report as locked
        /// before the Game view has really taken the pointer.
        /// </summary>
        public bool HasMouse { get; private set; }
        /// <summary>The last raw mouse movement read from the device, for the on-screen input readout.</summary>
        public Vector2 RawMouseDelta { get; private set; }
        float lastGroundedTime = -10f, lastJumpTime = -10f, jumpBufferedUntil = -1f;
        bool wasGrounded = true;
        float landDip, bobPhase, bobWeight;
        float leanVelocity, stepTimer, lastLeanTarget;
        Vector3 vaultFrom, vaultControl, vaultTo, vaultDirection;
        float vaultElapsed, vaultDuration, jumpStartY;

        void Awake()
        {
            Instance = this;
            cc = GetComponent<CharacterController>();
            // Debris and fallen dummies never block the player.
            Physics.IgnoreLayerCollision(PlayerLayer, Prims.IgnoreRaycastLayer, true);
            if (legsHitbox != null) legsCol = legsHitbox.GetComponent<Collider>();
            if (torsoHitbox != null) torsoCol = torsoHitbox.GetComponent<Collider>();
            if (headHitbox != null) headCol = headHitbox.GetComponent<Collider>();
            Yaw = transform.eulerAngles.y;
            CurrentHeight = standHeight;
            CurrentEyeHeight = standEye;
            ApplyRig();
        }

        void Start()
        {
            Sfx.Warm(); // build the sounds now rather than on the first shot
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (useDeviceInput) ReadDeviceInput();
            if (IsVaulting)
            {
                UpdateVault(dt);
                UpdateLook();
                UpdateLean(dt);
                ApplyRig();
                return;
            }
            UpdateStance(dt);
            UpdateLook();
            UpdateMove(dt);
            UpdateLean(dt);
            ApplyRig();
        }

        // ------------------------------------------------------------------ input

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void ReadDeviceInput()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            PlayerInputState s = new PlayerInputState();

            // The game takes the mouse on a click and gives it back on Escape or when the window loses focus.
            if (HasMouse && ((kb != null && kb.escapeKey.wasPressedThisFrame) || !Application.isFocused))
            {
                HasMouse = false;
                LockCursor(false);
            }
            else if (!HasMouse && mouse != null && mouse.leftButton.wasPressedThisFrame && Application.isFocused)
            {
                HasMouse = true;
                fireBlockedUntilRelease = true; // the click that grabs the mouse is not a shot
            }
            // Keep asserting the lock: the editor drops it on its own in several situations.
            if (HasMouse && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
            bool locked = HasMouse;

            if (kb != null && locked)
            {
                s.move = new Vector2(
                    (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                    (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
                s.sprint = kb.leftShiftKey.isPressed;
                s.crouchPressed = kb.cKey.wasPressedThisFrame;
                s.pronePressed = kb.leftCtrlKey.wasPressedThisFrame || kb.rightCtrlKey.wasPressedThisFrame;
                s.reloadPressed = kb.rKey.wasPressedThisFrame;
                s.jumpPressed = kb.spaceKey.wasPressedThisFrame;
                s.meleePressed = kb.vKey.wasPressedThisFrame;
                s.interactHeld = kb.fKey.isPressed;

                if (toggleLean)
                {
                    if (kb.qKey.wasPressedThisFrame) leanToggleState = leanToggleState < 0f ? 0f : -1f;
                    if (kb.eKey.wasPressedThisFrame) leanToggleState = leanToggleState > 0f ? 0f : 1f;
                    s.lean = leanToggleState;
                }
                else
                {
                    s.lean = (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);
                }
            }

            RawMouseDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            if (mouse != null && locked)
            {
                s.look = RawMouseDelta;
                s.aim = mouse.rightButton.isPressed;
                if (fireBlockedUntilRelease && !mouse.leftButton.isPressed) fireBlockedUntilRelease = false;
                s.fire = mouse.leftButton.isPressed && !fireBlockedUntilRelease;
            }

            input = s;
        }

        // ------------------------------------------------------------------ stances

        public float HeightFor(Stance s)
        {
            return s == Stance.Stand ? standHeight : (s == Stance.Crouch ? crouchHeight : proneHeight);
        }

        public float EyeFor(Stance s)
        {
            return s == Stance.Stand ? standEye : (s == Stance.Crouch ? crouchEye : proneEye);
        }

        bool HasHeadroom(float height)
        {
            return HasRoomAt(transform.position, height);
        }

        /// <summary>Would a body of this height fit standing at this spot?</summary>
        bool HasRoomAt(Vector3 feet, float height)
        {
            float r = cc.radius * 0.9f;
            Vector3 bottom = feet + Vector3.up * (r + 0.1f);
            Vector3 top = feet + Vector3.up * Mathf.Max(height - r, r + 0.11f);
            return !Physics.CheckCapsule(bottom, top, r, WorldMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Change stance if there is room to do so. Returns true if the requested stance was taken.</summary>
        public bool TrySetStance(Stance s)
        {
            if (HeightFor(s) > CurrentHeight + 0.01f && !HasHeadroom(HeightFor(s)))
            {
                // No room to stand: settle for a crouch when coming up from prone.
                if (s == Stance.Stand && Stance == Stance.Prone && HasHeadroom(crouchHeight)) Stance = Stance.Crouch;
                return false;
            }
            if (Stance != s) Sfx.PlayLocal(SoundId.Cloth, s == Stance.Prone || Stance == Stance.Prone ? 0.6f : 0.4f); // kit shifting
            Stance = s;
            return true;
        }

        void UpdateStance(float dt)
        {
            if (input.crouchPressed) TrySetStance(Stance == Stance.Crouch ? Stance.Stand : Stance.Crouch);
            if (input.pronePressed) TrySetStance(Stance == Stance.Prone ? Stance.Stand : Stance.Prone);

            bool wantsSprint = input.sprint && input.move.y > 0.1f;
            if (wantsSprint && Stance != Stance.Stand) TrySetStance(Stance.Stand);

            BlendStanceHeight(dt);
        }

        void BlendStanceHeight(float dt)
        {
            CurrentHeight = Mathf.MoveTowards(CurrentHeight, HeightFor(Stance), stanceSpeed * dt);
            CurrentEyeHeight = Mathf.MoveTowards(CurrentEyeHeight, EyeFor(Stance), stanceSpeed * 1.05f * dt);
        }

        // ------------------------------------------------------------------ look + recoil

        /// <summary>Kick the view. Positive pitch = up, positive yaw = right. Applied over a few frames.</summary>
        public void AddRecoil(float pitchUp, float yawRight)
        {
            recoilPending += new Vector2(yawRight, pitchUp);
        }

        void UpdateLook()
        {
            float sens = mouseSensitivity * Mathf.Lerp(1f, adsSensitivityScale, AimBlend);
            Yaw += input.look.x * sens;
            Pitch += input.look.y * sens;

            Vector2 step = recoilPending * Mathf.Min(1f, Time.deltaTime * 30f);
            recoilPending -= step;
            Yaw += step.x;
            Pitch += step.y;

            Pitch = Mathf.Clamp(Pitch, -80f, 85f);
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
            // The camera hangs off the lean pivot, so what you see is exactly what the hitbox does.
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(-Pitch, 0f, 0f);
        }

        // ------------------------------------------------------------------ movement

        void UpdateMove(float dt)
        {
            Vector2 m = Vector2.ClampMagnitude(input.move, 1f);
            Vector3 wish = transform.right * m.x + transform.forward * m.y;

            bool grounded = cc.isGrounded;
            if (grounded) lastGroundedTime = Time.time;
            if (input.jumpPressed) jumpBufferedUntil = Time.time + 0.12f;

            float vy = velocity.y;
            if (grounded && vy < 0f) vy = -2f;

            // Jump: a short buffer and a little coyote time make it feel responsive.
            if (Time.time <= jumpBufferedUntil && Time.time - lastGroundedTime <= coyoteTime && Time.time - lastJumpTime > 0.3f)
            {
                jumpBufferedUntil = -1f;
                if (Stance == Stance.Prone)
                {
                    TrySetStance(Stance.Stand); // from prone, Space just gets you up
                }
                else if (Stance == Stance.Stand || TrySetStance(Stance.Stand))
                {
                    vy = Mathf.Sqrt(2f * jumpHeight * -gravity);
                    jumpStartY = transform.position.y;
                    Sfx.PlayLocal(SoundId.Jump, 0.6f);
                    Sfx.PlayLocal(SoundId.GearRattle, 0.5f);
                    lastJumpTime = Time.time;
                    lastGroundedTime = -10f;
                    grounded = false;
                }
            }

            // Aiming or firing drops you out of a sprint.
            bool wantsSprint = input.sprint && m.y > 0.1f;
            IsSprinting = wantsSprint && Stance == Stance.Stand && !input.aim && !input.fire && !IsBusy
                && AimBlend < 0.1f && Mathf.Abs(input.lean) < 0.1f;

            float speed = Stance == Stance.Prone ? proneSpeed : (Stance == Stance.Crouch ? crouchSpeed : walkSpeed);
            if (IsSprinting) speed = sprintSpeed;
            speed *= Mathf.Lerp(1f, adsMoveScale, AimBlend);
            speed *= Mathf.Lerp(1f, leanMoveScale, Mathf.Abs(Lean));

            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
            planar = Vector3.MoveTowards(planar, wish * speed, (grounded ? acceleration : airAcceleration) * dt);

            vy += gravity * dt;
            velocity = new Vector3(planar.x, vy, planar.z);
            CollisionFlags flags = cc.Move(velocity * dt);
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f; // bumped a ceiling

            // Space always jumps. If that jump carries you into cover while you hold forward, climb over it.
            if (!cc.isGrounded && (flags & CollisionFlags.Sides) != 0 && m.y > 0.3f
                && Time.time - lastJumpTime < 0.7f && TryStartVault(jumpStartY))
                return;

            bool nowGrounded = cc.isGrounded;
            if (nowGrounded && !wasGrounded && vy < -3f)
            {
                landDip = Mathf.Clamp(-vy * 0.012f, 0f, 0.12f);
                Sfx.PlayLocal(SoundId.Land, Mathf.Clamp01(-vy / 9f));
                Sfx.PlayLocal(SoundId.GearRattle, Mathf.Clamp01(-vy / 9f) * 0.7f, 0.95f, 0.03f);
            }
            wasGrounded = nowGrounded;
            IsGrounded = nowGrounded;
            landDip = Mathf.MoveTowards(landDip, 0f, dt * 0.5f);

            // Head bob follows distance travelled, so it speeds up when sprinting.
            float planarSpeed = PlanarSpeed;
            bobWeight = Mathf.MoveTowards(bobWeight, nowGrounded ? Mathf.Clamp01(planarSpeed / walkSpeed) : 0f, dt * 5f);
            bobPhase += planarSpeed * dt * 2.6f;
            float amp = (IsSprinting ? 0.035f : 0.014f) * Mathf.Lerp(1f, 0.15f, AimBlend) * (Stance == Stance.Prone ? 0.5f : 1f);
            BobOffset = new Vector2(Mathf.Cos(bobPhase * 0.5f), Mathf.Sin(bobPhase)) * (amp * bobWeight);

            // Footsteps: how often and how loud depends on how you are moving. Crouching is close to silent.
            float stepInterval = Stance == Stance.Prone ? 0.75f : (Stance == Stance.Crouch ? 0.55f : (IsSprinting ? 0.30f : 0.42f));
            if (nowGrounded && planarSpeed > 0.6f)
            {
                stepTimer += dt;
                if (stepTimer >= stepInterval)
                {
                    stepTimer = 0f;
                    if (Stance == Stance.Prone) Sfx.PlayLocal(SoundId.Crawl, 0.35f);
                    else if (IsSprinting)
                    {
                        Sfx.PlayLocal(SoundId.Footstep, 1f, 1.05f);
                        Sfx.PlayLocal(SoundId.GearRattle, 0.4f, 1f, 0.04f); // running is loud: boots and everything strapped to you
                    }
                    else if (Stance == Stance.Crouch) Sfx.PlayLocal(SoundId.Footstep, 0.2f, 0.9f);
                    else Sfx.PlayLocal(SoundId.Footstep, Mathf.Lerp(0.6f, 0.35f, AimBlend), 0.95f);
                }
            }
            else
            {
                stepTimer = stepInterval * 0.6f; // the first step lands soon after you set off
            }
        }

        // ------------------------------------------------------------------ vault

        /// <summary>
        /// Climb over the obstacle the player has just jumped into: across to the far side, or up
        /// onto it when it is too deep to cross. Heights are measured from where the jump started.
        /// </summary>
        bool TryStartVault(float groundY)
        {
            Vector3 feet = new Vector3(transform.position.x, groundY, transform.position.z);
            int mask = WorldMask;
            const QueryTriggerInteraction NoTriggers = QueryTriggerInteraction.Ignore;

            // Something solid in front at shin-to-waist height, and we are facing it.
            RaycastHit front;
            if (!Physics.SphereCast(feet + Vector3.up * 0.45f, 0.15f, transform.forward, out front, vaultReach, mask, NoTriggers)) return false;
            Vector3 into = -front.normal;
            into.y = 0f;
            if (into.sqrMagnitude < 0.01f) return false;
            into.Normalize();
            if (Vector3.Dot(into, transform.forward) < 0.6f) return false;

            // How tall is it? Probe straight down just past its front face.
            Vector3 edge = front.point;
            edge.y = feet.y;
            RaycastHit top;
            float probeHeight = vaultMaxHeight + 0.4f;
            if (!Physics.Raycast(edge + into * 0.12f + Vector3.up * probeHeight, Vector3.down, out top, probeHeight, mask, NoTriggers)) return false;
            float h = top.point.y - feet.y;
            if (h < vaultMinHeight || h > vaultMaxHeight) return false;

            // Find the floor on the far side; if the obstacle is too deep, climb onto it instead.
            Vector3 landing = Vector3.zero;
            bool found = false;
            for (float d = 0.5f; d <= vaultMaxDepth + 0.6f; d += 0.15f)
            {
                RaycastHit ground;
                Vector3 above = edge + into * d + Vector3.up * (h + 0.3f);
                if (!Physics.Raycast(above, Vector3.down, out ground, h + 1.8f, mask, NoTriggers)) continue;
                if (ground.point.y > feet.y + h - 0.25f) continue; // still on top of the obstacle
                if (!HasRoomAt(ground.point, crouchHeight)) continue; // too close to its back face
                landing = ground.point;
                found = true;
                break;
            }
            if (!found)
            {
                Vector3 onTop = edge + into * 0.45f;
                onTop.y = top.point.y;
                if (!HasRoomAt(onTop, crouchHeight)) return false;
                landing = onTop;
            }

            // The slot above the obstacle has to be clear all the way across (a barricaded window is not).
            float slotY = Mathf.Max(top.point.y, landing.y) + 0.35f;
            Vector3 a = new Vector3(feet.x, slotY, feet.z);
            Vector3 b = new Vector3(landing.x, slotY, landing.z);
            RaycastHit blocked;
            if (Physics.SphereCast(a, 0.22f, (b - a).normalized, out blocked, (b - a).magnitude, mask, NoTriggers)) return false;

            // Stay crouched over open cover; only tuck right down when the gap is low, like a window.
            Vector3 onLedge = edge + into * 0.12f;
            onLedge.y = top.point.y + 0.02f;
            bool lowSlot = !HasRoomAt(onLedge, crouchHeight);

            Vector3 from = transform.position;
            Vector3 mid = (from + landing) * 0.5f;
            mid.y = Mathf.Max(top.point.y, landing.y) + 0.1f;
            vaultFrom = from;
            vaultTo = landing;
            vaultControl = 2f * mid - 0.5f * (from + landing); // quadratic curve that passes through 'mid'
            vaultDirection = into;
            vaultDuration = Mathf.Clamp((Vector3.Distance(from, mid) + Vector3.Distance(mid, landing)) / Mathf.Max(0.5f, vaultSpeed), 0.3f, 0.55f);
            vaultElapsed = 0f;
            IsVaulting = true;
            IsSprinting = false;
            Stance = lowSlot ? Stance.Prone : Stance.Crouch;
            cc.enabled = false;
            Sfx.PlayLocal(SoundId.Cloth, 0.7f, 0.85f);
            return true;
        }

        void UpdateVault(float dt)
        {
            vaultElapsed += dt;
            float t = Mathf.Clamp01(vaultElapsed / vaultDuration);
            float k = t * (1.5f - 0.5f * t); // quick off the ground, easing into the landing
            float u = 1f - k;
            transform.position = u * u * vaultFrom + 2f * u * k * vaultControl + k * k * vaultTo;
            BlendStanceHeight(dt);

            if (t >= 1f)
            {
                transform.position = vaultTo + Vector3.up * 0.02f;
                cc.enabled = true;
                IsVaulting = false;
                lastGroundedTime = Time.time;
                wasGrounded = true;
                Stance = Stance.Crouch;
                TrySetStance(Stance.Stand);
                Sfx.PlayLocal(SoundId.Land, 0.45f);
                // Keep moving if the player is still pushing forward, so the climb flows into a walk.
                velocity = input.move.y > 0.1f ? vaultDirection * walkSpeed : Vector3.zero;
            }
        }

        /// <summary>Move the player instantly (respawns, tests). Clears momentum.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            IsVaulting = false;
            cc.enabled = false;
            transform.position = position;
            cc.enabled = true;
            velocity = Vector3.zero;
            Yaw = yaw;
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
        }

        // ------------------------------------------------------------------ lean

        void UpdateLean(float dt)
        {
            // Sprinting or leaving the ground straightens you up (and clears a toggled lean).
            bool airborne = Time.time - lastGroundedTime > 0.15f;
            bool upright = IsSprinting || airborne || IsVaulting;
            if (upright) leanToggleState = 0f;
            float target = upright ? 0f : Mathf.Clamp(input.lean, -1f, 1f);

            if (Mathf.Abs(target) > 0.01f)
            {
                // Do not lean the head into a wall: sweep from the upright head to the leaned head.
                float side = Mathf.Sign(target);
                float waist = CurrentEyeHeight * 0.55f;
                float upper = CurrentEyeHeight - waist;
                Vector3 upright0 = transform.TransformPoint(new Vector3(0f, CurrentEyeHeight, 0f));
                Vector3 leanedLocal = new Vector3(side * CurrentLeanShift(), waist, 0f)
                    + Quaternion.Euler(0f, 0f, -side * CurrentLeanAngle()) * new Vector3(0f, upper, 0f);
                Vector3 leaned = transform.TransformPoint(leanedLocal);
                Vector3 delta = leaned - upright0;
                float dist = delta.magnitude;
                RaycastHit hit;
                if (dist > 0.001f && Physics.SphereCast(upright0, 0.14f, delta / dist, out hit, dist, WorldMask, QueryTriggerInteraction.Ignore))
                    target *= Mathf.Clamp01(hit.distance / dist);
            }

            if (Mathf.Abs(target - lastLeanTarget) > 0.5f) Sfx.PlayLocal(SoundId.Cloth, 0.2f, 1.15f);
            lastLeanTarget = target;
            Lean = Mathf.SmoothDamp(Lean, target, ref leanVelocity, leanSmoothTime, Mathf.Infinity, dt);
            LeanChangeRate = Mathf.Abs(leanVelocity);
        }

        float CurrentLeanShift()
        {
            float proneT = Mathf.InverseLerp(crouchHeight, proneHeight, CurrentHeight);
            return leanShift * Mathf.Lerp(1f, 0.4f, proneT);
        }

        float CurrentLeanAngle()
        {
            // Lean less while prone: there is not much torso left to swing.
            float proneT = Mathf.InverseLerp(crouchHeight, proneHeight, CurrentHeight);
            return leanAngle * Mathf.Lerp(1f, 0.5f, proneT);
        }

        // ------------------------------------------------------------------ rig + hitbox

        void ApplyRig()
        {
            cc.height = CurrentHeight;
            cc.center = new Vector3(0f, CurrentHeight * 0.5f, 0f);

            float waist = CurrentEyeHeight * 0.55f;
            float upper = CurrentEyeHeight - waist;

            if (leanPivot != null)
            {
                leanPivot.localPosition = new Vector3(Lean * CurrentLeanShift(), waist, 0f);
                leanPivot.localRotation = Quaternion.Euler(0f, 0f, -Lean * CurrentLeanAngle());
            }
            if (cameraPivot != null)
                cameraPivot.localPosition = new Vector3(BobOffset.x * 0.5f, upper + BobOffset.y - landDip, 0f);

            if (legsHitbox == null || torsoHitbox == null || headHitbox == null) return;

            float proneT = Mathf.InverseLerp(crouchHeight, proneHeight, CurrentHeight);
            if (proneT < 0.5f)
            {
                // Upright: legs stay planted, torso and head swing with the lean pivot.
                legsHitbox.localPosition = new Vector3(0f, waist * 0.5f, 0f);
                legsHitbox.localRotation = Quaternion.identity;
                legsHitbox.localScale = new Vector3(0.42f, waist * 0.5f, 0.42f);

                float torso = upper + 0.02f;
                torsoHitbox.localPosition = new Vector3(0f, torso * 0.5f - 0.06f, 0f);
                torsoHitbox.localRotation = Quaternion.identity;
                torsoHitbox.localScale = new Vector3(0.48f, torso * 0.5f, 0.48f);

                headHitbox.localPosition = new Vector3(0f, torso + 0.06f, 0f);
                headHitbox.localScale = Vector3.one * 0.26f;
            }
            else
            {
                // Prone: the body lies flat behind the head, low but long.
                Quaternion flat = Quaternion.Euler(90f, 0f, 0f);
                legsHitbox.localPosition = new Vector3(0f, 0.2f, -1.15f);
                legsHitbox.localRotation = flat;
                legsHitbox.localScale = new Vector3(0.38f, 0.5f, 0.38f);

                torsoHitbox.localPosition = new Vector3(0f, 0.03f, -0.35f);
                torsoHitbox.localRotation = flat;
                torsoHitbox.localScale = new Vector3(0.44f, 0.35f, 0.44f);

                headHitbox.localPosition = new Vector3(0f, 0.12f, 0.08f);
                headHitbox.localScale = Vector3.one * 0.26f;
            }
        }

        /// <summary>World-space bounds of the three hitbox colliders combined.</summary>
        public Bounds GetHitboxBounds()
        {
            Physics.SyncTransforms();
            Bounds b = legsCol.bounds;
            b.Encapsulate(torsoCol.bounds);
            b.Encapsulate(headCol.bounds);
            return b;
        }
    }
}
