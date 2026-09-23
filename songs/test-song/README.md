# Test song package

The current test audio is `大国奏音 - YOUNITHM.ogg`.

Optional cover art can be added later as `jacket.png`; update `jacketFile` in
`song.json` when it exists.

## Files to edit

- `song.json`: song title, artist, audio file, jacket, and preview range.
- `timing.json`: source-audio time for tick zero, BPM changes, and time signatures.
- `charts/test.atendchart`: the formal chart loaded by the game.

`audioTimeAtTickZeroSeconds` is a virtual trim point. The source audio remains
unchanged, and that source-audio time becomes chart tick zero.

The original hand-written draft remains at `charts/test.atendchart.json` for
reference. The loader intentionally ignores it because only files ending exactly
in `.atendchart` are playable charts.

## Current chart metadata

- Chart ID: `test-song-test`
- Difficulty: `Test 3`
- Charter: `TynnyVessels`
- Objects: 18 clicks and 2 holds
