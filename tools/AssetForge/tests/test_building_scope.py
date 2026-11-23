"""Protect full catalog coverage, race restrictions and the independent art review gate."""
from collections import defaultdict
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_all import bindings, file_stamp, require_regenerated, retire_obsolete_pages
from building_review import CRITERIA, approved_family_sheets, director_holds, passing
from building_scope import AGES, RACES, early_unlocks, specifications, work_list
from building_surfaces import inverted_shells, overlaps, outward_ray_hits_surface, shape_digest


class BuildingScopeTests(unittest.TestCase):
    def test_director_gate_only_withholds_open_findings_for_known_models(self):
        import json
        from types import SimpleNamespace

        models = [SimpleNamespace(id=key) for key in ("early", "late")]
        with tempfile.TemporaryDirectory() as directory, patch("building_review.REVIEWS", Path(directory)):
            path = Path(directory) / "art_direction_holds.json"
            with self.assertRaisesRegex(ValueError, "Missing art direction"):
                director_holds(models)
            findings = [dict(status="open", models=["early"]), dict(status="closed", models=["late"])]
            path.write_text(json.dumps(findings))
            self.assertEqual(director_holds(models), {"early"})
            findings[0]["models"] = ["unknown"]
            path.write_text(json.dumps(findings))
            with self.assertRaises(ValueError):
                director_holds(models)

    def test_complete_publication_requires_matching_catalog_approval(self):
        import json
        with tempfile.TemporaryDirectory() as directory, patch("building_review.REVIEWS", Path(directory)):
            with self.assertRaisesRegex(ValueError, "Missing independent"):
                approved_family_sheets([])
            approval = dict(schema="godsim.art-direction/1", reviewer="independent",
                            model_count=1, family_sheets={"01.png":"hash"}, evidence_sha256="stale")
            path = Path(directory) / "art_direction_approval.json"
            path.write_text(json.dumps(approval))
            with patch("building_review.catalog_evidence", return_value="current"):
                with self.assertRaisesRegex(ValueError, "does not match"):
                    approved_family_sheets([{"id":"house"}])
                approval["evidence_sha256"] = "current"
                path.write_text(json.dumps(approval))
                self.assertEqual(approved_family_sheets([{"id":"house"}]), {"01.png":"hash"})

    def test_withdrawn_package_retires_only_numbered_contact_pages(self):
        with tempfile.TemporaryDirectory() as directory, patch("build_all.SHEETS",Path(directory)):
            package = Path(directory) / "package" / "elf_food"
            package.mkdir(parents=True)
            for name in ("01.png","02.png","round-2-lods.png"):
                (package/name).touch()
            retire_obsolete_pages([package/"01.png"],["elf_food"])
            self.assertEqual(sorted(p.name for p in package.iterdir()),["01.png","round-2-lods.png"])
            retire_obsolete_pages([],["elf_food"])
            self.assertEqual([p.name for p in package.iterdir()],["round-2-lods.png"])

    def test_signed_volume_rejects_inward_closed_shells_and_leaves_open_faces_to_the_critic(self):
        corners = np.array([(0.,0,0),(1,0,0),(0,1,0),(0,0,1)])
        faces = corners[np.array([(0,2,1),(0,1,3),(0,3,2),(1,2,3)])]
        self.assertEqual(list(inverted_shells(faces)), [])
        self.assertEqual(len(list(inverted_shells(faces[:,::-1]))), 1)
        self.assertEqual(list(inverted_shells(faces[1:,::-1])), [])

    def test_shape_comparison_ignores_reordering_but_detects_new_massing(self):
        panel = np.array([[(0.,0,0),(1,0,0),(0,1,0)], [(1,0,0),(1,1,0),(0,1,0)]])
        self.assertEqual(shape_digest(panel), shape_digest(panel[::-1,::-1]))
        changed = panel.copy()
        changed[1,1,2] = .01
        self.assertNotEqual(shape_digest(panel), shape_digest(changed))

    def test_surface_inspection_distinguishes_contacts_and_exposed_intersections(self):
        face = np.array([(0.,0,0),(1,0,0),(0,1,0)])
        self.assertFalse(overlaps([face, face[::-1]]))
        self.assertFalse(overlaps([face, face + (1,0,0)]))
        surfaces = np.array([face, face + (.2,0,0)])
        area, _, _, point = overlaps(surfaces)[0]
        self.assertGreater(area, .1)
        self.assertFalse(outward_ray_hits_surface(point, np.array([0.,0,1]), surfaces))
        self.assertTrue(outward_ray_hits_surface(point, np.array([0.,0,1]), np.array([face + (0,0,.2)])))

    def test_rebuild_rejects_untouched_outputs_even_when_the_old_bytes_match(self):
        with tempfile.TemporaryDirectory() as directory:
            old = Path(directory) / "old.glb"
            absent = Path(directory) / "unwritten.png"
            old.write_bytes(b"old mesh")
            before = {p: file_stamp(p) for p in (old, absent)}
            with self.assertRaisesRegex(ValueError, "old.glb"):
                require_regenerated(before)
            replacement = Path(directory) / "generated.glb"
            replacement.write_bytes(b"old mesh")
            replacement.replace(old)
            with self.assertRaisesRegex(ValueError, "unwritten.png"):
                require_regenerated(before)
            absent.write_bytes(b"preview")
            require_regenerated(before)

    def test_source_bindings_are_neither_lost_nor_invented(self):
        rows = work_list()
        expected = defaultdict(set)
        for row in rows:
            for race in RACES if row["race"] == "neutral" else (row["race"],):
                for age in row["ages"]:
                    expected[row["buildingType"], race, age].add(row["variant"])
        for key in early_unlocks():
            expected[key].add(0)
        self.assertIn(("SunPyramid", "saurian", "bronze"), expected)
        self.assertIn(("Mine", "dwarf", "dawn"), expected)
        self.assertNotIn(("Mine", "human", "dawn"), expected)
        models = specifications()
        self.assertEqual(len({m.id for m in models}), len(models))
        self.assertEqual({row["id"] for row in rows}, {s for m in models for s in m.source_ids})
        entries = [vars(model) for model in models]
        by_id = {model.id: model for model in models}
        actual = {(b["buildingType"], b["race"], b["age"]):
                  {by_id[i].variant for i in b["modelIds"]} for b in bindings(entries)}
        self.assertEqual(actual, expected)
        for m in models:
            for row in (r for r in rows if r["id"] in m.source_ids):
                self.assertEqual(tuple(row["footprint"]), m.footprint)

    def test_hero_variants_and_wonder_identity(self):
        models = specifications()
        for race in RACES:
            for age in AGES:
                for name, count in (("House", 2), ("TownCenter", 1)):
                    matching = [m for m in models if m.race == race and m.building_type == name and age in m.ages]
                    self.assertEqual(len(matching), count)
                    self.assertTrue(all(m.ages == (age,) for m in matching))
        wonders = [m for m in models if m.kind == "wonder"]
        self.assertEqual(len(wonders), len({m.building_type for m in wonders}))

    def test_review_rejects_stale_evidence_low_scores_and_open_findings(self):
        review = {"scores": dict.fromkeys(CRITERIA, 100), "observations": "Viewed at phone size",
                  "defects": [], "evidence_sha256": "current"}
        with patch("building_review.evidence", return_value="current"):
            self.assertTrue(passing({}, review))
            self.assertFalse(passing({}, dict(review, evidence_sha256="older")))
            self.assertFalse(passing({}, dict(review, defects=["Front post is floating"])))
            for key, low in (("charm", 89), ("technical", 98)):
                self.assertFalse(passing({}, dict(review, scores={**review["scores"], key: low})))


if __name__ == "__main__":
    unittest.main()
