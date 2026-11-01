using System;
using UnityEngine;

namespace DesktopMateOnMaimai.Core
{
    // The original model and morphs, with modest procedural motion on the 2018 rig.
    internal sealed class RigDriver : ICharacterDriver
    {
        private readonly PortableCharacter _model;
        private readonly Transform _head, _spine, _leftArm, _rightArm;
        private readonly Quaternion _headRest, _spineRest, _leftRest, _rightRest;
        private readonly SkinnedMeshRenderer _face;
        private readonly int _blink = -1;
        private float _time, _blinkTime, _waveTime;
        private Vector3 _gaze;
        public bool Ready => _model.Root != null;

        public RigDriver(PortableCharacter model)
        {
            _model = model;
            foreach (var bone in model.Nodes)
            {
                switch (bone.name)
                {
                    case "Head": _head = bone; break;
                    case "Spine2": _spine = bone; break;
                    case "Arm_Left": _leftArm = bone; break;
                    case "Arm_Right": _rightArm = bone; break;
                }
            }
            _headRest = _head != null ? _head.localRotation : Quaternion.identity;
            _spineRest = _spine != null ? _spine.localRotation : Quaternion.identity;
            _leftRest = _leftArm != null ? _leftArm.localRotation : Quaternion.identity;
            _rightRest = _rightArm != null ? _rightArm.localRotation : Quaternion.identity;
            foreach (var renderer in model.Renderers)
            {
                int index = renderer.sharedMesh.GetBlendShapeIndex("Eye_Blink");
                if (index >= 0) { _face = renderer; _blink = index; break; }
            }
            Tick(0f);
        }

        public void Tick(float dt)
        {
            _time += dt;
            _blinkTime = Mathf.Max(0f, _blinkTime - dt);
            _waveTime = Mathf.Max(0f, _waveTime - dt);
            if (_head != null) _head.localRotation = _headRest * Quaternion.Euler(
                Mathf.Sin(_time * 0.7f) * 2f + _gaze.y * 8f, _gaze.x * 18f, Mathf.Sin(_time * 0.43f) * 2f);
            if (_spine != null) _spine.localRotation = _spineRest * Quaternion.Euler(Mathf.Sin(_time * 1.4f) * 0.7f, 0f, Mathf.Sin(_time * 0.65f) * 0.8f);
            // Move the imported T-pose arms down; a Wave briefly raises the right arm.
            if (_leftArm != null) _leftArm.localRotation = _leftRest * Quaternion.Euler(-65f, 0f, 0f);
            if (_rightArm != null) _rightArm.localRotation = _rightRest * Quaternion.Euler(_waveTime > 0f ? -15f : -65f,
                _waveTime > 0f ? Mathf.Sin(_time * 9f) * 12f : 0f, 0f);
            if (_face != null && _blink >= 0)
                _face.SetBlendShapeWeight(_blink, _blinkTime > 0f ? Mathf.Sin(_blinkTime / 0.18f * Mathf.PI) * 100f : 0f);
        }

        public void SetTrigger(string name)
        {
            if (string.Equals(name, "Blink", StringComparison.OrdinalIgnoreCase)) _blinkTime = 0.18f;
            if (string.Equals(name, "Wave", StringComparison.OrdinalIgnoreCase)) _waveTime = 2f;
        }
        public void SetBool(string name, bool value) { }
        public void SetFloat(string name, float value) { }
        public void PlayFace(string name) { SetTrigger(name); }
        public void LookAt(Vector3 localPoint) { _gaze = localPoint; }
        public void SetSpeed(float speed) { }
        public void Dispose() { _model.Dispose(); }
    }
}
