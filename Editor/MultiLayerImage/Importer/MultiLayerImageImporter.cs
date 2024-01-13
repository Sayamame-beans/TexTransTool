using System.Collections.Generic;
using net.rs64.MultiLayerImageParser.LayerData;
using UnityEngine;
using System.Linq;
using System;
using System.IO;
using UnityEditor;
using net.rs64.TexTransCore.TransTextureCore;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Buffers;

namespace net.rs64.TexTransTool.MultiLayerImage.Importer
{
    internal static class MultiLayerImageImporter
    {
        public static void ImportCanvasData(HandlerForFolderSaver ctx, CanvasData canvasData, Action<MultiLayerImageCanvas> preSaveCallBack)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Import Canvas", "Build Layer", 0);

                var prefabName = Path.GetFileName(ctx.SaveDirectory) + "-Canvas";
                var rootCanvas = new GameObject(prefabName);
                var multiLayerImageCanvas = rootCanvas.AddComponent<MultiLayerImageCanvas>();
                multiLayerImageCanvas.TextureSize = canvasData.Size;
                AddLayers(multiLayerImageCanvas.transform, ctx, canvasData.RootLayers);
                ctx.FinalizeTex2D();
                preSaveCallBack.Invoke(multiLayerImageCanvas);
                PrefabUtility.SaveAsPrefabAsset(rootCanvas, Path.Combine(ctx.SaveDirectory, prefabName + ".prefab"));
                UnityEngine.Object.DestroyImmediate(rootCanvas);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        public static void AddLayers(Transform thisTransForm, HandlerForFolderSaver ctx, List<AbstractLayerData> abstractLayers)
        {
            var parent = thisTransForm;
            var count = 0;
            foreach (var layer in abstractLayers.Reverse<AbstractLayerData>())
            {
                var newLayer = new GameObject(layer.LayerName);
                // var NewLayer = new GameObject(count + "-" + layer.LayerName);
                count += 1;
                newLayer.transform.SetParent(parent);

                switch (layer)
                {
                    case RasterLayerData rasterLayer:
                        {
                            if (rasterLayer.RasterTexture.Array == null) { UnityEngine.Object.DestroyImmediate(newLayer); continue; }
                            var rasterLayerComponent = newLayer.AddComponent<RasterLayer>();
                            ctx.AddTextureCallBack(rasterLayer.RasterTexture, layer.LayerName + "_Tex", (Texture2D tex) => rasterLayerComponent.RasterTexture = tex);
                            rasterLayerComponent.BlendTypeKey = layer.BlendTypeKey;
                            rasterLayerComponent.Opacity = layer.Opacity;
                            rasterLayerComponent.Clipping = layer.Clipping;
                            rasterLayerComponent.Visible = layer.Visible;
                            SetMaskTexture(rasterLayerComponent, rasterLayer);
                            break;
                        }
                    case LayerFolderData layerFolder:
                        {

                            var layerFolderComponent = newLayer.AddComponent<LayerFolder>();
                            layerFolderComponent.PassThrough = layerFolder.PassThrough;
                            layerFolderComponent.BlendTypeKey = layer.BlendTypeKey;
                            layerFolderComponent.Opacity = layer.Opacity;
                            layerFolderComponent.Clipping = layer.Clipping;
                            layerFolderComponent.Visible = layer.Visible;
                            SetMaskTexture(layerFolderComponent, layerFolder);
                            AddLayers(newLayer.transform, ctx, layerFolder.Layers);
                            break;
                        }
                }
            }

            void SetMaskTexture(AbstractLayer abstractLayer, AbstractLayerData abstractLayerData)
            {
                if (abstractLayerData.LayerMask != null)
                {
                    var mask = new LayerMask();
                    mask.LayerMaskDisabled = abstractLayerData.LayerMask.LayerMaskDisabled;
                    abstractLayer.LayerMask = mask;
                    ctx.AddTextureCallBack(abstractLayerData.LayerMask.MaskTexture, abstractLayerData.LayerName + "_Mask", (Texture2D tex) => abstractLayer.LayerMask.MaskTexture = tex);
                }
            }
        }

        public class HandlerForFolderSaver
        {
            public string SaveDirectory;

            public const string RasterImageData = "RasterImageData";

            public HandlerForFolderSaver(string v)
            {
                SaveDirectory = v;
            }


            public void AddTextureCallBack(LowMap<Color32> rasterTexture, string name, Action<Texture2D> value)
            {
                TexDict.Add(rasterTexture, (name, value));
            }

            Dictionary<LowMap<Color32>, (string name, Action<Texture2D> setAction)> TexDict = new();

