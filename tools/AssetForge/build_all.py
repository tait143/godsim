#!/usr/bin/env python3
"""Runs every generator under tools/AssetForge/generators (except _samples) and merges all manifest fragments
into Assets/GodSim/Art/Models/manifest.json. Also writes a contact sheet per category into previews/_sheets/.

Usage: python3 tools/AssetForge/build_all.py [--only <substring>] [--no-run] [--jobs N]
"""
import argparse
import glob
import json
import os
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from forge.registry import MANIFESTS, MODELS, PREVIEWS, ROOT  # noqa: E402
from forge.render import contact_sheet  # noqa: E402


def run(script):
    r = subprocess.run([sys.executable, script], capture_output=True, text=True, timeout=900)
    return script, r.returncode, (r.stdout[-400:] + r.stderr[-1200:])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only', default='')
    ap.add_argument('--no-run', action='store_true')
    ap.add_argument('--jobs', type=int, default=4)
    a = ap.parse_args()
    scripts = sorted(p for p in glob.glob(os.path.join(HERE, 'generators', '**', '*.py'), recursive=True)
                     if '_samples' not in p and os.path.basename(p) != '__init__.py' and a.only in p)
    failures = []
    if not a.no_run:
        with ThreadPoolExecutor(max_workers=max(1, a.jobs)) as ex:
            for script, code, out in ex.map(run, scripts):
                status = 'ok' if code == 0 else 'FAILED'
                print(f'[{status}] {os.path.relpath(script, ROOT)}')
                if code != 0:
                    failures.append((script, out))
    entries = []
    for frag in sorted(glob.glob(os.path.join(MANIFESTS, '*.json'))):
        try:
            with open(frag) as f:
                entries.extend(json.load(f))
        except Exception as e:  # corrupt fragment
            failures.append((frag, str(e)))
    seen, unique, dupes = set(), [], []
    for e in sorted(entries, key=lambda e: e['id']):
        if e['id'] in seen:
            dupes.append(e['id'])
            continue
        seen.add(e['id'])
        if os.path.exists(os.path.join(ROOT, e['path'])):
            unique.append(e)
    manifest = dict(schema='godsim.art/1', count=len(unique), assets=unique)
    with open(os.path.join(MODELS, 'manifest.json'), 'w') as f:
        json.dump(manifest, f, indent=1)
    # contact sheets per category
    os.makedirs(os.path.join(PREVIEWS, '_sheets'), exist_ok=True)
    cats = {}
    for e in unique:
        if e.get('preview'):
            cats.setdefault(e['category'], []).append(e)
    for cat, es in cats.items():
        contact_sheet([os.path.join(ROOT, e['preview']) for e in es], os.path.join(PREVIEWS, '_sheets', cat.replace('/', '_') + '.png'),
                      cols=6, cell=200, labels=[e['id'] for e in es])
    print(f'manifest: {len(unique)} assets, {len(dupes)} duplicate ids, {len(failures)} failures')
    for s, out in failures:
        print('FAIL', s, out[-600:])
    return 1 if failures or dupes else 0


if __name__ == '__main__':
    sys.exit(main())
