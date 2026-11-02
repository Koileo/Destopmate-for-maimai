"""Export local humanoid curves, not Unity-version-specific serialized clips.

Input is an already decoded inspection bundle. Outputs stay with the local
character; the default Miku runtime motion is also included in release packages.
"""
import bisect
import math
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / '.tools' / 'python'))
import UnityPy


def sample_curves(tree, rate=30):
    muscle = tree['m_MuscleClip']
    clip = muscle['m_Clip']['data']
    stream = clip['m_StreamedClip']
    raw = struct.pack('<' + 'I' * len(stream['data']), *stream['data'])
    tracks = [[] for _ in range(stream['curveCount'])]
    offset = 0
    while offset < len(raw):
        time, count = struct.unpack_from('<fi', raw, offset)
        offset += 8
        for _ in range(count):
            index, a, b, c, value = struct.unpack_from('<i4f', raw, offset)
            offset += 20
            if math.isfinite(time):
                tracks[index].append((time, a, b, c, value))
            elif time < 0:
                tracks[index].append((-1e10, 0, 0, 0, value))
    times = [[key[0] for key in track] for track in tracks]
    dense = clip['m_DenseClip']
    constants = clip['m_ConstantClip']['data']
    bindings = tree['m_ClipBindingConstant']['genericBindings']
    # Humanoid bindings 0..41 are root/motion/IK goals; 42..136 are
    # HumanTrait.MuscleName in order. Each customType=8 binding is scalar.
    if any(b['customType'] != 8 for b in bindings):
        raise ValueError('Expected scalar humanoid bindings: ' + tree['m_Name'])
    assert len(bindings) == len(tracks) + dense['m_CurveCount'] + len(constants)
    duration = muscle['m_StopTime'] - muscle['m_StartTime']
    frames = max(2, math.ceil(duration * rate) + 1)
    result = []
    for frame in range(frames):
        time = muscle['m_StartTime'] + min(duration, frame / rate)
        values = []
        for track, tt in zip(tracks, times):
            key = track[max(0, bisect.bisect_right(tt, time) - 1)]
            dt = max(0, time - key[0])
            values.append(((key[1] * dt + key[2]) * dt + key[3]) * dt + key[4])
        dc = dense['m_CurveCount']
        if dc:
            f = max(0, min(dense['m_FrameCount'] - 1,
                           (time - dense['m_BeginTime']) * dense['m_SampleRate']))
            lo = int(f)
            hi = min(lo + 1, dense['m_FrameCount'] - 1)
            values += [dense['m_SampleArray'][lo * dc + i] * (1 - (f - lo)) +
                       dense['m_SampleArray'][hi * dc + i] * (f - lo) for i in range(dc)]
        values += constants
        pose = [0.0] * 95
        for binding, value in zip(bindings, values):
            if 42 <= binding['attribute'] <= 136:
                if not math.isfinite(value) or abs(value) > 10:
                    raise ValueError('Invalid muscle curve')
                pose[binding['attribute'] - 42] = value
        result.extend(pose)
    return duration, frames, result


def main():
    env = UnityPy.load(sys.argv[1])
    avatar = next(o.read_typetree()['m_HumanDescription'] for o in env.objects if o.type.name == 'Avatar')
    selected = set(sys.argv[3:] or ['a1', 'a2', 'a4', 'a8', 'b1'])
    clips = [o.read_typetree() for o in env.objects if o.type.name == 'AnimationClip']
    clips = sorted((c for c in clips if c['m_Name'] in selected), key=lambda c: c['m_Name'])
    if {c['m_Name'] for c in clips} != selected:
        raise ValueError('Missing selected clips')
    output = Path(sys.argv[2])
    with output.open('wb') as f:
        def integer(v): f.write(struct.pack('<i', v))
        def text(v):
            data = v.encode('utf-8'); integer(len(data)); f.write(data)
        def vector(v, fields): f.write(struct.pack('<' + 'f' * len(fields), *(v[k] for k in fields)))
        f.write(b'DMMUSC01')
        integer(len(avatar['m_Human']))
        for bone in avatar['m_Human']:
            text(bone['m_BoneName']); text(bone['m_HumanName'])
        integer(len(avatar['m_Skeleton']))
        for bone in avatar['m_Skeleton']:
            text(bone['m_Name']); vector(bone['m_Position'], 'xyz')
            vector(bone['m_Rotation'], 'xyzw'); vector(bone['m_Scale'], 'xyz')
        integer(len(clips))
        for clip in clips:
            duration, frames, values = sample_curves(clip)
            text(clip['m_Name']); f.write(struct.pack('<ffi', duration, 30.0, frames))
            f.write(struct.pack('<' + 'f' * len(values), *values))
            print(clip['m_Name'], duration, frames)
    print(output, output.stat().st_size)


if __name__ == '__main__':
    main()
