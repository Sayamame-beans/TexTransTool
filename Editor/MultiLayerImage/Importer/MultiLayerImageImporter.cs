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
using UnityEditor.AssetImporters;
using net.rs64.TexTransCore.TransTextureCore.Utils;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace net.rs64.TexTransTool.MultiLayerImage.Importer
{
    internal class MultiLayerImageImporter
    {
        Dictionary<TTTImportedPng, LowMap<Color32>> EncodeTask = new();
        AssetImportContext ctx;

        internal MultiLayerImageImporter(AssetImportContext assetImportContext)
        {
            ctx = assetImportContext;
        }

        internal void AddLayers(Transform thisTransForm, List<AbstractLayerData> abstractLayers)
        {
            var parent = thisTransForm;
            var count = 0;
            foreach (var layer in abstractLayers.Reverse<AbstractLayerData>())
            {
                var newLayer = new GameObject(layer.LayerName);
                count += 1;
                newLayer.transform.SetParent(parent);

                switch (layer)
                {
                    case RasterLayerData rasterLayer:
                        {
                            CreateRasterLayer(newLayer, rasterLayer);
                            break;
                        }
                    case LayerFolderData layerFolder:
                        {
                            CreateLayerFolder(newLayer, layerFolder);
                            break;
                        }
                }
            }

        }


        private void CreateRasterLayer(GameObject newLayer, RasterLayerData rasterLayer)
        {
            if (!rasterLayer.RasterTexture.Array.IsCreated) { Debug.Log(rasterLayer.LayerName + " is Not RasterLayer"); UnityEngine.Object.DestroyImmediate(newLayer); return; }//ラスターレイヤーじゃないものはインポートできない。
            var rasterLayerComponent = newLayer.AddComponent<RasterImportedLayer>();
            rasterLayerComponent.BlendTypeKey = rasterLayer.BlendTypeKey;
            rasterLayerComponent.Opacity = rasterLayer.Opacity;
            rasterLayerComponent.Clipping = rasterLayer.Clipping;
            rasterLayerComponent.Visible = rasterLayer.Visible;

            var importedPng = rasterLayerComponent.ImportedPNG = ScriptableObject.CreateInstance<TTTImportedPng>();
            EncodeTask.Add(importedPng, rasterLayer.RasterTexture);
            importedPng.name = rasterLayer.LayerName + "_Tex";
            ctx.AddObjectToAsset(importedPng.name, importedPng);

            SetMaskTexture(rasterLayer, rasterLayerComponent);

        }

        private void SetMaskTexture(AbstractLayerData rasterLayer, AbstractLayer rasterLayerComponent)
        {
            if (rasterLayer.LayerMask != null)
            {
                var maskPNG = ScriptableObject.CreateInstance<TTTImportedPng>();
                maskPNG.name = rasterLayer.LayerName + "_MaskTex";
                EncodeTask.Add(maskPNG, rasterLayer.LayerMask.MaskTexture);
                ctx.AddObjectToAsset(maskPNG.name, maskPNG);
                rasterLayerComponent.LayerMask = new TTTImportedPngLayerMask(rasterLayer.LayerMask.LayerMaskDisabled, maskPNG);
            }
            else
            {
                rasterLayerComponent.LayerMask = new LayerMask();
            }
        }

        private void CreateLayerFolder(GameObject newLayer, LayerFolderData layerFolder)
        {
            var layerFolderComponent = newLayer.AddComponent<LayerFolder>();
            layerFolderComponent.PassThrough = layerFolder.PassThrough;
            layerFolderComponent.BlendTypeKey = layerFolder.BlendTypeKey;
            layerFolderComponent.Opacity = layerFolder.Opacity;
            layerFolderComponent.Clipping = layerFolder.Clipping;
            layerFolderComponent.Visible = layerFolder.Visible;

            SetMaskTexture(layerFolder, layerFolderComponent);

            AddLayers(newLayer.transform, layerFolder.Layers);
        }

        internal void EncodeExecution()
        {
            PNGEncoderExecuter(EncodeTask);

            foreach (var task in EncodeTask)
            {
                PNGByte2Preview(task.Value, task.Key);
            }
        }
        public static void PNGEncoderExecuter(Dictionary<TTTImportedPng, LowMap<Color32>> encData, int? forceParallelSize = null)
        {
            ParallelExecuter<KeyValuePair<TTTImportedPng, LowMap<Color32>>>(PNGEncoder, encData, forceParallelSize, ProgressDisplay);

            static void ProgressDisplay(float progress)
            {
                EditorUtility.DisplayProgressBar("Import Canvas", "EncodePNG", progress);
            }

        }

        private static void ParallelExecuter<T>(Action<T> taskExecute, IEnumerable<T> taskData, int? forceParallelSize, Action<float> progressCallBack = null)
        {
            var parallelSize = forceParallelSize.HasValue ? forceParallelSize.Value : Environment.ProcessorCount;
            var taskQueue = new Queue<T>(taskData);
            var taskParallel = new Task[parallelSize];
            var encDataCount = taskQueue.Count; var nowIndex = 0;
            while (taskQueue.Count > 0)
            {
                for (int i = 0; taskParallel.Length > i; i += 1)
                {
                    if (taskQueue.Count > 0)
                    {
                        var task = taskQueue.Dequeue();
                        taskParallel[i] = Task.Run(() => taskExecute.Invoke(task));
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
                    progressCallBack?.Invoke(nowIndex / (float)encDataCount);
                }
            }
        }

        public static async Task<bool> TaskAwaiter(Task task)
        {
            await task.ConfigureAwait(false);
            return true;
        }


        public static void PNGEncoder(KeyValuePair<TTTImportedPng, LowMap<Color32>> keyValuePair)
        {
            PNGEncoder(keyValuePair.Value, keyValuePair.Key);
        }
        public static void PNGEncoder(LowMap<Color32> image, TTTImportedPng sObj)
        {
            try
            {
                using (var bitMap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
                {
                    var bmd = bitMap.LockBits(new(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    var length = image.Array.Length * 4;
                    var argbValue = new NativeArray<byte>(length, Allocator.Persistent);

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

                    TexTransTool.Unsafe.UnsafeBitMapDataUtility.WriteBitMapData(argbValue, bmd);

                    argbValue.Dispose();
                    bitMap.UnlockBits(bmd);

                    using (var memStream = new MemoryStream())
                    {
                        bitMap.Save(memStream, ImageFormat.Png);
                        sObj.PngBytes = memStream.ToArray();
                    }
                }
            }
            catch (Exception e)
            {
                var code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Debug.LogError($"GetLastWin32Error:{code}");
                Debug.Log($"{image.Array.Length}-ArrayLength {image.Width}-{image.Height}-Size {image.Array.IsCreated}-IsCrated");
                throw e;
            }
        }
        private void PNGByte2Preview(LowMap<Color32> image, TTTImportedPng sObj)
        {

            using (var rawData = HeightInvert(image))
            {

                var setting = new TextureGenerationSettings(TextureImporterType.Default);
                setting.textureImporterSettings.alphaIsTransparency = true;
                setting.textureImporterSettings.mipmapEnabled = false;
                setting.textureImporterSettings.filterMode = FilterMode.Bilinear;

                setting.platformSettings.maxTextureSize = 1024;
                setting.platformSettings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
                setting.platformSettings.textureCompression = TextureImporterCompression.Compressed;
                setting.platformSettings.compressionQuality = 100;

                setting.sourceTextureInformation.width = image.Width;
                setting.sourceTextureInformation.height = image.Height;
                setting.sourceTextureInformation.containsAlpha = true;
                setting.sourceTextureInformation.hdr = false;

                var output = TextureGenerator.GenerateTexture(setting, rawData);

                sObj.PreviewTexture = output.texture;
                sObj.PreviewTexture.name = sObj.name + "_Preview";
                ctx.AddObjectToAsset(output.texture.name, output.texture);

            }

        }

        static NativeArray<Color32> HeightInvert(LowMap<Color32> lowMap)
        {
            var width = lowMap.Width;
            var map = new NativeArray<Color32>(lowMap.Array.Length, Allocator.Persistent);

            for (var y = 0; lowMap.Height > y; y += 1)
            {
                var from = lowMap.Array.Slice((lowMap.Height - 1 - y) * width, width);
                var to = map.Slice(y * width, width);
                to.CopyFrom(from);
            }
            return map;
        }




    }
}
