# Pavouk — underground workshop

The scene object is `Map-Domasov/PavoukuvWorkshop`. Everything is editable Unity geometry, with a local cosmetic controller (`TunnelWorkshop`). No network objects or gameplay bonuses are added.

## Placement and editing

The builder fits the east edge and actual mesh bounds of the user's floor at `Map-Domasov/Tunel/Corridors/Floor_1 (2)`. It leaves the existing floor, branch and ProBuilder doorway untouched. The new room has its own walls, ceiling and a closed door with a rectangular viewing window. The invisible window collider stops characters and projectiles while preserving visibility.

Use **BrosOverwatch → Easter egg → Postavit Pavoukuv workshop** to rebuild after resizing that floor. Rebuilding replaces the generated workshop, including manual edits inside it; it is undoable. The room is a sibling of `Tunel`, so rebuilding the original tunnel does not erase the new room.

Use **Ulozit workshop a scenu** to save the current room to `Assets/Prefab/EasterEggs/PavoukuvWorkshop.prefab` and save the scene. Initial installation also leaves a scene backup in `Temp/WorkshopQA/BeforeWorkshop.unity` (temporary local backup, not a versioned asset).

**Upravit stitky a servery** applies the typography/layout refinement to an existing room without rebuilding its lighting or furniture. It fits every label to its backing, uses the bundled Rajdhani SemiBold SDF font, brands the monitor `msi`, raises the rear sign above the monitor, and moves/turns the server rack toward the window. The font's SIL Open Font License is included in `Assets/Fonts/Workshop/OFL.txt`. The first application keeps a local scene copy in `Temp/WorkshopQA/BeforePolish.unity`.

## Media and animation

- `Assets/Video/chata_timelapse.mp4` plays silently in a looping 1280 × 720 monitor render texture. A still extracted from the supplied video is displayed in Edit Mode. Video decoding pauses while the listener is far away or above ground.
- `Assets/Audio/Music/tunnel_vision.mp3` streams through a spatial source behind the door. Default radius: 15 m; volume: 0.62; low-pass cutoff outside: 1250 Hz. Inside the room, the filter opens to 9000 Hz. It respects the game's master AudioListener volume; it is environmental sound, separate from the main-menu music toggle.
- `surfaceY` is the world-space top of the main ground. At and above that plane the AudioSource is immediately muted and its volume is zero. A short fade below the plane avoids a sudden approach; it never fades out above the cutoff. The source has no reverb-zone send.
- Head bob, torso sway, keyboard taps and mouse drags follow the audio playback clock. BPM defaults to 175, estimated from the supplied track. `beatsPerMinute` and `beatOffsetSeconds` are editable on the root.
- A throttled 8 × 8 GPU readback samples the actual video and drives a soft monitor light. No realtime GI is required. Other colored lighting is steady, with only a gentle slow accent variation.
- The video component is deliberately separate from any Renderer, so the existing `JokerTvBuff` does not treat this monitor as the casino television.
- `WorkshopServerActivity` animates the rack's 28 status LEDs independently with material property blocks. Green disk/network LEDs blink irregularly; amber status LEDs stay mostly lit. Shared materials and room lights are not modified.

## Verification

**Vytvorit nahledy a overit** writes camera renders and static checks to `Temp/WorkshopQA`. **Overit prehravani v Play Mode** enters Play Mode, moves a temporary listener through the doorway, above the surface and inside the room, checks actual audio and video playback / looping / lighting / animation, then returns to Edit Mode. It temporarily disables other listeners only in the disposable Play Mode scene.
