using UnityEngine;

namespace SiegeSandbox
{
    public enum SoundPathKind
    {
        Direct,     // clear line to the listener
        Absorbed,   // straight through something soft (wood, glass, a soft wall): quieter and duller
        Portal,     // obstructed: bent through one or more openings, heard from the last one
        Occluded    // no way round: only what leaks through the wall itself
    }

    public enum SoundSpace { Small, Room, Outdoor }

    /// <summary>How one sound reaches the listener.</summary>
    public struct SoundPath
    {
        public SoundPathKind kind;
        public float lossDb;             // on top of normal distance fall-off
        public float cutoffHz;           // low-pass applied to the voice
        public Vector3 apparentPosition; // where the sound seems to come from
        public int hops;                 // openings on the route (0 for a straight line)
        public float pathLength;         // metres actually travelled

        // The second version of the same sound, played alongside the first when there is one:
        // what leaks straight through the wall while the main part comes round through an opening
        // (or the other way about). Hearing both is what lets you place a sound behind a wall.
        public bool hasSecond;
        public float secondLossDb;
        public float secondCutoffHz;
        public Vector3 secondPosition;
    }

    /// <summary>
    /// Plays the synthesized sounds and decides how each one reaches the listener, following the
    /// approach Siege's audio director describes:
    ///  - Obstruction. If the straight line is blocked, the sound is routed through propagation
    ///    nodes (SoundPortal). A route's cost is its length, the angles it turns through, and the
    ///    penalty of each node and of anything soft it passes. The sound is then played from the
    ///    direction of the last node, at the distance the whole route covers, with treble taken
    ///    off for the bending.
    ///  - Occlusion. What passes straight through a wall is played from the true position,
    ///    filtered in real time by the materials in the way. It plays together with the obstructed
    ///    version.
    ///  - Destruction changes the routes at once: a hole or a missing board opens a way, a
    ///    barricade closes one or muffles it.
    ///  - Reflection. Loud sounds get a reverb tail baked for the kind of space the source is in,
    ///    played from the source's position, plus an echo off the nearest far wall in the open.
    /// The player's own sounds are played flat, and there is no ambience bed: what you hear is
    /// what somebody did.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        const int VoiceCount = 56;
        const float OpenCutoff = 22000f;

        /// <summary>Master volume, ready for an options menu.</summary>
        public static float MasterVolume = 1f;
        /// <summary>The route chosen for the most recent world sound (shown on the HUD, used by tests).</summary>
        public static SoundPath LastPath;
        public static SoundId LastWorldSound;
        public static float LastWorldTime = -100f;
        /// <summary>The kind of space the most recent loud sound was made in.</summary>
        public static SoundSpace LastSpace;

        /// <summary>How many of the sounds are playing from recordings rather than the built-in synthesizer.</summary>
        public static int RecordedSounds { get; private set; }

        static Sfx instance;
        static readonly int[] playCounts = new int[(int)SoundId.Count];

        AudioClip[][] clips;
        AudioSource[] voices;
        AudioLowPassFilter[] filters;
        int nextVoice;

        public static int PlayCount(SoundId id) { return playCounts[(int)id]; }

