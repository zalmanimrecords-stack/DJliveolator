#!/usr/bin/env python3
"""Offline stem separation for Liveolator (doc 32 Phase 2).

pip deps: openunmix (pulls torch as a dependency), soundfile (for FLAC writing).
The C# OpenUnmixStemSeparator installs these via the per-user download-on-demand runtime.

Contract (locked - the C# OpenUnmixStemSeparator parses this):
    argv[1] = path to an input audio file (any format openunmix/torchaudio can read)
    argv[2] = output directory (created if missing); stem FLAC files are written here
    stdout  = JSON ONLY:
        {"model":"umxhq","stems":{"drums":"<path>","bass":"<path>","vocals":"<path>","other":"<path>"}}
    stderr  = all diagnostics (never stdout)
    exit 0 on success; non-zero on any failure (the C# side then returns null).

Model: Open-Unmix (umxhq) - code and weights are MIT, safe to bundle/use in a distributed
closed-source app. htdemucs (CC-BY-NC weights) is a future opt-in via a different model id.
"""
import json
import os
import sys

MODEL = "umxhq"
STEMS = ("drums", "bass", "vocals", "other")


def _fail(message):
    print(message, file=sys.stderr)
    sys.exit(1)


# Process the track in blocks with overlap-add instead of all at once: predict.separate holds the
# whole-track STFT plus a 4-source Wiener expansion in memory, which crashes (native OOM / access
# violation) on real-length tracks - only clips of ~1 minute survive. Blocks keep peak memory bounded and
# a windowed overlap-add removes the seam between them, yielding full-length stems for any track length.
BLOCK_SECONDS = 30.0
OVERLAP_SECONDS = 1.0


def _stem_block(estimates, name, block_len):
    """Normalize one stem estimate for a block to (samples, channels)."""
    import numpy as np

    data = np.squeeze(estimates[name].detach().cpu().numpy())
    if data.ndim == 1:
        data = data[:, None]  # mono -> (samples, 1)
    else:
        data = data.T  # (channels, samples) -> (samples, channels)
    return data[:block_len]


def _separate(input_path, output_dir):
    import numpy as np
    import soundfile as sf
    import torch
    from openunmix import predict, utils

    audio, rate = sf.read(input_path, dtype="float32", always_2d=True)  # (samples, channels)
    total, channels = audio.shape
    rate = int(rate)

    separator = utils.load_separator(model_str_or_path=MODEL, targets=list(STEMS))

    block_len = max(int(BLOCK_SECONDS * rate), 1)
    overlap = min(int(OVERLAP_SECONDS * rate), block_len // 2)
    hop = max(block_len - overlap, 1)

    outputs = {name: np.zeros((total, channels), dtype=np.float32) for name in STEMS}
    weights = np.zeros(total, dtype=np.float32)

    start = 0
    while start < total:
        end = min(start + block_len, total)
        this_len = end - start
        # openunmix wants (channels, samples).
        block = torch.as_tensor(audio[start:end].T, dtype=torch.float32)
        estimates = predict.separate(audio=block, rate=rate, separator=separator)

        # Windowed overlap-add: ramp the overlap regions so adjacent blocks sum to ~1 at the seam.
        window = np.ones(this_len, dtype=np.float32)
        if overlap > 0:
            ramp = np.linspace(0.0, 1.0, overlap, endpoint=False, dtype=np.float32)
            if start > 0:
                window[:overlap] = ramp
            if end < total:
                window[this_len - overlap:] = ramp[::-1]

        for name in STEMS:
            if name not in estimates:
                _fail("model did not produce stem %r" % name)
            outputs[name][start:end] += _stem_block(estimates, name, this_len) * window[:, None]
        weights[start:end] += window
        start += hop

    weights[weights == 0.0] = 1.0
    os.makedirs(output_dir, exist_ok=True)
    paths = {}
    for name in STEMS:
        out = outputs[name] / weights[:, None]
        out_path = os.path.join(output_dir, "%s.flac" % name)
        sf.write(out_path, out, rate, format="FLAC")
        paths[name] = out_path
    return paths


def main():
    if len(sys.argv) < 3:
        _fail("usage: separate_stems.py <input-audio-file> <output-dir>")

    input_path = sys.argv[1]
    output_dir = sys.argv[2]
    if not os.path.isfile(input_path):
        _fail("input file not found: %r" % input_path)

    try:
        paths = _separate(input_path, output_dir)
    except ImportError as exc:
        _fail("openunmix/torch/soundfile not installed: %s" % exc)
    except Exception as exc:  # noqa: BLE001 - any failure is reported on stderr, non-zero exit
        _fail("stem separation failed for %r: %s" % (input_path, exc))

    json.dump({"model": MODEL, "stems": paths}, sys.stdout)


if __name__ == "__main__":
    main()
