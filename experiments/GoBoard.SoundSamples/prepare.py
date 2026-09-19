"""Offline extraction only; does not change GoBoard runtime or settings.

Requires numpy and soundfile. Original downloads are verified against source hashes.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--originals', type=Path, default=Path.home() / 'Downloads')
parser.add_argument('--decoder-path', type=Path, help='Optional directory containing SoundFile')
args = parser.parse_args()
if args.decoder_path:
    sys.path.insert(0, str(args.decoder_path))
import numpy as np
import soundfile as sf

root = Path(__file__).resolve().parent
sources = json.loads((root / 'sources.json').read_text(encoding='utf-8'))
report = []
for source in sources:
    original = args.originals / source['original_file']
    actual_hash = hashlib.sha256(original.read_bytes()).hexdigest()
    if actual_hash != source['original_sha256']:
        raise ValueError(f"Source hash mismatch: {original.name}")
    audio, rate = sf.read(original, always_2d=True, dtype='float64')
    if rate != source['sample_rate']:
        raise ValueError(f"Unexpected sample rate: {original.name}")
    mono = audio.mean(axis=1)
    mono -= mono.mean()
    for cut in source['samples']:
        start = round(cut['start_seconds'] * rate)
        end = round(cut['end_seconds'] * rate)
        clip = mono[start:end].copy()
        attack = min(round(.002 * rate), len(clip)//2)
        release = min(round(.010 * rate), len(clip)//2)
        clip[:attack] *= np.linspace(0, 1, attack)
        clip[-release:] *= np.linspace(1, 0, release)
        # Match Cushioned wood's nominal integrated energy across sample rates.
        target_energy = .6197242718860959 * .78**2 * rate / 48000
        energy = float(np.dot(clip, clip))
        peak = float(np.max(np.abs(clip)))
        if energy <= 0 or peak <= 0:
            raise ValueError(f"Empty sample: {cut['file']}")
        gain = min(np.sqrt(target_energy / energy), .16 / peak)
        clip *= gain
        output = root / 'prepared' / cut['file']
        output.parent.mkdir(parents=True, exist_ok=True)
        sf.write(output, clip, rate, subtype='PCM_16')
        decoded, decoded_rate = sf.read(output)
        info = sf.info(output)
        assert decoded_rate == 44100 and info.channels == 1 and info.subtype == 'PCM_16'
        assert len(decoded) == end-start and .025 < len(decoded)/rate < .3
        assert np.isfinite(decoded).all() and 0 < np.max(np.abs(decoded)) <= .16004
        assert decoded[0] == 0 and decoded[-1] == 0
        report.append({'file':str(output.relative_to(root)).replace('\\','/'),
                       'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),
                       'source_sha256':actual_hash, 'source_start_frame':start,
                       'source_end_frame':end, 'frames':len(decoded), 'rate':rate,
                       'duration_ms':round(len(decoded)/rate*1000,3),
                       'gain':float(gain), 'peak':float(np.max(np.abs(decoded))),
                       'status':'prepared candidate; listening acceptance pending'})
assert len(report) == 12
(root / 'prepared-manifest.json').write_text(json.dumps(report, indent=2)+'\n',encoding='utf-8')
print(f'Prepared and verified {len(report)} samples; {sum(r["frames"]*2+44 for r in report)} WAV bytes.')