        public static void ResetCounts()
        {
            for (int i = 0; i < playCounts.Length; i++) playCounts[i] = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void FullVolumeOnPlay()
        {
            AudioListener.volume = 1f; // in case a test run was stopped half-way with the sound off
        }

        static Sfx Get()
        {
            if (instance == null)
            {
                if (!Application.isPlaying) return null;
                instance = new GameObject("[Audio]").AddComponent<Sfx>();
            }
            return instance;
        }

        /// <summary>Synthesize everything up front (call once at scene start to avoid a hitch on the first shot).</summary>
        public static void Warm() { Get(); }

        void Awake()
        {
            clips = new AudioClip[(int)SoundId.Count][];
            RecordedSounds = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                // Recordings win: any audio files in Resources/Sfx/<SoundName>/ replace the synthesized
                // stand-in for that sound, and each file there is used as a variation of it.
                AudioClip[] recorded = Resources.LoadAll<AudioClip>("Sfx/" + (SoundId)i);
                if (recorded.Length > 0)
                {
                    clips[i] = recorded;
                    RecordedSounds++;
                    continue;
                }
                int variants = Synth.Variants((SoundId)i);
                clips[i] = new AudioClip[variants];
                for (int v = 0; v < variants; v++) clips[i][v] = Synth.Build((SoundId)i, v);
            }

            voices = new AudioSource[VoiceCount];
            filters = new AudioLowPassFilter[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                GameObject go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.maxDistance = 90f;
                voices[i] = s;
                filters[i] = go.AddComponent<AudioLowPassFilter>();
                filters[i].cutoffFrequency = OpenCutoff;
            }
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // ------------------------------------------------------------------ playing

        /// <summary>A sound the player makes themselves: no positioning, no filtering.</summary>
        public static void PlayLocal(SoundId id, float volume = 1f, float pitch = 1f, float delay = 0f)
        {
            Sfx sfx = Get();
            if (sfx == null) return;
            playCounts[(int)id]++;
            sfx.Voice(sfx.Pick(id), Vector3.zero, false, volume, OpenCutoff, pitch * Random.Range(0.96f, 1.04f), 1f, delay);
        }

        /// <summary>A sound out in the world: routed to the listener through the level.</summary>
        public static void Play(SoundId id, Vector3 position, float volume = 1f, float pitch = 1f, float minDistance = 2f, float delay = 0f)
        {
            Sfx sfx = Get();
            if (sfx == null) return;
            playCounts[(int)id]++;

            SoundPath path = new SoundPath { kind = SoundPathKind.Direct, cutoffHz = OpenCutoff, apparentPosition = position };
            Transform ear = Listener();
            if (ear != null) path = Evaluate(position, ear.position);
            LastPath = path;
            LastWorldSound = id;
            LastWorldTime = Time.time;

            // Both versions are the same recording at the same pitch, so they fuse into one sound with a direction.
            AudioClip clip = sfx.Pick(id);
            pitch *= Random.Range(0.96f, 1.04f);
            sfx.Voice(clip, path.apparentPosition, true, volume * Gain(path.lossDb), path.cutoffHz, pitch, minDistance, delay);
            if (path.hasSecond)
                sfx.Voice(clip, path.secondPosition, true, volume * Gain(path.secondLossDb), path.secondCutoffHz, pitch, minDistance, delay);
        }

        /// <summary>
        /// The room answering a loud sound: a reverb tail chosen for the space the source is in and
        /// played from the source, plus (in the open) the sound coming back off the nearest far wall.
        /// </summary>
        public static void PlayReflections(Vector3 source, float volume, bool ownSound)
        {
            SoundSpace space = Probe(source, out Vector3 wall, out float wallDistance);
            LastSpace = space;
            SoundId tail = space == SoundSpace.Small ? SoundId.TailSmall : (space == SoundSpace.Room ? SoundId.TailRoom : SoundId.TailOutdoor);
            float level = volume * (space == SoundSpace.Small ? 0.55f : (space == SoundSpace.Room ? 0.5f : 0.4f));
            if (ownSound) PlayLocal(tail, level);
            else Play(tail, source, level, 1f, 4f);

            if (space == SoundSpace.Outdoor && wallDistance > 8f)
                Play(SoundId.Echo, wall, volume * 0.5f, 1f, 6f, 2f * wallDistance / 343f); // there and back at the speed of sound
        }

        /// <summary>One line describing a route, for the HUD.</summary>
        public static string Describe(SoundPath p)
        {
            string main;
            switch (p.kind)
            {
                case SoundPathKind.Direct: main = "clear line"; break;
                case SoundPathKind.Absorbed: main = "through soft cover"; break;
                case SoundPathKind.Portal: main = "round " + p.hops + (p.hops == 1 ? " opening, " : " openings, ") + p.pathLength.ToString("0") + " m"; break;
                default: main = "through the wall"; break;
            }
            string text = main + "  -" + p.lossDb.ToString("0") + " dB";
            if (p.cutoffHz < 20000f) text += "  " + (p.cutoffHz / 1000f).ToString("0.0") + " kHz";
            if (p.hasSecond) text += "   + " + (p.kind == SoundPathKind.Portal ? "leak through wall" : "round an opening") + " -" + p.secondLossDb.ToString("0") + " dB";
            return text;
        }

        static float Gain(float lossDb) { return Mathf.Pow(10f, -lossDb / 20f); }

        AudioClip Pick(SoundId id)
        {
            AudioClip[] set = clips[(int)id];
            return set[Random.Range(0, set.Length)];
        }

        void Voice(AudioClip clip, Vector3 position, bool spatial, float volume, float cutoff, float pitch, float minDistance, float delay)
        {
            // Prefer a voice that has finished; otherwise take the next in line.
            int k = nextVoice;
            for (int i = 0; i < VoiceCount; i++)
            {
                int candidate = (nextVoice + i) % VoiceCount;
                if (voices[candidate].isPlaying) continue;
                k = candidate;
                break;
            }
            nextVoice = (k + 1) % VoiceCount;

            AudioSource s = voices[k];
            s.Stop();
            filters[k].cutoffFrequency = cutoff;
            s.transform.position = position;
            s.spatialBlend = spatial ? 1f : 0f;
            s.minDistance = minDistance;
            s.clip = clip;
            s.volume = Mathf.Clamp01(volume * MasterVolume);
            s.pitch = pitch;
            if (delay > 0f) s.PlayDelayed(delay);
            else s.Play();
        }

        static Transform Listener()
        {
            PlayerController p = PlayerController.Instance;
            return p != null && p.cam != null ? p.cam.transform : null;
        }

        // ------------------------------------------------------------------ materials

        struct Trace
        {
            public float lossDb;
            public float cutoffHz;
            public int solids;      // concrete or metal in the way: sound does not travel this leg, it only leaks
        }

        static readonly RaycastHit[] hitBuffer = new RaycastHit[32];

        /// <summary>
        /// Follow one straight stretch and add up what is in the way. Each material takes its own
        /// toll: glass least, then wood, soft wall, metal, and concrete most of all.
        /// </summary>
        static Trace Follow(Vector3 from, Vector3 to)
        {
            Trace t = new Trace { cutoffHz = OpenCutoff };
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 0.02f) return t;

            int count = Physics.RaycastNonAlloc(from, d / length, hitBuffer, length, PlayerController.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = hitBuffer[i].collider;
                if (c.GetComponentInParent<Target>() != null) continue; // bodies do not block sound

                IBulletSurface sheet = c.GetComponentInParent<IBulletSurface>();
                if (sheet != null && sheet.IsOpenAt(c, hitBuffer[i].point)) continue; // through a hole: untouched

                Surface surface = c.GetComponentInParent<Surface>();
                SurfaceKind kind = surface != null ? surface.kind : SurfaceKind.Hard;
                switch (kind)
                {
                    case SurfaceKind.Glass: t.lossDb += 5f; t.cutoffHz = Mathf.Min(t.cutoffHz, 5000f); break;
                    case SurfaceKind.Wood: t.lossDb += 7f; t.cutoffHz = Mathf.Min(t.cutoffHz, 3200f); break;
                    case SurfaceKind.SoftWall: t.lossDb += 9f; t.cutoffHz = Mathf.Min(t.cutoffHz, 2200f); break;
                    case SurfaceKind.Metal: t.lossDb += 18f; t.cutoffHz = Mathf.Min(t.cutoffHz, 900f); t.solids++; break;
                    default: t.lossDb += 30f; t.cutoffHz = Mathf.Min(t.cutoffHz, 400f); t.solids++; break;
                }
            }
            return t;
        }

