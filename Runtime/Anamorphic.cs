using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using GraphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat;
using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using SecretLab.Utilities;

namespace SecretLab.PostProcessing {

    [Serializable, VolumeComponentMenu("Post-processing/Custom/HDRP Lens Flares/Anamorphic")]
    public sealed class Anamorphic : CustomPostProcessVolumeComponent, IPostProcessComponent {
        #region Effect parameters

        [Tooltip("Controls the intensity of the effect.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Defines the threshold for anamorphic streaks.")]
        public ClampedFloatParameter threshold = new ClampedFloatParameter(1f, 0f, 10f);
        [Tooltip("Direction of the effect.")]
        public ClampedFloatParameter direction = new ClampedFloatParameter(0f, 0f, 180f);
        [Tooltip("Defines how stretched the anamorphic light streak is.")]
        public ClampedFloatParameter stretch = new ClampedFloatParameter(1f, 0f, 1f);
        [Tooltip("If enabled the anamorphic light streak will be blended with light streaks from old frame, i.e. faded out.")]
        public ClampedFloatParameter fade = new ClampedFloatParameter(0f, 0f, 0.3f);
        public ColorParameter tint = new ColorParameter(Color.white);

        #endregion

        #region Private members

        const string kShaderName = "Hidden/Shader/LensFlares/Anamorphic";
        static class ShaderIDs {
            internal static readonly int SourceTexture = Shader.PropertyToID("_SourceTexture");
            internal static readonly int InputTexture = Shader.PropertyToID("_InputTexture");
            internal static readonly int OtherTexture = Shader.PropertyToID("_OtherTexture");
            internal static readonly int Intensity = Shader.PropertyToID("_Intensity");
            internal static readonly int Threshold = Shader.PropertyToID("_Threshold");
            internal static readonly int Stretch = Shader.PropertyToID("_Stretch");
            internal static readonly int Color = Shader.PropertyToID("_Color");
            internal static readonly int Angle = Shader.PropertyToID("_Angle");
            internal static readonly int Fade = Shader.PropertyToID("_Fade");
            internal static readonly int AngleTextureScale = Shader.PropertyToID("_AngleTextureScale");
        }
        const int PREFILTER_PASS = 0;
        const int DOWNSAMPLE_PASS = 1;
        const int UPSAMPLE_PASS = 2;
        const int FADE_PASS = 3;
        const int COMPOSITION_PASS = 4;
        const int FILLBLACK_PASS = 5;
        const int COPY_PASS = 6;

        Material _material;
        MaterialPropertyBlock _prop;
        // Image pyramid storage
        // Use different downsample pyramids for each camera. Store them with the camera GUIDs.
        Dictionary<int, AnamorphicPyramid> _pyramids;

        AnamorphicPyramid GetPyramid(HDCamera camera) {
            AnamorphicPyramid candid;
            var cameraID = camera.camera.GetInstanceID();

            if (_pyramids.TryGetValue(cameraID, out candid)) {
                // Reallocate the RTs when the screen size was changed or direction of the effect.
                if (!candid.SizeValid(camera, direction.value)) {
                    candid.Reallocate(camera, direction.value);
                }
            } else {
                // None found: Allocate a new pyramid.
                _pyramids[cameraID] = candid = new AnamorphicPyramid(camera, direction.value);
            }

            return candid;
        }

        #endregion

        #region Post Process implementation
        public bool IsActive() {
            if (_material == null || intensity.value == 0f) {
                // Disable if material is missing or the effect intensity is zero.
                return false;
            }
            return true;
        }

        // Do not post-process the scene view
        public override bool visibleInSceneView => false;

        // Do not forget to add this post process in the Custom Post Process Orders list (Project Settings > HDRP Default Settings).
        //public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.AfterPostProcess;
        public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.BeforePostProcess;

        public override void Setup() {
            _material = CoreUtils.CreateEngineMaterial(kShaderName);
            _prop = new MaterialPropertyBlock();
            _pyramids = new Dictionary<int, AnamorphicPyramid>();
        }

        public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle srcRT, RTHandle dstRT) {
            var pyramid = GetPyramid(camera);

            _material.SetTexture(ShaderIDs.SourceTexture, srcRT);
            _material.SetFloat(ShaderIDs.Intensity, intensity.value);
            _material.SetFloat(ShaderIDs.Threshold, threshold.value);
            _material.SetFloat(ShaderIDs.Stretch, stretch.value);
            _material.SetFloat(ShaderIDs.Angle, pyramid.angle);
            _material.SetVector(ShaderIDs.AngleTextureScale, pyramid.textureScale);
            _material.SetColor(ShaderIDs.Color, tint.value);
            _material.SetFloat(ShaderIDs.Fade, fade.value);

            if (pyramid.needsClear) {
                HDUtils.DrawFullScreen(cmd, _material, pyramid.fadeIn, _prop, FILLBLACK_PASS);
            } else if (fade.value>0 && Application.isPlaying) {
                _prop.SetTexture(ShaderIDs.InputTexture, pyramid.fadeOut);
                HDUtils.DrawFullScreen(cmd, _material, pyramid.fadeIn, _prop, COPY_PASS);
            }

            // Source -> Prefilter -> down]
            HDUtils.DrawFullScreen(cmd, _material, pyramid[0].down, _prop, PREFILTER_PASS);

            // Do the downsampling
            var level = 1;
            for (; level < AnamorphicPyramid.MaxMipLevel && pyramid[level].down != null; level++) {
                _prop.SetTexture(ShaderIDs.InputTexture, pyramid[level - 1].down);
                HDUtils.DrawFullScreen(cmd, _material, pyramid[level].down, _prop, DOWNSAMPLE_PASS);
            }

            // Do the upsampling
            var lastRT = pyramid[--level].down;
            for (level--; level >= 1; level--) {
                var mip = pyramid[level];
                _prop.SetTexture(ShaderIDs.InputTexture, lastRT);
                _prop.SetTexture(ShaderIDs.OtherTexture, mip.down);
                HDUtils.DrawFullScreen(cmd, _material, mip.up, _prop, UPSAMPLE_PASS);
                lastRT = mip.up;
            }

            if (fade.value > 0 && Application.isPlaying) {
                _prop.SetTexture(ShaderIDs.InputTexture, lastRT);
                _prop.SetTexture(ShaderIDs.OtherTexture, pyramid.fadeIn);
                HDUtils.DrawFullScreen(cmd, _material, pyramid.fadeOut, _prop, FADE_PASS);
                lastRT = pyramid.fadeOut;
            }
            
            // (Source,down[0]) -> Composition -> Destination
            _prop.SetTexture(ShaderIDs.InputTexture, lastRT);
            HDUtils.DrawFullScreen(cmd, _material, dstRT, _prop, COMPOSITION_PASS);
        }

