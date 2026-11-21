"""Generate a self-contained WPF scene. All hardware is modeled here in world units."""
from collections import defaultdict
from math import sin, cos, pi, dist
from pathlib import Path

folder = Path(__file__).parent
mesh = defaultdict(lambda: ([], []))
lime, blue = '#BFF773', '#65D6FF'
def face(color, points):
    vertices, indices = mesh[color]
    offset = len(vertices)
    vertices.extend(points)
    for i in range(1, len(points)-1):
        indices.extend((offset, offset+i, offset+i+1))

def block(x, y, z, w, h, d, color):
    a,b=x-w/2,x+w/2
    c,e=y,y+h
    f,g=z-d/2,z+d/2
    for quad in [
        [(a,c,g),(b,c,g),(b,e,g),(a,e,g)], [(b,c,f),(a,c,f),(a,e,f),(b,e,f)],
        [(b,c,g),(b,c,f),(b,e,f),(b,e,g)], [(a,c,f),(a,c,g),(a,e,g),(a,e,f)],
        [(a,e,g),(b,e,g),(b,e,f),(a,e,f)], [(a,c,f),(b,c,f),(b,c,g),(a,c,g)]]:
        face(color,quad)

def disc(x,y,z,r,color,segments=24):
    face(color,[(x+r*cos(i*2*pi/segments),y+r*sin(i*2*pi/segments),z) for i in range(segments)])

def serialize():
    result=[]
    for col,(v,ix) in mesh.items():
        base='#17201D' if col in (lime,blue) else col
        material=f'<MaterialGroup><DiffuseMaterial Brush="{base}"/><SpecularMaterial Brush="#24333B" SpecularPower="38"/>'
        if col in (lime,blue,'#E6FEAE'):
            material+=f'<EmissiveMaterial Brush="{col}"/>'
        material+='</MaterialGroup>'
        result.append('<GeometryModel3D><GeometryModel3D.Geometry><MeshGeometry3D Positions="'+
            ' '.join(','.join(f'{n:.4f}' for n in p) for p in v)+'" TriangleIndices="'+' '.join(map(str,ix))+
            '"/></GeometryModel3D.Geometry><GeometryModel3D.Material>'+material+'</GeometryModel3D.Material></GeometryModel3D>')
    mesh.clear()
    return '\n'.join(result)

def slab(x,z,w,d):
    for margin,y,col in [(.4,.016,'#18232C'),(.18,.026,'#111B23'),(0,.043,'#34444D')]:
        block(x,y,z,w+margin,.04,d+margin,col)

# The raised floor and inset cable raceways.
block(0,-.5,0,28,.5,17,'#27343F')
for i in range(-14,15,2): block(i,.003,0,.018,.004,17,'#3C4A54')
for i in range(-8,9,2): block(0,.004,i,28,.003,.018,'#3C4A54')
routes=[
    [(-5.1,.18,1.4),(-3.4,.18,1.4),(-3.4,.18,-2.1),(0,.18,-2.1),(3.65,.18,-2.1),(3.65,.18,.15),(5.55,.18,.15)],
    [(-5.1,.18,1.7),(-3.9,.18,1.7),(-3.9,.18,3.35),(.65,.18,3.35),(4.4,.18,3.35),(4.4,.18,.8),(5.55,.18,.8)]]
for points,col in zip(routes,[lime,blue]):
    for (x,y,z),(xx,yy,zz) in zip(points,points[1:]):
        for width,height,base,color in [(.22,.10,.04,'#101921'),(.07,.025,.143,col)]:
            block((x+xx)/2,base,(z+zz)/2,abs(x-xx)+width,height,abs(z-zz)+width,color)

# Steel rack, recessed mounting rails and individually modeled hot-swap bays.
x,z=-5.75,0
slab(x,z,3.3,3.6)
block(x,.13,z,2.95,5.55,3.0,'#25323C')
block(x,5.68,z,3.12,.15,3.15,'#74838B')
block(x,.16,1.53,2.8,5.42,.12,'#111921')
for xx in [x-1.43,x+1.43]:
    block(xx,.17,1.61,.13,5.5,.18,'#86959B')
    for j in range(14): block(xx,.38+j*.37,1.713,.044,.10,.02,'#26333C')
for j in range(7):
    yy=1.30+j*.54
    block(x,yy,1.63,2.62,.48,.22,'#485762')
    for k in range(5):
        xx=x-1.03+k*.47
        block(xx,yy+.06,1.77,.42,.34,.09,'#1B272E')
        for s in range(3):block(xx,yy+.12+s*.07,1.822,.28,.028,.016,'#687782')
        block(xx+.14,yy+.075,1.835,.046,.045,.015,lime if (k+j)%3 else blue)
    block(x-1.22,yy+.10,1.79,.09,.27,.13,'#92A2A9')
