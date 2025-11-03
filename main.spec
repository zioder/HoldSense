# -*- mode: python ; coding: utf-8 -*-
# PyInstaller spec file for HoldSense Python backend

block_cipher = None

a = Analysis(
    ['main.py'],
    pathex=[],
    binaries=[],
    datas=[
        ('yolov8n.onnx', '.'),  # Include the ONNX model
    ],
    hiddenimports=[
        'pystray._win32',
        'PIL._tkinter_finder',
        'numpy.core._dtype_ctypes',
        'onnxruntime.capi.onnxruntime_pybind11_state',
        'cv2',
        'winsdk',
        'winsdk.windows.devices.bluetooth',
        'winsdk.windows.devices.enumeration',
        'winsdk.windows.media.devices',
        'ultralytics',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        'matplotlib',
        'scipy',
        'pandas',
        'IPython',
        'jupyter',
    ],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='HoldSenseBackend',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,  # Keep console for debugging via stdout/stdin
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)

coll = COLLECT(
    exe,
    a.binaries,
    a.zipfiles,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name='HoldSenseBackend',
)

