#!/usr/bin/env python
# tools/py/libretranslate_server.py
#
# Thin launcher for the LibreTranslate Python package, invoked by the .NET host the same way as
# argos_server.py:
#
#   python.exe libretranslate_server.py --host 127.0.0.1 --port 5000
#
# LibreTranslate reuses the Argos Translate engine under the hood, so the bundled language packs
# (tools/py/argos_packages) and the bundled MiniSBD sentence-splitter models are used automatically
# when the host sets ARGOS_PACKAGES_DIR / ARGOS_CHUNK_TYPE / XDG_DATA_HOME. No model download happens
# at runtime.

import sys

from libretranslate.main import main

if __name__ == "__main__":
    main()
