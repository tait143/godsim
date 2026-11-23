"""Compound and vertical clan houses for the Bronze, Iron and Medieval ages."""
from .hearth_masonry import block, dome, flank, gable, gallery, pane, profile, roundwork, steps, terrace


def hearth_door(m,x,z,y,w,h):
    m.recessed_gate(x,z-.006,y,w,h,'trim')
    steps(m,x,z-.018,w*1.23,y,.11)
    if m.lod<2:
        m.lamp_face(x+w*.79,y+h*.65,z-.013,.030,.061)


def bronze(m,v):
    if v:
        # An open copper-roofed porch fronts two round sleeping chambers.
        for x,z,r,top in ((-.192,.147,.230,.32),(.192,.059,.181,.224)):
            roundwork(m,'fieldstone',x,z,r,0,.064,r*.93,segments=(8,6,4))
            roundwork(m,'wall',x,z,r*.94,.063,top,r*.98,segments=(8,6,4))
            roundwork(m,'accent',x,z,r*1.12,top,top+.044,r*1.06)
            m.dome((x,top+.044,z),r*1.01,m.color('roof'),rings=(3,2,1)[m.lod],
                   segments=(8,6,4)[m.lod],squash=.85,material='roof')
        terrace(m,'wall',-.091,-.105,.37,.21,0,.252)
        hearth_door(m,-.095,-.210,.048,.132,.180)
        gallery(m,'timber',-.094,-.243,.412,.213,.039,.202,2)
        gable(m,'accent',-.094,-.243,.450,.236,.284,.104)
        if m.lod==0:
            m.pot(.217,-.156,.048,.094)
            m.flue_pot(-.252,.217,.472,.148,.066,'accent')
    else:
        # A low L shelters a stone-paved work court, with an enclosed roof granary.
        terrace(m,'fieldstone',-.204,.022,.421,.71,0,.063)
        terrace(m,'wall',-.204,.022,.391,.678,.062,.319)
        terrace(m,'accent',-.204,.022,.431,.711,.318,.362,taper=.975)
        terrace(m,'wall',-.225,.174,.292,.282,.36,.562,taper=.85)
        terrace(m,'roof',-.225,.174,.322,.311,.562,.647,taper=.59)
        terrace(m,'brick',.143,.224,.312,.269,0,.234)
        terrace(m,'roof',.143,.224,.353,.312,.233,.286,taper=.92)
        terrace(m,'fieldstone',.108,-.119,.284,.378,0,.044)
        hearth_door(m,-.202,-.317,.059,.138,.207)
        pane(m,.153,.089,.093,.161,.087)
        flank(m,-.009,-.057,.131,.192,.108)
        if m.lod==0:
            m.pot(.14,-.191,.054,.102)
            m.flue_pot(-.247,.04,.345,.196,.069,'accent')


