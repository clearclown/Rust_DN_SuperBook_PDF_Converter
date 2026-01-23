#!/usr/bin/env python3
"""
YomiToku Bridge for superbook-pdf

Japanese AI-OCR using YomiToku for high-accuracy text recognition.
Designed to be called from Rust via subprocess.

Usage:
    python yomitoku_bridge.py INPUT [options]

Options:
    --output        Output directory or file
    --format        Output format: json, text, pdf, markdown (default: json)
    --gpu           GPU device ID (default: 0)
    --no-gpu        Disable GPU, use CPU only
    --confidence    Minimum confidence threshold (0.0-1.0, default: 0.5)

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
from typing import List, Dict, Any, Optional

try:
    import torch
except ImportError:
    torch = None

try:
    from yomitoku import DocumentAnalyzer
    from yomitoku.data.functions import load_image
    YOMITOKU_AVAILABLE = True
except ImportError:
    YOMITOKU_AVAILABLE = False


# Exit codes matching Rust exit_codes
EXIT_SUCCESS = 0
EXIT_ERROR = 1
EXIT_INVALID_ARGS = 2
EXIT_INPUT_NOT_FOUND = 3
EXIT_OUTPUT_ERROR = 4
EXIT_GPU_ERROR = 5
EXIT_OOM = 6


def detect_text_direction(blocks: List[Dict]) -> str:
    """Detect overall text direction from blocks."""
    if not blocks:
        return "horizontal"

    vertical_count = 0
    horizontal_count = 0

    for block in blocks:
        direction = block.get("direction", "horizontal")
        if direction == "vertical":
            vertical_count += 1
        else:
            horizontal_count += 1

    if vertical_count > horizontal_count * 2:
        return "vertical"
    elif horizontal_count > vertical_count * 2:
        return "horizontal"
    else:
        return "mixed"


def process_image(
    input_path: Path,
    output_format: str = "json",
    gpu_id: Optional[int] = 0,
    confidence_threshold: float = 0.5,
) -> Dict[str, Any]:
    """Process a single image with YomiToku OCR."""
    start_time = time.time()

    if not input_path.exists():
        return {"error": "Input not found", "exit_code": EXIT_INPUT_NOT_FOUND}

    if not YOMITOKU_AVAILABLE:
        return {
            "error": "YomiToku not installed. Install with: pip install yomitoku",
            "exit_code": EXIT_ERROR,
        }

    try:
        # Configure device
        if gpu_id is not None and torch is not None and torch.cuda.is_available():
            device = f"cuda:{gpu_id}"
        else:
            device = "cpu"

        # Initialize analyzer
        analyzer = DocumentAnalyzer(device=device)

        # Load and process image
        img = load_image(str(input_path))
        result = analyzer(img)

        # Extract text blocks
        text_blocks = []
        full_text = []

        for line in result.lines:
            block = {
                "text": line.text,
                "bbox": [line.box.x1, line.box.y1, line.box.x2, line.box.y2],
                "confidence": float(line.confidence) if hasattr(line, "confidence") else 1.0,
                "direction": "vertical" if getattr(line, "vertical", False) else "horizontal",
            }

            if block["confidence"] >= confidence_threshold:
                text_blocks.append(block)
                full_text.append(line.text)

        elapsed = time.time() - start_time

        # Calculate overall confidence
        if text_blocks:
            avg_confidence = sum(b["confidence"] for b in text_blocks) / len(text_blocks)
        else:
            avg_confidence = 0.0

        return {
            "input_path": str(input_path),
            "text_blocks": text_blocks,
            "full_text": "\n".join(full_text),
            "confidence": avg_confidence,
            "text_direction": detect_text_direction(text_blocks),
            "processing_time": elapsed,
            "exit_code": EXIT_SUCCESS,
        }

    except RuntimeError as e:
        error_str = str(e).lower()
        if "cuda" in error_str or "gpu" in error_str:
            return {"error": str(e), "exit_code": EXIT_GPU_ERROR}
        elif "out of memory" in error_str:
            return {"error": "Out of memory", "exit_code": EXIT_OOM}
        return {"error": str(e), "exit_code": EXIT_ERROR}
    except Exception as e:
        return {"error": str(e), "exit_code": EXIT_ERROR}


def format_output(result: Dict[str, Any], output_format: str) -> str:
    """Format result according to specified output format."""
    if output_format == "json":
        return json.dumps(result, ensure_ascii=False, indent=2)
    elif output_format == "text":
        return result.get("full_text", "")
    elif output_format == "markdown":
        lines = [f"# OCR Result: {result.get('input_path', 'Unknown')}"]
        lines.append("")
        lines.append(f"**Confidence:** {result.get('confidence', 0):.2%}")
        lines.append(f"**Direction:** {result.get('text_direction', 'unknown')}")
        lines.append(f"**Processing Time:** {result.get('processing_time', 0):.2f}s")
        lines.append("")
        lines.append("## Detected Text")
        lines.append("")
        lines.append(result.get("full_text", ""))
        return "\n".join(lines)
    else:
        return json.dumps(result, ensure_ascii=False)


def main():
    parser = argparse.ArgumentParser(description="YomiToku Japanese AI-OCR")
    parser.add_argument("input", help="Input image or directory")
    parser.add_argument("--output", "-o", help="Output path")
    parser.add_argument(
        "--format",
        "-f",
        default="json",
        choices=["json", "text", "pdf", "markdown"],
        help="Output format",
    )
    parser.add_argument("--gpu", "-g", type=int, default=0, help="GPU device ID")
    parser.add_argument("--no-gpu", action="store_true", help="Disable GPU")
    parser.add_argument(
        "--confidence",
        "-c",
        type=float,
        default=0.5,
        help="Confidence threshold (0.0-1.0)",
    )

    args = parser.parse_args()

    input_path = Path(args.input)
    gpu_id = None if args.no_gpu else args.gpu

    if not input_path.exists():
        error_result = {"error": "Input not found", "exit_code": EXIT_INPUT_NOT_FOUND}
        print(json.dumps(error_result), file=sys.stderr)
        sys.exit(EXIT_INPUT_NOT_FOUND)

    # Process single file
    if input_path.is_file():
        result = process_image(
            input_path,
            output_format=args.format,
            gpu_id=gpu_id,
            confidence_threshold=args.confidence,
        )

        output = format_output(result, args.format)

        if args.output:
            output_path = Path(args.output)
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text(output, encoding="utf-8")
            print(f"Output written to: {output_path}")
        else:
            print(output)

        sys.exit(result.get("exit_code", EXIT_ERROR))

    # Process directory
    elif input_path.is_dir():
        results = []
        image_extensions = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff"}

        for img_file in sorted(input_path.iterdir()):
            if img_file.suffix.lower() in image_extensions:
                result = process_image(
                    img_file,
                    output_format=args.format,
                    gpu_id=gpu_id,
                    confidence_threshold=args.confidence,
                )
                results.append(result)

                # Print progress
                if result.get("exit_code", 1) == EXIT_SUCCESS:
                    block_count = len(result.get("text_blocks", []))
                    print(
                        f"Processed: {img_file.name} ({block_count} blocks, "
                        f"{result.get('processing_time', 0):.2f}s)",
                        file=sys.stderr,
                    )
                else:
                    print(
                        f"Failed: {img_file.name} - {result.get('error', 'Unknown')}",
                        file=sys.stderr,
                    )

        # Output batch results
        batch_result = {
            "results": results,
            "total": len(results),
            "successful": len([r for r in results if r.get("exit_code") == EXIT_SUCCESS]),
        }

        if args.output:
            output_path = Path(args.output)
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text(
                json.dumps(batch_result, ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
            print(f"Batch output written to: {output_path}")
        else:
            print(json.dumps(batch_result, ensure_ascii=False, indent=2))

        # Exit with error if any failed
        failed = [r for r in results if r.get("exit_code", 1) != EXIT_SUCCESS]
        if failed:
            sys.exit(EXIT_ERROR)

    else:
        error_result = {"error": "Invalid input path", "exit_code": EXIT_INVALID_ARGS}
        print(json.dumps(error_result), file=sys.stderr)
        sys.exit(EXIT_INVALID_ARGS)


if __name__ == "__main__":
    main()
