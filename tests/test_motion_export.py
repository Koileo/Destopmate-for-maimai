"""Verify curve decoding independently of local copyrighted character assets."""
import importlib.util
import math
import struct
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('motion_export', Path(__file__).resolve().parents[1] / 'tools/export_local_motion.py')
export = importlib.util.module_from_spec(spec)
spec.loader.exec_module(export)


class MotionExportChecks(unittest.TestCase):
    def clip(self):
        # Streamed cubic x(t)=t^3+2t^2+3t+0.5; a dense linear curve;
        # a constant muscle; and a root yaw that must not become a muscle.
        raw = struct.pack('<fi', 0, 1) + struct.pack('<i4f', 0, 1, 2, 3, 0.5)
        words = struct.unpack('<' + 'I' * (len(raw) // 4), raw)
        return {'m_Name': 'synthetic', 'm_MuscleClip': {
            'm_StartTime': 0, 'm_StopTime': 1,
            'm_Clip': {'data': {
                'm_StreamedClip': {'curveCount': 1, 'data': words},
                'm_DenseClip': {'m_CurveCount': 1, 'm_FrameCount': 2,
                                'm_BeginTime': 0, 'm_SampleRate': 1, 'm_SampleArray': [0.1, 0.9]},
                'm_ConstantClip': {'data': [0.7, 0.99]}}}},
            'm_ClipBindingConstant': {'genericBindings': [
                {'customType': 8, 'attribute': i} for i in [42, 43, 136, 13]]}}

    def test_stream_dense_constant_and_root_channels(self):
        duration, frames, values = export.sample_curves(self.clip(), rate=2)
        self.assertEqual((duration, frames), (1, 3))
        self.assertAlmostEqual(values[95], 0.125 + 0.5 + 1.5 + 0.5)
        self.assertAlmostEqual(values[96], 0.5)
        self.assertAlmostEqual(values[95 + 94], 0.7)
        self.assertTrue(all(values[i] == 0 for i in range(2, 94)))
        self.assertAlmostEqual(values[-95], 6.5)

    def test_rejects_nonfinite_and_nonhumanoid_data(self):
        tree = self.clip()
        tree['m_MuscleClip']['m_Clip']['data']['m_ConstantClip']['data'][0] = math.nan
        with self.assertRaises(ValueError):
            export.sample_curves(tree)
        tree = self.clip()
        tree['m_ClipBindingConstant']['genericBindings'][0]['customType'] = 0
        with self.assertRaises(ValueError):
            export.sample_curves(tree)


if __name__ == '__main__':
    unittest.main()
