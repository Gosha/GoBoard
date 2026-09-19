"""Create fresh audition candidates and source-context WAVs. Never edits app assets."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--originals', type=Path, default=Path.home()/'Downloads')
parser.add_argument('--decoder-path', type=Path, default=REPO/'.runtime/audio-tools')
args = parser.parse_args()
sys.path.insert(0, str(args.decoder_path))
import numpy as np
import soundfile as sf

sources = json.loads((REPO/'experiments/GoBoard.SoundSamples/sources.json').read_text())
selections_path = ROOT/'selections.json'
selections = json.loads(selections_path.read_text())

def activity(samples):
    peak = float(np.max(np.abs(samples)))
    rms = float(np.sqrt(np.mean(samples**2)))
    return {'peakDbfs':20*np.log10(max(peak,1e-12)),
            'rmsDbfs':20*np.log10(max(rms,1e-12)),
            'crestDb':20*np.log10(max(peak,1e-12)/max(rms,1e-12))}

def has_attack(samples):
    level = activity(samples)
    # Screening thresholds for these recordings, not a perceptual quality score.
    return level['peakDbfs'] >= -30 and level['crestDb'] >= 14

families = []
for s in sources:
    original = args.originals/s['original_file']
    if hashlib.sha256(original.read_bytes()).hexdigest() != s['original_sha256']:
        raise ValueError(f'Unexpected source: {original.name}')
    stereo, sr = sf.read(original, always_2d=True)
    x = stereo.mean(axis=1)
    x -= x.mean()
    slug = s['samples'][0]['file'].split('/')[0]
    prefix = {'cherry-mx-blue':'B','cherry-mx-clear':'C','gateron-yellow':'G'}[slug]
    h = round(sr*.003)
    env = np.sqrt(np.mean(x[:len(x)//h*h].reshape(-1,h)**2,axis=1))
    threshold = max(np.quantile(env,.15)*4, np.quantile(env,.95)*.045)
    active = np.flatnonzero(env>threshold)
    groups = np.split(active,np.flatnonzero(np.diff(active)>round(.085*sr/h))+1)
    proposals = []
    for g in groups:
        a,b = g[0]*h,(g[-1]+1)*h
        if not .04 <= (b-a)/sr <= .45 or a < sr*.2 or b > len(x)-sr*.25:
            continue
        if any(abs(a/sr-c['start_seconds'])<.45 for c in s['samples']):
            continue
        if not has_attack(x[a:b]):
            continue
        body = np.sqrt(np.mean(x[a:b]**2))
        before = np.sqrt(np.mean(x[a-round(.06*sr):a]**2))
        after = np.sqrt(np.mean(x[b:b+round(.08*sr)]**2))
        # Prefer quiet surroundings and some decay; this is not an ear-quality score.
        score = body/(before+after+1e-8) * min(1,(b-a)/sr/.13)
        proposals.append((float(score),a,b))
    # Dense typing may never fall below the grouping threshold. Also consider
    # windows around local envelope peaks instead of filling gaps with noise.
    radius = round(.10*sr/h)
    for i in range(radius,len(env)-radius):
        if env[i] < .01 or env[i] != np.max(env[i-radius:i+radius+1]):
            continue
        a,b = i*h-round(.012*sr), i*h+round(.160*sr)
        if a < sr*.2 or b > len(x)-sr*.25:
            continue
        if any(abs(a/sr-c['start_seconds'])<.45 for c in s['samples']) or not has_attack(x[a:b]):
            continue
        body = np.sqrt(np.mean(x[a:b]**2))
        before = np.sqrt(np.mean(x[a-round(.06*sr):a]**2))
        after = np.sqrt(np.mean(x[b:b+round(.08*sr)]**2))
        proposals.append((float(body/(before+after+1e-8)),a,b))
    # Keep published IDs and source times stable so favorites never change meaning.
    saved = selections[slug]
    chosen = [(c['id'],round(c['startSeconds']*sr),round(c['endSeconds']*sr)) for c in saved['active']]
    for ident,a,b in chosen:
        if not has_attack(x[a:b]):
            raise ValueError(f'{ident}: saved candidate failed activity screening')
    next_id = max(int(c['id'][1:]) for c in saved['active']+saved['retired'])+1
    for candidate in sorted(proposals,reverse=True):
        if len(chosen)==8:
            break
        if all(abs(candidate[1]-p[1])>.6*sr for p in chosen):
            _,a,b = candidate
            ident = f'{prefix}{next_id:02}'
            next_id += 1
            chosen.append((ident,a,b))
            saved['active'].append({'id':ident,'startSeconds':a/sr,'endSeconds':b/sr})
    if len(chosen)<8:
        raise ValueError(f'Only {len(chosen)} fresh candidates for {slug}')
    chosen.sort(key=lambda p:p[1])
    samples=[]
    audio_dir=ROOT/'audio'/slug
    audio_dir.mkdir(parents=True,exist_ok=True)
    # Retain rejected examples as regression fixtures, never audition candidates.
    for rejected in saved['retired']:
        a,b = round(rejected['startSeconds']*sr),round(rejected['endSeconds']*sr)
        if has_attack(x[a:b]):
            raise ValueError(f"{rejected['id']}: rejection regression changed")
        sf.write(audio_dir/f"{rejected['id']}.wav",x[a-round(.080*sr):b+round(.160*sr)],sr,subtype='PCM_16')
    definitions=[(ident,a,b,False) for ident,a,b in chosen]
    definitions += [(f'{prefix}O{i+1}',round(c['start_seconds']*sr),round(c['end_seconds']*sr),True)
                    for i,c in enumerate(s['samples'])]
    for ident,a,b,old in definitions:
        start=max(0,a-round(.080*sr)); end=min(len(x),b+round(.160*sr))
        context=x[start:end].copy()
        # Preserve source character and ample context; runtime audition controls apply fades.
        if np.max(np.abs(context))>.98:
            context*=.98/np.max(np.abs(context))
        target=audio_dir/f'{ident}.wav'
        sf.write(target,context,sr,subtype='PCM_16')
        entry={'id':ident,'existing':old,'url':f'audio/{slug}/{ident}.wav',
               'durationMs':len(context)/sr*1000,'startMs':(a-start)/sr*1000,
               'endMs':(b-start)/sr*1000,'sourceStartSeconds':start/sr,
               'sourceCoreSeconds':a/sr,'sourceEndSeconds':end/sr,
               'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),
               'activity':activity(context[a-start:b-start])}
        if old:
            current=REPO/'assets/audio/mechanical'/s['samples'][len(samples)-8]['file']
            dest=audio_dir/f'{ident}-current.wav'
            dest.write_bytes(current.read_bytes())
            entry['currentUrl']=f'audio/{slug}/{ident}-current.wav'
        samples.append(entry)
    families.append({'id':slug,'name':s['title'],'prefix':prefix,'author':s['author'],
                     'source':s['url'],'sourceHash':s['original_sha256'],'samples':samples,
                     'retired':saved['retired']})
    print(slug, '8 new + 4 current:',[(e['id'],round(e['sourceCoreSeconds'],2),round(e['endMs']-e['startMs'])) for e in samples])
manifest={'version':2,'license':'CC0 1.0','licenseUrl':'https://creativecommons.org/publicdomain/zero/1.0/',
          'families':families}
(selections_path).write_text(json.dumps(selections,indent=2)+'\n',encoding='utf-8')
(ROOT/'samples.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
