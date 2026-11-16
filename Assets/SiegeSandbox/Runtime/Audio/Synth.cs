using UnityEngine;

namespace SiegeSandbox
{
    /// <summary>
    /// Every sound in the sandbox is synthesized here at startup from filtered noise bursts and
    /// decaying sine sweeps, so the project ships no audio files. Each sound is layered the way
    /// a recorded one would be (a gunshot is crack + body + thump + tail + action), and most have
    /// a few variants so repeats do not sound identical.
    /// </summary>
    public static class Synth
    {
        public const int Rate = 44100;

        static System.Random rng = new System.Random(1);

        public static int Variants(SoundId id)
        {
            switch (id)
            {
                case SoundId.GunShot: return 3;
                case SoundId.Footstep:
                case SoundId.NpcFootstep: return 4;
                case SoundId.ImpactHard:
                case SoundId.ImpactWood:
                case SoundId.ImpactSoftWall:
                case SoundId.ImpactFlesh:
                case SoundId.PlankBreak: return 3;
                case SoundId.Crawl:
                case SoundId.Cloth:
                case SoundId.ShellCasing:
                case SoundId.Hammer:
                case SoundId.GearRattle:
                case SoundId.TailSmall:
                case SoundId.TailRoom:
                case SoundId.TailOutdoor:
                case SoundId.WallPunch: return 2;
                default: return 1;
            }
        }

        public static AudioClip Build(SoundId id, int variant)
        {
            float[] b = Samples(id, variant);
            AudioClip clip = AudioClip.Create(id + "_" + variant, b.Length, 1, Rate, false);
            clip.SetData(b, 0);
            return clip;
        }

