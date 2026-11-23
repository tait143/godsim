from io import BytesIO
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import building_pictures as pics


class PictureAuditTests(unittest.TestCase):
    def test_metric_uses_rgb_mean_and_separate_luminance_edges(self):
        def screenshot(rect,colour):
            im=Image.new("RGB",(103,91),(205,190,165))
            ImageDraw.Draw(im).rectangle(rect,fill=colour)
            file=BytesIO()
            im.save(file,format="PNG")
            file.seek(0)
            return im,file
        images=[screenshot((17,23,74,69),(16,140,49)),screenshot((28,12,87,64),(135,50,79))]
        def reference(im):
            thumb=im.resize((64,64),Image.Resampling.LANCZOS)
            grey=np.array(thumb,dtype=float).mean(2)
            edge=np.array(thumb.convert("L").filter(ImageFilter.FIND_EDGES),dtype=float)
            return [(a-a.mean())/(a.std()+1e-6) for a in (grey,edge)]
        a,b=[reference(im) for im,_ in images]
        expected=round(sum(float((x*y).mean()) for x,y in zip(a,b))/2,3)
        self.assertEqual(pics.likeness(*(pics.feature(file) for _,file in images)),expected)

    def test_all_ages_compare_within_variant_and_wonders_are_excluded(self):
        rows=[dict(race="orc",building_type="House",kind="building",id=i,variant=0)
              for i in ("a","b","c")]
        rows += [dict(rows[0],id="d",variant=1),dict(rows[0],id="w",kind="wonder")]
        scores=pics.picture_pairs(rows,lambda e:(np.zeros((2,2)),np.zeros((2,2))))
        self.assertEqual({(a,b) for _,a,b in scores},{("a","b"),("b","c"),("a","c")})

    def test_threshold_uses_coordinator_rounding_and_rejects_exact_700(self):
        plane=np.ones((1,1))
        score=pics.likeness((plane,plane),(plane*.6996,plane*.6996))
        self.assertEqual(score,.700)
        entries=[dict(id=i,race="elf",building_type="Mill",variant=0) for i in ("a","b")]
        with patch.object(pics,"picture_pairs",return_value=[(score,"a","b")]), \
                patch.object(pics,"listed",return_value={("elf","Mill",0)}):
            with self.assertRaisesRegex(ValueError,"Listed family picture"):
                pics.check_catalog(entries,[])

    def test_decision_must_cover_the_exact_original_score_hash_and_page(self):
        row=dict(a="a",b="b",score=.755,sha256={"a":["mesh","image"]},page="01.png",row=1)
        base=dict(pairs=[row],sheet_sha256={"01.png":"hash"})
        vote=dict(row,decision="rework",reason="Same pitched hall and loading yard.")
        good=dict(base,critic="separate critic",pairs=[vote])
        self.assertEqual(pics.decisions_from_screening(base,good),{("a","b"):vote})
        for wrong in ([],[dict(vote,score=.754)],[dict(vote,page="other")],
                      [dict(vote,sha256={})],[dict(vote,reason="")],[vote,vote]):
            with self.assertRaises(ValueError):
                pics.decisions_from_screening(base,dict(good,pairs=wrong))
