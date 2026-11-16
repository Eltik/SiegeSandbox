using System.Collections.Generic;
using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Barricading, Siege style: hold the interact key at an open doorway or window and the
    /// planks are carried up and hammered on one at a time, bottom to top. Hold it at a standing
    /// barricade to pull the planks off again. Letting go or walking away abandons the job, and
    /// a half-built barricade falls apart. The weapon is lowered the whole time.
    /// </summary>
    [DefaultExecutionOrder(-5)] // after the player reads input, before the weapon decides whether it may fire
    public class Interactor : MonoBehaviour
    {
        public PlayerController player;
        public Transform hands;        // root of the two work arms, hidden unless busy
        public Transform hammerArm;    // swings on each plank
        public float reach = 2.3f;
        public float plankInterval = 0.16f;   // seconds between planks going up
        public float removeInterval = 0.09f;  // seconds between planks coming off
        public float carryTime = 0.14f;       // how long a plank takes to travel from hands to wall

        /// <summary>Text for the HUD, empty when there is nothing to interact with.</summary>
        public string Prompt { get; private set; }
        /// <summary>0..1 while an action is running.</summary>
        public float Progress { get; private set; }
        public bool IsBusy { get; private set; }

        struct Carry
        {
            public Transform plank;
            public Vector3 from;
            public Quaternion fromRotation;
            public Vector3 to;
            public float elapsed;
        }

        readonly List<Carry> carries = new List<Carry>();
        Barricade active;
        bool building, needRelease;
        float timer, hammerPulse, handsBlend;
        int launched, removed;

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

            UpdateCarries(dt);

            if (IsBusy)
            {
                bool tooFar = active == null || Vector3.Distance(active.ClosestPoint(eye.position), eye.position) > reach + 0.6f;
                if (!held || tooFar || (building && !HasRoomToBuild(active)))
                {
                    Abandon();
                }
                else if (building)
                {
                    timer += dt;
                    while (launched < active.PlankCount && timer >= launched * plankInterval)
                    {
                        Launch(launched, eye);
                        launched++;
                    }
                    Progress = active.PlanksPlaced / (float)active.PlankCount;
                    if (active.IsComplete && carries.Count == 0) Finish();
                }
                else
                {
                    timer += dt;
                    while (active.PlanksPlaced > 0 && timer >= 0.15f + removed * removeInterval)
                    {
                        active.RemoveTopPlank(eye.position - active.ClosestPoint(eye.position));
                        removed++;
                        hammerPulse = 1f;
                    }
                    Progress = 1f - active.PlanksPlaced / (float)active.PlankCount;
                    if (active.PlanksPlaced == 0) Finish();
                }
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
                    launched = 0;
                    removed = 0;
                    Progress = 0f;
                    IsBusy = true;
                    Prompt = building ? "Barricading..." : "Removing barricade...";
                }
            }

            player.IsBusy = IsBusy;
            AnimateHands(dt);
        }

        // ------------------------------------------------------------------ planks in flight

        void Launch(int index, Transform eye)
        {
            Carry c = new Carry();
            c.from = eye.position + eye.forward * 0.45f - eye.up * 0.28f;
            c.fromRotation = eye.rotation * Quaternion.Euler(20f, 0f, Random.Range(-12f, 12f));
            c.to = active.PlankWorldCenter(index);
            GameObject plank = Prims.Cube("CarriedPlank", null, c.from, active.PlankSize, active.plankMaterial, false);
            plank.transform.rotation = c.fromRotation;
            c.plank = plank.transform;
            carries.Add(c);
        }

        void UpdateCarries(float dt)
        {
            for (int i = 0; i < carries.Count; i++)
            {
                Carry c = carries[i];
                c.elapsed += dt;
                float t = Mathf.Clamp01(c.elapsed / Mathf.Max(0.01f, carryTime));
                float k = t * t; // accelerates into the wall, like being slapped on
                if (c.plank != null)
                {
                    c.plank.position = Vector3.Lerp(c.from, c.to, k);
                    if (active != null) c.plank.rotation = Quaternion.Slerp(c.fromRotation, active.transform.rotation, k);
                }
                carries[i] = c;

                if (t >= 1f)
                {
                    // Arrived: it becomes part of the barricade and the hammer comes down on it.
                    if (c.plank != null) Destroy(c.plank.gameObject);
                    if (active != null && building) active.PlaceNextPlank();
                    hammerPulse = 1f;
                    carries.RemoveAt(i);
                    i--;
                }
            }
        }

        void DropCarries()
        {
            for (int i = 0; i < carries.Count; i++)
                if (carries[i].plank != null) Destroy(carries[i].plank.gameObject);
            carries.Clear();
        }

        // ------------------------------------------------------------------ outcomes

        void Finish()
        {
            needRelease = true; // holding on after the job is done must not start the opposite job
            Stop();
        }

        void Abandon()
        {
            DropCarries();
            // A half-nailed barricade does not hold.
            if (building && active != null && !active.IsComplete)
                active.Clear(true, player.cam.transform.position - active.ClosestPoint(player.cam.transform.position));
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
            hammerPulse = Mathf.MoveTowards(hammerPulse, 0f, dt * 8f);

            bool show = handsBlend > 0.01f;
            if (hands.gameObject.activeSelf != show) hands.gameObject.SetActive(show);
            if (!show) return;

            // Arms come up from below the view; the hammer arm cocks back, then snaps down on each plank.
            hands.localPosition = new Vector3(0f, Mathf.Lerp(-0.45f, 0f, Mathf.SmoothStep(0f, 1f, handsBlend)), 0f);
            if (hammerArm != null)
            {
                float swing = building ? Mathf.Lerp(-48f, 14f, hammerPulse) : Mathf.Lerp(0f, -30f, hammerPulse);
                hammerArm.localRotation = Quaternion.Euler(swing, building ? 0f : -10f * hammerPulse, 0f);
            }
        }
    }
}