        /// <summary>The raw waveform for a sound (also used by tests to sanity-check the synthesis).</summary>
        public static float[] Samples(SoundId id, int v)
        {
            rng = new System.Random(1000 + (int)id * 17 + v * 101);
            float[] b;
            switch (id)
            {
                case SoundId.GunShot:
                    b = Buf(0.3f);
                    Noise(b, 0f, 0.03f, 1.0f, 0f, 0.006f, 0f, 2500f);                 // supersonic crack
                    Noise(b, 0f, 0.14f, 0.9f, 0.0005f, 0.030f, 2200f + 150f * v, 200f); // body of the blast
                    Tone(b, 0f, 0.14f, 0.9f, 190f, 60f, 0.02f, 0.045f);              // chest thump
                    Tone(b, 0.012f, 0.03f, 0.15f, 3200f, 3200f, 1f, 0.006f);         // bolt clatter
                    return Finish(b, 0.95f, 1.6f); // dry: the room is added as a separate, positioned tail

                case SoundId.ShellCasing:
                    b = Buf(0.5f);
                    float t = 0f;
                    for (int i = 0; i < 3; i++)
                    {
                        float g = 1f / (1f + i * 1.2f);
                        Tone(b, t, 0.12f, g, 5200f + 300f * v + 200f * i, 5200f, 1f, 0.03f);
                        Tone(b, t, 0.08f, g * 0.5f, 7900f, 7900f, 1f, 0.02f);
                        t += 0.09f + 0.05f * Unit();
                    }
                    return Finish(b, 0.5f, 0f);

                case SoundId.DryFire:
                    b = Buf(0.08f);
                    Noise(b, 0f, 0.01f, 1f, 0f, 0.003f, 0f, 1500f);
                    Tone(b, 0f, 0.05f, 0.4f, 2400f, 2400f, 1f, 0.012f);
                    return Finish(b, 0.5f, 0f);

                case SoundId.MagOut:
                    b = Buf(0.25f);
                    Noise(b, 0f, 0.012f, 1f, 0f, 0.004f, 6000f, 800f);                // catch releases
                    Tone(b, 0f, 0.06f, 0.3f, 900f, 700f, 0.02f, 0.02f);
                    Noise(b, 0.02f, 0.12f, 0.35f, 0.02f, 0.05f, 3500f, 900f);         // magazine slides out
                    return Finish(b, 0.6f, 0f);

                case SoundId.MagIn:
                    b = Buf(0.3f);
                    Noise(b, 0f, 0.08f, 0.3f, 0.03f, 0.04f, 3000f, 700f);             // slides up the well
                    Noise(b, 0.09f, 0.02f, 1f, 0f, 0.006f, 5000f, 500f);              // seats with a clack
                    Tone(b, 0.09f, 0.10f, 0.7f, 320f, 220f, 0.02f, 0.03f);
                    Tone(b, 0.09f, 0.05f, 0.3f, 1800f, 1800f, 1f, 0.01f);
                    return Finish(b, 0.75f, 0f);

                case SoundId.BoltBack:
                    b = Buf(0.2f);
                    Noise(b, 0f, 0.1f, 0.6f, 0.01f, 0.05f, 4500f, 1200f);             // spring scrape
                    Noise(b, 0.1f, 0.015f, 0.8f, 0f, 0.005f, 6000f, 700f);
                    Tone(b, 0.1f, 0.05f, 0.4f, 1400f, 1400f, 1f, 0.012f);
                    return Finish(b, 0.65f, 0f);

                case SoundId.BoltForward:
                    b = Buf(0.15f);
                    Noise(b, 0f, 0.02f, 1f, 0f, 0.005f, 6000f, 600f);
                    Tone(b, 0f, 0.08f, 0.6f, 520f, 380f, 0.015f, 0.025f);
                    Tone(b, 0f, 0.04f, 0.3f, 2600f, 2600f, 1f, 0.008f);
                    return Finish(b, 0.8f, 0f);

                case SoundId.MagDrop:
                    b = Buf(0.4f);
                    for (int i = 0; i < 3; i++)                                       // hits the floor and skips twice
                    {
                        float at = i == 0 ? 0f : 0.11f * i + 0.03f * Unit();
                        float g = 1f / (1f + i * 1.5f);
                        Tone(b, at, 0.08f, g, 820f + 120f * i, 640f, 0.02f, 0.018f);
                        Tone(b, at, 0.05f, g * 0.5f, 2100f, 2100f, 1f, 0.01f);
                        Noise(b, at, 0.015f, g, 0f, 0.004f, 5000f, 600f);
                    }
                    return Finish(b, 0.6f, 0f);

                case SoundId.GearRattle:
                    b = Buf(0.2f);
                    Noise(b, 0f, 0.18f, 0.5f, 0.02f, 0.05f, 3200f, 900f);             // webbing and pouches
                    Grains(b, 5 + v, 0f, 0.14f, 0.5f, 8000f, 3000f);                  // buckles
                    return Finish(b, 0.35f, 0f);

                case SoundId.TailSmall:                                               // a tight, bright box of a room
                    b = Buf(0.4f);
                    for (int i = 0; i < 4; i++) Noise(b, 0.007f + 0.008f * i * (1f + 0.2f * v), 0.01f, 0.7f / (1 + i), 0f, 0.004f, 4000f, 400f);
                    Noise(b, 0.005f, 0.4f, 1f, 0.004f, 0.07f, 3500f, 300f);
                    return Finish(b, 0.7f, 0f);

                case SoundId.TailRoom:                                                // a hall: later reflections, longer ring
                    b = Buf(0.95f);
                    for (int i = 0; i < 3; i++) Noise(b, 0.02f + 0.018f * i * (1f + 0.15f * v), 0.015f, 0.6f / (1 + i), 0f, 0.006f, 3000f, 300f);
                    Noise(b, 0.01f, 0.9f, 1f, 0.01f, 0.20f, 2200f, 200f);
                    return Finish(b, 0.7f, 0f);

                case SoundId.TailOutdoor:                                             // open air: a long, low roll
                    b = Buf(1.6f);
                    Noise(b, 0.02f, 1.6f, 1f, 0.03f, 0.45f, 1100f - 150f * v, 90f);
                    Noise(b, 0.05f, 1.2f, 0.6f, 0.08f, 0.6f, 320f, 40f);
                    return Finish(b, 0.6f, 0f);

                case SoundId.Echo:                                                    // the shot coming back off a far wall
                    b = Buf(0.45f);
                    Noise(b, 0f, 0.25f, 1f, 0.003f, 0.06f, 1400f, 150f);
                    Tone(b, 0f, 0.18f, 0.6f, 150f, 70f, 0.03f, 0.05f);
                    return Finish(b, 0.6f, 0f);

                case SoundId.Footstep:
                case SoundId.NpcFootstep:
                    b = Buf(0.18f);
                    Noise(b, 0f, 0.12f, 1f, 0.002f, 0.03f, 420f + 40f * v, 0f);       // heel
                    Tone(b, 0f, 0.10f, 0.5f, 110f, 70f, 0.02f, 0.03f);
                    Noise(b, 0.01f + 0.004f * v, 0.10f, 0.25f, 0.01f, 0.035f, 2600f, 900f); // sole scuff
                    return Finish(b, 0.7f, 0f);

                case SoundId.Crawl:
                    b = Buf(0.45f);
                    Noise(b, 0f, 0.45f, 1f, 0.12f, 0.14f, 1500f + 200f * v, 250f);    // body dragging
                    Noise(b, 0.1f, 0.2f, 0.4f, 0.05f, 0.08f, 3500f, 1500f);           // gear rubbing
                    return Finish(b, 0.5f, 0f);

                case SoundId.Land:
                    b = Buf(0.3f);
                    Noise(b, 0f, 0.2f, 1f, 0.002f, 0.05f, 350f, 0f);
                    Tone(b, 0f, 0.15f, 0.8f, 90f, 50f, 0.03f, 0.05f);
                    Noise(b, 0.02f, 0.2f, 0.3f, 0.02f, 0.07f, 2500f, 700f);           // kit settling
                    return Finish(b, 0.85f, 0f);

                case SoundId.Jump:
                    b = Buf(0.22f);
                    Noise(b, 0f, 0.2f, 1f, 0.05f, 0.07f, 2200f, 500f);
                    Tone(b, 0f, 0.06f, 0.4f, 120f, 80f, 0.02f, 0.025f);
                    return Finish(b, 0.45f, 0f);

                case SoundId.Cloth:
                    b = Buf(0.2f);
                    Noise(b, 0f, 0.2f, 1f, 0.05f, 0.06f, 2800f - 500f * v, 600f);
                    return Finish(b, 0.3f, 0f);

                case SoundId.MeleeSwing:
                    b = Buf(0.28f);
                    Noise(b, 0f, 0.28f, 1f, 0.08f, 0.07f, 3000f, 600f);
                    Noise(b, 0.05f, 0.2f, 0.6f, 0.04f, 0.05f, 7000f, 2500f);          // air tearing at the peak
                    return Finish(b, 0.55f, 0f);

                case SoundId.ImpactHard:
                    b = Buf(0.2f);
                    Noise(b, 0f, 0.03f, 1f, 0f, 0.008f, 0f, 1800f);
                    Tone(b, 0f, 0.15f, 0.35f, 2900f + 300f * v, 2700f, 0.05f, 0.04f); // ricochet ring
                    Noise(b, 0.01f, 0.15f, 0.3f, 0.005f, 0.05f, 1500f, 300f);         // grit
                    return Finish(b, 0.7f, 0f);

                case SoundId.ImpactWood:
                    b = Buf(0.2f);
                    Tone(b, 0f, 0.15f, 1f, 300f + 30f * v, 210f, 0.02f, 0.035f);      // knock
                    Noise(b, 0f, 0.02f, 0.8f, 0f, 0.006f, 4000f, 500f);
                    Noise(b, 0.015f, 0.12f, 0.3f, 0.004f, 0.04f, 6000f, 2000f);       // splinters
                    return Finish(b, 0.75f, 0f);

                case SoundId.ImpactSoftWall:
                    b = Buf(0.22f);
                    Noise(b, 0f, 0.06f, 1f, 0f, 0.02f, 1100f, 150f);                  // dull puff of plaster
                    Grains(b, 5, 0.02f, 0.15f, 0.3f, 3500f, 900f);                    // crumbs falling
                    return Finish(b, 0.6f, 0f);

                case SoundId.ImpactFlesh:
                    b = Buf(0.18f);
                    Noise(b, 0f, 0.1f, 1f, 0.001f, 0.03f, 600f + 60f * v, 80f);
                    Tone(b, 0f, 0.1f, 0.7f, 150f, 90f, 0.02f, 0.035f);
                    return Finish(b, 0.7f, 0f);

                case SoundId.Headshot:
                    b = Buf(0.3f);
                    Tone(b, 0f, 0.3f, 1f, 1850f, 1850f, 1f, 0.07f);                   // the "dink"
                    Tone(b, 0f, 0.25f, 0.6f, 2780f, 2780f, 1f, 0.05f);
                    Tone(b, 0f, 0.2f, 0.3f, 4200f, 4200f, 1f, 0.03f);
                    Noise(b, 0f, 0.02f, 0.5f, 0f, 0.005f, 0f, 2500f);
                    return Finish(b, 0.8f, 0f);

                case SoundId.HitTick:
                    b = Buf(0.05f);
                    Tone(b, 0f, 0.05f, 1f, 1300f, 1300f, 1f, 0.012f);
                    Noise(b, 0f, 0.006f, 0.5f, 0f, 0.002f, 0f, 2000f);
                    return Finish(b, 0.35f, 0f);

                case SoundId.WallPunch:
                    b = Buf(0.35f);
                    Tone(b, 0f, 0.15f, 1f, 140f, 80f, 0.03f, 0.05f);
                    Noise(b, 0f, 0.1f, 1f, 0f, 0.03f, 900f + 150f * v, 100f);         // sheet giving way
                    Grains(b, 8, 0.03f, 0.3f, 0.35f, 4000f, 800f);
                    return Finish(b, 0.8f, 0f);

                case SoundId.PlankBreak:
                    b = Buf(0.3f);
                    Noise(b, 0f, 0.015f, 1f, 0f, 0.004f, 0f, 1200f);                  // the snap
                    Tone(b, 0f, 0.15f, 0.8f, 620f + 60f * v, 400f, 0.03f, 0.04f);
                    Tone(b, 0.01f, 0.1f, 0.4f, 1150f, 900f, 0.02f, 0.025f);
                    Grains(b, 4, 0.02f, 0.2f, 0.3f, 6000f, 1800f);
                    return Finish(b, 0.85f, 0f);

                case SoundId.BarricadeCollapse:
                    b = Buf(0.9f);
                    Tone(b, 0.03f, 0.2f, 0.9f, 130f, 70f, 0.03f, 0.06f);
                    for (int i = 0; i < 7; i++)
                    {
                        float at = 0.6f * i / 7f + 0.04f * Unit();
                        Tone(b, at, 0.12f, 0.7f, 350f + 400f * Unit(), 300f, 0.03f, 0.03f);
                        Noise(b, at, 0.02f, 0.6f, 0f, 0.005f, 5000f, 800f);
                    }
                    Grains(b, 10, 0.05f, 0.7f, 0.25f, 6000f, 1500f);
                    return Finish(b, 0.9f, 0f);

                case SoundId.Hammer:
                    b = Buf(0.16f);
                    Noise(b, 0f, 0.01f, 1f, 0f, 0.003f, 0f, 1500f);
                    Tone(b, 0f, 0.12f, 0.8f, 980f + 60f * v, 940f, 0.05f, 0.03f);     // steel head
                    Tone(b, 0f, 0.10f, 0.5f, 260f, 200f, 0.02f, 0.03f);               // board answering
                    Tone(b, 0f, 0.03f, 0.3f, 2300f, 2300f, 1f, 0.008f);
                    return Finish(b, 0.8f, 0f);

                case SoundId.BarricadeUnroll:
                    b = Buf(0.5f);
                    Noise(b, 0f, 0.5f, 0.3f, 0.08f, 0.2f, 2500f, 500f);               // canvas straps
                    for (int i = 0; i < 7; i++)
                    {
                        float at = 0.42f * i / 7f + 0.02f * Unit();
                        Tone(b, at, 0.06f, 0.6f, 500f + 400f * Unit(), 450f, 0.02f, 0.015f); // boards knocking
                        Noise(b, at, 0.01f, 0.4f, 0f, 0.003f, 5000f, 900f);
                    }
                    return Finish(b, 0.55f, 0f);

                case SoundId.Pry:
                    b = Buf(0.5f);
                    Tone(b, 0f, 0.42f, 0.7f, 380f, 520f, 0.2f, 0.25f, 0.05f);          // nails creaking out
                    Tone(b, 0f, 0.42f, 0.3f, 770f, 1050f, 0.2f, 0.2f, 0.05f);
                    Noise(b, 0f, 0.45f, 0.3f, 0.1f, 0.2f, 3000f, 1000f);
                    Noise(b, 0.42f, 0.02f, 1f, 0f, 0.006f, 0f, 1000f);                // and letting go
                    Tone(b, 0.42f, 0.06f, 0.6f, 500f, 380f, 0.02f, 0.02f);
                    return Finish(b, 0.6f, 0f);

                case SoundId.BodyFall:
                    b = Buf(0.4f);
                    Noise(b, 0f, 0.25f, 1f, 0.003f, 0.06f, 300f, 0f);
                    Tone(b, 0f, 0.2f, 0.9f, 80f, 45f, 0.04f, 0.06f);
                    Noise(b, 0.03f, 0.3f, 0.3f, 0.03f, 0.09f, 2200f, 600f);
                    return Finish(b, 0.85f, 0f);
            }
            return Buf(0.05f);
        }

