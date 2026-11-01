"""Convert a local CBv3 file to an inspection copy without changing the original.

Format reference: https://github.com/Xyzstk/DesktopMateAssetDecryptor
"""
import base64
import struct
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / '.tools' / 'python'))
import numpy as np
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
import UnityPy

source = Path(sys.argv[1])
destination = Path(sys.argv[2]) if len(sys.argv) > 2 else root / '.tools' / (source.stem + '.unity3d')
raw = source.read_bytes()
if raw[:4] != b'CBv3':
    raise ValueError('Expected CBv3 input')
offset, = struct.unpack_from('<I', raw, 4)
xor = raw[8:264]
length = len(raw) - offset
blocks = bytearray(((length + 15) // 16) * 16)
for index in range(1, len(blocks) // 16 + 1):
    a, = struct.unpack_from('<Q', xor, index & 0x1f)
    b, = struct.unpack_from('<Q', xor, (index + 16) & 0x1f)
    struct.pack_into('<Q', blocks, (index - 1) * 16, index ^ a ^ b)
cipher = Cipher(algorithms.AES(base64.b64decode('TyJripS/Hy9NRQp7/6Spjw==')), modes.ECB()).encryptor()
stream = cipher.update(bytes(blocks)) + cipher.finalize()
decoded = np.bitwise_xor(np.frombuffer(raw[offset:], np.uint8), np.frombuffer(stream[:length], np.uint8)).tobytes()
if not decoded.startswith(b'UnityFS\0'):
    raise ValueError('Decoded input is not UnityFS')
destination.write_bytes(decoded)
print('Inspection bundle:', destination, decoded[:48])

if '--decode-only' in sys.argv:
    sys.exit(0)
env = UnityPy.load(str(destination))
from collections import Counter
print(Counter(o.type.name for o in env.objects))
for obj in env.objects:
    if obj.type.name in ('Mesh', 'Material', 'Texture2D', 'SkinnedMeshRenderer', 'AnimationClip'):
        data = obj.read()
        print(obj.path_id, obj.type.name, getattr(data, 'm_Name', ''),
              (getattr(data, 'm_Width', ''), getattr(data, 'm_Height', '')))
