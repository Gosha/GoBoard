"""Offline conversion of pinned kbsim press/release assets for the lab only."""
import hashlib
import json
import math
from pathlib import Path
import sys
import wave

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1] / '.runtime/audio-tools'))
import numpy as np
import soundfile as sf

REVISION = 'ba103f3b0afa9dab80447aa2e7e2ed80b6bd80e4'
UPSTREAM = ROOT / 'upstream/kbsim'
SR = 44100

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def level(data):
    return dict(peakDbfs=round(float(20*np.log10(max(np.max(np.abs(data)), 1e-12))), 2),
                rmsDbfs=round(float(20*np.log10(max(np.sqrt(np.mean(data**2)), 1e-12))), 2))

def read(pack, edge, name):
    path = UPSTREAM / pack / edge / (name + '.mp3')
    data, rate = sf.read(path, dtype='float64')
    assert rate == SR and data.ndim == 1, 'Unexpected upstream format'
    assert np.isfinite(data).all() and np.max(np.abs(data)) < 1
    assert level(data)['peakDbfs'] > -40, f'Missing/very quiet event: {path}'
    return data, dict(path=path.relative_to(ROOT).as_posix(), sha256=sha(path),
                      url=f'https://github.com/tplai/kbsim/blob/{REVISION}/src/assets/audio/{pack}/{edge}/{name}.mp3',
                      frames=len(data), sampleRate=rate, **level(data))

def main():
    # Freeze hashes on first import; future conversions reject changed originals.
    hashes = {p.relative_to(UPSTREAM).as_posix(): sha(p) for p in sorted(UPSTREAM.rglob('*')) if p.is_file()}
    lock = ROOT / 'kbsim-source-hashes.json'
    if lock.exists():
        assert json.loads(lock.read_text()) == hashes, 'Upstream assets changed'
    else:
        lock.write_text(json.dumps(hashes, indent=2)+'\n')
    families = []
    for pack, name, prefix in [('blackink', 'Gateron Black Ink', 'BI'), ('holypanda', 'Holy Panda', 'HP'), ('topre', 'Topre', 'TP')]:
        family = dict(id='kbsim-'+pack, name=name, author='Thomas Lai / kbsim',
                      source=f'https://github.com/tplai/kbsim/tree/{REVISION}/src/assets/audio/{pack}',
                      sourceHash=REVISION, license='MIT', licenseUrl='upstream/kbsim/LICENSE.md',
                      premade=True, samples=[])
        folder = ROOT / 'audio' / family['id']
        folder.mkdir(parents=True, exist_ok=True)
        for index, key in enumerate([f'GENERIC_R{i}' for i in range(5)] + ['SPACE', 'ENTER', 'BACKSPACE']):
            press, press_meta = read(pack, 'press', key)
            release, release_meta = read(pack, 'release', 'GENERIC' if key.startswith('GENERIC') else key)
            # Preserve the complete decoded files, padding to integer-ms UI boundaries.
            # The 40 ms display gap is synthetic; live release uses actual key-up timing.
            split = math.ceil(len(press)*1000/SR)
            release_start = split + 40
            end = release_start + math.ceil(len(release)*1000/SR)
            data = np.zeros(round(end*SR/1000))
            data[:len(press)] = press
            offset = round(release_start*SR/1000)
            data[offset:offset+len(release)] = release
            sample_id = f'{prefix}{index+1:02}'
            path = folder / (sample_id+'.wav')
            with wave.open(str(path), 'wb') as output:
                output.setparams((1, 2, SR, 0, 'NONE', 'not compressed'))
                output.writeframes(np.round(data*32767).astype('<i2').tobytes())
            family['samples'].append(dict(id=sample_id, label=key.replace('GENERIC_R', 'Ordinary '),
                existing=False, premade=True, url=path.relative_to(ROOT).as_posix(),
                durationMs=end, startMs=0, endMs=end, sha256=sha(path),
                sourceFiles=dict(press=press_meta, release=release_meta),
                assembly=dict(pressStartMs=0, pressEndMs=split, releaseStartMs=release_start, endMs=end, gapMs=40),
                defaultSettings=dict(start=0, end=end, split=split, releaseStart=release_start,
                                     paired=True, releaseGain=100, holdMs=200, fade=1, pitch=0, match=True, overlap=True)))
        families.append(family)
    result = dict(version=1, revision=REVISION, license='MIT', families=families)
    (ROOT/'premade-samples.json').write_text(json.dumps(result, indent=2)+'\n')
    print('Prepared 24 full-file pairs from 36 original mono 44.1 kHz MP3s; all edges pass finite/non-silent screening.')

if __name__ == '__main__':
    main()
