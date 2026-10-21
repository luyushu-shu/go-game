# Song-aesthetic Go UI BGM

Generator for the main-menu / board ambient loop:

`Assets/Audio/BGM/go_ui_song_guqin_loop.wav`

## Spec

| Item | Value |
|------|--------|
| Duration | 72 s seamless loop |
| Sample rate | 48 kHz stereo PCM WAV |
| Tempo | ~58 BPM, 4/4 (felt, not pulsed) |
| Scale | C 宫 pentatonic |
| Lead | Dry guqin-like plucked tones |
| Color | One jade-qing strike at loop head; faint xun every 12 s; thin forest air |
| Forbidden | Voice, drums, dizi, bright guzheng runs, pipa, string stacks, epic / electronic beds |

## Regenerate

```bash
pip install numpy scipy
python3 Tools/audio/synthesize_song_guqin_bgm.py
```

In Unity: assign the clip to an `AudioSource`, enable **Loop**, keep volume modest (bed level).
