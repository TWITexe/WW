from pathlib import Path
import sys
import numpy as np
ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT/'tools'))
import soundfile as sf

music, sr = sf.read(ROOT/'rpg-the-graveyard.ogg', always_2d=True)
fade_samples = round(3.5*sr)
phase = np.linspace(0,1,fade_samples)
music[:fade_samples] *= (phase*phase*(3-2*phase))[:,None]
sf.write(ROOT/'music-fade-in-3.5s.wav',music,sr,subtype='PCM_16')

laugh, voice_sr = sf.read(ROOT/'mage-laugh-source.mp3',always_2d=True)
laugh = laugh.mean(axis=1)
laugh -= laugh.mean()
# Trim only near-silent margins, retaining the entire recorded laugh.
active = np.flatnonzero(np.abs(laugh) > max(np.max(np.abs(laugh))*.025,1e-6))
if len(active):
    laugh = laugh[max(0,active[0]-int(.04*voice_sr)):min(len(laugh),active[-1]+int(.12*voice_sr))]
positions = np.arange(round(len(laugh)*sr/voice_sr)) * voice_sr/sr
laugh = np.interp(positions,np.arange(len(laugh)),laugh)
freqs = np.fft.rfftfreq(len(laugh),1/sr)
laugh = np.fft.irfft(np.fft.rfft(laugh)/np.sqrt(1+(freqs/3500)**8),n=len(laugh))
for seconds, reverse in [(.035,False),(.2,True)]:
    n=min(round(seconds*sr),len(laugh)//2)
    if reverse: laugh[-n:] *= np.linspace(1,0,n)
    else: laugh[:n] *= np.linspace(0,1,n)
laugh *= .23/max(np.max(np.abs(laugh)),1e-9)
wet = np.pad(laugh,(0,round(.5*sr)))
# Quiet short reflections keep the voice close while giving a room impression.
for delay,level in [(.09,.1),(.17,.06),(.29,.035)]:
    start=round(delay*sr)
    wet[start:start+len(laugh)] += laugh*level
sf.write(ROOT/'mage-laugh-intro.wav',wet,sr,subtype='PCM_16')
mix = music.copy()*.65
start=round(.2*sr)
mix[start:start+len(wet)] += wet[:,None]
assert np.isfinite(mix).all() and np.max(np.abs(mix)) < .98
sf.write(ROOT/'menu-music-with-mage-intro.wav',mix,sr,subtype='PCM_16')
preview=mix[:20*sr].copy()
preview[-sr:] *= np.linspace(1,0,sr)[:,None]
sf.write(ROOT/'menu-intro-preview.wav',preview,sr,subtype='PCM_16')
print(f'Fade: 3.5 s; full music: {len(music)/sr:.3f} s; voice with tail: {len(wet)/sr:.3f} s; mix peak: {20*np.log10(np.max(np.abs(mix))):.2f} dBFS; preview: 20 s')
