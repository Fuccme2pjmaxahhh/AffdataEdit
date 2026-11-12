# Native audio dependencies

AffStretch uses a minimal vendored subset of these upstream projects:

| Dependency | Origin | Pinned commit | License |
| --- | --- | --- | --- |
| Signalsmith Stretch | https://github.com/Signalsmith-Audio/signalsmith-stretch | `88061fd4eda56a2353c5d65c74aa3f4ccf76f9da` | [MIT](signalsmith-stretch/LICENSE.txt) |
| Signalsmith Linear | https://github.com/Signalsmith-Audio/linear | `7bb1c8f36794d6980080ce817c91f8da8fe56a66` (tag `0.6.6`) | [MIT](linear/LICENSE.txt) |

The checked-in third-party source files are ordinary files, not Git submodules:

```text
signalsmith-stretch/signalsmith-stretch.h
signalsmith-stretch/include/signalsmith-stretch/signalsmith-stretch.h
signalsmith-stretch/LICENSE.txt
linear/stft.h
linear/fft.h
linear/include/signalsmith-linear/stft.h
linear/LICENSE.txt
```

These files preserve the upstream include layout and are sufficient for
[`Native/AffStretch/build.py`](../AffStretch/build.py), which uses the default FFT
implementation without optional acceleration macros. Upstream demos, audio samples,
tests, build systems, optional platform backends and Git metadata are not vendored.
Enabling an optional acceleration backend requires its additional headers and dependencies.

Keep both upstream license files with the source. The same MIT texts are included in
[`Assets/StreamingAssets/ThirdPartyNotices.txt`](../../Assets/StreamingAssets/ThirdPartyNotices.txt)
so player builds carry their attribution.