        // ------------------------------------------------------------------ building blocks

        static float[] Buf(float seconds)
        {
            return new float[Mathf.CeilToInt(seconds * Rate)];
        }

        static float Unit() { return (float)rng.NextDouble(); }
        static float Bipolar() { return (float)(rng.NextDouble() * 2.0 - 1.0); }

        /// <summary>
        /// Add a burst of white noise shaped by a one-pole low-pass and high-pass (0 = off) and an
        /// attack / exponential-decay envelope. Gain is compensated so that a dark burst is about as
        /// loud as a bright one.
        /// </summary>
        static void Noise(float[] b, float start, float length, float gain, float attack, float decay, float lowPass, float highPass)
        {
            int s0 = Mathf.Clamp((int)(start * Rate), 0, b.Length);
            int n = Mathf.Min(b.Length - s0, (int)(length * Rate));
            float lpA = lowPass > 0f ? 1f - Mathf.Exp(-2f * Mathf.PI * lowPass / Rate) : 1f;
            float hpA = highPass > 0f ? 1f - Mathf.Exp(-2f * Mathf.PI * highPass / Rate) : 0f;
            float makeup = lowPass > 0f ? Mathf.Min(8f, 1f / Mathf.Sqrt(lpA / (2f - lpA))) : 1f;
            float lp = 0f, hp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += lpA * (Bipolar() - lp);
                float y = lp;
                if (highPass > 0f)
                {
                    hp += hpA * (y - hp);
                    y -= hp;
                }
                float env = (attack > 0f ? Mathf.Min(1f, t / attack) : 1f) * Mathf.Exp(-t / decay);
                b[s0 + i] += y * env * gain * makeup;
            }
        }

