import numpy as np
from scipy import ndimage
def hair(a):
    r,g,b,al=[a[...,i].astype(int) for i in range(4)]
    p=(r>170)&(g<120)&(al>128)
    ys,xs=np.nonzero(p)
    return xs.mean(),ys.mean(),np.percentile(ys,10),np.percentile(ys,95),np.percentile(xs,5),np.percentile(xs,95)
def visor4(a):
    """Face = green patch with true-blue eye pixels inside, searched only around the lower middle
    of the hair (her head sits there). Falls back to an estimate from the hair."""
    A=a.astype(int); r,g,b,al=[A[...,i] for i in range(4)]
    hx,hy,ht,hb,hl,hr=hair(a)
    green=(g>140)&(g>r+45)&(g>b+15)&(al>200)
    eye=(b>150)&(b>g+30)&(r<110)&(al>200)
    lab,n=ndimage.label(green)
    best=None
    for k in range(1,n+1):
        m=lab==k; area=m.sum()
        if area<40 or area>8000: continue
        ys,xs=np.nonzero(m); cx,cy=xs.mean(),ys.mean()
        if not (hl<cx<hr and ht<cy<hb+0.15*(hb-ht)): continue
        f=ndimage.binary_fill_holes(ndimage.binary_closing(m,iterations=2))
        nb=(eye&ndimage.binary_dilation(f,iterations=1)).sum(); nh=(f&~m).sum()
        d=np.hypot(cx-hx,cy-(hy+0.2*(hb-ht)))/(hb-ht)
        score=min(nb,40)*3+min(nh,60)-d*60
        if best is None or score>best[0]: best=(score,cx,cy,nb,nh)
    est=(hx,hy+0.2*(hb-ht))
    if best is None or (best[3]<3 and best[4]<5): return est+('hair',)
    return (best[1],best[2],'face')
