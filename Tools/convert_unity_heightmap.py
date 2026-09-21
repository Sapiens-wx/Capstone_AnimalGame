"""Convert a Unity 16-bit RAW heightmap to a lossless 16-bit grayscale PNG.

Requires Pillow: python -m pip install Pillow
Run from the project root; Unity's Export Raw defaults used here are
Depth = Bit 16, Byte Order = Windows, Flip Vertically = off.
"""

import argparse
from pathlib import Path
import struct

from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("raw", type=Path)
    parser.add_argument("png", type=Path)
    parser.add_argument("--width", type=int, default=513)
    parser.add_argument("--height", type=int, default=513)
    parser.add_argument("--byte-order", choices=("little", "big"), default="little")
    parser.add_argument(
        "--raw-top-down", action="store_true",
        help="Use if Flip Vertically was enabled during Unity RAW export.",
    )
    parser.add_argument("--overwrite", action="store_true")
    args = parser.parse_args()

    if args.width <= 0 or args.height <= 0:
        parser.error("Width and height must be positive.")
    if args.png.suffix.lower() != ".png" or args.png.resolve() == args.raw.resolve():
        parser.error("Output must be a separate .png file.")
    if args.png.exists() and not args.overwrite:
        parser.error("Output already exists; use --overwrite to replace it.")

    data = args.raw.read_bytes()
    expected = args.width * args.height * 2
    if len(data) != expected:
        parser.error(f"Expected {expected} RAW bytes, got {len(data)}. Check dimensions and bit depth.")

    raw_mode = "I;16" if args.byte_order == "little" else "I;16B"
    heightmap = Image.frombytes(
        "I", (args.width, args.height), data, "raw", raw_mode,
    ).convert("I;16")
    if not args.raw_top_down:
        # Unity RAW starts at Z=0; PNG stores the top image row first.
        # Unity's PNG importer reverses that storage convention again, so
        # GetPixel(x, y) then matches Terrain.GetHeights()[y, x].
        heightmap = heightmap.transpose(Image.Transpose.FLIP_TOP_BOTTOM)

    args.png.parent.mkdir(parents=True, exist_ok=True)
    heightmap.save(args.png, format="PNG")

    header = args.png.read_bytes()[:33]
    width, height, depth, color_type = struct.unpack(">IIBB", header[16:26])
    if (width, height, depth, color_type) != (args.width, args.height, 16, 0):
        raise RuntimeError("Output is not a 16-bit grayscale PNG.")
    with Image.open(args.png) as restored:
        if restored.convert("I;16").tobytes() != heightmap.tobytes():
            raise RuntimeError("PNG verification failed: a height sample changed.")

    print(f"Saved: {args.png}")
    print(f"Verified: {width} x {height}, 16-bit grayscale, every sample preserved.")
    print(f"Sample range: {heightmap.getextrema()}; no normalization or resizing.")


if __name__ == "__main__":
    main()
