// AffdataEdit slow-playback renderer: Arcade Alpha style (resample to the slower speed,
// then shift pitch back up with Signalsmith Stretch). Offline/streaming, delay-trimmed:
// output sample n corresponds exactly to original time n * speed.
#include "signalsmith-stretch/signalsmith-stretch.h"
#include <cmath>
#include <vector>
#include <algorithm>
#include <memory>
#include <map>
#include <mutex>
#include <tuple>

#if defined(_WIN32)
#define AFF_EXPORT extern "C" __declspec(dllexport)
#else
#define AFF_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace {
constexpr int HalfTaps = 24, Phases = 1024;
constexpr double Pi = 3.14159265358979323846;

double besselI0(double x) { double s = 1, t = 1; for (int k = 1; k < 40; ++k) { t *= (x / (2 * k)) * (x / (2 * k)); s += t; } return s; }

// Windowed-sinc interpolator (upsampling only, speed <= 1), zero delay.
struct Resampler {
    int channels; double step; double pos = 0; long long base = -HalfTaps;
    std::vector<std::vector<float>> buf; std::vector<float> table;
    Resampler(int ch, double speed) : channels(ch), step(speed), buf(ch, std::vector<float>(HalfTaps, 0.f)), table((Phases + 1) * 2 * HalfTaps) {
        const double cutoff = 0.97, beta = 8.0, norm = besselI0(beta);
        for (int p = 0; p <= Phases; ++p)
            for (int k = 0; k < 2 * HalfTaps; ++k) {
                double x = (k - HalfTaps + 1) - p / double(Phases);
                double w = std::fabs(x) >= HalfTaps ? 0 : besselI0(beta * std::sqrt(1 - (x / HalfTaps) * (x / HalfTaps))) / norm;
                double s = x == 0 ? 1 : std::sin(Pi * cutoff * x) / (Pi * cutoff * x);
                table[p * 2 * HalfTaps + k] = float(cutoff * s * w);
            }
    }
    void push(const float *interleaved, int frames) {
        for (int c = 0; c < channels; ++c) { auto &b = buf[c]; size_t o = b.size(); b.resize(o + frames); for (int i = 0; i < frames; ++i) b[o + i] = interleaved[i * channels + c]; }
    }
    // Produce as many outputs as the buffered input allows (up to limit).
    int pull(std::vector<std::vector<float>> &out, int limit) {
        int n = 0; long long have = base + (long long)buf[0].size();
        while (n < limit) {
            long long i0 = (long long)std::floor(pos); double frac = pos - i0;
            if (i0 + HalfTaps >= have) break;
            double fp = frac * Phases; int p = int(fp); float t = float(fp - p);
            const float *a = &table[p * 2 * HalfTaps], *b2 = &table[(p + 1) * 2 * HalfTaps];
            size_t first = size_t(i0 - HalfTaps + 1 - base);
            for (int c = 0; c < channels; ++c) {
                const float *s = &buf[c][first]; float acc = 0;
                for (int k = 0; k < 2 * HalfTaps; ++k) acc += s[k] * (a[k] + t * (b2[k] - a[k]));
                out[c].push_back(acc);
            }
            ++n; pos += step;
        }
        long long keep = (long long)std::floor(pos) - HalfTaps;  // discard consumed input
        if (keep > base) { size_t d = size_t(keep - base); for (auto &b : buf) b.erase(b.begin(), b.begin() + d); base = keep; }
        return n;
    }
};

struct Chain {
    int channels; double speed;
    Resampler rs; signalsmith::stretch::SignalsmithStretch<float> st{12345};
    std::vector<std::vector<float>> mid, out;
    std::vector<float *> ip, op;
    Chain(int ch, int sr, double sp) : channels(ch), speed(sp), rs(ch, sp), mid(ch), out(ch), ip(ch), op(ch) {
        st.presetDefault(ch, float(sr)); st.setTransposeFactor(float(1 / sp));
    }
    // Resample what is buffered (up to limit), run it through Stretch, append to `out`.
    int run(int limit) {
        for (auto &m : mid) m.clear();
        int n = rs.pull(mid, limit);
        if (n <= 0) return 0;
        for (int c = 0; c < channels; ++c) { size_t o = out[c].size(); out[c].resize(o + n); ip[c] = mid[c].data(); op[c] = out[c].data() + o; }
        st.process(ip.data(), n, op.data(), n);
        return n;
    }
};

