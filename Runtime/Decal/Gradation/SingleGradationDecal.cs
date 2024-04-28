using UnityEngine;
using System.Collections.Generic;
using net.rs64.TexTransTool.IslandSelector;
using System;
using JetBrains.Annotations;
using Unity.Collections;
using net.rs64.TexTransCore.Decal;
using Unity.Jobs;
using Unity.Burst;
using net.rs64.TexTransCore.TransTextureCore;
using System.Linq;
using net.rs64.TexTransCore.BlendTexture;
using net.rs64.TexTransTool.Utils;
using net.rs64.TexTransCore.TransTextureCore.Utils;

namespace net.rs64.TexTransTool.Decal
{
    [AddComponentMenu(TexTransBehavior.TTTName + "/" + MenuPath)]
    public sealed class SingleGradationDecal : TexTransRuntimeBehavior
    {
        internal const string ComponentName = "TTT SingleGradationDecal";
        internal const string MenuPath = ComponentName;

        internal override List<Renderer> GetRenderers => null;
        internal override bool IsPossibleApply => true;
        internal override TexTransPhase PhaseDefine => TexTransPhase.AfterUVModification;

        public List<Material> TargetMaterials;
        public Gradient Gradient;
        public AbstractIslandSelector IslandSelector;
        [BlendTypeKey] public string BlendTypeKey = TextureBlend.BL_KEY_DEFAULT;
        public PropertyName TargetPropertyName = PropertyName.DefaultValue;

        internal override void Apply([NotNull] IDomain domain)
        {
            var targetMat = GetTargetMaterials(domain.OriginEqual, domain.EnumerateRenderer());
            var gradTex = GradientToTextureWithTemp(Gradient);
            var space = new SingleGradientSpace(transform.worldToLocalMatrix);
            var filter = new IslandSelectFilter(IslandSelector);

            var decalContext = new DecalContext<SingleGradientSpace, IslandSelectFilter, Vector2>(space, filter);
            decalContext.TargetPropertyName = TargetPropertyName;
            decalContext.TextureWarp = TextureWrap.NotWrap;
            decalContext.NotContainsKeyAutoGenerate = false;

            var writeable = new Dictionary<Material, RenderTexture>();
            decalContext.GenerateKey(writeable, targetMat);

            foreach (var renderer in domain.EnumerateRenderer())
            {
                if (!renderer.sharedMaterials.Any(mat => targetMat.Contains(mat))) { continue; }
                decalContext.WriteDecalTexture(writeable, renderer, gradTex);
            }

            foreach (var m2rt in writeable) { domain.AddTextureStack(m2rt.Key.GetTexture(TargetPropertyName), new TextureBlend.BlendTexturePair(m2rt.Value, BlendTypeKey)); }
        }

        private HashSet<Material> GetTargetMaterials(Func<UnityEngine.Object, UnityEngine.Object, bool> originEqual, IEnumerable<Renderer> domainRenderers)
        {
            return RendererUtility.GetMaterials(domainRenderers).Where(m => TargetMaterials.Any(tm => originEqual.Invoke(m, tm))).ToHashSet();
        }

