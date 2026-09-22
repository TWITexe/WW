from pathlib import Path
import sys
import json
import numpy as np

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / 'tools'))
import soundfile as sf

def read(name):
    x, sr = sf.read(ROOT / (name + '-source.mp3'), always_2d=True)
    return x - x.mean(axis=0), sr

def soften(x, sr, cutoff):
    # Smooth low-pass magnitude, without a sharp spectral edge.
    freqs = np.fft.rfftfreq(len(x), 1 / sr)
    gain = 1 / np.sqrt(1 + (freqs / cutoff) ** 8)
    return np.fft.irfft(np.fft.rfft(x, axis=0) * gain[:, None], n=len(x), axis=0)

stats = {}
def save(name, x, sr):
    assert np.isfinite(x).all() and np.max(np.abs(x)) < 1
    sf.write(ROOT / (name + '.wav'), x, sr, subtype='PCM_16')
    stats[name] = {'seconds': round(len(x)/sr, 3), 'peak_dbfs': round(20*np.log10(max(np.max(np.abs(x)), 1e-9)), 2)}

def fades(x, sr, attack, release):
    a, b = min(int(sr*attack),len(x)//2), min(int(sr*release),len(x)//2)
    x[:a] *= np.linspace(0,1,a)[:,None]
    x[-b:] *= np.linspace(1,0,b)[:,None]
    return x

click, sr = read('click')
click = soften(np.pad(click, ((sr//20,sr//20),(0,0))), sr, 1600)
click = fades(click, sr, .004, .025) * .6
save('click-muffled', click, sr)
whoosh, sr = read('whoosh')
whoosh = soften(np.pad(whoosh, ((sr//20,sr//20),(0,0))), sr, 2200)
whoosh = fades(whoosh, sr, .2, .4) * .16
save('whoosh-subtle', whoosh, sr)
portal, sr = read('portal')
overlap = int(sr*2.5)
t = np.linspace(0, 1, overlap)[:,None]
t = t*t*(3-2*t)
loop = np.concatenate((portal[overlap:-overlap], portal[-overlap:]*(1-t)+portal[:overlap]*t))
loop = soften(loop, sr, 3200) * .45
save('portal-loop', loop, sr)
save('portal-loop-preview-4-cycles', np.tile(loop,(4,1)), sr)
stats['portal_boundary'] = {'max_step': float(np.max(np.abs(loop[0]-loop[-1]))), 'max_internal_step': float(np.max(np.abs(np.diff(loop,axis=0))))}
(ROOT/'verification.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
print(json.dumps(stats,indent=2))
