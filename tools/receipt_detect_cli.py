import argparse
import json
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
from services.ocr.receipt_detection import detect_and_warp


def draw_bbox(img, bbox, color=(0, 255, 0)):
    x1, y1, x2, y2 = bbox
    cv2.rectangle(img, (x1, y1), (x2, y2), color, 2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--img', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--debug', action='store_true')
    args = ap.parse_args()

    img = cv2.imread(args.img)
    if img is None:
        print(f"Cannot read image: {args.img}", file=sys.stderr)
        sys.exit(1)
    warped, meta = detect_and_warp(img)
    # montage: original with bbox on left, warped on right
    vis = img.copy()
    if meta.get('bbox'):
        draw_bbox(vis, meta['bbox'])
    h1, w1 = vis.shape[:2]
    h2, w2 = warped.shape[:2]
    out_h = max(h1, h2)
    canvas = np.zeros((out_h, w1 + w2, 3), dtype=np.uint8)
    canvas[:h1, :w1] = vis
    canvas[:h2, w1:w1 + w2] = warped
    cv2.imwrite(args.out, canvas)
    print(json.dumps(meta, indent=2))


if __name__ == '__main__':
    main()

