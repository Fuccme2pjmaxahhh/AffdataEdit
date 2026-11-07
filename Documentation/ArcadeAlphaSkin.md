# Built-in Arcade Alpha skin

Copied from `/Volumes/T7/ReferenceProject/Arcade-Alpha/Assets`.
Existing texture GUIDs are retained; JPG textures are decoded with macOS sips and stored as PNG without further lossy compression.
Arc colors use the Normal palette from `Scripts/Gameplay/Utility/ColorLibrary.cs`.
Long-particle colors use Alpha runtime `LongNoteJudgeEffects` and its two-gradient start-color distribution.
Light/Conflict hit atlases use 4x4 frames; the SFX atlas uses the existing 6x5 setup.
Tap effects retain VFX Graph. Arc/Hold effects now use Alpha’s ParticleSystem prefab and shader through the existing position/skin anchors.

These imported resources retain their original owners and licenses; the project MIT license does not relicense them.

| Built-in asset | Alpha source asset |
|---|---|
| `Note/TapNote/TapNoteLight.png` | `LowiroLimited/Note/Tap/TapNote.png` |
| `Note/TapNote/TapNoteDark.png` | `LowiroLimited/Note/Tap/TapNoteDark.png` |
| `Note/HoldNote/HoldNoteLight.png` | `LowiroLimited/Note/Hold/HoldNote.png` |
| `Note/HoldNote/HoldNoteDark.png` | `LowiroLimited/Note/Hold/HoldNoteDark.png` |
| `Note/HoldNote/HoldNoteLightHighlight.png` | `LowiroLimited/Note/Hold/HoldNoteHighlight.png` |
| `Note/HoldNote/HoldNoteDarkHighlight.png` | `LowiroLimited/Note/Hold/HoldNoteDarkHighlight.png` |
| `Note/ArcTap/ArcTapLight.png` | `Textures/Gameplay/Note/tap_l.png` |
| `Note/ArcTap/ArcTapDark.png` | `Textures/Gameplay/Note/tap_d.png` |
| `Note/ArcBody/ArcBody.png` | `LowiroLimited/Note/Arc/arc_body.png` |
| `Note/ArcBody/ArcBodyHighlight.png` | `LowiroLimited/Note/Arc/arc_body_hi.png` |
| `Note/ArcCap.png` | `Textures/Gameplay/5.0/arc_cap.png` (current 84×84 diamond; uncompressed) |
| `Note/HeightIndicator.png` | `LowiroLimited/Note/Arc/height_indicator.png` |
| `Particle/ParticleArc.png` | `Textures/Gameplay/Effect/particle_arc.png` |
| `Particle/ParticleSfxTap.png` | `Scripts/Gameplay/EffectParticle/note_sfx.png` |
| `Particle/ParticleNote/NoteParticles.png` | `LowiroLimited/Particle/note_light.png` |
| `Particle/ParticleNote/NoteParticlesConflict.png` | `LowiroLimited/Particle/note_conflict.png` |
| `Background/BaseLight.png` | `Textures/Gameplay/Backgrounds/BaseLight.jpg` |
| `Background/BaseConflict.png` | `Textures/Gameplay/Backgrounds/BaseConflict.jpg` |
| `BackgroundDarken.png` | `Textures/Gameplay/Backgrounds/bg_darken.png` |
| `Note/SfxArcTap/SfxArcTapNoteLight.png` | `Textures/Gameplay/Note/SFXArcTap/sfx_l_note.jpg` |
| `Note/SfxArcTap/SfxArcTapCoreLight.png` | `Textures/Gameplay/Note/SFXArcTap/sfx_l_core.jpg` |
| `Note/SfxArcTap/SfxArcTapNoteDark.png` | `Textures/Gameplay/Note/SFXArcTap/sfx_d_note.jpg` |
| `Note/SfxArcTap/SfxArcTapCoreDark.png` | `Textures/Gameplay/Note/SFXArcTap/sfx_d_core.jpg` |

## Validation

Unity 6000.6.0f1 imported the assets and compiled the scripts with no console errors.
Play mode startup succeeded; the Game view background was checked in `Captures/alpha-skin.png`.
The loaded scene contains both particle texture references and the Alpha Arc palette.
All imported texture dimensions match their source; the 17 PNG source files match decoded pixels exactly.
The six JPEG conversions differ from Pillow decoding by at most 4/255 per channel due to decoder differences.
Full chart playback and a standalone rebuild were not performed for this asset change.

## Scroll speed and particle blending follow-up

- Scroll velocity now uses the stored speed directly (UI speed × 30), matching Alpha's `bpm × (highSpeed × 180 / baseBpm) / 6`. Removed the previous integer division and 2.65 coefficient.
- Tap shader keeps RGB premultiplication and uses SrcAlpha / OneMinusSrcAlpha, as Alpha's Judge shader/material do.
- Long shader retains Additive blending and now uses the alpha of texture × particle color for both premultiplication and output alpha, matching Alpha's HoldJudge shader.
- Six EditMode travel-distance regression cases passed, including fine increments, maximum speed, doubled BPM and negative BPM. Both Shader Graph assets imported without shader errors.
- Automated full-frame particle capture was inconclusive: the MCP capture reported recursive PlayerLoop execution; no standalone visual comparison was completed.

## Song information and combo UI

