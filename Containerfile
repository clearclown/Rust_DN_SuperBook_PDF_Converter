# DN_SuperBook_PDF_Converter - Linux Podman Container with CUDA Support
# Base: NVIDIA CUDA 11.8 + Ubuntu 22.04

FROM docker.io/nvidia/cuda:11.8.0-runtime-ubuntu22.04

LABEL maintainer="DN_SuperBook_PDF_Converter Linux Port"
LABEL description="PDF Converter with AI upscaling (RealESRGAN) and Japanese OCR (YomiToku)"

# Prevent interactive prompts during package installation
ENV DEBIAN_FRONTEND=noninteractive
ENV TZ=Asia/Tokyo

# Install system dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    # .NET dependencies
    wget \
    ca-certificates \
    # Image processing tools
    imagemagick \
    ghostscript \
    libimage-exiftool-perl \
    qpdf \
    # OCR
    tesseract-ocr \
    tesseract-ocr-jpn \
    tesseract-ocr-eng \
    # Python for RealESRGAN and YomiToku
    python3 \
    python3-venv \
    # Build tools
    git \
    curl \
    xz-utils \
    # OpenCV dependencies
    libgdiplus \
    libx11-6 \
    libxext6 \
    libsm6 \
    libxrender1 \
    libfontconfig1 \
    libice6 \
    libgl1-mesa-glx \
    libglib2.0-0 \
    # Additional libraries for image processing
    libpng-dev \
    libjpeg-dev \
    libtiff-dev \
    libwebp-dev \
    # Fonts (for PDF rendering)
    fonts-noto-cjk \
    fonts-noto-cjk-extra \
    && rm -rf /var/lib/apt/lists/*

# Install .NET 6.0 SDK using official install script
ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --channel 6.0 --install-dir $DOTNET_ROOT \
    && rm dotnet-install.sh \
    && dotnet --info

# Install pdfcpu (Go-based PDF utility)
RUN wget -q https://github.com/pdfcpu/pdfcpu/releases/download/v0.11.0/pdfcpu_0.11.0_Linux_x86_64.tar.xz \
    && tar -xf pdfcpu_0.11.0_Linux_x86_64.tar.xz \
    && mv pdfcpu_0.11.0_Linux_x86_64/pdfcpu /usr/local/bin/ \
    && rm -rf pdfcpu_0.11.0_Linux_x86_64* \
    && chmod +x /usr/local/bin/pdfcpu

# Fix ImageMagick policy for PDF processing and increase resource limits
# Ubuntu 22.04 has ImageMagick 6 with 'convert' command, not 'magick'
RUN sed -i 's/rights="none" pattern="PDF"/rights="read|write" pattern="PDF"/g' /etc/ImageMagick-6/policy.xml || true \
    && sed -i 's/<policy domain="resource" name="memory" value="[^"]*"/<policy domain="resource" name="memory" value="8GiB"/g' /etc/ImageMagick-6/policy.xml || true \
    && sed -i 's/<policy domain="resource" name="map" value="[^"]*"/<policy domain="resource" name="map" value="8GiB"/g' /etc/ImageMagick-6/policy.xml || true \
    && sed -i 's/<policy domain="resource" name="disk" value="[^"]*"/<policy domain="resource" name="disk" value="16GiB"/g' /etc/ImageMagick-6/policy.xml || true \
    && sed -i 's/<policy domain="resource" name="area" value="[^"]*"/<policy domain="resource" name="area" value="1GiB"/g' /etc/ImageMagick-6/policy.xml || true \
    && sed -i 's/<policy domain="resource" name="width" value="[^"]*"/<policy domain="resource" name="width" value="64KP"/g' /etc/ImageMagick-6/policy.xml || true \
    && sed -i 's/<policy domain="resource" name="height" value="[^"]*"/<policy domain="resource" name="height" value="64KP"/g' /etc/ImageMagick-6/policy.xml || true \
    && ln -sf /usr/bin/convert /usr/bin/magick \
    && ln -sf /usr/bin/mogrify /usr/bin/mogrify

# Install uv (ultra-fast Python package manager - 10-100x faster than pip)
RUN curl -LsSf https://astral.sh/uv/install.sh | sh
ENV PATH="/root/.local/bin:$PATH"

# Create app directory
WORKDIR /app

# Copy project files
COPY . /app/

# Setup SINGLE shared Python virtual environment for AI tools (RealESRGAN + YomiToku)
# Using uv for dramatically faster package installation
# Pin torch/torchvision versions for basicsr 1.4.2 compatibility
# basicsr needs torchvision.transforms.functional_tensor which was removed in 0.18+
# basicsr also requires numpy<2 (compiled against NumPy 1.x ABI)
# Using torch 2.0.1+cu118 with torchvision 0.15.2+cu118 for compatibility
# Install torch LAST to override versions pulled in by realesrgan/yomitoku
ENV AI_VENV=/app/external_tools/ai_venv
RUN uv venv $AI_VENV --python python3.10 \
    && VIRTUAL_ENV=$AI_VENV uv pip install realesrgan yomitoku \
    && VIRTUAL_ENV=$AI_VENV uv pip install --force-reinstall \
        torch==2.0.1+cu118 torchvision==0.15.2+cu118 \
        --index-url https://download.pytorch.org/whl/cu118 \
    && VIRTUAL_ENV=$AI_VENV uv pip install "numpy<2"

# Clone Real-ESRGAN repository for inference script
# Remove the local realesrgan folder to use the pip-installed package instead
# (the local folder shadows the installed package and causes import errors)
RUN mkdir -p /app/external_tools/external_tools/image_tools/RealEsrgan/RealEsrgan_Repo \
    && cd /app/external_tools/external_tools/image_tools/RealEsrgan/RealEsrgan_Repo \
    && git clone --depth 1 https://github.com/xinntao/Real-ESRGAN.git \
    && rm -rf /app/external_tools/external_tools/image_tools/RealEsrgan/RealEsrgan_Repo/Real-ESRGAN/realesrgan \
    && ln -s $AI_VENV /app/external_tools/external_tools/image_tools/RealEsrgan/RealEsrgan_Repo/venv \
    && mkdir -p /app/external_tools/external_tools/image_tools/yomitoku \
    && ln -s $AI_VENV /app/external_tools/external_tools/image_tools/yomitoku/venv

# Setup Tesseract data directory
RUN mkdir -p /app/external_tools/external_tools/image_tools/TesseractOCR_Data \
    && ln -sf /usr/share/tesseract-ocr/4.00/tessdata/eng.traineddata /app/external_tools/external_tools/image_tools/TesseractOCR_Data/ \
    && ln -sf /usr/share/tesseract-ocr/4.00/tessdata/jpn.traineddata /app/external_tools/external_tools/image_tools/TesseractOCR_Data/

# Build the .NET application
RUN dotnet restore SuperBookToolsApp/SuperBookToolsApp.csproj \
    && dotnet build SuperBookToolsApp/SuperBookToolsApp.csproj -c Release -o /app/build

# Create input/output directories
RUN mkdir -p /data/input /data/output

# Set environment variables
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV NVIDIA_VISIBLE_DEVICES=all
ENV NVIDIA_DRIVER_CAPABILITIES=compute,utility

# Default working directory for data
WORKDIR /data

# Entry point
ENTRYPOINT ["dotnet", "/app/build/SuperBookToolsApp.dll"]
CMD ["--help"]
