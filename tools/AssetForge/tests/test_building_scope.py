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
    def test_comparison_label_keeps_retroactive_unlocks_in_chronological_intervals(self):
        from building_eras import age_span
        self.assertEqual(age_span(["industrial","modern","renaissance"]), "Renaissance - Modern")
        self.assertEqual(age_span(["renaissance","medieval"]), "Medieval - Renaissance")
        self.assertEqual(age_span(["classical","iron"]), "Iron - Classical")
        self.assertEqual(age_span(["medieval","dawn","iron"]), "Dawn, Iron, Medieval")

    def test_round_two_report_rejects_missing_models_and_inherited_or_stale_scores(self):
        from building_round2 import check_repair_reviews, require_inventory
        entries={"A":{"id":"A"},"B":{"id":"B"}}
        require_inventory(list(entries.values()),entries)
        for bad in ([entries["A"]], [entries["A"],entries["A"]], [entries["A"],{"id":"C"}]):
            with self.assertRaisesRegex(ValueError,"checkpoint ID set"):
                require_inventory(bad,entries)
        review=dict(id="A",scores={key:99 for key in CRITERIA},defects=[],observations="Viewed both sides.",
                    evidence_sha256="approved")
        with patch("building_review.evidence",return_value="approved"):
            self.assertEqual(check_repair_reviews({"A"},entries,[{"models":[review]}]),[review["scores"]])
            for rounds in ([], [{"models":[dict(review,evidence_sha256="old")]}],
                           [{"models":[review]},{"models":[dict(review,defects=["Open issue"])]}]):
                with self.assertRaisesRegex(ValueError,"missing current passing"):
                    check_repair_reviews({"A"},entries,rounds)

    def test_catalog_compares_same_era_ages_and_nonadjacent_models_within_a_variant(self):
        from building_eras import catalog_pairs
        rows = [dict(kind="townhall", race="dwarf", building_type="TownCenter", variant=0,
                     id=age, ages=[age]) for age in ("classical", "medieval", "renaissance")]
        other = [dict(rows[0], kind="wonder", id="wonder"), dict(rows[0], variant=1, id="variant")]
        pts = np.array([[[0.,0.,0.],[1.,0.,0.],[0.,1.,0.]]])
        with patch("building_eras.serialized_level", return_value=(pts,None)):
            actual = {(p["a"],p["b"]) for p in catalog_pairs(rows+other)}
        self.assertEqual(actual, {(a["id"],b["id"]) for a in rows for b in rows if a!=b})

    def test_moderate_overlap_needs_exact_current_visual_exception(self):
        from building_eras import reviewed_exceptions
        pair = dict(a="A", b="B", matched=601, total=1000)
        hashes = {"A":"alpha", "B":"beta"}
        item = dict(a="B", b="A", decision="keep", glb_sha256=hashes.copy(), reason="Distinct roof mass.",
                    shares={"b_to_a":dict(matched=601,total=1000)})
        triage = {"pairs":[item]}
        self.assertEqual(reviewed_exceptions([pair], hashes, triage), [dict(pair,reason=item["reason"])])
        self.assertEqual(reviewed_exceptions([dict(pair,matched=600)], {}, {"pairs":[]}), [])
        for variant in (dict(item,decision="rework"), dict(item,glb_sha256={"A":"stale","B":"beta"}),
                        dict(item,reason=""), dict(item,shares={"b_to_a":dict(matched=600,total=1000)})):
            with self.assertRaises(ValueError):
                reviewed_exceptions([pair], hashes, {"pairs":[variant]})
        with self.assertRaises(ValueError):
            reviewed_exceptions([dict(pair,matched=801)], hashes, triage)

    def test_baseline_triage_rejects_a_changed_comparison_image(self):
        import hashlib
        import json
        from building_eras import load_triage
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            (root/"triage").mkdir()
            page=root/"triage/elf-01.png"
            page.write_bytes(b"reviewed image")
            record={"reviewer":"critic","sheet_sha256":{page.name:hashlib.sha256(page.read_bytes()).hexdigest()},
                    "pairs":[{"a":"a","b":"b","decision":"keep","evidence":{"path":"triage/elf-01.png"}}]}
            (root/"triage.json").write_text(json.dumps(record))
            with patch("building_eras.ROOT", root):
                self.assertEqual(load_triage(root),record)
                page.write_bytes(b"different image")
                with self.assertRaisesRegex(ValueError, "checkpoint sheets"):
                    load_triage(root)

    def test_era_comparisons_count_repeated_corners_in_both_directions(self):
        from building_eras import ordered_counts
        rows = [{"id":"small"},{"id":"large"}]
        a = [[0,0,0],[1,0,0],[0,1,0]]
        b = [[0.0001,0,0],[1,0,0],[0,1,0],[0,0,0],[2,0,0],[0,2,0]]
        with patch("building_eras.serialized_level",side_effect=[(np.array(a),None),(np.array(b),None)]):
            self.assertEqual(ordered_counts(rows),[("small","large",3,3),("large","small",4,6)])

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
                with self.assertRaisesRegex(ValueError, "does not match"):
                    approved_family_sheets([{"id":"house"}])
                approval["era_sheets"] = {"dwarf-01.png":"grey"}
                path.write_text(json.dumps(approval))
                self.assertEqual(approved_family_sheets([{"id":"house"}]),
                                 ({"01.png":"hash"}, {"dwarf-01.png":"grey"}))

    def test_withdrawn_package_retires_only_numbered_contact_pages(self):
        with tempfile.TemporaryDirectory() as directory, patch("build_all.SHEETS",Path(directory)):
            package = Path(directory) / "package" / "elf_food"
            package.mkdir(parents=True)
            for name in ("01.png","02.png","round-2-lods.png"):
                (package/name).touch()
            grey = Path(directory) / "era_geometry"
            grey.mkdir()
            for name in ("elf-01.png", "elf-02.png", "rc4-elf-01.png", "rc4-elf-02.png", "manual-comparison.png"):
                (grey / name).touch()
            retire_obsolete_pages([package/"01.png",grey/"elf-01.png",grey/"rc4-elf-01.png"],["elf_food"])
            self.assertEqual(sorted(p.name for p in package.iterdir()),["01.png","round-2-lods.png"])
            self.assertEqual(sorted(p.name for p in grey.iterdir()),["elf-01.png","manual-comparison.png","rc4-elf-01.png"])
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