- SongInfo.png now uses Alpha's `Textures/Gameplay/UI/Info/Default/Info.png`, retaining the existing asset GUID and skin loading path.
- Adapted the active Alpha scene's newer right panel layout (cover, score, title and composer); retained the existing project controls and five difficulty selectors. The panel stays within the editor viewport.
- Combo uses the same GeosansLight font bytes as Alpha, a 230 px font at half scale, 175/255 opacity (68.6%) on Light/Conflict and a matching-color 1 px outline. The runtime palette, rather than the initial scene text color, is authoritative. A single-pass font-atlas shader composes fill and outline, avoiding the repeated alpha accumulation of Unity Outline. Theme colors retain their authored alpha. Score uses Exo at 110 px / half scale with Alpha's purple outline.
- Text remains Unity UI Text for compatibility with existing managers and inputs. This is a visual adaptation, not the NText native renderer; font metrics/antialiasing can differ slightly. Text boxes allow the taller Unity font line metrics.

- Arc and Slide shadows now share Alpha runtime tints: Light black at 50/255, Conflict white at 20/255. Trace shadows use 30/255 and 10/255 respectively. Premultiplied blending and normal note fade still apply; floor Slides do not draw a duplicate shadow.

## Hit sounds and playback controls

- Ground Tap width is applied to both resident GPU geometry and CPU geometry used for rendering/picking. Mine Taps use Alpha's `Textures/Gameplay/Note/ColorNote/note_mine_light.png` / `note_mine_dark.png` and do not connect to ArcTaps. Non-default Tap/Hold/ArcTap widths are saved with two decimal places.

- The optional AUTO judgement details use Alpha's `Textures/Gameplay/Judge/hit_pure.png`, with its short/long-note offsets, 0.5/0.3 s fade and 0.4 s upward motion. The setting is saved in `settings.json`; Tap, ArcTap, Hold, Arc and Slide judgement points share the pooled display.

- Default Tap/Arc WAV files in StreamingAssets/Audio match Alpha's Resources/Audio/tap.wav.bytes and arc.wav.bytes. They load by path at runtime; custom skin overrides are preserved.
- The four Play/Pause normal and pressed sprites come from Alpha's Textures/Gameplay/UI/Pause/Default.
- The difficulty rating input is restricted to the area to the right of all five difficulty buttons; it no longer intercepts BYD/ETR pointer events.

## Arc/Hold particle alignment

- Reused `Prefabs/Player/LongNoteEffectPrefab.prefab` and `Shader/Judge/HoldJudge.shader` in `Resources/AlphaLongNote`, with local material/texture references. The particle modules are preserved: 10.5 particles/s, 0.49 s lifetime, maximum 5 particles, 18.8–20.6 starting size, original shrink curve and velocity modules.
- Uses runtime Normal colors `(0.4,0.2,0.4,1)` / `(0.4,0.2,0.8,1)`, with Alpha’s two-gradient RGB distribution and 0.1 RGB variation; its CreateGradient helper keeps alpha at 1. Color-over-lifetime remains disabled as in the source prefab.
- Matches `SrcAlpha One` blending with RGB premultiplication. Converts both size and velocity from Alpha's UI camera world units (orthographic size 20), including 1.1 emitter scale and aspect correction. Uses local simulation on the camera-facing effect plane so moving hit points do not leave a trail (intentional behavior requested for AffdataEdit).
- Arc, integer Hold and FloatLane Hold share one adapter: preview/subsequent-start prewarming, fresh-playback cold start, cancellable 200 ms stop delay, and clear/reset on seek/pause. Removed the former extra one-second simulation on start/stop.
- Existing VFX components remain serialized as skin and position anchors but are disabled for long-note rendering. Custom skin color/texture changes also refresh the ParticleSystem.
- A non-prewarmed playback start emits one particle immediately, avoiding the first emission interval's dark gap. Continuous emission, pooled reuse and local no-trail simulation keep their existing behavior.

### Particle adapter corrections

- Removed the selection rendering-layer bit inherited from Alpha's all-bits prefab mask. Without this, the editor mistakenly drew orange selection outlines around hit particles.
- Removed per-anchor root objects. A single `Long note particles` pool under the effect manager lazily allocates instances on hits and reuses them after the 200 ms stop delay.
- Keep Local scaling. Simulation is local to the moving hit point to avoid residual trails. Both size and velocity use the UI camera’s 40-unit visible height: Local scaling ignores the CanvasScaler parent. Dividing size by 1080 incorrectly made particles 27 times too small.
- Update hit positions before starting/prewarming in LateUpdate. Always simulate active particles so an initial offscreen bound cannot freeze their emission.
- Billboard alignment faces the gameplay camera, matching Alpha's straight-facing UI camera.
- Match the fixed alpha of Alpha's CreateGradient helper.

### Targeted verification (2026-09-15)

- Compiled and checked the Unity console: no warnings or errors after the final refresh.
- Inspected `Captures/particle-alpha-size.png`: both the sky Arc and lane Hold emit visible particles at their hit positions. This is a current-project check, not a side-by-side Alpha screenshot comparison.
- Seeking away and back retained one pool with two instances: active count 2 → 0 → 2, without creating additional roots or instances.

- No-trail adjustment: verified both live Arc/Hold systems use Local simulation; moving the emitter by one world unit moves existing particles by the same amount. Compile/runtime console checks reported no warnings or errors.
