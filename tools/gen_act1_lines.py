# Finds the Act 1 story lines in the player's SLUS_014.11 and writes their offsets (no text is written to the mod).
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from iso import Disc

T = "18=A 2D=B 2B=C 20=D 25=E 31=F 29=G 23=H 1A=I 3B=J 33=K 2A=L 1E=M 2C=N 21=O 2F=P 3E=Q 26=R 1D=S 1C=T 35=U 39=V 22=W 46=X 24=Y 3F=Z 03=a 15=b 0F=c 0C=d 01=e 13=f 10=g 09=h 05=i 34=j 16=k 0A=l 0E=m 06=n 04=o 14=p 37=q 08=r 07=s 02=t 0D=u 19=v 12=w 36=x 11=y 32=z 38=0 3D=1 3A=2 41=3 4A=4 42=5 4E=6 45=7 57=8 59=9 30=- 0B=. 1F=, 17=! 1B=' 2E=?"
ENC = {e[3]: int(e[:2], 16) for e in T.split(' ')}
ENC[' '] = 0

# id -> start of the line (only the opening words are needed to find it)
LINES = {
    # town plaza at night
    'tea_lots': "Wow... there're lots of",
    'tea_mages': "Look, the mages are about to",
    'tea_awesome': "Awesome...",
    'tea_spooky': "...but kinda spooky, too.",
    'tea_heishin': "This must be the work",
    'tea_kid': "When I was a kid, it used",
    'tea_attitude': "Even the attitude of the mages",
    'tea_world': "What's our world",
    'tea_lookjono': "Look! It's Jono!",
    'jono_lost': "I...|I lost...",
    'seto_speck': "I believe I have|wasted my efforts",
    'jono_saywhat': "Say what!",
    'seto_forgive': "Forgive my choice of words.",
    'tea_heyjono': "Hey! Jono!",
    'tea_whatsup': "What's going on?",
    'jono_whaddaya': "Whaddaya think?",
    'tea_youlost': "You lost...",
    'jono_blunt': "Don't be so blunt!",
    'seto_weary': "I grow weary of this insect",
    'seto_worthy': "Is there not one among you that",
    'jono_nerve': "Man!|The nerve of this guy!",
    'tea_afterall': "After all, you're the best",
    'tea_bet': "I'll bet you can beat him!",
    'seto_victim': "Hello...|Are you my next victim?",
    'seto_hope': "I hope you are better than",
    'guard_calls': "Master Seto! Master Heishin|calls for you.",
    'seto_isee': "I see...",
    'seto_postponed': "So be it. The duel is postponed.",
    'seto_frequent': "Am I correct in assuming that you",
    'jono_right': "That's right.",
    'seto_waitthere': "Then wait there...",
    'seto_name': "Seto is my name.",
    'jono_believe': "Can you believe that guy!?",
    'tea_charmer': "Yeah... A real charmer.",
    # the duel ground
    'jono_wait': "Let's wait for the mage.",
    'jono_tough': "Y'know, the guy's real tough...",
    'jono_listen': "Listen. I play here",
    'jono_fact': "Fact is, there ain't too many",
    'jono_butmage': "But that mage...",
    'jono_chance': "I didn't have a chance!",
    'jono_sloppy': "I know you're good.",
    'jono_there': "There he is...",
    'jono_lookat': "Look at that...",
    'jono_groupies': "He's got his own groupies",
    'jono_whip': "Do me a favor and|whip the guy.",
    'seto_entertain': "I do not know how good you are...",
    'seto_enjoy': "I will have you know that",
    'seto_disappoint': "Do not disappoint me!",
    # after the duel
    'seto_cannot': "I...|I cannot believe I lost...",
    'seto_wait': "But... wait a moment.",
    'seto_hmm': "Hmmm... I see...",
    'seto_enough': "Enough.|That is all for today.",
    'seto_paths': "I have a feeling our paths",
    'seto_farewell': "'Til then... I bid you farewell.",
    'jono_didja': "Didja see the look on his face!?",
    # the invasion
    'simon_room': "Go on now! Off to your",
    'simon_ready': "I really hope you're",
    'simon_annoy': "Sometimes I wonder if the",
    'serv_trouble': "Lord Simon Muran!",
    'serv_mage': "Th-the High Mage...",
    'serv_power': "They wield a strange power...",
    'simon_what': "What!?",
    'serv_sorcery': "I think it's sorcery, my lord!",
    'simon_where': "Where's the High Mage now!?",
    'serv_hall': "In the Palace Hall, my lord!",
    'simon_dog': "Heishin... Heishin!",
    'simon_whatthe': "Wha... What the...",
    'hei_goodsee': "It's good to see you again,",
    'simon_revolt': "You dare to revolt...",
    'hei_mad': "Mad...?|Me?",
    'hei_throne': "I don't think so...",
    'simon_throne': "Your throne!?",
    'hei_power': "Yes, my throne. For I now",
    'simon_power': "The 'Power'...",
    'hei_recog': "I see recognition in your eyes.",
    'simon_dark': "The power of darkness...!?",
    'hei_treasure': "That's right. I've discovered",
    'hei_taste': "Here! Taste the forbidden",
    'isis_invaded': "The High Mage Heishin|has invaded the palace!",
    'isis_leave': "This place is too dangerous!",
    'isis_no': "No...",
    'seto_meet': "We meet again...",
    'seto_away': "All right men...",
    'seto_speak': "I wish to speak with the prince.",
    'guard_cmd': "By your command!",
    'seto_captive': "Heishin already holds the",
    'seto_provoke': "Running away would",
    'seto_want': "Is that what you want?",
    'seto_iwant': "Well... I'll tell you what I want.",
    'seto_location': "...the location of the",
    'seto_spare': "Tell me where it is and I'll",
    'seto_tellme': "You know where it is!|So... tell me!",
    'seto_talk': "Good... Now, talk!",
    'seto_fool': "Don't take me for a fool!",
    'simon_myprince': "My prince...!",
    'seto_simon': "Simon Muran!",
    'simon_item': "Dear prince...|Here is the Millennium Item...",
    'simon_takeit': "Take it... take it and run!!!",
    'simon_keep': "You must keep this treasure",
    'simon_key': "The treasure is the final key to",
    'simon_wicked': "Should it fall in the hands of",
    'seto_wellwell': "Well, well... You couldn't have",
    'seto_handkey': "Hand the key over to me right",
    'simon_cur': "Ignore this cur!",
    'simon_leave': "Leave him to me, my prince!",
    'seto_outway': "Out of my way, old man!",
    'simon_run': "Go, my prince! Run!",
    'simon_listen': "Listen to what I say...",
    'hei_donotmove': "Do not move! You are mine!",
    'hei_found': "I see you've found them, Seto.",
    'seto_yes': "Yes, my lord...",
    'hei_escape': "You didn't think you'd",
    'hei_resist': "There's no point in resisting.",
    'hei_handover': "...and hand it over.",
    'simon_option': "Only one option remains, my prince.",
    'simon_battle': "You must do battle with Heishin.",
    'hei_wasted': "A wasted effort, boy!",
    'simon_confront': "My dear prince!|You must confront him!",
    'simon_unleash': "The Millennium Item will",
    'simon_face': "Face him!",
    # after the unwinnable duel
    'hei_foolish': "Foolish child!",
    'hei_rip': "If you insist on keeping the",
    'hei_orelse': "Out of my way, old man!|Or else...",
    'simon_shatter': "My prince!|Shatter the Millennium Puzzle...",
    'simon_vile': "You must not hand it over",
    'hei_hands': "Out of my way, old man!|Get your hands off me!",
    'simon_puzzle': "Prince...|The puzzle...",
    'simon_dontgive': "...don't give them",
    'hei_no': "No!!!",
    'simon_awoken': "Thank goodness! You've awoken!",
    'simon_precise': "To be precise, your soul is now",
    'simon_sorry': "I'm sorry, my prince. But this",
    'simon_allow': "I couldn't allow you to simply",
    'simon_destroy': "The power of darkness that",
    'simon_oneday': "One day, when someone puts the",
    'simon_sleep': "Until that day comes...",
    'simon_someone': "I'm sure there is someone",
    'simon_guide': "In the end, that person and",
    'simon_rest': "Now rest, dear prince...",
}

