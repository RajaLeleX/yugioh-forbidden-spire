# Builds sheets/retheme.json: every remaining StS potion/relic -> a Forbidden Memories card it references
# (names, flavor and art come from the player's disc at runtime; this file only stores ids), and Game/FmRetheme.g.cs.
import os, json, re
H = os.path.expanduser('~/fmsts')
cards = json.load(open(f'{H}/cache/fm.json'))['cards']
by_name = {c['name'].lower(): c['id'] for c in cards}
R = json.load(open(f'{H}/cache/loc/localization_eng_relics.json', encoding='utf-8'))
P = json.load(open(f'{H}/cache/loc/localization_eng_potions.json', encoding='utf-8'))
E = json.load(open(f'{H}/sheets/removed_energy.json'))
snake = lambda n: re.sub(r'(?<!^)(?=[A-Z])', '_', n).upper()
rem_r = {snake(n) for n in E['relics']}; rem_p = {snake(n) for n in E['potions']}
taken = {r['art'] for r in json.load(open(f'{H}/sheets/relics.json'))} | {362, 315, 21}

POTIONS = {
 'ASHWATER': 'Eternal Rest', 'ATTACK_POTION': 'Legendary Sword', 'BEETLE_JUICE': 'Bad Reaction to Simochi', 'BLOCK_POTION': 'Steel Shell',
 'BLOOD_POTION': 'Red Medicine', 'BONE_BREW': 'Revival of Skeleton Rider', 'BOTTLED_POTENTIAL': 'Magical Labyrinth', 'CLARITY': 'Book of Secret Arts',
 'COLORLESS_POTION': 'Commencement Dance', 'DEXTERITY_POTION': 'Follow Wind', 'DISTILLED_CHAOS': 'Stain Storm', 'DROPLET_OF_PRECOGNITION': "Elf's Light",
 'DUPLICATOR': 'Megamorph', 'ENTROPIC_BREW': 'Hamburger Recipe', 'ESSENCE_OF_DARKNESS': 'Dark Energy', 'EXPLOSIVE_AMPOULE': 'Ookazi',
 'FAIRY_IN_A_BOTTLE': 'Soul of the Pure', 'FIRE_POTION': 'Hinotama', 'FLEX_POTION': 'Raise Body Heat', 'FOCUS_POTION': 'Violet Crystal',
 'FORTIFIER': 'Cyber Shield', 'FOUL_POTION': 'Vile Germs', 'FRUIT_JUICE': 'Mooyan Curry', 'FYSH_OIL': 'Umi', 'GAMBLERS_BREW': 'Fake Trap',
 'GHOST_IN_A_JAR': 'Dragon Capture Jar', 'GIGANTIFICATION_POTION': 'Axe of Despair', 'GLOWWATER_POTION': 'Mystical Moon', 'HEART_OF_IRON': 'Metalmorph',
 'KINGS_COURAGE': 'Power of Kaishin', 'LIQUID_BRONZE': 'Beast Fangs', 'LIQUID_MEMORIES': "Novox's Prayer", 'LUCKY_TONIC': 'Horn of the Unicorn',
 'MAZALETHS_GIFT': 'Dark Magic Ritual', 'OROBIC_ACID': 'Acid Trap Hole', 'POISON_POTION': 'Eradicating Aerosol', 'POTION_OF_BINDING': 'Spellbinding Circle',
 'POTION_OF_CAPACITY': 'Machine Conversion Factory', 'POTION_OF_DOOM': 'Widespread Ruin', 'POTION_SHAPED_ROCK': 'Sparks', 'POT_OF_GHOULS': 'Yamadron Ritual',
 'POWDERED_DEMISE': 'Black Pendant', 'POWER_POTION': 'Sword of Dark Destruction', 'REGEN_POTION': "Goblin's Secret Remedy",
 'SHACKLING_POTION': 'House of Adhesive Tape', 'SHIP_IN_A_BOTTLE': "Fortress Whale's Oath", 'SKILL_POTION': 'Elegant Egotist',
 'SOLDIERS_STEW': 'Garma Sword Oath', 'SPEED_POTION': 'Electro-whip', 'STABLE_SERUM': 'Bright Castle', 'STAR_POTION': 'Petit Angel',
 'STRENGTH_POTION': 'Invigoration', 'SWIFT_POTION': 'Winged Trumpeter', 'VULNERABLE_POTION': 'Stop Defense', 'WEAK_POTION': 'Eatgaboon',
}
RELICS = {
 'Akabeko': 'Frenzied Panda', 'Alchemical Coffer': 'Ancient Jar', 'Amethyst Aubergine': 'Rainbow Flower', 'Anchor': '30,000-Year White Turtle',
 'Arcane Scroll': 'Sectarian of Secrets', 'Bag of Marbles': 'Needle Ball', 'Bag of Preparation': 'Supporter in the Shadows', 'Beating Remnant': 'Embryonic Beast',
 'Beautiful Bracelet': 'Lucky Trinket', 'Belt Buckle': 'Hard Armor', 'Big Hat': 'White Magical Hat', 'Big Mushroom': 'Mushroom Man', 'Biiig Hug': 'Stuffed Animal',
 'Bing Bong': 'Boo Koo', 'Black Blood': 'The Melting Red Shadow', 'Black Star': 'Dark Shade', 'Blood Vial': 'Hourglass of Life', 'Bone Flute': 'Skull Servant',
 'Book of Five Rings': 'Masaki the Legendary Swordsman', 'Book Repair Knife': 'Winged Cleaver', 'Bowler Hat': 'Masked Clown', 'Brimstone': 'Fire Eye',
 'Bronze Scales': 'Armored Starfish', 'Burning Blood': 'Flame Ghost', 'Burning Sticks': 'Firegrass', 'Byrdpip': 'Niwatori', 'Calling Bell': 'Spirit of the Harp',
 "Captain's Wheel": 'The Furious Sea King', 'Cauldron': "Witch's Apprentice", 'Centennial Puzzle': 'Labyrinth Wall', "Charon's Ashes": 'The Wandering Doomed',
 'Choices Paradox': 'Job-change Mirror', 'The Chosen Cheese': 'Prevent Rat', 'Circlet': 'Goddess of Whim', 'Cloak Clasp': 'Curtain of the Dark Ones',
 'Cracked Core': 'Mega Thunderball', 'Crossbow': 'Silver Bow and Arrow', 'Cursed Kettle': 'Pot the Trick', 'Cursed Pearl': 'Enchanting Mermaid',
 'Darkstone Periapt': 'Mask of Darkness', 'Data Disk': 'Holograh', 'Daughter of the Wind': 'Djinn the Watcher of the Wind', 'Delicate Frond': 'Yashinoki',
 'Demon Tongue': 'That Which Feeds on Life', 'Diamond Diadem': 'Princess of Tsurugi', 'Dingy Rug': 'Wood Remains', 'Distinguished Cape': 'Kagemusha of the Blue Flame',
 'Divine Destiny': 'Lady of Faith', 'Divine Right': 'Protector of the Throne', "Dolly's Mirror": 'Wicked Mirror', 'Dragon Fruit': 'Snakeyashi',
 'Dream Catcher': 'Nemuriko', 'Driftwood': 'Root Water', 'Electric Shrymp': 'Electric Snake', 'Ember Tea': 'Hinotama Soul', 'Emotion Chip': 'Cyber-Stein',
 'Empty Cage': 'Dark Prisoner', 'Endless Appetite': 'Ooguchi', 'Eternal Feather': 'Wing Egg Elf', 'Fencing Manual': 'The Little Swordsman of Aile',
 'Festive Popper': 'Blast Juggler', 'Fiddle': 'Muse-A', 'Forgotten Soul': 'Phantom Ghost', 'Fresnel Lens': 'Big Eye', 'Funerary Mask': 'Temple of Skulls',
 'Fur Coat': 'Silver Fang', 'Galactic Dust': 'Star Boy', 'Gambling Chip': 'Dream Clown', 'Game Piece': 'Mysterious Puppeteer', 'Ghost Seed': 'Shadow Specter',
 'Girya': 'Giant Soldier of Stone', 'Glass Eye': 'Monster Eye', 'Glitter': 'Shining Friendship', 'Glowing Orb': 'Mystic Lamp', 'Gnarled Hammer': 'Hitotsu-me Giant',
 'Golden Compass': 'Weather Report', 'Golden Pearl': 'Kanikabuto', 'Gold-Plated Cables': 'Cyber Commander', 'Gorget': 'Arma Knight', 'Hand Drill': 'Mech Mole Zombie',
 'Hefty Tablet': 'The Statue of Easter Island', 'Helical Dart': 'Killer Needle', 'History Course': 'Ancient Brain', 'Horn Cleat': 'Horn Imp', 'Infused Core': 'Jinzo #7',
 'Ink Bottle': 'Dark Artist', 'Iron Club': 'Rock Ogre Grotto #1', 'Jeweled Mask': 'Illusionist Faceless Mage', 'Jewelry Box': 'Morphing Jar',
 'Joss Paper': 'Graveyard and the Hand of Invitation', 'Juzu Bracelet': 'Tao the Chanter', 'Kaleidoscope': 'Prisman', 'Kifuda': 'Mystical Capture Chain',
 'Kunai': 'Kunai with Chain', 'Kusarigama': 'Armed Ninja', 'Large Capsule': 'Monster Egg', 'Lasting Candy': 'Happy Lover', 'Lava Rock': 'Dissolverock',
 'Lead Paperweight': 'Barrel Rock', 'Leafy Poultice': 'Dryad', "Lee's Waffle": 'Obese Marmot of Nefariousness', 'Letter Opener': 'Bladefly',
 'Lizard Tail': 'Drooling Lizard', 'Looming Fruit': 'Laughing Flower', "Lord's Parasol": 'Weather Control', 'Lost Coffer': 'Living Vase',
 'Lost Wisp': 'Wretched Ghost of the Attic', 'Lucky Fysh': 'White Dolphin', 'Lunar Pastry': 'Moon Envoy', 'Mango': 'Barrel Lily', 'Massive Scroll': 'Yamatano Dragon Scroll',
 'Maw Bank': 'Zone Eater', 'Meal Ticket': 'Meotoko', 'Meat Cleaver': 'Tiger Axe', 'Meat on the Bone': 'Wolf', 'Membership Card': 'Trap Master',
 'Mercury Hourglass': 'Hourglass of Courage', 'Metronome': 'Oscillo Hero #2', 'Miniature Tent': 'Castle of Dark Illusions', 'Mini Regent': "Queen's Double",
 'Mr. Struggles': 'Kuriboh', 'Music Box': 'Dancing Elf', 'Mysterious Cocoon': 'Cocoon of Evolution', 'Mystic Lighter': 'Flame Manipulator',
 "Neow's Bones": 'Dokuroizo the Grim Reaper', "Neow's Torment": 'Trial of Nightmares', 'New Leaf': 'Green Phantom King', 'Ninja Scroll': 'Genin',
 'Nutritious Oyster': 'Yado Karu', 'Nutritious Soup': 'Toad Master', 'Oddly Smooth Stone': 'Sand Stone', 'Old Coin': 'Burglar', 'Orange Dough': 'Change Slime',
 'Orichalcum': 'Metal Guardian', 'Ornamental Fan': 'Goblin Fan', 'Orrery': 'Ray & Temperature', "Pael's Blood": 'Blue-eyed Silver Zombie', "Pael's Claw": 'Claw Reacher',
 "Pael's Eye": 'Eyearmor', "Pael's Growth": 'Ancient Tree of Enlightenment', "Pael's Horn": 'Horn of Light', "Pael's Legion": 'Insect Soldiers of the Sky',
 "Pael's Wing": 'Wings of Wicked Flame', "Pandora's Box": 'Mystery Hand', 'Pantograph': 'Dimensional Warrior', 'Paper Krane': 'Droll Bird', 'Paper Phrog': 'Frog The Jam',
 'Parrying Shield': 'One-eyed Shield Dragon', 'Pear': 'Griggle', 'Pendulum': 'Time Wizard', 'Pen Nib': 'Akakieisu', 'Permafrost': 'Ice Water', 'Petrified Toad': 'Haniwa',
 'Phial Holster': 'Water Element', 'Phylactery Unbound': 'Necrolancer the Timelord', 'Planisphere': 'Megirus Light', 'Pocketwatch': 'Gate Deeg',
 'Pollinous Core': 'Abyss Flower', 'Potion Belt': 'Aqua Madoor', 'Prayer Wheel': 'Magician of Faith', 'Precarious Shears': 'Brave Scizzar',
 'Precise Scissors': 'Kamakiriman', 'Preserved Fog': 'King Fog', 'Punch Dagger': 'Hero of the East', 'Rainbow Ring': 'Milus Radiant', 'Red Mask': 'Saggi the Dark Clown',
 'Red Skull': 'Skull Stalker', 'Red Vine Tea': 'Darkworld Thorns', 'Regalite': 'Minomushi Warrior', 'Regal Pillow': 'Sleeping Lion', 'Reptile Trinket': 'Krokodilus',
 'Ringing Triangle': 'Tenderness', 'Ring of the Drake': 'Lesser Dragon', 'Ring of the Snake': 'Sinister Serpent', 'Ripple Basin': 'Jellyfish',
 'Royal Poison': 'Fungi of the Musk', 'Royal Stamp': 'Lord of Zemia', 'Ruined Helmet': 'Battle Warrior', 'Runic Capacitor': 'Bolt Penguin', 'Runic Pyramid': 'Tainted Wisdom',
 'Sai': 'Kageningen', 'Screaming Flagon': 'Ameba', 'Scroll Boxes': 'Kamion Wizard', 'Sea Glass': 'Hitodenchak', 'Self-Forming Clay': 'Gruesome Goo', 'Sere Talon': 'Dig Beak',
 'Shovel': 'Shovel Crusher', 'Shuriken': 'Hyo', 'Signet Ring': 'Rhaimundos of the Red Sword', 'Silken Tress': 'Witch of the Black Forest', 'Sling of Courage': 'Penguin Soldier',
 'Small Capsule': 'Gorgon Egg', 'Snecko Skull': 'Beaked Snake', 'Sparkling Rouge': 'Nekogal #1', 'Stone Calendar': 'Muka Muka', 'Stone Humidifier': 'Stone Ghost',
 'Storybook': 'The Bewitching Phantom Thief', 'Strawberry': 'Dorover', 'Strike Dummy': 'Blocker', 'Sturdy Clamp': 'Kuwagata a', 'Sword of Jade': 'Swordsman from a Foreign Land',
 'Sword of Stone': 'Rock Ogre Grotto #2', 'Symbiotic Virus': 'Larvas', "Tanx's Whistle": 'Dragon Piper', 'Tea of Discourtesy': 'Dark Assailant', 'The Abacus': 'Fusionist',
 'The Boot': 'Grappler', 'The Courier': 'Kattapillar', 'Throwing Axe': 'Mountain Warrior', 'Tingsha': 'Tripwire Beast', 'Tiny Mailbox': 'Air Marmot of Nefariousness',
 'Toasty Mittens': 'Fireyarou', 'Toolbox': 'Mechanical Spider', 'Touch of Orobas': "Fiend's Hand", 'Tough Bandages': 'Three-legged Zombies', 'Toy Box': 'Toon Alligator',
 'Tri-Boomerang': 'Twin Long Rods #1', 'Tungsten Rod': 'Twin Long Rods #2', 'Tuning Fork': 'LaLa Li-oon', 'Twisted Funnel': 'Tentacle Plant', 'Unceasing Top': 'Korogashi',
 'Undying Sigil': 'Yaranzo', 'Unsettling Lamp': 'Midnight Fiend', 'Vajra': 'Electric Lizard', 'Vambrace': 'Armored Rat', 'Vexing Puzzlebox': 'Invader from Another Dimension',
 'Vine Bracelet': 'Man-eating Plant', 'Vitruvian Minion': 'Patrol Robo', 'White Beast Statue': 'Dragon Statue', 'White Star': 'Hoshiningen', 'Winged Boots': 'Hane-Hane',
 'Wing Charm': 'Fairy Dragon', "Wongo's Mystery Ticket": 'Great Bill', 'Wongo Customer Appreciation Badge': 'Little Chimera',
}

