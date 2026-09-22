from pathlib import Path
import sys
import numpy as np
ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT/'tools'))
import soundfile as sf

music, sr = sf.read(ROOT/'rpg-the-graveyard.ogg', always_2d=True)
n = round(3.5*sr)
t = np.linspace(0,1,n)
music[:n] *= (t*t*(3-2*t))[:,None]
voice, voice_sr = sf.read(ROOT/'tavern-laugh-source.mp3',always_2d=True)
voice = voice.mean(axis=1)
voice -= voice.mean()
# Preserve the natural performance; only resample to the music rate.
voice = np.interp(np.arange(round(len(voice)*sr/voice_sr))*voice_sr/sr,
                  np.arange(len(voice)),voice)
freq = np.fft.rfftfreq(len(voice),1/sr)
voice = np.fft.irfft(np.fft.rfft(voice)/np.sqrt(1+(freq/4500)**8),n=len(voice))
a,b=round(.015*sr),round(.075*sr)
voice[:a] *= np.linspace(0,1,a)
voice[-b:] *= np.linspace(1,0,b)
voice *= .20/max(np.max(np.abs(voice)),1e-9)
# Close, dry voice with a small room reflection, rather than a long echo.
wet = np.pad(voice,(0,round(.15*sr)))
for delay,gain in [(.045,.055),(.081,.025)]:
    offset=round(delay*sr)
    wet[offset:offset+len(voice)] += voice*gain
sf.write(ROOT/'tavern-laugh-intro.wav',wet,sr,subtype='PCM_16')
mix=music*.65
offset=round(.25*sr)
mix[offset:offset+len(wet)] += wet[:,None]
assert np.isfinite(mix).all() and np.max(np.abs(mix)) < .98
sf.write(ROOT/'menu-music-tavern-intro.wav',mix,sr,subtype='PCM_16')
preview=mix[:20*sr].copy()
preview[-sr:] *= np.linspace(1,0,sr)[:,None]
sf.write(ROOT/'menu-tavern-intro-preview.wav',preview,sr,subtype='PCM_16')
for name in ['tavern-laugh-intro.wav','menu-tavern-intro-preview.wav','menu-music-tavern-intro.wav']:
    check,check_sr=sf.read(ROOT/name,always_2d=True)
    assert np.isfinite(check).all() and np.max(np.abs(check)) < .98
    print(f'{name}: {len(check)/check_sr:.3f} seconds, peak {20*np.log10(np.max(np.abs(check))):.2f} dBFS')
