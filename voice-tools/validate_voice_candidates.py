#!/usr/bin/env python3
"""Report duration, pitch, and local Whisper transcripts for generated candidates."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import soundfile as sf


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = PROJECT_ROOT / "audio" / "voice" / "candidates"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--no-asr", action="store_true")
    args = parser.parse_args()

    whisper_model = None
    if not args.no_asr:
        import whisper

        whisper_model = whisper.load_model(
            "small",
            device="cuda",
            download_root=str(PROJECT_ROOT / ".voice-runtime" / "whisper-cache"),
        )

    import pyworld
    from scipy.signal import resample_poly

    for path in sorted(args.input.glob("*.wav")):
        audio, sample_rate = sf.read(path, dtype="float32")
        if audio.ndim > 1:
            audio = audio.mean(axis=1)
        duration = len(audio) / sample_rate
        f0, times = pyworld.harvest(audio.astype(np.float64), sample_rate)
        f0 = pyworld.stonemask(audio.astype(np.float64), f0, times, sample_rate)
        voiced = f0[f0 > 45]
        median_f0 = float(np.median(voiced)) if voiced.size else 0.0

        transcript = ""
        if whisper_model is not None:
            audio_16k = resample_poly(audio, 16000, sample_rate).astype(np.float32)
            result = whisper_model.transcribe(
                audio_16k,
                language="zh",
                fp16=True,
                temperature=0,
                condition_on_previous_text=False,
            )
            transcript = str(result.get("text", "")).strip()
        print(f"{path.name}\t{duration:.2f}s\tF0={median_f0:.1f}Hz\t{transcript}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
