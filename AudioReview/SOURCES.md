# Menu audio audition drafts

## Review status

- Latest request: replace sinister laughter with a warm, friendly tavern-host mood inspired by Hearthstone. The existing music and 3.5-second initial fade remain accepted. New laugh is an audition candidate, not yet approved.
- tavern-laugh-source.mp3: HQ preview of LaughM.ogg by egomassive, https://freesound.org/people/egomassive/sounds/536811/ , CC0. Author describes a man's hearty laugh and credits original recording "Male Laughter 01" by Arbernaut (CC0). No Hearthstone recording is used.
- prepare_tavern_intro.py produces tavern-laugh-intro.wav, menu-music-tavern-intro.wav (full mixed audition), and menu-tavern-intro-preview.wav (20 seconds). Natural pitch preserved; mild high-frequency softening, reduced level, edge fades, and very quiet short room reflections. Laugh starts at 0.25 seconds and occurs only once. Runtime should use separate voice and the original music loop; the mixed audition must not be looped. Numerical file decoding/clipping checks passed; perceptual character still requires listening review.

- RPG - The Graveyard accepted in latest review. User requests 3–4 second fade-in plus elderly man/wizard laughter at the beginning.
- New voice candidate: Crazy old man laugh.wav by Cavernstones, CC0, https://freesound.org/people/Cavernstones/sounds/148915/ . Downloaded HQ MP3 preview; processed with quiet gain, softened highs, edge fades and subtle short echoes. Original performance character not verified by listening.
- prepare_intro.py creates a 3.5-second smoothstep music fade, separate mage-laugh-intro.wav, full mixed audition menu-music-with-mage-intro.wav, and a 20-second menu-intro-preview.wav. The preview ends with a one-second fade only for audition comfort.
- Runtime integration should apply music fade only on initial playback and trigger the separate laugh once upon menu entry. Do not loop the mixed audition or the faded intro file: this would repeat the laugh/fade. Keep the original music as the loop source. Scene implementation remains pending.

- Latest music review: RPG Ambience - Exploration rejected due to a harsh string-like instrument. Avoid sharp plucked/string attacks; previous no-guitar, restrained dark-fantasy and 2+ minute requirements still apply.
- New candidate: rpg-the-graveyard.ogg, RPG - The Graveyard by HitCtrl, https://opengameart.org/content/rpg-the-graveyard . Unmodified original OGG. CC BY 3.0, https://creativecommons.org/licenses/by/3.0/ . Attribution: HitCtrl, https://soundcloud.com/hitctrl . Duration verified locally: 161.750 seconds. Author describes strings, cellos and a little choir, and states loopable. No auditory confirmation of instrument attacks or boundary quality yet.

- Latest review: whoosh-v2-even.wav accepted for camera movement. All four SFX choices are now accepted (click, camera, portal, fireplace).
- A Darkness Opus rejected: guitar evokes a western. Music must avoid guitar/western character, remain restrained mystical dark fantasy, and last at least two minutes.
- New music candidate: rpg-ambience-exploration.ogg, RPG Ambience - Exploration by HitCtrl. https://opengameart.org/content/rpg-ambience-exploration . CC BY 3.0, https://creativecommons.org/licenses/by/3.0/ . Credit: HitCtrl, https://soundcloud.com/hitctrl . Unmodified download. Decoded duration verified: 126 seconds at 44100 Hz stereo. Author describes it as loopable castle exploration background music; tags include dark, orchestral, violin, cello, plucks. Perceptual suitability and loop boundary still unverified.

- Accepted: click-muffled.wav.
- Accepted: portal-loop.wav; requested integration as a 3D source, louder near the portal. Scene integration pending.
- Rejected: whoosh-subtle.wav (prominent middle, wrong sound character).
- New candidate: whoosh-v2-even.wav, based on Smooth whoosh by mokasza, https://freesound.org/people/mokasza/sounds/810329/ . Page lists CC BY 4.0; author discloses ElevenLabs generation and requests no unmodified standalone redistribution. Modified preview: softened high frequencies, levelled envelope, reduced volume and fades. Original source stored for provenance, not for shipping as a sound library.
- New music candidate: a-darkness-opus.ogg, A Darkness Opus by Alexandr Zhelanov, https://opengameart.org/content/a-darkness-opus . OGA-BY 3.0; attribution requested: Alexandr Zhelanov https://soundcloud.com/alexandr-zhelanov . Download is unmodified. No verified seamless loop yet. License: https://opengameart.org/content/oga-by-30-faq

Processed from Freesound HQ MP3 previews, not original WAV downloads. Not imported into Unity. Use original files for final production processing.

- click-muffled.wav: Flashlight Button Click.wav by steprock, CC0. https://freesound.org/people/steprock/sounds/509676/
  Changes: low-pass filtering, reduced level, edge fades.
- whoosh-subtle.wav: long wispy woosh2.wav by newagesoup, CC BY 4.0. https://freesound.org/people/newagesoup/sounds/377829/
  Changes: low-pass filtering, reduced level, softened envelope.
- portal-loop.wav and portal-loop-preview-4-cycles.wav: Portal_Continuous_Rumble.wav by zimbot, CC BY 4.0. https://freesound.org/people/zimbot/sounds/122972/
  Changes: 2.5-second wrap crossfade, low-pass filtering, reduced level. Preview repeats the identical loop four times, without gaps.

License: https://creativecommons.org/licenses/by/4.0/
CC0: https://creativecommons.org/publicdomain/zero/1.0/

Numerical verification covers duration, clipping and boundary sample continuity. Perceptual seamlessness and creative suitability still need listening review. Music selection remains pending: at least two minutes, restrained dark fantasy mood. Approved fireplace selection remains unchanged.

