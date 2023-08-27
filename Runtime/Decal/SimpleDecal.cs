#if UNITY_EDITOR
using System.Diagnostics.SymbolStore;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.Serialization;
using net.rs64.TexTransTool.Island;

namespace net.rs64.TexTransTool.Decal
{
    [AddComponentMenu("TexTransTool/SimpleDecal")]
    [ExecuteInEditMode]
    public class SimpleDecal : AbstructSingleDecal<ParallelProjectionSpase>
    {
        public Vector2 Scale = Vector2.one;
        public float MaxDistans = 1;
        public bool FixedAspect = true;
        [FormerlySerializedAs("SideChek")] public bool SideCulling = true;
        [FormerlySerializedAs("PolygonCaling")] public PolygonCulling PolygonCulling = PolygonCulling.Vartex;

        public override ParallelProjectionSpase GetSpaseConverter => new ParallelProjectionSpase(transform.worldToLocalMatrix);
        public override DecalUtil.ITrianglesFilter<ParallelProjectionSpase> GetTriangleFilter
        {
            get
            {
                if (IslandCulling) { return new IslandCullingPPFilter(GetFilter(), GetIslandSelecotor()); }
                else { return new ParallelProjectionFilter(GetFilter()); }
            }
        }

        public bool IslandCulling = false;
        public Vector2 IslandSelectorPos = new Vector2(0.5f, 0.5f);
        public float IslandSelectorRange = 1;
        public override void ScaleApply()
        {
            ScaleApply(new Vector3(Scale.x, Scale.y, MaxDistans), FixedAspect);
        }
        public List<TrainagelFilterUtility.ITriangleFiltaring<List<Vector3>>> GetFilter()
        {
            var Filters = new List<TrainagelFilterUtility.ITriangleFiltaring<List<Vector3>>>
            {
                new TrainagelFilterUtility.FarStruct(1, false),
                new TrainagelFilterUtility.NearStruct(0, true)
            };
            if (SideCulling) Filters.Add(new TrainagelFilterUtility.SideStruct());
            Filters.Add(new TrainagelFilterUtility.OutOfPorigonStruct(PolygonCulling, 0, 1, true));

            return Filters;
        }

        public List<IslandSelector> GetIslandSelecotor()
        {
            if (!IslandCulling) return null;
            return new List<IslandSelector>() {
                new IslandSelector(new Ray(transform.localToWorldMatrix.MultiplyPoint3x4(IslandSelectorPos - new Vector2(0.5f, 0.5f)), transform.forward), MaxDistans * IslandSelectorRange)
                };
        }


        [NonSerialized] public Material DisplayDecalMat;
        public Color GizmoColoro = new Color(0, 0, 0, 1);
        [NonSerialized] public Mesh Quad;

        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = GizmoColoro;
            var Matrix = transform.localToWorldMatrix;

            Gizmos.matrix = Matrix;

            var CenterPos = Vector3.zero;
            Gizmos.DrawWireCube(CenterPos + new Vector3(0, 0, 0.5f), new Vector3(1, 1, 1));//基準となる四角形

            if (DecalTexture != null)
            {
                if (DisplayDecalMat == null || Quad == null) GizmInstans();
                DisplayDecalMat.SetPass(0);
                Graphics.DrawMeshNow(Quad, Matrix);
            }
            if (IslandCulling)
            {
                Vector3 SelecotOrigin = new Vector2(IslandSelectorPos.x - 0.5f, IslandSelectorPos.y - 0.5f);
                var SelectorTail = (Vector3.forward * IslandSelectorRange) + SelecotOrigin;
                Gizmos.DrawLine(SelecotOrigin, SelectorTail);
            }
        }

        public void GizmInstans()
        {
            DisplayDecalMat = new Material(Shader.Find("Hidden/DisplayDecalTexture"));
            DisplayDecalMat.mainTexture = DecalTexture;
            Quad = AssetDatabase.LoadAllAssetsAtPath("Library/unity default resources").ToList().Find(i => i.name == "Quad") as Mesh;

        }

        [SerializeField] protected bool _IsRealTimePreview = false;
        public bool IsRealTimePreview => _IsRealTimePreview;
        [SerializeField] RenderTexture DecalRendereTexture;
        Dictionary<RenderTexture, RenderTexture> _RealTimePreviewDecalTextureCompile;
        Dictionary<Texture2D, RenderTexture> _RealTimePreviewDecalTextureBlend;

        public List<MatPair> PreViewMaterials = new List<MatPair>();

