"""Rebuild the lossless compact atlas from the original community PNGs.

Run from the full source checkout after changing the selected source frames.
The installation ZIP already contains this atlas and needs no raw community PNGs.
"""
from pathlib import Path
import json
import hashlib
import build_cursors as builder
from PIL import Image


def main():
    repo = Path(__file__).resolve().parents[1]
    # Explicitly bypass the cached compact atlas to use the retained originals.
    builder.COMPACT_ACTIONS = False
    builder.ACTION_CACHE.clear()
    atlas = Image.new('RGBA', (3072, 2304))
    groups = {}
    for row, action in enumerate(builder.ACTION_SPECS):
        frames = builder.action_group(action)
        assert len(frames) <= 12
        assert all(im.size == frames[0].size for im in frames)
        assert frames[0].width <= 256 and frames[0].height <= 256
        for i, im in enumerate(frames):
            atlas.paste(im, (i * 256, row * 256))
        groups[action] = dict(row=row, count=len(frames),
                              width=frames[0].width, height=frames[0].height)
    path = repo / 'source/characters/deepseek-actions.png'
    atlas.save(path)
    metadata = dict(cell=256, groups=groups,
                    normalization='Exact RGBA pixels of original atlas extraction, shared crop/scale and breathing registration. Original community PNGs preserved in repository and formal archive.')
    (repo / 'source/compact-actions.json').write_text(
        json.dumps(metadata, indent=2), encoding='utf-8')
    print(json.dumps(dict(groups=len(groups), bytes=path.stat().st_size,
                         sha256=hashlib.sha256(path.read_bytes()).hexdigest())))


if __name__ == '__main__':
    main()
