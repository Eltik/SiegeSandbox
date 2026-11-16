using System.IO;
using UnityEditor;
using UnityEngine;

namespace SiegeSandbox.EditorTools
{
    /// <summary>
    /// Siege Sandbox > Sound Board. Lists every sound in the game with the clips it plays, lets you
    /// listen to each one without starting the game, and swap in your own recordings.
    /// </summary>
    public class SoundBoard : EditorWindow
    {
        const string Root = "Assets/SiegeSandbox/Resources/Sfx";

        // What each sound is for, and where its current recording falls short.
        static readonly string[,] Notes =
        {
            { "GunShot", "The rifle firing, without the room. Recorded 5.56 rifle." },
            { "TailOutdoor", "What rings on after a shot outdoors. Recorded." },
            { "TailSmall", "A shot's echo in a tight space. Recorded shot through a generated room." },
            { "TailRoom", "A shot's echo in a hall. Recorded shot through a generated room." },
            { "Echo", "The shot coming back off a far wall. Recorded rifle at a distance." },
            { "ShellCasing", "Brass landing after each shot." },
            { "DryFire", "Trigger pulled on an empty gun." },
            { "MagOut", "Magazine pulled out. Recorded AR-15." },
            { "MagIn", "Magazine pushed home. Recorded AR-15." },
            { "MagDrop", "Empty magazine hitting the floor. STAND-IN: generic metal falling." },
            { "BoltBack", "Charging handle pulled. Recorded AR-15." },
            { "BoltForward", "Bolt slamming home. Recorded AR-15." },
            { "Footstep", "Your boots, walking. Recorded on a hard floor." },
            { "FootstepRun", "Your boots, sprinting. Recorded on a hard floor." },
            { "NpcFootstep", "Somebody else's boots. Recorded on a hard floor." },
            { "Crawl", "Moving while prone. STAND-IN: slowed footsteps on grit." },
            { "Land", "Landing from a jump. Recorded." },
            { "Jump", "Pushing off into a jump. Recorded." },
            { "Cloth", "Kit shifting when you aim, lean or change stance. STILL SYNTHESIZED." },
            { "GearRattle", "Kit rattling when you sprint or land. STILL SYNTHESIZED." },
            { "MeleeSwing", "The melee strike through the air. STILL SYNTHESIZED." },
            { "ImpactHard", "Bullet hitting concrete. STAND-IN: no recorded bullet impacts yet." },
            { "ImpactWood", "Bullet hitting a board. STAND-IN: wood cracks and knocks." },
            { "ImpactSoftWall", "Bullet hitting a soft wall. STAND-IN: wood knocks." },
            { "ImpactFlesh", "Bullet hitting a body. STAND-IN: a dull thump." },
            { "Headshot", "The headshot cue. STAND-IN: thump plus a crack." },
            { "HitTick", "Hit confirmation tick. Synthesized on purpose (interface sound)." },
            { "WallPunch", "Melee through a soft wall. Recorded wood slams." },
            { "PlankBreak", "One barricade board breaking. Recorded wood breaking." },
            { "BarricadeCollapse", "A whole barricade coming down. Built from recorded wood." },
            { "Hammer", "Nailing a barricade up. Recorded hammer." },
            { "BarricadeUnroll", "Boards unrolling down the frame. STAND-IN: wood falling." },
            { "Pry", "Crowbar prying a barricade off. Recorded wood squeaks." },
            { "BodyFall", "A body hitting the floor. STAND-IN: slowed landing." },
        };

        Vector2 scroll;
        AudioSource player;
        string filter = "";

        [MenuItem("Siege Sandbox/Sound Board")]
        static void Open()
        {
            GetWindow<SoundBoard>("Sound Board").minSize = new Vector2(560f, 400f);
        }

        void OnDisable()
        {
            if (player != null) DestroyImmediate(player.gameObject);
        }

        void Play(AudioClip clip)
        {
            if (clip == null) return;
            if (player == null)
            {
                GameObject go = new GameObject("SoundBoardPlayer") { hideFlags = HideFlags.HideAndDontSave };
                player = go.AddComponent<AudioSource>();
                player.spatialBlend = 0f;
            }
            player.Stop();
            player.clip = clip;
            player.Play();
        }

        static string NoteFor(string name)
        {
            for (int i = 0; i < Notes.GetLength(0); i++)
                if (Notes[i, 0] == name) return Notes[i, 1];
            return "";
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Press a numbered button to hear a clip. To replace a sound, use Add files and pick your own recordings " +
                "(wav, ogg, mp3 or aiff, ideally mono and trimmed tight). Every file in a sound's folder is played as a variation; " +
                "remove the ones you don't like. A sound with no files falls back to the built-in synthesized version.",
                MessageType.Info);
            filter = EditorGUILayout.TextField("Filter", filter);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < (int)SoundId.Count; i++)
            {
                SoundId id = (SoundId)i;
                string name = id.ToString();
                string note = NoteFor(name);
                if (filter.Length > 0 && name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    note.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                AudioClip[] clips = Resources.LoadAll<AudioClip>("Sfx/" + name);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(name, EditorStyles.boldLabel, GUILayout.Width(150f));
                GUILayout.Label(clips.Length > 0 ? clips.Length + " recorded" : "synthesized", GUILayout.Width(90f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add files", GUILayout.Width(80f))) AddFiles(name);
                if (GUILayout.Button("Folder", GUILayout.Width(60f)))
                {
                    Directory.CreateDirectory(Root + "/" + name);
                    EditorUtility.RevealInFinder(Root + "/" + name);
                }
                EditorGUILayout.EndHorizontal();
                if (note.Length > 0) EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.BeginHorizontal();
                if (clips.Length == 0)
                {
                    int variants = Synth.Variants(id);
                    for (int v = 0; v < variants; v++)
                        if (GUILayout.Button("synth " + (v + 1), GUILayout.Width(64f))) Play(Synth.Build(id, v));
                }
                for (int c = 0; c < clips.Length; c++)
                {
                    if (GUILayout.Button(new GUIContent((c + 1).ToString(), clips[c].name + "  " + clips[c].length.ToString("0.00") + " s"), GUILayout.Width(30f)))
                        Play(clips[c]);
                    if (GUILayout.Button(new GUIContent("x", "Remove " + clips[c].name + " (moved to the Trash)"), EditorStyles.miniButton, GUILayout.Width(18f)))
                    {
                        if (EditorUtility.DisplayDialog("Remove clip", "Move " + clips[c].name + " to the Trash?", "Remove", "Cancel"))
                        {
                            AssetDatabase.MoveAssetToTrash(AssetDatabase.GetAssetPath(clips[c]));
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        static void AddFiles(string name)
        {
            string picked = EditorUtility.OpenFilePanelWithFilters("Recording for " + name, "", new[] { "Audio", "wav,ogg,mp3,aiff,aif" });
            if (string.IsNullOrEmpty(picked)) return;
            string folder = Root + "/" + name;
            Directory.CreateDirectory(folder);
            string target = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(picked));
            File.Copy(picked, target);
            AssetDatabase.Refresh();
        }
    }
}
