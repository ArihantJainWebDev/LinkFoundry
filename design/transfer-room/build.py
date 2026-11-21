"""Compose the original WPF hardware and packet tracks with a synchronized range inspector."""
from pathlib import Path
from xml.etree import ElementTree as ET

here = Path(__file__).parent
presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"
xaml = "http://schemas.microsoft.com/winfx/2006/xaml"
ET.register_namespace("", presentation)
ET.register_namespace("x", xaml)
scene = ET.parse(here.parent / "server-room/ServerRoom.xaml").getroot()
viewport = next(n for n in scene if n.tag.endswith("}Viewport3D"))
viewport.attrib.update({"Canvas.Left": "16", "Canvas.Top": "104", "Width": "744", "Height": "345"})
story = next(n for n in scene.iter() if n.tag.endswith("}Storyboard"))
for n in list(story):
    if n.get("Storyboard.TargetName", "").startswith(("Hud", "Progress", "Status")):
        story.remove(n)

ui = []
lime, blue, white, muted = "#BFF773", "#65D6FF", "#EEF5F7", "#A7BCC8"
def label(x,y,value,size=15,color=white,bold=False,name=None,mono=False):
    attr = f'x:Name="{name}"' if name else ""
    ui.append(f'<TextBlock {attr} Canvas.Left="{x}" Canvas.Top="{y}" Text="{value}" FontSize="{size}" Foreground="{color}" FontFamily="{("Consolas" if mono else "Segoe UI")}" FontWeight="{("SemiBold" if bold else "Normal")}"/>')
def box(x,y,w,h,color,r=0,name=None):
    attr = f'x:Name="{name}"' if name else ""
    ui.append(f'<Border {attr} Canvas.Left="{x}" Canvas.Top="{y}" Width="{w}" Height="{h}" CornerRadius="{r}" Background="{color}"/>')
def animate(name,prop,keys,text=False):
    kind,frame=("String","Discrete") if text else ("Double","Linear")
    node=ET.SubElement(story, f'{{{presentation}}}{kind}AnimationUsingKeyFrames', {"Storyboard.TargetName":name,"Storyboard.TargetProperty":prop})
    for t,v in keys:
        ET.SubElement(node,f'{{{presentation}}}{frame}{kind}KeyFrame',{"KeyTime":f"0:0:{t:.3f}","Value":str(v)})
def pin(x,y,title,color,points):
    ui.append(f'<Path Data="{points}" Stroke="{color}" StrokeThickness="1.3"/>')
    width=len(title)*7.4+20
    box(x,y,width,25,"#EB14232D",4)
    label(x+9,y+4,title,12,color,True,mono=True)

label(26,17,"World Atlas.zip",32,white,True)
label(27,62,"Direct download    /    96 MiB",14,muted)
box(990,27,164,34,"#22362F",17)
label(1007,33,"●  Downloading",15,lime,True,"Status")
animate("Status","Text",[(0,"●  Downloading"),(6,"◈  Assembling"),(7.6,"✓  Completed")],True)
box(16,94,744,357,"#101923",8)
ui.append(ET.tostring(viewport,encoding="unicode"))
pin(35,157,"File server",white,"M124,170 L151,170 L172,191")
pin(396,147,"Wi-Fi · A + C",lime,"M437,171 L437,222 L407,271")
pin(210,405,"USB tether · B + D",blue,"M342,415 L368,415 L377,378")
pin(583,199,"Local drive",white,"M632,223 L632,251 L596,289")

box(774,94,390,357,"#192632",10)
label(797,111,"Saved file ranges",21,white,True)
for index,(network,color,letters,end,speed) in enumerate([
    ("Wi-Fi",lime,"AC",4,12),("USB tether",blue,"BD",6,8)
]):
    y=155+index*139
    box(798,y+7,8,8,color,4)
    label(815,y,network,16,white,True)
    rate=f"Rate{index}"
    label(1011,y,f"{speed} MiB/s",15,color,True,rate,True)
    animate(rate,"Text",[(0,f"{speed} MiB/s"),(end,"48 MiB saved")],True)
    for j,letter in enumerate(letters):
        offset=(ord(letter)-ord("A"))*24
        top=y+38+j*43
        box(798,top,25,24,"#304250",5)
        label(806,top+3,letter,13,color,True,mono=True)
        label(835,top+2,f"{offset}–{offset+24} MiB",14,white,mono=True)
        box(951,top+8,185,8,"#364853",4)
        box(951,top+8,0,8,color,4,"RangeBar"+letter)
        animate("RangeBar"+letter,"Width",[(0,0),(end,185)])
box(797,282,338,1,"#344450")
box(16,464,1148,117,"#1C2A36",10)
label(37,479,"File on disk",19,white,True,"FileTitle")
animate("FileTitle","Text",[(0,"File on disk"),(6,"Assembling file · range A"),(6.4,"Assembling file · range B"),(6.8,"Assembling file · range C"),(7.2,"Assembling file · range D"),(7.6,"World Atlas.zip · Ready to open")],True)
label(1004,481,"0 / 96 MiB",17,white,True,"Total",True)
ticks=[i/4 for i in range(25)]
animate("Total","Text",[(t,f"{int(min(48,12*t)+min(48,8*t))} / 96 MiB") for t in ticks],True)
for i,letter in enumerate("ABCD"):
    color,end=(lime,4) if i%2==0 else (blue,6)
    x=38+i*277
    label(x,515,f"{letter}   {24*i}–{24*(i+1)} MiB",13,muted,False,mono=True)
    box(x,542,269,13,"#354753",3)
    box(x,542,0,13,color,3,"File"+letter)
    animate("File"+letter,"Width",[(0,0),(end,269)])
    box(x,559,0,2,"#E6F2F6",0,"Merged"+letter)
    animate("Merged"+letter,"Width",[(6+i*.4,0),(6.35+i*.4,269)])

result=f'<Canvas xmlns="{presentation}" xmlns:x="{xaml}" Width="1180" Height="597" Background="#101923"><Canvas.Resources>{ET.tostring(story,encoding="unicode")}</Canvas.Resources>{"".join(ui)}</Canvas>'
(here/"TransferRoom.xaml").write_text(result,encoding="utf-8")
