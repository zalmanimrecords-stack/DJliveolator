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


def _separate(input_path, output_dir):
    import numpy as np
    import soundfile as sf
    import torch
    from openunmix import predict, utils

    info = sf.info(input_path)
    audio, rate = sf.read(input_path, dtype="float32", always_2d=True)
    # openunmix wants (channels, samples) as a torch tensor at the model's sample rate.
    tensor = torch.as_tensor(audio.T, dtype=torch.float32)

    separator = utils.load_separator(model_str_or_path=MODEL, targets=list(STEMS))
    estimates = predict.separate(audio=tensor, rate=rate, separator=separator)

    os.makedirs(output_dir, exist_ok=True)
    paths = {}
    for name in STEMS:
        if name not in estimates:
            _fail("model did not produce stem %r" % name)
        # estimate shape is (nb_samples=1?, channels, samples) or (channels, samples) - normalize.
        data = estimates[name].detach().cpu().numpy()
        data = np.squeeze(data)
        if data.ndim == 1:
            out = data
        else:
            out = data.T  # soundfile wants (samples, channels)
        out_path = os.path.join(output_dir, "%s.flac" % name)
        sf.write(out_path, out, int(rate), format="FLAC")
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
