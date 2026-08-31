from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src" / "Shutdown" / "Assets"
FONT = Path(r"C:\Windows\Fonts\SegoeIcons.ttf")
SIZES = [(16,16),(20,20),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)]

def rounded(draw, box, radius, fill):
    draw.rounded_rectangle(box, radius=radius, fill=fill)

def app_icon(size=1024):
    im = Image.new("RGBA", (size,size), (0,0,0,0)); px=im.load()
    m=int(size*.0625); r=int(size*.18); bottom=int(size*.695)
    for y in range(m,size-m):
        t=(y-m)/(size-2*m)
        for x in range(m,size-m):
            if x < m+r and y < m+r and (x-(m+r))**2+(y-(m+r))**2>r*r: continue
            if x > size-m-r and y < m+r and (x-(size-m-r))**2+(y-(m+r))**2>r*r: continue
            if x < m+r and y > size-m-r and (x-(m+r))**2+(y-(size-m-r))**2>r*r: continue
            if x > size-m-r and y > size-m-r and (x-(size-m-r))**2+(y-(size-m-r))**2>r*r: continue
            top=(40,75,155); bot=(7,25,67)
            px[x,y]=tuple(int(top[i]*(1-t)+bot[i]*t) for i in range(3))+(255,)
    d=ImageDraw.Draw(im)
    d.rectangle((m,bottom,size-m,size-m-r), fill=(33,66,147,255))
    d.rounded_rectangle((m,bottom,size-m,size-m), radius=r, fill=(24,53,126,255))
    d.rectangle((m,bottom,size-m,bottom+r), fill=(39,80,175,255))
    sc=size/256
    def line(points, fill, width): d.line([(int(x*sc),int(y*sc)) for x,y in points], fill=fill, width=max(1,int(width*sc)), joint="curve")
    line([(168,210),(178,200),(188,210)], (145,189,255,255), 7)
    cx,cy,rad=214*sc,210*sc,21*sc
    symbol=Image.new("RGBA", im.size, (0,0,0,0)); sd=ImageDraw.Draw(symbol)
    sd.ellipse((int(cx-rad),int(cy-rad),int(cx+rad),int(cy+rad)), outline="white", width=max(1,int(8*sc)))
    sd.rectangle((int(cx-9*sc),int(cy-rad-2*sc),int(cx+9*sc),int(cy-rad+12*sc)), fill=(0,0,0,0))
    sd.line((int(cx),int(192*sc),int(cx),int(212*sc)), fill="white", width=max(1,int(8*sc)))
    im.alpha_composite(symbol)
    return im

def glyph_icon(code, color, scheduled=False, hibernate=False):
    size=512
    im=Image.new("RGBA",(size,size),(0,0,0,0)); d=ImageDraw.Draw(im)
    font_size = 325 if scheduled and hibernate else 400 if hibernate else 340 if scheduled else 430
    font=ImageFont.truetype(str(FONT), font_size)
    text=chr(code)
    box=d.textbbox((0,0),text,font=font)
    x=(size-(box[2]-box[0]))//2-box[0]; y=(size-(box[3]-box[1]))//2-box[1]
    if scheduled: x-=58; y-=48
    elif hibernate: x-=50; y-=28
    d.text((x,y),text,font=font,fill=color)
    if hibernate:
        d.line((375,315,375,400), fill=color, width=24)
        d.line((340,368,375,403,410,368), fill=color, width=24, joint="curve")
    if scheduled:
        cx,cy,r=390,390,90
        d.ellipse((cx-r,cy-r,cx+r,cy+r), outline=color, width=26)
        d.line((cx,cy,cx,cy-52), fill=color, width=22)
        d.line((cx,cy,cx+42,cy+20), fill=color, width=22)
    return im

def save_ico(image, path):
    image.save(path, format="ICO", sizes=SIZES)

OUT.mkdir(parents=True,exist_ok=True)
# The approved red application icon is packaged by build-app-icon.cjs.
# Do not overwrite it when rebuilding monochrome action icons.

glyphs={"shutdown":0xE7E8,"restart":0xE72C,"sleep":0xE708,"hibernate":0xE708,"lock":0xE72E}
for name,code in glyphs.items():
    for tone,color in (("white",(255,255,255,255)),("black",(20,20,20,255))):
        base=glyph_icon(code,color,hibernate=name=="hibernate")
        save_ico(base,OUT/f"tray_{name}_{tone}.ico")
        scheduled=glyph_icon(code,color,scheduled=True,hibernate=name=="hibernate")
        save_ico(scheduled,OUT/f"tray_{name}_scheduled_{tone}.ico")

print(f"Generated icons in {OUT}")
