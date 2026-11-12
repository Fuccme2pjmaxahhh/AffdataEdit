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

Rebuild `Assets/Plugins/AffStretch/macOS/libAffStretch.dylib` for Apple Silicon only
with Xcode command-line tools:

```sh
python3 Native/AffStretch/build.py --target macos-arm64
```

The macOS build targets ARM64 and uses an ad-hoc signature. Its FLAC cache companions
are the ARM64 slices of the official [BASSenc](https://www.un4seen.com/files/bassenc24-osx.zip),
[BASSenc_FLAC](https://www.un4seen.com/files/bassenc_flac24-osx.zip) and
[BASSFLAC](https://www.un4seen.com/files/bassflac24-osx.zip) libraries, under
`Assets/Plugins/Bass/macOS`. Their Unity import settings enable only macOS ARM64.
