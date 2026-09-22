from pathlib import Path
import sys
import numpy as np
ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT/'tools'))
import soundfile as sf

x, sr = sf.read(ROOT/'whoosh-v2-source.mp3', always_2d=True)
x -= x.mean(axis=0)
freq = np.fft.rfftfreq(len(x),1/sr)
x = np.fft.irfft(np.fft.rfft(x,axis=0)/(1+(freq[:,None]/2600)**6), n=len(x), axis=0)
# Gently flatten the body instead of accenting the middle of the transition.
hop = int(sr*.08)
positions = np.arange(0,len(x),hop)
rms = np.array([np.sqrt(np.mean(x[p:min(p+hop,len(x))]**2)) for p in positions])
target = max(float(np.median(rms)),1e-6)
gains = np.clip(target/np.maximum(rms,1e-6),.35,2)
gain = np.interp(np.arange(len(x)),positions,gains)
x *= gain[:,None]
a,b = int(sr*.18), int(sr*.28)
x[:a] *= np.linspace(0,1,a)[:,None]
x[-b:] *= np.linspace(1,0,b)[:,None]
x *= .045/max(np.max(np.abs(x)),1e-9)
sf.write(ROOT/'whoosh-v2-even.wav',x,sr,subtype='PCM_16')
assert np.isfinite(x).all() and np.max(np.abs(x)) < 1
for name in ['whoosh-v2-even.wav','a-darkness-opus.ogg']:
    info = sf.info(ROOT/name)
    print(f'{name}: {info.duration:.3f} seconds, {info.samplerate} Hz, {info.channels} channels')
