#!/usr/bin/env python3
"""Create a clean fictional male reference for CosyVoice3 from Qwen CustomVoice."""

from pathlib import Path

import soundfile as sf
import torch
from qwen_tts import Qwen3TTSModel


PROJECT_ROOT = Path(__file__).resolve().parents[1]
MODEL_DIR = PROJECT_ROOT / ".voice-runtime" / "models" / "Qwen3-TTS-12Hz-0.6B-CustomVoice"
OUTPUT = PROJECT_ROOT / "voice-tools" / "prompts" / "qwen_uncle_fu_reference.wav"


def main() -> int:
    model = Qwen3TTSModel.from_pretrained(
        str(MODEL_DIR),
        device_map="cuda:0" if torch.cuda.is_available() else "cpu",
        dtype=torch.bfloat16 if torch.cuda.is_available() else torch.float32,
        attn_implementation="sdpa",
    )
    wavs, sample_rate = model.generate_custom_voice(
        text="在下行走江湖多年，今日有緣相逢，請多指教。",
        language="Chinese",
        speaker="Uncle_Fu",
        instruct=(
            "成熟男性，自然中低音，溫暖厚實，語速平穩，咬字清楚。"
            "像真人在安靜錄音室說話，不要喊叫，不要電子音。"
        ),
        do_sample=True,
        temperature=0.8,
        top_p=0.9,
        max_new_tokens=1536,
    )
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    sf.write(OUTPUT, wavs[0], sample_rate, subtype="PCM_16")
    print(OUTPUT)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
