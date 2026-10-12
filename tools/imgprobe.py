import sys,os,struct
from PIL import Image
wa=open(os.path.expanduser('~/fmsts/cache/WA_MRG.MRG'),'rb').read()
def pal(buf,off,n=256):
    out=[]
    for k in range(n):
        v=struct.unpack_from('<H',buf,off+2*k)[0]
        r=(v&31)<<3; g=((v>>5)&31)<<3; b=((v>>10)&31)<<3
        out+= [r,g,b]
    return out
def card(i,base=0x169000,w=102,h=96,stride=0x3800):
    o=base+i*stride
    px=wa[o:o+w*h]; p=pal(wa,o+w*h)
    im=Image.frombytes('P',(w,h),px); im.putpalette(p); return im.convert('RGB')
out=os.path.expanduser('~/fmsts/cache/probe'); os.makedirs(out,exist_ok=True)
sheet=Image.new('RGB',(102*6,96*2))
for k,i in enumerate([0,1,2,3,346,721]): sheet.paste(card(i),(102*(k%6),0))
sheet.save(out+'/sheet.png'); print('ok')
