#!/usr/bin/env python3
"""
RealESRGAN Bridge for superbook-pdf

Upscale images using Real-ESRGAN for AI-enhanced super-resolution.
Designed to be called from Rust via subprocess.

Usage:
    python realesrgan_bridge.py -i INPUT -o OUTPUT [options]

Options:
    -i, --input     Input image path or directory
    -o, --output    Output path (file or directory)
    -s, --scale     Upscale factor (2 or 4, default: 2)
    -t, --tile      Tile size for processing (default: 400)
    -g, --gpu       GPU device ID (default: 0)
    --model         Model name (default: realesrgan-x4plus)
    --fp32          Use FP32 precision instead of FP16
    --json          Output result as JSON

Exit codes:
    0: Success
    1: General error
    2: Invalid arguments
    3: Input not found
    4: Output error
    5: GPU/CUDA error
    6: Out of memory
"""

import argparse
import json
import os
import sys
import time
from pathlib import Path

try:
    import torch
    from basicsr.archs.rrdbnet_arch import RRDBNet
    from realesrgan import RealESRGANer
    import cv2
    import numpy as np
except ImportError as e:
    print(f"Error: Required package not installed: {e}", file=sys.stderr)
    print("Install with: pip install realesrgan basicsr torch opencv-python", file=sys.stderr)
    sys.exit(2)


# Exit codes matching Rust exit_codes
EXIT_SUCCESS = 0
EXIT_ERROR = 1
EXIT_INVALID_ARGS = 2
EXIT_INPUT_NOT_FOUND = 3
EXIT_OUTPUT_ERROR = 4
EXIT_GPU_ERROR = 5
EXIT_OOM = 6


MODEL_URLS = {
    "RealESRGAN_x4plus.pth": "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth",
    "RealESRGAN_x2plus.pth": "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.1/RealESRGAN_x2plus.pth",
    "RealESRGAN_x4plus_anime_6B.pth": "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.2.4/RealESRGAN_x4plus_anime_6B.pth",
}


def download_model(model_filename: str, target_path: Path) -> bool:
    """Download model weights if not present."""
    import urllib.request
    import ssl
    
    if target_path.exists():
        return True
    
    url = MODEL_URLS.get(model_filename)
    if not url:
        print(f"Unknown model: {model_filename}", file=sys.stderr)
        return False
    
    print(f"Downloading model: {model_filename}...", file=sys.stderr)
    target_path.parent.mkdir(parents=True, exist_ok=True)
    
    try:
        # Create SSL context that doesn't verify certificates (for GitHub redirects)
        ctx = ssl.create_default_context()
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
        
        urllib.request.urlretrieve(url, target_path)
        print(f"Downloaded: {target_path}", file=sys.stderr)
        return True
    except Exception as e:
        print(f"Download failed: {e}", file=sys.stderr)
        return False


def get_model(model_name: str, scale: int):
    """Load RealESRGAN model."""
    # Determine script directory for weights path
    script_dir = Path(__file__).parent
    weights_dir = script_dir / "weights"
    
    if model_name == "realesrgan-x4plus":
        model = RRDBNet(
            num_in_ch=3,
            num_out_ch=3,
            num_feat=64,
            num_block=23,
            num_grow_ch=32,
            scale=4
        )
        netscale = 4
        model_filename = "RealESRGAN_x4plus.pth"
    elif model_name == "realesrgan-x2plus":
        model = RRDBNet(
            num_in_ch=3,
            num_out_ch=3,
            num_feat=64,
            num_block=23,
            num_grow_ch=32,
            scale=2
        )
        netscale = 2
        model_filename = "RealESRGAN_x2plus.pth"
    elif model_name == "realesrgan-x4plus-anime":
        model = RRDBNet(
            num_in_ch=3,
            num_out_ch=3,
            num_feat=64,
            num_block=6,
            num_grow_ch=32,
            scale=4
        )
        netscale = 4
        model_filename = "RealESRGAN_x4plus_anime_6B.pth"
    else:
        raise ValueError(f"Unknown model: {model_name}")
    
    # Try multiple locations for weights
    model_path = weights_dir / model_filename
    
    # Also check common cache locations
    alt_paths = [
        Path.home() / ".cache" / "realesrgan" / model_filename,
        Path("/usr/share/realesrgan") / model_filename,
    ]
    
    # Find existing or download
    if not model_path.exists():
        for alt in alt_paths:
            if alt.exists():
                model_path = alt
                break
        else:
            # Download to script's weights directory
            if not download_model(model_filename, model_path):
                raise FileNotFoundError(f"Model weights not found and download failed: {model_filename}")
    
    return model, netscale, str(model_path)


