# Taiwanese Character Voice Generator

This local tool generates six dry Taiwanese Minnan voice files for the Unity puppet project. All project and runtime locations are resolved relative to the script, so moving the project does not leave hard-coded paths behind.

## Commands

```powershell
.\.voice-runtime\python310\python.exe .\generate_taiwanese_voice.py --all
.\.voice-runtime\python310\python.exe .\generate_taiwanese_voice.py --voice sword_01
.\.voice-runtime\python310\python.exe .\generate_taiwanese_voice.py --all --overwrite
.\.voice-runtime\python310\python.exe .\generate_taiwanese_voice.py --all --candidates --overwrite
```

The default backend order is CosyVoice3, then Qwen3-TTS only if the first backend cannot load. The output directory is `audio/voice` and existing WAV files are preserved unless `--overwrite` is supplied.

For final character-quality rendering, use the isolated Qwen3-TTS runtime with the official `Uncle_Fu` CustomVoice speaker. This seasoned, low and mellow male speaker does not inherit electronic artefacts from the Windows SAPI reference:

```powershell
.\voice-tools\setup_qwen3_tts.ps1
.\.voice-runtime\qwen-env\Scripts\python.exe .\generate_taiwanese_voice.py --all --candidates --backend qwen3-tts --overwrite
```

The model runtime and weights live under `.voice-runtime` and are excluded from source control.

Candidate mode writes three performances per line under `audio/voice/candidates`: A prioritizes the deepest and thickest chest-resonant delivery, B prioritizes emotional contrast and glove-puppetry stage presence, and C prioritizes a natural cinematic delivery. Candidate C is copied to the six top-level WAV files as the provisional Unity version.

The Windows SAPI prompt files are retained only for the offline CosyVoice3 fallback. They are not used by Qwen3-TTS CustomVoice. Generated WAV files are never pitch-shifted or time-stretched.

The final Minnan workflow uses Qwen only to create a clean fictional male timbre reference, then uses CosyVoice3's native Minnan instruction for pronunciation. This avoids both the Windows SAPI electronic timbre and Qwen CustomVoice's Mandarin pronunciation:

```powershell
.\.voice-runtime\qwen-env\Scripts\python.exe .\voice-tools\generate_clean_reference.py
.\.voice-runtime\python310\python.exe .\generate_taiwanese_voice.py --all --candidates --backend cosyvoice3 --overwrite
```

The CosyVoice3 path intentionally bypasses WeText normalization. WeText targets standard Chinese normalization and can turn Minnan wording into Mandarin-like input; the generator instead preserves the original characters and supplies an explicit Taiwanese Minnan instruction to CosyVoice3.

## Mandarin voice set

The separate Mandarin set uses Qwen3-TTS with the same mature `Uncle_Fu`
character speaker and writes ten dry WAV files without replacing the Taiwanese
Minnan files:

```powershell
.\.voice-runtime\qwen-env\Scripts\python.exe .\generate_mandarin_voice.py --all
.\.voice-runtime\qwen-env\Scripts\python.exe .\generate_mandarin_voice.py --voice sword_01
.\.voice-runtime\qwen-env\Scripts\python.exe .\generate_mandarin_voice.py --all --overwrite
```

Outputs are written to `audio/voice_mandarin`.