def iron(m,v):
    if v:
        # Iron-strapped longhouse. Offset aisle and a deep entrance beneath the gable.
        terrace(m,'fieldstone',-.164,.045,.422,.702,0,.097)
        terrace(m,'wall',-.164,.045,.389,.668,.096,.265)
        profile(m,'slate',-.164,.038,.498,.730,.265,
                [(-.5,.031),(-.18,.334),(0,.412),(.5,.033)],'timber')
        terrace(m,'fieldstone',.162,.189,.257,.344,0,.215)
        profile(m,'roof',.162,.189,.306,.385,.215,[(-.5,.147),(.5,.025)],'wall')
        hearth_door(m,-.164,-.292,.092,.163,.221)
        pane(m,.175,.017,.087,.114,.092)
        flank(m,.291,.197,.104,.159,.080)
        if m.lod<2:
            m.bent_rail('trim',[(-.403,.284),(-.165,.674),(.073,.285)],-.328,.030)
        m.flue_pot(-.255,.206,.516,.211,.078,'metal')
        if m.lod==0:
            m.pot(.254,-.059,.047,.091)
    else:
        # Corbelled beehive dwelling, a smoke hole at the crown and a square storm lobby.
        x,z,r=-.056,.091,.331
        roundwork(m,'fieldstone',x,z,r,0,.095,r*1.02)
        roundwork(m,'wall',x,z,r*.987,.094,.281,r*.91)
        for low,high,outer,inner,role in ((.28,.397,.323,.273,'slate'),
                  (.397,.506,.282,.216,'fieldstone'),(.506,.596,.224,.146,'slate'),
                  (.596,.666,.151,.069,'fieldstone')):
            roundwork(m,role,x,z,outer,low,high,inner)
        roundwork(m,'metal',x,z,.075,.666,.692,.064)
        if m.lod<2:
            roundwork(m,'door',x,z,.049,.691,.696,.046)
        terrace(m,'wall',-.056,-.212,.289,.222,0,.266)
        profile(m,'slate',-.056,-.211,.338,.262,.265,[(-.5,.027),(0,.11),(.5,.027)],'wall')
        hearth_door(m,-.056,-.324,.055,.153,.204)
        with m.transform(translate=(.227,.097,-.038),rotate_y=-61):
            pane(m,0,.009,0,.107,.096)
        if m.lod==0:
            m.pot(.179,-.234,.060,.108)
            m.pot(.255,-.185,.043,.077)


def medieval(m,v):
    if v:
        # Two households share a fireproof party wall; the upper floor is a full-width loft.
        # The split roof and three-tier front parapet follow the two separate entrances.
        terrace(m,'fieldstone',.002,.055,.706,.481,0,.118)
        terrace(m,'wall',.002,.055,.665,.451,.116,.355)
        terrace(m,'timber',.002,.052,.718,.506,.354,.389)
        terrace(m,'wall',.002,.059,.681,.453,.388,.529)
        for side in (-1,1):
            center=.002+side*.170
            profile(m,'slate',center,.067,.341,.489,.529,
                    [(-.5,.128),(.5,.036)] if side>0 else [(-.5,.036),(.5,.128)],'fieldstone')
            hearth_door(m,center,-.174,.067,.135,.216)
            pane(m,center,-.168,.420,.157,.082)
            if m.lod==0:
                m.lamp_face(center,.493,-.184,.022,.043)
        # Rising capstones dress the front firewall. The side faces stop at each tread.
        for z in (-.191,.307):
            shoulder=.648 if m.lod==2 else .609
            block(m,'fieldstone',.002,z,.711,.051,.528,shoulder)
            if m.lod<2:
                block(m,'fieldstone',.002,z,.468,.051,shoulder,.682)
            block(m,'fieldstone',.002,z,.242,.051,.648 if m.lod==2 else .682,.749)
        m.flue_pot(.022,.176,.638,.250,.109,'slate')
        if m.lod==0:
            m.pot(-.287,-.235,.042,.081)
    else:
        # The fortified stair house rises beside a single-storey hearth wing.
        terrace(m,'fieldstone',-.209,.076,.388,.486,0,.549,taper=.93)
        terrace(m,'trim',-.209,.076,.417,.502,.548,.599)
        terrace(m,'wall',-.209,.076,.349,.428,.599,.712)
        m.hip_roof((-.209,.712,.076),.454,.535,.246,m.color('slate'),overhang=0,material='slate')
        terrace(m,'wall',.132,-.052,.378,.432,0,.271)
        gable(m,'roof',.132,-.052,.426,.489,.271,.151)
        hearth_door(m,.132,-.270,.054,.152,.213)
        pane(m,-.209,-.161,.378,.132,.122)
        flank(m,.324,-.042,.116,.147,.094)
        if m.lod<2:
            pane(m,-.209,-.140,.623,.172,.063)
        m.flue_pot(.239,.045,.344,.207,.076,'slate')
        if m.lod==0:
            m.pot(-.307,-.232,.056,.111)