def upscale_image(
    input_path: Path,
    output_path: Path,
    scale: int = 2,
    tile: int = 400,
    gpu_id: int = 0,
    model_name: str = "realesrgan-x4plus",
    fp32: bool = False,
) -> dict:
    """Upscale a single image."""
    start_time = time.time()

    # Load model
    model, netscale, model_path = get_model(model_name, scale)

    # Calculate output scale (model scale may differ from requested scale)
    outscale = scale

    # Check for model weights
    if not os.path.exists(model_path):
        # Try alternate locations
        alt_paths = [
            f"~/.cache/realesrgan/{os.path.basename(model_path)}",
            f"/usr/share/realesrgan/{os.path.basename(model_path)}",
        ]
        for alt in alt_paths:
            alt = os.path.expanduser(alt)
            if os.path.exists(alt):
                model_path = alt
                break

    try:
        # Initialize upsampler
        upsampler = RealESRGANer(
            scale=netscale,
            model_path=model_path,
            model=model,
            tile=tile,
            tile_pad=10,
            pre_pad=0,
            half=not fp32,
            device=f"cuda:{gpu_id}" if torch.cuda.is_available() else "cpu",
        )
    except RuntimeError as e:
        if "CUDA" in str(e) or "GPU" in str(e):
            return {"error": str(e), "exit_code": EXIT_GPU_ERROR}
        raise

    # Read image
    img = cv2.imread(str(input_path), cv2.IMREAD_UNCHANGED)
    if img is None:
        return {"error": f"Failed to read image: {input_path}", "exit_code": EXIT_INPUT_NOT_FOUND}

    original_size = img.shape[:2]

    try:
        # Upscale
        output, _ = upsampler.enhance(img, outscale=outscale)
    except RuntimeError as e:
        if "out of memory" in str(e).lower():
            return {"error": "Out of memory", "exit_code": EXIT_OOM}
        raise

    # Save output
    output_path.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(output_path), output)

    elapsed = time.time() - start_time
    output_size = output.shape[:2]

    return {
        "input_path": str(input_path),
        "output_path": str(output_path),
        "original_size": list(original_size),
        "output_size": list(output_size),
        "scale": scale,
        "model": model_name,
        "processing_time": elapsed,
        "exit_code": EXIT_SUCCESS,
    }


def main():
    parser = argparse.ArgumentParser(description="RealESRGAN image upscaler")
    parser.add_argument("-i", "--input", required=True, help="Input image or directory")
    parser.add_argument("-o", "--output", required=True, help="Output path")
    parser.add_argument("-s", "--scale", type=int, default=2, choices=[2, 4], help="Upscale factor")
    parser.add_argument("-t", "--tile", type=int, default=400, help="Tile size")
    parser.add_argument("-g", "--gpu", type=int, default=0, help="GPU device ID")
    parser.add_argument("--model", default="realesrgan-x4plus", help="Model name")
    parser.add_argument("--fp32", action="store_true", help="Use FP32 precision")
    parser.add_argument("--json", action="store_true", help="Output as JSON")

    args = parser.parse_args()

    input_path = Path(args.input)
    output_path = Path(args.output)

    if not input_path.exists():
        if args.json:
            print(json.dumps({"error": "Input not found", "exit_code": EXIT_INPUT_NOT_FOUND}))
        else:
            print(f"Error: Input not found: {input_path}", file=sys.stderr)
        sys.exit(EXIT_INPUT_NOT_FOUND)

    # Process single file or directory
    if input_path.is_file():
        result = upscale_image(
            input_path,
            output_path,
            scale=args.scale,
            tile=args.tile,
            gpu_id=args.gpu,
            model_name=args.model,
            fp32=args.fp32,
        )

        if args.json:
            print(json.dumps(result))
        elif result.get("exit_code", 1) == EXIT_SUCCESS:
            print(f"Upscaled: {input_path} -> {output_path} ({result['processing_time']:.2f}s)")
        else:
            print(f"Error: {result.get('error', 'Unknown error')}", file=sys.stderr)

        sys.exit(result.get("exit_code", EXIT_ERROR))

    elif input_path.is_dir():
        # Process directory
        results = []
        image_extensions = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff"}

        output_path.mkdir(parents=True, exist_ok=True)

        for img_file in sorted(input_path.iterdir()):
            if img_file.suffix.lower() in image_extensions:
                out_file = output_path / f"{img_file.stem}_upscaled{img_file.suffix}"
                result = upscale_image(
                    img_file,
                    out_file,
                    scale=args.scale,
                    tile=args.tile,
                    gpu_id=args.gpu,
                    model_name=args.model,
                    fp32=args.fp32,
                )
                results.append(result)

                if not args.json:
                    if result.get("exit_code", 1) == EXIT_SUCCESS:
                        print(f"Upscaled: {img_file.name} ({result['processing_time']:.2f}s)")
                    else:
                        print(f"Failed: {img_file.name} - {result.get('error', 'Unknown error')}", file=sys.stderr)

        if args.json:
            print(json.dumps({"results": results, "total": len(results)}))

        # Exit with error if any failed
        failed = [r for r in results if r.get("exit_code", 1) != EXIT_SUCCESS]
        if failed:
            sys.exit(EXIT_ERROR)

    else:
        if args.json:
            print(json.dumps({"error": "Invalid input path", "exit_code": EXIT_INVALID_ARGS}))
        else:
            print(f"Error: Invalid input path: {input_path}", file=sys.stderr)
        sys.exit(EXIT_INVALID_ARGS)


if __name__ == "__main__":
    main()
