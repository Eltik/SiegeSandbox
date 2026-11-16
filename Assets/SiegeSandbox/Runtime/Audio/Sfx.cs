using UnityEngine;

namespace SiegeSandbox
{
    public enum SoundPathKind
    {
        Direct,     // clear line to the listener
        Absorbed,   // straight through something soft (wood, a soft wall): quieter and duller
        Portal,     // bent through an opening: heard from the opening's direction
        Occluded    // no way round and solid in between: a faint thud through the wall
    }

    public struct SoundPath
    {
        public SoundPathKind kind;
        public float lossDb;            // on top of normal distance fall-off
        public float cutoffHz;          // low-pass applied to the voice
        public Vector3 apparentPosition; // where the sound seems to come from
    }

    /// <summary>
    /// Plays the synthesized sounds. World sounds are routed the way Siege routes them: if the
    /// straight line to the listener is blocked, the sound looks for the cheapest route through
    /// an opening (a SoundPortal), arrives from that opening's direction, and loses level and
    /// treble for the detour. Wood and soft walls let sound through muffled; a hole in a soft wall
    /// or a missing plank lets it through clean. The player's own sounds are played flat.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        const int VoiceCount = 28;
        const float OpenCutoff = 22000f;

        /// <summary>Master volume, ready for an options menu.</summary>
        public static float MasterVolume = 1f;
        /// <summary>The route chosen for the most recent world sound (for debugging and tests).</summary>
        public static SoundPath LastPath;

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
            for (int i = 0; i < clips.Length; i++)
            {
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

            // A very quiet bed of air so silence is never digital silence.
            AudioSource bed = gameObject.AddComponent<AudioSource>();
            bed.clip = clips[(int)SoundId.RoomTone][0];
            bed.loop = true;
            bed.spatialBlend = 0f;
            bed.volume = 0.12f * MasterVolume;
            bed.priority = 200;
            bed.Play();
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
            AudioSource s = sfx.Claim(out AudioLowPassFilter f);
            f.cutoffFrequency = OpenCutoff;
            s.spatialBlend = 0f;
            sfx.Begin(s, id, volume, pitch, delay);
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

            AudioSource s = sfx.Claim(out AudioLowPassFilter f);
            f.cutoffFrequency = path.cutoffHz;
            s.transform.position = path.apparentPosition;
            s.spatialBlend = 1f;
            s.minDistance = minDistance;
            sfx.Begin(s, id, volume * Mathf.Pow(10f, -path.lossDb / 20f), pitch, delay);
        }

        AudioSource Claim(out AudioLowPassFilter filter)
        {
            // Prefer a voice that has finished; otherwise take the next in line.
            for (int i = 0; i < VoiceCount; i++)
            {
                int k = (nextVoice + i) % VoiceCount;
                if (voices[k].isPlaying) continue;
                nextVoice = (k + 1) % VoiceCount;
                filter = filters[k];
                return voices[k];
            }
            int stolen = nextVoice;
            nextVoice = (nextVoice + 1) % VoiceCount;
            voices[stolen].Stop();
            filter = filters[stolen];
            return voices[stolen];
        }

        void Begin(AudioSource s, SoundId id, float volume, float pitch, float delay)
        {
            AudioClip[] set = clips[(int)id];
            s.clip = set[Random.Range(0, set.Length)];
            s.volume = Mathf.Clamp01(volume * MasterVolume);
            s.pitch = pitch * Random.Range(0.96f, 1.04f);
            if (delay > 0f) s.PlayDelayed(delay);
            else s.Play();
        }

        static Transform Listener()
        {
            PlayerController p = PlayerController.Instance;
            return p != null && p.cam != null ? p.cam.transform : null;
        }

        // ------------------------------------------------------------------ propagation

        /// <summary>Work out how a sound at 'source' reaches 'listener': which way round, how loud, how dull.</summary>
        public static SoundPath Evaluate(Vector3 source, Vector3 listener)
        {
            float direct = Mathf.Max(0.5f, Vector3.Distance(source, listener));
            SoundPath best = new SoundPath { kind = SoundPathKind.Occluded, lossDb = 26f, cutoffHz = 550f, apparentPosition = source };

            // Straight line: clear, or through something that only absorbs.
            float loss = 0f, cutoff = OpenCutoff;
            if (Leg(source, listener, ref loss, ref cutoff))
            {
                best.kind = loss > 0f ? SoundPathKind.Absorbed : SoundPathKind.Direct;
                best.lossDb = loss;
                best.cutoffHz = cutoff;
                if (loss <= 0f) return best;
            }

            // Bent through one opening. Cost: the detour, the angle turned, and the opening itself.
            for (int i = 0; i < SoundPortal.All.Count; i++)
            {
                SoundPortal portal = SoundPortal.All[i];
                Vector3 node = portal.transform.position;
                float total = Vector3.Distance(source, node) + Vector3.Distance(node, listener);
                if (total > direct * 4f + 12f) continue;

                loss = portal.extraLossDb;
                cutoff = OpenCutoff;
                if (!Leg(source, node, ref loss, ref cutoff) || !Leg(node, listener, ref loss, ref cutoff)) continue;

                float angle = Vector3.Angle(node - source, listener - node);
                loss += angle / 180f * 14f + 6f * Mathf.Log(total / direct, 2f);
                cutoff = Mathf.Min(cutoff, Mathf.Lerp(20000f, 3000f, angle / 120f));
                if (loss >= best.lossDb) continue;

                best.kind = SoundPathKind.Portal;
                best.lossDb = loss;
                best.cutoffHz = cutoff;
                // Heard from the opening, as far away as the whole route is long.
                best.apparentPosition = listener + (node - listener).normalized * total;
            }
            return best;
        }

        /// <summary>
        /// One straight stretch of a route. Returns false if something solid is in the way;
        /// otherwise adds up what the wood and soft walls along it take out of the sound.
        /// </summary>
        static bool Leg(Vector3 from, Vector3 to, ref float lossDb, ref float cutoffHz)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 0.02f) return true;

            RaycastHit[] hits = Physics.RaycastAll(from, d / length, length, PlayerController.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c.GetComponentInParent<Target>() != null) continue; // bodies do not block sound

                Surface surface = c.GetComponentInParent<Surface>();
                if (surface == null || surface.kind == SurfaceKind.Hard) return false;

                IBulletSurface sheet = c.GetComponentInParent<IBulletSurface>();
                if (sheet != null && sheet.IsOpenAt(c, hits[i].point)) continue; // through a hole: untouched

                if (surface.kind == SurfaceKind.Wood)
                {
                    lossDb += 7f;
                    cutoffHz = Mathf.Min(cutoffHz, 3200f);
                }
                else
                {
                    lossDb += 9f;
                    cutoffHz = Mathf.Min(cutoffHz, 2200f);
                }
            }
            return true;
        }
    }
}