block(x,5.13,1.63,2.59,.37,.22,'#32434D')
block(x-.5,5.23,1.753,.9,.13,.02,blue)
for i in range(3):disc(x+.60+i*.18,5.29,1.77,.045,lime)
for xx in [x-.77,x,x+.77]:
    disc(xx,.74,1.67,.35,'#839099')
    disc(xx,.74,1.68,.31,'#111B23')
    for j in range(7):
        a=j*2*pi/7
        face('#52616A',[(xx,.74,1.70),(xx+.27*cos(a),.74+.27*sin(a),1.70),
            (xx+.20*cos(a+.53),.74+.20*sin(a+.53),1.70)])
    disc(xx,.74,1.72,.10,'#7B8C95')
for j in range(16):block(x+1.483,1.2+j*.21,0,.025,.043,2.30,'#101D25')
for i in range(8):block(x-.75+i*.21,5.837,0,.09,.012,1.95,'#3A4954')

# Access point with antennae, tethered phone on a dock, destination storage.
slab(0,-2.1,2.62,1.72)
block(0,.16,-2.1,2.45,.52,1.55,'#394852')
block(0,.68,-2.1,2.50,.11,1.58,'#829097')
for xx in [-.95,.95]:
    block(xx,.75,-2.70,.13,1.24,.13,'#374A55')
    block(xx,1.99,-2.70,.13,.08,.13,lime)
for i in range(5):block(-.73+i*.25,.43,-1.31,.12,.055,.025,lime)
for i in range(7):block(-.57+i*.18,.80,-2.08,.065,.013,.60,'#475B65')
slab(.65,3.35,1.85,1.2)
block(.65,.15,3.35,1.65,.27,.98,'#50616A')
block(.65,.37,3.32,1.17,2.04,.17,'#8D9DA5')
block(.65,.43,3.425,1.07,1.92,.045,'#152734')
block(.65,.59,3.456,.96,1.52,.012,'#21516B')
for j in range(4):block(.38+j*.15,1.13,3.47,.085,.14+j*.13,.012,blue)
block(.65,2.18,3.46,.27,.046,.018,'#6A818B')
slab(6.2,.40,3.65,3.16)
for j in range(4):
    y=.18+j*.49
    block(6.2,y,.40,3.14,.39,2.63,'#53646E')
    block(6.2,y+.05,1.74,3.0,.28,.07,'#25343F')
    for k in range(11):block(5.12+k*.17,y+.12,1.79,.075,.10,.025,'#75858B')
    block(7.42,y+.12,1.79,.11,.08,.026,[lime,blue][j%2])
block(6.2,2.05,.4,3.25,.16,2.73,'#92A1A6')
block(6.2,2.217,.42,2.58,.035,2.03,'#435760')
for j in range(4):block(5.52+j*.43,2.255,.43,.30,.035,.84,'#263542')
static=serialize()

animations=[]
models=[]
for route_index,(points,color) in enumerate(zip(routes,[lime,blue])):
    lengths=[dist(a,b) for a,b in zip(points,points[1:])]
    times=[0]
    for length in lengths:times.append(times[-1]+length/sum(lengths))
    period=2.4 if route_index==0 else 3.6
    for j in range(4 if route_index==0 else 3):
        name=f'Packet{route_index}_{j}'
        block(0,0,0,.32,.21,.29,color)
        block(0,.21,0,.20,.025,.17,'#F5FFE5')
        models.append(f'<Model3DGroup><Model3DGroup.Transform><Transform3DGroup><TranslateTransform3D x:Name="{name}"/><TranslateTransform3D x:Name="{name}Hide"/></Transform3DGroup></Model3DGroup.Transform>{serialize()}</Model3DGroup>')
        end=4 if route_index==0 else 6
        animations.append(f'<DoubleAnimationUsingKeyFrames Storyboard.TargetName="{name}Hide" Storyboard.TargetProperty="OffsetY"><DiscreteDoubleKeyFrame KeyTime="0:0:{end}" Value="-15"/></DoubleAnimationUsingKeyFrames>')
        phase=j/(4 if route_index==0 else 3)
        for axis,index in [('X',0),('Z',2),('Y',1)]:
            # Start at a different point along the same repeating route.
            keyframes=[(a-phase,p[index]) for a,p in zip(times,points)]
            keys=sorted([(t+k,v) for t,v in keyframes for k in (0,1) if 0<t+k<1])
            segment=next(i for i in range(len(lengths)) if times[i]<=phase<=times[i+1])
            a,b=points[segment][index],points[segment+1][index]
            value=a+(b-a)*(phase-times[segment])/(times[segment+1]-times[segment])
            # At the wrap, the ending position precedes the starting position.
            wrap=1-phase
            keys=[(t,v) for t,v in keys if abs(t-wrap)>1e-7]
            if 0<wrap<1:keys.extend([(wrap-.0001,points[-1][index]),(wrap,points[0][index])])
            keys=sorted(keys)
            keys=[(0,value)]+keys+[(1,points[-1][index] if phase==0 else value)]
            frames=''.join(f'<LinearDoubleKeyFrame KeyTime="0:0:{period*t:.5f}" Value="{v:.4f}"/>' for t,v in keys)
            animations.append(f'<DoubleAnimationUsingKeyFrames Storyboard.TargetName="{name}" Storyboard.TargetProperty="Offset{axis}" Duration="0:0:{period}" RepeatBehavior="Forever">{frames}</DoubleAnimationUsingKeyFrames>')

