"""Wattle compounds, iron-bound lodges and civic halls of the Orc clans."""
from math import cos, pi, sin
from .hearth_masonry import block, dome, flank, gable, gallery, pane, profile, roundwork, steps, terrace


def house_entry(m,x,z,y,w,h):
    m.doorway(x,z-.028,y,h,w,horns=False)
    steps(m,x,z-.031,w*1.10,y,.095)
    if m.lod<2:
        m.horn(x+w*.43,y+h*.84,z-.030,1,.51)


def bronze_house(m):
    if m.v:
        # Low walled family court. The copper-clad sleeping loft faces a sheltered work bench.
        terrace(m,'brick',.173,.070,.359,.578,0,.081,taper=.95)
        terrace(m,'wall',.173,.070,.327,.538,.080,.358)
        terrace(m,'accent',.173,.07,.376,.588,.358,.390)
        terrace(m,'brick',.171,.210,.268,.235,.390,.553,taper=.79)
        gable(m,'hide',.171,.209,.281,.254,.552,.089)
        terrace(m,'brick',-.159,.208,.301,.275,0,.239)
        gable(m,'thatch',-.159,.208,.349,.322,.239,.096)
        terrace(m,'soil',-.159,-.060,.286,.382,0,.046)
        block(m,'timber',-.294,-.062,.038,.377,.045,.138)
        house_entry(m,.172,-.200,.054,.180,.231)
        pane(m,-.159,.069,.075,.129,.103,sill='timber',light='door')
        if m.lod==0:
            m.drum(-.144,-.134,.046,.083,.046)
            flank(m,.337,.071,.138,.180,.102)
    else:
        # An earthen roundhouse under a hide cap, and an attached low, timber-roofed cook room.
        x,z=-.117,.097
        roundwork(m,'brick',x,z,.267,0,.155,.278,segments=(10,7,5))
        roundwork(m,'wall',x,z,.274,.155,.341,.248,segments=(10,7,5))
        # A high smoke opening on the leeward edge vents the low reed roof.
        terrace(m,'hide',x,z,.538,.496,.337,.456,taper=.53,shift=(-.091,.047))
        roundwork(m,'brick',x-.091,z+.047,.104,.449,.586,.077,segments=(8,5,4))
        roundwork(m,'trim',x-.091,z+.047,.087,.586,.619,.081,segments=(8,5,4))
        terrace(m,'brick',.164,-.070,.281,.347,0,.244)
        profile(m,'thatch',.164,-.071,.335,.399,.243,[(-.5,.123),(.5,.028)],'timber')
        house_entry(m,.161,-.245,.039,.162,.171)
        if m.lod<2:
            m.horn(-.278,.314,-.075,-1,.61)
        if m.lod==0:
            m.drum(-.124,-.192,.054,.093)
            flank(m,.307,-.061,.075,.128,.087)


def iron_house(m):
    if m.v:
        # A half-timbered watch loft over a stone hearth, linked to a turf-roofed store.
        terrace(m,'fieldstone',-.184,.061,.339,.404,0,.344,taper=.94)
        terrace(m,'timber',-.184,.061,.428,.469,.343,.394)
        terrace(m,'wall',-.184,.061,.396,.435,.394,.625)
        profile(m,'slate',-.184,.061,.455,.503,.625,[(-.5,.032),(.03,.208),(.5,.037)],'timber')
        terrace(m,'fieldstone',.142,-.067,.285,.393,0,.249)
        profile(m,'roof',.142,-.067,.335,.444,.248,[(-.5,.126),(.5,.024)],'timber')
        house_entry(m,.142,-.265,.039,.157,.171)
        pane(m,-.184,-.157,.438,.190,.121,sill='timber')
        if m.lod<2:
            m.horn(-.184,.816,-.187,-1,.42,'ivory','metal')
        if m.lod==0:
            m.drum(-.202,-.215,.053,.092)
            flank(m,.015,.065,.451,.144,.093,sill='timber')
    else:
        # The iron clan hall uses pegged end frames to lift both ends of its bark roof.
        # The ridge dips over the hearth. Broad hide gores alternate with the bark on the slopes.
        terrace(m,'fieldstone',0,.087,.738,.392,0,.091)
        terrace(m,'wall',0,.087,.698,.354,.088,.286)
        xs=(-.423,-.253,.071,.402); peaks=(.723,.514,.488,.679)
        rows=[]
        for x,peak in zip(xs,peaks):
            rows.append([(x,.277,-.136),(x,peak,.086),(x,.277,.317)])
        for i,(left,right) in enumerate(zip(rows,rows[1:])):
            for j,role in enumerate(('roof','slate')):
                a,b,c,d=left[j],right[j],right[j+1],left[j+1]
                skin='cloth' if i==0 and j==0 else role
                m.face(skin,(d,c,b))
                m.face(skin,(d,b,a))
        for row,side in ((rows[0],-1),(rows[-1],1)):
            m.face('timber',row[::-1] if side<0 else row)
        m.face('timber',((-.423,.277,-.136),(.402,.277,-.136),
                         (.402,.277,.317),(-.423,.277,.317)))
        terrace(m,'fieldstone',.028,-.174,.268,.201,0,.218)
        gable(m,'roof',.028,-.174,.313,.251,.218,.115)
        house_entry(m,.028,-.277,.043,.171,.171)
        if m.lod<2:
            for x,peak in ((-.420,.723),(.402,.679)):
                m.horn(x*.93,peak-.059,.086,1 if x>0 else -1,.66,'ivory','metal')
            pane(m,.232,-.091,.104,.119,.086,sill='timber')
        if m.lod==0:
            m.drum(-.202,-.140,.052,.093)
            for x in (-.259,.238):
                m.pole('metal',(x,.098,-.094),(x,.274,-.113),.021,.016,5)


