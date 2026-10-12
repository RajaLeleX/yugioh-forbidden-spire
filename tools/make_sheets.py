# Builds design sheets. Manual rows live here; monster rows are derived from the disc extract (dev-only, never shipped).
import json, os
H=os.path.expanduser('~/fmsts'); D=json.load(open(H+'/cache/fm.json'))
C={c['id']:c for c in D['cards']}
def w(name,rows): json.dump(rows,open(f'{H}/sheets/{name}.json','w'),indent=1)
STARTER_MON=[253,154,172,282,242,395,218,247,417,191,177,210,244,180]
STARTER_SPELL=[337,336,342]
STARTER_RANDOM_HEALS=dict(count=3, fromIds=[338,339,340,341])
DRAGON_BUFF_CARDS=[4,200]
EXODIA=[21,19,20,17,18]
RAR={1:'Common',2:'Common',3:'Common',4:'Uncommon',5:'Uncommon',6:'Rare',7:'Rare',8:'Rare',9:'Rare',10:'Rare',11:'Rare',12:'Rare'}
MONTYPES=["Dragon","Spellcaster","Zombie","Warrior","Beast-Warrior","Beast","Winged Beast","Fiend","Fairy","Insect","Dinosaur","Reptile","Fish","Sea Serpent","Machine","Thunder","Aqua","Pyro","Rock","Plant"]
mons=[]
for c in D['cards']:
    if c['type'] not in MONTYPES: continue
    lv=c['level']; dragon=c['type']=='Dragon'
    if dragon:
        acts=['None','Common','Common'] if lv<=4 else ['None','Uncommon','Common']
    else:
        r=RAR.get(lv,'Rare')
        acts=[r if lv<=4 else 'None', r if lv<=6 else 'None', r]
    if c['id'] in EXODIA: acts=['None','None','None']
    mons.append(dict(id=c['id'],cls=f"FmCard{c['id']:03d}",type=c['type'],level=lv,atk=c['atk'],def_=c['def_'],exodia=c['id'] in EXODIA,
        dragonTier=('none' if not dragon else 'weak' if lv<=4 else 'strong'),
        act1=acts[0],act2=acts[1],act3=acts[2],starter=STARTER_MON.count(c['id']),
        fuseResultNeedsBuff=dragon and lv>=5, name_dev=c['name']))
w('monsters',mons)
# effect kinds implemented in code: burn, burn_all, heal, field, equip, trap, utility, ritual, invuln
S=[
 (343,'burn',50,'AnyEnemy','Common',0),(344,'burn',100,'AnyEnemy','Common',0),(345,'burn',200,'AnyEnemy','Common',0),
 (346,'burn',500,'AnyEnemy','Uncommon',0),(347,'burn_selfdmg',1000,'AnyEnemy','Uncommon',0),
 (337,'burn_all',3000,'AllEnemies','Rare',1),(336,'dark_hole',3000,'AllEnemies','Rare',1),
 (338,'heal',200,'Self','Common',0),(339,'heal',500,'Self','Common',0),(340,'heal',600,'Self','Common',0),
 (341,'heal',800,'Self','Common',0),(342,'heal',1000,'Self','Common',1),
 (348,'invuln',3,'Self','Rare',0),
 (320,'stop_defense',0,'AnyEnemy','Common',0),(672,'strip_buffs',0,'AllEnemies','Uncommon',0),
 (349,'stun',1,'AnyEnemy','Uncommon',0),
 (669,'block_weak',1000,'AnyEnemy','Uncommon',0),(655,'block_cleanse',800,'Self','Common',0),
 (664,'block_heal',500,'Self','Common',0),(663,'block',1500,'Self','Uncommon',0),
 (329,'stun',1,'AnyEnemy','Uncommon',0,'colorless'),(653,'burn',1500,'AnyEnemy','Uncommon',0,'colorless'),
 (660,'burn_all',1200,'AllEnemies','Uncommon',0,'colorless'),(661,'burn_strongest',2000,'AllEnemies','Rare',0,'colorless'),
 (662,'weak_all',3,'AllEnemies','Uncommon',0,'colorless'),(656,'strip_block_burn_all',1000,'AllEnemies','Rare',0,'colorless'),(350,'vulnerable_all',2,'AllEnemies','Uncommon',0),
]
FIELD={330:['Beast-Warrior','Insect','Plant','Beast'],331:['Dinosaur','Zombie','Rock'],332:['Dragon','Winged Beast','Thunder'],
       333:['Beast-Warrior','Warrior'],334:['Aqua','Thunder','Sea Serpent','Fish'],335:['Fiend','Spellcaster']}
