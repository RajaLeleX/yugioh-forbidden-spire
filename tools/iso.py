import sys, struct, os
SEC=2352; OFF=24
class Disc:
    def __init__(s, path): s.f=open(path,'rb')
    def sector(s,n):
        s.f.seek(n*SEC+OFF); return s.f.read(2048)
    def read(s,lba,size):
        out=bytearray(); n=lba
        while len(out)<size: out+=s.sector(n); n+=1
        return bytes(out[:size])
    def listdir(s,lba,size):
        data=s.read(lba,size); i=0; res=[]
        while i<len(data):
            l=data[i]
            if l==0: i=(i//2048+1)*2048; continue
            rec=data[i:i+l]; elba=struct.unpack_from('<I',rec,2)[0]; esz=struct.unpack_from('<I',rec,10)[0]
            flags=rec[25]; nl=rec[32]; name=rec[33:33+nl].decode('latin1')
            res.append((name,elba,esz,flags&2)); i+=l
        return res
    def walk(s,lba=None,size=None,prefix=''):
        if lba is None:
            pvd=s.sector(16); root=pvd[156:156+34]
            lba=struct.unpack_from('<I',root,2)[0]; size=struct.unpack_from('<I',root,10)[0]
        for name,elba,esz,isdir in s.listdir(lba,size):
            if name in ('\x00','\x01'): continue
            p=prefix+'/'+name
            yield p,elba,esz,isdir
            if isdir: yield from s.walk(elba,esz,p)
if __name__=='__main__':
    d=Disc(sys.argv[1])
    for p,l,sz,isd in d.walk(): print(p,l,sz,'DIR' if isd else '')