struct Render {
    Chain chain; long long inFrames = 0, produced = 0, delay = 0;
    Render(int ch, int sr, double sp) : chain(ch, sr, sp) {}
    // Copies whatever is past the trimmed delay into interleaved `dst`.
    int drain(float *dst, int cap, long long limitTotal) {
        int ch = chain.channels; auto &o = chain.out; long long avail = (long long)o[0].size();
        long long skip = std::max<long long>(0, std::min(avail, delay - produced));
        long long usable = avail - skip;
        long long already = std::max<long long>(0, produced - delay);
        if (limitTotal >= 0) usable = std::min(usable, std::max<long long>(0, limitTotal - already));
        int n = int(std::min<long long>(usable, cap));
        for (int i = 0; i < n; ++i) for (int c = 0; c < ch; ++c) dst[i * ch + c] = o[c][skip + i];
        long long consumed = skip + n; produced += consumed;
        for (auto &v : o) v.erase(v.begin(), v.begin() + consumed);
        return n;
    }
};

long long measureDelay(int ch, int sr, double sp) {
    Chain c(ch, sr, sp);
    int total = sr * 3, click = sr;
    std::vector<float> in(size_t(total) * ch, 0.f);
    for (int k = 0; k < ch; ++k) in[size_t(click) * ch + k] = 1.f;
    c.rs.push(in.data(), total);
    while (c.run(1 << 15) > 0) {}
    int best = 0; float bv = -1;
    for (size_t i = 0; i < c.out[0].size(); ++i) { float v = 0; for (int k = 0; k < ch; ++k) v += std::fabs(c.out[k][i]); if (v > bv) { bv = v; best = int(i); } }
    long long d = best - (long long)std::llround(click / sp);
    return d > 0 ? d : 0;
}

// The delay depends only on the format and speed. Live playback creates a renderer at every
// play or seek, so measure it once per format and speed instead of each time.
long long calibrate(int ch, int sr, double sp) {
    static std::mutex lock;
    static std::map<std::tuple<int, int, double>, long long> known;
    auto key = std::make_tuple(ch, sr, sp);
    {
        std::lock_guard<std::mutex> hold(lock);
        auto found = known.find(key);
        if (found != known.end()) return found->second;
    }
    long long d = measureDelay(ch, sr, sp);
    std::lock_guard<std::mutex> hold(lock);
    known[key] = d;
    return d;
}
}

// Returns 0 on bad arguments. speed must be in (0, 1).
AFF_EXPORT void *AffStretch_Create(int channels, int sampleRate, float speed) {
    if (channels < 1 || channels > 8 || sampleRate < 8000 || !(speed > 0.05f && speed < 1.f)) return nullptr;
    try { std::unique_ptr<Render> r(new Render(channels, sampleRate, speed)); r->delay = calibrate(channels, sampleRate, speed); return r.release(); }
    catch (...) { return nullptr; }
}
// The trimmed processing delay in output frames (diagnostics).
AFF_EXPORT int AffStretch_Delay(void *h) { return h ? int(static_cast<Render *>(h)->delay) : -1; }
// Feeds interleaved float input; writes up to `capacity` interleaved output frames. Returns frames written.
// Size capacity as frames / speed + 4096 so nothing is held back.
AFF_EXPORT int AffStretch_Process(void *h, const float *input, int frames, float *output, int capacity) {
    if (!h || frames < 0 || capacity < 0) return -1;
    auto *r = static_cast<Render *>(h);
    try {
        if (frames > 0) { r->chain.rs.push(input, frames); r->inFrames += frames; }
        while (r->chain.run(1 << 14) > 0) {}
        return r->drain(output, capacity, -1);
    } catch (...) { return -1; }
}
// After the last input: writes the remaining tail. Call until it returns 0. The total output
// over all calls is exactly round(inputFrames / speed).
AFF_EXPORT int AffStretch_Finish(void *h, float *output, int capacity) {
    if (!h || capacity < 0) return -1;
    auto *r = static_cast<Render *>(h);
    try {
        long long target = std::llround(r->inFrames / r->chain.speed);
        long long need = target + r->delay;            // samples Stretch must still emit
        int ch = r->chain.channels; std::vector<float> zeros(size_t(4096) * ch, 0.f);
        long long emitted = r->produced + (long long)r->chain.out[0].size();
        while (emitted < need && (long long)r->chain.out[0].size() < capacity + r->delay) {
            r->chain.rs.push(zeros.data(), 4096);
            int n = r->chain.run(int(std::min<long long>(need - emitted, 1 << 14)));
            emitted += n;
        }
        return r->drain(output, capacity, target);
    } catch (...) { return -1; }
}
AFF_EXPORT void AffStretch_Destroy(void *h) { delete static_cast<Render *>(h); }