spells=[]
for row in S:
    cid,kind,val,tgt,rar,st=row[:6]; pool=row[6] if len(row)>6 else 'fm'
    spells.append(dict(id=cid,cls=f"FmCard{cid:03d}",kind=kind,value=val,target=tgt,rarity=rar,starter=st,pool=pool,exhaust=cid in (336,337,348),boostTypes=[],name_dev=C[cid]['name']))
for cid,types in FIELD.items():
    spells.append(dict(id=cid,cls=f"FmCard{cid:03d}",kind='field',value=500,target='Self',rarity='Uncommon',starter=0,exhaust=True,boostTypes=types,name_dev=C[cid]['name']))
for c in D['cards']:
    if c['type']=='Equip':
        mega=c['id']==657
        spells.append(dict(id=c['id'],cls=f"FmCard{c['id']:03d}",kind='equip',value=1000 if mega else 500,target='Osty',rarity='Rare' if mega else 'Common',starter=0,exhaust=False,boostTypes=[],name_dev=c['name']))
TRAP={681:('trap_destroy_le',500),682:('trap_destroy_le',1000),683:('trap_destroy_le',1500),684:('trap_destroy_le',2000),685:('trap_destroy_le',3000),
      686:('trap_destroy_any',0),687:('trap_reflect_burn',0),688:('trap_weak',2),689:('trap_reverse_buffs',0),690:('trap_absorb',1)}
for cid,(kind,val) in TRAP.items():
    spells.append(dict(id=cid,cls=f"FmCard{cid:03d}",kind=kind,value=val,target='Self',rarity='Uncommon' if kind!='trap_destroy_any' else 'Rare',starter=0,exhaust=False,boostTypes=[],name_dev=C[cid]['name']))
w('spells',spells)
w('dragon_roll',[dict(id='act1_weak_dragon', act=1, chance=0.20, shopChance=0.40, maxLevel=4)])
w('starter_random',[dict(id='heals', count=STARTER_RANDOM_HEALS['count'], fromIds=STARTER_RANDOM_HEALS['fromIds'], distinct=True),
                    dict(id='monsters', count=14, act1='Common', maxAtk=1000, maxLevel=3, excludeTypes=['Dragon'], distinctTypes=True)])
RIT={670:'Black Luster Soldier',671:'Zera The Mant',673:'Super War-lion',674:"Fiend's Mirror",721:'Magician of Black Chaos',667:'Gate Guardian',
     675:'Blue-eyes Ultimate Dragon',666:'Yamadron',665:'Millennium Shield',676:'Performance of Sword',677:'Hungry Burger',678:'Sengenjin',
     679:'Skull Guardian',680:'Tri-horned Dragon',691:'Serpent Night Dragon',692:'Crab Turtle',693:'Mask of Shine & Dark',694:'Chakra',
     695:'Psycho-Puppet',696:'Javelin Beetle',697:'Garma Sword',698:'Cosmo Queen',699:'Dokurorider',700:'Fortress Whale'}
byname={c['name'].lower():c['id'] for c in D['cards']}
rits=[]
for cid,res in RIT.items():
    rid=byname.get(res.lower())
    if rid is None:
        cands=[c['id'] for c in D['cards'] if c['type'] in MONTYPES and res.lower().split()[0] in c['name'].lower()]
        rid=cands[0] if len(cands)==1 else None
    rits.append(dict(id=cid,cls=f"FmCard{cid:03d}",resultId=rid,materials=3,rarity='Rare',pool='colorless',starter=0,result_dev=res,name_dev=C[cid]['name']))
w('rituals',rits)
print('monsters',len(mons),'spells',len(spells),'rituals',len(rits),'unresolved rituals',[r for r in rits if r['resultId'] is None])
