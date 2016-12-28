﻿using UnityEngine;

namespace Skinner
{
    /// Emits trail lines from a given Skinner source.
    [AddComponentMenu("Skinner/Skinner Trail")]
    [RequireComponent(typeof(MeshRenderer))]
    public class SkinnerTrail : MonoBehaviour
    {
        #region Public properties

        /// Reference to an effect source.
        public SkinnerSource source {
            get { return _source; }
            set { _source = value; _reconfigured = true; }
        }

        [SerializeField]
        [Tooltip("Reference to an effect source.")]
        SkinnerSource _source;

        /// Reference to a template object used for rendering trail lines.
        public SkinnerTrailTemplate template {
            get { return _template; }
            set { _template = value; _reconfigured = true; }
        }

        [SerializeField]
        [Tooltip("Reference to a template object used for rendering trail lines.")]
        SkinnerTrailTemplate _template;

        /// Limits an amount of a vertex movement. This only affects changes
        /// in vertex positions (doesn't change velocity vectors).
        public float speedLimit {
            get { return _speedLimit; }
            set { _speedLimit = value; }
        }

        [SerializeField]
        [Tooltip("Limits an amount of a vertex movement. This only affects changes " +
                 "in vertex positions (doesn't change velocity vectors).")]
        float _speedLimit = 0.4f;

        /// Drag coefficient (damping coefficient).
        public float drag {
            get { return _drag; }
            set { _drag = value; }
        }

        [SerializeField]
        [Tooltip("Drag coefficient (damping coefficient).")]
        float _drag = 5;

        /// Determines the random number sequence used for the effect.
        public int randomSeed {
            get { return _randomSeed; }
            set { _randomSeed = value; _reconfigured = true; }
        }

        [SerializeField]
        [Tooltip("Determines the random number sequence used for the effect.")]
        int _randomSeed = 0;

        #endregion

        #region Reconfiguration detection

        // Indicates changes in the configuration.
        // (temporary objects have to be reset)
        bool _reconfigured;

        #if UNITY_EDITOR

        /// Notify changes in the configuration.
        /// This method is only available from Editor.
        public void UpdateConfiguration()
        {
            _reconfigured = true;
        }

        #endif

        #endregion

        #region Built-in assets

        [SerializeField] Shader _kernelShader;
        [SerializeField] Material _defaultMaterial;

        #endregion

        #region Animation kernels management

        enum Kernels {
            InitializePosition, InitializeVelocity, InitializeOrthnorm,
            UpdatePosition, UpdateVelocity, UpdateOrthnorm
        }

        enum Buffers { Position, Velocity, Orthnorm }

        AnimationKernelSet<Kernels, Buffers> _kernel;

        void InvokeAnimationKernels()
        {
            if (_kernel == null)
                _kernel = new AnimationKernelSet<Kernels, Buffers>(_kernelShader);

            if (!_kernel.ready)
            {
                // Initialize the animation kernels and buffers.
                _kernel.Setup(_source.vertexCount, _template.historyLength);
                _kernel.material.SetTexture("_SourcePositionBuffer1", _source.positionBuffer);
                _kernel.material.SetFloat("_RandomSeed", _randomSeed);
                _kernel.Invoke(Kernels.InitializePosition, Buffers.Position);
                _kernel.Invoke(Kernels.InitializeVelocity, Buffers.Velocity);
                _kernel.Invoke(Kernels.InitializeOrthnorm, Buffers.Orthnorm);
            }
            else
            {
                // Transfer the source position attributes.
                _kernel.material.SetTexture("_SourcePositionBuffer0", _source.previousPositionBuffer);
                _kernel.material.SetTexture("_SourcePositionBuffer1", _source.positionBuffer);

                // Invoke the velocity update kernel.
                _kernel.material.SetTexture("_PositionBuffer", _kernel.GetLastBuffer(Buffers.Position));
                _kernel.material.SetTexture("_VelocityBuffer", _kernel.GetLastBuffer(Buffers.Velocity));
                _kernel.material.SetFloat("_SpeedLimit", _speedLimit);
                _kernel.Invoke(Kernels.UpdateVelocity, Buffers.Velocity);

                // Invoke the position update kernel with the updated velocity.
                _kernel.material.SetTexture("_VelocityBuffer", _kernel.GetWorkingBuffer(Buffers.Velocity));
                _kernel.material.SetFloat("_Drag", Mathf.Exp(-_drag * Time.deltaTime));
                _kernel.Invoke(Kernels.UpdatePosition, Buffers.Position);

                // Invoke the orthonormal update kernel with the updated velocity.
                _kernel.material.SetTexture("_PositionBuffer", _kernel.GetWorkingBuffer(Buffers.Position));
                _kernel.material.SetTexture("_OrthnormBuffer", _kernel.GetLastBuffer(Buffers.Orthnorm));
                _kernel.Invoke(Kernels.UpdateOrthnorm, Buffers.Orthnorm);
            }

            _kernel.SwapBuffers();
        }

        #endregion

        #region External renderer control

        RendererAdapter _renderer;

        void UpdateRenderer()
        {
            if (_renderer == null)
                _renderer = new RendererAdapter(gameObject, _defaultMaterial);

            // Update the custom property block.
            var block = _renderer.propertyBlock;
            block.SetTexture("_PositionBuffer", _kernel.GetLastBuffer(Buffers.Position));
            block.SetTexture("_VelocityBuffer", _kernel.GetLastBuffer(Buffers.Velocity));
            block.SetTexture("_OrthnormBuffer", _kernel.GetLastBuffer(Buffers.Orthnorm));
            block.SetFloat("_RandomSeed", _randomSeed);

            _renderer.Update(_template.mesh);
        }

        #endregion

        #region MonoBehaviour functions

        void Reset()
        {
            _reconfigured = true;
        }

        void OnDestroy()
        {
            _kernel.Release();
        }

        void LateUpdate()
        {
            // Do nothing if the source is not ready.
            if (_source == null || !_source.isReady) return;

            // Reset the animation kernels on reconfiguration.
            if (_reconfigured)
            {
                if (_kernel != null) _kernel.Release();
                _reconfigured = false;
            }

            // Invoke the animation kernels and update the renderer.
            InvokeAnimationKernels();
            UpdateRenderer();
        }

        #endregion
    }
}
