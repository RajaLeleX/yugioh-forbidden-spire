import sys, struct, json, os
sys.path.insert(0, os.path.dirname(__file__))
from iso import Disc
TYPES=["Dragon","Spellcaster","Zombie","Warrior","Beast-Warrior","Beast","Winged Beast","Fiend","Fairy","Insect","Dinosaur","Reptile","Fish","Sea Serpent","Machine","Thunder","Aqua","Pyro","Rock","Plant","Magic","Trap","Ritual","Equip"]
ATTR=["Light","Dark","Earth","Water","Fire","Wind","Magic","Trap"]
def chartable(p):
    d={}
    for line in open(p,encoding='utf-8-sig'):
        line=line.rstrip('\r\n')
        if '=' not in line: continue
        k,v=line.split('=',1); d[int(k,16)]=v if v else ' '
    return d
def text(buf,pos,d):
    s=''
    while True:
        b=buf[pos]; pos+=1
        if b==0xFF: return s
        if b==0xFE: s+='\n'
        elif b in d: s+=d[b]
        else: s+='[%02X]'%b
def main(binp,out):
    disc=Disc(binp); files={p.split(';')[0]:(l,sz) for p,l,sz,_ in disc.walk()}
    slus=disc.read(*files['/SLUS_014.11']); wa=disc.read(*files['/DATA/WA_MRG.MRG'])
    os.makedirs(out,exist_ok=True)
    open(os.path.join(out,'SLUS_014.11'),'wb').write(slus); open(os.path.join(out,'WA_MRG.MRG'),'wb').write(wa)
    d=chartable(os.path.join(os.path.dirname(__file__),'CharacterTable.txt'))
    cards=[]
    for i in range(722):
        v=struct.unpack_from('<I',slus,0x1C4A44+i*4)[0]; la=slus[0x1C5B33+i]
        np=struct.unpack_from('<H',slus,0x1C6002+i*2)[0]; dp=struct.unpack_from('<H',slus,0x1B0A02+i*2)[0]
        cards.append(dict(id=i+1,atk=(v&0x1FF)*10,def_=((v>>9)&0x1FF)*10,gs2=(v>>18)&0xF,gs1=(v>>22)&0xF,type=TYPES[(v>>26)&0x1F],
            level=la&0xF,attr=ATTR[(la>>4)&0xF] if (la>>4)<8 else la>>4,
            name=text(slus,0x1C6800+np-0x6000,d),desc=text(slus,0x1B11F4+dp-0x9F4,d)))
    fz=wa[0xB87800:0xB87800+0x10000]; fusions=[]
    for i in range(722):
        p=struct.unpack_from('<H',fz,2+i*2)[0]
        if not p: continue
        n=fz[p]; p+=1
        if n==0: n=511-fz[p]; p+=1
        while n>0:
            a,b,c,dd,e=fz[p:p+5]; p+=5
            fusions.append((i+1,((a&3)<<8)|b,((a>>2&3)<<8)|c)); n-=1
            if n>0: fusions.append((i+1,((a>>4&3)<<8)|dd,((a>>6&3)<<8)|e)); n-=1
    eq={}; p=0x1C4000+0x000 
    m=wa[12079104:12079104+10240]; p=0
    while True:
        cid=struct.unpack_from('<H',m,p)[0]; p+=2
        if cid==0: break
        cnt=struct.unpack_from('<H',m,p)[0]; p+=2
        eq[cid]=[struct.unpack_from('<H',m,p+2*k)[0] for k in range(cnt)]; p+=2*cnt
    json.dump(dict(cards=cards,fusions=fusions,equips=eq),open(os.path.join(out,'fm.json'),'w'),indent=0)
    print(len(cards),len(fusions),len(eq))
if __name__=='__main__': main(sys.argv[1],sys.argv[2])