def enc(s):
    return bytes(0xFE if c == '|' else ENC[c] for c in s)

disc = Disc(sys.argv[1])
files = {p.split(';')[0]: (l, sz) for p, l, sz, _ in disc.walk()}
d = disc.read(*files['/SLUS_014.11'])
lo, hi = 0x1A0000, 0x1AA000
out = []
AFTER = {'isis_no': 0x1A5170, 'seto_simon': 0x1A5460}
for k, s in LINES.items():
    i = d.find(enc(s), AFTER.get(k, lo), hi)
    if i < 0:
        raise SystemExit(f'missing: {k} {s!r}')
    out.append((k, i))
cs = ['// Generated by tools/gen_act1_lines.py: where each Act 1 story line starts in SLUS_014.11 (NTSC-U).',
      '// The text itself is read from the player\'s disc at runtime.',
      'namespace FMDuelist.Game;', '', 'static partial class FmAct1Story', '{',
      '    static readonly Dictionary<string, int> At = new()', '    {']
cs += [f'        ["{k}"] = 0x{i:X},' for k, i in out]
cs += ['    };', '}', '']
open(os.path.expanduser('~/fmsts/src/FMDuelist/Game/FmAct1Story.g.cs'), 'w', encoding='utf-8').write('\n'.join(cs))
print(len(out), 'lines')
