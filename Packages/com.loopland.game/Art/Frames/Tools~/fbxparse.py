import struct, zlib, sys, numpy as np
data=open(sys.argv[1],'rb').read()
ver=struct.unpack('<I',data[23:27])[0]
def read_node(off):
    if ver>=7500:
        end,np_,pl=struct.unpack('<QQQ',data[off:off+24]); off+=24
    else:
        end,np_,pl=struct.unpack('<III',data[off:off+12]); off+=12
    nl=data[off]; off+=1
    if end==0: return None,off
    name=data[off:off+nl].decode(); off+=nl
    props=[]
    for _ in range(np_):
        t=chr(data[off]); off+=1
        if t in 'YCIFDL':
            fmt={'Y':'<h','C':'<?','I':'<i','F':'<f','D':'<d','L':'<q'}[t]; sz=struct.calcsize(fmt)
            props.append(struct.unpack(fmt,data[off:off+sz])[0]); off+=sz
        elif t in 'fdlib':
            n,enc,cl=struct.unpack('<III',data[off:off+12]); off+=12
            raw=data[off:off+cl]; off+=cl
            if enc: raw=zlib.decompress(raw)
            dt={'f':'<f4','d':'<f8','l':'<i8','i':'<i4','b':'?'}[t]
            props.append(np.frombuffer(raw,dtype=dt))
        elif t in 'SR':
            n=struct.unpack('<I',data[off:off+4])[0]; off+=4
            props.append(data[off:off+n]); off+=n
    children=[]
    while off<end:
        c,off=read_node(off)
        if c is None: break
        children.append(c)
    return (name,props,children),end
def parse():
    off=27; nodes=[]
    while True:
        n,off=read_node(off)
        if n is None: break
        nodes.append(n)
    return nodes
def find(nodes,name):
    for n in nodes:
        if n[0]==name: yield n
        yield from find(n[2],name)