rows, used, errors = [], set(taken), []
def pick(name, kind, key):
    i = by_name.get(name.lower())
    if i is None: errors.append(f'{kind} {key}: no FM card named {name!r}'); return None
    if i in used: errors.append(f'{kind} {key}: {name} already used'); return None
    used.add(i); return i

for k in sorted({k.split('.')[0] for k in P}):
    if k in rem_p or k.startswith(('DEPRECATED', 'MOCK')): continue
    if k not in POTIONS: errors.append(f'potion {k} unmapped'); continue
    i = pick(POTIONS[k], 'potion', k)
    if i: rows.append({'kind': 'potion', 'key': k, 'fm': i, 'was': P.get(k + '.title')})

title_to_keys = {}
for k in sorted({k.split('.')[0] for k in R}):
    if k in rem_r or k.startswith('DEPRECATED'): continue
    title_to_keys.setdefault(R.get(k + '.title', k), []).append(k)
for t, keys in title_to_keys.items():
    base = t.rstrip('?')
    for k in keys:
        if t.endswith('???'):   # the fake merchant's knock-offs share their original's card
            continue
        if base not in RELICS: errors.append(f'relic {k} ({t}) unmapped'); continue
        i = pick(RELICS[base], 'relic', k)
        if i: rows.append({'kind': 'relic', 'key': k, 'fm': i, 'was': t})
