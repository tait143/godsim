"""Three Human cottages showing atlas rows and example authored LODs."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from forge import MeshBuilder, style, publish, AssetMeta

GEN = 'sample_house'


def human_house(age, variant, lod=0):
    m = MeshBuilder(seed=f'human_house_{age}_{variant}')
    wall, roof, trim = (style.blended('human', age, key) for key in ('wall','roof','trim'))
    stone = style.age_mat(age)['stone']
    m.box((0,.045,0),(.86,.09,.76),stone,material='fieldstone')
    m.box((0,.34,0),(.72,.52,.61),wall,material='wall')
    m.gable_roof((0,.594,0),.78,.68,.37,roof,overhang=.045,material='roof')
    # A roof cap and entry make the distant silhouette recognisable.
    with m.transform(translate=(.19,.75,.095),rotate_z=4):
        flue = 'timber' if age=='dawn' else 'brick'
        m.box((0,.115,0),(.11,.23,.12),stone,material=flue)
        if lod <= 1:
            m.box((0,.237,0),(.14,.025,.15),stone,material=flue)
    with m.transform(translate=(0,.09,0)):
        m.door(-.105,-.306,.145,.275,arch=age!='dawn')
    if lod <= 1:
        for x in (-.346,.346):
            for z in (-.291,.291):
                m.box((x,.34,z),(.044,.53,.045),trim,material='trim')
        m.box((0,.545,-.31),(.72,.035,.027),trim,material='trim')
        m.window(.145,.30,-.308,.135,.142,lit=True,frame=trim)
        if lod == 1:
            m.box((.145,.27,-.343),(.19,.048,.071),trim,material='timber')
    if lod == 0:
        m.flower_box(.145,.27,-.308,.19,seed=str(variant))
        m.barrel((-.335,0,-.391),r=.056,h=.119)
        m.box((-.105,.052,-.354),(.19,.053,.122),stone,material='fieldstone')
        for x in (.052,.238):
            m.box((x,.365,-.325),(.039,.16,.025),trim,material='banner')
    return m


if __name__=='__main__':
    for age in ('dawn','medieval','industrial'):
        meta = AssetMeta(id=f'sample_human_house_{age}',kind='house',category='_Samples',
                         building_type='House',race='human',ages=[age],footprint=[1,1])
        levels = [human_house(age,0,lod) for lod in range(3)]
        result = publish(levels[0],meta,GEN,lods=levels[1:],preview_size=384)
        print(result['id'],result['lod_triangles'],result['bytes'],'bytes')