        public void EnableRealTimePreview()
        {
            if (_IsRealTimePreview) return;
            if (!IsPossibleApply) return;
            _IsRealTimePreview = true;

            PreViewMaterials.Clear();

            _RealTimePreviewDecalTextureCompile = new Dictionary<RenderTexture, RenderTexture>();
            _RealTimePreviewDecalTextureBlend = new Dictionary<Texture2D, RenderTexture>();
            DecalRendereTexture = new RenderTexture(DecalTexture.width, DecalTexture.height, 32, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, -1);


            foreach (var Rendarer in TargetRenderers)
            {
                var Materials = Rendarer.sharedMaterials;
                for (int i = 0; i < Materials.Length; i += 1)
                {
                    if (!Materials[i].HasProperty(TargetPropertyName)) { continue; }
                    if (Materials[i].GetTexture(TargetPropertyName) is RenderTexture) { continue; }
                    if (PreViewMaterials.Any(i2 => i2.Material == Materials[i]))
                    {
                        Materials[i] = PreViewMaterials.Find(i2 => i2.Material == Materials[i]).SecondMaterial;
                    }
                    else
                    {
                        var DistMat = Materials[i];
                        var NewMat = Instantiate<Material>(Materials[i]);
                        var srostex = NewMat.GetTexture(TargetPropertyName);

                        if (srostex is Texture2D tex2d && tex2d != null)
                        {
                            if (!_RealTimePreviewDecalTextureBlend.ContainsKey(tex2d))
                            {
                                var NewTexBlend = new RenderTexture(tex2d.width, tex2d.height, 32, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, -1);
                                var NewTexCompiled = new RenderTexture(tex2d.width, tex2d.height, 32, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, -1);
                                _RealTimePreviewDecalTextureCompile.Add(NewTexBlend, NewTexCompiled);
                                _RealTimePreviewDecalTextureBlend.Add(tex2d, NewTexBlend);
                                NewMat.SetTexture(TargetPropertyName, NewTexBlend);
                            }
                            else
                            {
                                NewMat.SetTexture(TargetPropertyName, _RealTimePreviewDecalTextureBlend[tex2d]);
                            }
                            Materials[i] = NewMat;
                            PreViewMaterials.Add(new MatPair(DistMat, NewMat));
                        }


                    }
                }
                Rendarer.sharedMaterials = Materials;
            }
        }
        public void DisableRealTimePreview()
        {
            if (!_IsRealTimePreview) return;
            _IsRealTimePreview = false;

            foreach (var Rendarer in TargetRenderers)
            {
                var Materials = Rendarer.sharedMaterials;
                for (int i = 0; i < Materials.Length; i += 1)
                {
                    var DistMat = PreViewMaterials.Find(i2 => i2.SecondMaterial == Materials[i]).Material;
                    if (DistMat != null) Materials[i] = DistMat;

                }
                Rendarer.sharedMaterials = Materials;

            }
            PreViewMaterials.Clear();
            _RealTimePreviewDecalTextureBlend = null;
            _RealTimePreviewDecalTextureCompile = null;
            DecalRendereTexture = null;
        }

        public void UpdateRealTimePreview()
        {
            if (!_IsRealTimePreview) return;

            DecalRendereTexture.Release();
            TextureLayerUtil.MuldRenderTexture(DecalRendereTexture, DecalTexture, Color);

            foreach (var rt in _RealTimePreviewDecalTextureCompile)
            {
                rt.Value.Release();
            }

            foreach (var render in TargetRenderers)
            {
                DecalUtil.CreatDecalTexture(render, _RealTimePreviewDecalTextureCompile, DecalRendereTexture, GetSpaseConverter, GetTriangleFilter, TargetPropertyName, GetOutRangeTexture, Pading);
            }
            foreach (var Stex in _RealTimePreviewDecalTextureBlend.Keys)
            {
                var BlendRT = _RealTimePreviewDecalTextureBlend[Stex];
                var CompoledRT = _RealTimePreviewDecalTextureCompile[BlendRT];
                BlendRT.Release();
                Graphics.Blit(Stex, BlendRT);
                TextureLayerUtil.BlendBlit(BlendRT, CompoledRT, BlendType);
            }



        }

        private void Update()
        {
            UpdateRealTimePreview();
        }
    }

    public class ParallelProjectionSpase : DecalUtil.IConvertSpace
    {
        public Matrix4x4 ParallelProjectionMatrix;
        public List<Vector3> PPSVarts;
        public DecalUtil.MeshDatas MeshData;
        public ParallelProjectionSpase(Matrix4x4 ParallelProjectionMatrix)
        {
            this.ParallelProjectionMatrix = ParallelProjectionMatrix;

        }
        public void Input(DecalUtil.MeshDatas MeshData)
        {
            this.MeshData = MeshData;
            PPSVarts = DecalUtil.ConvartVerticesInMatlix(ParallelProjectionMatrix, MeshData.Varticals, new Vector3(0.5f, 0.5f, 0));
        }

        public List<Vector2> OutPutUV()
        {
            var UV = new List<Vector2>(PPSVarts.Capacity);
            foreach (var Vart in PPSVarts)
            {
                UV.Add(Vart);
            }
            return UV;
        }

    }

    public class ParallelProjectionFilter : DecalUtil.ITrianglesFilter<ParallelProjectionSpase>
    {
        public List<TrainagelFilterUtility.ITriangleFiltaring<List<Vector3>>> Filters;

        public ParallelProjectionFilter(List<TrainagelFilterUtility.ITriangleFiltaring<List<Vector3>>> Filters)
        {
            this.Filters = Filters;
        }

        public virtual List<TriangleIndex> Filtering(ParallelProjectionSpase Spase, List<TriangleIndex> Traiangeles)
        {
            return TrainagelFilterUtility.FiltaringTriangle(Traiangeles, Spase.PPSVarts, Filters);
        }
    }

    public class IslandCullingPPFilter : ParallelProjectionFilter
    {
        public List<IslandSelector> IslandSelectors;

        public IslandCullingPPFilter(List<TrainagelFilterUtility.ITriangleFiltaring<List<Vector3>>> Filters, List<IslandSelector> IslandSelectors) : base(Filters)
        {
            this.IslandSelectors = IslandSelectors;
        }

        public override List<TriangleIndex> Filtering(ParallelProjectionSpase Spase, List<TriangleIndex> Traiangeles)
        {
            Traiangeles = Island.IslandCulling.Culling(IslandSelectors, Spase.MeshData.Varticals, Spase.MeshData.UV, Traiangeles);
            return base.Filtering(Spase, Traiangeles);
        }

    }
}



#endif
