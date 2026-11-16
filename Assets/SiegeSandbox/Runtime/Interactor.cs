using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Barricading, Siege style. Hold the interact key at an open doorway or window: the
    /// barricade is hung from the top of the frame, unrolls down it and is nailed in place. Let
    /// go early and it comes away again. Hold the key at a standing barricade and it is levered
    /// off with a crowbar, all in one piece at the end. The weapon is lowered the whole time.
    /// </summary>
    [DefaultExecutionOrder(-5)] // after the player reads input, before the weapon decides whether it may fire
    public class Interactor : MonoBehaviour
    {
        public PlayerController player;
        public Transform hands;        // root of the two work arms, hidden unless busy
        public Transform toolArm;      // swings the hammer, levers the crowbar
        public GameObject hammer;
        public GameObject crowbar;
        public float reach = 2.3f;
        public float deployTime = 2.0f;
        public float removeTime = 1.5f;

        /// <summary>Text for the HUD, empty when there is nothing to interact with.</summary>
        public string Prompt { get; private set; }
        /// <summary>0..1 while an action is running.</summary>
        public float Progress { get; private set; }
        public bool IsBusy { get; private set; }

        static readonly float[] HammerBeats = { 0.08f, 0.2f, 0.55f, 0.8f, 0.93f };
        static readonly float[] PryBeats = { 0.15f, 0.5f, 0.85f };

        Barricade active;
        bool building, needRelease;
        float timer, pulse, handsBlend;
        int beat, lastBoards;

        void Awake()
        {
            Prompt = "";
            if (hands != null) hands.gameObject.SetActive(false);
        }

        void Update()
        {
            if (player == null || player.cam == null) return;
            float dt = Time.deltaTime;
            Transform eye = player.cam.transform;
            bool held = player.input.interactHeld;
            if (!held) needRelease = false;

            if (IsBusy)
            {
                bool tooFar = active == null || Vector3.Distance(active.ClosestPoint(eye.position), eye.position) > reach + 0.6f;
                if (!held || tooFar || (building && !HasRoomToBuild(active))) Abandon();
                else if (building) Deploy(dt);
                else Remove(dt, eye);
            }
            else
            {
                Barricade target = FindTarget(eye.position, eye.forward);
                if (target == null) Prompt = "";
                else if (target.IsBuilt) Prompt = "Hold F   remove barricade";
                else if (HasRoomToBuild(target)) Prompt = "Hold F   barricade";
                else Prompt = "Step back to barricade";

                if (target != null && held && !needRelease && (target.IsBuilt || HasRoomToBuild(target)))
                {
                    active = target;
                    building = !target.IsBuilt;
                    timer = 0f;
                    beat = 0;
                    lastBoards = 0;
                    Progress = 0f;
                    IsBusy = true;
                    Prompt = building ? "Barricading..." : "Removing barricade...";
                    Sfx.PlayLocal(SoundId.Cloth, 0.5f);
                }
            }

            player.IsBusy = IsBusy;
            AnimateHands(dt);
        }

        void Deploy(float dt)
        {
            timer += dt;
            Progress = Mathf.Clamp01(timer / deployTime);

            // It unrolls down the frame through the middle of the action...
            float unrolled = Mathf.InverseLerp(0.22f, 0.78f, Progress);
            int boards = Mathf.CeilToInt(unrolled * active.PlankCount);
            if (boards > lastBoards)
            {
                active.SetDeployed(boards);
                if (lastBoards == 0 || boards / 3 != lastBoards / 3)
                    Sfx.Play(SoundId.BarricadeUnroll, active.PlankWorldCenter(active.PlankCount - boards), 0.7f);
                lastBoards = boards;
            }

            // ...and is nailed at the top before and at the bottom after.
            while (beat < HammerBeats.Length && Progress >= HammerBeats[beat])
            {
                pulse = 1f;
                Sfx.Play(SoundId.Hammer, active.PlankWorldCenter(beat < 2 ? active.PlankCount - 1 : 0), 0.9f);
                beat++;
            }

            if (Progress >= 1f)
            {
                active.BuildAll();
                Finish();
            }
        }

        void Remove(float dt, Transform eye)
        {
            timer += dt;
            Progress = Mathf.Clamp01(timer / removeTime);
            while (beat < PryBeats.Length && Progress >= PryBeats[beat])
            {
                pulse = 1f;
                Sfx.Play(SoundId.Pry, active.ClosestPoint(eye.position), 0.8f, 0.9f + 0.1f * beat);
                beat++;
            }
            if (Progress >= 1f)
            {
                active.PryOff(eye.position - active.ClosestPoint(eye.position));
                Finish();
            }
        }

        void Finish()
        {
            needRelease = true; // holding on after the job is done must not start the opposite job
            Stop();
        }

        void Abandon()
        {
            // A barricade that is not nailed at the bottom does not stay up. Prying has no effect until it is done.
            if (building && active != null) active.Retract();
            needRelease = true;
            Stop();
        }

        void Stop()
        {
            IsBusy = false;
            Progress = 0f;
            active = null;
        }

        /// <summary>You cannot board up an opening you are standing in.</summary>
        bool HasRoomToBuild(Barricade b)
        {
            Vector3 local = b.transform.InverseTransformPoint(player.transform.position);
            bool inOpening = Mathf.Abs(local.x) < b.size.x * 0.5f + 0.3f && local.y < b.size.y;
            return !inOpening || Mathf.Abs(local.z) > 0.45f;
        }

        Barricade FindTarget(Vector3 eye, Vector3 forward)
        {
            Barricade best = null;
            float bestDist = reach;
            for (int i = 0; i < Barricade.All.Count; i++)
            {
                Barricade b = Barricade.All[i];
                Vector3 to = b.ClosestPoint(eye) - eye;
                float dist = to.magnitude;
                if (dist > bestDist) continue;
                if (dist > 0.6f && Vector3.Dot(forward, to / dist) < 0.4f) continue; // must be roughly looking at it
                best = b;
                bestDist = dist;
            }
            return best;
        }

        // ------------------------------------------------------------------ first-person arms

        void AnimateHands(float dt)
        {
            if (hands == null) return;
            handsBlend = Mathf.MoveTowards(handsBlend, IsBusy ? 1f : 0f, dt * 6f);
            pulse = Mathf.MoveTowards(pulse, 0f, dt * 7f);

            bool show = handsBlend > 0.01f;
            if (hands.gameObject.activeSelf != show) hands.gameObject.SetActive(show);
            if (!show) return;

            if (hammer != null && hammer.activeSelf != building) hammer.SetActive(building);
            if (crowbar != null && crowbar.activeSelf == building) crowbar.SetActive(!building);

            // Arms come up from below the view. Hammer: cocked back, snapping down on each nail.
            // Crowbar: pushed in, then hauled back on each heave.
            hands.localPosition = new Vector3(0f, Mathf.Lerp(-0.45f, 0f, Mathf.SmoothStep(0f, 1f, handsBlend)), 0f);
            if (toolArm == null) return;
            if (building) toolArm.localRotation = Quaternion.Euler(Mathf.Lerp(-48f, 14f, pulse), 0f, 0f);
            else toolArm.localRotation = Quaternion.Euler(Mathf.Lerp(-8f, -34f, pulse), Mathf.Lerp(0f, -14f, pulse), 0f);
        }
    }
}
