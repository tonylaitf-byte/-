#!/usr/bin/env python3
"""Generate dry Mandarin voices for the Unity glove-puppet character."""

from __future__ import annotations

import argparse
import random
from dataclasses import dataclass
from pathlib import Path

from generate_taiwanese_voice import _save_wav


PROJECT_ROOT = Path(__file__).resolve().parent
MODEL_DIR = (
    PROJECT_ROOT
    / ".voice-runtime"
    / "models"
    / "Qwen3-TTS-12Hz-0.6B-CustomVoice"
)
OUTPUT_DIR = PROJECT_ROOT / "audio" / "voice_mandarin"

CHARACTER_STYLE = (
    "五十歲成熟男性，固定低男中音，厚實胸腔共鳴。"
    "用力度和節奏演出布袋戲武生感，不要升高音調。"
)


@dataclass(frozen=True)
class VoiceSpec:
    key: str
    text: str
    performance: str
    target_seconds: float


VOICES: dict[str, VoiceSpec] = {
    "sword_01": VoiceSpec(
        "sword_01",
        "看招！吃我一劍！",
        "正在正面揮劍進攻。全句保持低男中音。看招低沉警告，短暫停頓；吃我一劍由胸腔增加力度而不是升高音調，最後劍字厚重向下收住。",
        3.2,
    ),
    "sword_02": VoiceSpec(
        "sword_02",
        "劍出無情。接招！",
        "劍出無情說得緩慢、低沉而冷酷，無情向下降調；停頓後接招突然有力爆發，短促收尾。",
        3.4,
    ),
    "greeting_01": VoiceSpec(
        "greeting_01",
        "久仰久仰，幸會！",
        "向值得尊敬的江湖人物拱手鞠躬。前段沉穩從容並帶一點豪爽笑意，停頓後真誠加重幸會。",
        3.2,
    ),
    "greeting_02": VoiceSpec(
        "greeting_02",
        "幸會幸會，請多指教！",
        "初次正式見面拱手行禮。重複要自然穩重；請多指教稍慢、尊敬誠懇，句尾沉穩落下。",
        3.6,
    ),
    "drink_01": VoiceSpec(
        "drink_01",
        "來！我們一起喝一杯！",
        "在江湖酒館舉杯邀請好友。全句保持低男中音。來字由胸腔厚實召喚；停頓後爽朗溫暖、帶輕微笑意並向下有力收尾。",
        3.3,
    ),
    "drink_02": VoiceSpec(
        "drink_02",
        "好酒！再來一杯！",
        "剛喝完好酒。全句保持低男中音。好酒以低沉胸腔聲表現滿足，不可用升高音調表現驚喜；停頓後豪爽要求再來一杯，句尾向下收住。",
        3.3,
    ),
    "landscape_01": VoiceSpec(
        "landscape_01",
        "今晚的月亮真美，讓人捨不得移開目光。",
        "飲酒後獨自抬頭賞月。開頭低沉緩慢，真美帶明顯欣賞並稍微拉長；停頓後放鬆悠然、略帶微醺感。",
        5.0,
    ),
    "landscape_02": VoiceSpec(
        "landscape_02",
        "明月高掛天際，今晚真是一番好景色。",
        "歷經江湖的俠客持酒賞月。前段低沉緩慢，停頓後帶欣賞與滿足感，好景色放慢並沉穩收尾。",
        5.0,
    ),
    "alert_01": VoiceSpec(
        "alert_01",
        "嗯？你想做什麼？",
        "突然察覺異狀。嗯要短促警覺；後句壓低聲音，冷靜質問、戒備並帶輕微威嚇，乾脆收住。",
        4.0,
    ),
    "alert_02": VoiceSpec(
        "alert_02",
        "嗯！你是什麼人？想做什麼？",
        "陌生人突然出現。嗯短促警覺；你是什麼人稍重質問，停頓後再壓低聲音威嚇，冷靜準備拔劍。",
        4.0,
    ),
}

DEFAULT_SEEDS = {
    key: 51000 + index * 101 + sum(ord(char) for char in key)
    for index, key in enumerate(VOICES)
}
SEED_OVERRIDES = {
    "sword_01": 71101,
    "drink_01": 71201,
    "drink_02": 71202,
}

MODEL_CUES = {
    "sword_01": "前句低沉警告，後句用胸腔力度爆發，向下收尾。",
    "sword_02": "前句低沉冷酷，停頓後有力接招，短促收尾。",
    "greeting_01": "沉穩親切地拱手問候，帶微弱笑意。",
    "greeting_02": "正式尊敬地行禮，語速稍慢，沉穩收尾。",
    "drink_01": "低沉豪爽地舉杯邀請，帶輕微笑意。",
    "drink_02": "低沉滿足地讚嘆好酒，豪爽而不升高音調。",
    "landscape_01": "低沉悠然地賞月，真美稍微拉長，帶感慨。",
    "landscape_02": "低沉緩慢地賞月，帶欣賞與滿足感。",
    "alert_01": "短促警覺後壓低聲音質問，乾脆收尾。",
    "alert_02": "短促警覺，冷靜質問後壓低聲音威嚇。",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="產生 Unity 戲偶專案的中文角色語音")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--all", action="store_true", help="產生全部十句")
    group.add_argument("--voice", nargs="+", choices=sorted(VOICES))
    parser.add_argument("--overwrite", action="store_true", help="覆蓋既有中文 WAV")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    selected = list(VOICES.values()) if args.all else [VOICES[key] for key in args.voice]
    pending = []
    for spec in selected:
        output = OUTPUT_DIR / f"{spec.key}.wav"
        if output.exists() and not args.overwrite:
            print(f"[略過] {spec.key}：檔案已存在（使用 --overwrite 才會覆蓋）")
        else:
            pending.append((spec, output))
    if not pending:
        return 0

    if not MODEL_DIR.exists():
        raise RuntimeError(f"找不到 Qwen3-TTS 模型：{MODEL_DIR}")

    import numpy as np
    import torch
    from qwen_tts import Qwen3TTSModel

    print("[模型] Qwen3-TTS-12Hz-0.6B-CustomVoice / Uncle_Fu")
    model = Qwen3TTSModel.from_pretrained(
        str(MODEL_DIR),
        device_map="cuda:0" if torch.cuda.is_available() else "cpu",
        dtype=torch.bfloat16 if torch.cuda.is_available() else torch.float32,
        attn_implementation="sdpa",
    )

    for spec, output in pending:
        seed = SEED_OVERRIDES.get(spec.key, DEFAULT_SEEDS[spec.key])
        random.seed(seed)
        np.random.seed(seed % (2**32 - 1))
        torch.manual_seed(seed)
        if torch.cuda.is_available():
            torch.cuda.manual_seed_all(seed)

        print(f"[生成] {spec.key}: {spec.text}")
        wavs, sample_rate = model.generate_custom_voice(
            text=spec.text,
            language="Chinese",
            speaker="Uncle_Fu",
            instruct=(
                CHARACTER_STYLE + MODEL_CUES[spec.key]
            ),
            do_sample=True,
            temperature=0.65,
            top_p=0.85,
            max_new_tokens=1536,
        )
        chunks = ({"tts_speech": wav, "sample_rate": sample_rate} for wav in wavs)
        duration = _save_wav(chunks, output, spec.target_seconds)
        print(f"[完成] {output} ({duration:.2f} 秒)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