            public void FinalizeTex2D()
            {
                EditorUtility.DisplayProgressBar("Import Canvas", "Save RasterLayer Data", 0);
                var RasterDataPath = Path.Combine(SaveDirectory, RasterImageData);
                if (!Directory.Exists(RasterDataPath)) { Directory.CreateDirectory(RasterDataPath); }

                var pathToSetAction = new Dictionary<string, Action<Texture2D>>();
                var pathToEncode = new Dictionary<string, LowMap<Color32>>();
                var progressesCount = TexDict.Count;
                var imageIndex = 0;
                foreach (var texAndSetAct in TexDict)
                {
                    imageIndex += 1;

                    var texName = texAndSetAct.Value.name;
                    var texMap = texAndSetAct.Key;

                    if (!Directory.Exists(SaveDirectory)) { Directory.CreateDirectory(SaveDirectory); }

                    var path = CreatePath(texName, imageIndex);

                    pathToEncode.Add(path, texMap);
                    pathToSetAction.Add(path, texAndSetAct.Value.setAction);
                }
                TexDict.Clear();
                // var timer = System.Diagnostics.Stopwatch.StartNew();
                EditorUtility.DisplayProgressBar("Import Canvas", "SavePNG", 0);
                PNGEncoderExecuter(pathToEncode);
                // UnityPNGEncoder(PathToEncode);

                // timer.Stop(); Debug.Log("EncAllTime : " + timer.ElapsedMilliseconds + "ms");
                foreach (var path2e in pathToEncode)
                {
                    if (File.Exists(path2e.Key + ".meta")) { continue; }
                    File.WriteAllText(path2e.Key + ".meta", MetaGUIDPre + GUID.Generate().ToString() + MetaGUIDPost);
                }


                EditorUtility.DisplayProgressBar("Import Canvas", "AssetDatabase.Refresh();", 0);
                AssetDatabase.Refresh();
                EditorUtility.DisplayProgressBar("Import Canvas", "Refresh End", 1);

                imageIndex = 0;
                foreach (var loadTex2d in pathToSetAction)
                {
                    EditorUtility.DisplayProgressBar("Import Canvas", "SetUpLayer for PNG-" + Path.GetFileName(loadTex2d.Key), imageIndex / (float)progressesCount);
                    imageIndex += 1;

                    var Tex2D = AssetDatabase.LoadAssetAtPath<Texture2D>(loadTex2d.Key);
                    loadTex2d.Value.Invoke(Tex2D);
                }
                EditorUtility.ClearProgressBar();
            }


            public static void UnityPNGEncoder(Dictionary<string, TwoDimensionalMap<Color32>> encData)
            {
                var encDataCount = encData.Count; var nowIndex = 0;
                foreach (var path2Tex in encData)
                {
                    var texMap = path2Tex.Value;
                    var path = path2Tex.Key;
                    var tex2D = new Texture2D(texMap.MapSize.x, texMap.MapSize.y, TextureFormat.RGBA32, false);
                    tex2D.SetPixelData(texMap.Array, 0);
                    var pngByte = tex2D.EncodeToPNG();
                    File.WriteAllBytes(path, pngByte);
                    UnityEngine.Object.DestroyImmediate(tex2D);

                    nowIndex += 1;
                    EditorUtility.DisplayProgressBar("Import Canvas", "SavePNG", nowIndex / (float)encDataCount);
                }
            }
            public static void PNGEncoderExecuter(Dictionary<string, LowMap<Color32>> encData, int? forceParallelSize = null)
            {
                var parallelSize = forceParallelSize.HasValue ? forceParallelSize.Value : Environment.ProcessorCount;
                var taskQueue = new Queue<KeyValuePair<string, LowMap<Color32>>>(encData);
                var taskParallel = new Task[parallelSize];
                var encDataCount = taskQueue.Count; var nowIndex = 0;
                while (taskQueue.Count > 0)
                {
                    for (int i = 0; taskParallel.Length > i; i += 1)
                    {
                        if (taskQueue.Count > 0)
                        {
                            var task = taskQueue.Dequeue();
                            taskParallel[i] = Task.Run(() => PNGEncoder(task.Key, task.Value));
                        }
                        else
                        {
                            taskParallel[i] = null;
                            break;
                        }
                    }

                    foreach (var task in taskParallel)
                    {
                        if (task == null) { break; }
                        _ = TaskAwaiter(task).Result;
                        nowIndex += 1;
                        EditorUtility.DisplayProgressBar("Import Canvas", "SavePNG", nowIndex / (float)encDataCount);
                    }
                }
            }


            public static async Task<bool> TaskAwaiter(Task task)
            {
                await task.ConfigureAwait(false);
                return true;
            }

            public static void PNGEncoder(string path, LowMap<Color32> image)
            {
                try
                {
                    // var timer = System.Diagnostics.Stopwatch.StartNew();
                    using (var bitMap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
                    {
                        var bmd = bitMap.LockBits(new(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        var length = image.Array.Length * 4;
                        var argbValue = ArrayPool<byte>.Shared.Rent(length);
                        // var ctime = timer.ElapsedMilliseconds; timer.Restart();
                        var widthByteLen = image.Width * 4;
                        for (var y = 0; image.Height > y; y += 1)
                        {
                            var withByteOffset = widthByteLen * y;
                            var withOffset = image.Width * y;
                            for (var x = 0; image.Width > x; x += 1)
                            {
                                var colI = withByteOffset + (x * 4);
                                var col = image.Array[withOffset + x];
                                argbValue[colI + 0] = col.b;
                                argbValue[colI + 1] = col.g;
                                argbValue[colI + 2] = col.r;
                                argbValue[colI + 3] = col.a;
                            }
                        }

                        System.Runtime.InteropServices.Marshal.Copy(argbValue, 0, bmd.Scan0, length);
                        ArrayPool<byte>.Shared.Return(argbValue);
                        bitMap.UnlockBits(bmd);
                        // var wtime = timer.ElapsedMilliseconds; timer.Restart();
                        bitMap.Save(path);
                        // timer.Stop(); Debug.Log($"c:{ctime} w:{wtime}ms s:{timer.ElapsedMilliseconds}ms all:{ctime + wtime + timer.ElapsedMilliseconds}");
                    }
                }
                catch (Exception e)
                {
                    var code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    Debug.LogError($"GetLastWin32Error:{code}");
                    throw e;
                }
            }


            private string CreatePath(string texName, int count)
            {
                return Path.Combine(SaveDirectory, RasterImageData, texName + "-" + count) + ".png";
            }

            public const string MetaGUIDPre =
@"fileFormatVersion: 2
guid: ";
            public const string MetaGUIDPost =
@"
  TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 11
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  applyGammaDecoding: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 1024
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 3
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
  spritePackingTag: 
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData: 
  assetBundleName: 
  assetBundleVariant:
";
        }

    }
}