        public override void Cleanup() {
            CoreUtils.Destroy(_material);
            foreach (var pyramid in _pyramids.Values) {
                pyramid.Release();
            }
        }
        private static (float sin, float cos) DirectionSinCos(float degree) {
            return (Mathf.Sin(degree * Mathf.Deg2Rad), Mathf.Cos(degree * Mathf.Deg2Rad));
        }

        #endregion

        #region Image pyramid class used in Flare effect
        sealed class AnamorphicPyramid {
            const GraphicsFormat QUALITY = GraphicsFormat.R32G32B32A32_SFloat;
            const GraphicsFormat OPTIMAL = GraphicsFormat.R16G16B16A16_SFloat;
            const GraphicsFormat FAST = GraphicsFormat.B10G11R11_UFloatPack32;
            const GraphicsFormat RTFormat = FAST;
            public const int MaxMipLevel = 16;

            int _baseWidth, _baseHeight;
            float _degree;
            Matrix2x2 _rotation;
            Vector2 _scale;
            bool _needsClear;


            public Vector2 textureScale { get { return _scale; } }
            public bool needsClear { get { bool r = _needsClear; _needsClear = false; return r; } }

            readonly (RTHandle down, RTHandle up)[] _mips = new (RTHandle,RTHandle)[MaxMipLevel];
            public RTHandle fadeIn, fadeOut;

            public float angle { get { return _degree; } }

            public Matrix2x2 rotation { get { return _rotation; } }

            public (RTHandle down, RTHandle up) this[int index] {
                get { return _mips[index]; }
            }

            public AnamorphicPyramid(HDCamera camera, float degree) {
                Allocate(camera, degree);
            }

            public bool SizeValid(HDCamera camera, float degree) {
                return _baseHeight == camera.actualHeight && _baseWidth == camera.actualWidth && _degree == degree;
            }

            public void Reallocate(HDCamera camera, float degree) {
                Release();
                Allocate(camera, degree);
            }

            public void Release() {
                foreach (var mip in _mips) {
                    if (mip.up != null) {
                        RTHandles.Release(mip.up);
                    }
                    if (mip.down != null) {
                        RTHandles.Release(mip.down);
                    }
                }
                if (fadeIn != null) {
                    RTHandles.Release(fadeIn);
                }
                if (fadeOut != null) {
                    RTHandles.Release(fadeOut);
                }
            }

            void Allocate(HDCamera camera, float degree) {
                _degree = degree;
                _baseHeight = camera.actualHeight;
                _baseWidth = camera.actualWidth;
                _rotation = Matrix2x2.Rotation(-degree);

                var width = _baseWidth;
                var height = _baseHeight;

                // Normal rectangle bounds for non-rotated anamorphic light streaks
                Vector2[] p = new Vector2[4];
                p[0] = new Vector2(-0.5f, -0.5f);
                p[1] = new Vector2(0.5f, -0.5f);
                p[2] = new Vector2(-0.5f, 0.5f);
                p[3] = new Vector2(0.5f, 0.5f);

                Vector2 pMin = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 pMax = new Vector2(float.MinValue, float.MinValue);

                // Rotate bounding box and set dimension of rotated rectangle
                foreach (var p_i in p) {
                    var i = _rotation * p_i;
                    pMin.x = Mathf.Min(pMin.x, i.x);
                    pMin.y = Mathf.Min(pMin.y, i.y);
                    pMax.x = Mathf.Max(pMax.x, i.x);
                    pMax.y = Mathf.Max(pMax.y, i.y);
                }

                width = (int)Mathf.Ceil(((pMax.x - pMin.x)*width))+16;
                height = (int)Mathf.Ceil(((pMax.y - pMin.y)*height))+16;
                _scale = new Vector4((float)width / (float)_baseWidth, (float)height / (float)_baseHeight);
                height /= 2;

                // First level do not feature an up-sampling texture
                _mips[0] = (RTHandles.Alloc(width, height, colorFormat: RTFormat), null);

                for (var i = 1; i < MaxMipLevel; i++) {
                    // Divide width by 2
                    width /= 2;
                    //Debug.Log("Level "+i+": " + width + "x" + height);
                    if (width < 4 || height < 4) {
                        // Texture would be to small
                        _mips[i] = (null, null);
                    } else {
                        _mips[i] = (RTHandles.Alloc(width, height, colorFormat: RTFormat), RTHandles.Alloc(width, height, colorFormat: RTFormat));
                    }
                    if (i == 1) {
                        fadeIn = RTHandles.Alloc(width, height, colorFormat: RTFormat);
                        fadeOut = RTHandles.Alloc(width, height, colorFormat: RTFormat);
                        _needsClear = true;
                    }
                }
            }
        }
        #endregion
    }

}