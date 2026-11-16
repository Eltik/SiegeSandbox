RECORDED SOUNDS

Every sound in the sandbox has a synthesized stand-in built into the code. To replace one with
a real recording, make a folder here named after the sound and put audio files in it
(.wav, .ogg, .mp3 or .aiff). Each file in the folder is used as a variation, picked at random.

    Resources/Sfx/GunShot/shot_01.wav
    Resources/Sfx/GunShot/shot_02.wav
    Resources/Sfx/Footstep/boot_concrete_01.wav

Nothing else needs changing: positioning, routing round walls, muffling and reverb tails are
applied to whatever is in the folder.

Sound names:

  Weapon      GunShot  ShellCasing  DryFire  MagOut  MagIn  MagDrop  BoltBack  BoltForward
  Movement    Footstep  NpcFootstep  Crawl  Land  Jump  Cloth  GearRattle  MeleeSwing
  Impacts     ImpactHard  ImpactWood  ImpactSoftWall  ImpactFlesh  Headshot  HitTick
  Destruction WallPunch  PlankBreak  BarricadeCollapse  Hammer  BarricadeUnroll  Pry  BodyFall
  Reflections TailSmall  TailRoom  TailOutdoor  Echo

Tips
  - Mono files, trimmed tight to the start of the sound. Stereo files lose their positioning.
  - GunShot should be the dry shot only (about 0.3 s). The room is added separately from the
    Tail sounds, chosen by where the gun is fired.
  - Tail* are the reverb-only part of a shot in a small room, a hall, and outdoors.
