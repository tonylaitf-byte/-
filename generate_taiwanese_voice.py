#!/usr/bin/env python3
"""Generate dry Taiwanese Minnan character voices for the Unity puppet project."""

from __future__ import annotations

import argparse
import os
import random
import shutil
import sys
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Iterable


PROJECT_ROOT = Path(__file__).resolve().parent
RUNTIME_ROOT = PROJECT_ROOT / ".voice-runtime"
OUTPUT_DIR = PROJECT_ROOT / "audio" / "voice"
DEFAULT_PROMPT = PROJECT_ROOT / "voice-tools" / "prompts" / "qwen_uncle_fu_reference.wav"
CANDIDATE_DIR = OUTPUT_DIR / "candidates"
MAIN_VARIANTS = {
    "sword_01": "b",
    "sword_02": "b",
    "greeting_01": "c",
    "greeting_02": "c",
    "drink_01": "c",
    "drink_02": "c",
    "landscape_01": "c",
    "landscape_02": "c",
    "alert_01": "b",
    "alert_02": "b",
}

QWEN_ROLE = (
    "使用自然台湾闽南语。固定为同一位四十五至五十五岁的台湾男性江湖侠客："
    "成熟中低音，温暖厚实但不要刻意压低，真实胸腔共鸣，少量自然沙哑，咬字清楚。"
    "音质必须自然、干净、像真人录音；禁止电子音、金属感、声码器感、机器人节奏、"
    "新闻播报、广告旁白、年轻明亮声线或尖锐夸张戏腔。"
)

QWEN_VARIANTS = {
    "a": "音色温暖厚实、沉稳有重量，以自然中低音演出，不要过度压低音高。",
    "b": "加强情绪、重音与布袋戏武生舞台张力，但保持真人感与中低音，禁止全程吼叫。",
    "c": "自然电影化演出，成熟从容、有清楚情绪起伏，优先真实音质与连贯语流。",
}

BASE_ROLE = (
    # CosyVoice3 starts speaking long free-form instructions as content. Keep
    # the native prompt deliberately short; age, weight and resonance come
    # primarily from the mature reference speaker rather than post-processing.
    "You are a helpful assistant. 请用闽南话表达。"
)


@dataclass(frozen=True)
class VoiceSpec:
    key: str
    text: str
    performance: str
    model_cue: str
    target_seconds: float
    speed: float = 1.0
    design_instruction: str = ""
    variant_key: str = "c"


@dataclass(frozen=True)
class VariantStyle:
    key: str
    instruction: str
    speed: float
    seed_offset: int


VARIANTS: dict[str, VariantStyle] = {
    "a": VariantStyle(
        "a",
        "请用低沉、自然、沉稳的声音说。",
        0.91,
        304,
    ),
    "b": VariantStyle(
        "b",
        "请用低沉、有强烈情绪起伏的声音说。",
        0.95,
        202,
    ),
    "c": VariantStyle(
        "c",
        "请用低沉、自然、沉稳的声音说。",
        0.91,
        303,
    ),
}

# Specific retries for takes rejected by transcript QA. Keeping these explicit
# makes --all deterministic while avoiding a known extra-words sample.
SEED_OVERRIDES = {
    "sword_01_a": 40101,
    "sword_02_c": 40403,
    "greeting_01_b": 40202,
    "drink_02_a": 40301,
}
COSY_SEED_OFFSET_OVERRIDES = {
    "greeting_01_a": 305,
    "sword_01_c": 202,
    "sword_02_c": 202,
}


