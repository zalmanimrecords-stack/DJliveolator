#!/usr/bin/env python3
"""Offline song-structure segmentation for Liveolator (doc 32).

Contract (locked — the C# PythonSongStructureAnalyzer parses this):
    argv[1] = path to an audio file (WAV or any format librosa/audioread can read)
    stdout  = JSON ONLY:
        {"sections":[{"startSeconds":<float>,"label":"<intro|buildup|drop|breakdown|outro|section>"}],
         "analyzedWith":"librosa <ver>"}
    stderr  = all diagnostics (never stdout)
    exit 0 on success; non-zero on any failure (the C# side then returns null).

Method: librosa laplacian segmentation (McFee & Ellis) over a CQT-derived recurrence matrix
to find boundaries, then a coarse energy/percussive heuristic to label each segment. librosa
is ISC-licensed (clean for a distributed closed-source app).
"""
import json
import sys


def _fail(message):
    print(message, file=sys.stderr)
    sys.exit(1)


def _segment(path):
    import numpy as np
    import librosa

    y, sr = librosa.load(path, sr=22050, mono=True)
    if y.size == 0:
        return [], librosa.__version__

    duration = librosa.get_duration(y=y, sr=sr)

    # --- Boundaries via Laplacian structural segmentation -----------------------------------------
    # Recurrence matrix over chroma+CQT, smoothed, then a spectral-clustering (Laplacian) cut.
    bpm_hop = 512
    chroma = librosa.feature.chroma_cqt(y=y, sr=sr, hop_length=bpm_hop)
    # Beat-synchronous to make the recurrence matrix musically meaningful.
    _, beats = librosa.beat.beat_track(y=y, sr=sr, hop_length=bpm_hop, trim=False)
    if beats.size < 4:
        # Too short / arrhythmic to read structure — emit a single intro section.
        return [{"startSeconds": 0.0, "label": "intro"}], librosa.__version__

    sync = librosa.util.sync(chroma, beats, aggregate=np.median)
    rec = librosa.segment.recurrence_matrix(sync, mode="affinity", sym=True)
    # Path-enhance and combine with a local-timbre matrix (the standard laplacian recipe).
    mfcc = librosa.feature.mfcc(y=y, sr=sr, hop_length=bpm_hop)
    mfcc_sync = librosa.util.sync(mfcc, beats)
    path = librosa.segment.recurrence_matrix(mfcc_sync, mode="affinity", sym=True, width=3)
    combined = (rec + path) / 2.0

    # Agglomerative boundary detection on the combined affinity → boundary beat indices.
    n_segments = int(max(2, min(10, round(duration / 30.0))))
    bound_beats = librosa.segment.agglomerative(combined, n_segments)
    bound_frames = beats[np.clip(bound_beats, 0, len(beats) - 1)]
    bound_times = librosa.frames_to_time(bound_frames, sr=sr, hop_length=bpm_hop)
    bound_times = sorted(set([0.0] + [float(t) for t in bound_times if t > 0.0]))

    # --- Label each segment by energy / percussive content ---------------------------------------
    rms = librosa.feature.rms(y=y, hop_length=bpm_hop)[0]
    rms_t = librosa.frames_to_time(np.arange(len(rms)), sr=sr, hop_length=bpm_hop)
    y_perc = librosa.effects.percussive(y)
    perc = librosa.feature.rms(y=y_perc, hop_length=bpm_hop)[0]

    def seg_energy(series, t0, t1):
        mask = (rms_t >= t0) & (rms_t < t1)
        return float(series[mask].mean()) if mask.any() else 0.0

    max_rms = float(rms.max()) if rms.size else 1.0
    max_perc = float(perc.max()) if perc.size else 1.0
    sections = []
    edges = bound_times + [duration]
    for i in range(len(bound_times)):
        t0, t1 = bound_times[i], edges[i + 1]
        e = seg_energy(rms, t0, t1) / max_rms if max_rms > 0 else 0.0
        p = seg_energy(perc, t0, t1) / max_perc if max_perc > 0 else 0.0

        if i == 0:
            label = "intro"
        elif i == len(bound_times) - 1:
            label = "outro"
        elif p >= 0.6 and e >= 0.6:
            label = "drop"
        elif p < 0.35 and e >= 0.3:
            label = "breakdown"
        elif i + 1 < len(bound_times) and p > seg_energy(perc, t1, edges[i + 2] if i + 2 < len(edges) else duration) / max_perc:
            label = "buildup"
        else:
            label = "section"
        sections.append({"startSeconds": round(t0, 3), "label": label})

    return sections, librosa.__version__


def main():
    if len(sys.argv) < 2:
        _fail("usage: analyze_structure.py <audio-file>")

    path = sys.argv[1]
    try:
        sections, version = _segment(path)
    except ImportError as exc:
        _fail("librosa/numpy not installed: %s" % exc)
    except Exception as exc:  # noqa: BLE001 - any failure is reported on stderr, non-zero exit
        _fail("structure analysis failed for %r: %s" % (path, exc))

    json.dump({"sections": sections, "analyzedWith": "librosa %s" % version}, sys.stdout)


if __name__ == "__main__":
    main()