        static bool Better(Trace a, Trace b)
        {
            if (a.solids != b.solids) return a.solids < b.solids;
            return a.lossDb < b.lossDb;
        }

        static readonly Vector3[] Around = { Vector3.zero, Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        /// <summary>
        /// From a node to a point. A node on the end or top of a wall has some width to it (sound
        /// spills round the whole edge), so the stretch may start from either side of it.
        /// </summary>
        static Trace FromNode(int node, Vector3 point)
        {
            Trace best = Follow(nodePosition[node], point);
            float r = nodeRadius[node];
            if (r <= 0f || (best.solids == 0 && best.lossDb <= 0f)) return best;
            for (int k = 1; k < Around.Length; k++)
            {
                Trace t = Follow(nodePosition[node] + Around[k] * r, point);
                if (Better(t, best)) best = t;
                if (best.solids == 0 && best.lossDb <= 0f) break;
            }
            return best;
        }

        static Trace Between(int a, int b)
        {
            Trace best = FromNode(a, nodePosition[b]);
            if (best.solids == 0 && best.lossDb <= 0f) return best;
            Trace other = FromNode(b, nodePosition[a]);
            return Better(other, best) ? other : best;
        }

        // ------------------------------------------------------------------ propagation graph

        static int graphVersion = -1, nodeCount;
        static Vector3[] nodePosition = new Vector3[0];
        static float[] nodeLoss = new float[0], nodeRadius = new float[0], nodeCutoff = new float[0];
        static Trace[,] link = new Trace[0, 0];
        static bool[,] linked = new bool[0, 0];

        // Working space for one search, reused between sounds.
        static float[] cost = new float[0], length = new float[0], loss = new float[0], cutoff = new float[0], turned = new float[0];
        static int[] via = new int[0], hops = new int[0];
        static bool[] done = new bool[0];

        /// <summary>How many propagation nodes there are and how many pairs of them can hear each other.</summary>
        public static void GraphSize(out int nodes, out int links)
        {
            EnsureGraph();
            nodes = nodeCount;
            links = 0;
            for (int i = 0; i < nodeCount; i++)
                for (int j = i + 1; j < nodeCount; j++)
                    if (linked[i, j]) links++;
        }

        /// <summary>Work out which nodes can hear which. Redone whenever nodes or destruction change.</summary>
        static void EnsureGraph()
        {
            if (graphVersion == SoundPortal.Version && nodeCount == SoundPortal.All.Count) return;
            graphVersion = SoundPortal.Version;
            nodeCount = SoundPortal.All.Count;
            int n = nodeCount;
            nodePosition = new Vector3[n];
            nodeLoss = new float[n];
            nodeRadius = new float[n];
            nodeCutoff = new float[n];
            link = new Trace[n, n];
            linked = new bool[n, n];
            cost = new float[n]; length = new float[n]; loss = new float[n]; cutoff = new float[n]; turned = new float[n];
            via = new int[n]; hops = new int[n]; done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                nodePosition[i] = SoundPortal.All[i].transform.position;
                nodeLoss[i] = SoundPortal.All[i].extraLossDb;
                nodeRadius[i] = SoundPortal.All[i].radius;
                nodeCutoff[i] = SoundPortal.All[i].cutoffHz;
            }
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float apart = Vector3.Distance(nodePosition[i], nodePosition[j]);
                    if (apart > 45f || apart < 0.05f) continue;
                    Trace t = Between(i, j);
                    linked[i, j] = linked[j, i] = t.solids == 0;
                    link[i, j] = link[j, i] = t;
                }
            }
        }

        /// <summary>What a route costs while searching: metres, plus the equivalent of its losses and its turns.</summary>
        static float StepCost(float metres, float lossDb, float angle)
        {
            return metres + 0.5f * lossDb + 0.05f * angle;
        }

        /// <summary>Work out how a sound at 'source' reaches 'listener': which way round, how loud, how dull.</summary>
        public static SoundPath Evaluate(Vector3 source, Vector3 listener)
        {
            float direct = Mathf.Max(0.5f, Vector3.Distance(source, listener));
            SoundPath result = new SoundPath { apparentPosition = source, cutoffHz = OpenCutoff, pathLength = direct };

            // The straight line.
            Trace through = Follow(source, listener);
            if (through.solids == 0 && through.lossDb <= 0f)
            {
                result.kind = SoundPathKind.Direct;
                return result;
            }

            // The cheapest way round, node to node (Dijkstra over the propagation graph).
            EnsureGraph();
            int n = nodeCount;
            for (int i = 0; i < n; i++)
            {
                cost[i] = float.PositiveInfinity;
                via[i] = -1;
                done[i] = false;
                turned[i] = 0f;
                Trace t = FromNode(i, source);
                if (t.solids > 0) continue;
                length[i] = Vector3.Distance(source, nodePosition[i]);
                loss[i] = t.lossDb + nodeLoss[i];
                cutoff[i] = Mathf.Min(t.cutoffHz, nodeCutoff[i]);
                hops[i] = 1;
                cost[i] = StepCost(length[i], loss[i], 0f);
            }
            for (int step = 0; step < n; step++)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && !float.IsInfinity(cost[i]) && (u < 0 || cost[i] < cost[u])) u = i;
                if (u < 0) break;
                done[u] = true;
                Vector3 arriving = nodePosition[u] - (via[u] < 0 ? source : nodePosition[via[u]]);
                for (int v = 0; v < n; v++)
                {
                    if (done[v] || !linked[u, v]) continue;
                    float leg = Vector3.Distance(nodePosition[u], nodePosition[v]);
                    float angle = Vector3.Angle(arriving, nodePosition[v] - nodePosition[u]);
                    float c = cost[u] + StepCost(leg, link[u, v].lossDb + nodeLoss[v], angle);
                    if (c >= cost[v]) continue;
                    cost[v] = c;
                    via[v] = u;
                    length[v] = length[u] + leg;
                    loss[v] = loss[u] + link[u, v].lossDb + nodeLoss[v];
                    cutoff[v] = Mathf.Min(cutoff[u], Mathf.Min(link[u, v].cutoffHz, nodeCutoff[v]));
                    turned[v] = turned[u] + angle;
                    hops[v] = hops[u] + 1;
                }
            }

            // Finish each candidate route at the listener and keep the best one.
            bool found = false;
            float bestScore = float.PositiveInfinity;
            SoundPath round = result;
            for (int i = 0; i < n; i++)
            {
                if (float.IsInfinity(cost[i])) continue;
                Trace last = FromNode(i, listener);
                if (last.solids > 0) continue;
                Vector3 arriving = nodePosition[i] - (via[i] < 0 ? source : nodePosition[via[i]]);
                float angle = turned[i] + Vector3.Angle(arriving, listener - nodePosition[i]);
                float total = length[i] + Vector3.Distance(nodePosition[i], listener);
                float lossDb = loss[i] + last.lossDb + angle / 180f * 14f;      // bending costs level...
                float score = lossDb + 6f * Mathf.Log(Mathf.Max(1f, total / direct), 2f); // ...and the detour costs distance
                if (score >= bestScore) continue;
                bestScore = score;
                found = true;
                round.kind = SoundPathKind.Portal;
                round.lossDb = lossDb;
                round.cutoffHz = Mathf.Min(Mathf.Min(cutoff[i], last.cutoffHz), Mathf.Lerp(20000f, 2500f, angle / 150f)); // ...and treble
                round.hops = hops[i];
                round.pathLength = total;
                // Heard from the last opening, as far away as the whole route is long.
                round.apparentPosition = listener + (nodePosition[i] - listener).normalized * total;
            }

            // Straight through whatever is in the way, from where it really is.
            SoundPath leak = result;
            leak.kind = through.solids == 0 ? SoundPathKind.Absorbed : SoundPathKind.Occluded;
            leak.lossDb = through.lossDb;
            leak.cutoffHz = through.cutoffHz;
            bool leakAudible = through.lossDb <= 45f;

            if (!found)
            {
                if (!leakAudible) leak.lossDb = 60f; // sealed off: as good as silent
                return leak;
            }

            // Both versions exist: lead with the louder, and play the other alongside it if it is close enough to matter.
            bool roundLeads = !leakAudible || bestScore <= leak.lossDb;
            SoundPath first = roundLeads ? round : leak, second = roundLeads ? leak : round;
            float firstScore = roundLeads ? bestScore : leak.lossDb;
            float secondScore = roundLeads ? leak.lossDb : bestScore;
            if ((leakAudible || !roundLeads) && secondScore - firstScore < 24f)
            {
                first.hasSecond = true;
                first.secondLossDb = second.lossDb;
                first.secondCutoffHz = second.cutoffHz;
                first.secondPosition = second.apparentPosition;
            }
            return first;
        }

        // ------------------------------------------------------------------ reflection

        static readonly Vector3[] ProbeDirections =
        {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            new Vector3(1f, 0f, 1f).normalized, new Vector3(-1f, 0f, 1f).normalized,
            new Vector3(1f, 0f, -1f).normalized, new Vector3(-1f, 0f, -1f).normalized,
            Vector3.up, new Vector3(0.5f, 1f, 0f).normalized, new Vector3(-0.5f, 1f, 0.3f).normalized
        };

        /// <summary>What kind of space is this point in? Feels outward for walls and for a ceiling.</summary>
        public static SoundSpace Probe(Vector3 point)
        {
            return Probe(point, out Vector3 wall, out float distance);
        }

        static readonly float[] probeReach = new float[8];

        /// <summary>The first piece of the level along a ray. People standing about are not walls.</summary>
        static bool NearestWall(Vector3 origin, Vector3 direction, float reach, out RaycastHit nearest)
        {
            nearest = new RaycastHit();
            bool any = false;
            int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, reach, PlayerController.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (hitBuffer[i].collider.GetComponentInParent<Target>() != null) continue;
                if (any && hitBuffer[i].distance >= nearest.distance) continue;
                nearest = hitBuffer[i];
                any = true;
            }
            return any;
        }

        static SoundSpace Probe(Vector3 point, out Vector3 nearestFarWall, out float wallDistance)
        {
            nearestFarWall = point;
            wallDistance = 0f;
            float bestWall = float.PositiveInfinity, headroom = 0f;
            int walls = 0, roof = 0;
            for (int i = 0; i < ProbeDirections.Length; i++)
            {
                bool upward = ProbeDirections[i].y > 0.5f;
                RaycastHit hit;
                bool struck = NearestWall(point, ProbeDirections[i], 60f, out hit);
                if (upward)
                {
                    if (struck && hit.distance < 12f) { roof++; headroom += hit.distance; }
                    continue;
                }
                float d = struck ? hit.distance : 60f;
                probeReach[walls++] = d;
                if (struck && d > 8f && d < bestWall)
                {
                    bestWall = d;
                    nearestFarWall = hit.point - ProbeDirections[i] * 0.3f; // just off the wall's face
                    wallDistance = d;
                }
            }

            // The typical reach, ignoring the odd long view down a corridor or out of a doorway.
            System.Array.Sort(probeReach, 0, walls);
            float typical = walls == 0 ? 60f : 0.5f * (probeReach[(walls - 1) / 2] + probeReach[walls / 2]);
            if (roof < 2) return SoundSpace.Outdoor;          // open sky above
            if (headroom / roof < 1.6f) return SoundSpace.Small; // a crawl space, however wide
            return typical < 4.5f ? SoundSpace.Small : SoundSpace.Room;
        }
    }
}
