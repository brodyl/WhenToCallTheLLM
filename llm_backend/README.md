# XR NLP FastAPI Server — Setup & Run Guide

This README explains how to get the provided **FastAPI** server running **locally**, with the exact NLP stack it expects:
- **FAISS** with optional **GPU acceleration**
- **spaCy** with the **`en_core_web_trf`** transformer model
- **Stanza** (tokenize / POS / lemma / dep)
- **Sentence-Transformers** on CUDA (recommended)
- OpenAI API access for a few helper endpoints

If you just want the quick start (CPU-only), jump to **[Quick Start (CPU‑only)](#quick-start-cpu-only)**. If you need **GPU FAISS** and **CUDA** acceleration for embeddings, use **[GPU Setup (Recommended)](#gpu-setup-recommended)**.

> **Python version:** Please use **Python 3.10 or 3.11**. Some packages in this stack (transformers, spaCy TRF, stanza, faiss, coreferee) are more finicky on 3.12+.

---

## Contents
- [Project layout](#project-layout)
- [Prerequisites](#prerequisites)
- [Environment setup options](#environment-setup-options)
  - [A) Quick Start (CPU‑only)](#quick-start-cpu-only)
  - [B) GPU Setup (Recommended)](#gpu-setup-recommended)
  - [C) Windows with WSL2 (GPU or CPU)](#windows-with-wsl2-gpu-or-cpu)
- [Environment variables](#environment-variables)
- [First-time model downloads](#first-time-model-downloads)
- [Running the API](#running-the-api)
- [Interactive docs](#interactive-docs)
- [Example requests](#example-requests)
  - [/create_faiss_embeddings](#create_faiss_embeddings)
  - [/search_name_in_faiss](#search_name_in_faiss)
  - [/search_description_in_faiss](#search_description_in_faiss)
  - [/command_complexity](#command_complexity)
  - [/openai_get_objects_and_attributes_using_chatgpt](#openai_get_objects_and_attributes_using_chatgpt)
- [Troubleshooting](#troubleshooting)
- [Notes on performance](#notes-on-performance)

---

## Project layout

```
your_project/
├─ server.py                # <— put the provided script here, or rename to your liking
├─ .env                     # holds OPENAI_API_KEY
├─ requirements.txt         # optional; see below
└─ embedding_exports/       # created automatically after first embedding export
```

A suggested `requirements.txt` that works well on CPU is:

```txt
fastapi==0.115.*
uvicorn[standard]==0.34.*
python-dotenv==1.1.*
requests==2.*
pydantic==2.*
tiktoken==0.9.*

# NLP
spacy==3.8.*
stanza==1.10.*
sentence-transformers==3.4.*

# Indexing (assuming you have cuda 12)
faiss-gpu-cu12==1.10.*
```

> For **GPU FAISS** don’t install `faiss-cpu`. install `faiss-gpu` instead (details below).

---

## Prerequisites

- **Python 3.10 or 3.11**
- (Recommended) **Conda / mamba** for managing FAISS GPU builds and CUDA toolkits:
  - [Miniconda](https://docs.anaconda.com/miniconda/)
  - [Mambaforge](https://github.com/conda-forge/miniforge)
- **OpenAI API key** for the `/openai_*` endpoints

If using **GPU acceleration**:
- **NVIDIA driver** installed on the host
- CUDA-compatible GPU (e.g., RTX / A100 / etc.)
- On Linux or WSL2: CUDA runtime via conda environment (easiest)

---

## Environment setup options

### Quick Start (CPU‑only)

Good for trying the API on any machine without GPU:

```bash
# 1) Create & activate a clean venv (Python 3.10 or 3.11)
python -m venv .venv
source .venv/bin/activate          # Windows PowerShell: .venv\Scripts\Activate.ps1

# 2) Install requirements
pip install --upgrade pip
pip install -r requirements.txt    # or paste the list from this README into a file first

# If you didn't create requirements.txt, you can install the basics directly:
# pip install fastapi uvicorn[standard] python-dotenv requests pydantic tiktoken \
#             spacy stanza sentence-transformers faiss-cpu

# 3) Prepare .env with your OpenAI key
echo "OPENAI_API_KEY=sk-xxxxxxxx" > .env

# 4) Download the spaCy transformer model once
python -m spacy download en_core_web_trf
```

Then run the server (see **Running the API**).

> **Note:** CPU FAISS is fine for small toy sets. For large vector collections and fast searches, use GPU setup.

---

### GPU Setup (Recommended)

Using **conda/mamba** is the most reliable way to get **FAISS GPU** and the right CUDA runtime:

```bash
# 1) Create a new environment (Python 3.10 or 3.11)
mamba create -n xrnlp python=3.11 -y       # or: conda create -n xrnlp python=3.11 -y
mamba activate xrnlp

# 2) Install FAISS with GPU from conda-forge
mamba install -c conda-forge faiss-gpu -y

# 3) Install the rest via pip (or mamba where available)
pip install fastapi uvicorn[standard] python-dotenv requests pydantic tiktoken \
            spacy stanza sentence-transformers

# OPTIONAL: local LLM backends (script imports them, not required for main endpoints)
# pip install llama-cpp-python ctransformers

# 4) Ensure PyTorch has CUDA support (needed by sentence-transformers & spaCy TRF)
# Choose the CUDA wheel that matches the conda CUDA runtime; e.g. cu121 or cu118:
pip install --index-url https://download.pytorch.org/whl/cu121 torch torchvision torchaudio

# 5) Prepare .env
echo "OPENAI_API_KEY=sk-xxxxxxxx" > .env

# 6) Download the spaCy transformer model once
python -m spacy download en_core_web_trf
```

Validate CUDA visibility:

```bash
python -c "import torch;print('CUDA available:', torch.cuda.is_available()); \
import faiss; import numpy as np; print('FAISS OK:', hasattr(faiss,'StandardGpuResources'))"
```

If you see `CUDA available: True` and FAISS exposes `StandardGpuResources`, you’re in good shape.

---

### Windows with WSL2 (GPU or CPU)

If you’re on Windows and want **GPU FAISS**:
1. Install **WSL2** (Ubuntu).
2. Install the latest **NVIDIA driver** on Windows.
3. In WSL2 Ubuntu, follow the **GPU Setup** steps above using mamba/conda.
4. Verify GPU access:
   ```bash
   nvidia-smi
   python -c "import torch; print(torch.cuda.is_available())"
   ```

If you only need CPU, you can also run everything directly in native Windows Python and install `faiss-cpu` via pip.

---

## Environment variables

Create a `.env` file in the project root (or set env vars another way). The script **requires**:

```
OPENAI_API_KEY=sk-xxxxxxxxxxxxxxxxxxxxxxxx
```

> Without this, any `/openai_*` endpoints will raise `RuntimeError("OPENAI_API_KEY not set")` at import time.

---

## First-time model downloads

The script includes a helper you can call **once** to fetch models:

- **spaCy** `en_core_web_trf` is required:
  ```bash
  python -m spacy download en_core_web_trf
  ```

- **Stanza** downloads English models on first pipeline creation automatically:
  ```python
  import stanza
  stanza.Pipeline(lang='en', processors='tokenize,mwt,pos,lemma,depparse')
  ```

- **Sentence-Transformers** will download `all-MiniLM-L6-v2` the first time it runs.

> You can also manually run `initial_setup()` from the script if you want. It just downloads the spaCy TRF model.

---

## Running the API

If your file is named `server.py` (and it contains `if __name__ == "__main__": uvicorn.run(...)`), you can simply run:

```bash
python server.py
```

Or, to run with `uvicorn` explicitly:

```bash
uvicorn server:app --host 0.0.0.0 --port 8000 --reload
```

Server starts at: **http://127.0.0.1:8000**

---

## Interactive docs

Once running, open:
- **Swagger UI:** <http://127.0.0.1:8000/docs>
- **ReDoc:** <http://127.0.0.1:8000/redoc>

These are generated automatically by FastAPI and let you call endpoints from the browser.

---

## Example requests

Below are minimal examples using `curl`. You can also use the Swagger UI.

### `/create_faiss_embeddings`

Registers your scene objects, computes embeddings (CUDA if available), and builds the FAISS indices.

```bash
curl -X POST "http://127.0.0.1:8000/create_faiss_embeddings" \
  -H "Content-Type: application/json" \
  -d '{
    "objects": [
      {"id": "0001", "name": "Silver Apple MacBook Pro", "description": "A thin, silver Apple laptop computer."},
      {"id": "0002", "name": "Yellow Office Desk", "description": "Large rectangular desk with smooth surface."},
      {"id": "0003", "name": "Office Chair", "description": "Black wheeled chair with mesh back."}
    ]
  }'
```

Response:
```json
{"message":"Objects updated!","total_objects":3}
```

This also writes TSVs to `embedding_exports/` for projector tooling.

---

### `/search_name_in_faiss`

Name-based nearest-neighbor search:

```bash
curl -X POST "http://127.0.0.1:8000/search_name_in_faiss" \
  -H "Content-Type: application/json" \
  -d '{"query":"macbook","top_k":3}'
```

### `/search_description_in_faiss`

Description-based nearest-neighbor search:

```bash
curl -X POST "http://127.0.0.1:8000/search_description_in_faiss" \
  -H "Content-Type: application/json" \
  -d '{"query":"thin silver apple laptop","top_k":5}'
```

---

### `/command_complexity`

Returns 1 (simple) / 2 (moderate) / 3 (complex) based on heuristic rules:

```bash
curl -X POST "http://127.0.0.1:8000/command_complexity" \
  -H "Content-Type: application/json" \
  -d '{"command":"Select the blue laptop next to the desk"}'
```

---

### `/openai_get_objects_and_attributes_using_chatgpt`

Requires an **OpenAI key** and accepts optional named entities to bias parsing:

```bash
curl -X POST "http://127.0.0.1:8000/openai_get_objects_and_attributes_using_chatgpt" \
  -H "Content-Type: application/json" \
  -d '{
    "command":"Select the largest shiny blue pot on top of the wooden table.",
    "named_entities": ["Apple MacBook Pro", "Office Desk"]
  }'
```

> Similar request shapes exist for:
> - `/openai_get_main_object_and_descriptors_using_chatgpt`
> - `/openai_get_spatial_relationships_using_chatgpt`
> - `/openai_get_object_matches_using_chatgpt`
> - `/openai_get_object_description_matches_using_chatgpt`

---

## Troubleshooting

**Q: `OSError: [E050] Can't find model 'en_core_web_trf'`**
- Run: `python -m spacy download en_core_web_trf`
- Ensure you’re inside the **same environment** where the server runs.

**Q: GPU not used for FAISS / Sentence-Transformers**
- Confirm Torch sees CUDA: `python -c "import torch; print(torch.cuda.is_available())"`
- Use conda `faiss-gpu` build (not `faiss-cpu`).
- Ensure your Torch wheel matches the CUDA runtime (e.g., `cu121` for CUDA 12.1).

**Q: `OPENAI_API_KEY not set` at import time**
- Create `.env` with `OPENAI_API_KEY=...` in the project root **before** starting the server.
- Or export in shell: `export OPENAI_API_KEY=...` (PowerShell: `$env:OPENAI_API_KEY="..."`).

**Q: Stanza takes a while on first run**
- That’s normal — it downloads models. Subsequent runs are cached in `~/.stanza`.

**Q: Windows + GPU?**
- Prefer **WSL2**. Install NVIDIA drivers on Windows, then use **WSL2 Ubuntu** with conda/mamba to install `faiss-gpu` and CUDA-enabled PyTorch.

---

## Notes on performance

- **GPU embeddings + GPU FAISS** dramatically speed up `/create_faiss_embeddings` and subsequent searches.
- The **spaCy transformer** pipeline is accurate but heavier; if you need throughput, consider running with `en_core_web_sm` for quick experiments (update the code accordingly).
- The `/openai_*` endpoints add network latency + token costs; the script includes price tables and cost reporting for transparency.

---

### One-liner sanity check

After starting the server, this should return an empty structure (no objects yet) rather than a crash:

```bash
curl -X POST "http://127.0.0.1:8000/search_name_in_faiss" \
  -H "Content-Type: application/json" \
  -d '{"query":"test","top_k":1}'
```
