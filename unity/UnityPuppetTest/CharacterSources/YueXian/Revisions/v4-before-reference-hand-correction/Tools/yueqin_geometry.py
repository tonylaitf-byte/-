def disc(name,r,front_y,depth,material,segments=128):
    v=[(0,front_y,0),(0,front_y+depth,0)]
    for y in [front_y,front_y+depth]:
        v.extend((r*math.cos(i*math.tau/segments),y,r*math.sin(i*math.tau/segments)) for i in range(segments))
    f=[]
    for i in range(segments):
        a=2+i;b=2+(i+1)%segments;c=a+segments;d=b+segments
        f.extend([(0,b,a),(1,c,d),(a,b,d,c)])
    add(name,v,f,material,{},True)
disc('Yueqin_Round_Rosewood_Frame',.207,-.031,.056,'rim')
disc('Yueqin_Round_Soundboard',.193,-.033,.008,'wood')
for radius,y,thick,material in [(.196,-.036,.0023,'fret'),(.185,-.035,.001,'fret'),(.207,-.027,.003,'rim')]:
    tube('Yueqin_Circular_Binding',[(radius*math.cos(i*math.tau/128),y,radius*math.sin(i*math.tau/128)) for i in range(129)],thick,material,{},True)
# Subtle soundboard grain is an engraved family of fine parallel curves.
for i in range(-10,11):
    x=i*.016;h=math.sqrt(max(0,.18*.18-x*x))
    tube('Yueqin_Soundboard_Grain',[(x+.001*math.sin(j*.8+i),-.0341,-h+2*h*j/32) for j in range(33)],.00032,'fret',{},True,sides=4)
prism('Yueqin_Short_Neck',[(-.026,.158),(.026,.158),(.020,.509),(-.020,.509)],-.005,.038,'rim',{},True)
prism('Yueqin_Fingerboard',[(-.022,.17),(.022,.17),(.017,.521),(-.017,.521)],-.013,.006,'rim',{},True)
prism('Yueqin_PegHead',[(-.02,.49),(.02,.49),(.029,.563),(.017,.582),(-.017,.582),(-.029,.563)],-.006,.030,'rim',{},True)
for z in [.542,.572]:
    for side in [-1,1]:
        tube('Yueqin_Four_Tuning_Pegs',[(side*.017,.006,z),(side*.055,.006,z)],.0053,'rim',{},True)
        ellipse('Yueqin_Four_Tuning_Pegs',(side*.055,.006,z),(.008,.009,.012),'fret',{},True,segments=20,rings=12)
for z in [.018,.061,.108,.156,.218,.281,.336,.387,.431,.467]:
    w=.044 if z<.18 else .024
    tube('Yueqin_Raised_Frets',[(-w,-.039 if z<.18 else -.020,z),(w,-.039 if z<.18 else -.020,z)],.0027,'fret',{},True)
prism('Yueqin_Bridge',[(-.040,-.118),(.040,-.118),(.035,-.097),(-.035,-.097)],-.045,.015,'rim',{},True)
prism('Yueqin_Tailpiece',[(-.022,-.169),(.022,-.169),(.018,-.146),(-.018,-.146)],-.037,.01,'fret',{},True)
for x in [-.014,-.009,.009,.014]:
    tube('Yueqin_Four_Strings',[(x,-.041,-.159),(x,-.053,-.107),(x,-.044,.156),(x,-.026,.501),(x,-.026,.533)],.00075,'strings',{},True,sides=8)
crescent('Yueqin_HalfMoon_Inlay',(-.102,-.0355,-.015),.038,'fret',{},True,depth=.0008)
for i in range(12):
    dx=(i-5.5)*.0017;tube('Yueqin_Short_Tassel',[(dx*.4,0,-.204),(dx*.8,-.005,-.240),(.011+dx,-.01,-.283)],.0012,'ink',{},True,sides=6)
ellipse('Yueqin_Tassel_JadeBead',(0,-.002,-.229),(.007,.006,.011),'jade',{},True,segments=20,rings=12)

