"""
AI Bridge - Python module for superbook-pdf AI integration

This package provides Python bridges for AI tools:
- RealESRGAN: Image super-resolution and upscaling
- YomiToku: Japanese AI-OCR

Usage from CLI:
    python -m ai_bridge.realesrgan_bridge -i input.png -o output.png
    python -m ai_bridge.yomitoku_bridge input.png --format json
"""

__version__ = "0.1.0"
__all__ = ["realesrgan_bridge", "yomitoku_bridge"]