for j in range(4):
    block(5.52+j*.43,2.255,.43,.30,.065,.84,[lime,blue][j%2])
    models.append(f'<Model3DGroup><Model3DGroup.Transform><TranslateTransform3D x:Name="Range{j}" OffsetY="-15"/></Model3DGroup.Transform>{serialize()}</Model3DGroup>')
    t=6+j*.4
    animations.append(f'<DoubleAnimationUsingKeyFrames Storyboard.TargetName="Range{j}" Storyboard.TargetProperty="OffsetY"><DiscreteDoubleKeyFrame KeyTime="0:0:{t:.2f}" Value="0.65"/><LinearDoubleKeyFrame KeyTime="0:0:{t+.35:.2f}" Value="0"/></DoubleAnimationUsingKeyFrames>')
def label(x,y,title,size,color,weight='Normal',font='Segoe UI',name=''):
    attr=f'x:Name="{name}"' if name else ''
    return f'<TextBlock {attr} Canvas.Left="{x}" Canvas.Top="{y}" FontFamily="{font}" FontSize="{size}" FontWeight="{weight}" Foreground="{color}" Text="{title}"/>'
def bar(x,y,w,h,color,name=''):
    attr=f'x:Name="{name}"' if name else ''
    return f'<Rectangle {attr} Canvas.Left="{x}" Canvas.Top="{y}" Width="{w}" Height="{h}" Fill="{color}"/>'
hud=[label(32,17,'World Atlas.zip',29,'#F0F4F6','SemiBold'),label(33,59,'Transfer view   /   Server room',14,'#B3C3CE'),
     label(804,35,'●   Downloading',16,lime,'SemiBold',name='Status')]
for x,name,value,color in [(32,'SOURCE','File server','#F0F4F6'),(273,'WI-FI','12 MiB/s',lime),(510,'USB TETHER','8 MiB/s',blue),(752,'SAVED TO DISK','60 / 96 MiB','#F0F4F6')]:
    hud.extend([bar(x,489,27,3,color),label(x,502,name,12,'#AEC1CD','SemiBold','Consolas'),label(x,523,value,22,color,'SemiBold',name=f'Hud{x}')])
hud+= [bar(32,569,936,5,'#30424E'),bar(32,569,0,5,'#B9D4BE','Progress')]
def captions(name,keys):
    frames=''.join(f'<DiscreteStringKeyFrame KeyTime="0:0:{t:.3f}" Value="{value}"/>' for t,value in keys)
    animations.append(f'<StringAnimationUsingKeyFrames Storyboard.TargetName="{name}" Storyboard.TargetProperty="Text">{frames}</StringAnimationUsingKeyFrames>')
captions('Status',[(0,'●   Downloading'),(6,'◈   Assembling'),(7.6,'✓   Completed')])
captions('Hud273',[(0,'12 MiB/s'),(4,'48 MiB saved')])
captions('Hud510',[(0,'8 MiB/s'),(6,'48 MiB saved')])
captions('Hud752',[(i/4,f'{int(min(48,i*3)+min(48,i*2))} / 96 MiB') for i in range(25)])
animations.append('<DoubleAnimationUsingKeyFrames Storyboard.TargetName="Progress" Storyboard.TargetProperty="Width"><LinearDoubleKeyFrame KeyTime="0:0:0" Value="0"/><LinearDoubleKeyFrame KeyTime="0:0:4" Value="780"/><LinearDoubleKeyFrame KeyTime="0:0:6" Value="936"/></DoubleAnimationUsingKeyFrames>')
scene=f'''<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Width="1000" Height="600" Background="#101923">
<Canvas.Resources><Storyboard x:Key="Flow" Duration="0:0:9" RepeatBehavior="Forever">{''.join(animations)}</Storyboard></Canvas.Resources>
<Viewport3D Canvas.Top="90" Width="1000" Height="379" ClipToBounds="True">
<Viewport3D.Camera><OrthographicCamera Position="7,11.85,22" LookDirection="-7,-9.4,-22" UpDirection="0,1,0" Width="23"/></Viewport3D.Camera>
<ModelVisual3D><ModelVisual3D.Content><Model3DGroup>
<AmbientLight Color="#778390"/><DirectionalLight Color="#FFF3D9" Direction="-.3,-1,-.65"/><DirectionalLight Color="#83B4D7" Direction="-.8,-.45,.4"/>
{static}{''.join(models)}</Model3DGroup></ModelVisual3D.Content></ModelVisual3D></Viewport3D>{''.join(hud)}</Canvas>'''
(folder/'ServerRoom.xaml').write_text(scene,encoding='utf-8')