VOICES: dict[str, VoiceSpec] = {
    "sword_01": VoiceSpec(
        "sword_01",
        "看招！食我一劍！",
        "正在正面挥剑进攻。看招低沉警告，停顿约零点三秒；食我一剑从胸腔发力并逐渐增强，最后剑字厚重收住。不可全程同音量或吼叫。",
        "前句冷峻警告，后句出剑爆发。",
        3.2,
    ),
    "sword_02": VoiceSpec(
        "sword_02",
        "劍出無情。接招！",
        "剑出无情要慢而冷酷，无情压低并向下降调；停顿约零点五秒，接招突然加快爆发，但仍低沉厚实，句尾短促收住。",
        "前句冷酷压迫，后句突然爆发。",
        3.4,
    ),
    "greeting_01": VoiceSpec(
        "greeting_01",
        "久仰久仰，幸會！",
        "正在向值得尊敬的江湖人物拱手鞠躬。久仰久仰沉稳从容，带微弱豪爽笑意；自然停顿约零点三秒，幸会稍微加重，真诚尊敬。",
        "沉稳亲切，幸会加重。",
        3.2,
    ),
    "greeting_02": VoiceSpec(
        "greeting_02",
        "幸會幸會，請多指教！",
        "第一次正式见面并拱手行礼。幸会幸会自然稳重，不要机械重复；逗号停顿约零点三秒，请多指教稍慢、尊敬诚恳，最后沉稳落下，不是道别。",
        "正式诚恳，句尾沉稳落下。",
        3.6,
    ),
    "drink_01": VoiceSpec(
        "drink_01",
        "來！咱做伙飲一杯！",
        "正在江湖酒馆举杯邀请好友。来从胸腔厚实召唤，停顿约零点四秒；咱做伙饮一杯爽朗有温度、语速稍慢，带笑意并有力收尾。",
        "前句豪爽召唤，后句温暖邀请。",
        3.3,
    ),
    "drink_02": VoiceSpec(
        "drink_02",
        "好酒！閣來一杯！",
        "刚喝完好酒。好酒低沉满足，带惊喜赞叹；停顿约零点四秒，阁来一杯豪爽兴奋但不升成高音，只有轻微笑意，不笑出声、不醉酒含糊。",
        "前句满足赞叹，后句豪爽有力。",
        3.3,
    ),
    "landscape_01": VoiceSpec(
        "landscape_01",
        "今暝月娘真媠，予人看甲袂放。",
        "喝酒后独自抬头赏月。今暝月娘缓慢低沉，真媠带欣赏感并稍微拉长；自然停顿后，予人看甲袂放放松而略带微醺感。成熟、沉静，不是旁白。",
        "",
        5.0,
        speed=1.20,
    ),
    "landscape_02": VoiceSpec(
        "landscape_02",
        "月娘懸懸掛天頂，今暝真正是好景緻。",
        "历经江湖的侠客持酒赏月。月娘稍微拉长，月娘悬悬挂天顶低沉缓慢；停顿约零点四秒，今暝真正是好景致带欣赏满足，好景致放慢收尾。",
        "",
        5.0,
        speed=1.20,
    ),
    "alert_01": VoiceSpec(
        "alert_01",
        "嗯？你想欲創啥物？",
        "突然察觉异状。嗯短促警觉；你想欲压低，创啥物带冷静的质问、戒备和轻微威吓，不能大吼，尾句干脆收住。",
        "突然警觉，随后压低声音冷静质问。",
        4.0,
        speed=1.00,
    ),
    "alert_02": VoiceSpec(
        "alert_02",
        "嗯！你是啥人？想欲按怎？",
        "陌生人突然出现。嗯短促警觉；你是啥人稍重质问，短暂停顿后，想欲按怎再压低并带威吓，冷静准备拔剑，结尾干脆。",
        "短促警觉，质问后停顿，再压低威吓。",
        4.0,
        speed=1.00,
    ),
}


def _variant_spec(spec: VoiceSpec, variant: VariantStyle) -> VoiceSpec:
    instruction = f"{BASE_ROLE}{variant.instruction}{spec.model_cue}"
    design_instruction = f"{QWEN_ROLE}{QWEN_VARIANTS[variant.key]}{spec.performance}"
    return replace(
        spec,
        performance=instruction,
        speed=spec.speed * variant.speed,
        design_instruction=design_instruction,
        variant_key=variant.key,
    )


def _ensure_import_paths() -> None:
    repo = RUNTIME_ROOT / "CosyVoice"
    matcha = repo / "third_party" / "Matcha-TTS"
    for path in (repo, matcha):
        if path.exists() and str(path) not in sys.path:
            sys.path.insert(0, str(path))


