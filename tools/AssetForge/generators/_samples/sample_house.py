"""Reference sample generator (lead): a Human Dawn-age house. Copy this structure for new generators."""
import os, sys
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))
from forge import MeshBuilder, style, publish, AssetMeta

GEN = 'sample_house'

def human_house(age: str, variant: int):
    m = MeshBuilder(seed=f'human_house_{age}_{variant}')
    wall = style.blended('human', age, 'wall'); roof = style.blended('human', age, 'roof', 0.6); trim = style.blended('human', age, 'trim')
    stone = style.age_mat(age)['stone']
    m.box((0, 0.06, 0), (0.92, 0.12, 0.78), stone)                               # plinth
    m.box((0, 0.42, 0), (0.8, 0.6, 0.66), wall, taper=0.03)                       # walls
    for x in (-0.38, 0.38):                                                       # timber corner posts
        for z in (-0.31, 0.31):
            m.box((x, 0.42, z), (0.06, 0.62, 0.06), trim)
    m.box((0, 0.73, -0.335), (0.82, 0.05, 0.03), trim)                            # beam
    m.gable_roof((0, 0.72, 0), 0.86, 0.7, 0.46, roof, overhang=0.1)               # chunky, tall roof
    m.door(-0.12, -0.33, 0.18, 0.34, arch=(age != 'dawn'))
    m.window(0.18, 0.38, -0.33, 0.16, 0.14, lit=True, frame=trim)
    m.flower_box(0.18, 0.33, -0.33, 0.2, seed=str(variant))
    m.chimney((0.24, 0.85, 0.12), 0.34, stone, lean=5 + variant * 3)
    m.barrel((-0.42, 0.0, -0.42))
    m.wobble(0.012)
    return m

if __name__ == '__main__':
    for age in ('dawn', 'medieval', 'industrial'):
        e = publish(human_house(age, 0), AssetMeta(id=f'sample_human_house_{age}', kind='house', category='_Samples', building_type='House', race='human', ages=[age], footprint=[1, 1]), GEN)
        print(e['id'], e['triangles'], 'tris', e['bytes'], 'bytes')
