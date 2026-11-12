# AffStretch slow-playback plugin

`AffStretch.cpp` renders 75% / 50% / 25% playback by resampling to the slower speed,
then shifting pitch back up with Signalsmith Stretch. The
processing delay is measured with a test click and trimmed, so output sample n is original
time n x speed. `SlowAudioCache.cs` calls it on a background thread and caches FLAC files.

Signalsmith Stretch and its Signalsmith Linear dependency are vendored under MIT licenses.
The pinned commits and required headers are listed in [ThirdParty/README.md](../ThirdParty/README.md).
Their license texts are also included in the player's
[third-party notices](../../Assets/StreamingAssets/ThirdPartyNotices.txt).

Rebuild `Assets/Plugins/AffStretch/AffStretch.dll` (Windows x64; depends only on the UCRT):

```sh
pip install ziglang
python Native/AffStretch/build.py
```
