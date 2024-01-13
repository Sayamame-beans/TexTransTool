using System.Collections.Generic;
using System.Linq;
using net.rs64.TexTransCore.BlendTexture;
using net.rs64.TexTransCore.TransTextureCore.Utils;
using net.rs64.TexTransTool.Decal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static net.rs64.TexTransCore.BlendTexture.TextureBlend;

namespace net.rs64.TexTransTool
{
    internal class RealTimePreviewManager : ScriptableSingleton<RealTimePreviewManager>
    {
        public Dictionary<AbstractDecal, (string PropertyName, List<BlendTextureClass> blendTextureList, Dictionary<Material, Dictionary<string, RenderTexture>> decalTargets)> RealTimePreviews = new Dictionary<AbstractDecal, (string PropertyName, List<BlendTextureClass> blendTextureList, Dictionary<Material, Dictionary<string, RenderTexture>> decalTargets)>();
        private Dictionary<Material, Dictionary<string, ((Texture2D SouseTexture, RenderTexture TargetTexture), List<BlendTextureClass> Decals)>> Previews = new Dictionary<Material, Dictionary<string, ((Texture2D SouseTexture, RenderTexture TargetTexture), List<BlendTextureClass> Decals)>>();
        private Dictionary<Material, Material> PreviewMatDict = new Dictionary<Material, Material>();
        private HashSet<Renderer> PreviewTargetRenderer = new HashSet<Renderer>();
        protected RealTimePreviewManager()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ExitPreview;
            AssemblyReloadEvents.beforeAssemblyReload += ExitPreview;
            EditorSceneManager.sceneClosing -= ExitPreview;
            EditorSceneManager.sceneClosing += ExitPreview;
        }

        public static bool IsContainsRealTimePreview => RealTimePreviewManager.instance.RealTimePreviews.Count > 0;

        private void RegtRenderer(Renderer renderer)
        {
            if (PreviewTargetRenderer.Contains(renderer) || renderer == null) { return; }
            PreviewTargetRenderer.Add(renderer);
            foreach (var MatPair in PreviewMatDict)
            {
                SwapMaterial(renderer, MatPair.Key, MatPair.Value);
            }
        }

