# BASS audio backend

The editor uses BASS 2.4 for playback and AffStretch for native pitch-preserving slow playback. Unity Audio is disabled in ProjectSettings/AudioManager.asset.

## Sources

The original BASS, BASS_FX and BASSOPUS libraries were copied from AffdataPlay's `Assets/Plugins/Un4seen.Bass`, with import settings restricted to their matching platform. The narrow C# ABI follows AffdataPlay's ManagedBass bindings. Windows x64 FLAC cache support uses official Un4seen binaries: [BASSenc 2.4.17](https://www.un4seen.com/files/bassenc24.zip), [BASSenc_FLAC 2.4.5](https://www.un4seen.com/files/bassenc_flac24.zip) and [BASSFLAC 2.4.6](https://www.un4seen.com/files/bassflac24.zip). macOS uses the ARM64 slices from the corresponding official [BASSenc](https://www.un4seen.com/files/bassenc24-osx.zip), [BASSenc_FLAC](https://www.un4seen.com/files/bassenc_flac24-osx.zip) and [BASSFLAC](https://www.un4seen.com/files/bassflac24-osx.zip) archives. The new macOS audio plugins and macOS CI player target Apple Silicon only.

These libraries retain their upstream licenses; this project's license does not relicense them. BASSenc, BASSenc_FLAC and BASSFLAC are free to use with BASS under their respective terms; BASS has its own license. The FLAC add-ons include libFLAC 1.5.0. Their upstream license and credit text, the libFLAC notice, and the Signalsmith licenses used by [AffStretch](../Native/AffStretch/README.md) are shipped in [`Assets/StreamingAssets/ThirdPartyNotices.txt`](../Assets/StreamingAssets/ThirdPartyNotices.txt).

## Playback

- Music: file-backed BASS streams avoid retaining the encoded song in managed memory. At 75%, 50% and 25%, AffStretch renders pitch-preserving audio live while a FLAC cache is generated, then cached playback streams from disk. Native slow-playback binaries are supplied for Windows x64 and macOS ARM64; if they are unavailable, the fallback slows the original stream by reducing its frequency and pitch. Seeking, pause/resume and delayed playback use BASS plus a monotonic clock, independent of Unity Audio.
- Hit sounds: cached native samples with up to 32 overlapping voices per clip. Tap, Hold, Arc, ArcTap and Slide use the same existing judgement triggers.
- Custom `*_wav` effects and shutter sounds keep their previous music-volume routing; ordinary hits use effect volume. Volume controls update active voices.
- Skin audio: built-in sounds load by filename from `Assets/StreamingAssets/Audio` at runtime; custom `Skin/Sound/*.wav` overrides retain priority. No serialized audio references, BassClip assets, TextAssets or Unity audio decoding are used. Runtime encoded bytes and BASS samples are cached after loading.
- Streams, sample caches and pinned memory are released on replacement, scene teardown, Play Mode exit and domain reload.

Removed the old AudioSource/AudioListener components, pitch AudioMixer, Unity audio tween module, and NAudio/NLayer/NVorbis loader dependencies.

The Audio settings page provides a one-click slow-playback cache cleanup action. It cancels queued renders, releases any cached playback file by switching to live pitch-preserving playback at the same position, and removes generated FLAC files without touching settings or charts. macOS uses the writable system cache directory rather than the signed application bundle.

Validation on Apple Silicon covers native live rendering, FLAC encoding and playback, rate changes and seeks, cache cleanup, and cancellation before BASS shutdown. Windows binaries are supplied but were not executed on this Mac.

Default shutter visuals use Arcade Alpha `Assets/Textures/Shutter/New/shutter_l.png` and `shutter_r.png`. Opening lasts 0.3 seconds with power-1.5 ease-in; closing lasts 0.75 seconds with OutCubic. Pivot motion preserves the existing layout and the same full-panel travel distance.

Shutter audio matches Alpha’s new-skin scene references: `Assets/Audios/shutter_open.wav` and `shutter_close.wav`, copied without conversion to StreamingAssets.
