#!/usr/bin/env bash
# Dev loop: stop the game, copy the build into the mods folder, relaunch through Steam.
G="/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/FMDuelist"
cd ~/tools/universal-modder
P=$(PYTHONPATH=. python -m um win ps SlayTheSpire2 | awk '{print $1}' | head -1)
[ -n "$P" ] && PYTHONPATH=. python -m um win kill "$P" >/dev/null
sleep 3
mkdir -p "$G" "$G/stars"
cp ~/fmsts/dist/FMDuelist/stars/*.png "$G/stars/"
cp ~/fmsts/src/FMDuelist/bin/Release/net9.0/FMDuelist.dll ~/fmsts/dist/FMDuelist/
cp ~/fmsts/dist/FMDuelist/FMDuelist.dll ~/fmsts/dist/FMDuelist/FMDuelist.json ~/fmsts/dist/FMDuelist/millennium_puzzle.png ~/fmsts/dist/FMDuelist/star_chip.png ~/fmsts/dist/FMDuelist/star_chip_removal.png ~/fmsts/dist/FMDuelist/position_attack.png ~/fmsts/dist/FMDuelist/position_defense.png "$G/"
[ -f ~/fmsts/devdata/settings.cfg ] && cp ~/fmsts/devdata/settings.cfg "$G/settings.cfg"
# local-only portrait (a screenshot from the creator's own copy); never part of the published package
[ -f ~/fmsts/devdata/duel_master_k.png ] && cp ~/fmsts/devdata/duel_master_k.png "$G/duel_master_k.png"
[ -f ~/fmsts/devdata/seto_standard.png ] && cp ~/fmsts/devdata/seto_standard.png "$G/seto_standard.png"
[ -f ~/fmsts/devdata/card_back.png ] && cp ~/fmsts/devdata/card_back.png "$G/card_back.png"
[ -d ~/fmsts/devdata/story ] && mkdir -p "$G/story" && cp ~/fmsts/devdata/story/*.png "$G/story/"
[ -d ~/fmsts/devdata/music ] && mkdir -p "$G/music" && cp ~/fmsts/devdata/music/track*.pcm "$G/music/" 2>/dev/null
PYTHONPATH=. python -m um win launch --steam 2868840 >/dev/null 2>&1
sleep "${1:-28}"
grep -i "FMDuelist\|Exception" "$APPDATA/SlayTheSpire2/logs/godot.log" | head -${2:-15}