def _save_wav(chunks: Iterable, output: Path, target_seconds: float) -> float:
    import numpy as np
    import soundfile as sf
    import torch

    tensors = []
    sample_rate = 24000
    for chunk in chunks:
        speech = chunk.get("tts_speech")
        if speech is None:
            continue
        if not isinstance(speech, torch.Tensor):
            speech = torch.as_tensor(speech)
        tensors.append(speech.detach().float().cpu().reshape(-1))
        sample_rate = int(chunk.get("sample_rate", sample_rate))
    if not tensors:
        raise RuntimeError("模型沒有回傳任何語音資料。")

    audio = torch.cat(tensors).numpy()
    if not np.isfinite(audio).all():
        raise RuntimeError("模型輸出包含無效數值。")

    # Remove only long edge silence, preserve natural pauses, and keep the requested
    # 2.5–4 second range by adding clean silence rather than distorting speech.
    threshold = max(float(np.max(np.abs(audio))) * 0.006, 1e-5)
    active = np.flatnonzero(np.abs(audio) >= threshold)
    if active.size:
        margin = int(sample_rate * 0.08)
        audio = audio[max(0, active[0] - margin) : min(len(audio), active[-1] + margin + 1)]

    # Never time-stretch generated speech. Phase-vocoder compression is useful
    # for timing but can add metallic/electronic artefacts to short voice lines.

    # Keep each take in its requested performance window. Slow TTS
    # delivery and punctuation should provide most of the duration; padding is
    # only alignment room and is placed mostly before the line.
    wanted = int(max(3.0, min(6.0, target_seconds)) * sample_rate)
    if len(audio) < wanted:
        # Unity must respond immediately when an action fires. Keep only a tiny
        # lead-in and place alignment room after the spoken line.
        before = min(int(sample_rate * 0.06), wanted - len(audio))
        after = wanted - len(audio) - before
        audio = np.pad(audio, (before, after))

    peak = float(np.max(np.abs(audio))) if audio.size else 0.0
    if peak > 0:
        audio = audio * (0.92 / peak)
    fade = min(int(sample_rate * 0.012), len(audio) // 2)
    if fade:
        ramp = np.linspace(0.0, 1.0, fade, dtype=np.float32)
        audio[:fade] *= ramp
        audio[-fade:] *= ramp[::-1]

    # Unity imports 48 kHz WAV directly. scipy's polyphase resampler avoids a
    # dependency on ffmpeg.
    if sample_rate != 48000:
        from scipy.signal import resample_poly

        audio = resample_poly(audio, 48000, sample_rate).astype(np.float32)
        sample_rate = 48000

    output.parent.mkdir(parents=True, exist_ok=True)
    sf.write(output, audio, sample_rate, subtype="PCM_16")
    return len(audio) / sample_rate


class CosyVoice3Backend:
    name = "CosyVoice3"

    def __init__(self, prompt_wav: Path):
        _ensure_import_paths()
        try:
            from cosyvoice.cli.cosyvoice import AutoModel
        except Exception as exc:
            raise RuntimeError(f"無法載入 CosyVoice3：{exc}") from exc

        model_dir = Path(
            os.environ.get(
                "COSYVOICE_MODEL_DIR",
                RUNTIME_ROOT / "models" / "Fun-CosyVoice3-0.5B-2512",
            )
        )
        if not model_dir.exists():
            raise RuntimeError(f"找不到 CosyVoice3 模型：{model_dir}")
        if not prompt_wav.exists():
            raise RuntimeError(f"找不到男性音色參考 WAV：{prompt_wav}")

        self.prompt_wav = prompt_wav
        self.model = AutoModel(model_dir=str(model_dir), fp16=True)

    def _generate_segmented_performance(self, spec: VoiceSpec, output: Path) -> float:
        import torch

        variant_style = {
            "a": "请用闽南话自然低沉地说。",
            "b": "请用闽南话演出明显的舞台张力。",
            "c": "请用闽南话演出明显的舞台张力。",
        }[spec.variant_key]
        segments = {
            "sword_01": (
                ("看招！", "低沉警告，不要提高音调。", 0.32),
                ("食我一劍！", "从胸腔发力，力量快速上升，剑字有力收住。", 0.0),
            ),
            "sword_02": (
                ("劍出無情。", "缓慢冷酷，无情二字向下压低。", 0.50),
                ("接招！", "突然加快爆发，短促有力收住。", 0.0),
            ),
            "landscape_01": (
                ("今暝月娘真媠，", "低沉缓慢，月娘稍慢，真媠带欣赏感并稍微拉长。", 0.40),
                ("予人看甲袂放。", "放松悠然，略带微醺感，成熟沉静地收尾。", 0.0),
            ),
            "landscape_02": (
                ("月娘懸懸掛天頂，", "低沉缓慢，月娘稍微拉长，像抬头看向夜空。", 0.45),
                ("今暝真正是好景緻。", "带欣赏满足感，好景緻放慢并沉稳收尾。", 0.0),
            ),
            "alert_01": (
                ("嗯？", "突然察觉异状，短促警觉，不要拖长。", 0.18),
                ("你想欲創啥物？", "声音压低，冷静质问，戒备并轻微威吓，尾句干脆。", 0.0),
            ),
            "alert_02": (
                ("嗯！", "突然察觉陌生人，短促警觉。", 0.16),
                ("你是啥人？", "稍重地冷静质问，保持低沉。", 0.30),
                ("想欲按怎？", "声音再压低，带戒备威吓，尾句干脆收住。", 0.0),
            ),
        }[spec.key]

        pieces = []
        sample_rate = 24000
        for text, direction, pause_after in segments:
            instruction = (
                f"You are a helpful assistant. {variant_style}{direction}"
                "<|endofprompt|>"
            )
            chunks = self.model.inference_instruct2(
                text,
                instruction,
                str(self.prompt_wav),
                stream=False,
                speed=spec.speed,
                text_frontend=False,
            )
            tensors = []
            for chunk in chunks:
                speech = chunk.get("tts_speech")
                if speech is None:
                    continue
                tensors.append(torch.as_tensor(speech).detach().float().cpu().reshape(-1))
                sample_rate = int(chunk.get("sample_rate", sample_rate))
            if not tensors:
                raise RuntimeError(f"分段語音沒有輸出：{text}")
            segment = torch.cat(tensors)
            threshold = max(float(segment.abs().max()) * 0.006, 1e-5)
            active = torch.nonzero(segment.abs() >= threshold).reshape(-1)
            if active.numel():
                margin = int(sample_rate * 0.04)
                start = max(0, int(active[0]) - margin)
                end = min(segment.numel(), int(active[-1]) + margin + 1)
                segment = segment[start:end]
            pieces.append(segment)
            if pause_after:
                pieces.append(torch.zeros(int(sample_rate * pause_after)))

        combined = torch.cat(pieces)
        return _save_wav(
            ({"tts_speech": combined, "sample_rate": sample_rate},),
            output,
            spec.target_seconds,
        )

    def generate(self, spec: VoiceSpec, output: Path) -> float:
        if spec.key in {"sword_01", "sword_02"}:
            return self._generate_segmented_performance(spec, output)
        instruction = spec.performance.rstrip() + "<|endofprompt|>"
        chunks = self.model.inference_instruct2(
            spec.text,
            instruction,
            str(self.prompt_wav),
            stream=False,
            speed=spec.speed,
            # WeText normalizes Chinese text toward Mandarin and downloads an
            # additional model. Keeping the original characters is preferable
            # for CosyVoice3's native Minnan instruction following.
            text_frontend=False,
        )
        return _save_wav(chunks, output, spec.target_seconds)


class Qwen3TTSBackend:
    name = "Qwen3-TTS"

    def __init__(self, _prompt_wav: Path):
        try:
            import torch
            from qwen_tts import Qwen3TTSModel
        except Exception as exc:
            raise RuntimeError(f"無法載入 Qwen3-TTS：{exc}") from exc

        model_dir = Path(
            os.environ.get(
                "QWEN_TTS_MODEL_DIR",
                RUNTIME_ROOT / "models" / "Qwen3-TTS-12Hz-0.6B-CustomVoice",
            )
        )
        if not model_dir.exists():
            raise RuntimeError(f"找不到 Qwen3-TTS 模型：{model_dir}")
        self.model = Qwen3TTSModel.from_pretrained(
            str(model_dir),
            device_map="cuda:0" if torch.cuda.is_available() else "cpu",
            dtype=torch.bfloat16 if torch.cuda.is_available() else torch.float32,
            attn_implementation="sdpa",
        )

    def generate(self, spec: VoiceSpec, output: Path) -> float:
        wavs, sample_rate = self.model.generate_custom_voice(
            text=spec.text,
            language="Auto",
            speaker="Uncle_Fu",
            instruct=spec.design_instruction,
            do_sample=True,
            temperature=0.85,
            top_p=0.92,
            max_new_tokens=1536,
        )
        chunks = ({"tts_speech": wav, "sample_rate": sample_rate} for wav in wavs)
        return _save_wav(chunks, output, spec.target_seconds)


def _make_backend(name: str, prompt_wav: Path):
    errors = []
    choices = [name] if name != "auto" else ["cosyvoice3", "qwen3-tts"]
    for choice in choices:
        try:
            if choice == "cosyvoice3":
                return CosyVoice3Backend(prompt_wav)
            return Qwen3TTSBackend(prompt_wav)
        except Exception as exc:
            errors.append(f"{choice}: {exc}")
            print(f"[警告] {choice} 無法使用：{exc}", file=sys.stderr)
    raise RuntimeError("沒有可用的 TTS 後端。\n" + "\n".join(errors))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="產生 Unity 戲偶專案的台灣閩南語角色語音")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--all", action="store_true", help="產生全部六句")
    group.add_argument(
        "--voice",
        nargs="+",
        choices=sorted(VOICES),
        help="只產生一個或多個指定語音",
    )
    variant_group = parser.add_mutually_exclusive_group()
    variant_group.add_argument(
        "--candidates",
        action="store_true",
        help="每句產生 A/B/C 三個候選，並以 C 作為暫定主檔",
    )
    variant_group.add_argument(
        "--variant",
        choices=sorted(VARIANTS),
        help="只產生指定的 A、B 或 C 候選",
    )
    parser.add_argument("--overwrite", action="store_true", help="覆蓋既有 WAV")
    parser.add_argument(
        "--backend",
        choices=("auto", "cosyvoice3", "qwen3-tts"),
        default="auto",
        help="預設優先 CosyVoice3，失敗才嘗試 Qwen3-TTS",
    )
    parser.add_argument("--prompt-wav", type=Path, default=DEFAULT_PROMPT)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    selected = list(VOICES.values()) if args.all else [VOICES[key] for key in args.voice]
    if args.candidates:
        variants = list(VARIANTS.values())
    elif args.variant:
        variants = [VARIANTS[args.variant]]
    else:
        variants = [VARIANTS["c"]]

    pending = []
    for spec in selected:
        for variant in variants:
            if args.candidates or args.variant:
                output = CANDIDATE_DIR / f"{spec.key}_{variant.key}.wav"
            else:
                output = OUTPUT_DIR / f"{spec.key}.wav"
            label = f"{spec.key}_{variant.key}"
            if output.exists() and not args.overwrite:
                print(f"[略過] {label}：檔案已存在（使用 --overwrite 才會覆蓋）")
            else:
                pending.append((spec, variant, output))
    if not pending:
        return 0

    try:
        backend = _make_backend(args.backend, args.prompt_wav.resolve())
        print(f"[模型] 使用 {backend.name}")
        for spec, variant, output in pending:
            rendered = _variant_spec(spec, variant)
            label = f"{spec.key}_{variant.key}"
            seed_base = sum(ord(ch) for ch in spec.key) * 1000
            default_seed = seed_base + variant.seed_offset
            if backend.name == "Qwen3-TTS":
                seed = SEED_OVERRIDES.get(label, default_seed)
            else:
                seed = seed_base + COSY_SEED_OFFSET_OVERRIDES.get(
                    label, variant.seed_offset
                )
            random.seed(seed)
            try:
                import numpy as np
                import torch

                np.random.seed(seed % (2**32 - 1))
                torch.manual_seed(seed)
                if torch.cuda.is_available():
                    torch.cuda.manual_seed_all(seed)
            except ImportError:
                pass
            print(f"[生成] {spec.key}_{variant.key}: {spec.text}")
            duration = backend.generate(rendered, output)
            print(f"[完成] {output} ({duration:.2f} 秒)")

        if args.candidates:
            for spec in selected:
                main_variant = MAIN_VARIANTS.get(spec.key, "c")
                source = CANDIDATE_DIR / f"{spec.key}_{main_variant}.wav"
                target = OUTPUT_DIR / f"{spec.key}.wav"
                if source.exists() and (args.overwrite or not target.exists()):
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(source, target)
                    print(f"[主檔] {target} <- {source.name}")
    except Exception as exc:
        print(f"[失敗] {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