        private void SwapMaterial(Renderer renderer, Material souse, Material target)
        {
            using (var serialized = new SerializedObject(renderer))
            {
                foreach (SerializedProperty property in serialized.FindProperty("m_Materials"))
                {
                    if (property.objectReferenceValue is Material material && material == souse)
                    {
                        AnimationMode.AddPropertyModification(
                            EditorCurveBinding.PPtrCurve("", renderer.GetType(), property.propertyPath),
                            new PropertyModification
                            {
                                target = renderer,
                                propertyPath = property.propertyPath,
                                objectReference = souse,
                            },
                            true);
                        property.objectReferenceValue = target;
                    }
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        private void SwapMaterialAll(Material material, Material editableMat)
        {
            foreach (var renderer in PreviewTargetRenderer)
            {
                SwapMaterial(renderer, material, editableMat);
            }
        }

        private void RegtPreviewRenderTexture(Material material, string propertyName, BlendTextureClass blendTexture)
        {
            if (PreviewMatDict.ContainsKey(material)) { material = PreviewMatDict[material]; }

            if (Previews.ContainsKey(material))
            {
                if (Previews[material].ContainsKey(propertyName))
                {
                    Previews[material][propertyName].Decals.Add(blendTexture);
                }
                else
                {
                    var newTarget = new RenderTexture(blendTexture.RenderTexture.descriptor);
                    var souseTexture = material.GetTexture(propertyName) as Texture2D;
                    material.SetTexture(propertyName, newTarget);
                    Previews[material].Add(propertyName, ((souseTexture, newTarget), new List<BlendTextureClass>() { blendTexture }));
                }
            }
            else
            {
                var editableMat = Instantiate(material);
                SwapMaterialAll(material, editableMat);
                PreviewMatDict.Add(material, editableMat);
                var souseTexture = material.GetTexture(propertyName) as Texture2D;
                var newTarget = new RenderTexture(blendTexture.RenderTexture.descriptor);
                editableMat.SetTexture(propertyName, newTarget);
                Graphics.Blit(souseTexture, newTarget);
                Previews.Add(editableMat, new Dictionary<string, ((Texture2D SouseTexture, RenderTexture TargetTexture), List<BlendTextureClass> Decals)>() { { propertyName, ((souseTexture, newTarget), new List<BlendTextureClass>() { blendTexture }) } });
            }
        }


        private void UpdatePreviewTexture(Material material, string propertyName)
        {
            var TargetMat = material;
            if (!Previews.ContainsKey(TargetMat)) { return; }
            if (!Previews[TargetMat].ContainsKey(propertyName)) { return; }

            var target = Previews[TargetMat][propertyName];
            var targetRt = target.Item1.TargetTexture;
            targetRt.Release();
            var souseTex = target.Item1.SouseTexture;
            Graphics.Blit(souseTex, targetRt);

            targetRt.BlendBlit(target.Decals.Where(I => I.RenderTexture != null).Select<BlendTextureClass, BlendTexturePair>(I => I));
        }
        public bool ContainsPreview => PreviewTargetRenderer.Count != 0;
        public void ExitPreview()
        {
            if (ContainsPreview)
            {
                AnimationMode.StopAnimationMode();
                RealTimePreviews.Clear();
                Previews.Clear();
                PreviewMatDict.Clear();
                PreviewTargetRenderer.Clear();
                Previews.Clear();
            }
        }
        private void ExitPreview(UnityEngine.SceneManagement.Scene scene, bool removingScene)
        {
            ExitPreview();
        }

        public void RegtAbstractDecal(AbstractDecal abstractDecal)
        {
            if (RealTimePreviews.Count == 0) { AnimationMode.StartAnimationMode(); }
            if (RealTimePreviews.ContainsKey(abstractDecal)) { return; }
            var decalTargets = new Dictionary<Material, Dictionary<string, RenderTexture>>();
            var blends = new List<BlendTextureClass>();
            var TargetMats = RendererUtility.GetFilteredMaterials(abstractDecal.TargetRenderers);
            foreach (var mat in TargetMats)
            {
                if (mat.HasProperty(abstractDecal.TargetPropertyName) && mat.GetTexture(abstractDecal.TargetPropertyName) != null)
                {
                    var tex = mat.GetTexture(abstractDecal.TargetPropertyName);
                    RenderTexture Rt = null;
                    switch (tex)
                    {
                        case Texture2D texture2D:
                            {
                                Rt = new RenderTexture(texture2D.width, texture2D.height, 0);
                                break;
                            }
                        case RenderTexture renderTexture:
                            {
                                Rt = new RenderTexture(renderTexture.descriptor);
                                break;
                            }
                        default:
                            { continue; }
                    }

                    var blendTex = new BlendTextureClass(Rt, abstractDecal.BlendTypeKey);
                    blends.Add(blendTex);

                    RegtPreviewRenderTexture(mat, abstractDecal.TargetPropertyName, blendTex);
                    Material editableMat = PreviewMatDict.ContainsKey(mat) ? PreviewMatDict[mat] : mat;

                    decalTargets.Add(editableMat, new Dictionary<string, RenderTexture>());
                    decalTargets[editableMat].Add(abstractDecal.TargetPropertyName, Rt);
                }
            }
            foreach (var render in abstractDecal.GetRenderers) { RegtRenderer(render); }
            RealTimePreviews.Add(abstractDecal, (abstractDecal.TargetPropertyName, blends, decalTargets));
        }

        public bool IsRealTimePreview(AbstractDecal abstractDecal) => RealTimePreviews.ContainsKey(abstractDecal);
        public void UnRegtAbstractDecal(AbstractDecal abstractDecal)
        {
            if (!IsRealTimePreview(abstractDecal)) { return; }
            var absDecalData = RealTimePreviews[abstractDecal];

            foreach (var decalTarget in absDecalData.decalTargets)
            {
                var mat = decalTarget.Key;
                foreach (var target in decalTarget.Value)
                {
                    if (!Previews.ContainsKey(mat) || !Previews[mat].ContainsKey(target.Key)) { continue; }
                    Previews[mat][target.Key].Decals.Remove(Previews[mat][target.Key].Decals.Find(I => I.RenderTexture == target.Value));
                    if (Previews[mat][target.Key].Decals.Count == 0)
                    {
                        mat.SetTexture(target.Key, Previews[mat][target.Key].Item1.SouseTexture);
                        Previews[mat].Remove(target.Key);
                    }
                    else
                    {
                        UpdatePreviewTexture(mat, target.Key);
                    }
                }
            }

            RealTimePreviews.Remove(abstractDecal);
            if (RealTimePreviews.Count == 0) { ExitPreview(); }
        }

        public void UpdateAbstractDecal(AbstractDecal abstractDecal)
        {
            if (!RealTimePreviews.ContainsKey(abstractDecal)) { return; }
            var absDecalData = RealTimePreviews[abstractDecal];

            if (absDecalData.PropertyName != abstractDecal.TargetPropertyName)
            {
                UnRegtAbstractDecal(abstractDecal);
                RegtAbstractDecal(abstractDecal);
            }

            foreach (var blendData in absDecalData.blendTextureList)
            {
                blendData.BlendTypeKey = abstractDecal.BlendTypeKey;
            }

            foreach (var decalTarget in absDecalData.decalTargets)
            {
                foreach (var rt in decalTarget.Value)
                {
                    rt.Value.Release();
                }
            }

            abstractDecal.CompileDecal(new TextureManager(true), absDecalData.decalTargets);

            foreach (var mat in absDecalData.decalTargets.Keys)
            {
                UpdatePreviewTexture(mat, absDecalData.PropertyName);
            }
        }




        public class BlendTextureClass
        {
            public RenderTexture RenderTexture;
            public string BlendTypeKey;

            public BlendTextureClass(RenderTexture renderTexture, string blendTypeKey)
            {
                RenderTexture = renderTexture;
                BlendTypeKey = blendTypeKey;
            }

            public static implicit operator BlendTexturePair(BlendTextureClass bl) => new BlendTexturePair(bl.RenderTexture, bl.BlendTypeKey);
        }
    }
}