        /// <summary>Add a sine that glides from f0 to f1 (time constant 'glide') with an attack / decay envelope.</summary>
        static void Tone(float[] b, float start, float length, float gain, float f0, float f1, float glide, float decay, float attack = 0.001f)
        {
            int s0 = Mathf.Clamp((int)(start * Rate), 0, b.Length);
            int n = Mathf.Min(b.Length - s0, (int)(length * Rate));
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = f1 + (f0 - f1) * Mathf.Exp(-t / glide);
                phase += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Min(1f, t / attack) * Mathf.Exp(-t / decay);
                b[s0 + i] += Mathf.Sin(phase) * env * gain;
            }
        }

        /// <summary>Scatter short bright clicks over a window: crumbs, splinters, grit.</summary>
        static void Grains(float[] b, int count, float from, float to, float gain, float lowPass, float highPass)
        {
            for (int i = 0; i < count; i++)
                Noise(b, Mathf.Lerp(from, to, Unit()), 0.02f, gain * (0.5f + 0.5f * Unit()), 0f, 0.004f + 0.006f * Unit(), lowPass, highPass);
        }

        /// <summary>Crossfade the end of the buffer into its start so it loops without a click.</summary>
        static void Loop(float[] b, float seconds)
        {
            int n = Mathf.Min(b.Length / 2, (int)(seconds * Rate));
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)n;
                int tail = b.Length - n + i;
                b[tail] = b[tail] * (1f - k) + b[i] * k;
            }
        }

        /// <summary>Normalise to a peak level, optionally saturate (for the gunshot), and fade the last few milliseconds.</summary>
        static float[] Finish(float[] b, float peak, float drive)
        {
            float max = 0.0001f;
            for (int i = 0; i < b.Length; i++) max = Mathf.Max(max, Mathf.Abs(b[i]));
            float norm = 1f / max;
            float driven = drive > 0f ? (float)System.Math.Tanh(drive) : 1f;
            int fade = Mathf.Min(b.Length, Rate / 200);
            for (int i = 0; i < b.Length; i++)
            {
                float x = b[i] * norm;
                if (drive > 0f) x = (float)System.Math.Tanh(x * drive) / driven;
                int fromEnd = b.Length - 1 - i;
                if (fromEnd < fade) x *= fromEnd / (float)fade;
                b[i] = x * peak;
            }
            return b;
        }
    }
}