def dawn_moot(m):
    # A stretched hide canopy shelters the elders' circle. The front and flanks stay open.
    terrace(m,'soil',-.002,.013,1.55,1.27,0,.077)
    for z in (-.382,.422):
        for side in (-1,1):
            m.post('timber',(side*.648,.075,z),(side*-.078,1.179,z+.039),
                   .051,.032,sides=(8,5,4)[m.lod])
    m.post('timber',(0,1.056,-.439),(0,1.056,.517),.060,.055,sides=(9,6,4)[m.lod])
    for side in (-1,1):
        low=[(side*.591,.277,-.386),(side*.589,.301,.451)]
        crest=[(side*.032,1.065,-.366),(side*.031,1.076,.428)]
        up=[(x,y+.020,z) for x,y,z in (low[0],low[1],crest[1],crest[0])]
        down=[(x,y-.014,z) for x,y,z in up]
        if side>0:
            up.reverse(); down.reverse()
        m.panel('hide' if side>0 else 'cloth',up)
        m.panel('hide',down[::-1])
        for i,a in enumerate(up):
            j=(i+1)%4
            m.panel('hide',(up[j],a,down[i],down[j]))
    for s in (-1,1):
        terrace(m,'timber',s*.374,.025,.113,.597,.078,.192)
        m.tusk(s*.478,.077,-.399,s,.49)
    terrace(m,'timber',0,.280,.486,.199,.078,.201)
    m.drum(.013,.078,-.271,.157,.225)
    m.flag(0,1.075,.163,.292,.272)
    steps(m,0,-.622,.520,.077,.193,'timber')
    if m.lod==0:
        m.drum(.546,.079,-.128,.089,.148)


def iron_council(m):
    # Twin log watch posts enclose a raised cross-hall. Exposed ground passage leads to its public drum.
    for x,z,r,h in ((-.513,.159,.269,.943),(.495,-.060,.250,.699)):
        m.shell(x,z,[(0,r,'fieldstone'),(.145,r*.93,'timber'),(h-.16,r*.78,'metal'),
                     (h-.109,r*.86,'wall'),(h,r*.84,'trim')],(10,7,5)[m.lod],flatten=1.16)
        roundwork(m,'roof',x,z,r*1.08,h,h+.269,.034,segments=(10,7,5),depth=1.13)
        if m.lod<2:
            m.tusk(x,h+.242,z,1,.454,'ivory')
    terrace(m,'wall',-.004,.178,.893,.438,.419,.726)
    gable(m,'slate',-.004,.178,.975,.509,.725,.218)
    terrace(m,'fieldstone',-.023,-.342,.770,.542,0,.086)
    terrace(m,'timber',-.039,.242,.377,.289,.082,.417)
    m.entry(-.038,.089,.083,.265,.253,0)
    m.drum(.256,.086,-.428,.126,.247)
    m.flag(-.512,1.144,.134,.336,.302)
    if m.lod<2:
        for x in (-.260,.221):
            pane(m,x,-.044,.51,.164,.115,sill='metal',light='door')
        m.tusk(-.309,.086,-.504,-1,.558)
    if m.lod==0:
        m.drum(-.555,0,-.230,.099,.176)
        m.post('banner',(-.512,.47,-.135),(-.512,.65,-.134),.045)


def renaissance_council(m):
    # A civic loggia and a domed debating apsis face the plaza. A signal balcony announces assemblies.
    terrace(m,'fieldstone',-.015,.011,1.667,1.325,0,.106)
    terrace(m,'plaster',.120,.270,1.125,.570,.102,.629)
    gable(m,'roof_tile',.120,.270,1.197,.635,.628,.206)
    gallery(m,'ivory',.116,-.180,1.212,.445,.106,.441,4)
    gable(m,'roof_tile',.116,-.181,1.284,.482,.589,.150)
    m.entry(.114,-.020,.107,.405,.356,1)
    steps(m,.111,-.653,.706,.106,.241)
    x,z=-.555,.028
    roundwork(m,'wall',x,z,.241,.107,.609,.219,segments=(10,8,5))
    roundwork(m,'accent',x,z,.262,.609,.676,.253,segments=(10,8,5))
    roundwork(m,'plaster',x,z,.216,.676,.983,.210,segments=(10,8,5))
    dome(m,'roof_tile',x,z,.982,.243,.228)
    m.flag(x,1.199,z,.293,.268)
    m.drum(.603,.106,-.419,.112,.208)
    if m.lod<2:
        for s in (-1,1):
            m.tusk(.116+s*.438,.106,-.438,s,.41)
        pane(m,x,-.178,.738,.124,.125,sill='trim',light='door')
    if m.lod==0:
        m.drum(-.31,.107,-.480,.091,.156)
        flank(m,.685,.264,.274,.292,.166)


COUNCILS={0:dawn_moot,2:iron_council,5:renaissance_council}
