using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using GraphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat;
using System;
using System.Collections.Generic;

[Serializable, VolumeComponentMenu("Post-processing/Custom/HDRP Lens Flares/Radial Warps and Ghosts")]
public sealed class RadialWarpAndGhosts : CustomPostProcessVolumeComponent, IPostProcessComponent
{
    #region Effect parameters

    [Tooltip("Controls the intensity of the effect.")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Controls the blur when scaling up the different effect levels.")]
    public ClampedFloatParameter blur = new ClampedFloatParameter(0.5f, 0f, 1f);
    [Tooltip("LUT-Texture for the chromatic aberration effect.")]
    public TextureParameter spectralLut = new TextureParameter(null);
    [Header("Radial Warp and Ghosts")]
    public ClampedFloatParameter radialWarpIntensity = new ClampedFloatParameter(0.0025f, 0f, 1f);
    public ClampedFloatParameter radialWarpLength = new ClampedFloatParameter(0.5f, 0f, 1f);
    [Tooltip("Multiplier for the radial warp when mixing radial warp and ghosts together.")]
    public ClampedFloatParameter chromaticAberrationMultiplier = new ClampedFloatParameter(3.0f, 0f, 20f);
    public ClampedFloatParameter ghostIntensity = new ClampedFloatParameter(0.005f, 0f, 1f);
    [Header("Downsampling")]
    [Tooltip("Scaling factor of each sampling step.")]
    public ClampedFloatParameter factor = new ClampedFloatParameter(2f, 1f, 4f);
    [Tooltip("Controls the amount of downsampling steps.")]
    public ClampedIntParameter levels = new ClampedIntParameter(1, 1, 4);


    #endregion

    #region Private members

    const string kShaderName = "Hidden/Shader/LensFlares/RadialWarpAndGhosts";
    static class ShaderIDs {
        internal static readonly int SourceTexture = Shader.PropertyToID("_SourceTexture");
        internal static readonly int InputTexture = Shader.PropertyToID("_InputTexture");
        internal static readonly int AddTexture = Shader.PropertyToID("_AddTexture");
        internal static readonly int ChromaticAberration_Spectrum = Shader.PropertyToID("_ChromaticAberration_Spectrum");
        internal static readonly int Delta = Shader.PropertyToID("_Delta");
        internal static readonly int Intensity = Shader.PropertyToID("_Intensity");
        internal static readonly int GhostIntensity = Shader.PropertyToID("_GhostIntensity");
        internal static readonly int RadialWarpIntensity = Shader.PropertyToID("_RadialWarpIntensity");
        internal static readonly int RadialWarpLength = Shader.PropertyToID("_RadialWarpLength");
        internal static readonly int AddMultiplier = Shader.PropertyToID("_AddMultiplier");
    }
    const int PREFILTER_PASS = 0;
    const int HBLUR_PASS = 1;
    const int VBLUR_PASS = 2;
    const int RADIALWARP_PASS = 3;
    const int GHOST_PASS = 4;
    const int CHROMATIC_ABERRATION_PASS = 5;
    const int BOX_PASS = 6;
    const int COMPOSITION_PASS = 7;

    Material _material;
    MaterialPropertyBlock _prop;
    // Image pyramid storage
    // Use different downsample pyramids for each camera. Store them with the camera GUIDs.
    Dictionary<int, FlarePyramid> _pyramids;

    FlarePyramid GetPyramid(HDCamera camera) {
        FlarePyramid candid;
        var cameraID = camera.camera.GetInstanceID();

        if (_pyramids.TryGetValue(cameraID, out candid)) {
            // Reallocate the RTs when the screen size was changed or factor or levels.
            if (!candid.SizeValid(camera, levels.value + 1, factor.value)) {
                candid.Reallocate(camera, levels.value + 1, factor.value);
            }
        } else {
            // None found: Allocate a new pyramid.
            _pyramids[cameraID] = candid = new FlarePyramid(camera, levels.value + 1, factor.value);
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
        if (ghostIntensity.value == 0f && (radialWarpIntensity == 0f || chromaticAberrationMultiplier == 0f)) {
            // Disable if both ghost and radial warp are disabled.
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
        _pyramids = new Dictionary<int, FlarePyramid>();
    }
      
    public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle srcRT, RTHandle dstRT)
    {
        var pyramid = GetPyramid(camera);
        int MAX_DOWNSAMPLE = pyramid.max_downsample;

        _material.SetFloat(ShaderIDs.Intensity, intensity.value);
        _material.SetTexture(ShaderIDs.SourceTexture, srcRT);
        _material.SetTexture(ShaderIDs.ChromaticAberration_Spectrum, spectralLut.value);
        _material.SetFloat(ShaderIDs.RadialWarpLength, radialWarpLength.value);
        _material.SetFloat(ShaderIDs.RadialWarpIntensity, radialWarpIntensity.value);
        _material.SetFloat(ShaderIDs.GhostIntensity, ghostIntensity.value);

        // Source -> Prefilter -> vblur[0]
        HDUtils.DrawFullScreen(cmd, _material, pyramid[0].vblur, _prop, PREFILTER_PASS);

        // {H,V}Blur down by MAX_DOWNSAMPLE levels
        for (int i = 1; i <= pyramid.max_downsample; i++) {
            // vblur[i-1] -> hblur -> hblur[i]
            _prop.SetTexture(ShaderIDs.InputTexture, pyramid[i - 1].vblur);
            HDUtils.DrawFullScreen(cmd, _material, pyramid[i].hblur, _prop, HBLUR_PASS);
            // hblur[i] -> vblur -> vblur[i]
            _prop.SetTexture(ShaderIDs.InputTexture, pyramid[i].hblur);
            HDUtils.DrawFullScreen(cmd, _material, pyramid[i].vblur, _prop, HBLUR_PASS);
        }

        var downsample = pyramid[MAX_DOWNSAMPLE].vblur;

        // downsample -> radial warp -> radialWarped
        _prop.SetTexture(ShaderIDs.InputTexture, downsample);
        HDUtils.DrawFullScreen(cmd, _material, pyramid.radialWarped, _prop, RADIALWARP_PASS);
        
        // downsample -> ghost -> ghosts
        _prop.SetTexture(ShaderIDs.InputTexture, downsample);
        HDUtils.DrawFullScreen(cmd, _material, pyramid.ghosts, _prop, GHOST_PASS);

        // (ghosts+radialWarped*4) -> chromatic aberration-> aberration
        _prop.SetTexture(ShaderIDs.InputTexture, pyramid.ghosts);
        _prop.SetTexture(ShaderIDs.AddTexture, pyramid.radialWarped);
        _prop.SetFloat(ShaderIDs.AddMultiplier, chromaticAberrationMultiplier.value);
        HDUtils.DrawFullScreen(cmd, _material, pyramid.aberration, _prop, CHROMATIC_ABERRATION_PASS);

        // aberration -> hblur -> hblur[MAX_DOWNSAMPLE]
        _prop.SetTexture(ShaderIDs.InputTexture, pyramid.aberration);
        HDUtils.DrawFullScreen(cmd, _material, pyramid[MAX_DOWNSAMPLE].hblur, _prop, HBLUR_PASS);

        // hblur[MAX_DOWNSAMPLE] -> vblur -> vblur[MAX_DOWNSAMPLE]
        _prop.SetTexture(ShaderIDs.InputTexture, pyramid[MAX_DOWNSAMPLE].hblur);
        HDUtils.DrawFullScreen(cmd, _material, pyramid[MAX_DOWNSAMPLE].vblur, _prop, VBLUR_PASS);

        // Box up by MAX_DOWNSAMPLE levels, using vblur[i] for each stage
        _prop.SetFloat(ShaderIDs.Delta, blur.value);
        for (var i = MAX_DOWNSAMPLE-1; i >= 0; i--) {
            // vblur[i+1] -> box -> vblur[i]
            _prop.SetTexture(ShaderIDs.InputTexture, pyramid[i + 1].vblur);
            HDUtils.DrawFullScreen(cmd, _material, pyramid[i].vblur, _prop, BOX_PASS);
        }

        // Again {H,V} Blur level 0
        // vblur[0] -> hblur -> hblur[0]
        _prop.SetTexture(ShaderIDs.InputTexture, pyramid[0].vblur);
        HDUtils.DrawFullScreen(cmd, _material, pyramid[0].hblur, _prop, HBLUR_PASS);

        // hblur[0] -> vblur -> vblur[0]
        _prop.SetTexture(ShaderIDs.InputTexture, pyramid[0].hblur);
        HDUtils.DrawFullScreen(cmd, _material, pyramid[0].vblur, _prop, VBLUR_PASS);

        // (Source,vblur[0]) -> Composition -> Destination
        _prop.SetFloat(ShaderIDs.AddMultiplier, intensity.value);
        _prop.SetTexture(ShaderIDs.InputTexture, pyramid[0].vblur);
        HDUtils.DrawFullScreen(cmd, _material, dstRT, _prop, COMPOSITION_PASS);
    }

    public override void Cleanup()
    {
        CoreUtils.Destroy(_material);
        foreach(var pyramid in _pyramids.Values) {
            pyramid.Release();
        }
    }
    #endregion

    #region Image pyramid class used in Flare effect
    sealed class FlarePyramid {
        const GraphicsFormat QUALITY = GraphicsFormat.R32G32B32A32_SFloat;
        const GraphicsFormat OPTIMAL = GraphicsFormat.R16G16B16A16_SFloat;
        const GraphicsFormat FAST = GraphicsFormat.B10G11R11_UFloatPack32;
        const GraphicsFormat RTFormat = FAST;

        int _baseWidth, _baseHeight, _blurLevels;
        float _factor;

        public RTHandle radialWarped;
        public RTHandle ghosts;
        public RTHandle aberration;
        (RTHandle hblur, RTHandle vblur)[] _blurs;

        public int max_downsample { get { return _blurLevels-1; } }

        public (RTHandle hblur, RTHandle vblur) this[int index] {
            get { return _blurs[index]; }
        }

        public FlarePyramid(HDCamera camera, int blurLevels, float factor) {
            Allocate(camera, blurLevels, factor);
        }

        public bool SizeValid(HDCamera camera, int blurLevels, float factor) {
            return _baseHeight == camera.actualHeight && _baseWidth == camera.actualWidth && _blurLevels == blurLevels && _factor == factor;
        }

        public void Reallocate(HDCamera camera, int blurLevels, float factor) {
            Release();
            Allocate(camera, blurLevels, factor);
        }

        public void Release() {
            if (radialWarped != null) {
                RTHandles.Release(radialWarped);
            }
            if (ghosts != null) {
                RTHandles.Release(ghosts);
            }
            if (aberration != null) {
                RTHandles.Release(aberration);
            }
            foreach(var blur in _blurs) {
                if (blur.hblur != null) {
                    RTHandles.Release(blur.hblur);
                }
                if (blur.vblur != null) {
                    RTHandles.Release(blur.vblur);
                }
            }
        }

        void Allocate(HDCamera camera, int blurLevels, float factor) {
            _blurLevels = blurLevels;
            _factor = factor;
            _baseHeight = camera.actualHeight;
            _baseWidth = camera.actualWidth;

            _blurs = new (RTHandle, RTHandle)[blurLevels];

            var width = _baseWidth;
            var height = _baseHeight;

            var downsampledWidth = _baseWidth;
            var downsampledHeight = _baseHeight;

            _blurs[0] = (RTHandles.Alloc(width, height, colorFormat: RTFormat), RTHandles.Alloc(width, height, colorFormat: RTFormat));
            for (var i = 1; i < _blurs.Length; i++) {
                width = (int)((float)width / factor);
                height = (int)((float)height / factor);
                if (width < 4 || height < 4) {
                    // Texture would be to small to have an effect. Limit blurLevels.
                    _blurLevels = Math.Min(_blurLevels, i);
                    _blurs[i] = (null, null);
                } else {
                    downsampledWidth = width;
                    downsampledHeight = height;
                    _blurs[i] = (RTHandles.Alloc(width, height, colorFormat: RTFormat), RTHandles.Alloc(width, height, colorFormat: RTFormat));
                }
            }

            radialWarped = RTHandles.Alloc(downsampledWidth, downsampledHeight, colorFormat: RTFormat);
            ghosts = RTHandles.Alloc(downsampledWidth, downsampledHeight, colorFormat: RTFormat);
            aberration = RTHandles.Alloc(downsampledWidth, downsampledHeight, colorFormat: RTFormat);
        }
    }
    #endregion
}
