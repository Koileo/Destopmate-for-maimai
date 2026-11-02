using System;
using UnityEngine;

namespace DesktopMateOnMaimai.Core.Motion
{
    // Muscle-only clips omit root travel and foot IK. Restore the standing
    // support height after sampling, in model space so stage scaling is harmless.
    internal sealed class StandingAnchor
    {
        private readonly Transform _root, _hips, _left, _right;
        private readonly Vector3 _support;

        public StandingAnchor(PortableCharacter model)
        {
            _root = model.Root.transform;
            _hips = Find(model, "Hips", "Root");
            _left = Find(model, "Foot_Left", "L_Foot");
            _right = Find(model, "Foot_Right", "R_Foot");
            if (Ready) _support = Support();
        }

        private bool Ready => _hips != null && _left != null && _right != null;

        private static Transform Find(PortableCharacter model, string name, string alias) =>
            Array.Find(model.Nodes, t => t != null && (t.name == name || t.name == alias));

        private Vector3 Support()
        {
            Vector3 left = _root.InverseTransformPoint(_left.position);
            Vector3 right = _root.InverseTransformPoint(_right.position);
            // Keep horizontal support centered without cancelling the torso's
            // rotations. The lower foot carries the body's weight.
            return new Vector3((left.x + right.x) * 0.5f, Mathf.Min(left.y, right.y),
                (left.z + right.z) * 0.5f);
        }

        public void Apply()
        {
            if (!Ready) return;
            Vector3 correction = _root.TransformVector(_support - Support());
            Transform parent = _hips.parent;
            _hips.localPosition += parent != null ? parent.InverseTransformVector(correction) : correction;
        }
    }
}