# knock-offs reuse the original's card
orig = {r['was']: r['fm'] for r in rows if r['kind'] == 'relic'}
for t, keys in title_to_keys.items():
    if t.endswith('???') and t.rstrip('?') in orig:
        for k in keys: rows.append({'kind': 'relic', 'key': k, 'fm': orig[t.rstrip('?')], 'was': t, 'knockoff': True})

if errors:
    print('\n'.join(errors)); raise SystemExit(1)
json.dump(rows, open(f'{H}/sheets/retheme.json', 'w'), indent=1)

pot = [r for r in rows if r['kind'] == 'potion']; rel = [r for r in rows if r['kind'] == 'relic']
open(f'{H}/src/FMDuelist/Game/FmRetheme.g.cs', 'w').write(
    '// <auto-generated> from sheets/retheme.json </auto-generated>\nnamespace FMDuelist.Game;\npublic static partial class FmRetheme\n{\n'
    '    public static readonly Dictionary<string, int> Potions = new() { ' + ', '.join(f'["{r["key"]}"] = {r["fm"]}' for r in pot) + ' };\n'
    '    public static readonly Dictionary<string, int> Relics = new() { ' + ', '.join(f'["{r["key"]}"] = {r["fm"]}' for r in rel) + ' };\n'
    '    public static readonly HashSet<string> KnockOffs = new() { ' + ', '.join(f'"{r["key"]}"' for r in rel if r.get('knockoff')) + ' };\n}\n')
print('ok potions', len(pot), 'relics', len(rel))
