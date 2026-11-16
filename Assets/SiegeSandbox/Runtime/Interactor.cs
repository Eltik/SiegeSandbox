using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Lets the player build or remove a barricade with the interact key. The action takes
    /// time, lowers the weapon, and is cancelled by walking away or pressing the key again.
    /// </summary>
    public class Interactor : MonoBehaviour
    {
        public PlayerController player;
        public float reach = 2.3f;
        public float buildTime = 1.5f;
        public float removeTime = 1.0f;

        /// <summary>Text for the HUD, empty when there is nothing to interact with.</summary>
        public string Prompt { get; private set; }
        /// <summary>0..1 while an action is running.</summary>
        public float Progress { get; private set; }
        public bool IsBusy { get; private set; }

        Barricade active;
        float elapsed, duration;
        bool building;

        void Awake() { Prompt = ""; }

        void Update()
        {
            if (player == null || player.cam == null) return;
            Vector3 eye = player.cam.transform.position;

            if (IsBusy)
            {
                elapsed += Time.deltaTime;
                Progress = Mathf.Clamp01(elapsed / duration);
                bool tooFar = active == null || Vector3.Distance(active.ClosestPoint(eye), eye) > reach + 0.6f;
                if (tooFar || player.input.interactPressed || (building && !HasRoomToBuild(active)))
                {
                    Stop();
                }
                else if (elapsed >= duration)
                {
                    if (building) active.Build();
                    else active.Remove();
                    Stop();
                }
            }
            else
            {
                Barricade target = FindTarget(eye, player.cam.transform.forward);
                if (target == null) Prompt = "";
                else if (target.IsBuilt) Prompt = "F   remove barricade";
                else if (HasRoomToBuild(target)) Prompt = "F   barricade";
                else Prompt = "Step back to barricade";

                if (target != null && player.input.interactPressed && (target.IsBuilt || HasRoomToBuild(target)))
                {
                    active = target;
                    building = !target.IsBuilt;
                    duration = building ? buildTime : removeTime;
                    elapsed = 0f;
                    Progress = 0f;
                    IsBusy = true;
                    Prompt = building ? "Barricading..." : "Removing barricade...";
                }
            }

            player.IsBusy = IsBusy;
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
    }
}