        internal static Texture2D s_GradientTempTexture;
        internal static Texture2D GradientToTextureWithTemp(Gradient gradient)
        {
            if (s_GradientTempTexture == null)
            {
                s_GradientTempTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                s_GradientTempTexture.alphaIsTransparency = true;
            }


            using (var colorArray = new NativeArray<Color32>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
            {
                var writeSpan = colorArray.AsSpan();
                for (var i = 0; colorArray.Length > i; i += 1) { writeSpan[i] = gradient.Evaluate(i / 255f); }

                s_GradientTempTexture.LoadRawTextureData(colorArray);
            }
            s_GradientTempTexture.Apply(true);

            return s_GradientTempTexture;
        }

        internal override IEnumerable<UnityEngine.Object> GetDependency(IDomain domain)
        {
            IEnumerable<UnityEngine.Object> depend = new UnityEngine.Object[] { transform };
            if (IslandSelector != null) { depend = depend.Concat(IslandSelector.GetDependency()); }
            var materials = GetTargetMaterials(domain.OriginEqual, domain.EnumerateRenderer());
            var dependRenderer = domain.EnumerateRenderer().Where(x => x.sharedMaterials.Any(m => materials.Contains(m)));

            return depend.Concat(transform.GetParents().Select(i => i as UnityEngine.Object))
            .Concat(dependRenderer)
            .Concat(dependRenderer.Select(x => x.transform))
            .Concat(dependRenderer.Select(x => x.GetMesh()))
            .Concat(dependRenderer.Where(x => x is SkinnedMeshRenderer).Cast<SkinnedMeshRenderer>().SelectMany(x => x.bones));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.black;
            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.DrawLine(Vector3.zero, Vector3.up);
            IslandSelector?.OnDrawGizmosSelected();
        }

        internal override int GetDependencyHash(IDomain domain)
        {
            var hash = 0;
            foreach (var mat in TargetMaterials) { hash ^= mat?.GetInstanceID() ?? 0; }
            return hash;
        }
    }

    internal class SingleGradientSpace : IConvertSpace<Vector2>
    {
        Matrix4x4 _world2LocalMatrix;
        MeshData _meshData;

        JobResult<NativeArray<Vector2>> _uv;

        internal MeshData MeshData => _meshData;
        internal JobResult<NativeArray<Vector2>> UV => _uv;

        public SingleGradientSpace(Matrix4x4 w2l)
        {
            _world2LocalMatrix = w2l;
        }
        public void Input(MeshData meshData)
        {
            _meshData = meshData;
            var uvNa = new NativeArray<Vector2>(_meshData.Vertices.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var convertJob = new ConvertJob()
            {
                World2Local = _world2LocalMatrix,
                worldVerticals = _meshData.Vertices,
                uv = uvNa
            };

            _uv = new(uvNa, convertJob.Schedule(uvNa.Length, 32));
        }

        public NativeArray<Vector2> OutPutUV()
        {
            if (_uv == null) { return default; }
            return _uv.GetResult;
        }
        public void Dispose()
        {
            _meshData = null;
            _uv.GetResult.Dispose();
            _uv = null;
        }

        [BurstCompile]
        struct ConvertJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Vector3> worldVerticals;
            [ReadOnly] public Matrix4x4 World2Local;
            [WriteOnly] public NativeArray<Vector2> uv;
            public void Execute(int index) { uv[index] = new Vector2(World2Local.MultiplyPoint3x4(worldVerticals[index]).y, 0.5f); }
        }

    }

    internal class IslandSelectFilter : ITrianglesFilter<SingleGradientSpace>
    {
        public IIslandSelector IslandSelector;
        MeshData _meshData;
        NativeArray<TriangleIndex>[] _islandSelectedTriangles;

        public IslandSelectFilter(IIslandSelector islandSelector)
        {
            IslandSelector = islandSelector;
        }

        public void SetSpace(SingleGradientSpace space)
        {
            _meshData = space.MeshData;
            _islandSelectedTriangles = new NativeArray<TriangleIndex>[space.MeshData.TriangleIndex.Length];
        }

        NativeArray<TriangleIndex> ITrianglesFilter<SingleGradientSpace>.GetFilteredSubTriangle(int subMeshIndex)
        {
            if (_meshData == null) { return default; }
            if (_islandSelectedTriangles[subMeshIndex].IsCreated) { return _islandSelectedTriangles[subMeshIndex]; }
            var islandSelected = _islandSelectedTriangles[subMeshIndex] = IslandSelectToPPFilter.IslandSelectExecute(IslandSelector, _meshData, subMeshIndex);
            return islandSelected;
        }
        public void Dispose()
        {
            _meshData = null;
            foreach (var na in _islandSelectedTriangles) { na.Dispose(); }
            _islandSelectedTriangles = null;
        }

    }

}
